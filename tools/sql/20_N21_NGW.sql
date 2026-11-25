-- =====================================================================================================
-- [N21-NGW] 2026-11-25 — BẢN GỬI HÙNG DUYỆT (CHƯA CHẠY). Gói G-D, luật N21 (khách chốt mockup TH10 23/11; Hùng 25/11 16:27 "ok tự làm đi").
--   Vì sao: WTS có NG không còn tự sinh mã vụ — NG nằm ở "NG ở WTS chưa báo lỗi"; báo lỗi riêng ở form Giao / xử lý hàng lưu
--   phiếu báo lỗi (HandoverNgReports) với nguồn mới SourceCell = 'NGW' (lấy từ NG ở WTS chưa báo lỗi; báo dư thì phần mềm trừ
--   vào hàng đã làm xong chưa giao). DB đang có 2 ràng buộc CHECK chỉ cho 'DA' / 'CHUA' / 'PHOI' (quét chỉ đọc 25/11 16:30 [QUET-N21-CK])
--   → nới 2 ràng buộc cho thêm 'NGW' (chỉ nhóm sản xuất, Kho vẫn chỉ 'PHOI'). Không đổi bảng, không đổi dữ liệu cũ.
--   Chạy TRƯỚC khi chạy bản code G-D (bản code cũ không ghi 'NGW' nên chạy trước vẫn an toàn). Chạy lại nhiều lần không sao. SQL Server 2016.
--   Quay lại (chỉ khi chưa có phiếu 'NGW'): chạy lại 2 ALTER ... ADD CONSTRAINT dưới với danh sách cũ (bỏ N'NGW').
-- =====================================================================================================
USE [MES];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;   -- lỗi giữa chừng → tự ROLLBACK cả 2 ràng buộc
GO

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.CK_HandoverNgReports_Source', N'C') IS NOT NULL
    ALTER TABLE dbo.HandoverNgReports DROP CONSTRAINT CK_HandoverNgReports_Source;
ALTER TABLE dbo.HandoverNgReports WITH CHECK ADD CONSTRAINT CK_HandoverNgReports_Source
    CHECK ([SourceCell] = N'DA' OR [SourceCell] = N'CHUA' OR [SourceCell] = N'PHOI' OR [SourceCell] = N'NGW');

IF OBJECT_ID(N'dbo.CK_HandoverNgReports_KhoPhoi', N'C') IS NOT NULL
    ALTER TABLE dbo.HandoverNgReports DROP CONSTRAINT CK_HandoverNgReports_KhoPhoi;
ALTER TABLE dbo.HandoverNgReports WITH CHECK ADD CONSTRAINT CK_HandoverNgReports_KhoPhoi
    CHECK (([GroupCode] = N'KHO' AND [SourceCell] = N'PHOI')
        OR ([GroupCode] <> N'KHO' AND ([SourceCell] = N'DA' OR [SourceCell] = N'CHUA' OR [SourceCell] = N'NGW')));

COMMIT TRANSACTION;
GO

SELECT name AS RangBuoc, definition AS DieuKien
FROM sys.check_constraints
WHERE parent_object_id = OBJECT_ID(N'dbo.HandoverNgReports');
GO
