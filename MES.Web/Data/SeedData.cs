using MES.Web.Data.Entities;
using MES.Web.Services;

namespace MES.Web.Data;

public static class SeedData
{
    /// <summary>
    /// Seed dữ liệu mẫu để test flow:
    /// - 7 UserGroup
    /// - 4 User
    /// - 3 Customer
    /// - 5 PartMaster + PartMasterAttribute (default cho Material/Config/Location)
    /// - 1 FileStorage
    /// </summary>
    // [SEED-DEMO 2026-11-20] Gói 26 (C-07): DB TRỐNG mới seed. LUÔN tạo 7 nhóm + tài khoản admin (không có admin thì không vào được để cấu hình);
    //   Nguồn: CLAUDE quét Fable 14/11 (C-07); HÙNG 15/11 15:32 (câu 4) + 21:19 → Gói 26; HÙNG 20/11 18:32 OK đề xuất (B4).
    //   mật khẩu admin = biến môi trường MES_ADMIN_INIT_PW (≥ 6 ký tự), không đặt thì "admin123" + ghi cảnh báo vào log (đổi ngay sau lần đầu).
    //   Dữ liệu MẪU (kh01 / vt01 / kho01 mật khẩu 123456, 3 khách, 5 Part) CHỈ seed khi MES_SEED_DEMO=1 — trước: DB trống là có 4 tài khoản mẫu
    //   mật khẩu đoán được + khách / Part giả lẫn vào dữ liệu thật. DB đã có dữ liệu (máy Hùng, máy khách restore bản backup): không đổi gì.
    public const string DemoEnv = "MES_SEED_DEMO", AdminPwEnv = "MES_ADMIN_INIT_PW";
    /// <summary>[SEED-DEMO] Ghi cảnh báo lúc seed (Program gán logger); null = Console.</summary>
    public static Action<string>? Warn;
    public static bool DemoOn => Environment.GetEnvironmentVariable(DemoEnv) == "1";

