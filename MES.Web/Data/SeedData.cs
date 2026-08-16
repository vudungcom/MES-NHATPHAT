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
    public static void EnsureSeeded(AppDbContext db)
    {
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
            var adminGroup = db.UserGroups.First(g => g.GroupCode == "ADMIN");
            var planningGroup = db.UserGroups.First(g => g.GroupCode == "PLANNING");
            var supplierGroup = db.UserGroups.First(g => g.GroupCode == "SUPPLIER");
            var warehouseGroup = db.UserGroups.First(g => g.GroupCode == "WAREHOUSE");

            var defaultHash = PasswordHasher.Hash("123456");
            var adminHash = PasswordHasher.Hash("admin123");

            db.Users.AddRange(
                new User
                {
                    Username = "admin",
                    FullName = "Administrator",
                    PasswordHash = adminHash,
                    GroupId = adminGroup.GroupId,
                    IsActive = true
                },
                new User
                {
                    Username = "kh01",
                    FullName = "Nguyễn Văn KH",
                    PasswordHash = defaultHash,
                    GroupId = planningGroup.GroupId,
                    IsActive = true
                },
                new User
                {
                    Username = "vt01",
                    FullName = "Trần Thị Vật Tư",
                    PasswordHash = defaultHash,
                    GroupId = supplierGroup.GroupId,
                    IsActive = true
                },
                new User
                {
                    Username = "kho01",
                    FullName = "Lê Văn Kho",
                    PasswordHash = defaultHash,
                    GroupId = warehouseGroup.GroupId,
                    IsActive = true
                }
            );
            db.SaveChanges();
        }

        // ==== Customer ====
        if (!db.Customers.Any())
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
        if (!db.PartMasters.Any())
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
