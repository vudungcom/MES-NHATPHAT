-- =====================================================================================================
-- [VL9] 2026-11-06 — BẢN GỬI HÙNG DUYỆT (CHƯA CHẠY). Hùng 05/11 15:37 chốt.
--   Vì sao: cột tính NgCases.CaseNo = 'VL' + RIGHT('000000' + Id, 6) → từ vụ 1.000.001 cắt mất số đầu (VL000001 trùng vụ 1).
--   Code (NgCaseNo.Format) đã đúng: < 1 triệu 6 số, ≥ 1 triệu đủ số. Sửa cột tính cho giống code. Không đổi dữ liệu nào khác
--   (cột tính, SQL tự tính lại). Hiện chưa tới 1 triệu vụ → mọi mã cũ giữ nguyên.
--   An toàn: nếu cột đang có chỉ mục / ràng buộc dùng tới → DỪNG, báo (không tự xoá chỉ mục) — gửi Claude kết quả.
--   Chạy lại nhiều lần không sao (đã đúng công thức mới thì bỏ qua). SQL Server 2016.
-- =====================================================================================================
USE [MES];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

DECLARE @def nvarchar(max) = (SELECT cc.definition FROM sys.computed_columns cc
                              WHERE cc.object_id = OBJECT_ID(N'dbo.NgCases') AND cc.name = N'CaseNo');
IF @def IS NULL
    THROW 50120, N'NgCases.CaseNo không phải cột tính (hoặc không có) — DỪNG, gửi Claude kết quả câu: SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(''dbo.NgCases'')', 1;
IF @def LIKE N'%CASE%'
BEGIN
    PRINT N'CaseNo đã đúng công thức mới — bỏ qua.';
    RETURN;
END
IF EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
           WHERE ic.object_id = OBJECT_ID(N'dbo.NgCases') AND c.name = N'CaseNo')
    THROW 50121, N'Có chỉ mục dùng cột NgCases.CaseNo — DỪNG (không tự xoá chỉ mục), gửi Claude kết quả: EXEC sp_helpindex ''dbo.NgCases''', 1;
IF EXISTS (SELECT 1 FROM sys.sql_expression_dependencies d WHERE d.referenced_id = OBJECT_ID(N'dbo.NgCases') AND d.referenced_minor_id =
           (SELECT column_id FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.NgCases') AND name = N'CaseNo'))
    THROW 50122, N'Có view / hàm / ràng buộc dùng cột NgCases.CaseNo — DỪNG, gửi Claude kết quả.', 1;

BEGIN TRAN;
    ALTER TABLE dbo.NgCases DROP COLUMN CaseNo;
    ALTER TABLE dbo.NgCases ADD CaseNo AS (
        'VL' + CASE WHEN NgCaseId < 1000000 THEN RIGHT('000000' + CAST(NgCaseId AS varchar(20)), 6)
                    ELSE CAST(NgCaseId AS varchar(20)) END) PERSISTED;
COMMIT;
GO

-- Kiểm: mã các vụ hiện có không đổi (VL + 6 số), công thức mới
SELECT TOP (5) NgCaseId, CaseNo FROM dbo.NgCases ORDER BY NgCaseId DESC;
SELECT cc.definition AS CongThucMoi FROM sys.computed_columns cc WHERE cc.object_id = OBJECT_ID(N'dbo.NgCases') AND cc.name = N'CaseNo';
GO
