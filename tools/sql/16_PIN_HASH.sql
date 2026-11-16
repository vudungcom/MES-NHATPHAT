-- =====================================================================================================
-- [PIN-HASH] 2026-11-16 — Gói 24 (P-05 / C-02 quét Fable). CHỜ HÙNG DUYỆT rồi chạy trên DB MES (chạy lúc nào cũng được, không cần tắt app).
--   Vì sao: PIN đang lưu CHỮ RÕ ở Users.PIN (cột 20 ký tự) → ai đọc DB / file .bak thấy PIN mọi người. Code Gói 24 lưu PIN dạng hash PBKDF2
--   (cùng cách mật khẩu, ~95 ký tự) → cột phải rộng ≥ 95. Code chạy được cả khi CHƯA chạy script này (PIN chữ rõ vẫn so được; chỉ không nâng
--   hash được, app ghi log "chạy tools/sql/16_PIN_HASH.sql"). Sau khi chạy: khởi động lại app → tự nâng mọi PIN chữ rõ sang hash 1 lần.
--   Không xoá / sửa dữ liệu nào, chỉ nới cột. Chạy lại nhiều lần không sao. SQL Server 2016.
--   Quay lại: không cần (cột rộng hơn không ảnh hưởng gì); muốn về 20 thì mọi PIN phải còn chữ rõ (chưa nâng).
-- =====================================================================================================
USE [MES];
GO
SET QUOTED_IDENTIFIER ON;
GO
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id
           WHERE t.name = 'Users' AND c.name = 'PIN' AND c.max_length < 400)   -- nvarchar: max_length = 2 × số ký tự
BEGIN
    ALTER TABLE dbo.Users ALTER COLUMN PIN nvarchar(200) NULL;
    PRINT '16_PIN_HASH: Users.PIN -> nvarchar(200) XONG';
END
ELSE PRINT '16_PIN_HASH: Users.PIN đã đủ rộng — bỏ qua';
GO
-- Kiểm sau khi chạy (số ký tự cột và số PIN còn chữ rõ / đã hash):
SELECT c.name, c.max_length / 2 AS SoKyTu FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id WHERE t.name = 'Users' AND c.name = 'PIN';
SELECT SUM(CASE WHEN PIN IS NULL OR PIN = '' THEN 1 ELSE 0 END) AS ChuaDatPIN,
       SUM(CASE WHEN PIN LIKE 'PBKDF2.%' THEN 1 ELSE 0 END)     AS DaHash,
       SUM(CASE WHEN PIN IS NOT NULL AND PIN <> '' AND PIN NOT LIKE 'PBKDF2.%' THEN 1 ELSE 0 END) AS ConChuRo
FROM dbo.Users;
GO
