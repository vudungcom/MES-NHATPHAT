USE [MES];
GO
-- =====================================================================================================================
-- 18_BAN_TRANG_D1.sql — [D1-BAN-TRANG 2026-11-20] BẢN TRẮNG lúc bàn giao (D1 = phương án B)  ·  SOẠN SẴN — CHƯA CHẠY
--   Nguồn: HÙNG 01/11 13:22 chọn D1 = B (lúc bàn giao dọn TOÀN BỘ dữ liệu giao dịch test, giữ danh mục; trước đó không dọn lẻ);
--          HÙNG 15/11 21:19 (khoá kh01 / vt01 / kho01 lúc bản trắng, không khoá bây giờ); QF-SQL-01 (bảng chết KhPlanRouteSnapshotMachiningTiming
--          0 dòng → DROP); HÙNG 20/11 18:12 (Gói 26: script CHỈ SOẠN, không chạy — máy anh vẫn đang test) + 18:32 OK theo đề xuất:
--          Part test "T1-PM-…" xoá cứng, tài khoản "*-test" khoá (không xoá), giữ lịch sử sửa danh mục.
--   CHỈ chạy trên DB đã backup (BACKUP DATABASE … COPY_ONLY) — đúng lúc bàn giao, theo danh sách QF-F (tài liệu kết quả quét mục "Sẵn sàng lên máy khách").
--   SQL Server 2016: không dùng CREATE OR ALTER / DROP … IF EXISTS.
--
--   CÁCH DÙNG (TẮT app MES trước khi chạy — tránh có người ghi giữa chừng; thử trước trên DB bản sao MES_TEST)
--     1. Chạy NGUYÊN file với @CHAY_THAT = 0 (mặc định): làm hết trong 1 giao dịch, in bảng "Trước / Sau" từng bảng rồi ROLLBACK — DB KHÔNG đổi.
--        Đọc bảng in ra: cột Sau của bảng giao dịch phải = 0, bảng danh mục giữ nguyên (trừ PartMasters bớt đúng số Part T1-PM-…).
--     2. Đúng rồi thì sửa @CHAY_THAT = 1, chạy lại → COMMIT.
--     Bước KIỂM TRƯỚC (trước BEGIN TRAN): thiếu bảng nào / bảng chết còn dòng → báo lỗi + DỪNG (SET NOEXEC ON), không xoá gì.
--     [D1-BANG-LA] Bảng "BangNgoaiDanhSach_GIU" in ra = bảng có dòng mà script không đụng — chạy thật phải đọc rồi đặt @DA_XEM_BANG_LA = 1.
--     Có lỗi khi xoá (khoá ngoại) → cả giao dịch tự quay lại (XACT_ABORT), DB giữ nguyên; chụp lỗi gửi Claude. Sau khi chạy xong nhớ SET NOEXEC OFF nếu bị dừng.
--     Sau khi khoá tài khoản mà KHÔNG còn tài khoản nhóm ADMIN nào hoạt động → báo lỗi + ROLLBACK.
--
--   XOÁ (giao dịch test): WTS (+ phát sinh, giờ nghỉ, số chuẩn), Việc không ra SP, Update TG chuẩn (lô + chi tiết), Giao nhận (phiếu, lần nhận,
--     báo lỗi sau nhận, Kết quả sửa + KCS xác nhận, phiếu bù, huỷ phần chưa nhận), vụ lỗi, Kho vật liệu (+ lịch sử), StockTransactions,
--     snapshot quy trình PO (mọi bảng KhPlanRouteSnapshot*), Kế hoạch / PO (+ lịch sử sửa PO, PO đã xoá). Part test PartNo
--     LIKE 'T1-PM%' (gồm cả T1-PM8-… do SqlDongThoi S8 tạo) + công đoạn / máy đồng dạng / xác nhận / thuộc tính / lịch sử của chính Part đó.
--   GIỮ (danh mục): khách hàng, Part Master (trừ T1-PM-…), người dùng + nhóm quyền, thiết bị, đồ gá, dao, sơ đồ tổ chức, ca làm việc, WTS tiêu chuẩn
--     + mã gộp + cấu hình nhóm, DS Lỗi, máy loại trừ, ổ lưu file, FileAttachments (chưa trang nào dùng), MỌI bảng *ChangeLogs của danh mục,
--     log mượn / trả đồ gá, log thiết bị.
--   KHOÁ (IsActive = 0, không xoá): kh01, vt01, kho01 (tài khoản mẫu seed C-07) + mọi tài khoản tên kết thúc "-test".
--   DROP: bảng chết KhPlanRouteSnapshotMachiningTiming (tên số ít, không có trong code) — CHỈ khi 0 dòng.
-- =====================================================================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @CHAY_THAT bit = 0;   -- 0 = chạy thử (ROLLBACK)  ·  1 = ghi thật (COMMIT)
DECLARE @DA_XEM_BANG_LA bit = 0;   -- [D1-BANG-LA 2026-11-20] chạy thật mà có bảng ngoài danh sách còn dòng → phải đọc bảng in ra rồi đặt 1 (xem bước KIỂM TRƯỚC)

