USE [MES];
GO
-- =====================================================================
-- 08_KQSUA_HandoverRepair.sql — [GC-SUA-TAI-CHO] 04/11/2026 (NHÁP, CHỜ HÙNG DUYỆT)
-- Chứng từ nội bộ "Kết quả sửa" + "KCS xác nhận hàng sửa".
--   HandoverRepairResults : nhóm đang giữ hàng (Kho / GC / HTSP / KCS / Đóng gói) ghi kết quả sửa
--                           1 vụ lỗi, lấy từ ô Hàng sửa (SUA) hoặc ô Hàng lỗi (LOI);
--                           QtyPass = đạt (chờ KCS xác nhận), QtyFail = không đạt (chỉ khi lấy từ SUA → về ô Hàng lỗi cùng vụ).
--   HandoverRepairChecks  : mỗi lần KCS xác nhận (từng phần được): QtyOk = đạt → ô Hàng lỗi đã xử lý của nhóm sửa,
--                           QtyNg = KCS loại → ô Hàng lỗi của nhóm sửa, cùng vụ.
-- Không lưu số tồn: sổ chung HandoverLedgerService tính lại từ 2 bảng này (L6).
-- Append-only: sai thì huỷ (IsVoided + lý do + người + lúc), không UPDATE số, không DELETE.
-- SQL Server 2016: không CREATE OR ALTER. Chạy lại an toàn (IF OBJECT_ID … IS NULL).
-- Thứ tự: tắt app 5000 (hoặc để chạy cũng được — bảng mới, code cũ không đọc) → chạy file này → Claude mới ghi code.
-- Quay lại: DROP TABLE dbo.HandoverRepairChecks; DROP TABLE dbo.HandoverRepairResults; (chỉ khi chưa có dòng nào)
-- =====================================================================
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.HandoverRepairResults', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HandoverRepairResults
    (
        HandoverRepairResultId BIGINT IDENTITY(1,1) NOT NULL,
        KhPlanDetailId         INT            NOT NULL,
        GroupCode              NVARCHAR(20)   NOT NULL,   -- nhóm sửa = nhóm đang giữ hàng
        SourceCell             NVARCHAR(10)   NOT NULL,   -- SUA | LOI
        NgCaseId               BIGINT         NULL,       -- vụ lỗi lấy hàng (NULL = hàng lỗi cũ chưa có mã)
        QtyPass                DECIMAL(10,2)  NOT NULL CONSTRAINT DF_HandoverRepairResults_QtyPass DEFAULT (0),
        QtyFail                DECIMAL(10,2)  NOT NULL CONSTRAINT DF_HandoverRepairResults_QtyFail DEFAULT (0),
        ToNC                   NVARCHAR(20)   NULL,       -- Gia công: NC làm tiếp cho hàng đạt (NULL = như hàng mới)
        Notes                  NVARCHAR(500)  NULL,
        CreatedBy              INT            NOT NULL,
        CreatedAt              DATETIME2      NOT NULL,
        IsVoided               BIT            NOT NULL CONSTRAINT DF_HandoverRepairResults_IsVoided DEFAULT (0),
        VoidReason             NVARCHAR(200)  NULL,
        VoidedBy               INT            NULL,
        VoidedAt               DATETIME2      NULL,
        CONSTRAINT PK_HandoverRepairResults PRIMARY KEY CLUSTERED (HandoverRepairResultId),
        CONSTRAINT FK_HandoverRepairResults_KhPlanDetails FOREIGN KEY (KhPlanDetailId) REFERENCES dbo.KhPlanDetails (KhPlanDetailId),
        CONSTRAINT FK_HandoverRepairResults_NgCases       FOREIGN KEY (NgCaseId)       REFERENCES dbo.NgCases (NgCaseId),
        CONSTRAINT FK_HandoverRepairResults_CreatedBy     FOREIGN KEY (CreatedBy)      REFERENCES dbo.Users (UserId),
        CONSTRAINT FK_HandoverRepairResults_VoidedBy      FOREIGN KEY (VoidedBy)       REFERENCES dbo.Users (UserId),
        CONSTRAINT CK_HandoverRepairResults_Group  CHECK (GroupCode IN (N'KHO', N'GC', N'HTSP', N'KCS', N'PKG')),
        CONSTRAINT CK_HandoverRepairResults_Cell   CHECK (SourceCell IN (N'SUA', N'LOI')),
        CONSTRAINT CK_HandoverRepairResults_Qty    CHECK (QtyPass >= 0 AND QtyFail >= 0 AND QtyPass + QtyFail > 0),
        CONSTRAINT CK_HandoverRepairResults_Fail   CHECK (SourceCell = N'SUA' OR QtyFail = 0)
    );
    CREATE INDEX IX_HandoverRepairResults_Detail ON dbo.HandoverRepairResults (KhPlanDetailId, IsVoided);
END
GO

IF OBJECT_ID(N'dbo.HandoverRepairChecks', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HandoverRepairChecks
    (
        HandoverRepairCheckId  BIGINT IDENTITY(1,1) NOT NULL,
        HandoverRepairResultId BIGINT         NOT NULL,
        QtyOk                  DECIMAL(10,2)  NOT NULL CONSTRAINT DF_HandoverRepairChecks_QtyOk DEFAULT (0),   -- KCS xác nhận đạt
        QtyNg                  DECIMAL(10,2)  NOT NULL CONSTRAINT DF_HandoverRepairChecks_QtyNg DEFAULT (0),   -- KCS loại (về ô Hàng lỗi nhóm sửa)
        Notes                  NVARCHAR(500)  NULL,
        CheckedBy              INT            NOT NULL,
        CheckedAt              DATETIME2      NOT NULL,
        IsVoided               BIT            NOT NULL CONSTRAINT DF_HandoverRepairChecks_IsVoided DEFAULT (0),
        VoidReason             NVARCHAR(200)  NULL,
        VoidedBy               INT            NULL,
        VoidedAt               DATETIME2      NULL,
        CONSTRAINT PK_HandoverRepairChecks PRIMARY KEY CLUSTERED (HandoverRepairCheckId),
        CONSTRAINT FK_HandoverRepairChecks_Result    FOREIGN KEY (HandoverRepairResultId) REFERENCES dbo.HandoverRepairResults (HandoverRepairResultId),
        CONSTRAINT FK_HandoverRepairChecks_CheckedBy FOREIGN KEY (CheckedBy) REFERENCES dbo.Users (UserId),
        CONSTRAINT FK_HandoverRepairChecks_VoidedBy  FOREIGN KEY (VoidedBy)  REFERENCES dbo.Users (UserId),
        CONSTRAINT CK_HandoverRepairChecks_Qty CHECK (QtyOk >= 0 AND QtyNg >= 0 AND QtyOk + QtyNg > 0)
    );
    CREATE INDEX IX_HandoverRepairChecks_Result ON dbo.HandoverRepairChecks (HandoverRepairResultId, IsVoided);
END
GO

-- Kiểm sau khi chạy (phải ra 2 dòng, 0 bản ghi):
SELECT t.name, (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS SoCot
FROM sys.tables t WHERE t.name IN (N'HandoverRepairResults', N'HandoverRepairChecks');
SELECT (SELECT COUNT(*) FROM dbo.HandoverRepairResults) AS KetQuaSua, (SELECT COUNT(*) FROM dbo.HandoverRepairChecks) AS KcsXacNhan;
GO
