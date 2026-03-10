# Implementation Plan: Hỗ trợ định dạng token mới Antigravity

## Tổng quan

Triển khai hỗ trợ dual-format token cho AntiBridge.Avalonia, port logic từ Antigravity-Manager Rust. Thứ tự: protobuf helpers → version service → DB service update → wiring.

## Tasks

- [x] 1. Mở rộng ProtobufHelper với các hàm helper mới
  - [x] 1.1 Thêm phương thức public `EncodeStringField`, `EncodeLenDelimField`, và `CreateOAuthInfo` vào `src/AntiBridge.Core/Services/ProtobufHelper.cs`
    - `EncodeStringField(int fieldNum, string value)` → public version của `CreateStringField` hiện tại
    - `EncodeLenDelimField(int fieldNum, byte[] value)` → public version của `CreateBytesField` hiện tại
    - `CreateOAuthInfo(string accessToken, string refreshToken, long expiryTimestamp)` → tạo OAuthTokenInfo message KHÔNG có Field 6 wrapper (Field 1: access_token, Field 2: "Bearer", Field 3: refresh_token, Field 4: Timestamp)
    - Refactor `CreateOAuthField` để gọi `CreateOAuthInfo` bên trong rồi wrap Field 6, tránh duplicate code
    - _Requirements: 7.1, 7.2, 7.3_

  - [ ]* 1.2 Viết property test cho EncodeStringField round-trip
    - **Property 6: EncodeStringField round-trip**
    - Generate random strings và field numbers (1-100), encode bằng `EncodeStringField` rồi decode bằng `FindField`, verify trả về chuỗi gốc
    - **Validates: Requirements 7.2, 7.4**

  - [ ]* 1.3 Viết property test cho EncodeLenDelimField round-trip
    - **Property 7: EncodeLenDelimField round-trip**
    - Generate random byte arrays và field numbers (1-100), encode bằng `EncodeLenDelimField` rồi decode bằng `FindField`, verify trả về bytes gốc
    - **Validates: Requirements 7.3, 7.5**

  - [ ]* 1.4 Viết property test cho CreateOAuthInfo structure
    - **Property 4: CreateOAuthInfo structure correctness**
    - Generate random (access_token, refresh_token, expiry), verify output chứa Field 1,2,3,4 và KHÔNG chứa Field 6
    - **Validates: Requirements 3.3, 7.1**

- [x] 2. Tạo AntigravityVersionService
  - [x] 2.1 Tạo file `src/AntiBridge.Core/Services/AntigravityVersionService.cs`
    - Tạo record `VersionResult(string Version, bool IsNewFormat)`
    - Implement `CompareVersions(string v1, string v2)` — split theo '.', parse int, so sánh từng phần
    - Implement `IsNewVersion(string version)` — gọi `CompareVersions(version, "1.16.5") >= 0`
    - Implement `GetAntigravityVersion()` — gọi `AntigravityProcessService.GetAntigravityExecutablePath()`, rồi đọc version theo platform (Windows: PowerShell, macOS: Info.plist XML parse, Linux: --version hoặc package.json)
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 2.1, 2.2, 2.3_

  - [x] 2.2 Cập nhật `AntigravityProcessService` — đổi `GetAntigravityPath()` thành `public static` hoặc thêm phương thức public `GetAntigravityExecutablePath()`
    - _Requirements: 8.1_

  - [ ]* 2.3 Viết property test cho version comparison
    - **Property 1: Version comparison correctness**
    - Generate random (major, minor, patch) tuples, verify `IsNewVersion` trả về đúng so với threshold 1.16.5
    - **Validates: Requirements 2.1, 2.2**

  - [ ]* 2.4 Viết property test cho version comparison antisymmetry
    - **Property 2: Version comparison antisymmetry**
    - Generate random version pairs, verify `sign(CompareVersions(v1,v2)) == -sign(CompareVersions(v2,v1))`
    - **Validates: Requirements 2.3**

- [x] 3. Checkpoint - Đảm bảo tất cả tests pass
  - Đảm bảo tất cả tests pass, hỏi người dùng nếu có thắc mắc.