IF OBJECT_ID('tempdb..#Dem') IS NOT NULL DROP TABLE #Dem;
CREATE TABLE #Dem (Thu int IDENTITY(1,1) PRIMARY KEY, Bang sysname NOT NULL, Loai nvarchar(20) NOT NULL, Truoc bigint NULL, Sau bigint NULL);
INSERT #Dem (Bang, Loai) VALUES
 (N'WtsExtraItems', N'xoá'), (N'WtsBreakWorkLogs', N'xoá'), (N'WtsProductionLogStds', N'xoá'), (N'StdUpdateBatchDetails', N'xoá'), (N'StdUpdateBatches', N'xoá'),
 (N'WtsProductionLogs', N'xoá'), (N'WorkerActivityLogs', N'xoá'),
 (N'HandoverRepairChecks', N'xoá'), (N'HandoverRepairResults', N'xoá'), (N'HandoverReplacements', N'xoá'), (N'HandoverTxCloses', N'xoá'),
 (N'HandoverNgReports', N'xoá'), (N'HandoverReceives', N'xoá'), (N'HandoverTransactions', N'xoá'), (N'NgCases', N'xoá'),
 (N'KhoVatLieuChangeLogs', N'xoá'), (N'KhoVatLieus', N'xoá'), (N'StockTransactions', N'xoá'),
 (N'KhPlanRouteSnapshotStdConfirms', N'xoá'), (N'KhPlanRouteSnapshotWtsGops', N'xoá'), (N'KhPlanRouteSnapshotMachineExclude', N'xoá'),
 (N'KhPlanRouteSnapshotMachiningTimings', N'xoá'), (N'KhPlanRouteSnapshotMachining', N'xoá'), (N'KhPlanRouteSnapshotTaro', N'xoá'),
 (N'KhPlanRouteSnapshotBavia', N'xoá'), (N'KhPlanRouteSnapshotWashing', N'xoá'), (N'KhPlanRouteSnapshotInspection', N'xoá'), (N'KhPlanRouteSnapshotPackaging', N'xoá'),
 (N'KhPlanRouteSnapshotMachiningTiming', N'DROP nếu 0'),
 (N'KhPlanDetailChangeLogs', N'xoá'), (N'KhPlanDeletedDetails', N'xoá'), (N'KhPlanDetails', N'xoá'), (N'KhPlans', N'xoá'),
 (N'PartMasters', N'bớt T1-PM'), (N'PartMasterAttributes', N'bớt T1-PM'), (N'PartMachiningSteps', N'bớt T1-PM'), (N'PartMachiningTimings', N'bớt T1-PM'),
 (N'PartMachiningTimingConfirms', N'bớt T1-PM'), (N'PartStepStdTimeConfirms', N'bớt T1-PM'), (N'PartProcessStepChangeLogs', N'bớt T1-PM'),
 (N'PartTaroSteps', N'bớt T1-PM'), (N'PartBaviaSteps', N'bớt T1-PM'), (N'PartWashingSteps', N'bớt T1-PM'), (N'PartInspectionSteps', N'bớt T1-PM'), (N'PartPackagingSteps', N'bớt T1-PM'),
 (N'Customers', N'giữ'), (N'Users', N'giữ (khoá)'), (N'UserGroups', N'giữ'), (N'ThietBis', N'giữ'), (N'DoGas', N'giữ'), (N'Daos', N'giữ'), (N'CaLamViecs', N'giữ'),
 (N'StandardWtsTasks', N'giữ'), (N'NgCategories', N'giữ'), (N'FileAttachments', N'giữ'), (N'DoGaMuonTraLogs', N'giữ'), (N'ThietBiChangeLogs', N'giữ');

