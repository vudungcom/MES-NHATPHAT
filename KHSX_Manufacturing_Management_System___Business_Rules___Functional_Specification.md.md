Manufacturing Management System
Business Rules & Functional Specification

Version: 0.2
Status: Draft --- dùng làm cơ sở thống nhất yêu cầu trước khi code

1. Mục tiêu hệ thống

Hệ thống dùng để quản lý và theo dõi toàn bộ quá trình sản xuất, thay thế việc ghi chép bằng giấy/Excel.

Hệ thống phải quản lý được:

Kế hoạch sản xuất.
Order/PO.
Part.
Job.
Route/Process/Step.
Máy sản xuất.
Worker.
Số lượng.
Thời gian sản xuất.
Downtime.
NG.
Rework.
Transport/Lead Time.
Cycle Time.
Trạng thái hiện tại.
Lịch sử toàn bộ vòng đời.

Mục tiêu quan trọng nhất không chỉ là biết "hiện tại Job đang ở đâu", mà phải có khả năng truy ngược:

Part/Job này đã đi qua những đâu, ai làm, lúc nào, ở máy nào, số lượng bao nhiêu, tại sao phát sinh vấn đề và đã xử lý như thế nào.

2. Nguyên tắc kiến trúc nghiệp vụ
BR-001 --- Database là nguồn dữ liệu lịch sử chính

Giao diện có thể đơn giản, nhưng database phải lưu đủ dữ liệu để truy xuất toàn bộ vòng đời của:

Part
Order
Job
Process
Production Transaction
Rework
NG
Transfer
Machine activity
Worker activity

Không được thiết kế database chỉ để phục vụ việc hiển thị trạng thái hiện tại.

3. Traceability --- truy xuất toàn bộ vòng đời
BR-002 --- Áp dụng nguyên tắc 5W1H

Các transaction quan trọng phải có khả năng trả lời:

What --- Chuyện gì xảy ra?
Who --- Ai thực hiện?
When --- Khi nào?
Where --- Ở đâu/máy nào/công đoạn nào?
Why --- Vì sao?
How --- Bằng phương thức nào?

Ví dụ một transaction Finish phải có thể truy được:

text
What:
Finish Production

Who:
Worker A

When:
2026-08-13 10:15

Where:
CNC-05

Why:
Production completed

How:
Barcode scan + Worker confirmation
4. Part
BR-003 --- Part Code không thay đổi

Part Code là định danh xuyên suốt vòng đời Part.

Part Code phải được giữ nguyên khi Part đi qua các Process.

Không tạo Part Code mới chỉ vì Part chuyển công đoạn.

5. Order / PO / Planning
BR-004 --- Phân biệt kế hoạch và thực tế

Planning Quantity là số lượng theo kế hoạch/Order.

Số lượng phát sinh thực tế không được tự động làm thay đổi Planning Quantity.

Ví dụ:

text
PO-001
Planning Quantity = 10 EA

Trong sản xuất:

text
10 EA nhận vào
1 EA NG

Khách hàng cấp thêm 1 phôi để bù:

text
PO-001-BL
Quantity = 1 EA
Type = Bù lỗi
Parent PO = PO-001

Hệ thống phải giữ:

text
PO-001       = 10 EA kế hoạch
PO-001-BL    = 1 EA bổ sung

Không biến Planning Quantity thành 11 EA.

Quan hệ phải truy được:

text
PO-001-BL
   ↓
Bù cho PO-001
   ↓
Nguyên nhân: NG
   ↓
1 EA
   ↓
Rework / Production
   ↓
Hoàn thành
BR-005 --- Order bổ sung phải giữ quan hệ với Order gốc

Order/PO phát sinh để bù lỗi phải có:

Order/PO riêng theo chứng từ thực tế.
Loại Order.
Parent Order/PO.
Quantity.
Reason.

Mục đích là không phá dữ liệu chứng từ thực tế của khách hàng nhưng vẫn truy xuất được nguồn gốc.

6. Job
BR-006 --- Job là đối tượng công việc thực tế

Job đại diện cho công việc sản xuất cần thực hiện.

Job phải liên kết được với:

Order/PO.
Part.
Quantity.
Route.
Process hiện tại.
Status.
History.
7. Route / Process / Step
BR-007 --- Job phải đi theo Route

