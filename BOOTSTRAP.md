# BOOTSTRAP — MES Nhật Phát

Đây là dự án MES cho công ty cơ khí Nhật Phát. Hùng là chủ dự án.

## Việc đầu tiên mỗi session

1. Fetch `CLAUDE-RULES.md` từ GitHub — đây là rules đầy đủ, đọc trước khi làm bất cứ gì
2. Fetch `file-tree.txt` để xem cấu trúc project
3. Tự fetch file liên quan đến task — **không cần user upload**

```
Base URL: https://raw.githubusercontent.com/vudungcom/MES-NHATPHAT/master/
```

Ví dụ:
```bash
curl -s "https://raw.githubusercontent.com/vudungcom/MES-NHATPHAT/master/CLAUDE-RULES.md"
curl -s "https://raw.githubusercontent.com/vudungcom/MES-NHATPHAT/master/file-tree.txt"
```

## Tóm tắt cực ngắn (chi tiết trong CLAUDE-RULES.md trên GitHub)

- Stack: Blazor Server .NET 8, EF Core, SQL Server (DB: `MES`)
- Mỗi trang mới: fetch `Index_PartMaster.razor` + `Detail_Partmaster.razor` làm chuẩn UI
- Razor: không lambda phức tạp trong markup — tách thành private method
- File Razor: CRLF line ending (không phải LF)
- Logs: append-only, không UPDATE/DELETE
- Migration SQL: bắt đầu bằng `USE [MES]; GO`
- Ship file hoàn chỉnh để user overwrite — không ship diff/patch

## Khi user báo lỗi build

Hỏi ngay: đã chạy migration SQL chưa? Đã restart app chưa? Có file CRLF chưa?