DECLARE @b sysname, @q nvarchar(500), @n bigint;
DECLARE cTruoc CURSOR LOCAL FAST_FORWARD FOR SELECT Bang FROM #Dem;
OPEN cTruoc; FETCH NEXT FROM cTruoc INTO @b;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF OBJECT_ID(N'dbo.' + @b, N'U') IS NOT NULL
    BEGIN
        SET @q = N'SELECT @n = COUNT_BIG(1) FROM dbo.' + QUOTENAME(@b);
        EXEC sp_executesql @q, N'@n bigint OUTPUT', @n = @n OUTPUT;
        UPDATE #Dem SET Truoc = @n WHERE Bang = @b;
    END
    FETCH NEXT FROM cTruoc INTO @b;
END
CLOSE cTruoc; DEALLOCATE cTruoc;

-- [D1-BAN-TRANG] rà độc lập Gói 26 T2 / N1: KIỂM TRƯỚC — đủ bảng (lỗi 208 giữa giao dịch không bị XACT_ABORT bắt) + bảng chết phải 0 dòng
DECLARE @thieu nvarchar(max) = N'';
SELECT @thieu = @thieu + Bang + N', ' FROM #Dem WHERE Loai <> N'DROP nếu 0' AND OBJECT_ID(N'dbo.' + Bang, N'U') IS NULL;
IF LEN(@thieu) > 0
BEGIN
    RAISERROR(N'[D1] THIẾU BẢNG: %s — DỪNG, không xoá gì. Gửi Claude.', 16, 1, @thieu);
    SET NOEXEC ON;
END
IF OBJECT_ID(N'dbo.KhPlanRouteSnapshotMachiningTiming', N'U') IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.KhPlanRouteSnapshotMachiningTiming)
BEGIN
    RAISERROR(N'[D1] Bảng chết KhPlanRouteSnapshotMachiningTiming CÒN DÒNG — DỪNG, không xoá gì. Gửi Claude.', 16, 1);
    SET NOEXEC ON;