Mỗi Job có Route/Process sequence xác định.

Ví dụ:

text
Process 10
   ↓
Process 20
   ↓
Process 30
   ↓
Finished

Process hiện tại phải được xác định dựa trên dữ liệu hệ thống, không chỉ dựa vào lựa chọn của Worker.

BR-008 --- Part không được tự ý nhảy Process

Worker không được tự ý chuyển Job sang Process không hợp lệ.

Việc chuyển Process phải tuân theo Route.

8. Current Status
BR-009 --- Trạng thái hiện tại không phải dữ liệu duy nhất

Trạng thái hiện tại của Part/Job phải được xác định dựa trên tổng hợp:

text
Current Process
+
Status
+
Quantity
+
Route / Step
+
Transaction History

Không được phụ thuộc hoàn toàn vào một cột Status.

Ví dụ:

text
Status = Finished

không đủ để hiểu Job đã hoàn thành toàn bộ hay chỉ hoàn thành một Process.

9. Production Transaction
BR-010 --- Các hành động sản xuất phải tạo transaction

Các hành động quan trọng phải được lưu thành transaction/history.

Ví dụ:

Start.
Pause.
Resume.
Finish.
NG.
Rework.
Transfer.
Process completion.
Machine stop.
Machine restart.

Không được chỉ sửa giá trị hiện tại rồi xóa dữ liệu cũ.

10. Start
BR-011 --- Start phải ghi nhận đầy đủ thông tin

Khi Worker Start Job, hệ thống phải lưu tối thiểu:

Job.
Part.
Process.
Worker.
Machine.
Start Time.
Quantity liên quan.
Transaction type.
User/action information.
11. Finish
BR-012 --- Finish phải tạo lịch sử

Khi Finish:

Ghi Finish Time.
Ghi Worker.
Ghi Machine.
Ghi Process.
Ghi Quantity.
Ghi OK/NG nếu có.
Tạo History/Transaction.
Cập nhật trạng thái hiện tại.
Xác định Process tiếp theo theo Route nếu đủ điều kiện.
12. Quantity
BR-013 --- Quantity phải được quản lý theo transaction

Không chỉ lưu một Quantity cuối cùng.

Phải có khả năng truy được:

text
Planned Quantity
Received Quantity
Processed Quantity
OK Quantity
NG Quantity
Rework Quantity
Remaining Quantity

Các số liệu phải có thể đối chiếu với transaction history.

13. NG
BR-014 --- NG không được làm mất lịch sử sản xuất

Khi phát sinh NG:

Ghi nhận số lượng NG.
Ghi nguyên nhân NG.
Ghi Worker.
Ghi Machine.
Ghi Process.
Ghi thời gian.
Liên kết với Job/Part/Order tương ứng.

Không sửa/xóa transaction sản xuất ban đầu để biến nó thành dữ liệu "đúng".

14. Rework
BR-015 --- Rework phải bảo toàn lịch sử

Rework là một sự kiện mới trong vòng đời, không phải sửa lại transaction cũ.

Ví dụ:

text
Process 20
    ↓
10 EA processed
    ↓
8 OK
2 NG
    ↓
2 EA Rework
    ↓
2 EA Rework completed

Database phải giữ được toàn bộ chuỗi này.

BR-016 --- Rework không được làm mất Process History

Phải truy được:

text
Original Production
       ↓
NG
       ↓
Reason
       ↓
Rework
       ↓
Rework Result
15. Transport Time / Lead Time
BR-017 --- Transport Time khác Cycle Time

Transport/Lead Time là thời gian Part đi từ công đoạn này sang công đoạn tiếp theo.

Ví dụ:

text
Process 1 Finish = 10:00
Process 2 Input  = 10:15

Transport Time = 15 minutes

Khoảng thời gian này không được tính nhầm thành Manufacturing Cycle Time.

16. Manufacturing Cycle Time
BR-018 --- Cycle Time phục vụ tính Throughput

Manufacturing Cycle Time là khoảng thời gian giữa các sản phẩm liên tiếp hoàn thành/ra khỏi hệ thống hoặc công đoạn, tùy định nghĩa KPI.

Cycle Time dùng để đánh giá:

Throughput.
Năng lực sản xuất.
Productivity.
Machine performance.

Không dùng Cycle Time để thay thế Transport/Lead Time.

