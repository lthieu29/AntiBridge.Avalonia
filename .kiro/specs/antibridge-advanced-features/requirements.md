# Requirements Document

## Giới thiệu

Tài liệu này mô tả các yêu cầu cho việc bổ sung các tính năng nâng cao vào AntiBridge proxy server. Mục tiêu là đảm bảo tương thích hoàn toàn với CLIProxyAPI và hoạt động ổn định với Claude Code và OpenCode.

## Thuật ngữ

- **AntiBridge**: Proxy server chuyển đổi Claude/OpenAI API requests sang Antigravity API format
- **Antigravity_API**: Google's internal API cho AI coding assistant
- **Signature_Cache**: Bộ nhớ đệm lưu trữ mapping giữa thinking text và thought signature
- **Thought_Signature**: Chữ ký xác thực cho thinking blocks từ Antigravity API
- **Account_Manager**: Module quản lý nhiều tài khoản và phân phối request
- **Load_Balancer**: Cơ chế phân phối request giữa các tài khoản

## Yêu cầu

### Yêu cầu 1: Signature Caching

**User Story:** Là một developer, tôi muốn hệ thống cache thought signatures, để tránh lỗi signature validation khi gửi thinking blocks trong các request tiếp theo.

#### Acceptance Criteria

1. WHEN Antigravity_API trả về response chứa thoughtSignature, THE Signature_Cache SHALL lưu mapping từ thinking text hash sang signature
2. WHEN ClaudeToAntigravityRequest xử lý thinking block có signature từ client, THE Signature_Cache SHALL tìm kiếm signature trong cache trước
3. IF cache hit xảy ra, THEN THE Signature_Cache SHALL sử dụng cached signature thay vì client signature
4. IF cache miss xảy ra, THEN THE Signature_Cache SHALL fallback về client signature
5. WHEN signature được lấy từ cache hoặc client, THE Signature_Cache SHALL validate format trước khi sử dụng
6. WHILE cache đang hoạt động, THE Signature_Cache SHALL tự động xóa entries cũ hơn TTL (mặc định 1 giờ)
7. THE Signature_Cache SHALL sử dụng SHA256 hash của thinking text làm cache key

### Yêu cầu 2: Retry on 401 Unauthorized

**User Story:** Là một developer, tôi muốn hệ thống tự động refresh token khi gặp 401, để không bị gián đoạn khi token hết hạn.

#### Acceptance Criteria

1. WHEN Antigravity_API trả về HTTP 401 Unauthorized, THE AntigravityExecutor SHALL tự động gọi refresh token
2. WHEN refresh token thành công, THE AntigravityExecutor SHALL retry request ban đầu một lần
3. IF retry vẫn trả về 401, THEN THE AntigravityExecutor SHALL trả lỗi authentication về client
4. WHEN refresh token thất bại, THE AntigravityExecutor SHALL trả lỗi authentication về client ngay lập tức
5. THE AntigravityExecutor SHALL chỉ retry tối đa 1 lần cho mỗi request

### Yêu cầu 3: Parts Reordering

**User Story:** Là một developer, tôi muốn thinking blocks luôn được đặt ở đầu message, để đảm bảo tương thích với Antigravity API format.

#### Acceptance Criteria

1. WHEN ClaudeToAntigravityRequest xử lý message có role "model", THE ClaudeToAntigravityRequest SHALL sắp xếp parts với thinking blocks ở đầu
2. WHEN sắp xếp parts, THE ClaudeToAntigravityRequest SHALL giữ nguyên thứ tự tương đối của các thinking blocks với nhau
3. WHEN sắp xếp parts, THE ClaudeToAntigravityRequest SHALL giữ nguyên thứ tự tương đối của các non-thinking parts với nhau
4. IF message không có thinking blocks, THEN THE ClaudeToAntigravityRequest SHALL giữ nguyên thứ tự parts

### Yêu cầu 4: Interleaved Thinking Hint

**User Story:** Là một developer, tôi muốn hệ thống tự động inject hint khi sử dụng tools với thinking, để model biết có thể thinking giữa các tool calls.

#### Acceptance Criteria

1. WHEN request có cả tools và thinking enabled, THE ClaudeToAntigravityRequest SHALL inject interleaved thinking hint vào system instruction
2. THE ClaudeToAntigravityRequest SHALL inject hint với nội dung: "Interleaved thinking is enabled. You may think between tool calls to reflect on tool outputs before proceeding."
3. WHEN request không có tools hoặc không có thinking enabled, THE ClaudeToAntigravityRequest SHALL không inject hint
4. WHEN inject hint, THE ClaudeToAntigravityRequest SHALL đặt hint ở cuối system instruction parts

### Yêu cầu 5: Multi-Account Load Balancing

**User Story:** Là một developer, tôi muốn hệ thống hỗ trợ nhiều tài khoản với load balancing, để tăng throughput và tự động failover khi gặp rate limit.

#### Acceptance Criteria

1. THE Account_Manager SHALL hỗ trợ cấu hình nhiều tài khoản Antigravity
2. WHEN có nhiều tài khoản, THE Load_Balancer SHALL phân phối request theo chiến lược round-robin hoặc fill-first
3. WHEN Antigravity_API trả về HTTP 429 Too Many Requests, THE Load_Balancer SHALL tự động chuyển sang tài khoản khác
4. WHEN Antigravity_API trả về lỗi quota exceeded, THE Load_Balancer SHALL tự động chuyển sang tài khoản khác
5. IF tất cả tài khoản đều bị rate limit, THEN THE Load_Balancer SHALL trả lỗi 429 về client
6. THE Account_Manager SHALL theo dõi trạng thái rate limit của từng tài khoản
7. WHEN tài khoản hết rate limit period, THE Load_Balancer SHALL đưa tài khoản trở lại pool
