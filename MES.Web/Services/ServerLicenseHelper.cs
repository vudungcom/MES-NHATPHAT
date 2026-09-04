#pragma warning disable CA1416
#nullable disable
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using System.Text.Json;

namespace MES.Web.Services
{
    public static class ServerLicenseHelper
    {
        private static readonly object _ioLock = new object();

        private static readonly string _eApiUrl = "OhEEHBJZSmkaDxdIIhFeCw4MAioMQgZOP0odDQIRCjVGH0pgGQMJDwMaCj4RKRxnYQQUVVQVH3BcPT14KDwvJjMMSCQCODxVAzwBNCUlFR4RXEhANScYQSAEMnA8PTVoNRAZWwAHIBMfDjwONx0VDw==";
        private static readonly string _eApiKey = "AAAAAAAAAAAAAAATYldF";

        private static readonly byte[] _k1 = { 0x52, 0x65, 0x70, 0x6C };
        private static readonly byte[] _k2 = { 0x61, 0x63, 0x65, 0x46 };
        private static readonly byte[] _k3 = { 0x69, 0x6C, 0x65, 0x21 };

        private static string DecryptXor(string encrypted, byte[] key)
        {
            try
            {
                byte[] data = Convert.FromBase64String(encrypted);
                byte[] decoded = new byte[data.Length];
                for (int i = 0; i < data.Length; i++)
                    decoded[i] = (byte)(data[i] ^ key[i % key.Length]);
                return Encoding.UTF8.GetString(decoded);
            }
            catch { return ""; }
        }

        private static string GetApiUrl() => DecryptXor(_eApiUrl, _k1.Concat(_k2).Concat(_k3).ToArray());
        private static string GetApiKey() => DecryptXor(_eApiKey, _k1.Concat(_k2).Concat(_k3).ToArray());

        private const int TRIAL_MINUTES = 21600;       // 15 ngày
        private const int OFFLINE_GRACE_DAYS = 7;

        private static string GetPersistentFolder()
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string targetFolder = Path.Combine(programData, "MES_Server", "License");

            try
            {
                if (!Directory.Exists(targetFolder))
                    Directory.CreateDirectory(targetFolder);
            }
            catch { }

            return targetFolder;
        }

        private static readonly string _appFolder = GetPersistentFolder();
        private static readonly string CachePath = Path.Combine(_appFolder, "mes_cfg.dat");
        private static readonly string TrialPath = Path.Combine(_appFolder, "mes_init.dat");
        private static readonly string LogPath = Path.Combine(_appFolder, "mes_log.txt");

