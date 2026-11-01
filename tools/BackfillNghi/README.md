# BackfillNghi — chép bù giờ nghỉ cho dữ liệu cũ ([ML17-XN][CA-NGHI-HIST] 01/11/2026)

Từ gói ML17-XN mỗi dòng WTS / "Việc không ra sản phẩm" lưu kèm khoảng nghỉ ca nó chạm (bảng `WtsBreakWorkLogs`) và mọi
màn hình tính phút từ bảng này (không đọc giờ nghỉ hiện tại của ca nữa). Dòng lưu TRƯỚC gói chưa có → công cụ này chép giờ nghỉ
HIỆN TẠI của ca vào (phút làm thông = 0, cột `IsBackfill = 1`), tính bằng chính `WorkMinutes` của phần mềm.

- Dòng WTS: ca = cột CaId của dòng; không có ca → giữ phút thô (in số dòng).
- Việc không ra SP (không có cột ca): lấy ca của dòng WTS cùng người cùng ngày; không có / nhiều ca → giữ phút thô.
- Chỉ dòng chưa có dòng nghỉ nào và tạo trước mốc (dòng nghỉ đầu tiên code mới ghi). Chạy lại không sao.

## Chạy (thư mục `tools\BackfillNghi` của bản app trỏ đúng DB)
```
cd /d "D:\Setup\Ngoc\App cua Ngoc\Nhat Phat\Software\mes-prototype\tools\BackfillNghi"
dotnet run -c Release                 (chỉ đọc: in số dòng sẽ chép, dòng nào phút khác trang cũ)
dotnet run -c Release -- --ghi        (ghi thật: hỏi gõ đúng tên DB, 1 giao dịch)
```
Kết quả: màn hình + `ketqua_backfill_<DB>_<giờ>.txt`. Cần chạy SQL `tools\sql\06_ML17XN_WtsBreakWorkLogs.sql` trước.
