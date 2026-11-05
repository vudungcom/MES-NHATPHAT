USE [MES];
GO
-- =====================================================================
-- 09_PM_T4_UniqueNC.sql — [PM-T4] 05/11/2026 (Hùng duyệt 05/11 14:02: dữ liệu test, Part 1021 là nháp, xoá được)
-- Ràng buộc DB: mỗi Part, mỗi bảng công đoạn chỉ 1 dòng CHÍNH đang dùng mỗi mã NC
--   (Gia công / Bavia / Rửa / KCS / Đóng gói — PartId + NC, lọc IsActive = 1 AND IsBackup = 0).
--   Lớp chặn cuối sau service [PM-NC-DUP-SVC] (2 người cùng sửa 1 Part / lỗi code sau này không tạo được mã trùng).
--   Taro KHÔNG làm: khách chốt 02/11 giữ mã CV chung nhiều NC (gói TARO-NC-MA). Dòng dự phòng không xét.
-- Bước 1: dọn 2 dòng trùng duy nhất (QuetSo PM_TRUNG 05/11 10:31) — Part 1021 "part mới" (AMATA, PO aaa / tttttt):
--   Bavia StepId 10017 (trùng 2V với 10016), Rửa StepId 15 (trùng 1R với 14). Xoá như service (SoftDeleteAsync):
--   ghi PartProcessStepChangeLogs FieldName = Deleted + bản chụp JSON dòng cũ, rồi xoá cứng; không đụng dòng dự phòng.
-- Bước 2: còn mã trùng ở đâu thì DỪNG, lùi hết (không xoá, không tạo chỉ mục) và in danh sách.
-- Bước 3: tạo 5 chỉ mục (chạy lại an toàn: có rồi thì bỏ qua).
-- SQL Server 2016: không CREATE OR ALTER; chỉ mục lọc cần SET QUOTED_IDENTIFIER / ANSI_* như dưới.
-- Chạy lúc app 5000 đang chạy cũng được (vài giây).
-- Quay lại: DROP INDEX UX_PartMachiningSteps_PartId_NC_Main ON dbo.PartMachiningSteps; (tương tự 4 bảng còn lại)
-- =====================================================================
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @lyDo NVARCHAR(500) = N'[PM-T4] dọn mã chính trùng trước khi tạo ràng buộc DB (Hùng 05/11: Part 1021 là bản nháp)';
    DECLARE @nguoi INT = (SELECT TOP 1 u.UserId FROM dbo.Users u JOIN dbo.UserGroups g ON g.GroupId = u.GroupId
                          WHERE g.GroupCode = N'ADMIN' AND u.IsActive = 1 ORDER BY u.UserId);
    IF @nguoi IS NULL RAISERROR(N'Không tìm thấy tài khoản ADMIN đang dùng để ghi lịch sử xoá.', 16, 1);

    -- ── Bước 1a: Bavia StepId 10017 (chỉ khi vẫn đúng là dòng chính 2V trùng của Part 1021) ──
    IF EXISTS (SELECT 1 FROM dbo.PartBaviaSteps s WHERE s.StepId = 10017 AND s.PartId = 1021 AND s.IsActive = 1 AND s.IsBackup = 0
               AND EXISTS (SELECT 1 FROM dbo.PartBaviaSteps o WHERE o.PartId = s.PartId AND o.StepId <> s.StepId
                           AND o.IsActive = 1 AND o.IsBackup = 0 AND UPPER(LTRIM(RTRIM(o.NC))) = UPPER(LTRIM(RTRIM(s.NC)))))
    BEGIN
        INSERT INTO dbo.PartProcessStepChangeLogs (StepTable, StepId, TimingId, PartId, FieldName, OldValue, NewValue, ChangedBy, ChangedAt, Reason)
        SELECT N'Bavia', s.StepId, NULL, s.PartId, N'Deleted',
               (SELECT x.StepOrder, x.NC, x.StepName, x.StandardTime, x.IsBackup, x.ParentNC, x.CreatedBy, x.CreatedAt, x.UpdatedBy, x.UpdatedAt
                  FROM dbo.PartBaviaSteps x WHERE x.StepId = s.StepId FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
               NULL, @nguoi, GETDATE(), @lyDo
          FROM dbo.PartBaviaSteps s WHERE s.StepId = 10017;
        DELETE FROM dbo.PartBaviaSteps WHERE StepId = 10017;
        PRINT N'Đã xoá Bavia StepId 10017 (Part 1021, trùng 2V) + ghi lịch sử.';
    END
    ELSE PRINT N'Bỏ qua Bavia StepId 10017: không còn / không còn trùng.';

    -- ── Bước 1b: Rửa StepId 15 ──
    IF EXISTS (SELECT 1 FROM dbo.PartWashingSteps s WHERE s.StepId = 15 AND s.PartId = 1021 AND s.IsActive = 1 AND s.IsBackup = 0
               AND EXISTS (SELECT 1 FROM dbo.PartWashingSteps o WHERE o.PartId = s.PartId AND o.StepId <> s.StepId
                           AND o.IsActive = 1 AND o.IsBackup = 0 AND UPPER(LTRIM(RTRIM(o.NC))) = UPPER(LTRIM(RTRIM(s.NC)))))
    BEGIN
        INSERT INTO dbo.PartProcessStepChangeLogs (StepTable, StepId, TimingId, PartId, FieldName, OldValue, NewValue, ChangedBy, ChangedAt, Reason)
        SELECT N'Washing', s.StepId, NULL, s.PartId, N'Deleted',
               (SELECT x.StepOrder, x.NC, x.StepName, x.StandardTime, x.IsBackup, x.ParentNC, x.CreatedBy, x.CreatedAt, x.UpdatedBy, x.UpdatedAt
                  FROM dbo.PartWashingSteps x WHERE x.StepId = s.StepId FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
               NULL, @nguoi, GETDATE(), @lyDo
          FROM dbo.PartWashingSteps s WHERE s.StepId = 15;
        DELETE FROM dbo.PartWashingSteps WHERE StepId = 15;
        PRINT N'Đã xoá Rửa StepId 15 (Part 1021, trùng 1R) + ghi lịch sử.';
    END
    ELSE PRINT N'Bỏ qua Rửa StepId 15: không còn / không còn trùng.';

    -- ── Bước 2: còn trùng thì dừng ──
    DECLARE @trung TABLE (Bang NVARCHAR(20), PartId INT, NC NVARCHAR(20), SoDong INT);
    INSERT INTO @trung
    SELECT N'Gia công', PartId, NC, COUNT(*) FROM dbo.PartMachiningSteps  WHERE IsActive = 1 AND IsBackup = 0 GROUP BY PartId, NC HAVING COUNT(*) > 1 UNION ALL
    SELECT N'Bavia',    PartId, NC, COUNT(*) FROM dbo.PartBaviaSteps      WHERE IsActive = 1 AND IsBackup = 0 GROUP BY PartId, NC HAVING COUNT(*) > 1 UNION ALL
    SELECT N'Rửa',      PartId, NC, COUNT(*) FROM dbo.PartWashingSteps    WHERE IsActive = 1 AND IsBackup = 0 GROUP BY PartId, NC HAVING COUNT(*) > 1 UNION ALL
    SELECT N'KCS',      PartId, NC, COUNT(*) FROM dbo.PartInspectionSteps WHERE IsActive = 1 AND IsBackup = 0 GROUP BY PartId, NC HAVING COUNT(*) > 1 UNION ALL
    SELECT N'Đóng gói', PartId, NC, COUNT(*) FROM dbo.PartPackagingSteps  WHERE IsActive = 1 AND IsBackup = 0 GROUP BY PartId, NC HAVING COUNT(*) > 1;
    IF EXISTS (SELECT 1 FROM @trung)
    BEGIN
        SELECT t.Bang AS [Bảng], t.PartId, p.PartNo, t.NC, t.SoDong AS [Số dòng chính] FROM @trung t LEFT JOIN dbo.PartMasters p ON p.PartId = t.PartId;
        RAISERROR(N'DỪNG: còn mã chính trùng (danh sách ở trên) — đã lùi hết, chưa xoá / chưa tạo chỉ mục. Gửi Claude danh sách này.', 16, 1);
    END

    -- ── Bước 3: chỉ mục duy nhất có lọc ──
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_PartMachiningSteps_PartId_NC_Main' AND object_id = OBJECT_ID(N'dbo.PartMachiningSteps'))
        CREATE UNIQUE NONCLUSTERED INDEX UX_PartMachiningSteps_PartId_NC_Main ON dbo.PartMachiningSteps (PartId, NC) WHERE IsActive = 1 AND IsBackup = 0;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_PartBaviaSteps_PartId_NC_Main' AND object_id = OBJECT_ID(N'dbo.PartBaviaSteps'))
        CREATE UNIQUE NONCLUSTERED INDEX UX_PartBaviaSteps_PartId_NC_Main ON dbo.PartBaviaSteps (PartId, NC) WHERE IsActive = 1 AND IsBackup = 0;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_PartWashingSteps_PartId_NC_Main' AND object_id = OBJECT_ID(N'dbo.PartWashingSteps'))
        CREATE UNIQUE NONCLUSTERED INDEX UX_PartWashingSteps_PartId_NC_Main ON dbo.PartWashingSteps (PartId, NC) WHERE IsActive = 1 AND IsBackup = 0;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_PartInspectionSteps_PartId_NC_Main' AND object_id = OBJECT_ID(N'dbo.PartInspectionSteps'))
        CREATE UNIQUE NONCLUSTERED INDEX UX_PartInspectionSteps_PartId_NC_Main ON dbo.PartInspectionSteps (PartId, NC) WHERE IsActive = 1 AND IsBackup = 0;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_PartPackagingSteps_PartId_NC_Main' AND object_id = OBJECT_ID(N'dbo.PartPackagingSteps'))
        CREATE UNIQUE NONCLUSTERED INDEX UX_PartPackagingSteps_PartId_NC_Main ON dbo.PartPackagingSteps (PartId, NC) WHERE IsActive = 1 AND IsBackup = 0;

    COMMIT TRANSACTION;
    PRINT N'XONG: 5 chỉ mục UX_*_PartId_NC_Main đã có.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    DECLARE @msg NVARCHAR(2000) = ERROR_MESSAGE();
    RAISERROR(N'LỖI — đã lùi hết, DB không đổi: %s', 16, 1, @msg);
END CATCH
GO

-- Kiểm sau khi chạy (phải ra 5 dòng, is_unique = 1, filter IsActive = 1 AND IsBackup = 0):
SELECT OBJECT_NAME(object_id) AS [Bảng], name, is_unique, filter_definition
  FROM sys.indexes WHERE name LIKE N'UX[_]Part%[_]PartId[_]NC[_]Main' ORDER BY 1;
-- Lịch sử xoá vừa ghi:
SELECT TOP 5 ChangeId, StepTable, StepId, PartId, FieldName, ChangedAt, Reason
  FROM dbo.PartProcessStepChangeLogs WHERE Reason LIKE N'[[]PM-T4]%' ORDER BY ChangeId DESC;
GO
