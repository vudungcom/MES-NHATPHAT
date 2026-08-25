Stack
Backend + Frontend: ASP.NET Core 8 + Blazor Server + EF Core 8.0.10
Database: SQL Server 2016 tại 192.168.1.211
Ngôn ngữ: C# only, KHÔNG dùng JavaScript framework
DB name: MES, collation database Vietnamese_CI_AS, collation cột dùng Latin1_General_CP1_CI_AS
Additional lib: ClosedXML 0.104.0 (Excel import/export)
Auth: Cookie-based (CookieAuthenticationDefaults), scheme MES.Auth, expire 8 tiếng, sliding
File storage: filesystem, đường dẫn:
  <BasePath>/receiving/YYYY/MM/DD/SlipNo/*.jpg
  <BasePath>/issue/YYYY/MM/DD/SlipNo/*.jpg
Môi trường

Server: 192.168.1.211 (Windows 10 Pro Workstation, 16 cores, 24GB RAM)

Share với ứng dụng khác của user: QuanLyModel (đang chạy production, không được ảnh hưởng)
SQL Server: Mixed Auth, có user ngoc.du. KHÔNG hardcode credentials trong code — luôn để ở appsettings.json

Dev workflow (v0.5):

User build trên máy local (D:\Setup\Ngoc\App cua Ngoc\Nhat Phat\Software\mes-prototype\MES.Web\)
Chạy dotnet run, mặc định port 5000
Chưa deploy dạng service trên server → sẽ chuyển sang IIS/Kestrel + Windows Service sau khi ổn
Cloudflare Tunnel đã setup để truy cập từ ngoài

Deploy tương lai:

IIS hoặc Kestrel Self-Contained
HTTPS: pilot có thể tự-sign; production dùng Let's Encrypt DNS-01
Backup: .bak nightly + transaction log hourly (chưa setup)
Nguyên tắc kiến trúc
1. Event-sourced schema

StockTransaction là append-only, không UPDATE/DELETE trên production data. Tồn kho tính bằng SUM(QuantityChange). Sai thì thêm dòng ADJUST, không sửa dòng cũ.

2. Current State ≠ History

Có bảng snapshot (VD KhPlanDetail) cho current state, nhưng history đầy đủ trong ChangeLog. Snapshot rebuild được từ event log nếu cần.

3. Backend enforce business rule

Frontend chỉ:

Hiển thị data
Validation nhẹ trước khi gọi (tránh round-trip khi user rõ ràng nhập sai)
Gọi Service C#

Mọi rule quan trọng (validation SL, kiểm tra PIN, kiểm tra FK, transaction) phải ở Service layer, không ở Razor code.

4. Timestamp server-side

DateTime.Now chạy ở server. KHÔNG nhận timestamp từ client (client có thể fake thời gian).

Prototype đang dùng DateTime.Now (giờ local server = GMT+7 tại VN). Sau này nếu deploy multi-timezone → chuyển sang DateTime.UtcNow + convert khi hiển thị.

5. Segregation of Duties (SoD)

Người tạo ≠ người duyệt/xác nhận. Prototype v0.5 chưa enforce nhưng schema đã có ConfirmedBy để chuẩn bị.

6. File ảnh: lưu path, không lưu binary

Binary trong DB → backup nặng, query chậm, không mở trực tiếp qua Explorer.

7. Snapshot vs Reference cho attribute

Các field trong KhPlanDetail (Material, MaterialConfig, MaterialNotes) là snapshot copy từ PartMasterAttribute tại thời điểm tạo, không phải FK. Lý do:

Phiếu KH là chứng từ, phải giữ nguyên giá trị lúc tạo — dù sau này PartMaster đổi
Query nhanh (không JOIN attribute mỗi lần)
Nếu FK → xóa attribute sẽ cascade phá phiếu cũ
8. Loose reference cho audit log

KhPlanDetailChangeLog.KhPlanDetailId KHÔNG có FK constraint (đã drop ở v0.5.1). Log có thể trỏ về detail đã bị xóa — hành vi mong muốn cho audit trail.

Lý do: khi tách PO, dòng gốc bị xóa nhưng log của nó phải giữ để truy vết.

9. PIN cho thao tác nhạy cảm

Dùng PIN 4-6 số làm second-factor cho các thao tác quan trọng:

Đổi giá trị default trong PartMaster
Tách PO

Không hash PIN: chỉ 10^6 tổ hợp, hash không tăng bảo mật đáng kể; PIN không dùng login từ xa, chỉ xác nhận trong phiên đã đăng nhập.

Thứ tự ưu tiên khi thiết kế
Data Correctness > Business Logic > Usability > Reporting > UI Beauty

Khi có xung đột, luôn chọn cái phía trên. VD: chấp nhận UI xấu hơn nếu logic chặt chẽ hơn.

Ràng buộc phải luôn giữ
Mỗi thay đổi schema phải có file .sql migration riêng + update Schema-Kho-vX.md
Không code cho tới khi khảo sát xong workflow (nghiệp vụ)
Backup: .bak nightly + transaction log hourly (chưa setup ở prototype)
Excel export chỉ là snapshot, KHÔNG phải backup
Không hardcode credentials — luôn ở appsettings.json
Không dùng [NotMapped] bừa bãi trên entity — muốn ignore navigation phải config qua OnModelCreating
Chiến lược archive

Sau 5 năm phiếu đã Completed hoặc Cancelled → chuyển sang DB MES_Archive. Không bao giờ hard delete.

Archive job (chưa implement):

Copy phiếu + tất cả bảng liên quan sang MES_Archive
Verify count + checksum
Delete khỏi MES (chỉ xóa sau khi verify)
Log việc archive vào bảng riêng
Chiến lược mở rộng ổ file

Khi ổ chính (STG01) sắp đầy:

Thêm dòng mới vào FileStorages (STG02) với IsDefault = 1
Đổi STG01 thành Status = ReadOnly
Từ giờ upload mới đi vào STG02, file cũ ở STG01 vẫn đọc được (đường dẫn = <BasePath_STG01> + RelativePath)
Không di chuyển file cũ — di chuyển tốn thời gian và có rủi ro mất data
Module hiện tại
 Auth (v0.1): Login cookie, đổi password, PIN
 Kế hoạch KH (v0.5): Create/View/Edit/Split PO + Bulk Update STD + ChangeLog + AttributePicker
 PartMaster (v0.3): Ngầm — tự tạo khi tạo phiếu KH mới, có versioning attributes
 PartMaster module riêng (Lượt 6 — chuẩn bị): tab riêng, có bảng thống kê + trang chi tiết theo 4 vùng (Kế hoạch/Kỹ thuật/HTSP/Kiểm tra), phân quyền theo group, file đính kèm hướng dẫn
 Kho: mockup Nhập/Xuất v2 đã có (HTML), chưa code Blazor
 Sản xuất: chưa bàn nghiệp vụ chi tiết (đã có business rules từ file KHSX)
 Báo cáo + Dashboard: chưa bàn
Coexistence với QuanLyModel

MES chạy trên cùng server (192.168.1.211) với QuanLyModel. Ràng buộc:

Dùng DB riêng (MES), không đụng vào DB của QuanLyModel
SQL Server user riêng nếu có thể (hiện dùng chung ngoc.du)
IIS AppPool riêng khi deploy production
Port riêng (MES: 5000 pilot, 80/443 production; QuanLyModel giữ nguyên)
Nhóm quyền dự kiến (sẽ mở rộng ở Lượt 6)

Hiện có (v0.5): ADMIN, PLANNING, SUPPLIER, WAREHOUSE, PRODUCTION, QC, VIEWER

Sẽ thêm (Lượt 6, tiếng Việt): Kỹ thuật, Hoàn thiện SP, Kiểm tra. Kèm cột Role (Leader/Normal) trong Users.

Phân quyền chi tiết cho PartMaster:

Kế hoạch sửa vùng A (Tên, Mã VL, Cấu hình, Ghi chú phôi)
Kỹ thuật sửa vùng B (Quy trình gia công)
Hoàn thiện SP sửa vùng C (Quy trình Taro/Bavia/Rửa)
Kiểm tra sửa vùng D (Quy trình Kiểm tra)
Leader thêm quyền xóa dòng công đoạn (soft delete). User thường chỉ thêm/sửa
ADMIN sửa tất cả
Mọi sửa đổi cần PIN
Lịch sử migration
Version	File	Ngày	Nội dung
v0.1	(initial EnsureCreated)		Tạo tất cả bảng gốc
v0.2	(không cần script, EF tự)		Thêm ChangeLog
v0.3	migration-v0.2-to-v0.3.sql		Thêm PartMasterAttributes, bỏ WeightPerEA_g
v0.4	(chỉ code, không SQL)		Fix Razor bugs
v0.5	migration-v0.4-to-v0.5.sql		Thêm ReceivedDate + OldPurchaseOrder + xóa Location cũ
v0.5.1	fix-splitpo-fk.sql		Drop FK từ ChangeLog để Split PO hoạt động
Điều CLAUDE tương lai KHÔNG được làm (bảo vệ decisions)
KHÔNG tạo lại FK FK_KhPlanDetailChangeLogs_KhPlanDetails (đã drop có chủ đích v0.5.1)
KHÔNG đổi KhPlanDetailChangeLog.KhPlanDetailId thành nullable — nó vẫn là int NOT NULL, chỉ mất strict FK
KHÔNG bỏ PIN cho việc đổi default PartMaster hoặc Split PO
KHÔNG cho phép sửa PO/PartNo/PlanNo/CustomerId sau khi tạo phiếu
KHÔNG UPDATE hoặc DELETE dòng trong bảng ChangeLog và StockTransaction — append-only tuyệt đối
KHÔNG hardcode credentials trong code
KHÔNG dùng DateTime từ client (browser) — luôn DateTime.Now ở server
KHÔNG thêm Location vào PartMasterAttribute (v0.5 đã xóa, Location là input tự do)
KHÔNG hash PIN (10^6 tổ hợp, không giúp gì thêm)
KHÔNG đọc/ghi vào DB của QuanLyModel — MES chỉ dùng DB MES
### QUY TẮC BẮT BUỘC KHI XUẤT SCRIPT SQL (SQL PROTOCOL)

1. **Khóa chặt phạm vi Database (Database Scoping, đang test ở máy này thì là MES, sau này sẽ update sau):**
   - Mọi script SQL (tạo bảng, sửa cột, thêm trigger, migration) BẮT BUỘC phải mở đầu bằng:
     ```sql
     USE [MES];
     GO
     ```
   - Tuyệt đối không viết câu lệnh SQL trần trụi. Máy chủ `192.168.1.211` dùng chung với `QuanLyModel`, việc thiếu `USE [MES]; GO` có nguy cơ tác động sai database production đang chạy song song.

2. **Tính lũy kế an toàn (Idempotent Scripts):**
   - Mọi câu lệnh `ALTER TABLE`, `CREATE TABLE`, `ADD CONSTRAINT` đều phải bọc trong khối kiểm tra tồn tại `IF NOT EXISTS` / `IF EXISTS` với `sys.columns` hoặc `sys.tables` để đảm bảo chạy lại nhiều lần không sinh lỗi crash.

3. **Nguyên tắc dữ liệu bất biến (Append-Only):**
   - Tuyệt đối không sinh mã `UPDATE` hoặc `DELETE` trên các bảng giao dịch (`StockTransaction`, `ChangeLog`, `PartProcessStepChangeLog`)[cite: 13, 14, 15]. Mọi sai lệch số lượng chỉ bù trừ bằng bản ghi điều chỉnh (ADJUST).

4. **Quy chuẩn mã nguồn C# & Blazor:**
   - Sử dụng `IServiceScopeFactory` để tạo DbContext độc lập trong các Service đọc dữ liệu phân quyền / master data, tránh lỗi đa luồng `InvalidOperationException` khi kết hợp với `NavMenu.razor`.
   - Luôn trả về file mã nguồn đầy đủ (Full file), đối chiếu chính xác cây thư mục dự án và kiểm tra độc lập các biến phụ thuộc trước khi xuất file.