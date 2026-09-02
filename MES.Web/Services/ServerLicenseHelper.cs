#nullable disable
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace MES.Web.Services
{
    public static class ServerLicenseHelper
    {
        // ============================================================
        // Ổ KHOÁ CHỐNG XUNG ĐỘT ĐA LUỒNG
        // ============================================================
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

        // ============================================================
        // CONFIG
        // ============================================================
        private const int TRIAL_MINUTES = 21600; // 15 Ngày
        private const int OFFLINE_GRACE_DAYS = 7;

        // [FIXED] Lưu thẳng vào thư mục chứa Code của ứng dụng Web để đảm bảo 100% có quyền GHI/ĐỌC
        private static readonly string _appFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MES_LicenseData");

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
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(data + GetHardwareId() + "MES_CHK"));
            return Convert.ToBase64String(hash).Substring(0, 16);
        }

        private const string REGISTRY_PATH = @"Software\MES_Server\License";
        private const string REGISTRY_VALUE = "InitDate";

        private static DateTime GetTrialStartDate()
        {
            DateTime fileDate = DateTime.MinValue;
            DateTime regDate = DateTime.MinValue;

            try
            {
                lock (_ioLock)
                {
                    if (File.Exists(TrialPath))
                    {
                        string decrypted = DecryptString(File.ReadAllText(TrialPath));
                        if (DateTime.TryParse(decrypted, out DateTime fd) && fd <= DateTime.Now) fileDate = fd;
                    }
                }
            }
            catch { }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(REGISTRY_PATH) ?? Registry.CurrentUser.OpenSubKey(REGISTRY_PATH);
                if (key != null)
                {
                    string decrypted = DecryptString(key.GetValue(REGISTRY_VALUE) as string);
                    if (DateTime.TryParse(decrypted, out DateTime rd) && rd <= DateTime.Now) regDate = rd;
                }
            }
            catch { }

            DateTime oldest = (fileDate != DateTime.MinValue && regDate != DateTime.MinValue) 
                ? (fileDate < regDate ? fileDate : regDate) 
                : (fileDate != DateTime.MinValue ? fileDate : regDate);

            if (oldest != DateTime.MinValue)
            {
                if (fileDate != oldest) SaveTrialToFile(oldest);
                if (regDate != oldest) SaveTrialToRegistry(oldest);
            }

            return oldest;
        }

        private static void SaveTrialToFile(DateTime startDate)
        {
            try
            {
                lock (_ioLock)
                {
                    if (!Directory.Exists(_appFolder)) Directory.CreateDirectory(_appFolder);
                    // Bỏ thuộc tính Hidden trước khi ghi để chống văng lỗi Access Denied
                    if (File.Exists(TrialPath)) File.SetAttributes(TrialPath, FileAttributes.Normal);
                    File.WriteAllText(TrialPath, EncryptString(startDate.ToString("yyyy-MM-dd HH:mm:ss")));
                }
            }
            catch (Exception ex) { WriteLog("SaveTrialToFile Lỗi", ex); }
        }

        private static void SaveTrialToRegistry(DateTime startDate)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(REGISTRY_PATH) ?? Registry.CurrentUser.CreateSubKey(REGISTRY_PATH);
                key?.SetValue(REGISTRY_VALUE, EncryptString(startDate.ToString("yyyy-MM-dd HH:mm:ss")));
            }
            catch { }
        }

        private static (bool isValid, int minutesLeft, DateTime startDate) GetTrialInfo()
        {
            DateTime startDate = GetTrialStartDate();
            if (startDate == DateTime.MinValue)
            {
                startDate = DateTime.Now;
                SaveTrialToFile(startDate);
                SaveTrialToRegistry(startDate);
                return (true, TRIAL_MINUTES, startDate);
            }

            int minutesLeft = (int)(startDate.AddMinutes(TRIAL_MINUTES) - DateTime.Now).TotalMinutes;
            return (minutesLeft > 0, Math.Max(0, minutesLeft), startDate);
        }

        public static string FormatTimeLeft(int minutesLeft, bool vietnamese)
        {
            if (minutesLeft >= 1440) return vietnamese ? $"{minutesLeft / 1440} ngày" : $"{minutesLeft / 1440} days";
            if (minutesLeft >= 60) return vietnamese ? $"{minutesLeft / 60} giờ" : $"{minutesLeft / 60} hours";
            return vietnamese ? $"{minutesLeft} phút" : $"{minutesLeft} minutes";
        }

        public static void WriteLog(string message, Exception ex = null)
        {
            try
            {
                lock (_ioLock)
                {
                    if (!Directory.Exists(_appFolder)) Directory.CreateDirectory(_appFolder);
                    string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n";
                    if (ex != null) logEntry += $"Exception: {ex.Message}\nStackTrace: {ex.StackTrace}\n";
                    File.AppendAllText(LogPath, logEntry);

                    var fi = new FileInfo(LogPath);
                    if (fi.Exists && fi.Length > 1024 * 1024)
                        File.WriteAllLines(LogPath, File.ReadAllLines(LogPath).Skip(100).ToArray());
                }
            }
            catch { }
        }

        public enum LicenseStatus { Active, Inactive, Error }

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
            public DateTime LastOnlineVerify { get; set; } = DateTime.MinValue;
            internal string _hwid { get; set; } = "";
            internal string _chk { get; set; } = "";
        }

        private static string _cachedHwId = null;

        public static string GetHardwareId()
        {
            if (_cachedHwId != null) return _cachedHwId;
            var sb = new StringBuilder();

            try
            {
                sb.Append(Environment.MachineName);
                sb.Append(Environment.OSVersion.VersionString);
                sb.Append(Environment.ProcessorCount);
                
                var macAddr = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(nic => nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .OrderBy(nic => nic.Id) 
                    .Select(nic => nic.GetPhysicalAddress().ToString())
                    .FirstOrDefault();

                if (!string.IsNullOrEmpty(macAddr)) sb.Append(macAddr);
            }
            catch { }

            if (sb.Length == 0) return _cachedHwId = "UNKNOWN-" + Environment.MachineName;

            using var sha = SHA256.Create();
            string hex = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "");
            return _cachedHwId = $"{hex.Substring(0, 4)}-{hex.Substring(4, 4)}-{hex.Substring(8, 4)}-{hex.Substring(12, 4)}";
        }

        public static async Task<LicenseInfo> CheckLicenseAsync(bool forceCheckOnline)
        {
            var cachedInfo = new LicenseInfo();

            try
            {
                string encryptedData = null;
                lock (_ioLock)
                {
                    if (File.Exists(CachePath)) encryptedData = File.ReadAllText(CachePath);
                }

                if (!string.IsNullOrEmpty(encryptedData))
                {
                    cachedInfo = ParseJsonFromCache(DecryptString(encryptedData));
                    if (!VerifyLicenseIntegrity(cachedInfo)) forceCheckOnline = true;
                }
            }
            catch { cachedInfo = new LicenseInfo(); }

            if (cachedInfo.IsTrial)
            {
                if (!GetTrialInfo().isValid) forceCheckOnline = true;
            }

            if (!forceCheckOnline && cachedInfo.LastCheckDate != DateTime.MinValue)
            {
                double daysSinceLastCheck = (DateTime.Now - cachedInfo.LastCheckDate).TotalDays;
                if (cachedInfo.Status == LicenseStatus.Active && !cachedInfo.IsTrial)
                {
                    if (daysSinceLastCheck <= OFFLINE_GRACE_DAYS) return cachedInfo;
                    forceCheckOnline = true;
                }
                else if (cachedInfo.IsTrial)
                {
                    var trialInfo = GetTrialInfo();
                    if (trialInfo.isValid)
                    {
                        cachedInfo.TrialMinutesLeft = trialInfo.minutesLeft;
                        cachedInfo.TrialTimeLeftDisplay = FormatTimeLeft(trialInfo.minutesLeft, true);
                        return cachedInfo;
                    }
                }
            }

            try
            {
                string apiUrl = GetApiUrl();
                if (string.IsNullOrEmpty(apiUrl)) return ApplyTrialIfNeeded(new LicenseInfo { Status = LicenseStatus.Error, Message = "Lỗi cấu hình API" });

                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                client.DefaultRequestHeaders.Add("User-Agent", "MES_Server/1.0");

                string response = await client.GetStringAsync($"{apiUrl}?action=check&key={GetApiKey()}&hwid={Uri.EscapeDataString(GetHardwareId())}");
                var newInfo = ParseApiResponse(response);

                if (newInfo.Status != LicenseStatus.Active) newInfo = ApplyTrialIfNeeded(newInfo);

                newInfo.LastOnlineVerify = DateTime.Now;
                newInfo._hwid = GetHardwareId();
                newInfo._chk = ComputeChecksum(newInfo.Status.ToString() + newInfo.ExpirationDate + newInfo.IsTrial.ToString());

                SaveCacheSecure(newInfo);
                return newInfo;
            }
            catch (Exception ex)
            {
                WriteLog("License check failed (offline)", ex);

                if (cachedInfo.LastCheckDate != DateTime.MinValue && cachedInfo.Status == LicenseStatus.Active && !cachedInfo.IsTrial)
                {
                    if ((DateTime.Now - cachedInfo.LastCheckDate).TotalDays <= OFFLINE_GRACE_DAYS)
                    {
                        if (DateTime.TryParse(cachedInfo.ExpirationDate, out DateTime expDate) && expDate.Date < DateTime.Now.Date 
                            && !cachedInfo.ExpirationDate.Contains("Lifetime", StringComparison.OrdinalIgnoreCase))
                        {
                            cachedInfo.Status = LicenseStatus.Inactive;
                            cachedInfo.Message = "Bản quyền đã hết hạn";
                        }
                        return cachedInfo;
                    }
                    return ApplyTrialIfNeeded(new LicenseInfo { Status = LicenseStatus.Error, Message = $"Quá {OFFLINE_GRACE_DAYS} ngày không có Internet." });
                }
                return ApplyTrialIfNeeded(new LicenseInfo { Status = LicenseStatus.Error, Message = "Lỗi kết nối mạng" });
            }
        }

        private static LicenseInfo ParseApiResponse(string json)
        {
            var info = new LicenseInfo { Status = LicenseStatus.Inactive, Message = "Lỗi phân tích dữ liệu", LastCheckDate = DateTime.Now };
            try
            {
                if (Regex.Match(json, "\"success\"\\s*:\\s*true").Success)
                {
                    if (Regex.Match(json, "\"status\"\\s*:\\s*\"Active\"").Success) info.Status = LicenseStatus.Active;
                    info.ExpirationDate = Regex.Match(json, "\"expiration\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
                    info.Message = Regex.Match(json, "\"message\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
                    info.LicensedTo = Regex.Match(json, "\"name\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
                }
                else
                {
                    info.Message = Regex.Match(json, "\"error\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
                }
            }
            catch { }
            return info;
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
                info.TrialTimeLeftDisplay = FormatTimeLeft(trialInfo.minutesLeft, true);
                info.ExpirationDate = trialInfo.startDate.AddMinutes(TRIAL_MINUTES).ToString("yyyy-MM-dd HH:mm");
                info.Message = $"Dùng thử (còn lại {info.TrialTimeLeftDisplay})";
            }
            else
            {
                info.Status = LicenseStatus.Inactive;
                info.IsTrial = true;
                info.TrialMinutesLeft = 0;
                info.TrialTimeLeftDisplay = "";
                info.Message = "Đã hết hạn dùng thử";
            }
            info.LastCheckDate = DateTime.Now;
            return info;
        }

        private static bool VerifyLicenseIntegrity(LicenseInfo info)
        {
            if (string.IsNullOrEmpty(info._chk) || info._hwid != GetHardwareId()) return false;
            return info._chk == ComputeChecksum(info.Status.ToString() + info.ExpirationDate + info.IsTrial.ToString());
        }

        private static void SaveCacheSecure(LicenseInfo info)
        {
            try
            {
                lock (_ioLock)
                {
                    if (!Directory.Exists(_appFolder)) Directory.CreateDirectory(_appFolder);
                    // Bỏ thuộc tính Hidden trước khi ghi để chống văng lỗi Access Denied
                    if (File.Exists(CachePath)) File.SetAttributes(CachePath, FileAttributes.Normal);
                    
                    string jsonData = $@"{{
""Status"": ""{info.Status}"",
""Message"": ""{info.Message}"",
""ExpirationDate"": ""{info.ExpirationDate}"",
""LastCheckDate"": ""{info.LastCheckDate:yyyy-MM-dd HH:mm:ss}"",
""LastOnlineVerify"": ""{info.LastOnlineVerify:yyyy-MM-dd HH:mm:ss}"",
""IsTrial"": {info.IsTrial.ToString().ToLower()},
""TrialStartDate"": ""{info.TrialStartDate:yyyy-MM-dd HH:mm:ss}"",
""TrialMinutesLeft"": {info.TrialMinutesLeft},
""TrialTimeLeftDisplay"": ""{info.TrialTimeLeftDisplay}"",
""_hwid"": ""{info._hwid}"",
""_chk"": ""{info._chk}""
}}";
                    File.WriteAllText(CachePath, EncryptString(jsonData));
                }
            }
            catch (Exception ex) { WriteLog("Lỗi ghi Cache", ex); }
        }

        private static LicenseInfo ParseJsonFromCache(string json)
        {
            var info = new LicenseInfo();
            try
            {
                if (Regex.Match(json, "\"Status\"\\s*:\\s*\"Active\"").Success) info.Status = LicenseStatus.Active;
                info.Message = Regex.Match(json, "\"Message\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
                info.ExpirationDate = Regex.Match(json, "\"ExpirationDate\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
                if (DateTime.TryParse(Regex.Match(json, "\"LastCheckDate\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value, out DateTime dt)) info.LastCheckDate = dt;
                info._hwid = Regex.Match(json, "\"_hwid\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
                info._chk = Regex.Match(json, "\"_chk\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
                if (Regex.Match(json, "\"IsTrial\"\\s*:\\s*true", RegexOptions.IgnoreCase).Success) info.IsTrial = true;
            }
            catch { }
            return info;
        }
    }
}