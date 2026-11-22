# Chipi Prototype V0.1

Chipi là gia sư luyện nói tiếng Anh trên điện thoại. Mở project `Unity` bằng Unity 6000.6.7f1 và mở scene `Assets/Chipi.unity`. Nút **Try voice demo** phát câu trả lời mẫu ngay trong app. Chế độ này chạy offline.

## Hội thoại trên iPhone không cần backend

Bản iPhone dùng hội thoại trên thiết bị theo mặc định. Cần iOS 26 trở lên, iPhone hỗ trợ Apple Intelligence, Apple Intelligence đã bật và tải xong, cùng quyền Microphone và Speech Recognition. Chipi nhận diện tiếng Anh trên thiết bị, tạo câu trả lời bằng Foundation Models rồi đọc bằng giọng hệ thống. Luồng này không gọi Gemini và không cần Mac. Nếu đang dùng máy chủ thử nghiệm, chọn **Voice settings → Use on-device voice** để chuyển lại.

Bản Simulator giữ luồng máy chủ thử nghiệm. Kiểm tra hội thoại trên thiết bị thật bằng cách bật chế độ máy bay sau khi Apple Intelligence đã sẵn sàng, sau đó nói hai lượt và xác nhận Chipi đọc hết từng câu, tự mở lại mic và giữ được ngữ cảnh.


## Chạy Phase 1 trên iPhone giả lập

```sh
scripts/run_iphone_simulator.sh --build
```

Lần sau có thể bỏ `--build` để dùng lại bản đã đóng gói. Script mở iPhone 17 Pro Max giả lập, cài Chipi và khởi động máy chủ demo trên Mac nếu cần. **Start conversation** đã trỏ sẵn vào máy chủ cục bộ trên bản giả lập. Chạm một lần để bắt đầu phiên. Chipi mở lại micro sau khi phát xong câu trả lời. Chọn **End conversation** để dừng phiên. Chipi tự gửi sau khoảng 1,2 giây im lặng kể từ tiếng nói cuối cùng. Có thể dùng **Send now** để gửi sớm. Nếu chưa phát hiện tiếng nói sau 8 giây, Chipi dừng nghe. Mỗi lượt nhận tối đa khoảng 15 giây lời nói. Dùng **Try voice demo** để nghe câu mẫu ngay trong app.

Trong phiên, **Mute** dừng nghe và **Unmute** mở lại micro; lời nói chưa gửi khi bật Mute sẽ bị bỏ. Khi Chipi đang xử lý, **Cancel response** bỏ lượt hiện tại; khi Chipi đang nói, **Interrupt** dừng câu trả lời và cho phép nói tiếp. Nếu micro đang tắt, Chipi giữ trạng thái tắt. Sau lỗi, **Retry** gửi lại lượt vừa rồi và **Speak again** bắt đầu lượt mới, vẫn giữ ngữ cảnh của những lượt đã hoàn tất. Kết thúc phiên sẽ xóa ngữ cảnh và âm thanh đang chờ thử lại. Ngắt lời bằng giọng nói tự động chưa được hỗ trợ.

Trong khoảng 0,2 giây đầu, bộ phát hiện lời nói ước lượng tiếng nền; tiếng nói đủ lớn vẫn được nhận ngay. Các bản build Unity tự kiểm tra phát hiện lời nói với WAV mẫu, tiếng nền và khoảng ngừng ngắn.

## Các bản build

- iPhone giả lập dùng dự án `build/Chipi-iOS-Simulator/Unity-iPhone.xcodeproj`. Đặt `CHIPI_IOS_SIMULATOR=1` khi chạy `ChipiBuild.IOS` để xuất lại. Mở dự án trong Xcode và chọn iPhone giả lập làm thiết bị chạy.
- iPhone thật dùng dự án Xcode xuất tại `build/Chipi-iOS/Unity-iPhone.xcodeproj`. Unity có điểm build `ChipiBuild.IOS`. Bản thiết bị dùng Apple Development signing.
- Android dùng `build/Chipi-dev.apk` trên thiết bị ARM64 chạy Android 8 trở lên. Unity có điểm build `ChipiBuild.Android`.

Mỗi lần chạy `ChipiBuild.IOS` hoặc `ChipiBuild.Android`, Unity tăng phần cuối của phiên bản và hiển thị phiên bản trên màn hình chính. Build lại dự án đã xuất bằng Xcode không tăng phiên bản; cần chạy lại điểm build Unity để tạo phiên bản mới.

Khi macOS yêu cầu `codesign` truy cập khóa Apple Development, chọn **Always Allow** và nhập mật khẩu trực tiếp trong hộp thoại Keychain. Không lưu mật khẩu đăng nhập trong `.env`.

## Thử máy chủ thoại

Yêu cầu Java 17+ và Maven.

```sh
mvn -f backend/pom.xml package
java -jar backend/target/chipi-backend-0.1.0.jar
```

Trong terminal khác, chạy `python3 scripts/smoke_backend.py`. Chế độ demo mặc định trả văn bản và âm thanh mẫu. Trên Android cắm USB, dùng `adb reverse tcp:8080 tcp:8080` và chọn **Voice settings → Connect voice server**. Trên iPhone, chọn **Voice settings → Connect voice server** rồi nhập địa chỉ HTTPS của máy chủ thử nghiệm có chứng chỉ hợp lệ.