17. Công việc qua ngày
BR-019 --- Job đang mở phải được tiếp tục qua ngày

Nếu Worker Start Job hôm nay nhưng chưa Finish:

text
Day 1
Start
↓
Job Open

Ngày hôm sau:

text
Day 2
Resume / Continue
↓
Finish

Không được tự động coi Job là hoàn thành hoặc reset dữ liệu chỉ vì sang ngày mới.

Lịch sử phải thể hiện được công việc kéo dài qua nhiều ngày.

18. Thời gian
BR-020 --- Thời gian phải có timestamp rõ ràng

Các transaction quan trọng phải có timestamp.

Hệ thống sử dụng GMT+7 cho nghiệp vụ hiển thị/thực tế tại nhà máy.

BR-021 --- Không cho Worker tùy ý sửa thời gian lịch sử

Worker không được tự ý sửa Start/Finish Time về thời điểm quá khứ để làm sai dữ liệu.

Các timestamp quan trọng phải do hệ thống tạo hoặc được kiểm soát bởi quyền phù hợp.

19. Worker

Hệ thống phải quản lý:

Worker ID.
Worker Name.
Role.
Permission.
Timesheet.
Production activity.
Machine activity nếu có.

Mỗi Production Transaction phải truy được Worker thực hiện.

20. Machine

Hệ thống phải quản lý:

Machine ID.
Machine Name.
Machine status.
Process/Capability.
Production activity.
Downtime.

Khi máy dừng phải có khả năng ghi nhận:

text
Machine
Start Downtime
End Downtime
Reason
Worker/User
21. Machine Downtime
BR-022 --- Downtime phải có nguyên nhân

Không chỉ ghi:

text
Machine = STOP

mà phải có:

text
Start
End
Duration
Reason
Machine
User/Worker

Có thể phân loại nguyên nhân theo Master Data.

Mục đích là tính:

Downtime.
Machine utilization.
Useful/Unuseful time.
Production loss.
22. Barcode
BR-023 --- Barcode dùng để giảm nhập liệu thủ công

Part/Job có thể được định danh bằng barcode.

Worker có thể:

text
Scan Barcode
     ↓
System identifies Part / Job
     ↓
System identifies current Process
     ↓
Worker performs action

Không bắt Worker nhập lại những dữ liệu hệ thống đã biết nếu không cần thiết.

Barcode có thể được đọc bằng:

Camera điện thoại.
Barcode scanner.
Thiết bị USB/Bluetooth phù hợp.
23. Quy trình nhập liệu Worker

Luồng cơ bản:

text
Login
  ↓
Scan Part / Job
  ↓
System xác định Job
  ↓
System xác định Current Process
  ↓
Worker kiểm tra thông tin
  ↓
START
  ↓
Production
  ↓
PAUSE / NG / REWORK nếu cần
  ↓
FINISH
  ↓
System ghi Transaction
  ↓
Update Current Status
  ↓
Next Process
24. User / Permission

Hệ thống phải có login và phân quyền.

Các nhóm quyền dự kiến có thể bao gồm:

Admin.
Manager.
Planner.
Worker.
QC.
Maintenance.
Viewer.

Quyền phải được kiểm tra ở Backend, không chỉ ẩn nút trên giao diện.

25. Audit / History
BR-024 --- Transaction quan trọng không được hard delete

Dữ liệu lịch sử sản xuất không được xóa tùy tiện.

Nếu cần sửa sai:

Tạo correction/adjustment transaction phù hợp.
Ghi người sửa.
Ghi thời gian.
Ghi lý do.
Giữ dữ liệu gốc.

Mục tiêu là vẫn truy được lịch sử thực tế.

26. Kho / Material
BR-025 --- Planning có thể được chuẩn bị trước khi vật liệu về

Planning và Route có thể được chuẩn bị trước khi phôi/material về kho.

Tuy nhiên Job phải thể hiện rõ tình trạng vật liệu:

text
WAITING MATERIAL

hoặc:

text
MATERIAL AVAILABLE

Không được coi Job có thể sản xuất chỉ vì Planning đã tồn tại.

BR-026 --- Dashboard phải thể hiện tình trạng phôi

Theo Order/Part cần có khả năng xem:

Total material demand.
Total material received.
Total material available.
Total material NG.
Total replacement/supplement material.
Remaining material requirement.

