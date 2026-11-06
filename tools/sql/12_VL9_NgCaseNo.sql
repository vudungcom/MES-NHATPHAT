-- =====================================================================================================
-- [VL9] 2026-11-06 — BẢN GỬI HÙNG DUYỆT (CHƯA CHẠY). Hùng 05/11 15:37 chốt.
--   Vì sao: cột tính NgCases.CaseNo = 'VL' + RIGHT('000000' + Id, 6) → từ vụ 1.000.001 cắt mất số đầu (VL000001 trùng vụ 1).
--   Code (NgCaseNo.Format) đã đúng: < 1 triệu 6 số, ≥ 1 triệu đủ số. Sửa cột tính cho giống code. Không đổi dữ liệu nào khác
--   (cột tính, SQL tự tính lại). Hiện chưa tới 1 triệu vụ → mọi mã cũ giữ nguyên.
--   An toàn: chỉ mục UX_NgCases_CaseNo (UNIQUE, 1 cột CaseNo) được xoá rồi tạo lại y như cũ trong cùng giao dịch; chỉ mục / view /
--   ràng buộc KHÁC dùng cột → DỪNG, báo (không tự xoá) — gửi Claude kết quả. Lỗi giữa chừng → quay lại hết, không đổi gì.
--   Chạy lại nhiều lần không sao (đã đúng công thức mới thì bỏ qua). SQL Server 2016.
-- =====================================================================================================
USE [MES];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
-- [VL9-SUA2] chỉ mục trên cột tính cần đủ bộ SET này (SSMS mặc định đã bật; ghi rõ cho chắc)
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

DECLARE @def nvarchar(max) = (SELECT cc.definition FROM sys.computed_columns cc
                              WHERE cc.object_id = OBJECT_ID(N'dbo.NgCases') AND cc.name = N'CaseNo');
IF @def IS NULL
    THROW 50120, N'NgCases.CaseNo không phải cột tính (hoặc không có) — DỪNG, gửi Claude kết quả câu: SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(''dbo.NgCases'')', 1;
-- [VL9-SUA 06/11 17:42] Bản đầu kiểm LIKE '%CASE%' → khớp nhầm tên cột [NgCaseId] (collation không phân biệt hoa thường) → báo
--   "đã đúng" rồi bỏ qua, công thức cũ còn nguyên (Hùng chạy 06/11 17:40). Sửa: nhận công thức mới qua số 1000000 (công thức cũ không có).
IF @def LIKE N'%1000000%'
BEGIN
    PRINT N'CaseNo đã đúng công thức mới — bỏ qua.';
    RETURN;
END
-- [VL9-SUA2 06/11 17:45] Bản sửa 1 Hùng chạy 17:42 dừng 50121 (đúng thiết kế, không đổi gì): cột có chỉ mục UX_NgCases_CaseNo —
--   Hùng gửi sys.indexes 17:43: NONCLUSTERED, is_unique 1, không lọc, 1 cột khoá CaseNo, không cột kèm. Bản này: chỉ nhận ĐÚNG chỉ mục đó
--   (chỉ mục khác dùng CaseNo, hoặc UX_NgCases_CaseNo khác mô tả → vẫn DỪNG), xoá → đổi công thức → tạo lại y như cũ, 1 giao dịch.
IF EXISTS (SELECT 1 FROM sys.index_columns ic
           JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
           JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
           WHERE ic.object_id = OBJECT_ID(N'dbo.NgCases') AND c.name = N'CaseNo' AND i.name <> N'UX_NgCases_CaseNo')
    THROW 50121, N'Có chỉ mục KHÁC UX_NgCases_CaseNo dùng cột NgCases.CaseNo — DỪNG (không tự xoá), gửi Claude kết quả câu sys.indexes.', 1;

DECLARE @coUx bit = 0, @laRangBuoc bit = 0;
SELECT @coUx = 1, @laRangBuoc = i.is_unique_constraint
FROM sys.indexes i WHERE i.object_id = OBJECT_ID(N'dbo.NgCases') AND i.name = N'UX_NgCases_CaseNo';
IF @coUx = 1 AND NOT EXISTS (
       SELECT 1 FROM sys.indexes i
       WHERE i.object_id = OBJECT_ID(N'dbo.NgCases') AND i.name = N'UX_NgCases_CaseNo'
         AND i.is_unique = 1 AND i.type_desc = N'NONCLUSTERED' AND i.has_filter = 0 AND i.ignore_dup_key = 0
         AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id) = 1
         AND EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                     WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND c.name = N'CaseNo' AND ic.is_included_column = 0))
    THROW 50123, N'UX_NgCases_CaseNo khác mô tả (UNIQUE NONCLUSTERED 1 cột CaseNo, không lọc) — DỪNG, gửi Claude kết quả câu sys.indexes.', 1;

IF EXISTS (SELECT 1 FROM sys.sql_expression_dependencies d WHERE d.referenced_id = OBJECT_ID(N'dbo.NgCases') AND d.referenced_minor_id =
           (SELECT column_id FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.NgCases') AND name = N'CaseNo'))
    THROW 50122, N'Có view / hàm / ràng buộc dùng cột NgCases.CaseNo — DỪNG, gửi Claude kết quả.', 1;

SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRAN;
        IF @coUx = 1 AND @laRangBuoc = 1 ALTER TABLE dbo.NgCases DROP CONSTRAINT UX_NgCases_CaseNo;
        IF @coUx = 1 AND @laRangBuoc = 0 DROP INDEX UX_NgCases_CaseNo ON dbo.NgCases;
        ALTER TABLE dbo.NgCases DROP COLUMN CaseNo;
        ALTER TABLE dbo.NgCases ADD CaseNo AS (
            'VL' + CASE WHEN NgCaseId < 1000000 THEN RIGHT('000000' + CAST(NgCaseId AS varchar(20)), 6)
                        ELSE CAST(NgCaseId AS varchar(20)) END) PERSISTED;
        -- tạo lại y như cũ (EXEC: cột vừa thêm trong cùng lô)
        IF @coUx = 1 AND @laRangBuoc = 1 EXEC (N'ALTER TABLE dbo.NgCases ADD CONSTRAINT UX_NgCases_CaseNo UNIQUE NONCLUSTERED (CaseNo);');
        IF @coUx = 1 AND @laRangBuoc = 0 EXEC (N'CREATE UNIQUE NONCLUSTERED INDEX UX_NgCases_CaseNo ON dbo.NgCases (CaseNo);');
    COMMIT;
    PRINT N'Đã đổi công thức CaseNo' + CASE WHEN @coUx = 1 THEN N' + tạo lại UX_NgCases_CaseNo.' ELSE N'.' END;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    PRINT N'LỖI — đã quay lại, KHÔNG đổi gì. Gửi Claude nội dung lỗi dưới đây.';
    THROW;
END CATCH
GO

-- Kiểm: mã các vụ hiện có không đổi (VL + 6 số), công thức mới (có case when … 1000000), UX_NgCases_CaseNo vẫn còn (UNIQUE)
SELECT TOP (5) NgCaseId, CaseNo FROM dbo.NgCases ORDER BY NgCaseId DESC;
SELECT cc.definition AS CongThucMoi FROM sys.computed_columns cc WHERE cc.object_id = OBJECT_ID(N'dbo.NgCases') AND cc.name = N'CaseNo';
SELECT i.name AS ChiMuc, i.type_desc, i.is_unique, i.has_filter FROM sys.indexes i
WHERE i.object_id = OBJECT_ID(N'dbo.NgCases') AND i.name = N'UX_NgCases_CaseNo';
GO
