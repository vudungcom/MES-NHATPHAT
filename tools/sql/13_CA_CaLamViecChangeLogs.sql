-- =====================================================================================================
-- [CA-LOG] 2026-11-06 — BẢN GỬI HÙNG DUYỆT (CHƯA CHẠY). Hùng 05/11 15:37 chốt (B5).
--   Vì sao: sửa / ẩn / hiện ca làm việc (giờ ca, giờ nghỉ — ảnh hưởng phút WTS, hiệu suất) không có lịch sử: không biết ai đổi, lúc nào,
--   trước / sau ra sao. Bảng mới: mỗi lần lưu 1 dòng, JSON trước / sau (kể cả giờ nghỉ), ai, lúc nào. Chỉ thêm, không sửa / xoá.
--   Không khoá ngoại vào CaLamViecs (giữ log cả khi ca bị xoá hẳn lúc chưa dùng). Không đổi dữ liệu cũ. Chạy lại nhiều lần không sao.
--   Quay lại (chỉ khi code mới chưa chạy): DROP TABLE dbo.CaLamViecChangeLogs;
-- =====================================================================================================
USE [MES];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.CaLamViecChangeLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CaLamViecChangeLogs
    (
        LogId       bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CaLamViecChangeLogs PRIMARY KEY CLUSTERED,
        CaId        int NOT NULL,
        Action      nvarchar(20) NOT NULL,        -- TAO / SUA / AN / HIEN / XOA
        BeforeJson  nvarchar(max) NULL,           -- ca + giờ nghỉ trước khi lưu (NULL khi tạo)
        AfterJson   nvarchar(max) NULL,           -- sau khi lưu (NULL khi xoá)
        ChangedBy   int NOT NULL,
        ChangedAt   datetime2 NOT NULL CONSTRAINT DF_CaLamViecChangeLogs_ChangedAt DEFAULT (SYSDATETIME())
    );
    CREATE INDEX IX_CaLamViecChangeLogs_Ca ON dbo.CaLamViecChangeLogs (CaId, ChangedAt);
END
GO

SELECT N'CaLamViecChangeLogs' AS Muc, CASE WHEN OBJECT_ID(N'dbo.CaLamViecChangeLogs', N'U') IS NULL THEN N'CHƯA' ELSE N'CÓ' END AS TrangThai;
GO
