USE [MES];
GO
-- =====================================================================================================================
-- 19_SAO_LUU.sql — [G32B-SQL19 2026-11-23] Chuẩn bị DB cho SAO LƯU trong phần mềm (Gói 32a / 32b)  ·  SOẠN SẴN — CHƯA CHẠY, CHỜ HÙNG DUYỆT
--   Nguồn: HÙNG 22/11 14:52 (máy khách đặt SIMPLE qua SQL gửi duyệt) + 23/11 13:20 OK Gói 32b ("soạn 19_SAO_LUU.sql CHỈ SOẠN gửi duyệt").
--   Chạy 1 lần trên DB máy khách (lúc cài máy khách, sau khi dựng DB). SQL Server 2016 trở lên. Chạy bằng tài khoản sysadmin (SSMS).
--
--   LÀM 2 VIỆC:
--   (1) RECOVERY SIMPLE. Phần mềm tự sao lưu TRỌN DB (BACKUP … COPY_ONLY) mỗi 60 phút + bộ ngày — KHÔNG sao lưu log riêng. Ở chế độ FULL
--       (DB test 211 đang FULL) file log (.ldf) chỉ được dọn khi có sao lưu log → phình mãi tới đầy ổ. SIMPLE: log tự dọn sau mỗi CHECKPOINT.
--       Đổi lại: không khôi phục được "tới từng phút" giữa 2 bản (mất tối đa 1 chu kỳ lưu nhanh ~60 phút) — đúng mức Hùng chốt 22/11 (lưu mỗi giờ).
--       Không mất dữ liệu đang có, không khoá DB lâu (ALTER DATABASE … SET RECOVERY chạy tức thì).
--   (2) Quyền BACKUP cho tài khoản SQL của phần mềm: thêm vào vai trò db_backupoperator (chỉ được BACKUP, không đọc / sửa thêm gì).
--       Đã là db_owner (hoặc sysadmin) thì bỏ qua — db_owner vốn BACKUP được. KHÔNG cấp quyền RESTORE (khôi phục làm bằng script
--       khoi-phuc.ps1 dưới tài khoản Windows Administrator = sysadmin, không qua phần mềm).
--
--   CÁCH DÙNG
--     1. Điền @TAI_KHOAN = tên đăng nhập SQL của phần mềm (mục User Id trong appsettings.json, vd N'mes_app').
--     2. Chạy NGUYÊN file với @CHAY_THAT = 0 (mặc định): CHỈ IN hiện trạng + việc sẽ làm, KHÔNG đổi gì (ALTER DATABASE không chạy được
--        trong giao dịch → chế độ thử là không chạy lệnh, không phải ROLLBACK).
--     3. Đúng rồi thì sửa @CHAY_THAT = 1, chạy lại. Chạy lại lần 2 vô hại (đã SIMPLE / đã có quyền thì bỏ qua).
--   KHÔNG thu nhỏ file log tự động (DBCC SHRINKFILE làm phân mảnh, chỉ cần 1 lần nếu log đang quá to) — file in câu lệnh gợi ý khi log > 1 GB.
-- =====================================================================================================================
SET NOCOUNT ON;
DECLARE @CHAY_THAT bit = 0;            -- 0 = chỉ in (không đổi gì)  ·  1 = làm thật
DECLARE @TAI_KHOAN sysname = N'';      -- tên đăng nhập SQL của phần mềm (appsettings.json → User Id). Trống = chỉ làm việc (1).

DECLARE @db sysname = DB_NAME();
DECLARE @rm nvarchar(60), @rcsi bit, @logMb decimal(18,1), @logWait nvarchar(60), @s nvarchar(max);

IF @db IN (N'master', N'model', N'msdb', N'tempdb')
BEGIN
    RAISERROR(N'DỪNG: đang đứng ở DB hệ thống %s — dòng đầu file phải là USE [<DB của phần mềm>].', 16, 1, @db);
    RETURN;
END
IF IS_SRVROLEMEMBER(N'sysadmin') <> 1
BEGIN
    RAISERROR(N'DỪNG: tài khoản đang chạy không phải sysadmin — mở SSMS bằng tài khoản quản trị SQL.', 16, 1);
    RETURN;
END

SELECT @rm = d.recovery_model_desc, @rcsi = d.is_read_committed_snapshot_on, @logWait = d.log_reuse_wait_desc
FROM sys.databases d WHERE d.name = @db;
SELECT @logMb = CAST(SUM(CAST(f.size AS bigint)) * 8 / 1024.0 AS decimal(18,1)) FROM sys.database_files f WHERE f.type_desc = N'LOG';

PRINT N'=== HIỆN TRẠNG DB ' + @db + N' ===';
PRINT N'  Recovery model: ' + @rm + N'   (cần SIMPLE)';
PRINT N'  File log: ' + CAST(@logMb AS nvarchar(30)) + N' MB · đang chờ dọn log vì: ' + @logWait;
PRINT N'  READ_COMMITTED_SNAPSHOT: ' + CAST(@rcsi AS nvarchar(5)) + N'   (phần mềm cần 1 — ISO-FIX 31/10; script này KHÔNG đổi)';