Để bật OpenAI, cấu hình biến môi trường `OPENAI_API_KEY` trong tiến trình máy chủ, rồi chạy với `CHIPI_DEMO=false`. Máy chủ gửi âm thanh qua STT, chuyển văn bản cho mô hình gia sư, rồi trả câu trả lời và WAV qua TTS. Trong cùng phiên, app gửi tối đa ba cặp câu hỏi và câu trả lời gần nhất làm ngữ cảnh. App xóa ngữ cảnh khi kết thúc phiên hoặc khi phiên dừng do lỗi. Ngữ cảnh chỉ tồn tại trong bộ nhớ của app. Mặc định máy chủ chỉ nghe trên 127.0.0.1. Khi thử từ iPhone, ưu tiên đặt HTTPS ở lớp chuyển tiếp có giới hạn truy cập và giữ máy chủ nội bộ. Khóa OpenAI chỉ nằm ở máy chủ. Bản thử nghiệm chưa có xác thực người dùng.

## Thử luồng thoại Gemini

Đặt `CHIPI_VOICE_PROVIDER=gemini` và `GEMINI_API_KEY` trong file `.env` ở thư mục Chipi. Script `scripts/run_iphone_simulator.sh` tự nạp file này riêng cho backend. File được bỏ qua khi đưa mã vào Git. Gemini 3.1 Flash-Lite nhận WAV và tạo transcript cùng câu trả lời trong một yêu cầu. Gemini 3.8 Flash TTS tạo WAV để Unity phát. Hai model có Free Tier theo bảng giá Google, với hạn mức tùy project. Chế độ này không tự đổi model hoặc bật thanh toán khi vượt hạn mức.

```sh
scripts/run_iphone_simulator.sh
```

Khi cổng 8080 đang có một backend demo, dừng tiến trình demo trước khi khởi động Gemini. Dùng WAV mẫu để kiểm thử ban đầu. [Giá và Free Tier](https://aistudio.google.com/docs/pricing) · [Hạn mức project](https://aistudio.google.com/rate-limit)

## Nhân vật

`Unity/Assets/Resources/ChipiConcept.png` là ảnh chính diện người dùng cung cấp. `art/Chipi.blend` gồm các phần lưới 3D khép kín cho mũ, thân, tay và chân. Hình ảnh chính diện được chiếu lên bề mặt để giữ khuôn mặt, màu giáp và các chi tiết nhỏ. Chiều sâu, cạnh và mặt sau dựng theo ước lượng từ một ảnh.

Trong app, Chipi hơi xoay khi đứng yên. Vật liệu dùng pháp tuyến của lưới để tạo sáng tối và viền sáng trên giáp, với bóng mềm dưới chân.

Rig có năm clip Idle, Greeting, Listening, Talking và Happy. Shader điều chỉnh độ mở và độ tròn của miệng từ tín hiệu âm thanh, giữ nụ cười khi nghỉ. Màn hình nhỏ trên ngực phản ứng khi nghe, chờ máy chủ hoặc phát giọng. Khẩu hình theo từng âm vị chưa được triển khai.

Bản Unity Development có thể kiểm thử lượt gửi WAV mẫu đến máy chủ demo cục bộ trên iPhone giả lập bằng biến môi trường `SIMCTL_CHILD_CHIPI_CHECK_ROUND_TRIP=1` khi chạy `xcrun simctl launch`. Dùng `SIMCTL_CHILD_CHIPI_CHECK_HISTORY=1` để chạy hai lượt WAV mẫu và một lượt sau khi xóa ngữ cảnh.

Chạy `node scripts/check_simulator_voice.js` sau khi build simulator để tự kiểm tra phát hết PCM, tương thích WAV cũ, nhiều lượt, thử lại sau lỗi, Mute, Resume, Interrupt, hủy và các trạng thái quyền micro. Kiểm thử dùng fixture cục bộ và micro giả lập, không gọi provider thật hoặc chứng minh hoạt động của micro, loa hay AirPods trên iPhone; log được lưu trong thư mục build được in ra sau khi chạy.

Khi tự kiểm tra app trên simulator, log Unity nằm ở `build/phase1-simulator.log` và log backend ở `build/phase1-backend.log`. Gửi các dòng `[CHIPI-DIAG]` cùng các dòng `voice.turn` có cùng `id` để đối chiếu capture với backend. Log chẩn đoán không ghi âm thanh, transcript, câu trả lời hay khóa provider.

`art/build_chipi.py` tái tạo Blender, FBX và ảnh preview. Chạy script bằng Blender 5 ở chế độ nền. Các gói cài Android và iPhone thật đã tạo trước lần sửa ngoại hình này cần được build lại để lấy model mới.

## Phát triển với PO, Dev và QC

Mở thư mục Chipi trong Codex và yêu cầu chạy workflow PO → Dev → QC. Xem [vai trò, tiêu chí nghiệm thu và nguồn tham khảo](docs/agent-workflow.md).
