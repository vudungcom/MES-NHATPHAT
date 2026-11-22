namespace MES.Web.Services;

// =====================================================================
// [G32-SAOLUU 2026-11-22] Gói 32a — lịch sao lưu chạy ngầm trong app (SQL Express không có SQL Agent).
//   Chờ 60 giây sau khi app khởi động (không làm chậm lúc mở app), rồi mỗi 30 giây hỏi SaoLuuService.DenHan:
//   đến hạn bộ ngày → tạo trọn bộ; đến hạn bản nhanh → lưu nhanh. Tắt tự động / chưa cài thư mục → không làm gì (băng đỏ báo Admin).
//   Lỗi bất kỳ chỉ ghi log + để lại bản LOI (tab / băng đỏ thấy) — không bao giờ làm sập app.
//   App phải chạy liên tục mới có lịch → máy khách cài app dạng Windows Service (Gói 32b).
//   [G32-RA 2026-11-22] appsettings "SaoLuu:TatLichTuDong": true → không chạy lịch (bản app thử / harness — không tranh với nút bấm);
//   cùng 1 lỗi lặp lại chỉ ghi log 1 lần / 30 phút (trước: mỗi 30 giây ~2.880 dòng / ngày). Nguồn: CLAUDE rà độc lập Gói 32a (mục 20, 22).
// =====================================================================
public class SaoLuuWorker : BackgroundService
{
    private readonly SaoLuuService _svc;
    private readonly ILogger<SaoLuuWorker> _log;
    private readonly bool _tat;
    private string? _loiCuoi;
    private DateTime _lucLog;

    public SaoLuuWorker(SaoLuuService svc, ILogger<SaoLuuWorker> log, IConfiguration cfg)
    {
        _svc = svc; _log = log;
        _tat = string.Equals(cfg["SaoLuu:TatLichTuDong"], "true", StringComparison.OrdinalIgnoreCase);
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (_tat) { _log.LogWarning("[G32-SAOLUU] Lịch sao lưu tự động bị TẮT bằng appsettings SaoLuu:TatLichTuDong"); return; }
        try { await Task.Delay(TimeSpan.FromSeconds(60), stop); } catch (OperationCanceledException) { return; }
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var db = await _svc.DbAsync(stop);
                var c = _svc.Store.Doc(db);
                var l = _svc.DenHan(c, db, _svc.Clock());
                if (l != null) await _svc.ChayAsync(l.Value, "tu-dong", stop);
                _loiCuoi = null;
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                var now = DateTime.Now;
                if (ex.Message != _loiCuoi || now - _lucLog > TimeSpan.FromMinutes(30))
                {
                    _log.LogWarning(ex, "[G32-SAOLUU] Vòng lịch sao lưu lỗi");
                    _loiCuoi = ex.Message; _lucLog = now;
                }
            }
            try { await Task.Delay(TimeSpan.FromSeconds(30), stop); } catch (OperationCanceledException) { return; }
        }
    }
}
