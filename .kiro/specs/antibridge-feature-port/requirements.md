# Requirements Document

## Introduction

This document specifies the requirements for porting key features from Antigravity-Manager (Tauri/Rust) to AntiBridge (C#/.NET). The port includes device fingerprint isolation, API proxy protocol fixes, model routing, traffic monitoring, token statistics, and context compression. These features will enhance AntiBridge's capabilities to match the mature Antigravity-Manager implementation while maintaining C# best practices and thread-safety.

## Glossary

- **AntiBridge**: The target C#/.NET application using Avalonia UI that provides API proxy functionality
- **Antigravity_Manager**: The source Tauri/Rust application with mature feature implementations
- **Device_Profile**: A set of unique identifiers (machine_id, mac_machine_id, dev_device_id, sqm_id) used to isolate account fingerprints
- **Model_Router**: A system that maps incoming model names to target model names using exact match, wildcard patterns, and defaults
- **Context_Manager**: A component that estimates token usage and applies progressive compression to prevent context overflow
- **Traffic_Monitor**: A system that logs all API requests/responses with metadata for debugging and analysis
- **Token_Stats_Service**: A service that tracks and aggregates token usage statistics with SQLite persistence
- **Protocol_Translator**: Components that convert between Claude/OpenAI API formats and the internal Antigravity format
- **Signature_Cache**: A cache that stores thinking block signatures for reuse across requests

## Requirements

### Requirement 1: Device Fingerprint Isolation

**User Story:** As a user with multiple accounts, I want each account to have its own unique device fingerprint, so that I can avoid detection when switching between accounts.

#### Acceptance Criteria

1. THE Account model SHALL include an optional DeviceProfile property containing machine_id, mac_machine_id, dev_device_id, and sqm_id fields
2. THE Account model SHALL include a DeviceHistory list to store previous device profile versions
3. WHEN a device profile is created, THE System SHALL generate a unique version ID and timestamp
4. WHEN an account is used for API requests, THE System SHALL apply the account's device profile if present
5. THE DeviceProfile SHALL be serializable to JSON for persistence
6. WHEN no device profile is set, THE System SHALL use the system's default device identifiers

### Requirement 2: Claude Protocol Translator Fixes

**User Story:** As a developer using Claude API format, I want the protocol translator to correctly handle all message types, so that my requests work reliably.

#### Acceptance Criteria

1. WHEN processing thinking blocks, THE ClaudeToAntigravityRequest translator SHALL correctly extract and format thinking text and signatures
2. WHEN processing tool_use blocks, THE ClaudeToAntigravityRequest translator SHALL correctly map tool calls with proper ID and argument handling
3. WHEN processing tool_result blocks, THE ClaudeToAntigravityRequest translator SHALL correctly format function responses
4. WHEN model role messages contain multiple content types, THE ClaudeToAntigravityRequest translator SHALL reorder parts so thinking blocks come first
5. WHEN both tools and thinking are enabled, THE ClaudeToAntigravityRequest translator SHALL inject the interleaved thinking hint into system instructions
6. WHEN streaming responses, THE AntigravityToClaudeResponse translator SHALL correctly emit message_start, content_block_start, content_block_delta, content_block_stop, and message_stop events
7. WHEN a thinking signature is received, THE AntigravityToClaudeResponse translator SHALL emit a signature_delta event with the model group prefix
8. WHEN processing function calls in responses, THE AntigravityToClaudeResponse translator SHALL generate unique tool_use IDs and correctly format input JSON

### Requirement 3: OpenAI Protocol Translator Fixes

**User Story:** As a developer using OpenAI API format, I want the protocol translator to correctly handle all message types including reasoning content, so that my requests work reliably.

#### Acceptance Criteria

1. WHEN processing messages with reasoning_effort parameter, THE OpenAIToAntigravityRequest translator SHALL correctly configure thinking settings
2. WHEN processing tool_calls in assistant messages, THE OpenAIToAntigravityRequest translator SHALL correctly map function calls and track IDs for response matching
3. WHEN processing tool role messages, THE OpenAIToAntigravityRequest translator SHALL correctly format function responses with proper ID matching
4. WHEN streaming responses with thought content, THE AntigravityToOpenAIResponse translator SHALL emit reasoning_content in the delta
5. WHEN streaming responses with function calls, THE AntigravityToOpenAIResponse translator SHALL correctly format tool_calls array with unique IDs
6. WHEN processing inline image data, THE AntigravityToOpenAIResponse translator SHALL correctly format image URLs with base64 data

### Requirement 4: Model Router

**User Story:** As a user, I want to configure custom model name mappings with wildcard support, so that I can route requests to appropriate models based on flexible patterns.

#### Acceptance Criteria

1. WHEN an exact match exists in custom mappings, THE Model_Router SHALL use the exact match with highest priority
2. WHEN no exact match exists but wildcard patterns match, THE Model_Router SHALL select the most specific pattern based on non-wildcard character count
3. WHEN multiple wildcard patterns have equal specificity, THE Model_Router SHALL use the first matching pattern
4. WHEN no custom mapping matches, THE Model_Router SHALL fall back to system default mappings
5. THE Model_Router SHALL support multiple wildcards in a single pattern (e.g., "claude-*-sonnet-*")
6. THE Model_Router SHALL perform case-sensitive matching for all patterns
7. THE Model_Router SHALL be thread-safe for concurrent access to custom mappings

### Requirement 5: Traffic Monitor

**User Story:** As a developer, I want to monitor all API traffic through the proxy, so that I can debug issues and analyze usage patterns.

#### Acceptance Criteria

1. WHEN a request is processed, THE Traffic_Monitor SHALL log the request ID, timestamp, method, URL, status, and duration
2. WHEN a request includes model information, THE Traffic_Monitor SHALL log both the original model name and the mapped model name
3. WHEN a request is associated with an account, THE Traffic_Monitor SHALL log the account email
4. WHEN a request fails, THE Traffic_Monitor SHALL log the error message
5. WHEN streaming responses complete, THE Traffic_Monitor SHALL aggregate and log the total input and output tokens
6. THE Traffic_Monitor SHALL support enabling/disabling logging at runtime
7. THE Traffic_Monitor SHALL maintain a configurable maximum log count in memory
8. THE Traffic_Monitor SHALL persist logs to SQLite for historical queries
9. THE Traffic_Monitor SHALL auto-cleanup logs older than a configurable retention period
10. WHEN the protocol is detected, THE Traffic_Monitor SHALL log the protocol type (openai, anthropic, gemini)

### Requirement 6: Token Statistics Service

**User Story:** As a user, I want to track my token usage over time with breakdowns by account and model, so that I can monitor costs and optimize usage.

#### Acceptance Criteria

1. WHEN a request completes, THE Token_Stats_Service SHALL record the account email, model, input tokens, and output tokens
2. THE Token_Stats_Service SHALL aggregate statistics by hour for efficient querying
3. THE Token_Stats_Service SHALL provide hourly aggregated statistics for a configurable time range
4. THE Token_Stats_Service SHALL provide daily aggregated statistics for a configurable time range
5. THE Token_Stats_Service SHALL provide weekly aggregated statistics for a configurable time range
6. THE Token_Stats_Service SHALL provide per-account statistics for a configurable time range
7. THE Token_Stats_Service SHALL provide per-model statistics for a configurable time range
8. THE Token_Stats_Service SHALL provide summary statistics including total tokens, total requests, and unique accounts
9. THE Token_Stats_Service SHALL persist all data to SQLite with WAL mode for concurrent access
10. THE Token_Stats_Service SHALL be thread-safe for concurrent recording and querying

### Requirement 7: Context Compression (3-Layer Progressive)

**User Story:** As a user with long conversations, I want the system to automatically compress context when approaching limits, so that my conversations can continue without manual intervention.

#### Acceptance Criteria

1. THE Context_Manager SHALL estimate token usage with multi-language awareness (ASCII ~4 chars/token, Unicode/CJK ~1.5 chars/token)
2. THE Context_Manager SHALL add a 15% safety margin to token estimates
3. WHEN context pressure exceeds Layer 1 threshold (60%), THE Context_Manager SHALL trim old tool message pairs while keeping the last N rounds
4. WHEN context pressure exceeds Layer 2 threshold (75%), THE Context_Manager SHALL compress thinking content to "..." while preserving signatures
5. WHEN context pressure exceeds Layer 3 threshold (90%), THE Context_Manager SHALL extract the last valid signature for potential fork operations
6. THE Context_Manager SHALL protect the last N messages from compression (configurable, default 4)
7. WHEN purifying history, THE Context_Manager SHALL support both Soft (protect last 4 messages) and Aggressive (no protection) strategies
8. THE Context_Manager SHALL correctly identify tool rounds consisting of assistant tool_use followed by user tool_result messages
9. WHEN compressing thinking blocks, THE Context_Manager SHALL only compress blocks that have valid signatures

### Requirement 8: Integration and Thread Safety

**User Story:** As a developer, I want all new features to integrate seamlessly with existing AntiBridge code, so that the system remains stable and performant.

#### Acceptance Criteria

1. WHEN multiple requests are processed concurrently, THE System SHALL maintain thread-safety for all shared state
2. THE Model_Router custom mappings SHALL use thread-safe collections (ConcurrentDictionary or ReaderWriterLockSlim)
3. THE Traffic_Monitor logs SHALL use thread-safe collections for concurrent access
4. THE Token_Stats_Service SHALL use proper locking for SQLite write operations
5. THE Signature_Cache SHALL be thread-safe for concurrent get/set operations
6. WHEN the proxy server handles concurrent requests, THE System SHALL not experience race conditions or data corruption
7. THE System SHALL properly dispose of resources when shutting down
