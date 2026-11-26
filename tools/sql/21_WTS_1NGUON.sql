-- =====================================================================================================
-- [WTS-1NGUON] 2026-11-26 — BẢN GỬI HÙNG DUYỆT (CHƯA CHẠY). Gói WTS-1NGUON (Hùng 26/11 22:09–23:00 "chốt lại làm theo bạn").
--   Vì sao: hiệu suất công nhân chỉ lấy có ích / vô ích từ cờ mã WTS tiêu chuẩn. Dòng Gia công không mang mã CV → trang WTS tiêu
--   chuẩn có ô chọn "Mã dùng cho Gia công": ĐÚNG 1 mã cho mọi dòng Gia công (mọi NC, Hàng loạt lẫn Chạy thử). Cột mới đánh dấu mã đó;
--   chỉ mục lọc duy nhất bảo đảm cả bảng chỉ có tối đa 1 dòng = 1 (2 người chọn cùng lúc thì người sau bị DB từ chối, không thành 2 mã).
--   Không đổi dữ liệu cũ (mọi dòng = 0 → báo cáo hiện giờ Gia công ở "Chưa có mã" tới khi khách chọn mã).
--   Chạy TRƯỚC khi chạy bản code WTS-1NGUON (bản code cũ không đọc cột này nên chạy trước vẫn an toàn; bản code mới thiếu cột thì
--   trang WTS / báo cáo lỗi). Chạy lại nhiều lần không sao. SQL Server 2016 (không CREATE OR ALTER).
--   Quay lại: DROP INDEX UX_StandardWtsTasks_IsGcTask ON dbo.StandardWtsTasks; ALTER TABLE dbo.StandardWtsTasks DROP CONSTRAINT
--   DF_StandardWtsTasks_IsGcTask; ALTER TABLE dbo.StandardWtsTasks DROP COLUMN IsGcTask; (chỉ sau khi trả bản code cũ).
-- =====================================================================================================
USE [MES];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;   -- bắt buộc khi tạo chỉ mục lọc
SET XACT_ABORT ON;
GO

IF COL_LENGTH(N'dbo.StandardWtsTasks', N'IsGcTask') IS NULL
    ALTER TABLE dbo.StandardWtsTasks ADD IsGcTask bit NOT NULL
        CONSTRAINT DF_StandardWtsTasks_IsGcTask DEFAULT (0);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_StandardWtsTasks_IsGcTask' AND object_id = OBJECT_ID(N'dbo.StandardWtsTasks'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_StandardWtsTasks_IsGcTask
        ON dbo.StandardWtsTasks (IsGcTask) WHERE IsGcTask = 1;
GO

-- Kiểm: cột có, chỉ mục có, số mã đang đánh dấu Gia công (mới chạy = 0)
SELECT COL_LENGTH(N'dbo.StandardWtsTasks', N'IsGcTask') AS CotIsGcTask_Byte,
       (SELECT COUNT(1) FROM sys.indexes WHERE name = N'UX_StandardWtsTasks_IsGcTask') AS ChiMuc,
       (SELECT COUNT(1) FROM dbo.StandardWtsTasks WHERE IsGcTask = 1) AS SoMaGiaCong;
GO
