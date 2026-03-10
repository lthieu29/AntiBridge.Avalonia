---
inclusion: manual
---

# Antigravity Token Format - Hướng dẫn phân tích & cập nhật

## Mục đích

File này cung cấp context cho Kiro khi cần phân tích hoặc cập nhật format token của Antigravity IDE.
Khi Antigravity thay đổi format, người dùng chạy script diagnostic rồi paste output vào chat.
Kiro sẽ dùng file này để so sánh format cũ vs mới và đề xuất thay đổi code.

## Cách sử dụng

1. Chạy diagnostic script: `powershell -ExecutionPolicy Bypass -File scripts/diagnose-antigravity.ps1`
2. Mở file report trong thư mục `antigravity-diagnostic/antigravity-v{version}_{timestamp}/report.md`
3. Paste nội dung report vào Kiro chat kèm `#antigravity-format-guide` để load context này

## Format hiện tại đã biết

### Old Format (Antigravity < 1.16.5)

- Database key: `jetskiStateSync.agentManagerInitState`
- SQL đọc: `SELECT value FROM ItemTable WHERE key = 'jetskiStateSync.agentManagerInitState'`
- SQL ghi: `UPDATE ItemTable SET value = @value WHERE key = 'jetskiStateSync.agentManagerInitState'`
- Yêu cầu: key phải tồn tại sẵn (user đã login Antigravity trước)

Cấu trúc:
```
value = base64(StateBlob)

StateBlob (protobuf):
  Field 1 (string): user_id        ← bị XÓA khi inject
  Field 2 (string): email          ← bị XÓA khi inject  
  Field 3+ (...): other fields     ← giữ nguyên
  Field 6 (len-delim): OAuthTokenInfo  ← bị XÓA rồi THÊM MỚI
```

Khi inject Old Format:
1. Đọc state hiện tại từ DB
2. Base64 decode
3. Remove Field 1 (user_id), Field 2 (email), Field 6 (old OAuth)
4. Tạo Field 6 mới = OAuthTokenInfo
5. Nối vào clean data, base64 encode, UPDATE lại DB

### New Format (Antigravity >= 1.16.5)

- Database key: `antigravityUnifiedStateSync.oauthToken`
- SQL ghi: `INSERT OR REPLACE INTO ItemTable (key, value) VALUES (@key, @value)`
- Không cần state có sẵn, tạo mới hoàn toàn

Cấu trúc:
```
value = base64(OuterMessage)

OuterMessage (protobuf):
  Field 1 (len-delim): InnerMessage
    Field 1 (string): "oauthTokenInfoSentinelKey"    ← sentinel key cố định
    Field 2 (len-delim): InnerMessage2
      Field 1 (string): base64(OAuthTokenInfo)        ← OAuthTokenInfo được base64 encode
```

Khi inject New Format:
1. Tạo OAuthTokenInfo protobuf bytes
2. Base64 encode OAuthTokenInfo → base64String
3. InnerMessage2 = EncodeStringField(1, base64String)
4. InnerMessage = EncodeStringField(1, "oauthTokenInfoSentinelKey") + EncodeLenDelimField(2, InnerMessage2)
5. OuterMessage = EncodeLenDelimField(1, InnerMessage)
6. Base64 encode OuterMessage → INSERT OR REPLACE vào DB

### OAuthTokenInfo (chung cho cả 2 format)

```
OAuthTokenInfo (protobuf message):
  Field 1 (string): access_token
  Field 2 (string): "Bearer"           ← luôn là "Bearer"
  Field 3 (string): refresh_token
  Field 4 (len-delim): Timestamp
    Field 1 (varint): unix_seconds      ← expiry timestamp
```

### Onboarding Flag

Cả 2 format đều ghi thêm:
- Key: `antigravityOnboarding`
- Value: `"true"`
- SQL: `INSERT OR REPLACE INTO ItemTable (key, value) VALUES ('antigravityOnboarding', 'true')`

## Cách đọc token (Import)

Thứ tự ưu tiên:
1. Thử đọc New Format key trước (`antigravityUnifiedStateSync.oauthToken`)
2. Nếu không có → thử Old Format key (`jetskiStateSync.agentManagerInitState`)
3. Nếu cả 2 đều không có → báo lỗi

## Cách ghi token (Sync)

Phân nhánh theo version:
1. Detect version Antigravity (PowerShell VersionInfo / Info.plist / --version)
2. Version >= 1.16.5 → ghi New Format
3. Version < 1.16.5 → ghi Old Format
4. Không detect được version → thử cả 2, thành công nếu ít nhất 1 thành công

## Protobuf Wire Format Reference

```
Wire Type 0: Varint (int32, int64, uint32, uint64, sint32, sint64, bool, enum)
Wire Type 2: Length-delimited (string, bytes, embedded messages)

Tag = (field_number << 3) | wire_type

Varint encoding: 7 bits per byte, MSB = continuation bit
Length-delimited: tag + varint(length) + data
```

## Checklist khi phân tích format mới

Khi nhận được diagnostic report từ user, kiểm tra theo thứ tự:

### 1. So sánh danh sách keys
- [ ] Có key mới nào xuất hiện không? (so với danh sách keys đã biết ở trên)
- [ ] Key cũ có bị xóa/đổi tên không?
- [ ] Có key nào chứa "oauth", "token", "state", "sync" mà chưa biết không?

### 2. So sánh cấu trúc protobuf
- [ ] Cấu trúc lồng nhau có thay đổi không? (số lượng layer, field numbers)
- [ ] Sentinel key có đổi không? (hiện tại: "oauthTokenInfoSentinelKey")
- [ ] OAuthTokenInfo có thêm/bớt field không? (hiện tại: 4 fields)
- [ ] Có field mới nào trong OuterMessage/InnerMessage không?

### 3. So sánh encoding
- [ ] Vẫn dùng base64 standard không? (hay đổi sang base64url, hex, ...)
- [ ] Vẫn dùng protobuf không? (hay đổi sang JSON, MessagePack, ...)
- [ ] Wire types có thay đổi không?

### 4. Xác định phạm vi thay đổi code
- [ ] Chỉ cần thêm format mới (giống lần này)?
- [ ] Cần sửa format hiện tại?
- [ ] Cần thay đổi version threshold?
- [ ] Cần thay đổi logic fallback?

## Files liên quan trong project

```
src/AntiBridge.Core/Services/
├── ProtobufHelper.cs           ← Encode/decode protobuf
├── AntigravityVersionService.cs ← Detect version, compare versions
├── AntigravityDbService.cs      ← Read/write token (InjectNewFormat, InjectOldFormat, ReadNewFormat, ReadOldFormat)
└── AntigravityProcessService.cs ← Find Antigravity executable path

scripts/
└── diagnose-antigravity.ps1     ← Diagnostic tool

.kiro/steering/
└── antigravity-format-guide.md  ← File này
```

## Lịch sử thay đổi format

| Version | Thay đổi | Key | Ngày phát hiện |
|---------|----------|-----|----------------|
| < 1.16.5 | Format gốc | `jetskiStateSync.agentManagerInitState` | Ban đầu |
| >= 1.16.5 | New format, key mới, cấu trúc lồng nhau | `antigravityUnifiedStateSync.oauthToken` | 2026-02 |
