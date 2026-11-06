-- =====================================================================================================
-- [K5-CLOSE-NHIEU] 2026-11-06 — BẢN GỬI HÙNG DUYỆT (CHƯA CHẠY). Hùng 05/11 19:33 chốt A.
--   Vì sao: phiếu giao chỉ có 1 bộ QtyClosed / ClosedAt / ClosedBy / ClosedReason cho MỌI lần "Huỷ phần chưa nhận" → lần 2 ghi đè
--   giờ + người lần 1 (mất truy vết từng lần) và sổ coi số huỷ lần 1 là còn giao ở các mốc cũ → chặn oan theo thời gian (fuzz seed 602).
--   Bảng mới: mỗi lần huỷ phần chưa nhận = 1 dòng (phiếu, số, người, giờ, lý do). Chỉ thêm (append-only), không sửa / xoá.
--   Dữ liệu cũ: mỗi phiếu đang có QtyClosed > 0 chuyển thành 1 dòng (IsMigrated = 1, giờ / người / lý do = bộ cột cũ). Bộ cột cũ GIỮ NGUYÊN
--   (code mới vẫn ghi tổng vào đó cho trang cũ / báo cáo; sổ đọc từng dòng của bảng mới).
--   Chạy lại nhiều lần không sao (có kiểm tồn tại; dòng cũ chỉ chuyển 1 lần). SQL Server 2016.
--   Quay lại (chỉ khi code mới chưa chạy): DROP TABLE dbo.HandoverTxCloses;
-- =====================================================================================================
USE [MES];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.HandoverTxCloses', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HandoverTxCloses
    (
        CloseId       bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_HandoverTxCloses PRIMARY KEY CLUSTERED,
        HandoverTxId  bigint NOT NULL CONSTRAINT FK_HandoverTxCloses_HandoverTransactions
                          FOREIGN KEY REFERENCES dbo.HandoverTransactions (HandoverTxId),   -- NO ACTION (như các bảng con khác)
        Qty           decimal(10,2) NOT NULL CONSTRAINT CK_HandoverTxCloses_Qty CHECK (Qty > 0),
        ClosedBy      int NOT NULL,
        ClosedAt      datetime2 NOT NULL,
        Reason        nvarchar(200) NULL,
        IsMigrated    bit NOT NULL CONSTRAINT DF_HandoverTxCloses_IsMigrated DEFAULT (0),   -- 1 = chuyển từ bộ cột cũ (giờ = lần huỷ CUỐI)
        CreatedAt     datetime2 NOT NULL CONSTRAINT DF_HandoverTxCloses_CreatedAt DEFAULT (SYSDATETIME())
    );
    CREATE INDEX IX_HandoverTxCloses_Tx ON dbo.HandoverTxCloses (HandoverTxId, ClosedAt);
END
GO

-- Chuyển dữ liệu cũ: 1 dòng / phiếu (không biết từng lần cũ — bộ cột cũ chỉ giữ tổng + lần cuối)
INSERT INTO dbo.HandoverTxCloses (HandoverTxId, Qty, ClosedBy, ClosedAt, Reason, IsMigrated)
SELECT t.HandoverTxId, t.QtyClosed, ISNULL(t.ClosedBy, t.IssuedBy), ISNULL(t.ClosedAt, t.IssuedAt), t.ClosedReason, 1
FROM dbo.HandoverTransactions t
WHERE t.QtyClosed > 0
  AND NOT EXISTS (SELECT 1 FROM dbo.HandoverTxCloses c WHERE c.HandoverTxId = t.HandoverTxId);
GO

-- Kiểm: tổng bảng mới = tổng cột cũ (phải bằng nhau; khác → báo Claude, chưa chạy code mới)
SELECT N'Phiếu có huỷ phần chưa nhận' AS Muc, COUNT(*) AS SoPhieu, SUM(t.QtyClosed) AS TongCu,
       (SELECT SUM(c.Qty) FROM dbo.HandoverTxCloses c) AS TongMoi
FROM dbo.HandoverTransactions t WHERE t.QtyClosed > 0;
GO
