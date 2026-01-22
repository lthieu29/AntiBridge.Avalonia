# Implementation Plan: AntiBridge Advanced Features

## Tổng quan

Implementation plan cho các tính năng nâng cao của AntiBridge. Các task được sắp xếp theo thứ tự ưu tiên và dependencies.

## Tasks

- [x] 1. Implement Signature Cache (Ưu tiên cao)
  - [x] 1.1 Tạo SignatureCacheEntry model và ISignatureCache interface
    - Tạo file `Models/SignatureCacheEntry.cs`
    - Tạo file `Services/ISignatureCache.cs` với interface và options
    - _Requirements: 1.1, 1.5, 1.6, 1.7_
  
  - [x] 1.2 Implement SignatureCache class
    - Tạo file `Services/SignatureCache.cs`
    - Implement SHA256 hashing cho cache key
    - Implement TTL-based expiration với background cleanup
    - Implement LRU eviction khi cache full
    - _Requirements: 1.1, 1.6, 1.7_
  
  - [x] 1.3 Write property test cho Signature Cache Round-Trip
    - **Property 1: Signature Cache Round-Trip**
    - **Validates: Requirements 1.1, 1.7**
  
  - [x] 1.4 Write property test cho Cache TTL Expiration
    - **Property 4: Cache TTL Expiration**
    - **Validates: Requirements 1.6**
  
  - [x] 1.5 Integrate SignatureCache vào ClaudeToAntigravityRequest
    - Modify `ClaudeToAntigravityRequest.cs` để lookup signature từ cache
    - Implement cache hit/miss logic với fallback
    - _Requirements: 1.2, 1.3, 1.4_
  
  - [x] 1.6 Integrate SignatureCache vào AntigravityToClaudeResponse
    - Modify `AntigravityToClaudeResponse.cs` để store signature vào cache
    - Extract thinking text và signature từ response
    - _Requirements: 1.1_
  
  - [x] 1.7 Write property test cho Cache Lookup Priority
    - **Property 2: Cache Lookup Priority**
    - **Validates: Requirements 1.2, 1.3, 1.4**
  
  - [x] 1.8 Write property test cho Signature Validation
    - **Property 3: Signature Validation**
    - **Validates: Requirements 1.5**

- [x] 2. Checkpoint - Signature Cache
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Implement Retry on 401 (Ưu tiên cao)
  - [x] 3.1 Tạo RetryHandler helper class
    - Tạo file `Services/RetryHandler.cs`
    - Implement retry logic với configurable max retries
    - _Requirements: 2.5_
  
  - [x] 3.2 Modify AntigravityExecutor để handle 401 với retry
    - Update `ExecuteStreamAsync` để detect 401 và trigger refresh
    - Update `ExecuteAsync` để detect 401 và trigger refresh
    - Implement single retry after token refresh
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5_
  
  - [x] 3.3 Write property test cho 401 Retry with Token Refresh
    - **Property 5: 401 Retry with Token Refresh**
    - **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5**

- [x] 4. Implement Parts Reordering (Ưu tiên trung bình)
  - [x] 4.1 Implement ReorderParts method trong ClaudeToAntigravityRequest
    - Add static method `ReorderParts(JsonArray parts)`
    - Implement stable partition: thinking blocks first
    - Preserve relative order within each group
    - _Requirements: 3.1, 3.2, 3.3_
  
  - [x] 4.2 Integrate ReorderParts vào ProcessMessages
    - Call ReorderParts khi role là "model"
    - Skip reordering cho role "user"
    - _Requirements: 3.1_
  
  - [x] 4.3 Write property test cho Parts Stable Partitioning
    - **Property 6: Parts Stable Partitioning**
    - **Validates: Requirements 3.1, 3.2, 3.3**

- [x] 5. Implement Interleaved Thinking Hint (Ưu tiên trung bình)
  - [x] 5.1 Implement ShouldInjectThinkingHint check
    - Add method để check tools và thinking enabled
    - _Requirements: 4.1, 4.3_
  
  - [x] 5.2 Implement InjectThinkingHint method
    - Add hint text constant
    - Inject hint vào cuối system instruction parts
    - _Requirements: 4.2, 4.4_
  
  - [x] 5.3 Integrate hint injection vào Convert method
    - Call injection sau ProcessSystemInstruction
    - _Requirements: 4.1_
  
  - [x] 5.4 Write property test cho Interleaved Thinking Hint Injection
    - **Property 7: Interleaved Thinking Hint Injection**
    - **Validates: Requirements 4.1, 4.3, 4.4**

- [x] 6. Checkpoint - Core Features
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Implement Multi-Account Load Balancing (Ưu tiên thấp)
  - [x] 7.1 Tạo AccountRateLimitInfo model và ILoadBalancer interface
    - Tạo file `Models/AccountRateLimitInfo.cs`
    - Tạo file `Services/ILoadBalancer.cs` với interface, strategy enum, options
    - _Requirements: 5.1, 5.6_
  
  - [x] 7.2 Implement LoadBalancer class
    - Tạo file `Services/LoadBalancer.cs`
    - Implement round-robin distribution
    - Implement fill-first distribution
    - Implement rate limit tracking với expiry
    - _Requirements: 5.2, 5.6, 5.7_
  
  - [x] 7.3 Write property test cho Load Balancer Distribution
    - **Property 8: Load Balancer Distribution**
    - **Validates: Requirements 5.1, 5.2**
  
  - [x] 7.4 Integrate LoadBalancer vào AntigravityExecutor
    - Inject ILoadBalancer dependency
    - Use LoadBalancer để select account
    - Mark account rate limited on 429
    - Mark account quota exceeded on quota error
    - _Requirements: 5.3, 5.4_
  
  - [x] 7.5 Write property test cho Load Balancer Failover
    - **Property 9: Load Balancer Failover**
    - **Validates: Requirements 5.3, 5.4, 5.5**
  
  - [x] 7.6 Write property test cho Load Balancer Recovery
    - **Property 10: Load Balancer Recovery**
    - **Validates: Requirements 5.6, 5.7**

- [x] 8. Integration và Wiring
  - [x] 8.1 Update ProxyServer để inject dependencies
    - Inject SignatureCache vào translators
    - Inject LoadBalancer vào executor
    - _Requirements: 1.1-1.7, 5.1-5.7_
  
  - [x] 8.2 Update ProxyConfig với new options
    - Add SignatureCacheOptions
    - Add LoadBalancerOptions
    - Add RetryOptions
    - _Requirements: 1.6, 2.5, 5.2_

- [x] 9. Final Checkpoint
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tất cả tasks đều required để đảm bảo comprehensive testing từ đầu
- Mỗi task reference specific requirements để traceability
- Checkpoints đảm bảo incremental validation
- Property tests validate universal correctness properties
- Unit tests validate specific examples và edge cases
