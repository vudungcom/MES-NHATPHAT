﻿-- =====================================================================================================
-- [BM1-01] 2026-11-16 — Gói 24 vá DỮ LIỆU CŨ sổ sự cố máy (ThietBiChangeLogs). CHỜ HÙNG DUYỆT. Chạy 2 bước: (1) SELECT xem trước, gửi Claude số
--   dòng; (2) Hùng OK → mở khối UPDATE (XOÁ CẢ DÒNG mở chú thích "(2) CHẠY SAU KHI…" và CẢ DÒNG đóng chú thích ngay trước GO cuối file) rồi chạy. Chạy lúc nào cũng được (không cần tắt app).
--   Vì sao: trước Gói 24, máy ĐANG dừng mà bấm Sửa (đổi Báo hỏng → Bảo trì, hay chỉ sửa tên) tạo thêm dòng sự cố MỞ (ThoiGianKetThuc NULL);
--   về Hoạt động chỉ chốt dòng MỚI NHẤT → dòng cũ mở vĩnh viễn → trang Hiệu suất máy tô "Hỏng 100 %" mọi ngày sau cho máy đó.
--   Code Gói 24 không sinh thêm dòng mở trùng và về Hoạt động chốt mọi dòng mở; script này đóng các dòng mở CÒN SÓT của máy đang "Hoạt động":
--   giờ kết thúc = giờ của dòng log "Trạng thái → Hoạt động" ĐẦU TIÊN SAU dòng mở đó (đúng lúc máy chạy lại; rà độc lập 16/11 — UpdatedAt của máy
--   là lần sửa cuối, có thể sau lúc về Hoạt động → phút dừng phồng); không có dòng đó mới lấy UpdatedAt; không sớm hơn giờ bắt đầu dừng.
--   Máy đang dừng (Báo hỏng / Bảo trì / Tạm dừng) KHÔNG đụng (dòng mở của nó là đúng). Có bảng sao lưu ThietBiChangeLogs_bak_1116 trước khi UPDATE. SQL Server 2016.
--   Quay lại: UPDATE l SET ThoiGianKetThuc = NULL, SoPhutDung = NULL FROM dbo.ThietBiChangeLogs l JOIN dbo.ThietBiChangeLogs_bak_1116 b ON b.ChangeId = l.ChangeId;
-- =====================================================================================================
USE [MES];
GO
SET QUOTED_IDENTIFIER ON;
GO
-- (1) XEM TRƯỚC: dòng sự cố mở của máy đang Hoạt động (sẽ bị đóng) + giờ kết thúc sẽ ghi
SELECT l.ChangeId, t.SoMay, t.TrangThai, l.FieldName, l.OldValue, l.NewValue, l.Reason, l.ThoiGianBatDau, l.ChangedAt,
       v.ChangedAt AS GioVeHoatDong_TheoLog, t.UpdatedAt AS GioSuaCuoi_May,
       ISNULL(v.ChangedAt, ISNULL(t.UpdatedAt, l.ChangedAt)) AS GioKetThuc_SeGhi
FROM dbo.ThietBiChangeLogs l
JOIN dbo.ThietBis t ON t.ThietBiId = l.ThietBiId
OUTER APPLY (SELECT TOP 1 x.ChangedAt FROM dbo.ThietBiChangeLogs x
             WHERE x.ThietBiId = l.ThietBiId AND x.FieldName = N'Trạng thái' AND x.NewValue = N'Hoạt động' AND x.ChangedAt > l.ChangedAt
             ORDER BY x.ChangedAt) v
WHERE l.ThoiGianBatDau IS NOT NULL AND l.ThoiGianKetThuc IS NULL
  AND t.TrangThai = N'Hoạt động'
ORDER BY t.SoMay, l.ChangedAt;
-- Tham khảo: máy đang dừng mà có > 1 dòng mở (code mới sẽ chốt hết khi về Hoạt động — không cần vá)
SELECT t.SoMay, t.TrangThai, COUNT(*) AS SoDongMo
FROM dbo.ThietBiChangeLogs l JOIN dbo.ThietBis t ON t.ThietBiId = l.ThietBiId
WHERE l.ThoiGianBatDau IS NOT NULL AND l.ThoiGianKetThuc IS NULL AND t.TrangThai <> N'Hoạt động'
GROUP BY t.SoMay, t.TrangThai HAVING COUNT(*) > 1;
GO
/* (2) CHẠY SAU KHI HÙNG OK — xoá CẢ dòng này và CẢ dòng đóng chú thích (dấu sao + gạch chéo) ngay trước GO cuối file
IF OBJECT_ID('dbo.ThietBiChangeLogs_bak_1116') IS NULL
    SELECT l.* INTO dbo.ThietBiChangeLogs_bak_1116
    FROM dbo.ThietBiChangeLogs l JOIN dbo.ThietBis t ON t.ThietBiId = l.ThietBiId
    WHERE l.ThoiGianBatDau IS NOT NULL AND l.ThoiGianKetThuc IS NULL AND t.TrangThai = N'Hoạt động';
BEGIN TRAN;
UPDATE l
SET ThoiGianKetThuc = CASE WHEN k.GioKetThuc < l.ThoiGianBatDau THEN l.ThoiGianBatDau ELSE k.GioKetThuc END,
    SoPhutDung      = CASE WHEN k.GioKetThuc < l.ThoiGianBatDau THEN 0 ELSE DATEDIFF(MINUTE, l.ThoiGianBatDau, k.GioKetThuc) END
FROM dbo.ThietBiChangeLogs l
JOIN dbo.ThietBis t ON t.ThietBiId = l.ThietBiId
OUTER APPLY (SELECT TOP 1 x.ChangedAt FROM dbo.ThietBiChangeLogs x
             WHERE x.ThietBiId = l.ThietBiId AND x.FieldName = N'Trạng thái' AND x.NewValue = N'Hoạt động' AND x.ChangedAt > l.ChangedAt
             ORDER BY x.ChangedAt) v
CROSS APPLY (SELECT ISNULL(v.ChangedAt, ISNULL(t.UpdatedAt, l.ChangedAt)) AS GioKetThuc) k
WHERE l.ThoiGianBatDau IS NOT NULL AND l.ThoiGianKetThuc IS NULL AND t.TrangThai = N'Hoạt động';
PRINT CONCAT('17_THIETBI_LOG_MO: đã đóng ', @@ROWCOUNT, ' dòng');
COMMIT;
-- Kiểm lại: phải 0 dòng
SELECT COUNT(*) AS ConMo FROM dbo.ThietBiChangeLogs l JOIN dbo.ThietBis t ON t.ThietBiId = l.ThietBiId
WHERE l.ThoiGianBatDau IS NOT NULL AND l.ThoiGianKetThuc IS NULL AND t.TrangThai = N'Hoạt động';
*/
GO