- [x] 4. Cập nhật AntigravityDbService cho dual-format inject
  - [x] 4.1 Cập nhật `src/AntiBridge.Core/Services/AntigravityDbService.cs` — thêm logic inject New_Format
    - Thêm constant `NewOAuthKey = "antigravityUnifiedStateSync.oauthToken"`
    - Thêm dependency `AntigravityVersionService` (tạo instance trong constructor)
    - Thêm private method `InjectNewFormat(string dbPath, TokenData token)`:
      - Tạo OAuthTokenInfo bằng `ProtobufHelper.CreateOAuthInfo()`
      - Base64 encode OAuthTokenInfo
      - Tạo InnerMessage2: `EncodeStringField(1, base64OAuthInfo)`
      - Tạo InnerMessage: `EncodeStringField(1, "oauthTokenInfoSentinelKey")` + `EncodeLenDelimField(2, innerMessage2)`
      - Tạo OuterMessage: `EncodeLenDelimField(1, innerMessage)`
      - Base64 encode OuterMessage
      - SQL: `INSERT OR REPLACE INTO ItemTable (key, value) VALUES (@key, @value)` với key = NewOAuthKey
      - Ghi onboarding flag
    - _Requirements: 3.1, 3.2, 3.3, 3.4_

  - [x] 4.2 Cập nhật method `InjectTokenToAntigravity` với version branching
    - Gọi `_versionService.GetAntigravityVersion()`
    - Nếu version != null và IsNewFormat → gọi `InjectNewFormat()`
    - Nếu version != null và !IsNewFormat → gọi `InjectOldFormat()` (logic hiện tại, extract thành private method)
    - Nếu version == null → thử cả hai, thành công nếu ít nhất một thành công
    - _Requirements: 4.1, 4.2, 5.1, 5.2, 5.3_

  - [ ]* 4.3 Viết property test cho Old_Format field manipulation
    - **Property 5: Old_Format inject field manipulation**
    - Generate random protobuf blobs chứa Field 1, 2, 6, verify sau khi remove + add Field 6 mới thì kết quả không chứa Field 1, 2 và chứa đúng Field 6 mới
    - **Validates: Requirements 4.2**

- [x] 5. Cập nhật AntigravityDbService cho dual-format read
  - [x] 5.1 Cập nhật method `ReadTokenFromAntigravity` trong `src/AntiBridge.Core/Services/AntigravityDbService.cs`
    - Thêm private method `ReadNewFormat(SqliteConnection connection)`:
      - Đọc value từ key NewOAuthKey
      - Base64 decode → parse OuterMessage → lấy InnerMessage (Field 1) → lấy InnerMessage2 (Field 2) → lấy base64 string (Field 1) → base64 decode → parse OAuthTokenInfo → extract access_token (Field 1) và refresh_token (Field 3)
    - Extract logic đọc hiện tại thành `ReadOldFormat(SqliteConnection connection)`
    - Cập nhật `ReadTokenFromAntigravity()`: thử `ReadNewFormat()` trước, nếu null thì thử `ReadOldFormat()`
    - _Requirements: 6.1, 6.2, 6.3, 6.4_

  - [ ]* 5.2 Viết property test cho New_Format encode/decode round-trip
    - **Property 3: New_Format encode/decode round-trip**
    - Generate random (access_token, refresh_token, expiry), encode theo New_Format structure, decode ngược lại, verify trả về đúng token gốc
    - **Validates: Requirements 3.2, 6.3**

- [x] 6. Cập nhật MainViewModel nếu cần
  - [x] 6.1 Kiểm tra và cập nhật `src/AntiBridge.Avalonia/ViewModels/MainViewModel.cs`
    - Nếu `AntigravityDbService` constructor thay đổi (thêm dependency), cập nhật nơi khởi tạo trong MainViewModel
    - Đảm bảo `SyncToAntigravity`, `SyncAccountToAntigravity`, và `ImportFromAntigravityAsync` vẫn hoạt động đúng
    - _Requirements: 3.1, 4.1, 6.1_

- [x] 7. Final checkpoint - Đảm bảo tất cả tests pass
  - Đảm bảo tất cả tests pass, hỏi người dùng nếu có thắc mắc.

## Ghi chú

- Tasks đánh dấu `*` là optional, có thể bỏ qua cho MVP nhanh hơn
- Mỗi task reference requirements cụ thể để truy vết
- Checkpoints đảm bảo validation tăng dần
- Property tests validate correctness properties phổ quát
- Unit tests validate examples cụ thể và edge cases
