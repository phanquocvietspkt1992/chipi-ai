# Chipi Prototype V0.1

Chipi là gia sư luyện nói tiếng Anh trên điện thoại. Mở project `Unity` bằng Unity 6000.6.7f1 và mở scene `Assets/Chipi.unity`. Nút **Try voice sample** phát câu trả lời mẫu ngay trong app. Chế độ này chạy offline.

## Hội thoại trên iPhone không cần backend

Bản iPhone dùng Apple Speech để nhận diện giọng nói trên thiết bị và Apple Intelligence để tạo câu trả lời. Cần iOS 26 trở lên, iPhone hỗ trợ Apple Intelligence, mô hình đã sẵn sàng và quyền Microphone cùng Speech Recognition. Giọng chào, câu mẫu và câu trả lời dùng cùng giọng Alba của Pocket TTS English 18M. Mô hình giọng nói được đóng gói với app và chạy trên thiết bị. Apple Intelligence vẫn tạo nội dung câu trả lời.

Bản giả lập chạy không cần máy chủ. **Try voice sample** kiểm tra giọng nói Pocket TTS. **Start conversation** cần mô hình Apple Intelligence khả dụng. Kiểm tra toàn bộ luồng trên iPhone thật bằng cách bật chế độ máy bay sau khi Apple Intelligence đã sẵn sàng, nói hai lượt và xác nhận Chipi đọc hết câu, mở lại mic và giữ được ngữ cảnh.

### Nhớ cuộc trò chuyện

Bản iPhone lưu trọn văn bản của từng lượt đã phát xong vào bộ nhớ riêng của app. **End conversation** chỉ dừng phiên nghe. Khi mở lại, Chipi hiển thị lượt gần nhất và nút **Continue conversation**. Mỗi câu trả lời có thể dùng vài lượt gần nhất và những lượt cũ có từ khóa liên quan. Toàn bộ nhật ký được giữ trên thiết bị và được loại khỏi bản sao lưu. App không lưu bản ghi âm vào nhật ký. Lượt từ các bản cũ chưa lưu nhật ký không thể khôi phục.

Kiểm tra lưu, mở lại và tìm lượt cũ trên Mac:

```sh
mkdir -p build/conversation-memory
xcrun swiftc -parse-as-library Unity/Assets/Plugins/iOS/ChipiConversationMemory.swift \
  tests/ChipiConversationMemoryCheck.swift -o build/conversation-memory/check
build/conversation-memory/check
```

## Chạy Phase 1 trên iPhone giả lập

```sh
scripts/run_iphone_simulator.sh --build
```

Lần sau có thể bỏ `--build` để dùng lại bản đã đóng gói. Script mở iPhone 17 Pro Max giả lập (iOS 27.0) và cài Chipi. Có thể đặt `CHIPI_SIMULATOR_UDID` để chọn thiết bị khác. Trên máy phát triển này, iOS 26.2 báo mô hình sẵn sàng nhưng gặp lỗi nội bộ khi tạo câu trả lời. Dùng iOS 27.0 để kiểm tra hội thoại. Chạm một lần để bắt đầu phiên. Chipi mở lại micro sau khi phát xong câu trả lời. Chọn **End conversation** để dừng phiên. Chipi tự gửi sau khoảng 1,2 giây im lặng kể từ tiếng nói cuối cùng. Có thể dùng **Send now** để gửi sớm. Nếu chưa phát hiện tiếng nói sau 8 giây, Chipi dừng nghe. Mỗi lượt nhận tối đa khoảng 15 giây lời nói. Dùng **Try voice sample** để nghe câu mẫu ngay trong app.

Trong phiên, **Mute** dừng nghe và **Unmute** mở lại micro; lời nói chưa gửi khi bật Mute sẽ bị bỏ. Khi Chipi đang xử lý, **Cancel response** bỏ lượt hiện tại; khi Chipi đang nói, **Interrupt** dừng câu trả lời và cho phép nói tiếp. Nếu micro đang tắt, Chipi giữ trạng thái tắt. Sau lỗi, **Retry** gửi lại lượt vừa rồi và **Speak again** bắt đầu lượt mới, vẫn giữ ngữ cảnh của những lượt đã hoàn tất. Kết thúc phiên sẽ bỏ âm thanh đang chờ thử lại. Lịch sử đã lưu vẫn dùng được ở phiên sau. Ngắt lời bằng giọng nói tự động chưa được hỗ trợ.

Trong khoảng 0,2 giây đầu, bộ phát hiện lời nói ước lượng tiếng nền; tiếng nói đủ lớn vẫn được nhận ngay. Các bản build Unity tự kiểm tra phát hiện lời nói với WAV mẫu, tiếng nền và khoảng ngừng ngắn.

## Các bản build

