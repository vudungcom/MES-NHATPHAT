# QuetSo — quét toàn DB, CHỈ ĐỌC ([RA-SOCHUNG] 31/10/2026)

Tìm mọi PO đang: **không cân** (phôi ≠ tồn các nơi + đang giao + thành phẩm + huỷ + trả khách), **ô âm**, **WTS vượt trần**
(OK + NG > hàng đã nhận vào NC / mã), khung **"Đang ở đâu" lệch**, và **dòng WTS số lệch** (QtyDone ≠ QtyOk + QtyNg).
Mọi số tính bằng đúng hàm sổ chung của phần mềm (`HandoverLedgerService`) — không công thức riêng.

**Không ghi gì vào DB:** mọi câu đọc chạy trong 1 giao dịch và luôn Rollback ở cuối, không gọi SaveChanges.

## Chạy (máy server, thư mục `tools\QuetSo` của bản app đang trỏ DB muốn quét)
```
cd /d "D:\MES\MES_TEST_APP\mes-prototype\tools\QuetSo"
dotnet run -c Release
```
- Chuỗi kết nối tự lấy từ `..\..\MES.Web\appsettings.json` (bản MES_TEST_APP → DB MES_TEST). Dòng đầu in tên DB đang quét.
- Quét DB khác: `dotnet run -c Release -- --cs "Server=...;Database=MES;..."` (hoặc chạy trong thư mục tools\QuetSo của bản app 5000).
- Kết quả: màn hình + file `ketqua_quet_<DB>_<giờ>.txt` (gửi file này cho Claude). Mã thoát 0 = không phát hiện gì.

Thứ tự Hùng chốt: chạy MES_TEST trước, rồi MES.

## Loại phát hiện
| Mã | Nghĩa |
|---|---|
| KHONG_CAN | Phôi ≠ tồn + đang giao + thành phẩm + huỷ + trả khách (L6) |
| O_AM | Ô sổ âm (K5) — vd "Gia công: Đã làm chưa giao = −16" |
| WTS_VUOT_TRAN | NC / mã: đã ghi WTS OK + NG > hàng nhận vào (L3) |
| DANG_O_DAU_LECH | Khung "Đang ở đâu" cộng ≠ phôi |
| WTS_SO_LECH | Dòng WTS QtyDone ≠ QtyOk + QtyNg |
| WTS_SO_AM | Dòng WTS có số âm |
| CANH_BAO_SO | Cảnh báo khác của sổ (vd PO chưa có snapshot mã CV) |
| MA_TRUNG | Quy trình PO có 2 dòng chính cùng mã trong 1 nhóm ([PM-NC-DUP-SVC]) |
| PHIEU_CHO_O_DAT | [QUET-PHIEU-CHO] PO bật Workflow có phiếu chưa nhận xong lấy từ ô Hàng lỗi / Hàng sửa mà chỉ định ô Hàng lỗi đã xử lý / Hàng mới (lập lúc OFF / trước Gói 16 — nhận vào ô đạt bị chặn); lọc trước khi lên máy khách |
| TRA_VE_NHOM_LA | [QUET-PHIEU-CHO] Phiếu "Trả về Gia công theo NC" tới nhóm khác Gia công — máy chủ chặn, chỉ có khi ghi thẳng DB (khung Đang ở đâu lệch) |
| LOAI_TRU_DAU | Quy trình Gia công (snapshot PO) bắt đầu bằng NC máy loại trừ ([N3-FIRST] 03/11) — kèm mục cuối file: Part Master đang dùng có NC đầu là máy loại trừ (PO tạo sau sẽ gặp) |

Tự kiểm trên DB giả (Claude, sandbox): `tools/GiaoNhanFuzz` kịch bản `quet` (Q1 PO đúng → 0 phát hiện; Q2 cài lỗi cố ý → bắt đủ 4 loại, không ghi gì; Q3–Q6 các loại thêm sau — Q6 [QUET-PHIEU-CHO] PHIEU_CHO_O_DAT / TRA_VE_NHOM_LA).

Lưu ý PHIEU_CHO_O_DAT chỉ phủ chặn nhận vào ô đạt từ ô Hàng lỗi / Hàng sửa (Gói 16); 2 chặn khác khi bật ON (HTSP chỉ làm Taro hộ; vụ làm hộ vào ô Hàng sửa nhóm khác Gia công) chưa quét — ghi nhớ G21-GHI-NHO.