END
-- [D1-BANG-LA 2026-11-20] Bảng của DB KHÔNG có trong danh sách #Dem mà đang có dòng → script GIỮ NGUYÊN (không xoá). In ra để kiểm: bảng giao dịch mới
--   thêm sau Gói 26 (hoặc sót) sẽ còn dữ liệu test sau bản trắng. Danh mục (mọi *ChangeLogs, cấu hình nhóm WTS, mã gộp, sơ đồ tổ chức, …) giữ là đúng.
--   Chạy thật (@CHAY_THAT = 1) mà danh sách này KHÔNG trống → phải đọc rồi đặt @DA_XEM_BANG_LA = 1, không thì DỪNG.
--   Nguồn: CLAUDE rà độc lập Gói 26 20/11 (G26-27-GHI-NHO L9); HÙNG 20/11 22:51 OK Gói 28. Chỉ thêm bước kiểm — phần xoá / giữ không đổi.
SELECT t.name AS BangNgoaiDanhSach_GIU, SUM(p.rows) AS SoDong
FROM sys.tables t JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
WHERE t.is_ms_shipped = 0 AND SCHEMA_NAME(t.schema_id) = N'dbo' AND t.name COLLATE DATABASE_DEFAULT NOT IN (SELECT Bang COLLATE DATABASE_DEFAULT FROM #Dem)
GROUP BY t.name HAVING SUM(p.rows) > 0
ORDER BY t.name;
IF @CHAY_THAT = 1 AND @DA_XEM_BANG_LA = 0 AND EXISTS (
    SELECT 1 FROM sys.tables t JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
    WHERE t.is_ms_shipped = 0 AND SCHEMA_NAME(t.schema_id) = N'dbo' AND t.name COLLATE DATABASE_DEFAULT NOT IN (SELECT Bang COLLATE DATABASE_DEFAULT FROM #Dem) AND p.rows > 0)
BEGIN
    RAISERROR(N'[D1] Có bảng ngoài danh sách còn dòng (bảng "BangNgoaiDanhSach_GIU" ở trên) — đọc kỹ, đúng là danh mục cần giữ thì đặt @DA_XEM_BANG_LA = 1 rồi chạy lại. DỪNG, không xoá gì.', 16, 1);
    SET NOEXEC ON;
END
SELECT PartId, PartNo, PartName FROM dbo.PartMasters WHERE PartNo LIKE N'T1-PM%' ORDER BY PartNo;   -- danh sách Part test sẽ xoá

DECLARE @nKhoa int = 0, @nPartTest int = 0;
SELECT @nPartTest = COUNT(1) FROM dbo.PartMasters WHERE PartNo LIKE N'T1-PM%';

BEGIN TRAN;

-- (0) bảng chết: 0 dòng (đã kiểm trước) → DROP
IF OBJECT_ID(N'dbo.KhPlanRouteSnapshotMachiningTiming', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.KhPlanRouteSnapshotMachiningTiming;
    PRINT N'[D1] DROP bảng chết KhPlanRouteSnapshotMachiningTiming (0 dòng).';
END

-- (1) WTS + việc không ra SP + Update TG chuẩn
DELETE FROM dbo.WtsExtraItems;
DELETE FROM dbo.WtsBreakWorkLogs;
DELETE FROM dbo.WtsProductionLogStds;
DELETE FROM dbo.StdUpdateBatchDetails;
DELETE FROM dbo.StdUpdateBatches;
DELETE FROM dbo.WtsProductionLogs;
DELETE FROM dbo.WorkerActivityLogs;

-- (2) Giao nhận (phiếu trả tự lập trỏ lần nhận gốc → gỡ trỏ trước), vụ lỗi
DELETE FROM dbo.HandoverRepairChecks;
DELETE FROM dbo.HandoverRepairResults;
DELETE FROM dbo.HandoverReplacements;
DELETE FROM dbo.HandoverTxCloses;
DELETE FROM dbo.HandoverNgReports;
UPDATE dbo.HandoverTransactions SET SourceReceiveId = NULL WHERE SourceReceiveId IS NOT NULL;
DELETE FROM dbo.HandoverReceives;
DELETE FROM dbo.HandoverTransactions;
DELETE FROM dbo.NgCases;

-- (3) Kho vật liệu + StockTransactions
DELETE FROM dbo.KhoVatLieuChangeLogs;
DELETE FROM dbo.KhoVatLieus;
DELETE FROM dbo.StockTransactions;

-- (4) Snapshot quy trình PO (con trước cha)
DELETE FROM dbo.KhPlanRouteSnapshotStdConfirms;
DELETE FROM dbo.KhPlanRouteSnapshotWtsGops;
DELETE FROM dbo.KhPlanRouteSnapshotMachineExclude;
DELETE FROM dbo.KhPlanRouteSnapshotMachiningTimings;
DELETE FROM dbo.KhPlanRouteSnapshotMachining;
DELETE FROM dbo.KhPlanRouteSnapshotTaro;
DELETE FROM dbo.KhPlanRouteSnapshotBavia;
DELETE FROM dbo.KhPlanRouteSnapshotWashing;
DELETE FROM dbo.KhPlanRouteSnapshotInspection;
DELETE FROM dbo.KhPlanRouteSnapshotPackaging;

-- (5) Kế hoạch / PO
DELETE FROM dbo.KhPlanDetailChangeLogs;
DELETE FROM dbo.KhPlanDeletedDetails;
DELETE FROM dbo.KhPlanDetails;
DELETE FROM dbo.KhPlans;

-- (6) Part test T1-PM-… (xoá cứng cùng dữ liệu con của CHÍNH Part đó; Part khác + lịch sử của chúng giữ)
IF OBJECT_ID('tempdb..#PartTest') IS NOT NULL DROP TABLE #PartTest;
SELECT PartId INTO #PartTest FROM dbo.PartMasters WHERE PartNo LIKE N'T1-PM%';
DELETE FROM dbo.PartMachiningTimingConfirms WHERE PartId IN (SELECT PartId FROM #PartTest);
DELETE FROM dbo.PartStepStdTimeConfirms     WHERE PartId IN (SELECT PartId FROM #PartTest);
DELETE FROM dbo.PartProcessStepChangeLogs   WHERE PartId IN (SELECT PartId FROM #PartTest);
DELETE FROM dbo.PartMachiningTimings        WHERE StepId IN (SELECT StepId FROM dbo.PartMachiningSteps WHERE PartId IN (SELECT PartId FROM #PartTest));
DELETE FROM dbo.PartMachiningSteps          WHERE PartId IN (SELECT PartId FROM #PartTest);
DELETE FROM dbo.PartTaroSteps               WHERE PartId IN (SELECT PartId FROM #PartTest);
DELETE FROM dbo.PartBaviaSteps              WHERE PartId IN (SELECT PartId FROM #PartTest);
DELETE FROM dbo.PartWashingSteps            WHERE PartId IN (SELECT PartId FROM #PartTest);
DELETE FROM dbo.PartInspectionSteps         WHERE PartId IN (SELECT PartId FROM #PartTest);
DELETE FROM dbo.PartPackagingSteps          WHERE PartId IN (SELECT PartId FROM #PartTest);
DELETE FROM dbo.PartMasterAttributes        WHERE PartId IN (SELECT PartId FROM #PartTest);
DELETE FROM dbo.PartMasters                 WHERE PartId IN (SELECT PartId FROM #PartTest);

-- (7) Khoá tài khoản mẫu + tài khoản test (không xoá — lịch sử danh mục còn trỏ người sửa)
UPDATE dbo.Users SET IsActive = 0
WHERE IsActive = 1 AND (Username IN (N'kh01', N'vt01', N'kho01') OR Username LIKE N'%-test');
SET @nKhoa = @@ROWCOUNT;
-- rà độc lập Gói 26 L8: sau khi khoá phải còn ≥ 1 tài khoản nhóm ADMIN hoạt động
IF NOT EXISTS (SELECT 1 FROM dbo.Users u JOIN dbo.UserGroups g ON g.GroupId = u.GroupId WHERE u.IsActive = 1 AND g.GroupCode = N'ADMIN')
BEGIN
    ROLLBACK TRAN;
    RAISERROR(N'[D1] Sau khi khoá không còn tài khoản ADMIN nào hoạt động — đã ROLLBACK, không đổi gì. Gửi Claude.', 16, 1);
    SET NOEXEC ON;
END

-- (8) Đếm sau (trong giao dịch) + in
DECLARE cSau CURSOR LOCAL FAST_FORWARD FOR SELECT Bang FROM #Dem;
OPEN cSau; FETCH NEXT FROM cSau INTO @b;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF OBJECT_ID(N'dbo.' + @b, N'U') IS NOT NULL
    BEGIN
        SET @q = N'SELECT @n = COUNT_BIG(1) FROM dbo.' + QUOTENAME(@b);
        EXEC sp_executesql @q, N'@n bigint OUTPUT', @n = @n OUTPUT;
        UPDATE #Dem SET Sau = @n WHERE Bang = @b;
    END
    ELSE UPDATE #Dem SET Sau = NULL WHERE Bang = @b;
    FETCH NEXT FROM cSau INTO @b;
END
CLOSE cSau; DEALLOCATE cSau;

SELECT Thu, Bang, Loai, Truoc, Sau,
       CASE WHEN Loai = N'xoá' AND ISNULL(Sau, 0) <> 0 THEN N'LỆCH — còn dòng' ELSE N'' END AS KiemTra
FROM #Dem ORDER BY Thu;
SELECT @nPartTest AS PartTestXoa, @nKhoa AS TaiKhoanKhoa,
       (SELECT COUNT(1) FROM dbo.Users WHERE IsActive = 1) AS TaiKhoanConHoatDong;
SELECT Username, FullName, IsActive FROM dbo.Users ORDER BY IsActive DESC, Username;

IF @CHAY_THAT = 1
BEGIN
    COMMIT TRAN;
    PRINT N'[D1] ĐÃ GHI (COMMIT). Chạy lại trạm quet + mở app kiểm Kế hoạch / Giao nhận / WTS trống.';
END
ELSE
BEGIN
    ROLLBACK TRAN;
    PRINT N'[D1] CHẠY THỬ — đã ROLLBACK, DB không đổi. Đúng thì đặt @CHAY_THAT = 1 rồi chạy lại.';
END
GO
SET NOEXEC OFF;   -- bỏ chế độ dừng (nếu bước kiểm trước đã dừng)
GO
