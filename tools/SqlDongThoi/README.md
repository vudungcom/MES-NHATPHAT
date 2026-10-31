# SqlDongThoi — thử nhiều người cùng lưu trên SQL Server thật (T1) · 31/10/2026

Chạy **code thật** của MES.Web (HandoverService, WtsSaveService, KhoVatLieuService, KhPlanService) trên **DB bản sao MES_TEST**.
Mỗi "người" là 1 luồng có DbContext riêng (như 1 phiên trình duyệt), bấm Lưu cùng lúc.
Tool **chỉ chạy khi DB đang nối là MES_TEST** (kiểm `SELECT DB_NAME()`), trỏ nhầm MES là dừng ngay, không ghi gì.

## Chạy (trên máy server, nơi đã có MES_TEST + bản D:\MES\MES_TEST_APP)
1. Chép thư mục `tools\SqlDongThoi` vào `D:\MES\MES_TEST_APP\mes-prototype\tools\` (cạnh `GiaoNhanFuzz`).
2. Mở cmd:
   ```
   cd /d D:\MES\MES_TEST_APP\mes-prototype\tools\SqlDongThoi
   dotnet run -c Release
   ```
   Lần đầu build khoảng 1–2 phút. Chuỗi kết nối tự lấy từ `..\..\MES.Web\appsettings.json` của bản MES_TEST_APP (đã sửa `Database=MES_TEST`).
3. Chạy xong (~2–3 phút) dòng cuối phải là `KẾT QUẢ: ĐẠT hết`. Tool ghi file `ketqua_T1_<giờ>.txt` trong thư mục này → gửi file đó cho Claude.

Tuỳ chọn: `dotnet run -c Release -- <số người> <số giây> <số PO> <phôi mỗi PO>`, mặc định `6 60 4 100`.
Ví dụ thử nặng: `dotnet run -c Release -- 12 180 6 200`. Chuỗi kết nối khác: thêm `--cs "Server=...;Database=MES_TEST;..."`.

## Tool làm gì
Tự tạo 1 phiếu KH mới cho mỗi bài, PO tên `T1-<giờ chạy>-...` (khách đầu tiên trong danh mục, người thử = tài khoản nhóm ADMIN đầu tiên),
quy trình thử NC01 → NC02 → Taro T1, Kho nhận phôi. Dữ liệu này chỉ nằm trong MES_TEST; thử xong xoá cả MES_TEST bằng khối XOÁ của script dựng.

| Bài | Làm cùng lúc | Đúng phải là |
|---|---|---|
| S1 | 8 người lập phiếu Kho → Gia công 20, PO 100 phôi | đúng 5 phiếu lưu được, Kho còn 0 |
| S2 | 4 người bấm Nhận đủ cùng 1 phiếu | đúng 1 lần nhận |
| S3 | Gia công đã nhận 100; 8 người nhập WTS NC01 20 (8 máy) | đúng 5 dòng, NC01 = 100 |
| S4 | 2 người nhập WTS cùng máy, cùng giờ, 2 PO | đúng 1 người lưu được |
| S5 | N người làm ngẫu nhiên N giây: giao, nhận, WTS NC01 / NC02 / Taro, giao GC → HTSP, huỷ nhận, huỷ phiếu, xoá WTS | mọi PO: sổ cân, không ô âm, không lần nhận vượt phiếu, 0 lỗi lạ |

Bảng THỐNG KÊ: số lần lưu được / bị chặn đúng luật / "Máy chủ đang bận" (deadlock thử 3 lần chưa được) / chờ khoá quá 10 s / lỗi lạ, và thời gian (ms).
"Bị chặn" là bình thường (người đến sau bị luật chặn). **Lỗi lạ** hoặc **SAI** thì gửi file kết quả cho Claude.

## TuKiem (Claude dùng)
`TuKiem/` biên dịch cùng `Core.cs` trên DB giả của `GiaoNhanFuzz` (không cần SQL, không cần NuGet) để kiểm logic các bài trước khi giao — chạy tuần tự, không đo đồng thời.
`cd TuKiem && dotnet build -c Release -o <thư mục> && dotnet <thư mục>/TuKiem.dll 5`.