Nếu thiếu phôi, hệ thống phải cảnh báo.

BR-027 --- Phôi NG và phôi bổ sung phải truy được

Ví dụ:

text
Demand = 10
Original material received = 10
NG = 1
Supplement received = 1

Hệ thống phải hiểu:

text
Target OK = 10
Total material transactions = 11

Không được hiểu rằng Target/Order Quantity đã tăng thành 11.

27. Nhập kho
BR-028 --- Nhập kho phải ghi nhận tình trạng phôi

Khi nhập phôi/material cần ghi nhận:

Part No.
Part Name.
Purchase Order.
Manufacturing Order.
Quantity.
Material.
Weight nếu áp dụng.
Customer.
Condition/status.
User nhập kho.
Time.
Ghi chú.

Phôi có thể được phân loại:

text
OK
NG

Phôi NG phải được lưu lịch sử, kể cả khi thực tế trả lại Customer.

Không được xóa khỏi hệ thống.

BR-029 --- Phôi trả Customer vẫn phải giữ traceability

Nếu phôi NG được trả lại Customer:

Transaction trả phải được lưu.
Quantity phải được lưu.
Reason phải được lưu.
Quan hệ với lần nhập ban đầu phải được giữ.

Mục tiêu là truy được tổng lượng vật liệu đã từng nhận và sử dụng.

28. Xuất kho / Material Handover
BR-030 --- Xuất kho là transaction bàn giao

Khi kho xuất vật liệu cho bộ phận/người nhận, phải ghi nhận:

Part No.
Part Name.
Purchase Order.
Manufacturing Order.
Quantity.
Material.
Weight nếu áp dụng.
Người xuất.
Người nhận.
Time.
Condition.
Ghi chú.

Quantity xuất phải được kiểm soát và liên kết với Quantity gốc/Quantity còn lại.

Quantity xuất không được vượt quá Quantity hợp lệ còn lại.

29. Handover --- Bắt buộc xác nhận hai bên
BR-031 --- Transaction bàn giao phải có người giao và người nhận

Khi một Part/Material/Quantity được bàn giao từ một người/bộ phận cho một người/bộ phận khác, transaction chỉ được coi là hoàn tất khi:

Người giao đăng nhập và tạo phiếu.
Người giao Submit phiếu.
Người nhận đăng nhập.
Người nhận kiểm tra thực tế.
Người nhận Accept hoặc Reject.

Không coi phiếu là hoàn tất ngay khi người giao Submit.

Trạng thái:

text
DRAFT
   ↓
SUBMITTED
   ↓
WAITING_RECEIVER_CONFIRMATION
   ↓
ACCEPTED / REJECTED
30. Receiving Inspection
BR-032 --- Người nhận có quyền xác nhận tình trạng thực tế

Người nhận phải có khả năng xác nhận:

Đã nhận đủ số lượng.
Nhận thiếu.
Nhận hàng NG/hỏng.
Có sai Part.
Có vấn đề khác.

Nếu Reject/Exception phải nhập lý do.

Có thể đính kèm ảnh khi cần.

31. Không sửa transaction xuất kho gốc
BR-033 --- Tách biệt khai báo của người xuất và kết quả nhận thực tế

Ví dụ người xuất khai báo:

text
10 EA
Condition = OK

Người nhận phát hiện:

text
8 EA OK
2 EA NG

Không sửa transaction xuất kho ban đầu thành 8 OK + 2 NG.

Phải lưu riêng:

text
Transaction 001
Type = ISSUE
Sender = User A
Quantity = 10
Declared Condition = OK
Time = 08:30

và:

text
Transaction 002
Type = RECEIVING_INSPECTION
Receiver = User B
Received = 10
OK = 8
NG = 2
Time = 08:35
Reason = Damage

Như vậy hệ thống có thể xác định rõ:

Người xuất đã khai báo gì.
Người nhận thực tế xác nhận gì.
Khi nào có sự khác biệt.
Ai chịu trách nhiệm cho từng bước.
32. Handover Exception
BR-034 --- Khi người nhận phát hiện vấn đề

Nếu người nhận Reject:

text
Status = EXCEPTION / REJECTED

Phải lưu:

Receiver.
Time.
Actual quantity.
OK quantity.
NG quantity nếu có.
Reason.
Photo/evidence nếu cần.
Related issue/rework/return transaction nếu phát sinh.

