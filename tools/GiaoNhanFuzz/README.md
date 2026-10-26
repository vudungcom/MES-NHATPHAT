# GiaoNhanFuzz — kiểm in/out ngẫu nhiên Giao nhận + WTS

Không thuộc web (nằm ngoài thư mục MES.Web nên không bị biên dịch chung). Không cần NuGet: EF Core được thay bằng stub trong bộ nhớ (`EfStub.cs`, `AppDbContextStub.cs`), gọi thẳng `HandoverService` / `HandoverLedgerService` thật trong `../../MES.Web`.

Chạy (từ thư mục này):

```
dotnet build -o bin2
dotnet bin2/GiaoNhanFuzz.dll <số PO> <thao tác mỗi PO> <seed> [PO bắt đầu]
# ví dụ: dotnet bin2/GiaoNhanFuzz.dll 150 150 31
# EXCL=0.7 → 70% PO có NC máy loại trừ; DUMP=1 → in sổ chi tiết khi gặp lỗi
```

Sau mỗi thao tác thành công kiểm: cân PO, không ô âm (gồm từng NC GC + từng mã CV), WTS ≤ hàng thật, Σ chờ NC = Chưa GC, dòng B cuối = sổ, trạng thái phiếu, phiếu tự lập, quyền huỷ. Kết quả "SAI: 0" là đạt.

Thêm (GN-WHY):

```
dotnet bin2/GiaoNhanFuzz.dll scen    # 3 tình huống xung đột, in lời báo thật kèm gợi ý gỡ
dotnet bin2/GiaoNhanFuzz.dll bench   # đo thời gian tính sổ + tìm gốc rễ trên 1 PO lớn
```

Mỗi lần huỷ / xoá WTS bị chặn, bộ kiểm còn: tìm gợi ý gỡ, kiểm lời báo có gợi ý, làm đúng theo gợi ý qua hàm thật rồi thử lại thao tác gốc — phải lưu được.
