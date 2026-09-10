# CLAUDE-RULES.md — Dự án MES Nhật Phát

**Đọc file này mỗi đầu session. Không tuân thủ = hỏng dự án.**

---

## 1. THÔNG TIN DỰ ÁN

- **Tên:** MES Nhật Phát — Manufacturing Execution System cho công ty cơ khí gia công chi tiết máy
- **Chủ dự án:** Hùng (không phải developer chuyên nghiệp — Claude là người thực thi + tư vấn kỹ thuật)
- **Stack:** Blazor Server (.NET 8, InteractiveServer), EF Core, SQL Server (DB: `MES`), service-layer pattern, RBAC qua `SystemPermissions`
- **Repo:** `https://raw.githubusercontent.com/vudungcom/MES-NHATPHAT/master/`
- **Môi trường:** Server 192.168.1.211, dev local tại `D:\Setup\Ngoc\App cua Ngoc\Nhat Phat\Software\mes-prototype\MES.Web\`

---

## 2. WORKFLOW LÀM VIỆC VỚI GITHUB

### Đầu mỗi session
1. Fetch `file-tree.txt` để xem cấu trúc project
2. Tự fetch đúng file cần thiết cho task — **không cần user upload file**
3. Khi cần hiểu context rộng hơn (entity liên quan, service khác gọi vào), tự fetch thêm — không đoán

### Quy tắc fetch
- Dùng `bash_tool` + `curl` để fetch từ `raw.githubusercontent.com`
- Luôn fetch file mới nhất — user đã `git push` trước khi bắt đầu session
- Nếu không tìm thấy file qua tree → thử tên biến thể (VD: `Handover_Index.razor`, `HandoverIndex.razor`)

### Trước khi code trang mới hoặc component mới
Bắt buộc fetch 2 file chuẩn để đối chiếu UI pattern:
- `MES.Web/Components/Pages/PartMaster/Index_PartMaster.razor`
- `MES.Web/Components/Pages/PartMaster/Detail_Partmaster.razor`

---

## 3. NGUYÊN TẮC LÀM VIỆC

### Không bịa, không đoán
- Không rõ nghiệp vụ → hỏi, đưa 2-3 phương án A/B/C để user chọn
- Không thấy file → fetch từ GitHub, không fabricate tên method/field/entity
- Thiếu thông tin → dừng, hỏi trước khi code

### Không tự ý mở rộng scope
- Chỉ làm đúng những gì được yêu cầu
- Thấy bug khác trong file đang sửa → báo user, không tự sửa
- Không "improve" code nếu user không yêu cầu

### Không silent update
- Sửa logic cũ → phải thông báo trước
- Có mâu thuẫn với code cũ → dừng, hỏi rõ

### Kiểm tra ảnh hưởng dây chuyền
Trước khi thay đổi bất cứ gì, tự trả lời:
1. Bảng nào bị ảnh hưởng?
2. Module nào đang dùng field/logic này?
3. ChangeLog / audit trail có bị phá không?
4. Snapshot vs Reference — có phá phiếu cũ không?

Không trả lời được → hỏi user trước khi code.

### Mỗi lượt = 1 gói deploy được
- Ship file hoàn chỉnh để user overwrite trực tiếp
- Kèm migration SQL nếu có schema change
- Kèm hướng dẫn test cụ thể
- Không dồn nhiều thay đổi chờ hoàn thiện

---

## 4. UI RULES — BẮT BUỘC CHO MỌI TRANG

### Filter & Search (chuẩn từ Index_PartMaster)
**Tầng 1 — Quick filter:** `<select class="form-select-sm">` đặt trên bảng

**Tầng 2 — Excel-style column filter:**
- Header cột có icon funnel `<i class="bi bi-funnel">` 
- Click → dropdown nổi (position: fixed) gồm: Sort A→Z/Z→A, Xóa filter, ô search, list checkbox giá trị unique, footer Hủy/OK
- Pattern dùng `ColFilterDef` với `ExcludedValues` + `SearchText` + `Passes()`
- Icon đổi màu xanh khi đang active filter
- Nút "Xóa filter" chỉ hiện khi có filter/sort đang active

**Logic filter tổng:** Quick filter (AND) ∩ Column filter (AND giữa cột, OR trong cột)

### Layout bảng
- Double-click dòng → mở chi tiết (NavigateTo)
- Cột expand ▶ lazy load, cache vào Dictionary
- `stopPropagation` trên cột Thao tác và cột expand
- Nền `bg-light` cho expanded row

### Modal
- Backdrop **KHÔNG CÓ** `@onclick` — click ngoài không đóng
- Chỉ nút X header và nút Hủy footer mới đóng modal
- PIN modal: render unconditionally với void callbacks, không wrap trong `@if`

### Badge trạng thái
```razor
<span class="badge bg-success">OK</span>
<span class="badge bg-danger">NG</span>
<span class="badge bg-warning text-dark">Chờ</span>
```

### Nút lưu
- Idle: `btn-primary` — "Lưu & Xác nhận OK"
- Saving: disabled — "Đang xử lý..."
- Done: `btn-outline-success` — "Đã lưu ✓"
- Reset về Idle khi user chỉnh sửa bất kỳ field nào

---

## 5. RAZOR CODING RULES — CRITICAL

### KHÔNG dùng lambda phức tạp trong markup
```razor
@* SAI — gây RZ9980, CS0246, CS9348 *@
<button @onclick="async () => { var x = ...; await DoAsync(x); }">