    public static void EnsureSeeded(AppDbContext db)
    {
        // [SEED-DEMO] mẫu chỉ khi DB thật sự MỚI (chưa có tài khoản nào) — rà độc lập Gói 26 L1: DB đã có người dùng mà bật MES_SEED_DEMO=1 thì không thêm khách / Part mẫu
        bool demo = DemoOn && !db.Users.Any();
        // ==== UserGroup ====
        if (!db.UserGroups.Any())
        {
            db.UserGroups.AddRange(
                new UserGroup { GroupCode = "ADMIN", GroupName = "Quản trị hệ thống" },
                new UserGroup { GroupCode = "PLANNING", GroupName = "Kế hoạch (KH)" },
                new UserGroup { GroupCode = "SUPPLIER", GroupName = "Vật tư (VT)" },
                new UserGroup { GroupCode = "WAREHOUSE", GroupName = "Kho" },
                new UserGroup { GroupCode = "PRODUCTION", GroupName = "Sản xuất" },
                new UserGroup { GroupCode = "QC", GroupName = "QC" },
                new UserGroup { GroupCode = "VIEWER", GroupName = "Chỉ xem" }
            );
            db.SaveChanges();
        }

        // ==== User ====
        if (!db.Users.Any())
        {
            // [SEED-DEMO] admin luôn có; mật khẩu lấy biến môi trường nếu đặt
            var envPw = (Environment.GetEnvironmentVariable(AdminPwEnv) ?? "").Trim();
            bool envOk = envPw.Length >= 6;
            if (!envOk)
                (Warn ?? Console.WriteLine)($"[SEED-DEMO] DB trống: tạo tài khoản admin mật khẩu mặc định \"admin123\" — đổi ngay sau khi đăng nhập (hoặc đặt biến {AdminPwEnv} ≥ 6 ký tự trước lần chạy đầu).");
            var adminGroup = db.UserGroups.First(g => g.GroupCode == "ADMIN");
            // [SEED-DEMO] rà độc lập Gói 26 L2: 3 nhóm mẫu chỉ tra khi seed tài khoản mẫu (thiếu nhóm thì app không khởi động — có từ trước)
            var planningGroup = demo ? db.UserGroups.First(g => g.GroupCode == "PLANNING") : null;
            var supplierGroup = demo ? db.UserGroups.First(g => g.GroupCode == "SUPPLIER") : null;
            var warehouseGroup = demo ? db.UserGroups.First(g => g.GroupCode == "WAREHOUSE") : null;

            var defaultHash = PasswordHasher.Hash("123456");
            var adminHash = PasswordHasher.Hash(envOk ? envPw : "admin123");   // [SEED-DEMO]

            db.Users.Add(
                new User
                {
                    Username = "admin",
                    FullName = "Administrator",
                    PasswordHash = adminHash,
                    GroupId = adminGroup.GroupId,
                    IsActive = true
                });
            if (demo)   // [SEED-DEMO] tài khoản mẫu chỉ khi MES_SEED_DEMO=1
            db.Users.AddRange(
                new User
                {
                    Username = "kh01",
                    FullName = "Nguyễn Văn KH",
                    PasswordHash = defaultHash,
                    GroupId = planningGroup!.GroupId,
                    IsActive = true
                },
                new User
                {
                    Username = "vt01",
                    FullName = "Trần Thị Vật Tư",
                    PasswordHash = defaultHash,
                    GroupId = supplierGroup!.GroupId,
                    IsActive = true
                },
                new User
                {
                    Username = "kho01",
                    FullName = "Lê Văn Kho",
                    PasswordHash = defaultHash,
                    GroupId = warehouseGroup!.GroupId,
                    IsActive = true
                }
            );
            db.SaveChanges();
        }

        // ==== Customer ====
        if (demo && !db.Customers.Any())   // [SEED-DEMO] khách mẫu chỉ khi MES_SEED_DEMO=1
        {
            db.Customers.AddRange(
                new Customer
                {
                    CustomerCode = "QM",
                    CustomerName = "Quang Minh Production Trading Co., Ltd",
                    SupplierCode = "B03210",
                    IsActive = true
                },
                new Customer
                {
                    CustomerCode = "KOSTAL",
                    CustomerName = "Kostal Vietnam",
                    SupplierCode = "K-VN-001",
                    IsActive = true
                },
                new Customer
                {
                    CustomerCode = "NISSIN",
                    CustomerName = "Nissin Vietnam",
                    SupplierCode = "N-VN-002",
                    IsActive = true
                }
            );
            db.SaveChanges();
        }

        // ==== PartMaster + PartMasterAttribute ====
        if (demo && !db.PartMasters.Any() && db.Customers.Any(c => c.CustomerCode == "QM") && db.Customers.Any(c => c.CustomerCode == "KOSTAL"))   // [SEED-DEMO] Part mẫu chỉ khi MES_SEED_DEMO=1 (cần đủ khách mẫu)
        {
            var qm = db.Customers.First(c => c.CustomerCode == "QM");
            var kostal = db.Customers.First(c => c.CustomerCode == "KOSTAL");
            var adminUser = db.Users.First(u => u.Username == "admin");

            var seedParts = new[]
            {
                new { PartNo = "4A-BE7364810", Name = "Aluminum bracket - QM standard",
                      Mat = "MSP-12-A5052-MID", Cfg = "B12x82x97", Loc = "B1", Cust = qm },
                new { PartNo = "4A-BE7364811", Name = "Steel plate - QM heavy",
                      Mat = "S45C-STD", Cfg = "B15x120x150", Loc = "B2", Cust = qm },
                new { PartNo = "4A-BE7364812", Name = "Housing frame - QM series",
                      Mat = "AL6061-T6", Cfg = "B10x60x80", Loc = "B3", Cust = qm },
                new { PartNo = "KS-CONN-001", Name = "Connector housing - Kostal",
                      Mat = "PA66-GF30", Cfg = "B8x45x60", Loc = "A1", Cust = kostal },
                new { PartNo = "KS-BRK-002", Name = "Mounting bracket - Kostal",
                      Mat = "SUS304", Cfg = "B3x40x100", Loc = "A2", Cust = kostal }
            };

            foreach (var s in seedParts)
            {
                var pm = new PartMaster
                {
                    PartNo = s.PartNo,
                    PartName = s.Name,
                    Material = s.Mat,           // legacy field, giữ cho tương thích
                    MaterialConfig = s.Cfg,      // legacy field
                    LocationDefault = s.Loc,     // legacy field
                    CustomerId = s.Cust.CustomerId,
                    IsActive = true
                };
                db.PartMasters.Add(pm);
                db.SaveChanges();  // để lấy PartId

                // Thêm các Attribute default cho Part này
                db.PartMasterAttributes.AddRange(
                    new PartMasterAttribute
                    {
                        PartId = pm.PartId, AttributeType = "Material",
                        Value = s.Mat, IsDefault = true,
                        CreatedBy = adminUser.UserId
                    },
                    new PartMasterAttribute
                    {
                        PartId = pm.PartId, AttributeType = "MaterialConfig",
                        Value = s.Cfg, IsDefault = true,
                        CreatedBy = adminUser.UserId
                    },
                    new PartMasterAttribute
                    {
                        PartId = pm.PartId, AttributeType = "Location",
                        Value = s.Loc, IsDefault = true,
                        CreatedBy = adminUser.UserId
                    }
                );
            }

            // Thêm 1 vài attribute phụ cho Part 4A-BE7364810 để test dropdown xổ nhiều giá trị
            var part1 = db.PartMasters.First(p => p.PartNo == "4A-BE7364810");
            db.PartMasterAttributes.AddRange(
                new PartMasterAttribute
                {
                    PartId = part1.PartId, AttributeType = "MaterialConfig",
                    Value = "B12x85x100", IsDefault = false,
                    CreatedBy = adminUser.UserId
                },
                new PartMasterAttribute
                {
                    PartId = part1.PartId, AttributeType = "MaterialConfig",
                    Value = "B10x82x97", IsDefault = false,
                    CreatedBy = adminUser.UserId
                },
                new PartMasterAttribute
                {
                    PartId = part1.PartId, AttributeType = "Material",
                    Value = "AL6061-T6", IsDefault = false,
                    CreatedBy = adminUser.UserId
                },
                new PartMasterAttribute
                {
                    PartId = part1.PartId, AttributeType = "MaterialNote",
                    Value = "Cấu hình bộ phận KH gửi, KT chi up PM chạy lại trọng lượng. A5052-MID",
                    IsDefault = true,
                    CreatedBy = adminUser.UserId
                },
                new PartMasterAttribute
                {
                    PartId = part1.PartId, AttributeType = "MaterialNote",
                    Value = "Cắt phôi cần bevel 45 độ cạnh dài",
                    IsDefault = false,
                    CreatedBy = adminUser.UserId
                }
            );

            db.SaveChanges();
        }

        // ==== FileStorage default ====
        if (!db.FileStorages.Any())
        {
            db.FileStorages.Add(new FileStorage
            {
                StorageCode = "STG01",
                BasePath = @"C:\MES\files\",
                Status = "Active",
                IsDefault = true,
                Notes = "Ổ mặc định lưu file. Khi đầy: thêm STG02 với IsDefault=true, STG01 chuyển ReadOnly."
            });
            db.SaveChanges();
        }
    }
}
