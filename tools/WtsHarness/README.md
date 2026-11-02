# WtsHarness — chạy CẢ MES.Web trên EF giả, bấm trang WTS thật bằng Playwright, so DB bản cũ / bản mới

> **Chỉ chạy trên Linux (Claude kiểm trong sandbox).** `mk.sh` là bash, python3 đọc file không phải UTF-8 — Hùng không cần chạy bộ này trên Windows (Hùng chốt 30/10).

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

## drive_h10.py — huỷ dây chuyền trên trang Giao nhận ([H10] 30/10/2026)
Phiếu KH riêng `KH-H10` / `PO-H10` (thêm CUỐI HarnessSeed, không chung KH-TEST): Kho → Gia công 10, Gia công nhận 8 + NG 2 → phiếu trả tự lập → Kho đã nhận lại 2.
`gn01` (chỉ quyền sửa Gia công) bấm Huỷ lần nhận gốc → bị chặn vì thiếu quyền Kho, DB không đổi; `admin` → 1 lần huỷ cả lần nhận gốc + phiếu trả + lần nhận phiếu trả.
`python3 drive_h10.py 5101 new` → 30/10: bản mới 7/7 ĐẠT; bản master 7479f01 bị chặn "Huỷ lần nhận đó trước" (5/5 đúng như cũ). Dump thêm bảng `gntx` / `gnrec`.

## drive_kho.py — trang Kho ([KHO-5][KHO-LEDGER][KHO-ACTIVE][KHO-VIEW][KHO-STALE][KHO-LEN][E4-KHO-NEG][D11-KHO-SL] 02/11/2026)
Dữ liệu riêng `KH-KHO` (PO-K1…K7, người dùng `khoa01` / `khob01` Xem + Sửa KHO, `khoxem01` chỉ Xem — mật khẩu 123456) dựng khi drive gọi
`POST /harness/kho-seed` (`HarnessSeed.SeedKhoAsync`) — KHÔNG nằm trong seed khởi động (phiếu giao nhận thêm vào seed sẽ đổi `gntx` / `cases` của
`/harness/dump` → đổi new.json của drive.py). `GET /harness/kho` = mọi dòng Kho (cả dòng đã tắt), lịch sử Kho, phiếu từ Kho, sổ KHO từng PO.
Excel: `mk.sh` dùng `Stubs/XlMini.cs` (ClosedXML tối thiểu ghi .xlsx thật, bỏ định dạng) thay `GiaoNhanFuzz/XlStub.cs`; drive đọc file bằng zipfile + xml.
Cần Bootstrap tải sẵn (nút lọc cột chỉ có icon): `KHO_BS` (mặc định /home/claude/bs_shared). Chạy trên harness VỪA khởi động:
`python3 drive_kho.py <port> new` → 02/11: HEAD ĐẠT hết (36 bước); luật máy chủ cùng mã kiểm thêm ở `GiaoNhanFuzz kh` A11–A15.

## drive_ml9.py / drive_std.py — trang Part Master ([ML9-DUP][PM-DUP][B6-NC-DUP][SUBROW-DEL][CONFIRM-LOG-FIELD][STD-CONFIRM][SAVE-HANG-FIX][STD-UPDATE-V4][STD-5] 02/11/2026)
Dữ liệu thêm CUỐI HarnessSeed (`SeedPmAsync`): PART-ML9 (NC 2 có dữ liệu cũ trùng máy, Rửa R1 trùng cũ) và PART-STD + phiếu KH-STD (2 PO, snapshot chép bằng
`KhPlanService.CopyRouteSnapshotAsync` thật), người `stdadm` (ADMIN, PIN 1111). `Dump()` cũ lọc bỏ dữ liệu này (drive.py ra new.json giống hệt bản trước khi thêm);
đọc qua `/harness/dump2` (`Dump2()`, mk.sh thêm endpoint). Mỗi script chạy trên harness mới khởi động (DB giả mới tinh):
`python3 drive_ml9.py 5101 new` (pha D / P / M / X / B / C / H, mỗi pha mở trang mới — pha dừng giữa chừng ghi "<pha>! SAI") ·
`python3 drive_std.py 5101 new` (V / U / F / R). Phần máy chủ của Update TG chuẩn kiểm ở `GiaoNhanFuzz std`. Không kiểm Taro trùng mã CV (khách chốt Taro
dùng chung mã CV giữa 2 NC — chờ sửa) và không kiểm [TIMING-COPY-MAIN] (chờ chốt RA-CASCADE-PM).

## drive_rsc5.py / drive_okfb.py / drive_rse11.py ([RS-C5][WTS-OK-FALLBACK][RS-E11] 02/11/2026)
- `python3 drive_rsc5.py <port> new` — harness đang chạy: "Nhớ tên đăng nhập" chỉ lưu tên, mật khẩu bản cũ đã lưu bị xoá khi mở trang.
- `python3 drive_okfb.py <port> <bin harness> new` — TỰ bật harness với `HARNESS_OKF=1` (HarnessSeed thêm PO-OKF + 1 dòng WTS cũ hôm nay QtyOk NULL;
  không có biến này thì seed y cũ) rồi tự tắt: Sửa dòng cũ → form OK = QtyDone − NG, lưu ra OK 8 / NG 2.
- `python3 drive_rse11.py <port> <bin harness> new` — TỰ bật harness (Production) trong mount namespace riêng (`unshare -m`, cần root) với thư mục
  bản quyền tạm hỏng → bản quyền khoá; kiểm chỉ đúng `/about` qua khoá, DetailedErrors tắt, cookie HttpOnly + Secure sau proxy. Thư mục bản quyền chung không bị đụng.
- Hai file tự bật harness dùng `hk_launch.py` (giết theo pid của chính nó); cổng phải trống trước khi chạy.