@* ĐÚNG — tách thành method riêng *@
<button @onclick="HandleClickAsync">
```

Không dùng `{}` nhiều dòng, `out var`, generic `<T>` trong lambda của markup.  
Luôn tách thành `private method` trong `@code` block.

### KHÔNG dùng `value=` + `@onchange` cùng lúc trên select
```razor
@* SAI — Blazor reset về blank sau @onchange *@
<select value="@row.NC" @onchange="e => OnChanged(row, e.Value)">

@* ĐÚNG — dùng selected per-option *@
<select @onchange="e => OnChanged(row, e.Value)">
    @foreach (var item in items)
    {
        <option value="@item.Id" selected="@(row.NC == item.Id)">@item.Name</option>
    }
</select>
```

### File Razor phải dùng CRLF line ending
File LF tạo trên Linux gây `RZ9980` parser die trên Windows.  
Signal: warning `CS0414` trên field đang được HTML dùng = parser đã chết → kiểm tra line ending trước khi xét syntax.

---

## 6. SCHEMA & DATA RULES

### Không lưu quan hệ 1-N dạng CSV string
Luôn tạo child table để SQL có thể JOIN và index.  
Exception duy nhất: `PartMachiningTimings.SoMay` CSV gộp máy cùng timing.

### Snapshot khi tạo KhPlan
Route/process steps phải snapshot từ Part Master khi tạo KhPlan — không đọc live.

### Append-only logs
`WtsProductionLogs` và `WorkerActivityLogs`: void + new record, không UPDATE/DELETE.

### Migration SQL
Mọi migration bắt đầu bằng:
```sql
USE [MES];
GO
```

### Soft delete
Part Master dùng `IsObsolete` flag.  
Step rows (NC/CV) dùng hard delete + JSON snapshot vào `PartProcessStepChangeLogs`.

---

## 7. PHÂN QUYỀN

```csharp
// ĐÚNG
var perms = await UserSvc.GetAllUserPermissionsAsync(userId);
bool canEdit = perms.TryGetValue("PART_GC", out var p) && p.CanEdit;

// SAI — claim có thể cũ
var groupCode = user.FindFirst("GroupCode")?.Value;
```

ADMIN luôn có toàn quyền:
```csharp
bool isAdmin = user.Group.GroupCode == "ADMIN" || user.Username.ToLower() == "admin";
```

---

## 8. ĐIỀU TUYỆT ĐỐI KHÔNG LÀM

1. Thêm `@onclick` vào backdrop modal
2. Hardcode credential hoặc permission check bằng string group cứng trong UI
3. UPDATE/DELETE dòng trong ChangeLog / StockTransaction / WtsProductionLogs — append-only
4. Sửa PlanNo, PurchaseOrder, PartNo sau khi đã tạo (identity fields)
5. Dùng `DateTime` từ client — luôn `DateTime.Now` server-side
6. Viết logic nghiệp vụ trong Razor — đưa vào Service
7. Tự sửa code cũ nếu không được yêu cầu
8. Bịa tên entity/method/field — fetch file để verify
9. Lambda phức tạp trong Razor markup
10. Hard delete bất kỳ dữ liệu lịch sử nào

---

## 9. CÁC NHÓM SẢN XUẤT

| GroupCode | Tên | ProcessGroup trong WtsLogs |
|---|---|---|
| KHO | Kho vật liệu | — |
| GC | Gia công | GC |
| HTSP | Hoàn thiện bề mặt | TARO, BAVIA, WASHING |
| KCS | Kiểm tra chất lượng | KCS |
| PKG | Đóng gói | PKG |

---

## 10. CÁC FILE QUAN TRỌNG TRÊN REPO

| File | Mục đích |
|---|---|
| `file-tree.txt` | Cấu trúc project — đọc đầu session |
| `CODEBASE-MAP.md` | Map entities, services, pages |
| `Architecture-Decisions-v0.2.md` | Nguyên tắc kiến trúc đã chốt |
| `MES.Web/Components/Pages/PartMaster/Index_PartMaster.razor` | Chuẩn UI Index |
| `MES.Web/Components/Pages/PartMaster/Detail_Partmaster.razor` | Chuẩn UI Detail |
| `MES.Web/Data/AppDbContext.cs` | DbContext — kiểm tra DbSet khi cần |
| `MES.Web/Data/Entities/KhPlanRouteSnapshots.cs` | Snapshot entities |
