# SaoLuuThu — thử sao lưu THẬT trên SQL Server ([G32-SAOLUU] Gói 32a, 22/11/2026)

Dùng đúng `SaoLuuService` / `SaoLuuSql` của MES.Web (cùng code app chạy), DB lấy từ `..\..\MES.Web\appsettings.json`.

- `duong-dan.txt`: dòng 1 = thư mục sao lưu nhìn từ máy SQL Server (vd `D:\MES-Backup`), dòng 2 = cùng thư mục nhìn từ máy chạy tool
  (vd `\\192.168.1.211\MES-Backup`; trống = giống dòng 1). Tool lưu vào `<thư mục>\_thu` — KHÔNG đụng chỗ app thật lưu.
- Trạm (`tools\claude_tram.bat`):
  - `saoluu`  : 1 bản nhanh + 1 trọn bộ → kiểm manifest, mã băm từng file, `RESTORE FILELISTONLY` + `RESTORE VERIFYONLY WITH CHECKSUM`
                (chỉ đọc file sao lưu); in thời gian + dung lượng từng phần.
  - `saoluukp`: như trên + khôi phục thử vào DB tạm `MES_SAOLUU_THU` (file vào thư mục dữ liệu mặc định của SQL Server), so số dòng
                từng bảng với manifest, chạy QuetSo (quét sổ cân, chỉ đọc) trên DB tạm, rồi XOÁ DB tạm + file .bak giải nén.
- SQL ghi: `BACKUP DATABASE … WITH COPY_ONLY` (không đổi chuỗi sao lưu khác, không sửa dữ liệu) và `RESTORE` / `DROP` DB tên đúng
  `MES_SAOLUU_THU` (hằng số trong code) — HÙNG duyệt trước khi chạy.
- Kết quả: `ketqua_saoluu_<giờ>.txt` (gửi Claude). Mã thoát 0 = đạt hết.
