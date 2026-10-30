# WtsHarness — chạy CẢ MES.Web trên EF giả, bấm trang WTS thật bằng Playwright, so DB bản cũ / bản mới

Mục đích ([WTS-SVC] 29/10/2026): chứng minh tách phần lưu WTS ra `Services/WtsSaveService.cs` không đổi kết quả ghi DB —
cùng một chuỗi thao tác trên trang `HieuSuat_Index.razor` bản cũ (git HEAD trước khi tách) và bản mới phải cho
WtsProductionLogs / WtsProductionLogStds / WtsExtraItems / NgCases giống hệt nhau, chỉ khác ở bước [RS-E14] (bản mới chặn mã ngoài snapshot).

Không cần SQL Server, không cần NuGet (dùng `tools/GiaoNhanFuzz/EfStub.cs` + stub ở `Stubs/`). Cần: dotnet 8 SDK, python3 + playwright (có chromium).

## Cách chạy (Linux sandbox; Windows đổi đường dẫn tương ứng)
```
# nguồn cũ
mkdir -p /tmp/old_src && git archive <commit cũ> MES.Web | tar -x -C /tmp/old_src
./mk.sh /tmp/old_src/MES.Web  /tmp/harness_old 5100 && (cd /tmp/harness_old && dotnet build -c Release -o bin2)
./mk.sh ../../MES.Web         /tmp/harness_new 5101 && (cd /tmp/harness_new && dotnet build -c Release -o bin2)
./run.sh old /tmp/harness_old      # ghi old.json + ảnh old_*.png
./run.sh new /tmp/harness_new      # ghi new.json + ảnh new_*.png
python3 cmp.py                     # so từng bước: lời báo + 5 bảng DB
```
Kết quả 29/10: 23 bước (GC Hàng loạt / Chạy thử / vượt trần / máy trùng giờ / thiếu giờ / NG không loại lỗi / sửa dòng / K5 / xoá;
HTSP Taro + mã chuẩn bị, Bavia NG, Rửa, vượt trần, sửa; KCS; PKG hết hàng; E14) → 22 bước giống hệt DB + lời báo,
bước E14 khác có chủ đích (bản cũ ghi dòng "XYZ" sổ bỏ qua, bản mới chặn).

`mk.sh` chép MES.Web, bỏ PackageReference, thêm stub, chèn `HarnessSeed` (3 PO cố định, người dùng admin / gc01 / htsp01 / kcs01 / pkg01, mật khẩu 123456 — admin/admin123)
và endpoint `/harness/dump` (JSON các bảng WTS).

## drive_khe.py — trang "Sửa phiếu KH" ([KHE-6][KHE-3][KHE-7] 30/10/2026)
2 người Kế hoạch `khe01` / `khe02` (PIN 1111, thêm cuối HarnessSeed) cùng mở 1 PO: sửa khác ô thì giữ cả 2, cùng ô thì người sau bị báo,
không đụng công tắc Workflow thì không ghi đè, Ghi chú chung có lịch sử ở mọi PO, đổi Workflow có khoá PO.
`python3 drive_khe.py 5101 new` → 30/10: bản mới ĐẠT hết, bản master 70d59ac 7 SAI. (Harness 1 DbContext dùng chung, chạy tuần tự:
không đo được khoá chen nhau thật — phần đó kiểm ở `GiaoNhanFuzz kh` A6 bằng móc OnAppLock.)
