# Tài liệu Yêu cầu

## Giới thiệu

Tính năng này cập nhật dự án AntiBridge.Avalonia (C#) để hỗ trợ định dạng token mới của Antigravity IDE (phiên bản >= 1.16.5). Antigravity IDE đã thay đổi cơ chế lưu trữ token từ phiên bản 1.16.5, sử dụng key mới `antigravityUnifiedStateSync.oauthToken` với cấu trúc protobuf lồng nhau thay vì key cũ `jetskiStateSync.agentManagerInitState`. Dự án cần tự động phát hiện phiên bản Antigravity và chọn đúng định dạng để đảm bảo tương thích ngược và hỗ trợ phiên bản mới.

## Thuật ngữ

- **AntigravityVersionService**: Dịch vụ phát hiện phiên bản Antigravity IDE đang cài đặt trên hệ thống
- **AntigravityDbService**: Dịch vụ đọc/ghi token vào cơ sở dữ liệu SQLite của Antigravity IDE
- **ProtobufHelper**: Lớp tiện ích mã hóa/giải mã protobuf cho định dạng state của Antigravity
- **Old_Format**: Định dạng token sử dụng key `jetskiStateSync.agentManagerInitState`, áp dụng cho Antigravity < 1.16.5
- **New_Format**: Định dạng token sử dụng key `antigravityUnifiedStateSync.oauthToken`, áp dụng cho Antigravity >= 1.16.5
- **OAuthTokenInfo**: Cấu trúc protobuf chứa access_token, token_type, refresh_token và expiry
- **Version_Threshold**: Phiên bản 1.16.5, ranh giới giữa Old_Format và New_Format

## Yêu cầu

### Yêu cầu 1: Phát hiện phiên bản Antigravity

**User Story:** Là một người dùng, tôi muốn hệ thống tự động phát hiện phiên bản Antigravity IDE đang cài đặt, để hệ thống chọn đúng định dạng token tương ứng.

#### Tiêu chí chấp nhận

1. WHEN AntigravityVersionService được gọi trên Windows, THE AntigravityVersionService SHALL đọc phiên bản từ metadata của file thực thi Antigravity bằng PowerShell `(Get-Item 'path').VersionInfo.FileVersion`
2. WHEN AntigravityVersionService được gọi trên macOS, THE AntigravityVersionService SHALL đọc phiên bản từ file `Info.plist` (trường `CFBundleShortVersionString`) trong bundle `.app` của Antigravity
3. WHEN AntigravityVersionService được gọi trên Linux, THE AntigravityVersionService SHALL đọc phiên bản bằng cách chạy lệnh `--version` hoặc đọc file `package.json` trong thư mục cài đặt
4. WHEN phiên bản được phát hiện thành công, THE AntigravityVersionService SHALL trả về chuỗi phiên bản theo định dạng semantic versioning (ví dụ: "1.16.5")
5. IF không tìm thấy file thực thi Antigravity hoặc không đọc được metadata phiên bản, THEN THE AntigravityVersionService SHALL trả về lỗi mô tả rõ nguyên nhân thất bại

### Yêu cầu 2: So sánh phiên bản và xác định định dạng

**User Story:** Là một người dùng, tôi muốn hệ thống xác định chính xác Antigravity đang dùng định dạng cũ hay mới, để token được ghi đúng format.

#### Tiêu chí chấp nhận

1. WHEN phiên bản Antigravity >= 1.16.5, THE AntigravityVersionService SHALL xác định đây là New_Format
2. WHEN phiên bản Antigravity < 1.16.5, THE AntigravityVersionService SHALL xác định đây là Old_Format
3. THE AntigravityVersionService SHALL so sánh phiên bản theo từng thành phần major.minor.patch dưới dạng số nguyên

### Yêu cầu 3: Ghi token theo New_Format (Sync To Antigravity)

**User Story:** Là một người dùng chạy Antigravity >= 1.16.5, tôi muốn đồng bộ token từ AntiBridge sang Antigravity, để tôi có thể sử dụng tài khoản đã đăng nhập trong Antigravity.

#### Tiêu chí chấp nhận

1. WHEN Antigravity phiên bản >= 1.16.5, THE AntigravityDbService SHALL ghi token vào key `antigravityUnifiedStateSync.oauthToken` bằng lệnh SQL INSERT OR REPLACE
2. WHEN ghi theo New_Format, THE AntigravityDbService SHALL tạo cấu trúc protobuf lồng nhau: OuterMessage chứa Field 1 là InnerMessage, InnerMessage chứa Field 1 là chuỗi "oauthTokenInfoSentinelKey" và Field 2 là InnerMessage2, InnerMessage2 chứa Field 1 là chuỗi base64 của OAuthTokenInfo
3. WHEN ghi theo New_Format, THE ProtobufHelper SHALL tạo OAuthTokenInfo gồm access_token (Field 1), token_type "Bearer" (Field 2), refresh_token (Field 3), và expiry timestamp (Field 4)
4. WHEN ghi token thành công theo New_Format, THE AntigravityDbService SHALL ghi flag onboarding `antigravityOnboarding` = "true"

### Yêu cầu 4: Ghi token theo Old_Format (tương thích ngược)

**User Story:** Là một người dùng chạy Antigravity < 1.16.5, tôi muốn chức năng Sync To Antigravity vẫn hoạt động bình thường, để tôi không bị ảnh hưởng bởi bản cập nhật.

#### Tiêu chí chấp nhận

1. WHEN Antigravity phiên bản < 1.16.5, THE AntigravityDbService SHALL ghi token vào key `jetskiStateSync.agentManagerInitState` bằng lệnh SQL UPDATE
2. WHEN ghi theo Old_Format, THE AntigravityDbService SHALL đọc state hiện tại, giải mã base64, xóa Field 1, Field 2 và Field 6 cũ, thêm Field 6 mới chứa OAuthTokenInfo, mã hóa base64 và ghi lại
3. IF không tồn tại state hiện tại trong database khi ghi theo Old_Format, THEN THE AntigravityDbService SHALL trả về lỗi yêu cầu đăng nhập Antigravity trước

### Yêu cầu 5: Cơ chế fallback khi không phát hiện được phiên bản

**User Story:** Là một người dùng, tôi muốn chức năng Sync vẫn hoạt động ngay cả khi hệ thống không phát hiện được phiên bản Antigravity, để tôi không bị chặn bởi lỗi phát hiện phiên bản.

#### Tiêu chí chấp nhận

1. IF phát hiện phiên bản thất bại, THEN THE AntigravityDbService SHALL thử ghi token theo cả New_Format và Old_Format
2. WHEN thử cả hai định dạng trong chế độ fallback, THE AntigravityDbService SHALL trả về thành công nếu ít nhất một định dạng ghi thành công
3. IF cả hai định dạng đều thất bại trong chế độ fallback, THEN THE AntigravityDbService SHALL trả về lỗi mô tả chi tiết lỗi của từng định dạng

### Yêu cầu 6: Đọc token theo cả hai định dạng (Import From Antigravity)

**User Story:** Là một người dùng, tôi muốn import token từ Antigravity vào AntiBridge bất kể phiên bản Antigravity, để tôi có thể sử dụng tài khoản Antigravity trong AntiBridge.

#### Tiêu chí chấp nhận

1. WHEN đọc token từ Antigravity, THE AntigravityDbService SHALL thử đọc từ key New_Format (`antigravityUnifiedStateSync.oauthToken`) trước
2. IF đọc từ key New_Format thất bại hoặc không tìm thấy dữ liệu, THEN THE AntigravityDbService SHALL thử đọc từ key Old_Format (`jetskiStateSync.agentManagerInitState`)
3. WHEN đọc theo New_Format, THE AntigravityDbService SHALL giải mã cấu trúc protobuf lồng nhau, giải mã base64 của OAuthTokenInfo, và trích xuất refresh_token
4. IF cả hai key đều không chứa dữ liệu hợp lệ, THEN THE AntigravityDbService SHALL trả về lỗi thông báo không tìm thấy token

### Yêu cầu 7: Hàm protobuf helper mới

**User Story:** Là một nhà phát triển, tôi muốn có các hàm helper protobuf mới để hỗ trợ mã hóa New_Format, để code ghi token mới được triển khai chính xác.

#### Tiêu chí chấp nhận

1. THE ProtobufHelper SHALL cung cấp hàm `CreateOAuthInfo` tạo OAuthTokenInfo message không bao gồm wrapper Field 6 (dùng cho New_Format)
2. THE ProtobufHelper SHALL cung cấp hàm `EncodeStringField` mã hóa một chuỗi thành protobuf field với wire_type length-delimited
3. THE ProtobufHelper SHALL cung cấp hàm `EncodeLenDelimField` mã hóa mảng byte thành protobuf field với wire_type length-delimited
4. FOR ALL chuỗi UTF-8 hợp lệ, mã hóa bằng `EncodeStringField` rồi giải mã bằng `FindField` SHALL trả về chuỗi gốc (round-trip property)
5. FOR ALL mảng byte hợp lệ, mã hóa bằng `EncodeLenDelimField` rồi giải mã bằng `FindField` SHALL trả về mảng byte gốc (round-trip property)

### Yêu cầu 8: Tìm đường dẫn file thực thi Antigravity

**User Story:** Là một người dùng, tôi muốn hệ thống tự động tìm file thực thi Antigravity trên mọi nền tảng, để phát hiện phiên bản hoạt động chính xác.

#### Tiêu chí chấp nhận

1. THE AntigravityProcessService SHALL cung cấp phương thức `GetAntigravityExecutablePath` trả về đường dẫn file thực thi Antigravity
2. WHEN chạy trên Windows, THE AntigravityProcessService SHALL tìm file thực thi tại các đường dẫn chuẩn bao gồm `%LOCALAPPDATA%\Programs\Antigravity\Antigravity.exe`
3. WHEN chạy trên macOS, THE AntigravityProcessService SHALL tìm file thực thi tại `/Applications/Antigravity.app/Contents/MacOS/Antigravity`
4. WHEN chạy trên Linux, THE AntigravityProcessService SHALL tìm file thực thi tại `/usr/bin/antigravity`, `/usr/local/bin/antigravity`, hoặc sử dụng lệnh `which`
5. IF không tìm thấy file thực thi tại bất kỳ đường dẫn nào, THEN THE AntigravityProcessService SHALL trả về null
