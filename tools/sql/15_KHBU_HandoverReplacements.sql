-- =====================================================================================================
-- [KHACH-GUI-LAI] 2026-11-11 — HÙNG 11/11 14:39 DUYỆT (gửi file). Gói 14 — khách 10/11 vòng 1 câu 1a A + 1b A.
--   Vì sao: chưa có chứng từ "Khách gửi bù" — cách tạm là sửa Số lượng thực nhận (tăng số gốc, không biết bù cho lần lỗi nào).
--   Bảng mới: mỗi lần khách gửi bù = 1 dòng, gắn đúng 1 lần lỗi cũ (NgCaseId) có Trả khách / Huỷ.
--   Phần mềm (code Gói 14) kiểm: số bù ≤ (trả khách + huỷ của lần lỗi đó) − số đã bù (1b A: chặn, dư thì sửa Số lượng thực nhận) — chặn cả khi PO tắt Workflow (Hùng 11/11 14:39).
--   Kho nhận vào ô Hàng lỗi đã xử lý = phôi còn của Kho (1a A), rồi giao Gia công vào NC đầu như phôi thường.
--   Không sửa / xoá dòng: huỷ = IsVoided + người / giờ / lý do (K5 kiểm trước khi huỷ).
--   Chạy lại nhiều lần không sao (có kiểm tồn tại). SQL Server 2016. Không đụng bảng / dữ liệu cũ.
--   Quay lại (chỉ khi code mới chưa chạy): DROP TABLE dbo.HandoverReplacements;
-- =====================================================================================================
USE [MES];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.HandoverReplacements', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HandoverReplacements
    (
        ReplacementId   bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_HandoverReplacements PRIMARY KEY CLUSTERED,
        KhPlanDetailId  int    NOT NULL CONSTRAINT FK_HandoverReplacements_KhPlanDetails
                            FOREIGN KEY REFERENCES dbo.KhPlanDetails (KhPlanDetailId),   -- NO ACTION (như các bảng con khác của PO)
        NgCaseId        bigint NOT NULL CONSTRAINT FK_HandoverReplacements_NgCases
                            FOREIGN KEY REFERENCES dbo.NgCases (NgCaseId),               -- lần lỗi được bù (đã Trả khách / Huỷ)
        Qty             decimal(10,2) NOT NULL CONSTRAINT CK_HandoverReplacements_Qty CHECK (Qty > 0),
        ReceivedBy      int NOT NULL,                       -- người Kho bấm nhận phôi bù
        ReceivedAt      datetime2 NOT NULL,                 -- giờ máy chủ lúc lưu
        Notes           nvarchar(500) NULL,                 -- số phiếu / ghi chú của khách
        IsVoided        bit NOT NULL CONSTRAINT DF_HandoverReplacements_IsVoided DEFAULT (0),
        VoidedBy        int NULL,
        VoidedAt        datetime2 NULL,
        VoidReason      nvarchar(200) NULL,
        CreatedAt       datetime2 NOT NULL CONSTRAINT DF_HandoverReplacements_CreatedAt DEFAULT (SYSDATETIME())
    );
    CREATE INDEX IX_HandoverReplacements_Detail ON dbo.HandoverReplacements (KhPlanDetailId, ReceivedAt);
    CREATE INDEX IX_HandoverReplacements_Case   ON dbo.HandoverReplacements (NgCaseId);
END
GO

-- Kiểm: bảng có, 0 dòng
SELECT N'HandoverReplacements' AS Bang, COUNT(*) AS SoDong FROM dbo.HandoverReplacements;
GO
