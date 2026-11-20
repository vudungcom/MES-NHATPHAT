using System.Collections.Concurrent;
using System.Text;

namespace MES.Web.Services
{
    // ================================================================
    // [LOG-FILE 2026-11-20] Gói 26: ghi log ra FILE (trước: chỉ cửa sổ console — tắt cửa sổ / chạy Windows Service là mất hết lỗi).
    //   Tự viết (không thêm thư viện NuGet): mỗi ngày 1 file "<Path>/mes-yyyyMMdd.log" (mặc định thư mục logs cạnh app), ghi từ mức MinLevel
    //   (mặc định Warning) trở lên, giữ KeepDays ngày (mặc định 30, file cũ hơn tự xoá lúc sang ngày mới). Cấu hình mục "Logging:File"
    //   (appsettings.Production.json). Ghi lỗi file (ổ đầy / thiếu quyền) → bỏ qua, không làm sập app.
    //   Nguồn: CLAUDE quét Fable 15/11 (QF-F "Windows Service + log ra file"); HÙNG 20/11 18:12 (Gói 26) + 18:32 OK tự viết, logs\mes-YYYYMMDD.log, giữ 30 ngày, từ Warning.
    // ================================================================
    public sealed class FileLoggerProvider : ILoggerProvider
    {
        private readonly string _dir;
        private readonly LogLevel _min;
        private readonly int _keepDays;
        private readonly object _lock = new();
        private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
        private DateTime _cleanedFor = DateTime.MinValue;

        public FileLoggerProvider(string dir, LogLevel min = LogLevel.Warning, int keepDays = 30)
        {
            _dir = dir; _min = min; _keepDays = Math.Max(1, keepDays);
        }

        /// <summary>Đọc mục "Logging:File" (Path, MinLevel, KeepDays); Path tương đối tính từ thư mục gốc app.</summary>
        public static FileLoggerProvider FromConfig(IConfiguration cfg, string contentRoot)
        {
            var sec = cfg.GetSection("Logging:File");
            var path = sec["Path"];
            if (string.IsNullOrWhiteSpace(path)) path = "logs";
            if (!Path.IsPathRooted(path)) path = Path.Combine(contentRoot, path);
            var min = Enum.TryParse<LogLevel>(sec["MinLevel"], true, out var lv) ? lv : LogLevel.Warning;
            var keep = int.TryParse(sec["KeepDays"], out var k) && k > 0 ? k : 30;
            return new FileLoggerProvider(path, min, keep);
        }

        public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, n => new FileLogger(this, n));
        public void Dispose() => _loggers.Clear();

        internal bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= _min;

        internal void Write(string category, LogLevel level, string message, Exception? ex)
        {
            try
            {
                var now = DateTime.Now;
                var sb = new StringBuilder();
                sb.Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(" [").Append(LevelText(level)).Append("] ").Append(category).Append(": ").Append(message);
                if (ex != null) sb.Append(Environment.NewLine).Append(ex);
                sb.Append(Environment.NewLine);
                lock (_lock)
                {
                    Directory.CreateDirectory(_dir);
                    File.AppendAllText(Path.Combine(_dir, $"mes-{now:yyyyMMdd}.log"), sb.ToString(), Encoding.UTF8);
                    if (_cleanedFor != now.Date) { _cleanedFor = now.Date; Clean(now); }
                }
            }
            catch { /* ghi log lỗi không được làm sập app */ }
        }

        private void Clean(DateTime now)
        {
            try
            {
                var limit = now.Date.AddDays(-_keepDays + 1);   // giữ đúng KeepDays file (hôm nay + KeepDays−1 ngày trước) — rà độc lập Gói 26 L6
                foreach (var f in Directory.EnumerateFiles(_dir, "mes-*.log"))
                {
                    var name = Path.GetFileNameWithoutExtension(f);   // mes-yyyyMMdd
                    if (name.Length == 12 && DateTime.TryParseExact(name[4..], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var d) && d < limit)
                        File.Delete(f);
                }
            }
            catch { }
        }

        private static string LevelText(LogLevel l) => l switch
        {
            LogLevel.Trace => "TRACE", LogLevel.Debug => "DEBUG", LogLevel.Information => "INFO",
            LogLevel.Warning => "CANH BAO", LogLevel.Error => "LOI", LogLevel.Critical => "NGHIEM TRONG", _ => l.ToString()
        };

        private sealed class FileLogger : ILogger
        {
            private readonly FileLoggerProvider _p; private readonly string _cat;
            public FileLogger(FileLoggerProvider p, string cat) { _p = p; _cat = cat; }
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => _p.IsEnabled(logLevel);
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;
                _p.Write(_cat, logLevel, formatter(state, exception), exception);
            }
        }
    }
}