        private static byte[] GetEncryptionKey()
        {
            string hwid = GetHardwareId();
            string salt = "MES#2024$Server!";
            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes(hwid + salt));
        }

        private static string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return "";

            try
            {
                using var aes = Aes.Create();
                aes.Key = GetEncryptionKey();
                aes.GenerateIV();

                using var ms = new MemoryStream();
                ms.Write(aes.IV, 0, aes.IV.Length);

                using var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write);
                using var sw = new StreamWriter(cs);
                sw.Write(plainText);
                sw.Flush();
                cs.FlushFinalBlock();

                return Convert.ToBase64String(ms.ToArray());
            }
            catch { return ""; }
        }

        private static string DecryptString(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return "";

            try
            {
                byte[] fullCipher = Convert.FromBase64String(cipherText);
                if (fullCipher.Length <= 16) return "";

                using var aes = Aes.Create();
                aes.Key = GetEncryptionKey();

                byte[] iv = new byte[16];
                Array.Copy(fullCipher, 0, iv, 0, iv.Length);
                aes.IV = iv;

                using var ms = new MemoryStream(fullCipher, iv.Length, fullCipher.Length - iv.Length);
                using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
                using var sr = new StreamReader(cs);
                return sr.ReadToEnd();
            }
            catch { return ""; }
        }

        private static string ComputeChecksum(string data)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(
                Encoding.UTF8.GetBytes(data + GetHardwareId() + "MES_CHK"));
            return Convert.ToBase64String(hash).Substring(0, 16);
        }

        private const string REGISTRY_PATH = @"Software\MES_Server\License";
        private const string REGISTRY_VALUE = "InitDate";
        private static DateTime? _cachedTrialStartDate = null;

        // TrialStartDate là dữ liệu persistent. Không bao giờ tự tạo lại nếu
        // một trong các nguồn cũ tồn tại nhưng không đọc được.
        private static DateTime GetTrialStartDate()
        {
            if (_cachedTrialStartDate.HasValue)
                return _cachedTrialStartDate.Value;

            DateTime fileDate = DateTime.MinValue;
            DateTime regDate = DateTime.MinValue;
            bool fileExists = false;
            bool regExists = false;

            try
            {
                lock (_ioLock)
                {
                    fileExists = File.Exists(TrialPath);
                    if (fileExists)
                    {
                        string decrypted = DecryptString(File.ReadAllText(TrialPath));
                        if (DateTime.TryParse(decrypted, out DateTime fd) && fd <= DateTime.Now)
                            fileDate = fd;
                    }
                }
            }
            catch { }

            try
            {
                using var key =
                    Registry.LocalMachine.OpenSubKey(REGISTRY_PATH) ??
                    Registry.CurrentUser.OpenSubKey(REGISTRY_PATH);

                if (key != null)
                {
                    regExists = key.GetValue(REGISTRY_VALUE) != null;

                    if (regExists)
                    {
                        string decrypted = DecryptString(key.GetValue(REGISTRY_VALUE) as string);
                        if (DateTime.TryParse(decrypted, out DateTime rd) && rd <= DateTime.Now)
                            regDate = rd;
                    }
                }
            }
            catch { }

            DateTime oldest = DateTime.MinValue;

            if (fileDate != DateTime.MinValue && regDate != DateTime.MinValue)
                oldest = fileDate < regDate ? fileDate : regDate;
            else if (fileDate != DateTime.MinValue)
                oldest = fileDate;
            else if (regDate != DateTime.MinValue)
                oldest = regDate;

            // Nếu dữ liệu đã tồn tại nhưng không decrypt/đọc được:
            // tuyệt đối KHÔNG coi đây là máy mới và reset trial.
            if (oldest == DateTime.MinValue && (fileExists || regExists))
                return DateTime.MaxValue;

            if (oldest != DateTime.MinValue)
            {
                _cachedTrialStartDate = oldest;

                // Đồng bộ lại cả hai nơi.
                SaveTrialToFile(oldest);
                SaveTrialToRegistry(oldest);
            }

            return oldest;
        }

        private static void SaveTrialToFile(DateTime startDate)
        {
            try
            {
                lock (_ioLock)
                {
                    if (!Directory.Exists(_appFolder))
                        Directory.CreateDirectory(_appFolder);

                    if (File.Exists(TrialPath))
                        File.SetAttributes(TrialPath, FileAttributes.Normal);

                    string encrypted = EncryptString(startDate.ToString("O"));
                    if (!string.IsNullOrEmpty(encrypted))
                        File.WriteAllText(TrialPath, encrypted);
                }
            }
            catch (Exception ex)
            {
                WriteLog("SaveTrialToFile lỗi", ex);
            }
        }

        private static void SaveTrialToRegistry(DateTime startDate)
        {
            try
            {
                using var key =
                    Registry.LocalMachine.CreateSubKey(REGISTRY_PATH) ??
                    Registry.CurrentUser.CreateSubKey(REGISTRY_PATH);

                key?.SetValue(REGISTRY_VALUE, EncryptString(startDate.ToString("O")));
            }
            catch (Exception ex)
            {
                WriteLog("SaveTrialToRegistry lỗi", ex);
            }
        }

        private static (bool isValid, int minutesLeft, DateTime startDate) GetTrialInfo()
        {
            DateTime startDate = GetTrialStartDate();

            // Chỉ khởi tạo trial khi THỰC SỰ chưa có dữ liệu trial nào.
            if (startDate == DateTime.MinValue)
            {
                startDate = DateTime.Now;

                SaveTrialToFile(startDate);
                SaveTrialToRegistry(startDate);

                _cachedTrialStartDate = startDate;
            }

            // Có dữ liệu nhưng không thể đọc -> không reset.
            if (startDate == DateTime.MaxValue)
                return (false, 0, DateTime.MinValue);

            DateTime expiration = startDate.AddMinutes(TRIAL_MINUTES);
            int minutesLeft = (int)(expiration - DateTime.Now).TotalMinutes;

            return (minutesLeft > 0, Math.Max(0, minutesLeft), startDate);
        }

        public static string FormatTimeLeft(int minutesLeft, bool vietnamese)
        {
            if (minutesLeft >= 1440)
                return vietnamese ? $"{minutesLeft / 1440} ngày" : $"{minutesLeft / 1440} days";

            if (minutesLeft >= 60)
                return vietnamese ? $"{minutesLeft / 60} giờ" : $"{minutesLeft / 60} hours";

            return vietnamese ? $"{minutesLeft} phút" : $"{minutesLeft} minutes";
        }

        public static void WriteLog(string message, Exception ex = null)
        {
            try
            {
                lock (_ioLock)
                {
                    if (!Directory.Exists(_appFolder))
                        Directory.CreateDirectory(_appFolder);

                    string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n";

                    if (ex != null)
                        logEntry += $"Exception: {ex.Message}\nStackTrace: {ex.StackTrace}\n";

                    File.AppendAllText(LogPath, logEntry);

                    var fi = new FileInfo(LogPath);
                    if (fi.Exists && fi.Length > 1024 * 1024)
                    {
                        File.WriteAllLines(
                            LogPath,
                            File.ReadAllLines(LogPath).Skip(100).ToArray());
                    }
                }
            }
            catch { }
        }

        public enum LicenseStatus
        {
            Active,
            Inactive,
            Error
        }

        public class LicenseInfo
        {
            public LicenseStatus Status { get; set; } = LicenseStatus.Inactive;
            public string Message { get; set; } = "";
            public string ExpirationDate { get; set; } = "";
            public DateTime LastCheckDate { get; set; } = DateTime.MinValue;
            public string LicensedTo { get; set; } = "";

            public bool IsTrial { get; set; } = false;
            public DateTime TrialStartDate { get; set; } = DateTime.MinValue;
            public int TrialMinutesLeft { get; set; } = 0;
            public string TrialTimeLeftDisplay { get; set; } = "";

            // Đây là thời điểm CHECK ONLINE THÀNH CÔNG gần nhất.
            public DateTime LastOnlineVerify { get; set; } = DateTime.MinValue;

            public string _hwid { get; set; } = "";
            public string _chk { get; set; } = "";
        }

        private static string _cachedHwId = null;

        public static string GetHardwareId()
        {
            if (_cachedHwId != null)
                return _cachedHwId;

            var sb = new StringBuilder();

            try
            {
                sb.Append(Environment.MachineName);
                sb.Append(Environment.ProcessorCount);

                using var key =
                    Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");

                string guid = key?.GetValue("MachineGuid")?.ToString();

                if (!string.IsNullOrEmpty(guid))
                    sb.Append(guid);
                else
                    sb.Append(Environment.OSVersion.VersionString);
            }
            catch { }

            if (sb.Length == 0)
                return _cachedHwId = "UNKNOWN-" + Environment.MachineName;

            using var sha = SHA256.Create();

            string hex = BitConverter.ToString(
                sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())))
                .Replace("-", "");

            return _cachedHwId =
                $"{hex.Substring(0, 4)}-{hex.Substring(4, 4)}-" +
                $"{hex.Substring(8, 4)}-{hex.Substring(12, 4)}";
        }

        // ============================================================
        // UI CACHE:
        // CHỈ ĐỌC LOCAL. KHÔNG BAO GIỜ GỌI INTERNET.
        // ============================================================
        public static LicenseInfo GetCachedLicenseState()
        {
            LicenseInfo info = null;
            bool cacheExists = false;
            bool cacheReadFailed = false;

            try
            {
                string encryptedData = null;

                lock (_ioLock)
                {
                    cacheExists = File.Exists(CachePath);

                    if (cacheExists)
                        encryptedData = File.ReadAllText(CachePath);
                }

                if (!string.IsNullOrEmpty(encryptedData))
                {
                    string json = DecryptString(encryptedData);

                    if (!string.IsNullOrEmpty(json))
                    {
                        info = JsonSerializer.Deserialize<LicenseInfo>(json);

                        if (info == null)
                            cacheReadFailed = true;
                        else if (!VerifyLicenseIntegrity(info))
                            cacheReadFailed = true;
                    }
                    else
                    {
                        cacheReadFailed = true;
                    }
                }
            }
            catch
            {
                cacheReadFailed = true;
            }

            // Nếu cache tồn tại nhưng không đọc/verify được:
            // KHÔNG được giả vờ rằng đây là máy chưa kích hoạt.
            // Trả Error để UI không hiển thị "chưa có bản quyền".
            if (cacheReadFailed || (cacheExists && info == null))
            {
                return new LicenseInfo
                {
                    Status = LicenseStatus.Error,
                    Message = "Không thể đọc dữ liệu bản quyền cục bộ.",
                    _hwid = GetHardwareId()
                };
            }

            if (info == null)
            {
                // Chưa từng có license cache.
                // Chỉ ở trường hợp này mới thử xác định trial.
                var trialInfo = GetTrialInfo();

                if (trialInfo.isValid)
                    return BuildTrialInfo(trialInfo.startDate, trialInfo.minutesLeft);

                return new LicenseInfo
                {
                    Status = LicenseStatus.Inactive,
                    IsTrial = true,
                    Message = "Chưa có bản quyền"
                };
            }

            // Nếu cache là Trial thì tính thời gian còn lại từ TrialStartDate.
            if (info.IsTrial)
            {
                var trialInfo = GetTrialInfo();

                if (trialInfo.isValid)
                {
                    info.Status = LicenseStatus.Active;
                    info.IsTrial = true;
                    info.TrialStartDate = trialInfo.startDate;
                    info.TrialMinutesLeft = trialInfo.minutesLeft;
                    info.TrialTimeLeftDisplay =
                        FormatTimeLeft(trialInfo.minutesLeft, true);
                    info.ExpirationDate =
                        trialInfo.startDate.AddMinutes(TRIAL_MINUTES)
                        .ToString("yyyy-MM-dd HH:mm");
                    info.Message =
                        $"Dùng thử (còn lại {info.TrialTimeLeftDisplay})";
                }
                else
                {
                    info.Status = LicenseStatus.Inactive;
                    info.Message = "Đã hết hạn dùng thử";
                }

                info._hwid = GetHardwareId();
                return info;
            }

            // LICENSE ĐÃ KÍCH HOẠT:
            // vẫn cho user dùng local. Chỉ báo lỗi sau >7 ngày
            // không có lần xác minh ONLINE thành công.
            if (info.Status == LicenseStatus.Active)
            {
                if (info.LastOnlineVerify != DateTime.MinValue &&
                    (DateTime.Now - info.LastOnlineVerify).TotalDays > OFFLINE_GRACE_DAYS)
                {
                    info.Status = LicenseStatus.Error;
                    info.Message =
                        $"Đã quá {OFFLINE_GRACE_DAYS} ngày không xác minh bản quyền. " +
                        "Vui lòng kết nối Internet máy chủ.";
                }

                info._hwid = GetHardwareId();
                return info;
            }

            info._hwid = GetHardwareId();
            return info;
        }

        // ============================================================
        // ONLINE CHECK:
        // Chỉ Background Worker / first activation được gọi.
        // UI gọi false -> tuyệt đối KHÔNG hit API.
        // ============================================================
        public static async Task<LicenseInfo> CheckLicenseAsync(bool forceCheckOnline)
        {
            var cachedInfo = GetCachedLicenseState();

            // false = UI request.
            // UI không được gọi API.
            if (!forceCheckOnline)
                return cachedInfo;

            return await PerformOnlineCheck(cachedInfo);
        }

        private static async Task<LicenseInfo> PerformOnlineCheck(LicenseInfo cachedInfo)
        {
            try
            {
                string apiUrl = GetApiUrl();

                if (string.IsNullOrEmpty(apiUrl))
                {
                    WriteLog("License API URL rỗng.");
                    return cachedInfo;
                }

                using var client = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(15)
                };

                client.DefaultRequestHeaders.Add(
                    "User-Agent", "MES_Server/1.0");

                string response = await client.GetStringAsync(
                    $"{apiUrl}?action=check" +
                    $"&key={GetApiKey()}" +
                    $"&hwid={Uri.EscapeDataString(GetHardwareId())}");

                LicenseInfo newInfo = ParseApiResponse(response);

                // API trả lời thành công nhưng license chưa Active:
                // mới xét Trial.
                if (newInfo.Status != LicenseStatus.Active)
                {
                    newInfo = ApplyTrialIfNeeded(newInfo);
                }

                // Chỉ cập nhật thời điểm ONLINE khi API thực sự trả lời.
                DateTime now = DateTime.Now;

                newInfo.LastOnlineVerify = now;
                newInfo.LastCheckDate = now;
                newInfo._hwid = GetHardwareId();

                newInfo._chk = ComputeChecksum(
                    newInfo.Status.ToString() +
                    newInfo.ExpirationDate +
                    newInfo.IsTrial.ToString() +
                    newInfo.TrialStartDate.ToString("O") +
                    newInfo.LastOnlineVerify.ToString("O"));

                // Chỉ sau khi có response hợp lệ mới ghi đè cache.
                SaveCacheSecure(newInfo);

                return newInfo;
            }
            catch (Exception ex)
            {
                WriteLog("License online check failed - giữ nguyên local license", ex);

                // MẤT MẠNG:
                // Không sửa cache.
                // Không reset LastOnlineVerify.
                // Không biến Active thành Inactive.
                return cachedInfo;
            }
        }

        private static LicenseInfo ParseApiResponse(string json)
        {
            var info = new LicenseInfo
            {
                Status = LicenseStatus.Inactive,
                Message = "Lỗi phân tích dữ liệu"
            };

            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("success", out var succ) &&
                    succ.GetBoolean())
                {
                    if (root.TryGetProperty("status", out var stat) &&
                        stat.GetString() == "Active")
                    {
                        info.Status = LicenseStatus.Active;
                    }

                    if (root.TryGetProperty("expiration", out var exp))
                        info.ExpirationDate = exp.GetString() ?? "";

                    if (root.TryGetProperty("message", out var msg))
                        info.Message = msg.GetString() ?? "";

                    if (root.TryGetProperty("name", out var name))
                        info.LicensedTo = name.GetString() ?? "";
                }
                else
                {
                    if (root.TryGetProperty("error", out var err))
                        info.Message = err.GetString() ?? "API Error";
                }
            }
            catch { }

            return info;
        }

        private static LicenseInfo BuildTrialInfo(DateTime startDate, int minutesLeft)
        {
            return new LicenseInfo
            {
                Status = LicenseStatus.Active,
                IsTrial = true,
                TrialStartDate = startDate,
                TrialMinutesLeft = minutesLeft,
                TrialTimeLeftDisplay = FormatTimeLeft(minutesLeft, true),
                ExpirationDate =
                    startDate.AddMinutes(TRIAL_MINUTES)
                    .ToString("yyyy-MM-dd HH:mm"),
                Message =
                    $"Dùng thử (còn lại {FormatTimeLeft(minutesLeft, true)})",
                _hwid = GetHardwareId()
            };
        }

        private static LicenseInfo ApplyTrialIfNeeded(LicenseInfo info)
        {
            var trialInfo = GetTrialInfo();

            if (trialInfo.isValid)
            {
                info.Status = LicenseStatus.Active;
                info.IsTrial = true;
                info.TrialStartDate = trialInfo.startDate;
                info.TrialMinutesLeft = trialInfo.minutesLeft;
                info.TrialTimeLeftDisplay =
                    FormatTimeLeft(trialInfo.minutesLeft, true);
                info.ExpirationDate =
                    trialInfo.startDate.AddMinutes(TRIAL_MINUTES)
                    .ToString("yyyy-MM-dd HH:mm");
                info.Message =
                    $"Dùng thử (còn lại {info.TrialTimeLeftDisplay})";
            }
            else
            {
                info.Status = LicenseStatus.Inactive;
                info.IsTrial = true;
                info.TrialMinutesLeft = 0;
                info.TrialTimeLeftDisplay = "";
                info.Message = "Đã hết hạn dùng thử";
            }

            return info;
        }

        private static bool VerifyLicenseIntegrity(LicenseInfo info)
        {
            if (info == null)
                return false;

            if (string.IsNullOrEmpty(info._chk) ||
                info._hwid != GetHardwareId())
                return false;

            string data =
                info.Status.ToString() +
                info.ExpirationDate +
                info.IsTrial.ToString() +
                info.TrialStartDate.ToString("O") +
                info.LastOnlineVerify.ToString("O");

            // Hỗ trợ cache cũ: nếu checksum cũ vẫn hợp lệ,
            // chấp nhận và nâng cấp checksum ở lần online tiếp theo.
            string oldData =
                info.Status.ToString() +
                info.ExpirationDate +
                info.IsTrial.ToString();

            return info._chk == ComputeChecksum(data) ||
                   info._chk == ComputeChecksum(oldData);
        }

        private static void SaveCacheSecure(LicenseInfo info)
        {
            if (info == null)
                return;

            try
            {
                lock (_ioLock)
                {
                    if (!Directory.Exists(_appFolder))
                        Directory.CreateDirectory(_appFolder);

                    if (File.Exists(CachePath))
                        File.SetAttributes(CachePath, FileAttributes.Normal);

                    string jsonData = JsonSerializer.Serialize(info);
                    string encrypted = EncryptString(jsonData);

                    if (string.IsNullOrEmpty(encrypted))
                        return;

                    // Ghi file tạm trước, sau đó replace.
                    // Tránh trường hợp process chết giữa lúc ghi làm mất cache.
                    string tempPath = CachePath + ".tmp";

                    File.WriteAllText(tempPath, encrypted);

                    if (File.Exists(CachePath))
                        File.Replace(tempPath, CachePath, null);
                    else
                        File.Move(tempPath, CachePath);
                }
            }
            catch (Exception ex)
            {
                WriteLog("Lỗi ghi Cache", ex);
            }
        }
    }
}