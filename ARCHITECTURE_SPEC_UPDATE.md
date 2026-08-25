# TÀI LIỆU KIẾN TRÚC HỆ THỐNG MES (BẢN CẬP NHẬT GIAI ĐOẠN 1)

## 1. NGUYÊN TẮC THIẾT KẾ CỐT LÕI (CORE RULES)
1. **Dữ liệu chuẩn hóa (No Free-Text):** Không cho phép công nhân nhập chữ tự do ở các công đoạn; mã công việc, đồ gá, thiết bị phải chọn từ danh mục chuẩn (Master Data).
2. **Audit Trail (Append-only):** Mọi hành động Thêm/Sửa/Xóa đều không xóa vật lý (Soft Delete), lưu kèm lý do thay đổi và người thực hiện.
3. **Bảo mật thao tác bằng PIN:** Bắt buộc nhập mã PIN cá nhân khi thực hiện các tác vụ can thiệp dữ liệu.
4. **Phân quyền dạng Vùng (Section-level RBAC):** Phân chia chi tiết quyền Xem/Sửa theo từng vùng dữ liệu thay vì phân quyền theo trang thô sơ.

---

## 2. DANH MỤC VÙNG PHÂN QUYỀN HỆ THỐNG (SYSTEM SECTIONS)
| Mã vùng (`SectionCode`) | Tên vùng hiển thị | Mục đích quản lý |
| :--- | :--- | :--- |
| `KE_HOACH` | Kế hoạch | Tạo, sửa đơn hàng PO, tiến độ giao hàng |
| `PART_TAO_MOI` | Part Master: A. Tạo Part mới | Tạo mã Part No mới, gán khách hàng |
| `PART_GC` | Part Master: B. Quy trình Gia công | Thiết lập bước Tiện/Phay NC, dao cụ, thời gian |
| `PART_HTSP` | Part Master: C. Taro/Bavia/Rửa | Thiết lập bước HTSP dựa theo WTS chuẩn |
| `PART_KCS` | Part Master: D. Kiểm tra (KCS) | Thiết lập dung sai, kích thước kiểm tra |
| `PART_DONGGOI` | Part Master: E. Đóng gói | Thiết lập quy cách đóng gói sản phẩm |
| `KHO` | Kho | Quản lý nhập/xuất/tồn kho phôi và bán thành phẩm |
| `THIET_BI` | Thiết bị | Quản lý danh sách máy móc & nhật ký dừng máy |
| `DO_GA` | Đồ gá | Quản lý danh mục phụ kiện & mượn/trả đồ gá |
| `WTS_MASTER` | WTS Tiêu chuẩn | Quản lý danh mục 35+ công việc tiêu chuẩn |
| `SO_DO_TC` | Sơ đồ tổ chức | Quản lý 30 phòng ban & cây nhân sự |
| `SETTING_ADMIN` | Cài đặt & Phân quyền | Toàn quyền cấu hình User, Group & Ma trận quyền |

---

## 3. CƠ SỞ DỮ LIỆU ĐÃ NÂNG CẤP (DATABASE ENTITIES)

### 3.1. Phân hệ Người dùng & Phân quyền
* **`User`:** `UserId`, `Username`, `FullName`, `PasswordHash`, `PIN`, `GroupId`, `IsActive`, `CreatedAt`. (Đã loại bỏ trường `Role`).
* **`UserGroup`:** `GroupId`, `GroupCode`, `GroupName`, `Permissions` (JSON lưu ma trận `List<GroupPermissionSetting>`), `IsActive`.

### 3.2. Phân hệ Đồ gá & Phụ kiện
* **`DoGas`:** Quản lý đồ gá, phụ kiện (Bu lông, Chốt, Đệm, Kẹp, Long đen), `SoLuongMuonHienTai`, `SoLuongConLai` (tính toán tự động), `TrangThai`.
* **`DoGaMuonTraLogs`:** Ghi nhận lịch sử mượn/trả (Mã NV, Số lượng mượn, Ngày mượn, Hạn trả, Ngày trả thực tế, Tình trạng).
* **`DoGaChangeLogs`:** Ghi nhận lịch sử sửa đổi thông số kỹ thuật của đồ gá.

### 3.3. Phân hệ WTS Tiêu Chuẩn
* **`StandardWtsTasks`:** Danh mục 35 mã công việc gốc (`1T`, `3V1`, `1R`, `1K`, `KM1`...) theo 6 nhóm công đoạn (`TARO`, `BAVIA`, `RUA`, `KCS`, `DONG_GOI`, `KT_NC`) và hỗ trợ tạo nhóm động.
* **`StandardWtsTaskChangeLogs`:** Lưu vết chỉnh sửa danh mục WTS.

---

## 4. GIAO DIỆN & TRANG QUẢN TRỊ (UI/UX)
* **Trang `/settings`:** Chỉ mở cho `ADMIN`, gồm 3 Tab:
  * Tab 1: Danh sách User (Thêm/sửa, khóa tài khoản).
  * Tab 2: Danh sách Nhóm (Tạo nhóm phân biệt Leader/Nhân viên).
  * Tab 3: Ma trận phân quyền động (Sticky Header/Column, Toggle Switch cho phép bật/tắt `CanView` và `CanEdit` cho từng nhóm).