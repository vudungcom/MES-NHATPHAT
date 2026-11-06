-- =====================================================================================================
-- [TARO-NC-MA][TARO-3T-GOP] 2026-11-03 — BẢN CHỐT 06/11 GỬI HÙNG DUYỆT (CHƯA CHẠY): rà lại với master 4b05c99 — độ dài cột khớp
--   (PartTaroSteps.NC / WtsProductionLogs.NC = 20, StandardWtsTasks.TaskCode = 50), chưa có cột / bảng trùng tên; nội dung không đổi so với bản nháp 03/11.
--   Khách chốt 02/11 09:14 (anh Ngọc): Taro làm 2 lần (2 mặt) dùng CHUNG bộ mã CV giữa các NC → form WTS Taro thêm ô chọn NC
--   (phương án B); 3T là cách làm gộp THAY cho 3T1 + 3T2 + 3T3. Hùng chốt 02/11 10:34: khai rõ 3T gộp trong WTS tiêu chuẩn
--   (không suy theo tên mã).
--   1. WtsProductionLogs.StepNC   — NC của dòng quy trình Taro mà dòng WTS làm (cột NC cũ của dòng HTSP đang chở mã CV ở nhiều chỗ
--                                   — báo cáo, Update TG chuẩn, luồng NC — nên KHÔNG dùng lại cột NC). NULL = dòng cũ / mã không trùng NC.
--   2. StandardWtsTaskGops        — WTS tiêu chuẩn: mã gộp (3T) = các mã thành phần (3T1, 3T2, 3T3). Bảng con, không CSV.
--                                   Ngưng = IsActive 0 (không xoá cứng, giữ ai / lúc nào).
--   3. KhPlanRouteSnapshotWtsGops — chụp khai báo gộp vào PO lúc tạo / làm mới quy trình (PO đã chạy không đổi theo WTS tiêu chuẩn — K6).
--   SQL Server 2016 (không CREATE OR ALTER). Chạy lại nhiều lần không sao (có kiểm tồn tại). Không đổi dữ liệu cũ.
--   Quay lại (chỉ khi code TARO chưa chạy): DROP TABLE dbo.KhPlanRouteSnapshotWtsGops; DROP TABLE dbo.StandardWtsTaskGops;
--                                           ALTER TABLE dbo.WtsProductionLogs DROP COLUMN StepNC;
-- =====================================================================================================
USE [MES];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- 1. Cột StepNC (độ dài = PartTaroSteps.NC / KhPlanRouteSnapshotTaro.NC = 20)
IF COL_LENGTH(N'dbo.WtsProductionLogs', N'StepNC') IS NULL
    ALTER TABLE dbo.WtsProductionLogs ADD StepNC nvarchar(20) NULL;
GO

-- 2. Khai báo mã gộp trong WTS tiêu chuẩn (TaskCode = StandardWtsTasks.TaskCode, nvarchar(50))
IF OBJECT_ID(N'dbo.StandardWtsTaskGops', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StandardWtsTaskGops
    (
        Id            int IDENTITY(1,1) NOT NULL CONSTRAINT PK_StandardWtsTaskGops PRIMARY KEY CLUSTERED,
        GopTaskCode   nvarchar(50) NOT NULL,   -- vd 3T
        PartTaskCode  nvarchar(50) NOT NULL,   -- vd 3T1 / 3T2 / 3T3 (mỗi mã 1 dòng)
        IsActive      bit NOT NULL CONSTRAINT DF_StandardWtsTaskGops_IsActive DEFAULT (1),
        CreatedBy     int NULL,
        CreatedAt     datetime2 NOT NULL CONSTRAINT DF_StandardWtsTaskGops_CreatedAt DEFAULT (SYSDATETIME()),
        RemovedBy     int NULL,
        RemovedAt     datetime2 NULL
    );
    -- 1 cặp gộp → thành phần chỉ 1 dòng đang dùng
    CREATE UNIQUE INDEX UX_StandardWtsTaskGops_Active ON dbo.StandardWtsTaskGops (GopTaskCode, PartTaskCode) WHERE IsActive = 1;
END
GO

-- 3. Chụp vào PO (cùng kiểu các bảng KhPlanRouteSnapshot*: FK NO ACTION vào KhPlanDetails)
IF OBJECT_ID(N'dbo.KhPlanRouteSnapshotWtsGops', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.KhPlanRouteSnapshotWtsGops
    (
        SnapshotId      bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_KhPlanRouteSnapshotWtsGops PRIMARY KEY CLUSTERED,
        KhPlanDetailId  int NOT NULL CONSTRAINT FK_KhPlanRouteSnapshotWtsGops_KhPlanDetails
                            FOREIGN KEY REFERENCES dbo.KhPlanDetails (KhPlanDetailId),
        GopCode         nvarchar(50) NOT NULL,
        PartCode        nvarchar(50) NOT NULL,
        SnapshotAt      datetime2 NOT NULL CONSTRAINT DF_KhPlanRouteSnapshotWtsGops_At DEFAULT (SYSDATETIME()),
        SnapshotBy      int NULL
    );
    CREATE INDEX IX_KhPlanRouteSnapshotWtsGops_Detail ON dbo.KhPlanRouteSnapshotWtsGops (KhPlanDetailId);
END
GO

SELECT N'StepNC' AS Muc, CASE WHEN COL_LENGTH(N'dbo.WtsProductionLogs', N'StepNC') IS NULL THEN N'CHƯA' ELSE N'CÓ' END AS TrangThai
UNION ALL SELECT N'StandardWtsTaskGops', CASE WHEN OBJECT_ID(N'dbo.StandardWtsTaskGops', N'U') IS NULL THEN N'CHƯA' ELSE N'CÓ' END
UNION ALL SELECT N'KhPlanRouteSnapshotWtsGops', CASE WHEN OBJECT_ID(N'dbo.KhPlanRouteSnapshotWtsGops', N'U') IS NULL THEN N'CHƯA' ELSE N'CÓ' END;
GO
