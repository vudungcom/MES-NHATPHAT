# MES Prototype v0.1 - Hướng dẫn cài đặt

Cài đặt và chạy trên server Windows (địa chỉ mẫu: 192.168.1.211).

---

## 1. Yêu cầu

- **Windows 10/11 hoặc Windows Server**
- **.NET 8 SDK** (bắt buộc, không chỉ Runtime — vì cần build)
  - Download: <https://dotnet.microsoft.com/en-us/download/dotnet/8.0>
  - Chọn: **SDK 8.0.x → Windows x64 → Installer**
  - Sau khi cài, mở CMD gõ `dotnet --version` phải ra `8.0.xxx`
- **SQL Server** đã có sẵn (2016 trở lên đều được)
  - Cần **Mixed Mode Authentication** (SQL Server + Windows Auth)
  - Cần 1 SQL user có quyền `dbcreator` để tạo DB `MES`

---

## 2. Chuẩn bị SQL

Mở SSMS, chạy script sau (đổi password theo ý):

```sql
-- Nếu chưa có user riêng cho MES, tạo mới
CREATE LOGIN mes_user
WITH PASSWORD = 'DoiPasswordCuaBanODay',
     CHECK_POLICY = OFF;

-- Cấp quyền tạo DB
ALTER SERVER ROLE dbcreator ADD MEMBER mes_user;
```

Sau khi app chạy lần đầu và tạo DB xong, có thể hạ quyền:

```sql
USE MES;
CREATE USER mes_user FOR LOGIN mes_user;
ALTER ROLE db_owner ADD MEMBER mes_user;
ALTER SERVER ROLE dbcreator DROP MEMBER mes_user;
```

**Lưu ý:** Nếu SQL đang ở "Windows Auth only":
- Right-click server trong SSMS → Properties → Security
- Chọn "SQL Server and Windows Authentication mode"
- Restart SQL service (`services.msc` → SQL Server (MSSQLSERVER) → Restart)

---

## 3. Giải nén project

Giải nén file zip vào folder: `C:\MES\`

Cấu trúc thư mục sau khi giải nén:
```
C:\MES\
├── MES.sln
├── SETUP.md (file này)
├── README.md
└── MES.Web\
    ├── MES.Web.csproj
    ├── Program.cs
    ├── appsettings.json  ← EDIT FILE NÀY
    └── ...
```

---

## 4. Cấu hình connection string

Mở file `C:\MES\MES.Web\appsettings.json` bằng Notepad.

Tìm dòng:
```json
"DefaultConnection": "Server=localhost;Database=MES;User Id=YOUR_SQL_USER;Password=YOUR_SQL_PASSWORD;TrustServerCertificate=True;MultipleActiveResultSets=true"
```

Thay:
- `YOUR_SQL_USER` → SQL username của bạn (VD: `mes_user`)
- `YOUR_SQL_PASSWORD` → password của user đó

Nếu SQL Server cài trên cùng máy này thì `Server=localhost` giữ nguyên.
Nếu SQL ở máy khác thì đổi thành `Server=192.168.1.xxx`.

Save file.

---

## 5. Mở firewall port 5000

Mở PowerShell **as Administrator**, chạy:

```powershell
netsh advfirewall firewall add rule name="MES App" dir=in action=allow protocol=TCP localport=5000
```

---

## 6. Chạy app lần đầu

Mở CMD tại `C:\MES\MES.Web\`:

```cmd
cd C:\MES\MES.Web
dotnet restore
dotnet run
```

Lần đầu sẽ hơi chậm (đang download NuGet packages). Khi thấy log:

```
========================================
 MES đang chạy tại http://localhost:5000
 Login lần đầu: admin / admin123
========================================
```

→ app đã chạy được. DB `MES` được tạo tự động, có sẵn user `admin` / `admin123`.

**Truy cập:**
- Từ chính server: <http://localhost:5000>
- Từ máy khác trong LAN: <http://192.168.1.211:5000>

---

## 7. Chạy nền như Windows Service (khuyến nghị)

Khi mọi thứ OK, chạy như service để không cần mở CMD.

### 7.1 Build bản Release

```cmd
cd C:\MES\MES.Web
dotnet publish -c Release -o C:\MES\publish
```

### 7.2 Tạo Windows Service

Mở CMD **as Administrator**:

```cmd
sc.exe create MES binPath= "C:\MES\publish\MES.Web.exe" start= auto DisplayName= "MES Web App"
sc.exe description MES "MES - Manufacturing Execution System"
sc.exe start MES
```

Sau này quản lý qua `services.msc` (search "MES") hoặc lệnh:

```cmd
sc.exe stop MES     # Dừng
sc.exe start MES    # Chạy
sc.exe delete MES   # Xóa service (không xóa file)
```

**Kiểm tra service chạy chưa:** truy cập <http://192.168.1.211:5000> phải ra trang login.

---

## 8. Sau khi login

1. Login `admin` / `admin123`
2. **ĐỔI PASSWORD NGAY** (chức năng này chưa có UI ở v0.1, tạm thời chạy SQL:
   ```sql
   -- Password mới sẽ được hash lại khi có UI. Tạm thời hash trực tiếp:
   -- Xem PasswordHasher.cs để hash tay hoặc chờ UI có
   ```
3. Vào tab **Kế Hoạch** → tạo Customer đầu tiên → tạo phiếu KH đầu tiên → xem cách hệ thống lưu dữ liệu

---

## 9. Reset dữ liệu (khi cần test lại từ đầu)

Prototype cho phép xóa sạch DB để test:

```sql
-- Trong SSMS, chọn DB master
DROP DATABASE MES;
```

Sau đó restart app → DB được tạo lại + seed lại admin.

---

## 10. Xử lý sự cố

### Không kết nối được SQL
- Kiểm tra SQL service đang chạy: `services.msc` → SQL Server (MSSQLSERVER) → Running
- Kiểm tra Mixed Mode Auth (mục 2)
- Kiểm tra `TrustServerCertificate=True` có trong connection string chưa
- Test kết nối bằng SSMS trước với chính user/pass đó

### Port 5000 đã bị dùng
- Đổi port trong `appsettings.json` mục `Kestrel.Endpoints.Http.Url`
- Nhớ mở firewall port mới

### Truy cập từ máy khác trong LAN không được
- Firewall port 5000 (mục 5)
- Chắc chắn `Kestrel.Endpoints.Http.Url` là `http://0.0.0.0:5000` (không phải `localhost:5000`)

### Trang trắng, không thấy CSS
- CSS load từ CDN (cdn.jsdelivr.net) — server phải có internet. Nếu offline, sẽ cần bundle CSS local (làm ở v0.2)

---

## 11. Update code (khi Claude gửi bản mới)

1. Backup DB nếu có dữ liệu quan trọng: `BACKUP DATABASE MES TO DISK = 'C:\backup\MES.bak'`
2. Dừng service: `sc.exe stop MES`
3. Giải nén file mới đè lên `C:\MES\`
4. **Giữ lại `appsettings.json` cũ** (chứa password) — hoặc mở lại và điền password
5. Rebuild: `dotnet publish -c Release -o C:\MES\publish`
6. Start lại: `sc.exe start MES`
7. Nếu schema thay đổi → DROP DATABASE và để app tự tạo lại