-- (2) tài khoản phần mềm
DECLARE @user sysname = NULL, @laOwner bit = 0, @laBkOp bit = 0, @laSa bit = 0;
IF LEN(@TAI_KHOAN) > 0
BEGIN
    IF SUSER_ID(@TAI_KHOAN) IS NULL
    BEGIN
        RAISERROR(N'DỪNG: không có tài khoản đăng nhập SQL "%s" trên máy chủ này — kiểm lại @TAI_KHOAN (appsettings.json → User Id).', 16, 1, @TAI_KHOAN);
        RETURN;
    END
    SELECT @user = dp.name FROM sys.database_principals dp WHERE dp.sid = SUSER_SID(@TAI_KHOAN);
    SET @laSa = CASE WHEN IS_SRVROLEMEMBER(N'sysadmin', @TAI_KHOAN) = 1 THEN 1 ELSE 0 END;
    IF @user IS NULL AND @laSa = 0
    BEGIN
        RAISERROR(N'DỪNG: tài khoản "%s" chưa là người dùng của DB này (chưa map) — phần mềm sẽ không vào được DB; kiểm lại trước.', 16, 1, @TAI_KHOAN);
        RETURN;
    END
    IF @user IS NOT NULL
    BEGIN
        SELECT @laOwner = CASE WHEN EXISTS (SELECT 1 FROM sys.database_role_members rm JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
                                            JOIN sys.database_principals m ON m.principal_id = rm.member_principal_id
                                            WHERE r.name = N'db_owner' AND m.name = @user) OR @user = N'dbo' THEN 1 ELSE 0 END;
        SELECT @laBkOp = CASE WHEN EXISTS (SELECT 1 FROM sys.database_role_members rm JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
                                           JOIN sys.database_principals m ON m.principal_id = rm.member_principal_id
                                           WHERE r.name = N'db_backupoperator' AND m.name = @user) THEN 1 ELSE 0 END;
    END
    PRINT N'  Tài khoản phần mềm: ' + @TAI_KHOAN + N' → người dùng DB: ' + ISNULL(@user, N'(sysadmin, không cần)')
        + N' · sysadmin ' + CAST(@laSa AS nvarchar(1)) + N' · db_owner ' + CAST(@laOwner AS nvarchar(1)) + N' · db_backupoperator ' + CAST(@laBkOp AS nvarchar(1));
END
ELSE PRINT N'  (@TAI_KHOAN trống — bỏ qua việc cấp quyền BACKUP)';

PRINT N'';
PRINT N'=== VIỆC ' + CASE WHEN @CHAY_THAT = 1 THEN N'LÀM' ELSE N'SẼ LÀM (chạy thử — KHÔNG đổi gì)' END + N' ===';

-- (1) SIMPLE
IF @rm <> N'SIMPLE'
BEGIN
    PRINT N'  (1) ALTER DATABASE ' + QUOTENAME(@db) + N' SET RECOVERY SIMPLE; CHECKPOINT;';
    IF @CHAY_THAT = 1
    BEGIN
        SET @s = N'ALTER DATABASE ' + QUOTENAME(@db) + N' SET RECOVERY SIMPLE;';
        EXEC (@s);
        CHECKPOINT;
        PRINT N'      → đã đổi sang SIMPLE.';
    END
END
ELSE PRINT N'  (1) đã SIMPLE — bỏ qua.';

-- (2) db_backupoperator
IF @user IS NOT NULL AND @laSa = 0 AND @laOwner = 0 AND @laBkOp = 0
BEGIN
    PRINT N'  (2) ALTER ROLE db_backupoperator ADD MEMBER ' + QUOTENAME(@user) + N';';
    IF @CHAY_THAT = 1
    BEGIN
        SET @s = N'ALTER ROLE db_backupoperator ADD MEMBER ' + QUOTENAME(@user) + N';';
        EXEC (@s);
        PRINT N'      → đã cấp quyền BACKUP.';
    END
END
ELSE IF LEN(@TAI_KHOAN) > 0 PRINT N'  (2) tài khoản đã BACKUP được (sysadmin / db_owner / db_backupoperator) — bỏ qua.';

IF @logMb > 1024
BEGIN
    PRINT N'';
    PRINT N'  GỢI Ý (không tự chạy): file log ' + CAST(@logMb AS nvarchar(30)) + N' MB. Sau khi đã SIMPLE + phần mềm lưu được 1 bản, thu nhỏ 1 lần:';
    SELECT @s = N'      USE ' + QUOTENAME(@db) + N'; DBCC SHRINKFILE (' + QUOTENAME(f.name) + N', 512);' FROM sys.database_files f WHERE f.type_desc = N'LOG';
    PRINT @s;
END

PRINT N'';
SELECT d.name AS DB, d.recovery_model_desc AS RecoveryModel_Sau, d.log_reuse_wait_desc AS ChoDonLog,
       CASE WHEN @CHAY_THAT = 1 THEN N'ĐÃ CHẠY THẬT' ELSE N'CHẠY THỬ — chưa đổi gì' END AS CheDo
FROM sys.databases d WHERE d.name = @db;
GO
