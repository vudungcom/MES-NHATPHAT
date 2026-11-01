-- =====================================================================================================
-- [ML17-XN][CA-NGHI-HIST] 2026-11-01 — Bảng con WtsBreakWorkLogs: mọi khoảng nghỉ ca mà 1 dòng WTS /
--   1 dòng "Việc không ra sản phẩm" chạm vào, chép GIỜ NGHỈ LÚC LƯU (giờ tuyệt đối) + số phút làm thông nghỉ.
--   Append-only theo dòng cha: sửa dòng = huỷ dòng cha + dòng cha mới + bộ dòng nghỉ mới (không UPDATE / DELETE).
--   Không đụng bảng nào khác, không đổi dữ liệu cũ (dữ liệu cũ chép bù bằng tools/BackfillNghi, IsBackfill = 1).
--   SQL Server 2016 (không CREATE OR ALTER). Chạy lại nhiều lần không sao (có kiểm tồn tại).
--   Quay lại: DROP TABLE dbo.WtsBreakWorkLogs; (chỉ khi code ML17-XN chưa chạy — code mới đọc bảng này).
-- =====================================================================================================
USE [MES];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;   -- bắt buộc cho chỉ mục lọc
GO

-- Kiểm kiểu khoá 2 bảng cha (phải là bigint như code: long WtsLogId / long ActivityLogId)
IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
               WHERE c.object_id = OBJECT_ID(N'dbo.WtsProductionLogs') AND c.name = N'WtsLogId' AND t.name = N'bigint')
   OR NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
               WHERE c.object_id = OBJECT_ID(N'dbo.WorkerActivityLogs') AND c.name = N'ActivityLogId' AND t.name = N'bigint')
BEGIN
    RAISERROR(N'DỪNG: WtsProductionLogs.WtsLogId / WorkerActivityLogs.ActivityLogId không phải bigint — gửi lại Claude, chưa tạo gì.', 16, 1);
    SET NOEXEC ON;
END
GO

IF OBJECT_ID(N'dbo.WtsBreakWorkLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WtsBreakWorkLogs
    (
        Id            bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_WtsBreakWorkLogs PRIMARY KEY CLUSTERED,
        WtsLogId      bigint        NULL CONSTRAINT FK_WtsBreakWorkLogs_WtsProductionLogs  REFERENCES dbo.WtsProductionLogs (WtsLogId),
        ActivityLogId bigint        NULL CONSTRAINT FK_WtsBreakWorkLogs_WorkerActivityLogs REFERENCES dbo.WorkerActivityLogs (ActivityLogId),
        CaId          int           NULL,   -- chỉ tham chiếu (ca lúc lưu), KHÔNG khoá ngoại: ca / giờ nghỉ ca sửa / xoá sau không đổi dòng này
        NghiId        int           NULL,   -- chỉ tham chiếu (CaLamViecService.SaveAsync xoá + tạo lại giờ nghỉ → id cũ mất)
        BreakName     nvarchar(100) NULL,   -- ghi chú khoảng nghỉ chép lúc lưu (vd "Nghỉ trưa")
        BreakStart    datetime2(0)  NOT NULL,   -- giờ tuyệt đối đã xử lý ca qua đêm (không cắt theo dòng)
        BreakEnd      datetime2(0)  NOT NULL,
        OverlapMin    int           NOT NULL,   -- phần chồng giữa dòng cha và khoảng nghỉ (phút)
        WorkedMin     int           NOT NULL,   -- phút làm thông nghỉ (0 = nghỉ thật), khúc làm nằm ĐẦU phần chồng
        IsBackfill    bit           NOT NULL CONSTRAINT DF_WtsBreakWorkLogs_IsBackfill DEFAULT (0),   -- 1 = chép bù dữ liệu cũ (giờ nghỉ ca ngày chạy BackfillNghi)
        CreatedAt     datetime2     NOT NULL CONSTRAINT DF_WtsBreakWorkLogs_CreatedAt DEFAULT (SYSDATETIME()),
        CONSTRAINT CK_WtsBreakWorkLogs_OneParent CHECK ((WtsLogId IS NOT NULL AND ActivityLogId IS NULL) OR (WtsLogId IS NULL AND ActivityLogId IS NOT NULL)),
        CONSTRAINT CK_WtsBreakWorkLogs_Time      CHECK (BreakEnd > BreakStart),
        CONSTRAINT CK_WtsBreakWorkLogs_Min       CHECK (OverlapMin > 0 AND WorkedMin >= 0 AND WorkedMin <= OverlapMin
                                                        AND OverlapMin <= DATEDIFF(MINUTE, BreakStart, BreakEnd))
    );
    PRINT N'Đã tạo bảng dbo.WtsBreakWorkLogs';
END
ELSE PRINT N'Bảng dbo.WtsBreakWorkLogs đã có — bỏ qua tạo bảng';
GO

-- 1 dòng cha không có 2 bản cùng 1 khoảng nghỉ (chặn chép bù trùng)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.WtsBreakWorkLogs') AND name = N'UX_WtsBreakWorkLogs_Wts')
    CREATE UNIQUE NONCLUSTERED INDEX UX_WtsBreakWorkLogs_Wts ON dbo.WtsBreakWorkLogs (WtsLogId, BreakStart)
        INCLUDE (BreakEnd, OverlapMin, WorkedMin) WHERE WtsLogId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.WtsBreakWorkLogs') AND name = N'UX_WtsBreakWorkLogs_Act')
    CREATE UNIQUE NONCLUSTERED INDEX UX_WtsBreakWorkLogs_Act ON dbo.WtsBreakWorkLogs (ActivityLogId, BreakStart)
        INCLUDE (BreakEnd, OverlapMin, WorkedMin) WHERE ActivityLogId IS NOT NULL;
GO

SET NOEXEC OFF;
GO
-- Tự kiểm: in cấu trúc + số dòng (gửi ảnh kết quả cho Claude)
SELECT c.column_id, c.name, t.name AS kieu, c.max_length, c.is_nullable
FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID(N'dbo.WtsBreakWorkLogs') ORDER BY c.column_id;
SELECT name, type_desc, is_unique, has_filter, filter_definition FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.WtsBreakWorkLogs');
SELECT name, definition FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.WtsBreakWorkLogs');
SELECT COUNT(*) AS so_dong FROM dbo.WtsBreakWorkLogs;
GO
