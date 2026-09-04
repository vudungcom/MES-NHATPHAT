# CODEBASE-MAP — MES.Web

> Ngày tạo: 2026-09-03  
> Stack: ASP.NET Core 8 · Blazor Server InteractiveServer · EF Core 8 · SQL Server · Cookie Auth

---

## MỤC LỤC

1. [Cấu trúc thư mục](#1-cấu-trúc-thư-mục)
2. [Trang Razor (Pages)](#2-trang-razor-pages)
3. [Services](#3-services)
4. [Entities & DB Tables](#4-entities--db-tables)
5. [Flows end-to-end](#5-flows-end-to-end)
6. [Permission Matrix](#6-permission-matrix)
7. [Business Rules quan trọng](#7-business-rules-quan-trọng)
8. [TODOs còn lại](#8-todos-còn-lại)

---

## 1. Cấu trúc thư mục

```
MES.Web/
├── Components/
│   ├── App.razor                   — Blazor root, cấu hình router
│   ├── Routes.razor                — Route chính + AuthorizeRouteView
│   ├── RedirectToLogin.razor       — Redirect user chưa đăng nhập
│   ├── Layout/
│   │   ├── MainLayout.razor        — Layout chính: sidebar + topbar
│   │   ├── NavMenu.razor           — Menu trái (hiện/ẩn theo quyền)
│   │   └── LoginLayout.razor       — Layout trang Login (không có sidebar)
│   ├── Pages/
│   │   ├── Login.razor             — Trang đăng nhập
│   │   ├── Home.razor              — Trang chủ (dashboard)
│   │   ├── Profile.razor           — Hồ sơ cá nhân + đổi PIN
│   │   ├── About/Index.razor       — Thông tin bản quyền / phiên bản
│   │   ├── PartMaster/
│   │   │   ├── Index_PartMaster.razor
│   │   │   ├── Create.razor
│   │   │   └── Detail.razor
│   │   ├── KhPlan/
│   │   │   ├── KhPlanIndex.razor
│   │   │   ├── Create.razor
│   │   │   ├── Edit.razor
│   │   │   ├── View.razor
│   │   │   └── BulkUpdateStd.razor
│   │   ├── Setting/Settings_Index.razor
│   │   ├── Customer/Index.razor
│   │   ├── Thietbi/Index.razor
│   │   ├── DoGa/Index.razor
│   │   ├── Dao/Index.razor
│   │   ├── Kho/Index.razor
│   │   ├── SoDoToChuc/Index.razor
│   │   ├── WTStieuchuan/Index.razor
│   │   ├── Handover/Index.razor
│   │   ├── HieuSuatCongNhan/HieuSuat_Index.razor
│   │   └── HieuSuatMay/Index.razor
│   └── Shared/
│       ├── AttributePicker.razor   — Dropdown chọn giá trị PartMasterAttribute
│       ├── PinConfirmModal.razor   — Modal nhập PIN xác nhận đổi default
│       └── SplitPOModal.razor      — Modal tách 1 PO thành nhiều PO con
├── Constants/
│   └── SystemPermissions.cs        — Danh sách section code + PermissionSection + GroupPermissionSetting
├── Data/
│   ├── AppDbContext.cs             — DbContext tất cả entities
│   ├── SeedData.cs                 — Seed admin user + group mặc định
│   └── Entities/                   — 30+ entity classes (xem mục 4)
├── Services/                       — 20+ scoped services (xem mục 3)
├── Program.cs                      — DI, middleware, auth cookie, login/logout endpoints
└── appsettings.json                — ConnectionString: DefaultConnection
```

---

## 2. Trang Razor (Pages)

### `/login` — Login.razor
- **Permission:** public (không cần auth)
- **Inject:** (dùng form POST → `/login-submit`)
- **Cách hoạt động:** Blazor render form → submit POST → `AuthService.VerifyCredentialsAsync()` → `SignInAsync` với cookie → redirect.

### `/` — Home.razor
- **Permission:** `[Authorize]`
- **Inject:** `AuthenticationStateProvider`
- Trang chủ dashboard (hiện thông tin người dùng, link nhanh).

### `/part-master` — Index_PartMaster.razor
- **Permission:** `[Authorize]` + check `_canView` (bất kỳ quyền PartMaster nào)
- **Inject:** `PartMasterListService`, `PartMasterService`, `UserService`, `AuthenticationStateProvider`, `IJSRuntime`
- Danh sách Part với filter Excel-style per column, sort, tìm kiếm.
- Nút **Tạo Part mới** → `/part-master/create` (chỉ hiện khi `_canCreate`).
- Nút **Xuất Excel** qua `PartMasterListService`.

### `/part-master/create` — Create.razor
- **Permission:** `[Authorize]` + `PART_TAO_MOI.CanEdit == true`
- **Inject:** `PartMasterService`, `UserService`, `AppDbContext`, `NavigationManager`, `AuthenticationStateProvider`
- Form nhập: `PartNo` (duy nhất, không sửa sau), `PartName`, `CustomerId`, `Material`, `MaterialConfig`, `MaterialNote`.
- **Save** → `PartMasterService.CreatePartMasterAsync()` → redirect `/part-master/detail?partNo=...`.

### `/part-master/detail?partNo=` — Detail.razor
- **Permission:** `[Authorize]` + kiểm tra 5 vùng A/B/C/D/E riêng lẻ qua `UserSvc.GetAllUserPermissionsAsync()`
- **Inject:** `PartMasterService`, `PartMasterListService`, `PartMachiningService`, `PartTaroService`, `PartBaviaService`, `PartWashingService`, `PartInspectionService`, `PartPackagingService`, `StandardWtsTaskService`, `ThietBiService`, `DoGaService`, `DaoService`, `UserService`, `AppDbContext`
- 5 vùng: A (Kế hoạch), B (Gia công), C (Taro/Bavia/Rửa), D (KCS), E (Đóng gói).
- Mỗi vùng hiện/ẩn theo `_canViewX`, sáng tối theo `_canEditX`.
- **Lưu nháp** → `DoSaveAsync(confirmOk: false)` → set `IsXxxConfirmed = false`.
- **Lưu & Xác nhận OK** → `DoSaveAsync(confirmOk: true)` → set `IsXxxConfirmed = true`.
- **Ngưng sử dụng** → `PartMasterService.SetObsoleteAsync(obsolete: true)` (cần `CanDelete = true`).
- Modal **lịch sử thay đổi** → `PartProcessStepChangeLogs` theo `StepTable` + `StepId`.
- Modal **máy đồng dạng** → chọn từ `ThietBiList`.
- Modal **máy loại trừ** → `MachineExcludeSettings` (global setting, áp dụng tất cả Part).
- **PIN confirm modal** → chỉ bật khi user tích "Đặt làm mặc định" cho Material/Config/Note.

### `/kh-plan` — KhPlanIndex.razor
- **Permission:** `[Authorize]` + `KE_HOACH.CanView`
- **Inject:** `KhPlanService`, `UserService`, `AppDbContext`, `AuthenticationStateProvider`
- Bảng danh sách tất cả KhPlanDetail (JOIN KhPlan + Customer + KhoVatLieu).
- Filter/sort per column (giống PartMaster Index).
- Badge snapshot GC/HTSP/KCS/ĐG từ `KhPlanService.GetSnapshotSummaryAsync()`.
- Nút **Xóa dòng** → xác nhận PIN → `KhPlanService.DeleteDetailAsync()`.
- Nút **Tách PO** → `SplitPOModal` → `KhPlanService.SplitPOAsync()`.

### `/kh-plan/create` — KhPlan/Create.razor
- **Permission:** `[Authorize]` + `KE_HOACH.CanEdit`
- **Inject:** `AppDbContext`, `KhPlanService`, `PartMasterService`, `CustomerService`, `AuthenticationStateProvider`
- Form header (PlanDate, ReceivedDate, CustomerId) + N dòng detail.
- Nhập `PartNo` → `OnPartNoChangedAsync()` → auto-fill Material/Config/Note từ `PartMasterAttributes` default.
- Part không có trong DB → tự động tạo mới qua `PartMasterService.CreatePartMasterAsync()`.
- **Xác nhận KH** → `KhPlanService.CreateAsync()` + snapshot route B→E qua `CopyRouteSnapshotAsync()` (gọi trong CreateAsync).

### `/kh-plan/{Id}/detail/{DetailId}/edit` — KhPlan/Edit.razor
- **Permission:** `[Authorize]` + `KE_HOACH.CanEdit`
- **Inject:** `KhPlanService`
- Sửa 1 KhPlanDetail: Quantity, Unit, STD, Type, Priority, Material, MaterialConfig, Location, Notes.
- Mọi thay đổi ghi `KhPlanDetailChangeLog` qua `KhPlanService.UpdateFullAsync()`.

### `/kh-plan/{Id}/detail/{DetailId}` — KhPlan/View.razor
- **Permission:** `[Authorize]` + `KE_HOACH.CanView`
- **Inject:** `KhPlanService`
- Xem chi tiết 1 KhPlanDetail: snapshot route B→E, WTS logs, ChangeLog lịch sử.
- Nút **Làm mới quy trình** → `KhPlanService.RefreshRouteSnapshotAsync()`.

### `/kh-plan/bulk-update-std` — KhPlan/BulkUpdateStd.razor
- **Permission:** `[Authorize]` + `KE_HOACH.CanEdit`
- Upload file Excel → parse STD mới → `KhPlanService.BulkUpdateStdAsync()`.

### `/settings` — Settings_Index.razor
- **Permission:** `[Authorize]` + `SETTING_ADMIN.CanEdit`
- **Inject:** `UserService`
- Quản lý UserGroups + Users + Ma trận phân quyền (JSON `GroupPermissionSetting[]` per group).

### `/wts-tieu-chuan` — WTStieuchuan/Index.razor
- **Permission:** `[Authorize]` + `WTS_MASTER.CanView`
- **Inject:** `StandardWtsTaskService`
- CRUD `StandardWtsTask` (danh mục WTS chuẩn) theo category: TARO, BAVIA, RUA, KCS, KT_NC, DONG_GOI.

### `/thiet-bi` — Thietbi/Index.razor
- **Permission:** `[Authorize]` + `THIET_BI.CanView`
- **Inject:** `ThietBiService`
- CRUD thiết bị máy móc + `ThietBiChangeLog`.

### `/do-ga` — DoGa/Index.razor
- **Permission:** `[Authorize]` + `DO_GA.CanView`
- **Inject:** `DoGaService`
- CRUD đồ gá + mượn/trả log + `DoGaChangeLog`.

### `/dao` — Dao/Index.razor
- **Permission:** `[Authorize]` + `DAO.CanView`
- **Inject:** `DaoService`
- CRUD dao cụ + `DaoChangeLog`.

### `/customers` — Customer/Index.razor
- **Permission:** `[Authorize]` + `KHACH_HANG.CanView`
- **Inject:** `CustomerService`
- CRUD khách hàng + `CustomerChangeLog`.

### `/kho` — Kho/Index.razor
- **Permission:** `[Authorize]` + `KHO.CanView`
- **Inject:** `KhoVatLieuService`
- Quản lý kho vật liệu + `StockTransaction`.

### `/so-do-to-chuc` — SoDoToChuc/Index.razor
- **Permission:** `[Authorize]` + `SO_DO_TC.CanView`
- **Inject:** `DepartmentService`
- Sơ đồ tổ chức phòng ban (tree view) + `DepartmentChangeLog`.

### `/handover` — Handover/Index.razor
- **Permission:** `[Authorize]` + bất kỳ `HANDOVER_*` section
- **Inject:** `HandoverService`
- Giao/nhận hàng giữa các nhóm công đoạn (KHO→GC→HTSP→KCS→PKG).

### `/hieu-suat-cong-nhan` — HieuSuat_Index.razor
- **Permission:** `[Authorize]`
- Báo cáo hiệu suất công nhân từ `WtsProductionLogs`.

### `/hieu-suat-may` — HieuSuatMay/Index.razor
- **Permission:** `[Authorize]`
- Báo cáo hiệu suất máy từ `WtsProductionLogs`.

### `/about` — About/Index.razor
- **Permission:** `[Authorize]`
- Hiển thị thông tin bản quyền, phiên bản, thông tin license (đọc từ `ServerLicenseHelper`).

### `/profile` — Profile.razor
- **Permission:** `[Authorize]`
- Tự đặt/đổi PIN cá nhân.

---

## 3. Services

### `AuthService`
- `VerifyCredentialsAsync(username, password)` → query `Users` + `PasswordHasher.Verify()` → trả `User?`
- **DB tables:** `Users`, `UserGroups`

### `UserService`
- `GetAllGroupsAsync()` → `UserGroups`
- `CreateGroupAsync()`, `UpdateGroupAsync()` → `UserGroups`
- `UpdateGroupPermissionsAsync(groupId, perms)` → serialize `List<GroupPermissionSetting>` → ghi vào `UserGroup.Permissions` (JSON)
- `GetGroupPermissions(json)` → deserialize `List<GroupPermissionSetting>`
- `GetAllUserPermissionsAsync(userId)` → `Users` + `UserGroups` → trả `Dictionary<SectionCode, (CanView, CanEdit)>`
- `GetCanDeleteAsync(userId, sectionCode)` → đọc JSON → lấy `GroupPermissionSetting.CanDelete`
- `CreateUserAsync()`, `UpdateUserAsync()` → `Users`
- `ResetPinAsync(targetUserId, requestedBy)` → chỉ ADMIN → set `User.PIN = null`
- **DB tables:** `Users`, `UserGroups`

### `PartMasterService`
- `CreatePartMasterAsync(partNo, customerId, initialAttributes, createdBy, partName?)` → tạo `PartMaster` + `PartMasterAttribute[]` → set `IsPlanConfirmed = true` ngay khi tạo
- `GetAttributesGroupedAsync(partId)` → `PartMasterAttributes` group by `AttributeType`
- `GetDefaultValueAsync(partId, attributeType)` → lấy attribute IsDefault=true
- `GetDefaultsAsync(List<int> partIds)` → batch load defaults nhiều Part cùng lúc
- `AddValueAsync(partId, type, value, createdBy)` → thêm 1 giá trị vào `PartMasterAttributes`
- `SetAsDefaultAsync(partId, type, value, changedBy)` → transaction: unset old default → set new default
- `UpdatePartNameAsync(partId, newPartName, userId)` → `PartMasters`
- `SetObsoleteAsync(partId, userId, obsolete)` → kiểm tra `CanDelete` → set `IsObsolete`
- `GetByPartNoAsync(partNo)` → `PartMasters` + `Customer`
- `GetUserContextAsync(userId)` → trả `UserContext` (GroupCode, Role)
- **DB tables:** `PartMasters`, `PartMasterAttributes`, `Users`, `UserGroups`

### `PartMachiningService` (tương tự cho Taro, Bavia, Washing, Inspection, Packaging)
- `GetActiveByPartAsync(partId)` → `PartMachiningSteps` where `IsActive=true`, kèm `CreatedByUser`, `UpdatedByUser`
- `GetAllByPartAsync(partId)` → bao gồm cả `IsActive=false` (dữ liệu cũ trước v0.7)
- `AddAsync(partId, step, userId, reason)` → insert `PartMachiningStep` → ghi log `Created` vào `PartProcessStepChangeLogs`
- `UpdateAsync(stepId, updated, userId, reason)` → diff từng field → ghi log từng field thay đổi → update step
- `SoftDeleteAsync(stepId, userId, reason)` → **HARD DELETE** (v0.7+): snapshot toàn bộ → ghi log `Deleted` → `Remove()` khỏi DB
- **DB tables:** `PartMachiningSteps`, `PartProcessStepChangeLogs`, `Users`, `UserGroups`

> Quyền kiểm tra: `PartMasterPermissionHelper.CanEditArea(user.Group, Area)` → đọc JSON Permissions của Group.

### `PartMasterListService`
- `GetAllAsync()` → query `PartMasters` + `PartMasterAttributes` + `Customers`
- `ExportExcelAsync()` → dùng `ClosedXML` xuất file Excel
- **DB tables:** `PartMasters`, `PartMasterAttributes`, `Customers`

### `KhPlanService`
- `GenerateNextPlanNoAsync(date)` → đếm số phiếu trong ngày → `KH-YYYYMMDD-####`
- `CreateAsync(plan, userId)` → insert `KhPlan` + `KhPlanDetail[]` (LineNo tự tăng)
- `GetByIdAsync(id)` → `KhPlans` + `Customer` + `CreatedByUser` + `Details`
- `GetDetailByIdAsync(khPlanDetailId)` → lấy detail + plan cha
- `GetListAsync()` → list `KhPlans` + filter Customer/Status, max 500
- `GetDetailRowsAsync()` → JOIN `KhPlanDetails` + `KhPlans` + `Customers` + `KhoVatLieus`, max 1000
- `UpdateFieldAsync(id, field, newValue, changedBy)` → cập nhật 1 field + ghi `KhPlanDetailChangeLog`
- `UpdateFullAsync(id, newValues, changedBy, reason)` → diff nhiều field → ghi log mỗi field đổi
- `SplitPOAsync(sourceId, newRows, changedBy, reason)` → transaction: tạo N detail mới → hard delete gốc → ghi log `SplitFrom` / `SplitInto` → renumber LineNo
- `BulkUpdateStdAsync(rows, changedBy, reason)` → batch update STD từ Excel import + log `ExcelImport`
- `DeleteDetailAsync(khPlanDetailId, deletedBy)` → hard delete: xóa snapshots B→E, WTS logs, change logs, KhoVatLieu, detail → nếu KhPlan trống → xóa luôn KhPlan
- `GetChangeHistoryAsync(khPlanDetailId)` → `KhPlanDetailChangeLogs`
- `CopyRouteSnapshotAsync(khPlanDetailId, partId, snapshotBy)` → copy 6 bảng step từ PartMaster → 6 bảng Snapshot
- `RefreshRouteSnapshotAsync(khPlanDetailId, partId, userId, reason)` → transaction: xóa snapshot cũ → copy lại → ghi log `RouteSnapshot`
- `GetSnapshotSummaryAsync(List<int> detailIds)` → batch count snapshot mỗi loại → `SnapshotSummary`
- `GetRouteSnapshotAsync(khPlanDetailId)` → lấy 6 bảng snapshot đầy đủ
- `GetWtsLogsAsync(khPlanDetailId)` → `WtsProductionLogs` + `Worker`
- `GetWtsQtySummaryAsync(khPlanDetailId)` → tổng `QtyDone` theo `ProcessGroup|NC`
- **DB tables:** `KhPlans`, `KhPlanDetails`, `KhPlanDetailChangeLogs`, `KhPlanRouteSnapshot*` (×6), `WtsProductionLogs`, `KhoVatLieus`, `Customers`, `Users`

### `StandardWtsTaskService`
- `GetAllActiveAsync()` → `StandardWtsTasks` where `IsActive=true`
- `CreateAsync()`, `UpdateAsync()` → `StandardWtsTasks` + ghi `StandardWtsTaskChangeLog`
- **DB tables:** `StandardWtsTasks`, `StandardWtsTaskChangeLogs`

### `ThietBiService`
- `GetAllActiveAsync()` → `ThietBis` where `IsActive=true`
- CRUD + `ThietBiChangeLog`
- **DB tables:** `ThietBis`, `ThietBiChangeLogs`

### `DoGaService`
- `GetAllActiveAsync()` → `DoGas`
- CRUD, mượn/trả log + `DoGaChangeLog`
- **DB tables:** `DoGas`, `DoGaMuonTraLogs`, `DoGaChangeLogs`

### `DaoService`
- `GetAllAsync(includeInactive)` → `Daos`
- CRUD + `DaoChangeLog`
- **DB tables:** `Daos`, `DaoChangeLogs`

### `CustomerService`
- `GetAllAsync()` → `Customers`
- `CreateAsync()`, `UpdateAsync()` + `CustomerChangeLog`
- **DB tables:** `Customers`, `CustomerChangeLogs`

### `DepartmentService`
- Tree node phòng ban + `DepartmentChangeLog`
- **DB tables:** `Departments`, `DepartmentChangeLogs`

### `KhoVatLieuService`
- Quản lý kho vật liệu, tồn kho, vị trí
- `StockTransaction` ghi nhận xuất/nhập
- **DB tables:** `KhoVatLieus`, `KhoVatLieuChangeLogs`, `StockTransactions`

### `HandoverService`
- Giao nhận hàng giữa nhóm: KHO→GC→HTSP→KCS→PKG
- `IssueAsync()` → tạo `HandoverTransaction` (PENDING)
- `ReceiveAsync()` → tạo `HandoverReceive` → cập nhật status PENDING/PARTIAL/COMPLETED
- Kiểm tra `baseQty` từ snapshot: nếu chưa có snapshot → chặn giao (line 808)
- **DB tables:** `HandoverTransactions`, `HandoverReceives`, `KhPlanRouteSnapshotMachining`

### `PartMasterPermissionHelper` (static helper)
- `CanEditArea(UserGroup?, PartMasterArea)` → deserialize JSON Permissions → tìm `SectionCode` → trả `CanEdit`
- `CheckPermission(UserGroup?, sectionCode, requireEdit)` → ADMIN bypass → đọc JSON
- Enum `PartMasterArea`: A_KeHoach, B_Machining, C_HoanThienSP, D_KiemTra, E_DongGoi
- Map Area → SectionCode: A→PART_TAO_MOI, B→PART_GC, C→PART_HTSP, D→PART_KCS, E→PART_DONGGOI

### `AuthService`
- `VerifyCredentialsAsync(username, password)` → `Users` + `PasswordHasher.Verify()`

### `ExcelHelper`
- `CreateStdUpdateTemplate()` → tạo file Excel template cho BulkUpdateStd (dùng ClosedXML)

### `LicenseBackgroundWorker` (IHostedService)
- Chạy ngầm, định kỳ kiểm tra/gia hạn license file (`MES_LicenseData/`)
- Dùng `ServerLicenseHelper` để đọc/ghi file license mã hóa

---

## 4. Entities & DB Tables

### `PartMasters`
```
PartId          int PK
PartNo          nvarchar(100) UNIQUE NOT NULL
PartName        nvarchar(200)
MaterialConfig  nvarchar(100)    -- snapshot field cũ, hiện dùng PartMasterAttributes
Material        nvarchar(100)    -- snapshot field cũ
CustomerId      int FK→Customers
ProcessNo       nvarchar(50)
Type            nvarchar(50)
LocationDefault nvarchar(50)
IsActive        bit DEFAULT 1
IsObsolete      bit DEFAULT 0    -- "Ngưng sử dụng" — còn trong DB, ẩn khỏi dropdown tạo mới
CreatedAt       datetime
IsPlanConfirmed      bit         -- Vùng A confirmed
IsMachiningConfirmed bit         -- Vùng B confirmed
IsHtspConfirmed      bit         -- Vùng C confirmed
IsKcsConfirmed       bit         -- Vùng D confirmed
IsPkgConfirmed       bit         -- Vùng E confirmed
```
- Index: `PartNo` UNIQUE

### `PartMasterAttributes`
```
AttributeId     int PK
PartId          int FK→PartMasters (CASCADE DELETE)
AttributeType   nvarchar (Material | MaterialConfig | MaterialNote)
Value           nvarchar
IsDefault       bit     -- chỉ 1 giá trị IsDefault=true mỗi (PartId, AttributeType)
IsActive        bit
CreatedBy       int FK→Users (NO ACTION)
CreatedAt       datetime
```
- Index: (PartId, AttributeType, IsDefault), (PartId, AttributeType, Value)

### `PartMachiningSteps`
```
StepId              bigint PK
PartId              int FK→PartMasters (CASCADE)
StepOrder           int
NC                  nvarchar(20) NOT NULL   -- "NC01", "NC01-DP-01"
Drawing             nvarchar(100)
MachineRegistered   nvarchar(50)
MachineAlternative  nvarchar(200)           -- CSV nhiều máy
FixtureType         nvarchar(50)
ToolType            nvarchar(100)
TimingMachine       nvarchar(50)
IsBackup            bit DEFAULT 0
ParentNC            nvarchar(20)            -- NC gốc nếu IsBackup=true
SetupTime           decimal(10,2)           -- phút
MachiningTime       decimal(10,2)
InspectionTime      decimal(10,2)
PreparationTime     decimal(10,2)
TrialRunTime        decimal(10,2)
CreatedBy           int FK→Users
CreatedAt           datetime
UpdatedBy           int FK→Users
UpdatedAt           datetime
IsActive            bit DEFAULT 1
```

### `PartTaroSteps`, `PartBaviaSteps`, `PartWashingSteps`, `PartInspectionSteps`, `PartPackagingSteps`
Cấu trúc tương tự nhau (bỏ các cột machine-specific):
```
StepId        bigint PK
PartId        int FK→PartMasters (CASCADE)
StepOrder     int
NC            nvarchar(20)        -- Mã công việc (WTS TaskCode với Bavia/Washing)
WtsTaskCode   nvarchar(50)        -- chỉ có ở TaroStep: code WTS tiêu chuẩn
StepName      nvarchar(250)       -- tên công đoạn (fill từ StandardWtsTask.TaskName)
StandardTime  decimal(10,2)       -- phút
IsBackup      bit
ParentNC      nvarchar(20)
CreatedBy     int FK→Users
CreatedAt     datetime
UpdatedBy     int FK→Users
UpdatedAt     datetime
IsActive      bit
```

### `PartProcessStepChangeLogs`
```
ChangeId    bigint PK
StepTable   nvarchar(20)    -- Machining | Taro | Bavia | Washing | Inspection | Packaging
StepId      bigint          -- LOOSE ref (không có FK constraint — xóa step vẫn giữ log)
PartId      int             -- redundant để query nhanh
FieldName   nvarchar(50)    -- tên field HOẶC Created | Deleted | Restored
OldValue    nvarchar(max)
NewValue    nvarchar(max)
ChangedBy   int FK→Users
ChangedAt   datetime
Reason      nvarchar(500) NOT NULL min 3 ký tự
```
- **APPEND-ONLY** — không bao giờ UPDATE/DELETE

### `KhPlans`
```
KhPlanId    int PK
PlanNo      nvarchar(30) UNIQUE    -- "KH-YYYYMMDD-####"
PlanDate    datetime               -- ngày ghi trên PO khách
ReceivedDate datetime?             -- ngày công ty thực nhận PO
CustomerId  int FK→Customers
Notes       nvarchar(500)
AttachmentFilePath nvarchar(500)
Status      nvarchar(30)           -- WaitingSupplierOrder | ...
CreatedBy   int FK→Users
CreatedAt   datetime
ConfirmedBy int?
ConfirmedAt datetime?
```

### `KhPlanDetails`
```
KhPlanDetailId  int PK
KhPlanId        int FK→KhPlans (CASCADE)
LineNo          int
PartNo          nvarchar(100) NOT NULL
PurchaseOrder   nvarchar(50) NOT NULL
OldPurchaseOrder nvarchar(50)?  -- PO gốc nếu được tách từ SplitPO
ManufacturingOrder nvarchar(50)?
Quantity        decimal(18,4)
Unit            nvarchar(10)   -- EA | PCS | SET
STD             datetime       -- Ship To Deadline
Type            nvarchar(100)
Priority        nvarchar(100)
MaterialConfig  nvarchar(100)  -- snapshot tại thời điểm tạo phiếu
Material        nvarchar(100)
OrderVL         nvarchar(50)
Location        nvarchar(50)   -- vị trí kho — nhập tự do từ v0.5, không link PartMaster
MaterialCondition nvarchar(200)
PartNotes       nvarchar(500)
MaterialNotes   nvarchar(1000)
Status          nvarchar(30)   -- WaitingSupplierOrder | ...
CreatedAt       datetime
UpdatedAt       datetime?
```

### `KhPlanDetailChangeLogs`
```
ChangeId        bigint PK
KhPlanDetailId  int FK→KhPlanDetails (NO ACTION)
FieldName       nvarchar(50)    -- STD | Quantity | OrderVL | Location | SplitFrom | SplitInto | RouteSnapshot...
OldValue        nvarchar(500)
NewValue        nvarchar(500)
ChangedBy       int FK→Users
ChangedAt       datetime
ChangeSource    nvarchar(30)    -- Manual | ExcelImport | API
ImportBatchId   bigint?         -- group các dòng cùng 1 lần import
Reason          nvarchar(500)
```
- **APPEND-ONLY**

### `KhPlanRouteSnapshot*` (6 bảng: Machining, Taro, Bavia, Washing, Inspection, Packaging)
```
SnapshotId      bigint PK
KhPlanDetailId  int FK→KhPlanDetails (NO ACTION)
SourcePartId    int             -- PartId tại thời điểm snapshot
SnapshotAt      datetime
SnapshotBy      int FK→Users
StepOrder       int
NC              nvarchar(20)
[... các field step tương ứng ...]
```
- Copy từ bảng `Part*Steps` khi tạo/refresh phiếu KhPlan
- Không thay đổi sau khi tạo (trừ khi gọi `RefreshRouteSnapshotAsync()`)

### `WtsProductionLogs`
```
WtsLogId        bigint PK
KhPlanDetailId  int FK→KhPlanDetails
ProcessGroup    nvarchar(10)    -- GC | TARO | BAVIA | WASHING | KCS | PKG
NC              nvarchar(20)?   -- null với C/D/E
WtsCode         nvarchar(20)?   -- null với GC
WorkerId        int FK→Users
MachineUsed     nvarchar(50)?
QtyDone         decimal(10,2)
StartTime       datetime
EndTime         datetime
SetupTime_Actual, MachiningTime_Actual, InspectionTime_Actual, ...  -- chỉ GC
Notes           nvarchar(500)
CreatedAt       datetime
IsVoided        bit DEFAULT 0
VoidReason      nvarchar(200)
VoidedBy        int? FK→Users
VoidedAt        datetime?
```
- **APPEND-ONLY** — sai thì Void + tạo dòng mới đúng, không UPDATE/DELETE

### `HandoverTransactions`
```
HandoverTxId    bigint PK
KhPlanDetailId  int FK→KhPlanDetails
FromGroupCode   nvarchar(20)   -- KHO | GC | HTSP | KCS | PKG
ToGroupCode     nvarchar(20)
QtyIssued       decimal(10,2)
FromNC          nvarchar(20)?  -- NC nguồn (giao inter-NC)
Status          nvarchar(20)   -- PENDING | PARTIAL | COMPLETED
IssuedBy        int FK→Users
IssuedAt        datetime
Notes           nvarchar(500)
IsVoided        bit
VoidReason      nvarchar(200)
VoidedBy        int? FK→Users
```

### `HandoverReceives`
```
ReceiveId       bigint PK
HandoverTxId    bigint FK→HandoverTransactions
QtyOk           decimal(10,2)
QtyNg           decimal(10,2)
NgReason        nvarchar(500)
ReceivedBy      int FK→Users
ReceivedAt      datetime
Notes           nvarchar(500)
IsVoided        bit
```

### `StandardWtsTasks`
```
TaskId          int PK
CategoryCode    nvarchar(50)    -- TARO | BAVIA | RUA | KCS | KT_NC | DONG_GOI
CategoryName    nvarchar(150)
TaskCode        nvarchar(50) UNIQUE  -- khóa chuẩn: "1T", "3V1", "KM1"...
TaskName        nvarchar(250)
DefaultUnit     nvarchar(50)
StandardTimeSec int?            -- thời gian chuẩn tính theo giây
GhiChu          nvarchar(500)
DisplayOrder    int
IsActive        bit
CreatedAt       datetime
CreatedBy       int
UpdatedAt       datetime?
UpdatedBy       int?
```

### `Users` & `UserGroups`
```
Users:
  UserId        int PK
  Username      nvarchar(50) UNIQUE
  FullName      nvarchar(100)
  PasswordHash  nvarchar(200)   -- BCrypt hash
  PIN           nvarchar(20)?   -- PIN số để confirm thao tác nhạy cảm
  GroupId       int? FK→UserGroups
  IsActive      bit
  CreatedAt     datetime
  LastLoginAt   datetime?

UserGroups:
  GroupId       int PK
  GroupCode     nvarchar(50) UNIQUE   -- ADMIN, KY_THUAT, PLANNING...
  GroupName     nvarchar(100)
  Permissions   nvarchar(max)?        -- JSON: List<GroupPermissionSetting>
  IsActive      bit
```

### `MachineExcludeSettings`
```
Id          int PK
SoMay       nvarchar UNIQUE    -- số máy bị loại trừ (toàn hệ thống)
AddedAt     datetime
AddedByUserId int? FK→Users
```
- Global setting — áp dụng cho TẤT CẢ Part. NC chạy trên máy này không tính vào cycle time chính.

### `ThietBis`
```
ThietBiId   int PK
SoMay       nvarchar UNIQUE (where IsActive=1)
TenMay      nvarchar
LoaiMay     nvarchar
BoPhan      nvarchar
IsActive    bit
```

### `KhoVatLieus`
```
KhoVatLieuId    int PK
PlanDetailId    int → KhPlanDetails
OrderVatLieu    nvarchar
ViTriDePhoi     nvarchar
TinhTrangPhoi   nvarchar
...
```

---

## 5. Flows end-to-end

### Flow 1: Tạo Part mới

```
[UI] Create.razor — nút "Tạo Part Master"
    ↓  OnInitializedAsync: check PART_TAO_MOI.CanEdit
    ↓  form nhập PartNo, PartName, CustomerId, Material, MaterialConfig, MaterialNote
    ↓  nút Tạo → SaveAsync()
         ↓  validate PartNo không rỗng
         ↓  gọi PmSvc.CreatePartMasterAsync(partNo, customerId, attrs, userId, partName)
              ↓  EnsurePermissionAAsync(userId) — check PART_GC = PART_TAO_MOI.CanEdit
              ↓  check PartNo unique (PartMasters)
              ↓  INSERT PartMasters: IsPlanConfirmed=TRUE (người tạo = đã confirm vùng A)
              ↓  foreach attr: INSERT PartMasterAttributes (IsDefault=true)
              ↓  SaveChangesAsync()
         ↓  Nav.NavigateTo("/part-master/detail?partNo=...")
```
**DB writes:** `PartMasters` (1 row), `PartMasterAttributes` (N rows)

---

### Flow 2: Lưu & Xác nhận OK trong Detail Part Master

```
[UI] Detail.razor — nút "Lưu & Xác nhận OK"
    ↓  TrySaveAsync() → InitiateSaveAsync(isDraft: false)
         ↓  check RequiresReason: nếu sửa dữ liệu đã có → bắt buộc nhập lý do ≥3 ký tự
         ↓  CollectDefaultChanges(): tìm các attr muốn đổi IsDefault
              → nếu có → mở PinConfirmModal
              → user nhập PIN → OnPinConfirmed() → DoSaveAsync(confirmOk: true)
              → nếu không có → DoSaveAsync(confirmOk: true) trực tiếp
         ↓  DoSaveAsync(confirmOk: true):
              ↓  Vùng A (nếu _canEditA):
                   - UpdatePartNameAsync() → UPDATE PartMasters
                   - TryAddAttrValue(Material/Config/Note) → INSERT PartMasterAttributes (nếu giá trị mới)
                   - SafeSetDefaultAsync() nếu đã tick checkbox "Đặt làm mặc định"
              ↓  Vùng B (nếu _canEditB):
                   - SaveStepListAsync(_machiningEdits):
                     • State=New: MachiningSvc.AddAsync() → INSERT PartMachiningSteps + log "Created"
                     • State=Modified: MachiningSvc.UpdateAsync() → diff fields → log từng field + UPDATE step
                     • State=Deleted: MachiningSvc.SoftDeleteAsync() → log "Deleted" → HARD DELETE step
              ↓  Vùng C: Taro + Bavia + Washing (tương tự B)
              ↓  Vùng D: Inspection (tương tự B)
              ↓  Vùng E: Packaging (tương tự B)
              ↓  Load fresh PartMasters từ DB → set IsXxxConfirmed = true → SaveChangesAsync()
```
**DB writes:**
- `PartMasters` (update confirmed flags)
- `PartMasterAttributes` (insert/update)
- `PartMachiningSteps` (insert/update/delete)
- `PartProcessStepChangeLogs` (insert - APPEND ONLY)

---

### Flow 3: Xóa dòng NC/CV (ToggleDelete → SoftDeleteAsync → hard delete + ChangeLog)

```
[UI] Detail.razor — nút trash (icon rác) trên dòng NC
    ↓  ToggleDelete(edit, list):
         - Nếu State == New: list.Remove(edit) — bỏ khỏi UI, không cần save
         - Nếu State == Deleted: khôi phục (Restored hoặc Unchanged)
         - Else: State = Deleted (highlight đỏ, chưa ghi DB)
    ↓  [UI hiện dòng gạch ngang, button trash thành button restore]
    ↓  User nhấn "Lưu & Xác nhận OK" → DoSaveAsync()
         ↓  SaveStepListAsync: gặp State==Deleted && StepId>0 && WasActiveInDb==true
              ↓  MachiningSvc.SoftDeleteAsync(stepId, userId, reason):
                   ↓  EnsurePermissionAsync(userId)
                   ↓  Load step từ DB
                   ↓  Snapshot toàn bộ field → JSON
                   ↓  INSERT PartProcessStepChangeLogs (FieldName="Deleted", OldValue=snapshot JSON)
                   ↓  HARD DELETE PartMachiningSteps WHERE StepId = stepId
                   ↓  SaveChangesAsync()
```
**Lưu ý quan trọng:** Method tên là `SoftDeleteAsync` nhưng từ v0.7 thực ra là **HARD DELETE** (xóa hẳn khỏi DB). Tên giữ nguyên để tương thích API. Lịch sử được giữ trong `PartProcessStepChangeLogs`.

---

### Flow 4: Tạo phiếu KhPlan

```
[UI] KhPlan/Create.razor — nút "Xác nhận Kế hoạch"
    ↓  OnInitializedAsync: load Customers, PartMasters
    ↓  User nhập header (PlanDate, CustomerId) + N dòng detail
    ↓  Nhập PartNo → OnPartNoChangedAsync():
         - Nếu Part có trong PartMasters: load PartMasterAttributes → auto-fill Material/Config/Note default
         - AttributePicker hiện dropdown chọn các giá trị đã có
    ↓  AttemptSaveAsync() → validate → DoSaveAsync()
         ↓  Foreach dòng detail:
              - PartNo KHÔNG có trong DB: PartSvc.CreatePartMasterAsync() → tạo Part mới
              - PartNo đã có: MaybeAddNewValue() → thêm giá trị mới vào PartMasterAttributes nếu cần
         ↓  KhSvc.CreateAsync(plan, userId):
              ↓  GenerateNextPlanNoAsync() → "KH-YYYYMMDD-####"
              ↓  Set CreatedBy, CreatedAt, Status, LineNo cho từng detail
              ↓  INSERT KhPlans + KhPlanDetails
              ↓  SaveChangesAsync()
         ↓  Tự động set IsPlanConfirmed=true cho Part chưa confirmed (user KH không vào PartMaster được)
         ↓  Nav.NavigateTo("/kh-plan")

    [Lưu ý: CopyRouteSnapshotAsync() KHÔNG tự động gọi trong CreateAsync của KhPlanService.
     Snapshot route B→E chỉ được copy khi user vào View và nhấn "Làm mới quy trình",
     hoặc có thể được gọi từ nơi khác — cần kiểm tra lại code View.razor]
```
**DB writes:** `KhPlans`, `KhPlanDetails`, `PartMasters` (IsPlanConfirmed), `PartMasterAttributes` (nếu giá trị mới)

---

## 6. Permission Matrix

### Cấu trúc phân quyền
```
UserGroup.Permissions = JSON: List<GroupPermissionSetting>
  {
    SectionCode: "PART_TAO_MOI",
    CanView: true,
    CanEdit: true,
    CanDelete: false   // chỉ dùng cho PART_TAO_MOI (đánh dấu IsObsolete)
  }
```
- **ADMIN group (GroupCode="ADMIN"):** bypass toàn bộ, luôn có mọi quyền
- Các group khác: đọc JSON Permissions → tìm SectionCode → check CanView/CanEdit/CanDelete

### Danh sách Section Codes

| Code | Tên hiển thị | Mô tả |
|------|-------------|-------|
| `KE_HOACH` | Kế hoạch | Toàn quyền trang Kế hoạch |
| `PART_TAO_MOI` | Part Master: A. Kế hoạch nhập | Tạo/sửa PartNo, thông tin chung, phôi |
| `PART_GC` | Part Master: B. Quy trình Gia công | Sửa bước NC, dao, đồ gá, thời gian máy |
| `PART_HTSP` | Part Master: C. Taro/Bavia/Rửa | Chọn WTS & sửa bước HTSP |
| `PART_KCS` | Part Master: D. Kiểm tra (KCS) | Chọn WTS & sửa thông số kiểm tra |
| `PART_DONGGOI` | Part Master: E. Đóng gói | Chọn WTS & sửa quy cách đóng gói |
| `KHO` | Quản lý Kho | Xuất/nhập tồn, vị trí, cấp phát phôi |
| `SO_DO_TC` | Sơ đồ tổ chức | Quản lý nhân sự & phòng ban |
| `KHACH_HANG` | Khách hàng | Quản lý danh mục đối tác |
| `THIET_BI` | Danh sách thiết bị | Quản lý máy móc |
| `DO_GA` | Quản lý Đồ gá | Mượn/trả đồ gá |
| `DAO` | Quản lý dao | Dao cụ CNC, mũi khoan, taro |
| `WTS_MASTER` | WTS Tiêu chuẩn | Thêm/sửa danh mục WTS chuẩn |
| `WTS_GC` | Nhập WTS: Gia công | Công nhân GC nhập WTS thực tế |
| `WTS_HTSP` | Nhập WTS: Hoàn thiện SP | Công nhân HTSP nhập WTS thực tế |
| `WTS_KCS` | Nhập WTS: Kiểm tra | Công nhân KCS nhập WTS thực tế |
| `WTS_DONG_GOI` | Nhập WTS: Đóng gói | Công nhân đóng gói nhập WTS thực tế |
| `HANDOVER_KHO` | Giao nhận: Kho | Giao/nhận tại Kho |
| `HANDOVER_GC` | Giao nhận: Gia công | Giao/nhận tại nhóm GC |
| `HANDOVER_HTSP` | Giao nhận: HTSP | Giao/nhận tại nhóm HTSP |
| `HANDOVER_KCS` | Giao nhận: KCS | Giao/nhận tại nhóm KCS |
| `HANDOVER_PKG` | Giao nhận: Đóng gói | Giao/nhận tại nhóm ĐG |
| `SETTING_ADMIN` | Cài đặt & Phân quyền | Toàn quyền quản trị — chỉ Admin |

### Nhóm đề xuất mặc định (theo thiết kế)
| GroupCode | Sections có quyền điển hình |
|-----------|----------------------------|
| `ADMIN` | Tất cả — hardcoded bypass |
| `PLANNING` (Kế hoạch) | KE_HOACH (Edit), PART_TAO_MOI (View+Edit+Delete) |
| `TECHNICAL` (Kỹ thuật) | PART_GC, PART_HTSP, PART_KCS, PART_DONGGOI (Edit) |
| `PRODUCTION` (Sản xuất/GC) | WTS_GC (Edit), HANDOVER_GC |
| `FINISHING` (HTSP) | WTS_HTSP (Edit), HANDOVER_HTSP |
| `INSPECTION` (KCS) | WTS_KCS (Edit), HANDOVER_KCS |
| `DONG_GOI` (Đóng gói) | WTS_DONG_GOI (Edit), HANDOVER_PKG |
| `WAREHOUSE` (Kho) | KHO (Edit), HANDOVER_KHO |
| `VIEWER` | CanView = true các section, CanEdit = false |

> **Lưu ý:** Các GroupCode trên là convention — thực tế nhóm do Admin tự tạo và cấu hình JSON.

---

## 7. Business Rules quan trọng

### IsPlanConfirmed / IsXxxConfirmed (5 cờ trạng thái Part Master)
- Mỗi Part có 5 cờ xác nhận: A (Plan), B (Machining), C (HTSP), D (KCS), E (Packaging)
- Mặc định: tất cả `false` (NG — chưa xác nhận)
- **Tự động set `IsPlanConfirmed = true`** khi:
  - `CreatePartMasterAsync()` được gọi (người tạo = đã xác nhận vùng A)
  - Tạo phiếu KhPlan mà Part chưa confirmed
- **Nút "Lưu & Xác nhận OK"** set các cờ = true; **"Lưu nháp"** set về false
- User chỉ được set/unset cờ thuộc vùng mình có quyền Edit
- Đặc biệt: `IsPlanConfirmed` — user có `CanView` (không cần Edit) cũng có thể confirm vùng A (rule KH không vào được PartMaster)

### IsObsolete (Ngưng sử dụng Part)
- `IsObsolete = true` → Part không xuất hiện trong dropdown tạo mới, có badge "Ngưng" trong danh sách
- Part vẫn còn trong DB, lịch sử không ảnh hưởng
- Phiếu KhPlan/Kho đã tạo **không bị ảnh hưởng**
- Có thể kích hoạt lại (`IsObsolete = false`)
- Chỉ ADMIN hoặc nhóm có `PART_TAO_MOI.CanDelete = true` được thực hiện

### WtsTaskCode (mã CV trong TaroStep)
- `TaroStep.NC` = mã nguyên công do người dùng nhập thủ công (VD: "T01")
- `TaroStep.WtsTaskCode` = mã WTS tiêu chuẩn được chọn từ `StandardWtsTasks` (VD: "1T")
- `TaroStep.StepName` = tên công đoạn (fill từ `StandardWtsTask.TaskName`)
- Với Bavia/Washing/Inspection/Packaging: `NC` chính là `TaskCode` (auto-fill từ WTS), không nhập thủ công
- Với Machining: `NC` nhập thủ công, không có WtsTaskCode

### Append-only ChangeLog
Hai loại ChangeLog KHÔNG BAO GIỜ được UPDATE/DELETE:
1. **`PartProcessStepChangeLogs`** — log mọi thay đổi công đoạn Part Master (kể cả khi step đã bị xóa hẳn)
2. **`KhPlanDetailChangeLogs`** — log mọi thay đổi field của KhPlanDetail (STD, Qty, OrderVL...)
3. **`WtsProductionLogs`** — log WTS công nhân, sai thì Void + tạo dòng mới đúng

> **`PartProcessStepChangeLogs.StepId`** là LOOSE REFERENCE (không có FK constraint) — đảm bảo log tồn tại ngay cả khi step bị xóa khỏi DB.

### Backup NC (IsBackup)
- Mỗi NC chính có thể có nhiều NC dự phòng: `NC01`, `NC01-DP-01`, `NC01-DP-02`...
- `IsBackup = true`, `ParentNC = "NC01"` cho các dòng dự phòng
- NC dự phòng hiển thị với background vàng, icon mũi tên trái

### SoftDeleteAsync — thực ra là Hard Delete (v0.7+)
- Tên method giữ nguyên để tương thích API nhưng từ v0.7 thực hiện HARD DELETE
- Trước khi xóa: snapshot toàn bộ field → ghi vào `PartProcessStepChangeLogs` (FieldName="Deleted")
- Sau khi xóa: step không còn trong DB, nhưng history vẫn đầy đủ trong ChangeLog

### RouteSnapshot (snapshot quy trình B→E vào phiếu KhPlan)
- Khi tạo phiếu KhPlan, `CopyRouteSnapshotAsync()` copy 6 bảng `Part*Steps` → 6 bảng `KhPlanRouteSnapshot*`
- Snapshot frozen — không thay đổi khi Part Master được sửa sau đó
- `RefreshRouteSnapshotAsync()`: xóa snapshot cũ → copy lại từ Part Master hiện tại → ghi log
- `HandoverService` dùng snapshot để tính baseQty — nếu chưa có snapshot → chặn giao hàng

### MachineExcludeSettings (máy loại trừ toàn hệ thống)
- Danh sách máy mà NC chạy trên đó **không tính vào chu kỳ gia công chính**
- Áp dụng toàn bộ Part (không per-Part)
- Chỉ user có `PART_GC.CanEdit` mới được cài đặt

### PIN Confirm
- Yêu cầu nhập PIN khi **đổi giá trị default** của Material/MaterialConfig/MaterialNote trong vùng A
- PIN lưu trong `User.PIN` (plain text — cần hash sau)
- Các thao tác khác (sửa bước quy trình, sửa tên Part) chỉ cần nhập lý do text

---

## 8. TODOs còn lại

### Tính năng chưa hoàn thiện
1. **WTS nhập công nhân** (`/hieu-suat-cong-nhan`, `/hieu-suat-may`): trang báo cáo đã có route nhưng chưa có màn hình nhập WTS thực tế cho công nhân (WTS_GC, WTS_HTSP, WTS_KCS, WTS_DONG_GOI sections đã được khai báo nhưng UI chưa build).

2. **CopyRouteSnapshotAsync không tự gọi khi Create KhPlan**: `KhPlanService.CreateAsync()` không gọi `CopyRouteSnapshotAsync()`. Snapshot chỉ được tạo khi user vào View và nhấn "Làm mới quy trình". Cần kiểm tra `KhPlan/View.razor` để xác nhận flow chính xác.

3. **HandoverService line 808 — chặn giao khi chưa có snapshot**: `baseQty = 0` khi KhPlanDetail chưa có snapshot route → user không thể giao hàng cho PO mới. Cần đảm bảo snapshot luôn được tạo sau khi tạo phiếu KhPlan.

4. **User.PIN lưu plain text**: `User.PIN` hiện lưu chuỗi text trực tiếp, không hash. Cần hash PIN tương tự password.

5. **FileStorage / FileAttachment**: entity đã có trong DB (bảng `FileStorages`, `FileAttachments`) nhưng chưa có UI upload/quản lý file đính kèm.

6. **WorkerActivityLog**: entity đã có (`WorkerActivityLogs` — log thời gian chết / hoạt động không link PO) nhưng chưa có UI nhập và báo cáo.

7. **KhPlan Status workflow**: trường `KhPlan.Status` và `KhPlanDetail.Status` mặc định "WaitingSupplierOrder" nhưng chưa có logic chuyển trạng thái tự động (VD: sau khi Handover hoàn thành → "InProduction").

8. **Xuất Excel Part Master**: nút "Xuất Excel" trong `Index_PartMaster.razor` gọi `PartMasterListService.ExportExcelAsync()` — cần kiểm tra implementation đầy đủ.

9. **GroupCode "GroupCode" claim thiếu trong cookie**: Login endpoint chỉ ghi `ClaimTypes.Role = GroupCode` nhưng không ghi claim tên `"GroupCode"`. Trong Detail.razor kiểm tra cả `user.FindFirst("GroupCode")?.Value == "ADMIN"` — có thể miss nếu claim key không match.

10. **Phân quyền Handover**: `HandoverService` kiểm tra quyền theo `HANDOVER_*` sections nhưng chưa kiểm tra ở tầng UI rõ ràng (cần xác nhận `Handover/Index.razor`).

---

*Map này được tạo bằng cách đọc toàn bộ source code tại `MES.Web/` — 2026-09-03.*
