-- =====================================================================================================
-- [NHAN-DOI-O-NC] 2026-11-06 — BẢN GỬI HÙNG DUYỆT (CHƯA CHẠY). Hùng 06/11 06:56 OK.
--   Vì sao: phiếu mang vụ vào Gia công ô Hàng lỗi (không NC); bên nhận đổi ô sang Hàng lỗi đã xử lý / Hàng mới lúc bấm Nhận → hàng
--   luôn vào NC đầu (lùi không lý do, trái K2). Cột mới HandoverReceives.ToNC = NC nhận khi đổi ô vào Gia công; máy chủ tự điền NC theo
--   vụ (K2: NC của vụ, chỉ lùi về NC trước kèm lý do). NULL = lần nhận cũ / không đổi ô (sổ tính như cũ). Không đổi dữ liệu cũ.
--   Chạy lại nhiều lần không sao. SQL Server 2016.
--   Quay lại (chỉ khi code mới chưa chạy): ALTER TABLE dbo.HandoverReceives DROP COLUMN ToNC;
-- =====================================================================================================
USE [MES];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'dbo.HandoverReceives', N'ToNC') IS NULL
    ALTER TABLE dbo.HandoverReceives ADD ToNC nvarchar(20) NULL;
GO

SELECT N'HandoverReceives.ToNC' AS Muc, CASE WHEN COL_LENGTH(N'dbo.HandoverReceives', N'ToNC') IS NULL THEN N'CHƯA' ELSE N'CÓ' END AS TrangThai;
GO
