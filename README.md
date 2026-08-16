# MES Prototype v0.1

Manufacturing Execution System - Prototype đầu tiên để test flow.

## Stack

- ASP.NET Core 8 + Blazor Server
- EF Core 8 + SQL Server
- Cookie Authentication

## Scope v0.1

- ✅ Login (username/password, PBKDF2-SHA256 hash)
- ✅ Layout 4 tab: Kế Hoạch / Kho / Hiệu suất máy / Hiệu suất công nhân
- ✅ Kế Hoạch:
  - List phiếu KH
  - Tạo phiếu KH mới (theo mockup v2)
  - Xem chi tiết phiếu KH
  - Auto-fill Material/Config/Weight từ PartMaster khi chọn Part cũ
  - Tự sinh số phiếu `KH-YYYYMMDD-####`
  - Tự tạo PartMaster khi gặp Part mới
- ✅ Kho: query tồn kho từ StockTransaction (Nhập/Xuất chưa có)
- ⏳ 2 tab kia: placeholder

## Chưa có (làm ở v0.2+)

- Sửa phiếu KH (chỉ xem)
- Import Excel sửa STD hàng loạt
- Xem lịch sử ChangeLog trên UI
- Upload PDF PO đính kèm
- Nhập/Xuất kho
- Đổi password qua UI
- Quản lý Customer/PartMaster/User qua UI (hiện chỉ Customer tạo được qua form KH)

## Cài đặt

Xem `SETUP.md`.

## Cấu trúc

```
MES.Web/
├── Data/
│   ├── AppDbContext.cs
│   ├── SeedData.cs
│   └── Entities/          ← Toàn bộ table
├── Services/
│   ├── PasswordHasher.cs
│   ├── AuthService.cs
│   └── KhPlanService.cs
├── Components/
│   ├── App.razor          ← Root
│   ├── Routes.razor
│   ├── Layout/
│   │   ├── MainLayout.razor
│   │   ├── LoginLayout.razor
│   │   └── NavMenu.razor
│   └── Pages/
│       ├── Login.razor
│       ├── Home.razor
│       ├── KhPlan/
│       │   ├── Index.razor    ← List
│       │   ├── Create.razor   ← Form tạo mới
│       │   └── View.razor     ← Xem chi tiết
│       ├── Kho/
│       └── ...
├── wwwroot/css/app.css
├── Program.cs             ← Entry point
└── appsettings.json       ← CONNECTION STRING (edit trên server)
```

## Nguyên tắc kiến trúc (đã theo)

- Event-sourced: StockTransaction append-only
- ChangeLog cho mọi sửa đổi KhPlanDetail (chưa có UI xem)
- Timestamp server-side (`DateTime.Now`)
- Backend enforce business rules (validation trong Service)
- Password không hardcode trong code — nằm trong `appsettings.json`

## Login mặc định

`admin` / `admin123` — **ĐỔI NGAY sau khi login lần đầu**.
