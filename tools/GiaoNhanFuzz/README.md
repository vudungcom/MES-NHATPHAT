# GiaoNhanFuzz — kiểm in/out ngẫu nhiên Giao nhận + WTS

Không thuộc web (nằm ngoài thư mục MES.Web nên không bị biên dịch chung). Không cần NuGet: EF Core được thay bằng stub trong bộ nhớ (`EfStub.cs`, `AppDbContextStub.cs`), gọi thẳng `HandoverService` / `HandoverLedgerService` thật trong `../../MES.Web`.

Chạy (từ thư mục này):

```
dotnet build -o bin2
dotnet bin2/GiaoNhanFuzz.dll <số PO> <thao tác mỗi PO> <seed> [PO bắt đầu]
# ví dụ: dotnet bin2/GiaoNhanFuzz.dll 150 150 31
# EXCL=0.7 → 70% PO có NC máy loại trừ; DUMP=1 → in sổ chi tiết khi gặp lỗi
```

Sau mỗi thao tác thành công kiểm: cân PO, không ô âm (gồm từng NC GC + từng mã CV), WTS ≤ hàng thật, Σ chờ NC = Chưa GC, dòng B cuối = sổ, trạng thái phiếu, phiếu tự lập, quyền huỷ. Kết quả "SAI: 0" là đạt.

Thêm (GN-WHY):

```
dotnet bin2/GiaoNhanFuzz.dll scen    # 3 tình huống xung đột, in lời báo thật kèm gợi ý gỡ
dotnet bin2/GiaoNhanFuzz.dll bench   # đo thời gian tính sổ + tìm gốc rễ trên 1 PO lớn
```

Mỗi lần huỷ / xoá WTS bị chặn, bộ kiểm còn: tìm gợi ý gỡ, kiểm lời báo có gợi ý, làm đúng theo gợi ý qua hàm thật rồi thử lại thao tác gốc — phải lưu được.

Thêm (KH-GUARD, 27/10/2026):

```
dotnet bin2/GiaoNhanFuzz.dll kh      # kịch bản vòng A: Kho sửa phôi nhận (K5), Xoá / Tách PO, Cập nhật quy trình (K6), bật Workflow (K7)
PHOI=6 dotnet bin2/GiaoNhanFuzz.dll 80 120 11   # trọng số thao tác "Kho sửa phôi nhận" trong bộ kiểm ngẫu nhiên (mặc định 6)
```

Bộ kiểm ngẫu nhiên có thêm thao tác `phoi` gọi hàm thật `KhoVatLieuService.SaveKhoInputAsync`: bị chặn thì phôi nhận phải giữ nguyên (rollback) và lời báo phải có gợi ý gỡ; lưu được thì kiểm cân như mọi thao tác khác.
Biên dịch thêm `KhPlanService`, `KhoVatLieuService`, `PoGuard` (UserService / ClosedXML thay bằng stub trong thư mục này).

Thêm (GN-NG-AFTER, 27/10/2026): thao tác `ngrep` (báo hàng lỗi phát hiện sau khi nhận, hàm thật `ReportNgAfterReceiveAsync`, 10% cố tình vượt trần phải bị chặn, kiểm cả người ngoài nhóm không báo được) và `vngrep` (huỷ báo lỗi; bị chặn thì làm theo gợi ý gỡ rồi thử lại). Trọng số: `NGREP=30`. Kịch bản `kh` có thêm B1–B3 (Kho báo 3 phôi lỗi → trả khách; GC báo lỗi ở NC1 → chuyển hàng lỗi về Kho; báo rồi huỷ).

Thêm (GN-WHERE, 27/10/2026): sau mỗi thao tác kiểm `HandoverLedgerService.WhereIs` (PO đang ở đâu): tổng = phôi nhận, không ô âm (Workflow ON), không có phần lệch NC. Kịch bản `kh` thêm C1.

Thêm (GN-NG-TRACE, 28/10/2026): sau mỗi thao tác dựng **đường đi hàng lỗi** (`HandoverNgTrace.Build`) và kiểm: cân hàng lỗi ở MỌI bước dòng B (phát sinh − đã xử lý − huỷ − trả khách = đang giữ theo sổ + đang chuyển), đang giữ từng nơi = ô Tồn hàng lỗi / Tồn hàng sửa của sổ, đang chuyển = phiếu hàng lỗi chưa nhận, tổng theo nơi = tổng chung, lọc "có phát sinh lỗi" (`EverHadNg`) khớp khung lỗi; thỉnh thoảng gọi cả hàm nạp thật `GetNgTraceAsync`.

```
dotnet bin2/GiaoNhanFuzz.dll loi     # kịch bản cố định: hàng lỗi qua Kho → GC → HTSP (sửa) → KCS → huỷ / trả khách, số phải ra từng bước
LOI=1 dotnet bin2/GiaoNhanFuzz.dll 60 120 5   # thử nặng hàng lỗi: chọn thao tác có hàng thật, NG nhiều hơn (không đặt LOI = y như cũ)
dotnet bin2/GiaoNhanFuzz.dll nang [PO=60] [thao tác=120] [seed=1]   # kh + loi + 6 cấu hình ngẫu nhiên chạy song song, kết quả ở nang_ketqua/
```