- iPhone giả lập dùng dự án `build/Chipi-iOS-Simulator/Unity-iPhone.xcodeproj`. Đặt `CHIPI_IOS_SIMULATOR=1` khi chạy `ChipiBuild.IOS` để xuất lại. Mở dự án trong Xcode và chọn iPhone giả lập làm thiết bị chạy.
- Trước khi xuất iOS trực tiếp từ Unity, chạy `python3 scripts/prepare_chipi_pocket_voice.py` để tải và kiểm tra tệp mô hình Pocket TTS. Bản simulator cũng tải Kokoro để so sánh. Script build simulator tự làm bước này.
- iPhone thật dùng dự án Xcode xuất tại `build/Chipi-iOS/Unity-iPhone.xcodeproj`. Unity có điểm build `ChipiBuild.IOS`. Bản thiết bị dùng Apple Development signing.
- Android dùng `build/Chipi-dev.apk` trên thiết bị ARM64 chạy Android 8 trở lên. Unity có điểm build `ChipiBuild.Android`.

Mỗi lần chạy `ChipiBuild.IOS` hoặc `ChipiBuild.Android`, Unity tăng phần cuối của phiên bản và hiển thị phiên bản trên màn hình chính. Build lại dự án đã xuất bằng Xcode không tăng phiên bản; cần chạy lại điểm build Unity để tạo phiên bản mới.

Khi macOS yêu cầu `codesign` truy cập khóa Apple Development, chọn **Always Allow** và nhập mật khẩu trực tiếp trong hộp thoại Keychain. Không lưu mật khẩu đăng nhập trong `.env`.

## Thử máy chủ thoại cho Android

Yêu cầu Java 17+ và Maven.

```sh
mvn -f backend/pom.xml package
java -jar backend/target/chipi-backend-0.1.0.jar
```

Trong terminal khác, chạy `python3 scripts/smoke_backend.py`. Chế độ demo mặc định trả văn bản và âm thanh mẫu. Trên Android cắm USB, dùng `adb reverse tcp:8080 tcp:8080` và chọn **Voice settings → Connect voice server**.

Để bật OpenAI, cấu hình biến môi trường `OPENAI_API_KEY` trong tiến trình máy chủ, rồi chạy với `CHIPI_DEMO=false`. Máy chủ gửi âm thanh qua STT, chuyển văn bản cho mô hình gia sư, rồi trả câu trả lời và WAV qua TTS. Trong cùng phiên, app gửi tối đa ba cặp câu hỏi và câu trả lời gần nhất làm ngữ cảnh. App xóa ngữ cảnh khi kết thúc phiên hoặc khi phiên dừng do lỗi. Ngữ cảnh chỉ tồn tại trong bộ nhớ của app. Mặc định máy chủ chỉ nghe trên 127.0.0.1. Khóa OpenAI chỉ nằm ở máy chủ. Bản thử nghiệm chưa có xác thực người dùng.

## Nhân vật

`Unity/Assets/Resources/ChipiConcept.png` là ảnh chính diện người dùng cung cấp. `art/Chipi.blend` gồm các phần lưới 3D khép kín cho mũ, thân, tay và chân. Hình ảnh chính diện được chiếu lên bề mặt để giữ khuôn mặt, màu giáp và các chi tiết nhỏ. Chiều sâu, cạnh và mặt sau dựng theo ước lượng từ một ảnh.

Trong app, Chipi hơi xoay khi đứng yên. Vật liệu dùng pháp tuyến của lưới để tạo sáng tối và viền sáng trên giáp, với bóng mềm dưới chân.

Rig có năm clip Idle, Greeting, Listening, Talking và Happy. Shader điều chỉnh độ mở và độ tròn của miệng từ tín hiệu âm thanh, giữ nụ cười khi nghỉ. Màn hình nhỏ trên ngực phản ứng khi nghe, xử lý hoặc phát giọng. Khẩu hình theo từng âm vị chưa được triển khai.

Bản Unity Development có thể kiểm thử lượt gửi WAV mẫu đến máy chủ demo cục bộ trên iPhone giả lập bằng biến môi trường `SIMCTL_CHILD_CHIPI_CHECK_ROUND_TRIP=1` khi chạy `xcrun simctl launch`. Dùng `SIMCTL_CHILD_CHIPI_CHECK_HISTORY=1` để chạy hai lượt WAV mẫu và một lượt sau khi xóa ngữ cảnh.

Chạy `node scripts/check_simulator_voice.js` sau khi build simulator để tự kiểm tra phát hết PCM, tương thích WAV cũ, nhiều lượt, thử lại sau lỗi, Mute, Resume, Interrupt, hủy và các trạng thái quyền micro. Kiểm thử dùng fixture cục bộ và micro giả lập, không gọi provider thật hoặc chứng minh hoạt động của micro, loa hay AirPods trên iPhone; log được lưu trong thư mục build được in ra sau khi chạy.

Khi tự kiểm tra app trên simulator, log Unity nằm ở `build/phase1-simulator.log`. Gửi các dòng `[CHIPI-DIAG]` liên quan để đối chiếu. Log chẩn đoán không ghi âm thanh, transcript, câu trả lời hay khóa provider.

`art/build_chipi.py` tái tạo Blender, FBX và ảnh preview. Chạy script bằng Blender 5 ở chế độ nền. Các gói cài Android và iPhone thật đã tạo trước lần sửa ngoại hình này cần được build lại để lấy model mới.

## Phát triển với PO, Dev và QC

Mở thư mục Chipi trong Codex và yêu cầu chạy workflow PO → Dev → QC. Xem [vai trò, tiêu chí nghiệm thu và nguồn tham khảo](docs/agent-workflow.md).
