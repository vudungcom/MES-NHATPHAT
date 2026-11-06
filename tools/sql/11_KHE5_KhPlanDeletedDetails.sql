-- =====================================================================================================
-- [KHE-5] 2026-11-06 — BẢN GỬI HÙNG DUYỆT (CHƯA CHẠY). Hùng 05/11 15:37 chốt A.
--   Vì sao: xoá PO trắng (KhPlanService.DeleteDetailAsync) đang DELETE luôn KhPlanDetailChangeLogs của PO (khoá ngoại không null) và
--   không ghi ai xoá / lúc nào / lý do — trái luật log chỉ thêm. Bảng mới chụp JSON dòng PO + toàn bộ lịch sử sửa + ai / lúc nào / lý do
--   TRƯỚC khi xoá (cùng giao dịch). KHÔNG khoá ngoại vào KhPlanDetails (dòng PO đã xoá). Chỉ thêm, không sửa / xoá.
--   Không đổi dữ liệu cũ. Chạy lại nhiều lần không sao. SQL Server 2016.
--   Quay lại (chỉ khi code mới chưa chạy): DROP TABLE dbo.KhPlanDeletedDetails;
-- =====================================================================================================
USE [MES];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.KhPlanDeletedDetails', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.KhPlanDeletedDetails
    (
        DeletedId       bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_KhPlanDeletedDetails PRIMARY KEY CLUSTERED,
        KhPlanDetailId  int NOT NULL,              -- Id PO đã xoá (không khoá ngoại)
        KhPlanId        int NULL,
        PlanNo          nvarchar(50) NULL,
        PurchaseOrder   nvarchar(100) NULL,
        PartNo          nvarchar(100) NULL,
        DetailJson      nvarchar(max) NOT NULL,    -- dòng KhPlanDetails lúc xoá
        ChangeLogsJson  nvarchar(max) NULL,        -- mọi dòng KhPlanDetailChangeLogs của PO
        Reason          nvarchar(500) NOT NULL,
        DeletedBy       int NOT NULL,
        DeletedAt       datetime2 NOT NULL CONSTRAINT DF_KhPlanDeletedDetails_DeletedAt DEFAULT (SYSDATETIME())
    );
    CREATE INDEX IX_KhPlanDeletedDetails_Detail ON dbo.KhPlanDeletedDetails (KhPlanDetailId);
    CREATE INDEX IX_KhPlanDeletedDetails_PO ON dbo.KhPlanDeletedDetails (PurchaseOrder);
END
GO

SELECT N'KhPlanDeletedDetails' AS Muc, CASE WHEN OBJECT_ID(N'dbo.KhPlanDeletedDetails', N'U') IS NULL THEN N'CHƯA' ELSE N'CÓ' END AS TrangThai;
GO
