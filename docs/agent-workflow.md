# Workflow PO → Dev → QC cho Chipi

Mở thư mục Chipi trong Codex và yêu cầu **Dùng workflow PO → Dev → QC cho thay đổi này**. Mỗi vai trò có cấu hình riêng trong `.codex/agents/` và hướng dẫn trong `.agents/skills/`.

| Bước | Chủ trì | Đầu ra |
|---|---|---|
| 1. Xác định yêu cầu | PO | Mục tiêu người học, tiêu chí đạt, trường hợp lỗi, phần ngoài phạm vi |
| 2. Triển khai và chuẩn bị kiểm thử | Dev và QC | Dev viết mã và chạy kiểm thử của mình. QC chuẩn bị kiểm thử độc lập song song từ tiêu chí của PO. |
| 3. Kiểm chứng bản giao | QC | Chạy các ca liên quan trên đúng bản build. Ghi **PASS**, **FAIL** hoặc **BLOCKED** cho từng ca bắt buộc. |
| 4. Sửa và kiểm tra lại | Dev → QC | Dev nhận lỗi có bước tái hiện. QC chạy lại ca lỗi và ca hồi quy gần nhất. |
| 5. Bàn giao | PO và điều phối | PO đối chiếu với mục tiêu. Điều phối gửi một báo cáo chung về bản chạy, kết quả và phần còn thiếu. |

Trong một đợt kiểm thử toàn bộ app, PO lập danh mục tính năng và ca nghiệm thu. Dev bàn giao đúng bản build, hỗ trợ tái hiện và nhận lỗi để sửa. QC kiểm thử độc lập và chốt từng ca. Điều phối tổng hợp thành một báo cáo duy nhất. Các lần deploy dùng simulator theo mặc định của dự án.

QC nhận mã nhận diện bản build, môi trường, chế độ backend, cách chạy và lỗi đã biết từ Dev. Mỗi thời điểm chỉ một agent giữ quyền chạy hoặc khởi động lại cùng một iPhone, simulator hay backend. QC ghi bằng chứng và trả lỗi ứng dụng cho Dev.

Một thay đổi chỉ đạt khi các ca bắt buộc có bằng chứng PASS. Lỗi ảnh hưởng tiêu chí trả về Dev. Ca chưa chạy vì thiếu thiết bị, quyền hoặc dịch vụ được ghi BLOCKED. PO có thể điều chỉnh phạm vi với người dùng khi cần. Simulator xác nhận luồng phần mềm với WAV mẫu. Ca micro và âm thanh phần cứng cần iPhone thật. Xem [ma trận kiểm thử thoại](../.agents/skills/chipi-quality-control/references/voice-qc.md).

## Thay đổi giao diện

PO, Dev và QC đều dùng [hướng dẫn UX/UI trên iPhone](../.agents/skills/chipi-mobile-ux/SKILL.md). PO nêu thứ tự ưu tiên và trường hợp nội dung dài. Dev kiểm tra vùng an toàn và giữ nút thoại dễ bấm. QC đối chiếu ảnh trước và sau, kiểm tra phần chữ và ghi rõ thao tác nào đã thử trực tiếp trên simulator.

## Ví dụ đang mở

Bản iPhone hiện tại mở được micro, chọn mic tích hợp và nhận số lượng frame tăng, nhưng mức đỉnh ngay ở bộ thu iOS là **0,000000** trong lượt đã quan sát. Ca **micro iPhone thật** là **FAIL** cho lượt này. Cuộc gọi Gemini bằng WAV mẫu trên máy giả lập là bằng chứng riêng. QC cần thử lại bằng một câu nói trên iPhone thật, đối chiếu mức tín hiệu ở iOS và Unity, rồi xác nhận gửi và phát. Voice Memos là phép đối chiếu khi người dùng có thể thử thiết bị.

## Lát cắt đầu tiên

Một người học có thể nói “What did you just ask me?” trong cùng phiên và Chipi nhận được nội dung lượt trước để hiểu “that”. Giữ một cửa sổ ngắn trong phiên. Kết thúc phiên hoặc bắt đầu phiên mới phải xóa ngữ cảnh. Nếu lượt gửi hoặc phát thất bại, nó không được trở thành ngữ cảnh cho lượt sau. Luồng demo và client cũ vẫn dùng được. Phát hiện hết câu bằng ngữ nghĩa, ngắt lời khi Chipi đang nói và thay đổi tốc độ đọc nằm ngoài lát cắt này.

## Hạng mục tiếp theo

1. Thử micro với câu ngắn, câu có khoảng dừng tìm từ và tiếng nền. Ghi lại số lượt bị gửi sớm và thời gian từ lúc nói xong đến lúc nghe được câu trả lời.
2. Đánh giá phát hiện hết lượt theo ngữ nghĩa bằng các đoạn nói đã ghi nhận. Chỉ ưu tiên tích hợp nếu giảm cắt lời mà thời gian phản hồi vẫn phù hợp cho bài luyện nói.
3. Thử ngắt lời và gợi ý tốc độ đọc sau khi luồng nhiều lượt đã chạy ổn định.

## Nguồn

- [Codex subagents](https://learn.chatgpt.com/docs/agent-configuration/subagents) cho cấu hình vai trò và điều phối.
- [Codex skills](https://learn.chatgpt.com/docs/build-skills) cho hướng dẫn chuyên môn theo dự án.
- [Scrum Guide](https://scrumguides.org/scrum-guide.html) cho trách nhiệm PO, thứ tự ưu tiên và tiêu chí hoàn thành.
- [OpenAI conversation state](https://developers.openai.com/api/docs/guides/conversation-state) cho cách truyền ngữ cảnh qua Chat Completions.
