# CODEBASE-MAP — MES Nhật Phát

Bản tóm tắt từ "Bản đồ phần mềm MES Nhật Phát" (Claude Docs, tài liệu sống) — soạn lại 15/11/2026 từ bản đồ dựng trên master d756582 (Gói 21). Khi có claude.ai, đọc bản đồ gốc (link https://claude.ai/artifact/JaQ9u85zhjnDqCxzQEtVvB); khi không, đọc file này. Không chứa mật khẩu / PIN / chuỗi kết nối / tài khoản test.

Viết tắt: HLS = `Services/HandoverLedgerService.cs`, HS = `HandoverService.cs`, HF = `HandoverForm.cs`, WSS = `WtsSaveService.cs`, KPS = `KhPlanService.cs`, GXL = `HandoverService.GiaoXuLy.cs`; `HLS:123` = dòng theo d756582. Trong bảng, ∣ thay cho `|` của khoá chuỗi trong code.

## 0. Đọc trước

**Phần mềm làm gì.** MES Nhật Phát (Blazor Server .NET 8, EF Core, SQL Server 2016, DB `MES`) theo dõi từng PO (dòng `KhPlanDetails`) của xưởng gia công chi tiết máy: Kế hoạch tạo PO + chụp quy trình từ Part Master (snapshot NC / mã CV); Kho nhận phôi (`KhoVatLieus.SoLuongThucNhan`); hàng đi Gia công (theo NC) → HTSP (Taro / Bavia / Rửa theo mã CV) → KCS → Đóng gói → Kho, mỗi bước là phiếu giao (`HandoverTransactions`) + lần nhận (`HandoverReceives`); công nhân ghi WTS (`WtsProductionLogs`: OK / NG / giờ) → hiệu suất người và máy; hàng lỗi đi theo vụ lỗi (`NgCases`, VL000001) qua trả lỗi / gởi sửa / Kết quả sửa + KCS xác nhận / huỷ / trả khách / khách gửi bù. **Mọi con số số lượng trên mọi màn hình đều tính lại từ chứng từ gốc bởi một hàm duy nhất `HandoverLedgerService.Compute` (sổ chung); không bảng nào lưu số cộng dồn.** Mỗi PO phải cân: phôi + bù = Σ Tồn 5 nhóm + đang giao + TP nhập kho + đã huỷ + đã trả khách (`LedgerRow.Balanced`, L6).

**Thứ tự nguồn tin (Hùng chốt 14/11/2026).** (1) Code ở master mới nhất = chuẩn "đang làm gì". (2) Luật đã chốt = chuẩn "phải làm gì": CLAUDE-RULES.md mục 0; memory `ways-of-working`, `areas/wts-service-va-kiem-chuoi` (Bàn giao mới nhất), `areas/giao-nhan-so-luat`, `architecture-decisions`; Sổ luật Giao nhận MES (Claude Docs QWeWAMmVXgF2LaLKGSzLpR: tab chính L1–L7 / N1–N16 / K1–K8, tab "Lỗi cần sửa" = Tổng hợp còn mở + Đã làm); doc khách đã trả lời. (3) Tài liệu cũ (BOOTSTRAP.md, CLAUDE-RULES mục 1–10, file-tree.txt, README / SETUP / *_SPEC*.md, CODEBASE-MAP cũ 03/09) chỉ tham khảo sau khi đối chiếu code. Code lệch luật = lỗi hoặc "chờ Hùng quyết", không sửa luật cho khớp code.

**Tám luật không được phá:**
1. **Một nguồn số**: mọi số gọi `HandoverLedgerService.Compute` / `ComputeSteps`; ô mới = trường `LedgerGroup` / `LedgerRow` + công thức trong `Compute`.
2. **Mọi ghi số lượng qua một khuôn**: validate → `BeginTransactionAsync()` → `sp_getapplock "MES_PO_<id>"` (LockOwner=Transaction, 10 s, −3 → THROW 51205) → `ChangeTracker.Clear()` → đọc sổ lại → kiểm luật server → ghi → `CheckNoNewNegativeAsync` (K5/K8) → Commit → `HandoverChangeNotifier.Notify`. Deadlock 1205/51205 thử lại 3 lần (`RetryDeadlockAsync`). Không isolation level riêng (ISO-FIX).
3. **Thứ tự khoá cố định**: WTS `WTS_WORKER_<uid>` → `WTS_MACHINE_<máy>` → `MES_PO_<id>` (tăng dần) → `MES_KH_NUMBERS`; Part Master `MES_PM_STEPS_<PartId>` / `MES_PM_SAVE_<Part>`.
4. **Quyền kiểm ở server**: service ghi gọi `PermCheck` (`CanEditAsync` / `HandoverEditGroupsAsync` / `WtsProcessGroupsAsync` / `DenyGroupAsync`); ẩn nút razor chỉ là tiện.
5. **Không xoá chứng từ, chỉ huỷ**: cờ `IsVoided` / bản "Sửa lại" `EditedFromId`; sổ đọc cả bản void để tra vết (`EditOriginsFull`).
6. **Quy trình đóng tại PO (snapshot)**: chỉ PO "chưa chạy" (`RouteRefreshBlockAsync` không chặn) mới được chép lại.
7. **Không đoán, không sửa luật cho khớp code**: lệch → ghi "Lệch luật ↔ code — chờ Hùng quyết" + file/hàm/dòng + ca tái hiện.
8. **Bí mật không vào tài liệu công khai**: chỉ ghi "xem memory ways-of-working".

**Quy trình một gói.** Đọc trước (CLAUDE-RULES mục 0 → memory Bàn giao → bản đồ; `git log -1` ở `/home/claude/mes` phải trùng MỐC) → hiểu luật (Sổ luật + mục 6; chưa có → đề xuất Hùng chốt) → sửa (giữ EOL từng file 8.3) → kiểm (`dotnet build` 0 warning mới → `tools/GiaoNhanFuzz` 18 kịch bản + nang + danhmuc → `tools/WtsHarness` chỉ Linux → `tools/QuetSo` nếu đụng dữ liệu thật; ghi Sổ luật phần Đã làm) → đẩy (Hùng chạy `claude_day_git.bat` / `chay_app_5000.bat`; trạm `claude_tram.bat` nhận `kiemgap|kiem|quet|build|tong|danhmuc` qua `claude_lenh.txt`; `kiem` trong giờ khách test hỏi Hùng hoặc sau 17:00) → bàn giao (memory Bàn giao, project doc `claude/luu-tru/<ngày>/`, cập nhật bản đồ).

## 1. Kiến trúc & luồng

- **Công nghệ**: .NET 8 Blazor Server (`InteractiveServer`, `Routes` SSR), EF Core 8.0.10 SqlServer, ClosedXML 0.104. Một project `MES.Web` trong `MES.sln`; `tools/*` không vào sln.
- **DB**: SQL Server 2016, DB `MES`, máy chủ nội bộ (xem memory ways-of-working), RCSI bật. Connection string trong `appsettings.json` (có mật khẩu thực — RS-A3 còn mở).
- **Chạy**: Kestrel `http://0.0.0.0:5000`; trạm Hùng `chay_app_5000.bat`; khách test qua Cloudflare Tunnel → `UseForwardedHeaders`. `DisconnectedCircuitRetentionPeriod` 30 phút khớp `Blazor.start` reconnect 600×3 s.
- **Khởi động** (`Program.cs:113`): `Database.EnsureCreated()` + `SeedData.EnsureSeeded` — EnsureCreated **không** thêm bảng/cột khi DB đã có; đổi schema = script tay `tools/sql/06..15`. Seed chỉ khi bảng trống: 7 nhóm, 4 tài khoản mẫu, 3 khách, 5 Part.
- **Hosted service**: `LicenseBackgroundWorker` + `ServerLicenseHelper` kiểm bản quyền máy chủ; `MainLayout` khoá app nếu không Active. Chưa có tài liệu — hỏi Hùng.

**Ba tầng**: Trang `Components/Pages/<module>/*.razor` (28) + Shared (7) + Layout — nhận số từ LedgerRow, chỉ cảnh báo realtime → `Services/*.cs` (48 file, Scoped) — PermCheck, khoá PO, luật, ghi; `Compute` = nguồn số duy nhất → `Data/AppDbContext.cs` (69 DbSet) + `Data/Entities` (54) → SQL Server. Ngoại lệ trang ghi thẳng DbContext: `Detail_Partmaster.razor` 12 chỗ, `Create_Khplan.razor` 1 chỗ.

**Xác thực**: Cookie `MES.Auth` 8 giờ sliding; `/login-submit` (POST, `LoginThrottle` 5 sai/15 phút theo tên+IP, 50/IP), `/logout` (GET). Mật khẩu PBKDF2-SHA256 100 000 vòng (`PasswordHasher`). PIN = `Users.PIN` **plain text** (`AuthService.VerifyPinAsync`). Claims: NameIdentifier, Name, FullName, Role = GroupCode; **claim `GroupCode` không bao giờ được phát**. `AuthGuard.IsStillValidAsync` mỗi request; `MesRevalidatingAuthStateProvider` 1 phút + `AuthWatch`. Quyền động JSON `UserGroups.Permissions`, 28 mã `Constants/SystemPermissions.cs`, chốt qua `PermCheck`.

**Luồng**: Part Master (A–E) → Kế hoạch tạo PO + snapshot → Kho nhập phôi → Giao nhận Kho→GC→HTSP→KCS→PKG→Kho → WTS → Báo cáo (`WtsReportService`, `MachineEfficiencyService`) → Update TG chuẩn (`StdUpdateService`, PIN). Danh mục: Khách hàng, Thiết bị, Đồ gá, Dao, Sơ đồ tổ chức, Ca làm việc, WTS tiêu chuẩn + ánh xạ, DS lỗi. **Thông báo giữa phiên**: `HandoverChangeNotifier` sự kiện tĩnh trong tiến trình; hàm ghi gọi `Notify(poId)` sau Commit; chỉ `Handover_Index` nghe (GN-LIVE) + Timer 2 phút. **Repo**: `MES.sln`, `MES.Web/`, `tools/` (GiaoNhanFuzz, WtsHarness, QuetSo, SqlDongThoi, BackfillNghi, sql/06..15, DanhMucKiem.txt, 4 .bat). Rác trong git: `HieuSuat_Index_Old.razor`, `HieuSuat_Index_base.razor`, *.rar, *.bak, `bin2`, `nang_ketqua`, `ketqua_*`.

## 2. Mô-đun → trang → service → bảng

Thư mục `MES.Web/Components/Pages/<module>/`, `Components/Shared/`, `Components/Layout/`; dịch vụ `MES.Web/Services/`. **Perm-DB** = `UserSvc.GetAllUserPermissionsAsync(uid)` rồi tra `SystemPermissions.X`; **Claim-ADMIN** = nhánh tin claim ADMIN trước khi đọc DB (còn sót); **Ledger** = đọc `HandoverLedgerService`.

| Trang (file) | Route | Quyền | Ghi qua service | Đọc số / ghi chú |
|---|---|---|---|---|
| Login.razor | /login | AllowAnonymous, POST `/login-submit` | — | SSR tĩnh |
| Home.razor | / | Authorize | — | card không theo quyền, số cứng cũ |
| Profile.razor | /profile | Authorize | `AuthService.ChangePasswordAsync`, `SetPinAsync` | |
| About/Index.razor | /about | Authorize | `ServerLicenseHelper.CheckLicenseAsync` | bản quyền |
| KhPlan/KhPlanIndex.razor | /kh-plan | Claim-ADMIN rồi Perm-DB KE_HOACH | `KhPlanService.DeleteDetailAsync`; SplitPOModal → `SplitPOAsync` | Ledger `GetLedgerForDetailsAsync`, `WhereIs`, `GetStepsForDetailAsync`, `GetNgTraceAsync`; xoá PO xác nhận bằng **mật khẩu** dù nhãn PIN |
| KhPlan/Create_Khplan.razor | /kh-plan/create | Perm-DB KE_HOACH CanEdit | `KhPlanService.CreateAsync` + `CopyRouteSnapshotAsync`, `PartMasterService.AddValueAsync`, `CustomerService.CreateAsync`; **trang tự ghi** `PartMasters.IsPlanConfirmed` | |
| KhPlan/Edit_Khplan.razor | /kh-plan/{Id}/detail/{DetailId}/edit | Perm-DB KE_HOACH CanEdit | `KhPlanService.SaveEditAsync`, `RefreshRouteSnapshotAsync` | Ledger `WhereIs` khoá ô khi PO đã chạy (KH-GUARD); PinConfirmModal |
| KhPlan/View_Khplan.razor | /kh-plan/{Id}/detail/{DetailId} | Authorize **chỉ vậy** | — | Ledger `GetLedgerForDetailAsync`, `GetStepsForDetailAsync`, `WhereIs` |
| KhPlan/BulkUpdateStd.razor | /kh-plan/bulk-update-std | Perm-DB KE_HOACH CanEdit | `KhPlanService.BulkUpdateStdAsync` | upload .xlsx |
| Kho/Kho_Index.razor | /kho | Claim-ADMIN rồi Perm-DB KHO | `KhoVatLieuService.SaveKhoInputAsync` | Ledger `GetLedgerForDetailsAsync`; `HandoverService.GetKhoIssueSummaryAsync`; sửa kho xác nhận bằng **mật khẩu** dù nhãn PIN |
| Handover/Handover_Index.razor | /handover | **Không có `[Authorize]`** (tự redirect); Perm-DB 5 mục HANDOVER_* | `HandoverService.ReceiveAsync`, `ConfirmRepairAsync`, 7 kiểu `Void*`, `CloseRemainderAsync` | Ledger `GetLedgerAsync`, `GetLedgerForDetailAsync`, `GetStepsForDetailAsync`, `GetNgTraceAsync`; `GetPending*CountPerDetailAsync`; nghe Notifier → `LiveApplyAsync` |
| Handover/GiaoXuLyModal.razor | (component) | nhận `Groups` từ cha; server kiểm lại | `HandoverService.SaveFormAsync` (form 4 bước GN-FORM1), `ReceiveReplacementAsync` | `HandoverForm.FormPositions`, `IssueCap`, `BuCap` |
| HieuSuatCongNhan/HieuSuat_Index.razor | /hieu-suat-cong-nhan | Authorize; `_canSubmit` = **CanView** WtsGc/Htsp/Kcs/DongGoi hoặc ADMIN (DB); `_canViewReport` = WTS_REPORT | `WtsSaveService.SaveGroupAsync`, `VoidRowAsync`, `SaveActivityAsync` | Ledger `GetLedgerForDetailAsync(id, EditingLogIds)` → `WtsCap`; `WtsReportService.GetReportAsync`; 8.701 dòng; inject Db chỉ đọc |
| HieuSuatMay/HieuSuatMay_Index.razor | /hieu-suat-may | Authorize **chỉ vậy** | — | `MachineEfficiencyService.GetAsync`, `CaLamViecService.GetActiveAsync` |
| PartMaster/Index_PartMaster.razor | /part-master | Claim-ADMIN; `_canView` = bất kỳ quyền xem | — | `PartMasterListService` |
| PartMaster/Create.razor | /part-master/create | Claim-ADMIN rồi PART_TAO_MOI CanEdit | `PartMasterService.CreatePartMasterAsync` | |
| PartMaster/Detail_Partmaster.razor | /part-master/detail?partNo= | Claim-ADMIN rồi PART_TAO_MOI/GC/HTSP/KCS/DONGGOI, PART_STD_UPDATE | 6 Part*Service, `PartStepStdTimeConfirmService`, `StdUpdateService.RunAsync`, `KhPlanService.AutoRefreshRoutesForPartAsync`; **+12 `Db.SaveChangesAsync` trực tiếp** | Ledger `GetLedgerForDetailsAsync` + `WhereIs().Status=="DU"`; 4.848 dòng; khoá `PartStepGuard.AcquireSaveLockAsync` |
| PartMaster/History.razor | /part-master/history | Authorize chỉ vậy | — | `Db.PartProcessStepChangeLogs` |
| Setting/Settings_Index.razor | /settings | `UserService.IsAdminAsync` | `UserService.Create/UpdateUser`, `ResetPin`, `Create/UpdateGroup`, `UpdateAllGroupPermissionsAsync` | ma trận `SystemPermissions.Sections` |
| Calamviec/CaLamViec_Setting.razor | /cai-dat/ca-lam-viec | Perm-DB CA_LAM_VIEC | `CaLamViecService.Save/ToggleActive/Delete` | |
| Customer/Index.razor | /customers | Perm-DB KHACH_HANG | `CustomerService` | |
| Dao/Index.razor | /dao | Perm-DB DAO | `DaoService` | |
| DoGa/Index.razor | /do-ga | Perm-DB DO_GA | `DoGaService` (+PIN qua service) | mượn/trả |
| Thietbi/Index.razor | /thiet-bi | Perm-DB THIET_BI | `ThietBiService` (+PIN) | sự cố dừng máy |
| SoDoToChuc/Index.razor | /so-do-to-chuc | `DepartmentService.CanView/CanEditAsync` → PermCheck SO_DO_TC | `DepartmentService` (+PIN), `SaveAvatarAsync` | upload ảnh |
| WTStieuchuan/Index_WTS.razor | /wts-tieu-chuan | Perm-DB WTS_MASTER + WTS_MAPPING | `StandardWtsTaskService` (+PIN), `WtsCategoryGroupMappingService.SaveGroupMappingAsync` | `HandoverLedgerService.IsPrepCode`; bộ gộp 3T |
| DsLoi/DsLoi_Index.razor | /ds-loi | Perm-DB DS_LOI | `NgCaseService.SaveCategory/SetActive/DeleteCategory` | |
| Error.razor | /error | AllowAnonymous | — | |

**Shared / Layout**: `App.razor` Bootstrap 5.3.3 từ CDN (offline mất CSS); `Routes.razor` chỉ chặn trang có `[Authorize]`; `MainLayout` NavMenu SSR tĩnh (`isAdmin` vẫn Claim-ADMIN), khoá app khi license ≠ Active; Shared `PinConfirmModal`, `SplitPOModal`, `PoViTri` (`WhereIs`), `PoLoi` (`NgTrace`), `NgCategoryPicker`, `AttributePicker`.

**Dịch vụ**: Sổ chung `HandoverLedgerService` (4015 dòng), `HandoverForm`, `HandoverNgTrace`, `HandoverWhy` (GN-WHY), `LedgerQtyText` — tính, không ghi · Giao nhận ghi `HandoverService` (2277) + partial `GiaoXuLy`, `KetQuaSua`, `KhachBu`; `HandoverChangeNotifier` · WTS `WtsSaveService` (1621), `WorkMinutesService`, `WtsReportService`, `MachineEfficiencyService`, `StandardWtsTaskService`, `WtsCategoryGroupMappingService` · Kế hoạch/Kho `KhPlanService` (2071), `KhoVatLieuService`, `StdUpdateService` · Part Master `PartMasterService`, `PartMasterListService`, `PartMachiningService`, `PartTaroService`, `PartBaviaService`, `PartWashingService`, `PartInspectionService`, `PartPackagingService`, `PartStepStdTimeConfirmService`, `PartStepGuard` + `PmSaveLockSql`, `PartMasterPermissionHelper` · Danh mục `CustomerService`, `DaoService`, `DoGaService`, `ThietBiService`, `DepartmentService`, `CaLamViecService`, `NgCaseService` · Bảo vệ `PermCheck`, `PoGuard`, `QtyRule`, `AuthService`, `AuthGuard`, `PasswordHasher`, `UserService` · Khác `ServerLicenseHelper`, `LicenseBackgroundWorker`, `ExcelHelper`.

**Điểm lạ**: Handover_Index thiếu `[Authorize]`; "PIN" = mật khẩu ở KhPlanIndex/Kho_Index; Claim-ADMIN còn ở NavMenu, KhPlanIndex, Kho_Index, Index_PartMaster, PartMaster/Create, Detail_Partmaster; 13 chỗ trang ghi DB trực tiếp; HieuSuatMay/View_Khplan/History chỉ Authorize.

## 3. Luồng số + sổ chung HandoverLedgerService

**Đường đi PO**: [KHO] phôi + bù (`HandoverReplacements`) → NEW KHO→GC → nhận → [GC] WTS theo NC 1..n (lỗi → vụ NgCase) → NEW To=HTSP (chỉ OK NC cuối) → [HTSP] WTS theo mã CV (Taro 1T / Bavia 1V / Rửa 1R; bộ gộp 3T; Kho có thể giao thẳng Taro HELP_SEND FromNC=@VAO) → [KCS] kiểm + xác nhận Kết quả sửa (`HandoverRepairChecks`) → [PKG] → NEW To=KHO (TpNhap) → huỷ (SCRAP → "HUY"), trả khách (CUST_RETURN → "KHACH"). Mũi tên lỗi: `NG_RETURN`, `REPAIR_SEND` → `REPAIR_RETURN`, `HELP_SEND` → `HELP_RETURN`, `SCRAP`, `CUST_RETURN` (enum `HandoverPurpose`); `HandoverNgSource` PHOI / CHUA / DA, DAU_VAO / DAU_RA. Hằng `VaoCode="@VAO"`, `RepXong="@XONG"`, `RepOk="@OK"`; `PrepCodes` {1V,1T,1R,1K,1D,1G} = mã chuẩn bị, không số lượng.

**Đầu vào**: `LoadInputsAsync(poId)` đọc toàn bộ chứng từ gốc vào `LedgerInput`: `KhoVatLieus`, `HandoverTransactions` + `HandoverReceives`, `HandoverTxCloses`, `HandoverReplacements`, `WtsProductionLogs` (kể cả void), `HandoverNgReports`, `HandoverRepairResults` + `HandoverRepairChecks`, `NgCases` + `NgCategories`, 6 bảng snapshot. `Compute(input)` → `LedgerRow` gồm 5 `LedgerGroup` (KHO, GC, HTSP, KCS, PKG):

| Ô | Nghĩa | Công thức trong `Compute` |
|---|---|---|
| Nhận / Giao MOI/LOI/SUA/XL | theo ô (Giao tính cả phiếu chờ nhận) | Σ `HandoverReceives` theo `ToCell` / Σ `HandoverTransactions` chưa void theo `FromCell` |
| Chưa làm | hàng chờ làm (= trần WTS L3) | `NhanMoi + NhanLoiXl + nhRet − DaLam − NgCuoi − glTra − ghSend − blChua − nxGiu` |
| Đã làm | GC: OK NC cuối (+ okSua); HTSP/KCS/PKG: min OK mã CV chính (3T = OK gộp + min thành phần) | `ComputeGcOk` / `ComputeCodeOk` / `TaroGopUnits` |
| Còn giao | đã làm chưa giao (= trần phiếu L4) | `DaLam − GiaoMoi − blDa` (Kho: `NhanMoi + NhanLoiXl − GiaoMoi − glTra − blDa`) |
| Lỗi giữ / Sửa giữ / XlGiu / LamHoGiu | lỗi, hàng sửa, lỗi đã xử lý, làm hộ đang nằm | `NhanLoi (+NgCuoi) + bl − glGui − glTu − glChuyen − glHuy − glKhach` / `NhanSua − GiaoSua − glSuaNg` / `nxGiu − gx` / `nhSend − ghRet − glLamHo − glLhKho` |
| Chờ nhận / Chờ KCS | đã giao chưa nhận / chờ KCS xác nhận sửa | tx chưa có receive / `HandoverRepairResults` chưa check |
| **Tồn** | | `NhanTong − GiaoTong + ChoNhan + ChoKcs` |

**Cân PO** (`LedgerRow.Balanced`, L6): `Phoi + Bu == TonTong + DangGiao + TpNhap + DaHuy + TraKhach`; lệch = chứng từ hoặc công thức sai, không "làm tròn". Tiền tố khoá tích luỹ: n=nhận, g=giao, gl=giao lỗi, bl=báo lỗi sau nhận, rr=kết quả sửa, `:NC` theo nguyên công. `ComputeSteps` → `LedgerStepLine` từng NC (dòng B1..Bn, `@VAO`; `BuildGcSteps` HLS:3002–3118: `In` = OK NC trước + okSua − HelpOut (+ in:NC − rrNc:NC − blNc:NC), `Waiting = In − OK − OkSua − NG`; `Waiting < 0` → cảnh báo "WTS nhập vượt") hoặc từng mã CV (`FillCodeWaiting` / `FillWaitLines` / `FillLamHoWaiting`); `LedgerCase` = một vụ lỗi (`WhereIs`); `LedgerLamHoRun` = một lượt Taro làm hộ.

**Chỗ ghi số** (đều theo khuôn mục 0 luật 2): khoá PO `HandoverService.LockPoAsync`, `PoGuard.LockAsync` / `TryLockInTransactionAsync`; thử lại `RetryDeadlockAsync` (`IsDeadlock`) · **Giao** `IssueByCellAsync` → `HandoverCell.ResolveCells` → `IssueWithPurposeAsync` → `IssueWithPurposeOnceAsync` (thứ tự kiểm: khoTaro → `QtyRule` → `DenyGroupAsync` → `CheckRoute` → lỗi bắt buộc `NgCategory` + vụ, K3, N3-TONC, GN-NC-PIN, GN-NGUOC-CHAN, KHO-NC-DAU → vụ: `TakesHeldCase`, `IssueCapCase`, LAMHO khi ON, XL-VUOT-CHAN, SUA-QUA-KCS → `IssueCap` → tạo `NgCase` → Add → K8 → Commit → Notify) · **Nhận / huỷ** `ReceiveAsync`→`ReceiveOnceAsync`, `VoidReceiveAsync`, `VoidIssueAsync` (`VoidRightsAsync` đọc DB) · **Báo lỗi sau nhận** `ReportNgAfterReceiveAsync` / `VoidNgReportAsync` · **Đóng dư** `CloseRemainderAsync` → `HandoverTxCloses` · **Form** GXL `SaveFormAsync` · **Kết quả sửa** `KetQuaSua.cs` `CreateRepairResultAsync`, `ConfirmRepairAsync` (KCS); `RepairCap` · **Khách bù** `KhachBu.cs` `ReceiveReplacementAsync`, `VoidReplacementAsync`; `BuCap` · **WTS** `WtsSaveService.SaveGroupAsync` → `SaveGroupOnceAsync` → `SaveRowAsync` (`CheckActivityOverlap`, `CheckMachineOverlap`, `VerifyBreaks`, `CheckGroupQtyAsync` trần `WtsCap`) · **Phôi** `KhoVatLieuService` · **Kế hoạch** `KhPlanService` (`KhNumberLock="MES_KH_NUMBERS"`, KH-GUARD) · **Part Master** `PartStepGuard` + `PmSaveLockSql.OpenAsync`. Hàm trần trong HLS: `WtsCap`, `IssueCap` / `IssueCapCase`, `BuCap`, `RepairCap`, `NgReportCap`, `XlDest` / `RepairDest`, `CheckRoute`; K5 `NewNegatives` / `NewNegativeKeys`, theo thời gian `SnapshotBefore` + `LoopTimeline` + `NewLoopKeys`. **Chỗ đọc số**: `GetLedgerForDetailAsync`, `GetLedgerAsync`, `GetLedgerForDetailsAsync` (3 đường nạp), `WhereIs`, `GetStepsForDetailAsync`, `GetNgTraceAsync`.

**K6-NOTRUN**: `KhPlanService.CopyRouteSnapshotAsync` chép quy trình lúc tạo PO. Part Master "Lưu & Xác nhận OK" → `AutoRefreshRoutesForPartAsync` → `RouteRefreshBlockAsync(poId)`: chép lại chỉ khi "chưa chạy" = không WTS, không báo lỗi, không Kết quả sửa, phiếu chỉ Kho→GC NEW hoặc Kho giao thẳng Taro, lần nhận chỉ ô MOI, ToNC vẫn NC chính; rồi `GcWaitingMovedAsync`. PO đã chạy cần đổi quy trình = việc tay Hùng quyết.

**Dễ hiểu sai**: "Đã làm" GC = OK **NC cuối**, HTSP/KCS/PKG = **min** OK mã chính · WTS void vẫn load nhưng không cộng (`Classify` lọc `IsVoided` / `EditedFromId`) · `ChoNhan` cộng vào Tồn **bên giao** · Workflow OFF chỉ cảnh báo ở WTS, bỏ K5/K8/LAMHO ở giao nhận (khách đang ON) · trang có `Sum` chỉ để hiện · không sửa `Compute` cho một màn hình; đổi phải có ca GiaoNhanFuzz trước.

## 4. Dữ liệu

**Schema**: không EF Migrations; `EnsureCreated()` chỉ khi DB chưa có; đổi schema = SQL tay (`USE [MES]; GO`, idempotent, không `CREATE OR ALTER`). Seed: 7 nhóm (ADMIN, PLANNING, SUPPLIER, WAREHOUSE, PRODUCTION, QC, VIEWER), 4 tài khoản mẫu (xem memory), 3 khách, 5 Part. DB trống dựng từ EF sẽ **thiếu** 5 UX `UX_Part*Steps_PartId_NC_Main` (SQL 09), CHECK của SQL 06/08/10/15, FK `HandoverRepairResults→NgCases`; **thừa** FK `KhPlanDetailChangeLogs→KhPlanDetails` → máy khách dựng bằng backup/restore (RS-A2).

| Mô-đun | Bảng (cột khoá) | Kiểu ghi |
|---|---|---|
| Người dùng | `Users` (PIN plain, UX Username), `UserGroups` (JSON `Permissions`, UX GroupCode) | master UPDATE |
| Part Master | `PartMasters` (5 cờ Is*Confirmed, IsObsolete), `PartMasterAttributes`, `PartMachiningSteps`, `PartTaroSteps` (`WtsTaskCode`), `PartBaviaSteps`, `PartWashingSteps`, `PartInspectionSteps`, `PartPackagingSteps`, `PartMachiningTimings` (**`SoMay` CSV — ngoại lệ duy nhất**), `PartMachiningTimingConfirms`, `PartStepStdTimeConfirms`, `PartProcessStepChangeLogs` (JSON), `MachineExcludeSettings` | bước xoá cứng + log JSON; 5 UX `(PartId, NC) WHERE IsActive=1 AND IsBackup=0` không khai trong EF |
| Kế hoạch | `KhPlans` (`ApplyWorkflow` default 1), `KhPlanDetails` (gốc mọi chứng từ; `PartNo` chuỗi không FK), `KhPlanDetailChangeLogs`, `KhPlanDeletedDetails` (SQL 11), 7 bảng `KhPlanRouteSnapshot*` (Machining, MachiningTimings, Taro, Bavia, Washing, Inspection, Packaging, MachineExclude) + `KhPlanRouteSnapshotWtsGops` (SQL 07) + `KhPlanRouteSnapshotStdConfirms` | snapshot: raw DELETE theo PO rồi ghi lại |
| Kho | `KhoVatLieus` (`PlanDetailId long?` **không FK**; xoá PO chỉ `IsActive=0`), `KhoVatLieuChangeLogs`; `StockTransactions` **chết** | UPDATE |
| Giao nhận | `HandoverTransactions` (Purpose, FromCell, ToCell, FromNC, ToNC, NgCaseId, Status PENDING/PARTIAL/COMPLETED, QtyClosed, IsVoided), `HandoverReceives` (ToCell, ToNC SQL 14, NgCaseId), `HandoverNgReports`, `HandoverRepairResults` + `HandoverRepairChecks` (SQL 08), `HandoverTxCloses` (SQL 10), `HandoverReplacements` (SQL 15) | **append-only, huỷ = IsVoided**; ngoại lệ `HandoverTransactions` UPDATE Status/QtyIssued/QtyClosed |
| Vụ lỗi | `NgCategories` + `NgCategoryChangeLogs`, `NgCases` (`CaseNo` cột tính PERSISTED 'VL'+6 số SQL 12; không lưu số lượng) | append-only |
| WTS | `WtsProductionLogs` (ProcessGroup, NC, WtsCode, StepNC SQL 07, HtspSubGroup, QtyDone/Ok/Ng, 5 cột *_Actual, CaId, NgCaseId, IsVoided, EditedFromId), `WorkerActivityLogs`, `WtsBreakWorkLogs` (SQL 06), `WtsExtraItems`, `WtsProductionLogStds`, `StdUpdateBatches/Details`, `StandardWtsTasks` + ChangeLogs, `StandardWtsTaskGops` (SQL 07), `WtsCategoryGroupMappings` | append-only, void = raw UPDATE có điều kiện WorkerId |
| Danh mục | `Customers`, `ThietBis`, `DoGas` + `DoGaMuonTraLogs`, `Daos`, `Departments`, `CaLamViecs` + `CaLamViec_NghiGiaiLaos` + `CaLamViecChangeLogs` (SQL 13), mọi `*ChangeLogs` | master + log append |
| Chết | `StockTransactions`, `FileAttachments`, `FileStorages` | ứng viên dọn (hỏi Hùng) |

**Luật dữ liệu**: chứng từ + ChangeLog chỉ ghi thêm (`IsVoided` + `VoidReason/By/At`; WTS `EditedFromId`) · không CSV cho 1-N trừ `PartMachiningTimings.SoMay` · xoá cứng có chủ đích: Part*Steps (+JSON log), PO trọn (chụp `KhPlanDeletedDetails`), snapshot theo PO · raw SQL ngoài EF chỉ ở KPS, WSS (UPDATE void), `sp_getapplock`.

**SQL `tools/sql/`** (đều đã chạy trên MES): 06_ML17XN_WtsBreakWorkLogs (01/11) · 07_TARO_NC_3TGOP (06/11) · 08_KQSUA_HandoverRepair (04/11) · 09_PM_T4_UniqueNC (05/11) · 10_K5CLOSE_HandoverTxCloses · 11_KHE5_KhPlanDeletedDetails · 12_VL9_NgCaseNo · 13_CA_CaLamViecChangeLogs · 14_NHANNC_HandoverReceives_ToNC (06/11) · 15_KHBU_HandoverReplacements (11/11). Header "CHƯA CHẠY"/"NHÁP" chỉ là chữ lúc soạn. **SQL 01–05 và migration trước 22/10 chưa từng có trong git** → ~25 bảng, ~45 cột không có script trong repo.

**EF ≠ DB thực (9 điểm)**: 5 UX SQL 09; CHECK SQL 06/08/10/15; FK HandoverRepairResults→NgCases chỉ ở DB; INCLUDE 2 UX WtsBreakWorkLogs; FK KhPlanDetailChangeLogs EF có DB đã DROP (KPS:672); FK quy ước → Users CASCADE nếu EF dựng; UX_NgCases_CaseNo đã khớp; precision `PartMachiningTimings` 5 cột giờ + `WtsProductionLogs.QtyOk/QtyNg` (18,2) vs (10,2); `HandoverTransactions.ToNC/ToCell` nvarchar(max), `KhPlanRouteSnapshotTaro.WtsTaskCode` 50 vs `PartTaroSteps` 20. Câu SQL chỉ đọc đối chiếu: bản đồ gốc tab 4 mục 4.6.

## 5. Quyền

**28 mã** (`SystemPermissions`): KE_HOACH · PART_TAO_MOI (duy nhất dùng CanDelete = Ngưng Part) · PART_GC · PART_HTSP · PART_KCS · PART_DONGGOI · PART_STD_UPDATE · KHO · SO_DO_TC · KHACH_HANG · THIET_BI · DO_GA · DAO · WTS_MASTER · WTS_MAPPING · DS_LOI · WTS_GC / WTS_HTSP / WTS_KCS / WTS_DONG_GOI (nhập WTS — **CanView**) · WTS_REPORT · HANDOVER_KHO / GC / HTSP / KCS / PKG (giao nhận — **CanEdit**) · CA_LAM_VIEC · SETTING_ADMIN. Mã công đoạn KHO/GC/HTSP/KCS/PKG là `FromGroupCode/ToGroupCode`, `ProcessGroup`, `DetectGroupCode` — **không phải** nhóm người dùng. **ADMIN** (`PermCheck.ReadAsync:111`): `GroupCode == "ADMIN"` hoặc `Username == "admin"`, user + nhóm đang hoạt động.

**`PermCheck`** (static, đọc DB, không đọc claim): `CanEditAsync(db, uid, params sections)` / `CanViewAsync` (Admin → true; nhóm ngưng → false) · `HandoverEditGroupsAsync` / `…OrNullAsync` (tập nhóm có CanEdit HANDOVER_*; dùng ở `DenyGroupAsync`, `VoidRightsAsync` — editGroups trang gửi bị ghi đè) · `WtsProcessGroupsAsync` → `(IsAdmin, Groups)` — Groups **không** tự thêm cho Admin · `IsAdminAsync`. Lớp cùng luật đọc riêng: `UserService.GetAllUserPermissionsAsync` (ẩn nút), `KhoVatLieuService.HasPermissionAsync`, `PartMasterPermissionHelper.CheckPermission`.

**Lỗ P-xx**: P-01 `KhPlanService.CreateAsync` không kiểm · P-02 `RefreshRouteSnapshotAsync`, `AutoRefreshRoutesForPartAsync`, `CopyRouteSnapshotAsync`, `WriteSnapshotStdConfirmsAsync` public không kiểm · P-03 Detail_Partmaster ghi DB 12 chỗ, `_isAdmin` từ claim · P-05 Tách PO / Update TG chuẩn / Ngưng Part kiểm PIN ở trang; "PIN" xoá PO & sửa Kho = **mật khẩu** (`PasswordHasher.Verify`) · P-06 CheckPermission cho ADMIN trước khi xét IsActive · P-07 `GetCanDeleteAsync`/`SetObsoleteAsync` không kiểm Group.IsActive · P-08 Handover_Index thiếu `[Authorize]` · P-09 `/logout` GET · P-10 `WtsProcessGroupsAsync` không tự thêm nhóm cho Admin. Còn tin claim ADMIN cấp quyền: `Detail_Partmaster:2474`, `Index_PartMaster:359`, `PartMaster/Create:176`, `Kho_Index:617`, `KhPlanIndex:503`, `NavMenu:182`.

**Khi thêm hàm ghi**: nhận `userId`, gọi PermCheck trước (`PermCheck.NoEdit/NoView`) · giao nhận/WTS dùng `DenyGroupAsync` / `WtsProcessGroupsAsync` (xét IsAdmin) · không đọc `IsInRole`/claim trong trang · PIN kiểm trong service (`_authSvc.VerifyPinAsync`) · không `Db.SaveChangesAsync` trong .razor.

## 6. Luật → hàm → ca kiểm

Sổ phép kiểm `tools/DanhMucKiem.txt`: 318 dòng tag, 281 có ca thật, 0 CHUA. Cú pháp `MÃ | ca; ca | ghi chú | Nguồn: …`; ca = `<kịch bản>:<CA>` (vd `wts:W12`), `nang`, `harness:<file>[:<bước>]`, `giaodien`, `chung`. Tag trong code `[TAG yyyy-mm-dd …]` + "Nguồn:". Ca kiểm cụ thể của từng luật: tra DanhMucKiem.txt hoặc bản đồ gốc tab 6.

| Luật | Hàm thực hiện |
|---|---|
| L1 4 số mỗi nơi; Nhận = NG đầu + Chờ làm + OK + NG cuối | `Compute` HLS:1548; `BuildGcSteps` HLS:3002; `LedgerGroup` HLS:319–388 |
| L2 giao = lập phiếu, nhận từng phần, NG lúc nhận tự lập phiếu trả | `IssueWithPurposeOnceAsync` HS:592; `ReceiveOnceAsync` HS:900 (`SourceReceiveId`); `ChoNhan` |
| L3 WTS ≤ Chưa làm | `WtsCap` HLS:3243; `CheckGroupQtyAsync` WSS:636 |
| L4 lập phiếu ≤ Đã làm chưa giao (chỉ ca nang) | `IssueCap` HLS:3793; `ConGiao` |
| L5 NG chỉ ra khỏi nơi bằng Xử lý NG | `HandoverPurpose.IsOut`; HS:828 |
| L6 bảo toàn, mọi màn hình đọc sổ chung | `LedgerRow.Balanced` HLS:423; `GetLedgerForDetail(s)Async` |
| L7 luật huỷ (người lập/nhóm, dây chuyền, phần chưa nhận) | `VoidIssueOnceAsync` HS:1324; `VoidReceiveOnceAsync` HS:1187 (H10); `CloseRemainderOnceAsync` HS:1650; `CanVoidReceive` |
| K1 hàng chỉ ra qua ô OK / ô NG (chỉ ca nang) | `HandoverCell.IssueCells/IssueCellsXl`; `ResolveCells` HLS:188 |
| K2 NC làm tiếp theo vụ (OFF lùi + lý do) | `HF.CaseNextNc`, `NcOptions`, `CheckNextNc`; `HS.ReceiveNc`; `CaseToNcAsync` GXL |
| K3 phiếu vào GC chọn NC nhận | HS:709–715 (N3-TONC, N3-FIRST); `ReceiveNc` |
| K4 hàng làm hộ không vào 4 số nhóm làm hộ | `LedgerLamHoRun`; `FillLamHoWaiting` |
| K5 không tạo ô âm mới (theo vụ, theo thời gian) | `CheckNoNewNegativeAsync` HS:876; `NegativeCells` HLS:3563 (khoá "GC∣GC∣<NC>", "<nhóm>∣ChuaLam", "VU∣<id>∣…", "BU∣<id>", "TL∣<ô>"); `NewNegatives` / `NewNegativeKeys`; `SnapshotBefore`, `LoopTimeline`, `NewLoopKeys` |
| K6 PO chưa chạy tự lấy quy trình mới, đã chạy chặn | `RouteRefreshBlockAsync` KPS:1087; `RefreshRouteCoreAsync`; `GcWaitingMovedAsync`; `CopyRouteSnapshotAsync` |
| K7 chặn bật Workflow khi có ô âm | `CheckWorkflowOnAsync` KPS:820; `SetApplyWorkflowAsync` KPS:843 |
| K8 mọi lưu khoá MES_PO_ + tính lại trong giao dịch | `LockPoAsync` HS:853; ISO-FIX |
| N1 GC chia theo NC | `BuildGcSteps`; `LedgerStepLine.Waiting` HLS:3114 |
| N2 máy đồng dạng / dự phòng gộp NC chính | `LedgerRow.CodeMain`; `IsBackup/ParentNC` |
| N3 NC máy loại trừ (làm hộ; NC sau chỉ nhận trả về) | `BuildGcSteps` afterExcluded; `GoodEx`; `VaoCode/IsVao` |
| N4 giao theo NC lấy từ OK NC đó | `IssueCap` nhánh HelpSend; `GoodEx` |
| N5 HTSP chung Nhận; mã chuẩn bị không tính; OK = min mã | `Pg2Group`; `PrepCodes`; `TaroKey`; `GopUnit` |
| N6 giao ngược / chọn nhóm nhận tự do (chỉ ca nang) | `CheckRoute` HLS:3981; `HF.NextGroup/PrevGroup` |
| N7 Workflow OFF chỉ cảnh báo | HS:875 (chỉ ON); `ApplyWorkflow`; `WtsSaveResult.Warnings` |
| N8 KCS/Đóng gói loại hàng khâu trước = NG đầu | chưa chốt — `HandoverReceive.QtyNg` → `NgDau` |
| N9 Kho: phôi tồn = OK Kho; TP nơi cuối | chưa chốt — `ConGiao` Kho; `TpNhap` |
| N10 trả NG ngược / lỗi công đoạn trước (GC chọn NC phát hiện) | GN-NC-PIN HS:719/819; GN-NGRET-TO; `GcPrevStageOk` HF:267 |
| N11 báo lỗi phát hiện sau khi nhận | `ReportNgAfterReceiveOnceAsync` HS:1495; `VoidNgReportOnceAsync`; `HandoverNgSource` |
| N12 Kết quả sửa + KCS xác nhận (Chờ KCS) | `CreateRepairResultOnceAsync` KetQuaSua:84; `ChoKcs` |
| N13 hàng sửa đạt không quay lại NC (Gói 15) | `HandoverCell.Xl`; `RepXong/RepOk`; `XlDest`; tag SUA-XL-GIU / SUA-OK-NC / SUA-KHONG-LUI |
| N14 sửa xong: OK trả nơi báo lỗi, NG trả nơi làm lỗi; KCS trung gian | `HF.RepairTarget`; `RepairDest` HLS:3696; `GcXlBack`; `XlToOk/XlRepairGroup/XlTraLaiGroups/SuaQuaKcsBlocked` |
| N15 khách gửi bù phôi | `ReceiveReplacementOnceAsync` KhachBu:55; `BuCap` HLS:3386; `LedgerRow.Bu` |
| N16 Kho giao thẳng Taro khi NC đầu là Taro máy loại trừ | `IsKhoTaro/KhoTaroRun`; HS:617–622; `HF.KhoNcOk` |

Khi đổi luật: L4, K1, N6 chỉ có ca `nang` → viết ca trước; N8, N9 chưa chốt; K2, K4, N12–N16 kiểm qua tag. `TaroParentCode` không có trong code (dự phòng Taro = `ParentNC` + `IsBackup`). Hằng then chốt: `HandoverPurpose` HLS:24–63; `HandoverCell` MOI/LOI/SUA/XL (+LAMHO chỉ sổ) HLS:96–104; Status chỉ PENDING/PARTIAL/COMPLETED; `NgOrigin` WTS/NHAN/NHANLOI/BAOLOI/TRALOI; ProcessGroup GC/TARO/BAVIA/WASHING/KCS/PKG; HtspSubGroup "Taro"/"Bavia"/"Rua"; ProductionMode "serial"/"trial". Khách đổi luật: sửa sổ luật → sửa đáp án ca cùng commit → ca trên code cũ phải SAI đúng chỗ.

## 7. Bộ kiểm + cách chạy

| Công cụ | Thư mục | Chạy ở đâu | DB thật? | Dùng để |
|---|---|---|---|---|
| GiaoNhanFuzz | `tools/GiaoNhanFuzz` | Windows máy Hùng + Linux | Không (EF giả) | 19 kịch bản cố định + fuzz ngẫu nhiên gọi **hàm thật** Services |
| WtsHarness | `tools/WtsHarness` | CHỈ Linux (Playwright + python3) | Không (EF giả) | Chạy MES.Web trên EF giả, bấm trang thật, so bản cũ / mới |
| QuetSo | `tools/QuetSo` | Máy server | Chỉ đọc (Rollback) | Quét DB: PO lệch, ô âm, phiếu lạ |
| SqlDongThoi | `tools/SqlDongThoi` | Máy server | GHI — chỉ MES_TEST, trừ `--mes` | Nhiều người cùng lưu (S1–S9), đo khoá / deadlock |
| BackfillNghi | `tools/BackfillNghi` | Máy server | Chỉ đọc; `--ghi` | Chép bù giờ nghỉ WTS cũ (đã chạy 01/11) |
| DanhMucKiem.txt | `tools/` | — | — | Sổ phép kiểm; `GiaoNhanFuzz danhmuc` đối chiếu |
| 4 file .bat | `tools/` | Windows máy Hùng | Không (trừ `quet` chỉ đọc) | Trạm / commit / app 5000 — mục 8 |

**GiaoNhanFuzz**: lệnh `kh loi vu chuoi wts quyen iso quet nghi viec auth std sua form taro why bu ktt` (kịch bản cố định `*Scen.cs`; cuối `KẾT QUẢ: ĐẠT hết`) · `danhmuc` · `whyreplay <file.json> <rid>` · `nang [PO=60] [ops=120] [seed=1]` (18 kịch bản + 13 lượt ngẫu nhiên; `SONG_SONG`, `KHUC`, `TREO_PHUT` 20; kết quả `nang_ketqua/`; cuối `SAI: 0`) · `<số PO> [ops] [seed]` fuzz (mã thoát 0 khi SAI 0). Biến môi trường: `LOI=1`, `CHUOI=1`, `WHYALL=1`, `EXCL=0..1`, `TARO=1`, `LAMHO=1`, `KTTPO=1`, `TRACEPO`, `DUMP=1`, `DANHMUC_CHAN=1`. `CheckAll` kiểm sau MỖI thao tác: lệch cân PO, ô âm, WTS vượt hàng thật, Σ HandoverTxCloses = QtyClosed, Chờ KCS ≠ bảng, Taro 3T / làm hộ ≠ đáp án độc lập, Đang ở đâu lệch, `HandoverNgTrace.Build` ≠ sổ, vụ lỗi lệch cân, phiếu nhận vượt / mồ côi, `[ISO-FIX]`; bị K5 chặn → làm theo gợi ý `HandoverWhy` bằng hàm thật. Stub: `EfStub.cs` (`TrackPoLock`, `sp_getapplock` giả), `AppDbContextStub.cs` (bảng mới phải thêm DbSet + KeyName), `H10Help.cs` (`CloneInput`), `FuzzPerm.cs`; csproj Compile Include từng file Services thật + `../BackfillNghi/BackfillCore.cs` + `../QuetSo/QuetCore.cs`.

```
cd tools\GiaoNhanFuzz
dotnet build GiaoNhanFuzz.csproj -c Release -o bin2     (fuzz nền đang chạy thì build ra bin3 rồi xoá)
dotnet bin2\GiaoNhanFuzz.dll nang                        (cuối "SAI: 0")
dotnet bin2\GiaoNhanFuzz.dll kh|loi|…|ktt                ("KẾT QUẢ: ĐẠT hết")
dotnet bin2\GiaoNhanFuzz.dll 300 120 2                   (ngẫu nhiên kỹ)
dotnet bin2\GiaoNhanFuzz.dll danhmuc
```
Linux: `dotnet build -o bin2 && dotnet bin2/GiaoNhanFuzz.dll <PO> <ops> <seed>`; máy 2 nhân → `SONG_SONG=2`, lượt 25 PO.

**WtsHarness**: `mk.sh <src MES.Web> <dest> <port>` (chép nguồn, bỏ `PmSaveLockSql.cs`, chép `EfStub.cs` + `EfCompile2.cs` + `Stubs/*`, vá Program.cs gọi `HarnessSeed.RunAsync` + endpoint `/harness/*`, build `-o bin2`; `mkdir -p bin2/wwwroot/uploads/avatars`). `run.sh old|new <dir>` cổng 5100/5101; `drive.py` → `old.json`/`new.json`; `cmp.py` so. 44 driver `drive_*.py` (`python3 drive_xxx.py 5101 new`, mỗi driver một harness khởi động lại). 1 DbContext singleton → không đo khoá chen nhau thật. **QuetSo**: `RealHost.cs` chuỗi kết nối `--cs` → `MES_CS` → `../../MES.Web/appsettings.json`; `QuetCore.ScanAsync` luôn Rollback; mã KHONG_CAN (L6), O_AM (K5), WTS_VUOT_TRAN (L3), DANG_O_DAU_LECH, WTS_SO_LECH / WTS_SO_AM, CANH_BAO_SO, MA_TRUNG, LOAI_TRU_DAU, PM_TRUNG / PM_MA_A, VU_LECH, SO_LE, CLOSE_LECH, LAMHO_TARO, WTS_MA_KHOANGTRANG / SNAP_KHOANGTRANG, TARO_NC_LECH, KHO_TARO_LA, PHIEU_CHO_O_DAT (lọc trước khi lên máy khách), TRA_VE_NHOM_LA. **SqlDongThoi**: `dotnet run -c Release -- <người=6> <giây=60> <PO=4> <phôi/PO=100> [--cs …] [--mes]`; chỉ ghi khi `DB_NAME() = MES_TEST`; 12/11 MES S1–S9 ĐẠT hết.

**Kiểm 1 gói**: (1) kịch bản cố định + ca mới (tag có "Nguồn:" + dòng DanhMucKiem) → (2) fuzz nặng sandbox (nhiều seed, 4–16 lượt × 60 PO, SAI 0) → (3) đột biến: chép bản riêng (cả tools), cố ý làm sai từng luật → ca phải bắt → (4) harness driver mới + `drive.py` so bản cũ/mới, từng driver một → (5) rà độc lập agent CHỈ ĐỌC tới 0 Cao / 0 Trung → (6) ghi máy Hùng → trạm `build` + `kiem` ĐẠT HẾT → sổ + memory → commit. Lỗ hổng: Workflow OFF chỉ kịch bản phủ; hiệu suất / báo cáo / thiết bị / ca gần như không có fuzz. Giới hạn sandbox: NuGet bị chặn (QuetSo / SqlDongThoi / BackfillNghi thật, MES.Web build thật chỉ ở máy server / trạm); EF giả không unique / FK / index; `DateTime.Now` → fuzz không tất định (ca kẹt chụp JSON `why_ca/`); chờ ≤ 5 phút mỗi đợt.

## 8. Làm việc trên máy Hùng

**Thư mục + app**: thư mục app chính trên máy Hùng (`…\mes-prototype`) = clone git master = app 5000 khách đang test qua tunnel (DB `MES`, dữ liệu test). Claude sửa THẲNG vào đây; git là bản sao lưu. App 5000 chạy bằng `tools\chay_app_5000.bat` ([RUN-5000]: `dotnet build -c Release`, lỗi thì DỪNG, rồi `dotnet run --project MES.Web.csproj`); **không còn dotnet watch** — sửa file không ảnh hưởng khách tới khi Hùng chạy lại bat; kiểm bản khách đang dùng: mtime `MES.Web\bin\Debug\net8.0\MES.Web.dll`. DB thử `MES_TEST` (COPY_ONLY, cùng server) + app thử cổng 5001. Máy i7-12700KF 12 nhân / 20 luồng, RAM 39,8 GB; trạm `SONG_SONG` = 9 tiến trình [KIEM-75].

**Ghi file (theo thứ tự)**: (1) `device_request_folder_access` thư mục `mes-prototype` (computer-use KHÔNG mở được cmd / Explorer) → (2) `device_stage_files` từng file sẽ sửa, so với `git show HEAD:<path>` (chuẩn hoá xuống dòng); khác HEAD → dừng, hỏi Hùng → (3) soạn bản mới đúng EOL + BOM của file đó; patch Python: chuẩn hoá LF trước `replace`, kiểm `in` từng token (False = dừng), đổi lại CRLF nếu cần → (4) `device_commit_files` rồi stage lại so sha256; không xoá được file trên máy Hùng → (5) ghi `tools\claude_commit_msg.txt` (CRLF) cùng đợt → (6) trạm `build` → `kiem` → báo Hùng `claude_day_git.bat` + `chay_app_5000.bat`.

**8.3 EOL + BOM thật** (đo 14/11/2026 20:2x, 166 file khớp HEAD d756582; LF 94 · CRLF 61 · CRLF+BOM 11; git lưu LF không BOM trừ DanhMucKiem + sql):

| Nhóm file | Đa số | Ngoại lệ |
|---|---|---|
| `.razor` (42) | **CRLF 27** | **LF 15**: App, Routes, RedirectToLogin, _Imports, Pages/Home, Pages/Error, Pages/Profile, Pages/About/Index, PartMaster/Create, PartMaster/History, Setting/Settings_Index, Layout/LoginLayout, Shared/AttributePicker, Shared/PinConfirmModal, Shared/SplitPOModal |
| `Services/*.cs` (48) | **LF 39** | **CRLF 9**: AuthGuard, CaLamViecService, HandoverChangeNotifier, HandoverWhy, KhPlanService, NgCaseService, PartStepStdTimeConfirmService, ServerLicenseHelper, UserService |
| `Data/Entities/*.cs` (54) | **LF 34** | **CRLF 20**: CaLamViec, CaLamViecChangeLog, HandoverNgReport, HandoverReceive, HandoverRepairCheck, HandoverRepairResult, HandoverReplacement, HandoverTransaction, HandoverTxClose, KhPlanDeletedDetail, KhPlanRouteSnapshotMachiningTiming, KhPlanRouteSnapshots, NgCase, NgCategory, PartStepStdTimeConfirm, StandardWtsTaskGop, StdTimeTracking, WtsBreakWorkLog, WtsExtraItemLog, WtsProductionLog |
| `Data/AppDbContext.cs`, `Constants/SystemPermissions.cs` | CRLF | |
| `Data/SeedData.cs`, `Program.cs`, `appsettings.json`, `launchSettings.json`, `MES.Web.csproj` | LF | |
| `tools/sql/06–15.sql`, `tools/DanhMucKiem.txt` | CRLF + BOM | |
| `tools/*.bat` (4) | CRLF | **LF**: `claude_day_git.bat` |
| `tools/claude_commit_msg.txt` | CRLF (quy ước) | |
| tools `.cs / .py / .sh / .json / .csproj` | LF | (theo memory, chưa đo lại) |

**Kiểu của từng file là thứ đo được lúc stage, không phải bảng này.** Razor LF trên Windows = `RZ9980` parser chết (dấu hiệu `CS0414`). `.gitattributes` = `* text=auto`.

**Trạm `tools\claude_tram.bat`**: Hùng mở 1 lần; vòng 4 giây đọc `tools\claude_lenh.txt` (Claude ghi 1 từ, `kiem\r\n`), xoá file, chạy: `kiemgap` (18 kịch bản + nang 15 PO × 60, ~1–2 phút) · `kiem` (`kiemtra.bat`: build GiaoNhanFuzz + danhmuc + nang 60 PO × 120 seed `%RANDOM%`, ~13–15 phút) · `tong` (danhmuc CHẶN + 2 lượt 120 PO × 160, ~30–40 phút) · `danhmuc` → `tools\ketqua_danhmuc.txt` · `quet` → `tools\QuetSo\ketqua_quet_console.txt` · `build` → `tools\ketqua_build_mesweb.txt`. Kết quả kiểm `tools\ketqua_kiemtra_<yyMMdd_HHmm>.txt`, cuối `KET QUA CUOI: DAT HET` / `CO SAI` / `CO TREO`. Trạng thái `tools\claude_trangthai.txt`: `SAN SANG` / `DANG CHAY <lệnh>` / `XONG` / `KHONG HOP LE` / `TREO …`. Trạm không có lệnh dừng, không chạy / tắt app, không SQL ghi; không sửa .bat khi trạm chạy; tiêu đề "Select …" = QuickEdit → Esc; không gửi song song lệnh; chờ ≤ 5 phút rồi stage lại trạng thái.

**Commit + push `tools\claude_day_git.bat`**: `git add MES.Web tools/GiaoNhanFuzz tools/QuetSo tools/WtsHarness tools/BackfillNghi tools/SqlDongThoi tools/sql tools/kiemtra.bat tools/DanhMucKiem.txt tools/claude_tram.bat tools/claude_day_git.bat tools/chay_app_5000.bat` → `git reset -q -- bin2 nang_ketqua bin obj` → `git commit -F tools/claude_commit_msg.txt` → `git push` → `tools\ketqua_git.txt` (`KET QUA GIT: XONG` / `COMMIT LOI` / `PUSH LOI`; `COMMIT LOI` ngay sau push thành công = không còn gì để commit). **Phải nhờ Hùng**: 2 bat trên · mở / đóng trạm · chạy SQL trên server (Claude soạn `tools/sql/NN_*.sql`; SQL 2016: không `CREATE OR ALTER`, `SET QUOTED_IDENTIFIER ON` khi tạo chỉ mục lọc, đầu file `USE [MES]; GO`) · SqlDongThoi `--mes` · xoá file · cấp quyền thư mục. Thử live app 5000: `fetch POST /login-submit` (tài khoản test: memory ways-of-working), chỉ xem.

## 9. Thuật ngữ xưởng ↔ code (~25 mục; đầy đủ ở bản đồ gốc tab 9)

| Chữ xưởng | Code / giá trị DB | Ghi chú |
|---|---|---|
| Phôi | `LedgerRow.Phoi` từ `KhoVatLieus.SoLuongThucNhan` | đầu vào cân PO |
| 5 nhóm Kho / Gia công / HTSP / KCS / Đóng gói | `GroupCodes` KHO / GC / HTSP / KCS / PKG; HTSP gồm ProcessGroup TARO / BAVIA / WASHING (`Pg2Group`), HtspSubGroup "Taro"/"Bavia"/"Rua" | Kho không có Chưa làm / Đã làm, chỉ `ConGiao`; `NextGroup("PKG") = "KHO"` |
| Huỷ / Khách hàng (nơi nhận ảo) | ToGroupCode `HUY` (SCRAP) / `KHACH` (CUST_RETURN); `CauseGroupCode = KHACH` = phôi khách | phiếu ra khỏi PO |
| NC (nguyên công Gia công) | `KhPlanRouteSnapshotMachining.NC`; `WtsProductionLogs.NC`; sổ `LedgerStepLine.Code` | khoá ô "GC∣GC∣<NC>" |
| NC loại trừ / máy loại trừ | `MachineExcludeSetting.SoMay` → `KhPlanRouteSnapshotMachineExclude`; `LedgerStepLine.IsExcluded` | N3: không có ô Chưa làm riêng, hàng đi "Giao theo NC" cho nhóm khác làm hộ |
| Làm hộ (Taro ở NC loại trừ) | `LedgerLamHoRun`, `LedgerStepLine.LamHoNc` / `HelpOut`; Purpose `HELP_SEND` / `HELP_RETURN`; ô `HandoverCell.LamHo` = `LAMHO` (chỉ sổ) | khoá K5 "HTSP∣LamHoTaro∣<NC>", "GC∣helpOut∣<NC>"; chữ gửi khách: "Taro làm bước NC của Gia công" |
| Máy đồng dạng / dự phòng | `IsBackup` + `ParentNC`; sổ `LedgerRow.CodeMain` | N2 |
| @VAO (dòng vào GC) | `VaoCode = "@VAO"` trong `HandoverTransactions.FromNC`; `IsVao` | N3-FIRST; Part Master chặn mã "@" |
| Kho giao thẳng Taro | `IsKhoTaro` / `KhoTaroRun`: HELP_SEND From KHO, FromNC @VAO, FromCell MOI | N16; Taro trả phôi lỗi về Kho = NG_RETURN HTSP→KHO FromNC @VAO |
| @XONG (sửa đạt, hàng đã làm xong trước lúc phát hiện) | `RepXong = "@XONG"`; `HandoverRepairResults.ToNC = @XONG`; `RepairDest` → "XONG"/"GIU" | [SUA-XL-GIU] không quay lại NC |
| @OK (GC làm hỏng ở NC k, sửa đạt = xong NC k) | `RepOk = "@OK"`; `LedgerStepLine.OkSua` | [SUA-OK-NC] |
| 4 ô Hàng mới / Hàng lỗi / Hàng sửa / Hàng lỗi đã xử lý | `HandoverCell.Moi/Loi/Sua/Xl` = `MOI` / `LOI` / `SUA` / `XL` trong `FromCell` / `ToCell`; `ResolveCells(fromCell, prevStageNg, toCell)` → loại phiếu | XL từ 10/11 cũng là ô GIAO (`IssueCellsXl`); SUA = hàng nhóm khác gửi nhờ sửa |
| Loại phiếu (`HandoverPurpose`) | `NEW` · `NG_RETURN` (tự lập có `SourceReceiveId`) · `REPAIR_SEND` (LOI→SUA) · `REPAIR_RETURN` (→XL) · `HELP_SEND` / `HELP_RETURN` · `SCRAP` · `CUST_RETURN`; NULL = phiếu cũ, `Classify` suy | Status PENDING / PARTIAL / COMPLETED; huỷ = `IsVoided`; huỷ phần chưa nhận = `QtyClosed` + `HandoverTxCloses` |
| Vụ lỗi VL000001 | `NgCase` (CaseNo cột tính, KhPlanDetailId, NgCategoryId, OriginKind WTS/NHAN/NHANLOI/BAOLOI/TRALOI, DetectGroupCode, DetectNC, CauseGroupCode); sổ `LedgerCase` (SinhRa / Held / Transit / DaXuLy / Huy / TraKhach / DaBu / Lech) | 1 vụ 1 loại lỗi; bảng chỉ là nhãn, số do sổ tính; vụ làm hộ `LedgerCase.IsLamHo` chỉ GC sửa |
| Nơi gây lỗi (bắt buộc) | `NgCase.CauseGroupCode` KHACH/KHO/GC/HTSP/KCS/PKG; `FormGiaoRequest.Cause` | [CAUSE-BAT-BUOC] |
| Báo hàng lỗi sau khi nhận | `HandoverNgReport` (SourceCell PHOI/CHUA/DA, NC, Origin DAU_VAO/DAU_RA, CauseGroupCode, NgCaseId) | N11 |
| Chưa làm / Đã làm / Đã làm chưa giao | `LedgerGroup.ChuaLam` (theo NC `LedgerStepLine.Waiting` = trần WTS L3) / `DaLam` / `ConGiao` (= trần phiếu L4) | Kho: ConGiao = phôi tốt còn giao |
| Tồn / Chờ nhận / Chờ KCS | `LedgerGroup.Ton` = ΣNhận − ΣGiao + ChoNhan + ChoKcs; `ChoNhan` (Status ≠ COMPLETED, vào Tồn bên giao, KHÔNG vào Chưa làm); `ChoKcs` = `QtyPass` − Σ `HandoverRepairChecks` | khoá K5 "<nhóm>∣ChoKcs" |
| Kết quả sửa / KCS xác nhận | `HandoverRepairResult` (GroupCode, SourceCell SUA/LOI, NgCaseId, QtyPass, QtyFail, ToNC = NC / @XONG / @OK); `HandoverRepairCheck` (QtyOk, QtyNg, CheckedBy ≠ người ghi) | N12; ON sửa qua KCS bắt buộc `HF.SuaQuaKcsBlocked` |
| Khách gửi bù phôi | `HandoverReplacement` (NgCaseId, Qty, IsVoided); `LedgerRow.Bu`, `LedgerCase.DaBu`; `BuCap` = trả khách + huỷ − đã bù, chặn cả OFF | N15 |
| Mã CV / mã chuẩn bị / mã gộp 3T | `WtsProductionLog.WtsCode`, snapshot `WtsTaskCode`, `StandardWtsTask.TaskCode`; `PrepCodes` 1V/1T/1R/1K/1D/1G (`IsPrepCode`, WSS `IsPrepNoQty`) giờ không số; `StandardWtsTaskGop` → `KhPlanRouteSnapshotWtsGop`, sổ `InGop`, `LedgerStepLine.GopUnit / IsGop / GopDone`; Taro mã nhiều NC dùng `StepNC`, `TaroKey(mã, nc)`, `NcKey` ("NC03" ≡ "3") | khoá "HTSP∣TARO∣<mã>"; [TARO-3T-GOP] OK bộ = OK gộp + min thành phần |
| Workflow ON / OFF | `KhPlanDetail.ApplyWorkflow`; `LedgerRow.ApplyWorkflow`; `SetApplyWorkflowAsync`, `CheckWorkflowOnAsync` (K7) | OFF chỉ cảnh báo (N7) nhưng vẫn chặn WTS ≤ PO, trần bù |
| Đang ở đâu / Gợi ý gỡ K5 | `WhereIs(row)` → `WhereSummary` (`PoViTri.razor`); `HandoverWhy.Plan(...)` → `WhyPlan.Steps` (Kind WTS / NHAN / PHIEU / BAOLOI / WTSV / BU) | [GN-WHERE] [GN-WHY] |
| Việc không ra SP / Ca / Giờ nghỉ | `WorkerActivityLog` (`WSS.SaveActivityAsync`); `CaLamViec` (GioBatDau, GioKetThuc, IsOvernight) + `CaLamViec_NghiGiaiLao`; `WtsBreakWorkLog` + `WorkMinutesService` | không vào sổ giao nhận |
| L / N / K | L1–L7 luật gốc; N1–N16 ngoại lệ xưởng; K1–K8 khoá bảo toàn | mục 6; nguồn Sổ luật Giao nhận |

## 10. Bẫy đã gặp (bẫy → dấu hiệu → cách tránh)

- **Razor**: `.razor` LF trên Windows → `RZ9980`, `CS0414` → đúng EOL 8.3 · lambda phức tạp trong `@onclick` → `RZ9980`+`CS0246`+`CS9348` → tách `private` method · `@{}` trong `@if` → `RZ1010` · ngoặc kép lồng trong `class="…"` → `RZ1006` → `private string` tính sẵn · `@if { <div> }` trong `<div>` khác → `RZ9980` → top level hoặc `visibility:` · `@foreach` lồng > 2 cấp → `RZ1006` → `private RenderFragment X(…) => __builder => { … }` · chú thích `@* *@` giữa `@if {}` và `else if` → else gãy · `value=` + `@onchange` trên `<select>` → reset trống → `selected="@(…)"` · `HH\:mm` trong `$"…"` → `CS1009` · thiếu `@key` → DOM diff sai · Timer không dispose → rò mạch.
- **Patch Python / phiên dài**: `str.replace` im lặng khi `old` có `\r\n` còn file LF → chuẩn hoá LF trước, `print(token in c)`, False = dừng · patch từ trí nhớ → đè mất logic → view lại method ngay trước khi patch · viết lại cả method → mất nhánh cũ · Hùng báo "mất logic" → `git show HEAD:<file>` là chuẩn diff · chat dài → ghi Bàn giao, sang chat mới · ghi máy Hùng không so byte → stage lại + sha256.
- **Fuzz / EF giả**: EF giả sửa đối tượng tại chỗ → đọc kỳ vọng TRƯỚC khi gọi · lệnh thử bị từ chối vì lý do khác → ca đạt giả · "ngoài nhóm" dùng uid 99 → dùng `FuzzPerm.GcOnlyUser` · thêm bảng quên `AppDbContextStub` / `KeyName` / `H10Help.CloneInput` → checklist 13 · build `bin2` khi fuzz nền chạy → `bin3` · `nang` 2 nhân → TREO giả → `SONG_SONG=2`, 25 PO.
- **Harness**: driver liên tiếp không giết `dotnet MES.Web.dll` cũ → DB bẩn, SAI giả → `pgrep -f "dotnet MES.Web.dll"` → kill → kiểm 0 tiến trình · agent rà độc lập chạy harness / pkill cùng lúc → "Connection refused" giả (Gói 21) → agent CHỈ ĐỌC, bản sao riêng /tmp/<agent> · `runall` dưới tải → từng driver · thiếu `bin2/wwwroot/uploads/avatars` → `mkdir -p`.
- **SQL Server / EF thật**: `IsolationLevel.Serializable` riêng → rò qua pool (ISO-LEAK) → không đặt isolation level, `sp_getapplock` TRƯỚC khi đọc · SQL 2016: không `CREATE OR ALTER`, chỉ mục lọc cần `SET QUOTED_IDENTIFIER ON` · SQL 12 `LIKE '%CASE%'` khớp nhầm `NgCaseId` → điều kiện khớp đúng tên · deadlock 1205 / −3 → 51205 → `RetryDeadlockAsync` 3 lần · EnsureCreated tạo DB trống máy khách → lệch cấu trúc → backup/restore · chuỗi kết nối thật trong appsettings repo public (RS-A3) · DbContext trang giữ phiếu cũ → `ChangeTracker.Clear()` + đọc lại sau khoá · bấm 2 lần → lưu trùng → `*OnceAsync` + cờ busy.
- **Trạm / git**: QuickEdit "Select …" → Esc · gửi 2 lệnh liên tiếp → chờ `XONG` · sửa `.bat` khi trạm chạy → cmd đọc theo offset · `claude_day_git.bat` lần 2 → `COMMIT LOI` = không còn gì commit · XML comment `--` trong csproj → MSB4025 · `bin2`, `nang_ketqua`, `ketqua_*` đã tracked → `git rm --cached` + .gitignore (chờ Hùng) · yêu cầu quyền thư mục bị classifier chặn → Hùng nhắn "xin quyền đi tôi cấp" rồi gọi lại.
- **Luật nghiệp vụ dễ sai**: "bấm Giao thì vào Chưa làm bên nhận" → sai, vào Tồn (Chờ nhận) tới khi bấm Nhận (L2) · Excel khách không trừ Giao·Hàng lỗi khỏi Chưa làm → phần mềm trừ, Excel sai · Kết quả sửa vụ phát hiện sau khi GC làm xong → ToNC `@XONG` · huỷ phiếu trả tự lập = huỷ lần nhận gốc; đã nhận lại → huỷ dây chuyền 1 giao dịch (H10) · "chỉ ẩn nút là đủ" → kiểm ở service · Workflow OFF vẫn tính 4 số + cảnh báo, vẫn chặn WTS ≤ PO, trần bù · "PIN" ở Kho / Xoá PO = mật khẩu đăng nhập (KHO-PIN chờ Hùng) · chữ "làm hộ" gửi khách → nói "Taro làm bước NC1 của Gia công".

## 11. Trạng thái mô-đun + việc mở (chụp 14/11; nguồn sống = Sổ luật tab "Lỗi cần sửa")

| Mô-đun | Trạng thái | Còn mở |
|---|---|---|
| Đăng nhập / phiên / bản quyền | Xong (RS-C3/C4/C5, RS-E11) | ServerLicenseHelper chưa có tài liệu; Handover_Index thiếu `[Authorize]` (P-08) |
| Phân quyền | Xong (USER-PERM-SVC, SET-MATRIX-1TX) | QUYEN: Hùng cấp ma trận nhóm trước khi khách dùng thật; Claim-ADMIN còn 6 trang |
| Danh mục | Xong (RS-C1, PHAT-HIEN-0311, CA-LOG, DS-LOI, TARO-3T-GOP) | kiểm upload ảnh Sơ đồ |
| Part Master | Xong phần luật (PM-DUP, PM-NC-DUP-SVC, PM-SNAP-MID, PM-LOAITRU-TARO, TARO-DUPHONG, STD-UPDATE) | 12 chỗ ghi DB trực tiếp (P-03); RS-D4 chờ khách |
| Kế hoạch | Xong (KH-GUARD, KHE-3/5/6/7, K6-NOTRUN, KH-SL-MAY-CHU) | P-01, P-02, P-04; "PIN" xoá PO = mật khẩu; RS-D2 chờ khách; View_Khplan chỉ [Authorize] |
| Kho | Xong (KHO-LEDGER, KHO-4/5, N15, N16) | KHO-PIN chờ Hùng; KHO-06/N9 chờ khách; chưa có bước giao khách / xuất kho TP |
| Giao nhận | Xong Gói 1–21 (L1–L7, N1–N16, K1–K8) — khách đang test lại | KT-1211 8 câu chờ khách (2/3/4/6/7 là việc LỚN đụng sổ); N3-PIN-BYPASS chờ Hùng; nhóm C cũ (GOI-Y-FORM, HL-X, K4, N8, NG-PIN, X1) |
| WTS công nhân | Xong (WTS-SVC, WTS-PANEL-QTY, ML16/ML17, TARO-NC-MA, WTS-3T-BAO, WTS-VITRI) | file 8.701 dòng có mã chết; menu hiện cho mọi người; `_canSubmit` dùng CanView; VL7 chờ khách |
| Hiệu suất công nhân | Có (RPT-4, RPT-ACTIVE, ML17) | RS-D7; RPT-1/RS-D6 ca đêm; Excel export deferred; gần như không có fuzz |
| Hiệu suất máy | Có (RPT-3) | chỉ `[Authorize]`, không mục quyền; RPT-CA chờ khách; không có ca kiểm |
| Hồ sơ cá nhân | Có | PIN plain text, không giới hạn thử (P-05) |
| Trang chủ | Cũ | số cứng lỗi thời, card không theo quyền |
| Hạ tầng | Test trên máy Hùng qua tunnel | bản trắng D1, backup/restore RS-A2, mật khẩu SQL RS-A3, domain + HTTPS, sao lưu định kỳ, CDN Bootstrap |
| Chưa có trong code | — | xuất kho TP / giao khách (N9); thống kê lỗi nhiều PO (VL6 / X2); Excel báo cáo WTS; hướng dẫn sử dụng |

Việc mở — **C chờ khách**: KT-1211, GOI-Y-FORM, RS-D2, RS-D4, RS-D6 · ML-17 · RPT-1 · RPT-CA (ca đêm), ML-31 · STD-2, K4 · HTSP-05, HL-X (đang chặn HELP-LAST-EXCL), KCS-02 · N8, KHO-06 · N9, VL7, X1, NG-PIN, RS-D7, vòng 2–4 khách test lại. **B chờ Hùng**: N3-PIN-BYPASS (đề xuất B), KHO-PIN (đề xuất đổi chữ "Mật khẩu đăng nhập"), VL6 · X2. **Hùng làm**: QUYEN, RS-A3, RS-A2. **D lúc bàn giao**: D1 bản trắng (dọn giao dịch test, giữ danh mục), G16-PHIEU-CHO (trạm `quet` lọc PHIEU_CHO_O_DAT), domain + HTTPS, sao lưu định kỳ. **Ghi nhớ Thấp**: G21-GHI-NHO, G16-GHI-NHO, OKBO-NG-CHEO, WTS-SUA-PHAN-NG, WTS-PANEL-OFF, H7 · VL3 · VL4. Đã đóng không làm lại: KTT-KCS-PKG, G20-GHI-NHO (1), G15-SUAHO-OFF, G15-OFF-LAMLAI, G14-BU-THOIGIAN.

## 12. Lịch sử gói + tag

`git log` master d756582 (171 commit, 16/08 → 14/11/2026). Tìm code của gói: `grep -rn "\[TAG" MES.Web tools`.

| Gói | Commit | Ngày | Tag chính | 1 câu |
|---|---|---|---|---|
| 21 | d756582 | 14/11 | G20-NOTE-BO, G20-CHU-WTSCAP, G17-ON-DOI-O, QUET-PHIEU-CHO, G20-CA-SHOWNCELL | Dọn ghi nhớ Thấp; QuetSo PHIEU_CHO_O_DAT + TRA_VE_NHOM_LA |
| 20 | 4f446b8 | 13/11 | G19-3T-NGKHAC, G19-HUYHIEU-NHAN, G19-VITRI-OFF | Câu báo WtsCap 3T; huy hiệu nhận; PoViTri vàng khi OFF |
| 19 | 486fa57 | 12/11 | LS-THOIGIAN, WTS-3T-BAO, WTS-BAO-CU, WTS-VITRI | Lịch sử phiếu theo giờ; nút "hàng đang ở đâu" trên WTS |
| 18 | 0470386 | 12/11 | KHO-TARO-THANG, KTT-PHOILOI, KTT-CHI-TARO | N16 Kho giao thẳng Taro |
| 17 | f8010bd | 12/11 | G16-NHAN-CHONSAN, G14-KIEM-THEM | Trang Nhận chọn sẵn ô LOI khi ON |
| 16 + 14 | a3a3af4 | 12/11 | SUA-QUA-KCS, SUA-HO-TRA-NGUOC, XL-TRA-NOILOI, XL-CHU-KCS, WHY-WTSV-TRAN, KIEM-75, KHACH-GUI-LAI | N14 + N15 (HandoverReplacements, SQL 15) |
| 15 | fc55777 | 11/11 | SUA-XL-GIU, SUA-OK-NC, SUA-KHONG-LUI, XL-TRA-LAI, XL-VUOT-CHAN, K5-VONG-XL, SUA-OFF-LUI, XL-TRA-NHOMSUA | Hàng sửa đạt không quay lại NC (@XONG / @OK) |
| 13 | 86dc606 | 10/11 | GC-LOI-TRUOC-NCDAU, GN-NGUOC-CHAN, KHO-NC-DAU, GC-HTSP-NC-HIEN, PM-LOAITRU-TARO, WTS-TARO-NC, CAUSE-BAT-BUOC | Khách vòng 1 |
| 12 | 9bd3973 | 09/11 | LAMHO-OFF | OFF: vụ làm hộ không chặn |
| 11 | 719c14c | 09/11 | H10-WHY-NGAYCU, LAMHO-NHAN-NG, LAMHO-NCKEY | NcKey "NC03" ≡ "3" |
| 10 | 5d42247 | 08/11 | LAMHO-NHO-SUA, GOI8-THAP-C/D/E/F, H10-KET-WHY-3, GOI9-LOAILOI-OFF | Vụ Taro làm hộ chỉ GC sửa |
| 9 | a32c59a | 08/11 | WTS-SUA-NHOM, GOI9-OFF-CANHBAO | User nhóm nào sửa dòng nhóm đó |
| 8 | 2cdcd14 | 08/11 | H10-KET-WHY-2, LAMHO-SUA-MA-VU, LAMHO-L2-KCS, LAMHO-WTS-DSPO, LAMHO-B-CHU | Lỗi ngầm Taro làm hộ |
| 5–7 | becb81e | 07/11 | TARO-DUPHONG, TARO-DOI-CHEO-NC, WTS-HTSP-DSMA, S8D-KHOA, H10-KET-WHY, TARO-LAMHO-NC, WTS-MA-TRIM, WTS-QUYEN-MAY-CHU, WTS-PREP-NG, WTS-SUA-NC-VU, KH-SL-MAY-CHU, KHO-ADMIN-NHOM | Rà độc lập toàn hệ thống |
| 4 | 0594d93 | 07/11 | TARO-NC-MA, TARO-3T-GOP, B6-DP-THAP | Taro mã nhiều NC (StepNC), mã gộp 3T (SQL 07) |
| 3 | 1ee303c (+ cc3d33d, 88770e9) | 06/11 | K5-CLOSE-NHIEU, KHE-5, VL9, CA-LOG, NHAN-DOI-O-NC | SQL 10–14 |
| 2 | 4b05c99 | 06/11 | PM-SNAP-MID, B6-DP-THAP | Khoá lưu Part Master MES_PM_SAVE_ |
| 1 | b5e3e61 | 06/11 | A7-VU-AM, B3-SO-NGUYEN, WTS-AM, B6-DP-MA, B7-TIMING-MAY, BLZ-RECONNECT, WTS-SO-CULTURE, NGUON-TAG, QUET-VU-SOLE | Nhóm A + B sau form; tag phải có "Nguồn:" |
| GN-FORM1 | 2775213 / 1fedbaa | 06/11 | GN-FORM1, K2 | Form "Giao / xử lý hàng" 4 bước |

**Trước khi đánh số (29/10 → 05/11)**: dba72b7 05/11 K5-VONG-HO-WTS, WTS-SUA-GIO-GOC · 8a206dd 05/11 DONGTHOI-MES · dced780 / b08472d 05/11 K5-VONG-SUA, PM-NC-AT, PM-T4, SET-MATRIX-1TX · 770c129 05/11 GC-SUA-TAI-CHO (N12, SQL 08) · 87d15a0 04/11 N3-FIRST · 0a2a459 04/11 PHAT-HIEN-0311 · c991636 03/11 RA-CASCADE-PM, USER-PERM-SVC · a77a9ea 03/11 PM-NC-DUP-SVC · 0465df2 / 1d4ef55 / 5a9441d 02/11 RUN-5000, WHY-TRAN-DB, KIEM-TONG · c59c327 … 9328c05 01/11 ML17-XN, CA-NGHI-HIST, KIEM-NHAN, RA-SOCHUNG, WTS-PANEL-QTY (SQL 06, trạm kiểm) · 73b44e2 … c2148a7 31/10 ISO-FIX, VOID-PERM, KHO-4/5, H10, N3-TONC · 7479f01 … b3ddb9e 30/10 KHE-3/6/7, ML16-ACT, RS-C1..C5, K6-NOTRUN · 6170bb7 / ed8b95f / c71546d 29/10 WTS-SVC, CHUOI, HELP-LAST-EXCL, PM-PARENT (tách WtsSaveService + PoGuard).

**Nền giao nhận mới (20/10 → 28/10)**: 0b38d40 / 8028de3 28/10 VULOI, DS-LOI · b6588e4 28/10 GN-NG-TRACE · 1d1dfd7 / 20738e2 / d1fc112 27/10 KH-GUARD, GN-NG-AFTER, GN-WHERE · 2b4a1e6 26/10 GN-VOID-RULE, GN-WHY (GiaoNhanFuzz ra đời) · 11e8f78 … 164c03b 24/10 GN-LIVE, GN-FREEZE, GN-RECV-NG, GN-NGRET-TO, GN-ROUTE-FREE, GN-CELL2, GN-NC-PIN · 373e554 … 441ff95 23/10 PM-DUP, GN-TEXT, GN-CELL · 2437bc5 / 1d93699 22/10 GN-WTS-CAP, GN-NG-OUT, GN-K8, GN-LEDGER — **HandoverLedgerService ra đời** · 7a9e508 20/10 bản trước form mới. Trước 20/10 Hùng tự commit "update" (16/08 958f826 Add project files; 02/09 35a98bd giao nhận + WTS; 22/09 bdc00b7 Part Master nhiều NC; 28/09 98bd760 giao ngược chỉ NG). **SQL đã chạy trên MES**: 01–05 (VULOI 28/10, RCSI 31/10) không còn file → 06 (01/11) → 09, 08 (05/11) → 07 (06/11) → 10–14 (06/11) → 15 (11/11).

## 13. Cách cập nhật bản đồ + cách làm việc

**Gói sau sửa gì → tab nào (bản đồ gốc)**: trang / service / quyền → tab 2 + 5 · cách tính sổ → tab 3 + 9 + 6 · bảng / cột / SQL → tab 4 + 12.5 · luật mới / đổi luật → tab 6 + 9.9 + 11 · kịch bản / driver → tab 7 · .bat / trạm / EOL → tab 8 · bẫy mới → tab 10 · việc mở → tab 11 (+ Sổ luật) · mỗi gói push → tab 12 · quy trình đổi → tab 0 + 13. Ghi "đối chiếu code <commit> ngày <ngày>"; không xoá dòng cũ khi luật đổi. Bản sao `CODEBASE-MAP.md` gốc repo: cập nhật cùng lượt khi đổi tab 0–5 (tóm tắt, không bí mật); không đặt `|` trong ô bảng (dùng ∣).

**Checklist thêm vào sổ chung**: chứng từ / bảng mới → entity + `AppDbContext` DbSet + SQL `tools/sql/NN` → `LedgerInput` + 3 đường nạp → `Compute` → `HandoverWhy.Clone` + `H10Help.CloneInput` → `AppDbContextStub` + `KeyName` EfStub → `HarnessFixup` + stub "Old" → kịch bản + đáp án độc lập → QuetSo → DanhMucKiem · ô số mới → trường `LedgerGroup`/`LedgerRow` → `Compute` → `NegativeCells` (K5) → `Balanced` → `WhereIs` → `BuildSteps` → chữ Handover_Index → `CheckAll` fuzz · loại phiếu mới → `HandoverPurpose` hằng + Label + `IsOut`/`OutTarget` → `HandoverCell.ResolveCells` / `IssueCellOf` / `DefaultToCell` → `Classify` → `Compute` → `HandoverForm` → `IssueByCellAsync` / `SaveFormAsync` → `HandoverWhy` StepOp → `HandoverNgTrace` → QuetSo → kịch bản `form` + fuzz → Sổ luật mục N mới · chỗ ghi số mới → khuôn mục 0 luật 2, tên `*OnceAsync` + `RetryDeadlockAsync`, `PermCheck` trước khoá.

**Cách làm việc với Hùng (rút gọn CLAUDE-RULES 0.1)**: (1) đầu chat đọc CLAUDE-RULES mục 0 → memory `ways-of-working`, `areas/wts-service-va-kiem-chuoi` (Bàn giao + VIỆC TIẾP A/B/C/D), `areas/giao-nhan-so-luat` → Sổ luật tab "Lỗi cần sửa" → bản đồ tab 0; `git log -1` = mốc; xin quyền `mes-prototype`, so file ↔ HEAD trước khi ghi · (2) trước khi code gửi bản "em hiểu" (mốc, việc A tự làm / B cần Hùng / C chờ khách, việc cần SQL / đổi luật); Hùng OK mới code; việc tự làm được thì làm · (3) Claude cầm in/out cả chuỗi Part Master → KH → Kho → Giao nhận → WTS; vênh số → tự vá rồi báo; yêu cầu phá truy vết → phản biện; việc LỚN → kế hoạch từng file chờ duyệt · (4) sửa surgical, tag `[TÊN ngày]` + "Nguồn:", giữ logic cũ, giao file đầy đủ (không snippet), chữ xưởng theo Excel khách (GN-TEXT), UI mới theo mẫu Index_PartMaster / Detail_Partmaster · (5) kiểm đủ lớp (mục 7) → ghi máy Hùng (mục 8) → trạm build + kiem · (6) mỗi việc xong, TRƯỚC khi soạn commit: Sổ luật (dòng còn mở → Đã làm, KHÔNG xoá) + memory Bàn giao + bản đồ → báo Hùng chỗ đã cập nhật · (7) lời commit `tools\claude_commit_msg.txt` nhóm theo vùng, mỗi dòng có tag · (8) bàn giao chat: "Bàn giao <ngày> (chat <giờ>)" vào memory `wts-service-va-kiem-chuoi` VÀ đầu tab "Lỗi cần sửa"; chat dài → chủ động sang chat mới · (9) tiếng Việt, xưng "mình / anh em"; tài liệu gửi khách không xưng mình, không chữ nội bộ ("làm hộ"); không đoán — chưa chắc thì hỏi hoặc viết ca kiểm chạy thật.

**Câu dán đầu chat mới (mẫu CLAUDE-RULES 0.2, điền mốc trước khi gửi)**:

```
Đọc rule project trước khi làm gì (không cần anh nhắc lại): CLAUDE-RULES.md mục 0 (0.1 + 0.2) + memory ways-of-working, wts-service-va-kiem-chuoi (mục "Bàn giao <ngày> (chat <giờ>)" mới nhất + VIỆC TIẾP nhóm A/B/C/D), giao-nhan-so-luat; Sổ luật Giao nhận tab "Lỗi cần sửa" (đoạn bàn giao đầu tab + bảng Tổng hợp còn mở); Bản đồ phần mềm tab 0. Clone repo, master phải có <commit / tag>. Xin quyền thư mục mes-prototype nếu chưa nối, so file máy anh với git HEAD trước khi ghi.
Chat này làm: <phạm vi>.
Trước khi code gửi anh bản "em hiểu" (mục 0.1 bước 8). Mỗi việc xong cập nhật sổ + memory + bản đồ rồi báo chỗ đã cập nhật (bước 9). Vẫn cân số in/out, vênh ở đâu báo. Mỗi gói kiểm đủ lớp (kịch bản + fuzz nặng + đột biến + rà độc lập), soạn commit.
```
