# HỆ THỐNG MES NHẬT PHÁT - TÀI LIỆU KIẾN TRÚC & BẢO MẬT (SYSTEM ARCHITECTURE & SECURITY)

## 1. NGUYÊN TẮC THIẾT KẾ CỐT LÕI (SYSTEM CORE PRINCIPLES)
1. **Dữ liệu chuẩn hóa (Strict Master Data):** Không cho phép công nhân/người dùng nhập chữ tự do ở các công đoạn, mã máy, đồ gá, mã WTS. Mọi thao tác bắt buộc chọn từ danh mục chuẩn (Master Data) để đảm bảo tính đồng nhất khi tổng hợp báo cáo.
2. **Nguyên tắc bất biến (Audit Trail / Append-only):** Không xóa vật lý dữ liệu sản xuất (`Hard Delete`). Mọi thao tác Xóa đều là `Soft Delete` (`IsActive = false`), kèm bảng nhật ký `ChangeLogs` ghi nhận: Trường thay đổi, Giá trị cũ (`OldValue`), Giá trị mới (`NewValue`), Lý do sửa (`Reason`), Người thực hiện (`ChangedBy`) và Thời gian (`ChangedAt`).
3. **Bảo mật 2 lớp qua Mã PIN:** Mọi thao tác can thiệp dữ liệu nhạy cảm (Tạo mới, Sửa thông số, Xóa, Mượn/Trả đồ gá, Phân quyền) đều yêu cầu nhập mã PIN cá nhân (4-6 số) đã được mã hóa trong CSDL.
4. **Giao diện phản hồi tức thì (Reactive UI):** Phát triển trên nền tảng Blazor Server Interactive, cập nhật trạng thái dữ liệu theo thời gian thực mà không tải lại toàn trang (No Full-Page Reload).

---

## 2. KIẾN TRÚC PHÂN QUYỀN ĐỘNG (DYNAMIC RBAC MATRIX)

### 2.1. Quy hoạch Nhóm người dùng (User & UserGroup)
* **Bãi bỏ hoàn toàn cột `User.Role` ("Leader"/"Normal"):** Để tránh code phân nhánh phức tạp, toàn bộ các vị trí Leader và Nhân viên đều được quy hoạch thành **Nhóm người dùng (`UserGroup`) độc lập**.
* **Danh sách Nhóm người dùng tiêu chuẩn:**
  1. `ADMIN`: Quản trị viên tối cao (Toàn quyền hệ thống, truy cập trang Settings).
  2. `VIEWER`: Tài khoản chỉ xem (Read-only toàn hệ thống).
  3. `KE_HOACH` & `LEADER_KE_HOACH`: Bộ phận Kế hoạch & Quản lý đơn hàng.
  4. `KY_THUAT` & `LEADER_KY_THUAT`: Bộ phận Kỹ thuật / CAM / Lập trình gia công.
  5. `GIA_CONG` & `LEADER_GIA_CONG`: Bộ phận Vận hành máy CNC.
  6. `HTSP` & `LEADER_HTSP`: Bộ phận Hoàn thiện sản phẩm (Taro, Bavia, Rửa).
  7. `KCS` & `LEADER_KCS`: Bộ phận Kiểm tra chất lượng (QC/KCS).
  8. `DO_GA` & `LEADER_DO_GA`: Bộ phận Quản lý kho Đồ gá & Dụng cụ.
  9. `DONG_GOI` & `LEADER_DONG_GOI`: Bộ phận Đóng gói thành phẩm.
  10. `KHO` & `LEADER_KHO`: Bộ phận Quản lý Kho phôi & Vật tư.
  11. `HANH_CHINH` & `LEADER_HANH_CHINH`: Bộ phận Quản lý Sơ đồ tổ chức & Nhân sự.

### 2.2. Danh mục Vùng phân quyền hệ thống (System Sections)
Hệ thống chia nhỏ giao diện thành 12 Vùng nghiệp vụ độc lập:
* `KE_HOACH`: Toàn quyền thao tác trên module Kế hoạch & PO.
* `PART_TAO_MOI`: Tạo mã Part No mới, gán khách hàng & thông số ban đầu.
* `PART_GC`: Quy trình Gia công CNC (Thiết lập các bước Tiện/Phay NC, dao cụ, thời gian máy).
* `PART_HTSP`: Quy trình Hoàn thiện SP (Thiết lập các bước Taro, Bavia, Rửa theo chuẩn WTS).
* `PART_KCS`: Quy trình Kiểm tra chất lượng (Thiết lập bản vẽ, dung sai, kích thước kiểm tra).
* `PART_DONGGOI`: Quy trình Đóng gói thành phẩm.
* `KHO`: Quản lý kho phôi, nhập/xuất/tồn kho.
* `THIET_BI`: Quản lý danh sách 46 máy móc & nhật ký dừng máy.
* `DO_GA`: Quản lý danh mục 441 bộ đồ gá & mượn trả phụ kiện.
* `WTS_MASTER`: Quản lý danh mục 35+ công việc tiêu chuẩn.
* `SO_DO_TC`: Quản lý 30 phòng ban & cây sơ đồ nhân sự.
* `SETTING_ADMIN`: Quản trị User, Group & Ma trận phân quyền (Chỉ dành cho Admin).

### 2.3. Cấu trúc Ma trận Phân quyền (Permission Matrix)
Quyền hạn của từng nhóm được lưu trữ dưới dạng chuỗi JSON `List<GroupPermissionSetting>` tại cột `UserGroup.Permissions`:
```json
[
  { "SectionCode": "KE_HOACH", "CanView": true, "CanEdit": true },
  { "SectionCode": "PART_GC", "CanView": true, "CanEdit": false },
  { "SectionCode": "KHO", "CanView": false, "CanEdit": false }
]