Không tự động xóa hoặc sửa phiếu xuất gốc.

33. Bằng chứng điện tử
BR-035 --- Không cần chữ ký giấy nếu hệ thống ghi nhận đủ bằng chứng

Đối với transaction yêu cầu bàn giao, hệ thống dùng:

User ID người giao.
User ID người nhận.
Timestamp.
Nội dung khai báo.
Nội dung xác nhận.
Lịch sử thao tác.
Reason khi Reject.
Hình ảnh nếu có.

Đây là bằng chứng điện tử của transaction.

34. Quy tắc không xóa dữ liệu
BR-036 --- Database tuyệt đối không được xóa lịch sử nghiệp vụ

Dữ liệu nghiệp vụ đã phát sinh không được hard delete.

Nếu nghiệp vụ cần hủy/sửa:

text
Original Transaction
       ↓
Correction / Adjustment / Void
       ↓
Reason
       ↓
User
       ↓
Time

Dữ liệu gốc vẫn phải tồn tại để truy xuất lịch sử.

35. Báo cáo

Hệ thống cần có khả năng tạo các báo cáo/KPI:

Production
Planned Quantity.
Actual Quantity.
Completed Quantity.
Remaining Quantity.
Progress %.
Time
Worker useful time.
Worker idle/unuseful time.
Machine running time.
Machine downtime.
Transport Time.
Lead Time.
Cycle Time.
Quality
OK Quantity.
NG Quantity.
NG Rate.
Rework Quantity.
Rework Rate.
Productivity
Worker productivity.
Machine productivity.
Throughput.
Material
Planned material demand.
Received material.
Available material.
NG material.
Returned material.
Supplement material.
Material shortage.
36. WIP

Hệ thống phải xác định được Work In Progress.

Có thể truy vấn:

text
Part nào đang ở Process nào?
Job nào đang chờ?
Job nào đang chạy?
Job nào đang Pause?
Job nào đang Rework?
Job nào đang chờ vật liệu?
Job nào đã Finish?
Job nào có NG?
37. Dashboard

Dashboard quản lý cần thể hiện được tình trạng sản xuất hiện tại, ví dụ:

text
Total Jobs
Running
Waiting
Paused
Waiting Material
Rework
Completed
NG

và các KPI liên quan.

Dashboard chỉ là phần hiển thị; dữ liệu phải được lấy từ transaction/history chính xác.

38. Giao diện

Giao diện Worker phải đơn giản, ưu tiên thao tác nhanh trên điện thoại.

Các thao tác thường xuyên nên là nút lớn:

text
START
PAUSE
RESUME
FINISH
NG
REWORK

Các thông tin đã biết từ hệ thống nên tự động hiển thị.

Không bắt Worker nhập lại dữ liệu không cần thiết.

39. Web Application

Hệ thống được định hướng triển khai dạng Web App.

Kiến trúc:

text
PC / Laptop
      │
Mobile / Tablet
      │
      ▼
   Web App
      │
      ▼
C# ASP.NET Core API
      │
      ▼
 SQL Server

PC, điện thoại và tablet không cần cài một phần mềm desktop riêng để sử dụng nghiệp vụ thông thường.

Có thể sử dụng browser/PWA.

40. Backend là nguồn kiểm soát nghiệp vụ

Frontend không được tự quyết định các business rule quan trọng.

Ví dụ:

text
Frontend:
"User bấm FINISH"

Backend:
- Kiểm tra quyền.
- Kiểm tra Job.
- Kiểm tra Current Process.
- Kiểm tra Quantity.
- Kiểm tra trạng thái.
- Tạo transaction.
- Tạo history.
- Update current state.

Backend là nơi thực thi business rules chính.

41. Integrity Rule
BR-037 --- Input phải có Output giải thích được

Các transaction về số lượng phải đảm bảo cân bằng logic.

Ví dụ:

text
Input = 10

thì Output phải giải thích được:

text
8 OK
2 NG

hoặc:

text
10 OK

Không được có tình trạng:

text
Input = 10
Output = 9

mà không biết 1 EA còn ở đâu.

Tương tự:

text
10 Input
→ 8 OK
→ 1 NG
→ 1 WIP

phải giải thích được tổng 10.