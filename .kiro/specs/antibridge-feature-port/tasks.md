# Implementation Tasks

## Task 1: Device Fingerprint Isolation
- [x] 1.1 Create DeviceProfile model
  - [x] 1.1.1 Create `Models/DeviceProfile.cs` with Version, CreatedAt, MachineId, MacMachineId, DevDeviceId, SqmId properties
  - [x] 1.1.2 Implement `GenerateRandom()` static method for creating random fingerprints
  - [x] 1.1.3 Implement `GenerateRandomMac()` helper method
  - [x] 1.1.4 Add JSON serialization attributes
- [x] 1.2 Extend Account model
  - [x] 1.2.1 Add `DeviceProfile?` property to Account.cs
  - [x] 1.2.2 Add `DeviceHistory` list property to Account.cs
  - [x] 1.2.3 Implement `SetDeviceProfile()` method that archives old profile to history
- [x] 1.3 Write unit tests for DeviceProfile
  - [x] 1.3.1 Test GenerateRandom produces unique values
  - [x] 1.3.2 Test JSON serialization round-trip
  - [x] 1.3.3 Test SetDeviceProfile archives correctly

## Task 2: Model Router
- [x] 2.1 Create IModelRouter interface
  - [x] 2.1.1 Create `Services/IModelRouter.cs` with ResolveModel, SetCustomMapping, RemoveCustomMapping, GetCustomMappings methods
- [x] 2.2 Implement ModelRouter class
  - [x] 2.2.1 Create `Services/ModelRouter.cs` with ConcurrentDictionary for custom mappings
  - [x] 2.2.2 Implement system default mappings dictionary (Claude, OpenAI, Gemini)
  - [x] 2.2.3 Implement `ResolveModel()` with priority: exact → wildcard → system → passthrough → fallback
  - [x] 2.2.4 Implement `WildcardMatch()` supporting multiple wildcards
  - [x] 2.2.5 Implement `FindBestWildcardMatch()` with specificity calculation
- [x] 2.3 Write unit tests for ModelRouter
  - [x] 2.3.1 Test exact match priority over wildcard
  - [x] 2.3.2 Test wildcard specificity selection
  - [x] 2.3.3 Test multiple wildcards in pattern
  - [x] 2.3.4 Test case-sensitive matching
  - [x] 2.3.5 Test system default fallback
  - [x] 2.3.6 Test thread-safety with concurrent access

## Task 3: Traffic Monitor
- [x] 3.1 Create TrafficLog model
  - [x] 3.1.1 Create `Models/TrafficLog.cs` with Id, Timestamp, Method, Url, Status, DurationMs, Model, MappedModel, AccountEmail, Error, RequestBody, ResponseBody, InputTokens, OutputTokens, Protocol
- [x] 3.2 Create TrafficStats model
  - [x] 3.2.1 Create `Models/TrafficStats.cs` with TotalRequests, SuccessCount, ErrorCount
- [x] 3.3 Implement TrafficMonitor service
  - [x] 3.3.1 Create `Services/TrafficMonitor.cs` with ConcurrentQueue for in-memory logs
  - [x] 3.3.2 Implement SQLite database initialization with traffic_logs table
  - [x] 3.3.3 Implement `LogRequest()` method with async database persistence
  - [x] 3.3.4 Implement `GetLogs()` method with limit parameter
  - [x] 3.3.5 Implement `GetStats()` method with aggregate queries
  - [x] 3.3.6 Implement `Clear()` method
  - [x] 3.3.7 Implement `Enabled` property for runtime toggle
  - [x] 3.3.8 Add OnLogAdded event for UI notifications
- [x] 3.4 Write unit tests for TrafficMonitor
  - [x] 3.4.1 Test log persistence and retrieval
  - [x] 3.4.2 Test max log count enforcement
  - [x] 3.4.3 Test stats aggregation
  - [x] 3.4.4 Test enable/disable toggle

## Task 4: Token Statistics Service
- [x] 4.1 Create TokenStats models
  - [x] 4.1.1 Create `Models/HourlyTokenStats.cs` with Hour, AccountEmail, Model, InputTokens, OutputTokens, RequestCount
  - [x] 4.1.2 Create `Models/TokenStatsSummary.cs` with totals and ByModel/ByAccount dictionaries
- [x] 4.2 Implement TokenStatsService
  - [x] 4.2.1 Create `Services/TokenStatsService.cs` as singleton
  - [x] 4.2.2 Implement SQLite database initialization with WAL mode
  - [x] 4.2.3 Implement `RecordUsage()` with UPSERT aggregation and write lock
  - [x] 4.2.4 Implement `GetHourlyStats()` for time range queries
  - [x] 4.2.5 Implement `GetDailyStats()` with day aggregation
  - [x] 4.2.6 Implement `GetWeeklyStats()` with week aggregation
  - [x] 4.2.7 Implement `GetSummary()` with totals and breakdowns
- [x] 4.3 Write unit tests for TokenStatsService
  - [x] 4.3.1 Test usage recording and aggregation
  - [x] 4.3.2 Test hourly/daily/weekly queries
  - [x] 4.3.3 Test thread-safety with concurrent recording

## Task 5: Context Manager (3-Layer Compression)
- [x] 5.1 Implement token estimation
  - [x] 5.1.1 Create `Services/ContextManager.cs`
  - [x] 5.1.2 Implement `EstimateTokens()` with multi-language awareness (ASCII ~4 chars/token, Unicode ~1.5 chars/token)
  - [x] 5.1.3 Add 15% safety margin to estimates
  - [x] 5.1.4 Implement `EstimateRequestTokens()` for full request estimation
- [x] 5.2 Implement Layer 1: Tool Message Trimming
  - [x] 5.2.1 Implement `IdentifyToolRounds()` to find assistant tool_use + user tool_result pairs
  - [x] 5.2.2 Implement `TrimToolMessages()` keeping last N rounds
  - [x] 5.2.3 Implement reverse-order removal to preserve indices
- [x] 5.3 Implement Layer 2: Thinking Compression
  - [x] 5.3.1 Implement `CompressThinkingPreserveSignature()` replacing thinking text with "..."
  - [x] 5.3.2 Implement protected message range (last N messages)
  - [x] 5.3.3 Only compress blocks with valid signatures
- [x] 5.4 Implement Layer 3: Signature Extraction
  - [x] 5.4.1 Implement `ExtractLastValidSignature()` scanning from end
  - [x] 5.4.2 Validate signature length >= 50 characters
- [x] 5.5 Implement Progressive Compression
  - [x] 5.5.1 Implement `ApplyProgressiveCompression()` with configurable thresholds
  - [x] 5.5.2 Apply Layer 1 at 60% pressure
  - [x] 5.5.3 Apply Layer 2 at 75% pressure
  - [x] 5.5.4 Apply Layer 3 at 90% pressure
  - [x] 5.5.5 Recalculate pressure after each layer
- [x] 5.6 Write unit tests for ContextManager
  - [x] 5.6.1 Test token estimation accuracy
  - [x] 5.6.2 Test tool round identification
  - [x] 5.6.3 Test tool message trimming
  - [x] 5.6.4 Test thinking compression with signature preservation
  - [x] 5.6.5 Test signature extraction
  - [x] 5.6.6 Test progressive compression flow

## Task 6: Claude Protocol Translator Fixes
- [x] 6.1 Fix ClaudeToAntigravityRequest
  - [x] 6.1.1 Fix thinking block extraction with proper signature handling
  - [x] 6.1.2 Fix tool_use block mapping with ID preservation
  - [x] 6.1.3 Fix tool_result block formatting with function response structure
  - [x] 6.1.4 Implement parts reordering (thinking blocks first for model role)
  - [x] 6.1.5 Implement interleaved thinking hint injection when tools+thinking enabled
- [x] 6.2 Fix AntigravityToClaudeResponse
  - [x] 6.2.1 Fix SSE event emission sequence (message_start → content_block_start → delta → stop → message_stop)
  - [x] 6.2.2 Implement signature_delta event with model group prefix
  - [x] 6.2.3 Fix tool_use ID generation with unique IDs
  - [x] 6.2.4 Fix input JSON formatting for function calls
- [x] 6.3 Write tests for Claude translator fixes
  - [x] 6.3.1 Test thinking block round-trip
  - [x] 6.3.2 Test tool_use/tool_result mapping
  - [x] 6.3.3 Test parts reordering
  - [x] 6.3.4 Test streaming event sequence

## Task 7: OpenAI Protocol Translator Fixes
- [x] 7.1 Fix OpenAIToAntigravityRequest
  - [x] 7.1.1 Implement reasoning_effort parameter handling → thinking config
  - [x] 7.1.2 Fix tool_calls mapping with ID tracking
  - [x] 7.1.3 Fix tool role message formatting with ID matching
  - [x] 7.1.4 Fix function arguments parsing (string → JsonObject)
- [x] 7.2 Fix AntigravityToOpenAIResponse
  - [x] 7.2.1 Implement reasoning_content in streaming deltas
  - [x] 7.2.2 Fix tool_calls array formatting with unique IDs
  - [x] 7.2.3 Fix image URL handling with base64 data URI
  - [x] 7.2.4 Fix usage statistics in response
- [x] 7.3 Write tests for OpenAI translator fixes
  - [x] 7.3.1 Test reasoning_effort mapping
  - [x] 7.3.2 Test tool_calls round-trip
  - [x] 7.3.3 Test streaming with reasoning_content
  - [x] 7.3.4 Test image response handling

## Task 8: ProxyServer Integration
- [x] 8.1 Integrate ModelRouter
  - [x] 8.1.1 Add IModelRouter dependency to ProxyServer
  - [x] 8.1.2 Call ResolveModel() before executing requests
  - [x] 8.1.3 Log both original and mapped model names
- [x] 8.2 Integrate TrafficMonitor
  - [x] 8.2.1 Add TrafficMonitor dependency to ProxyServer
  - [x] 8.2.2 Create TrafficLog at request start
  - [x] 8.2.3 Update TrafficLog with response data
  - [x] 8.2.4 Call LogRequest() in finally block
  - [x] 8.2.5 Detect and log protocol type (openai/anthropic/gemini)
- [x] 8.3 Integrate ContextManager
  - [x] 8.3.1 Call ApplyProgressiveCompression() before request execution
  - [x] 8.3.2 Configure max tokens based on model (Flash: 1M, Pro: 2M)
  - [x] 8.3.3 Add X-Context-Purified header when compression applied
- [x] 8.4 Integrate DeviceProfile
  - [x] 8.4.1 Apply account's DeviceProfile to request headers when present
  - [x] 8.4.2 Fall back to system defaults when no profile set
- [x] 8.5 Write integration tests
  - [x] 8.5.1 Test full request flow with model routing
  - [x] 8.5.2 Test traffic logging end-to-end
  - [x] 8.5.3 Test context compression under pressure
  - [x] 8.5.4 Test device fingerprint application

## Task 9: Property-Based Tests
- [x] 9.1 ModelRouter properties
  - [x] 9.1.1 Property: Determinism - same input always produces same output
  - [x] 9.1.2 Property: Wildcard specificity - most specific pattern wins
- [x] 9.2 ContextManager properties
  - [x] 9.2.1 Property: Token estimation consistency
  - [x] 9.2.2 Property: Compression monotonicity - tokens never increase
  - [x] 9.2.3 Property: Signature preservation - no signatures lost in Layer 2
- [x] 9.3 Thread-safety properties
  - [x] 9.3.1 Property: Concurrent ModelRouter access doesn't corrupt state
  - [x] 9.3.2 Property: Concurrent TrafficMonitor logging doesn't lose data
  - [x] 9.3.3 Property: Concurrent TokenStatsService recording is accurate

## Task 10: Claude Translator Critical Fixes (Ported from Rust)
- [x] 10.1 Request preprocessing fixes
  - [x] 10.1.1 Implement `CleanCacheControlFromMessages()` to remove cache_control fields from all message blocks
  - [x] 10.1.2 Implement `MergeConsecutiveMessages()` to merge same-role messages (avoid role alternation errors)
  - [x] 10.1.3 Implement `SortThinkingBlocksFirst()` pre-processing for assistant messages before conversion
  - [x] 10.1.4 Implement `ShouldDisableThinkingDueToHistory()` to auto-disable thinking when tool_use exists without thinking
- [x] 10.2 Signature validation fixes
  - [x] 10.2.1 Add MIN_SIGNATURE_LENGTH constant (50 chars) for signature validation
  - [x] 10.2.2 Implement signature family tracking (cache signature with model family)
  - [x] 10.2.3 Implement `IsModelCompatible()` to check signature compatibility with target model
  - [x] 10.2.4 Implement `HasValidSignatureForFunctionCalls()` to validate signatures before enabling thinking
- [x] 10.3 Response processing fixes
  - [x] 10.3.1 Implement Base64 signature decoding in response (Gemini sends Base64, Claude expects raw)
  - [x] 10.3.2 Implement `RemapFunctionCallArgs()` for tool parameter remapping (description→pattern, query→pattern, paths→path)
  - [x] 10.3.3 Add special handling for grep, glob, read, ls tools parameter remapping
  - [x] 10.3.4 Add EnterPlanMode tool special handling (clear all args)
- [x] 10.4 Deep cleaning utilities
  - [x] 10.4.1 Implement `DeepCleanCacheControl()` recursive JSON cleaner
  - [x] 10.4.2 Implement `DeepCleanUndefined()` to remove "[undefined]" strings from JSON
- [x] 10.5 Write tests for critical fixes
  - [x] 10.5.1 Test cache_control cleaning
  - [x] 10.5.2 Test consecutive message merging
  - [x] 10.5.3 Test thinking block sorting
  - [x] 10.5.4 Test signature validation
  - [x] 10.5.5 Test Base64 signature decoding
  - [x] 10.5.6 Test function call args remapping

## Task 11: OpenAI Translator Critical Fixes (Ported from Rust)
- [x] 11.1 Request preprocessing fixes
  - [x] 11.1.1 Implement `DeepCleanUndefined()` call to remove "[undefined]" strings from request
  - [x] 11.1.2 Implement `MergeConsecutiveMessages()` to merge same-role messages (Gemini requires user/model alternation)
  - [x] 11.1.3 Implement `CleanCacheControlFromMessages()` to remove cache_control fields
- [x] 11.2 Thinking model support
  - [x] 11.2.1 Implement `IsThinkingModel()` detection (gemini-3-*-high/low/pro, *-thinking)
  - [x] 11.2.2 Implement `HasIncompatibleAssistantHistory()` to check for assistant messages without reasoning_content
  - [x] 11.2.3 Implement placeholder thinking block injection for model messages when thinking enabled
  - [x] 11.2.4 Handle `reasoning_content` field in request messages (convert to thought blocks)
  - [x] 11.2.5 Implement global thought signature storage/retrieval for reuse across requests
- [x] 11.3 Tool processing fixes
  - [x] 11.3.1 Implement `ToolNameToSchema` mapping for parameter type fixing
  - [x] 11.3.2 Implement `FixToolCallArgs()` using original schema to correct parameter types
  - [x] 11.3.3 Implement `CleanJsonSchema()` to remove invalid fields (format, strict, additionalProperties, definitions)
  - [x] 11.3.4 Implement `EnforceUppercaseTypes()` for Gemini type compatibility (object→OBJECT, string→STRING)
  - [x] 11.3.5 Inject default schema for tools without parameters
  - [x] 11.3.6 Handle `local_shell_call` → `shell` tool name remapping
- [x] 11.4 Content processing fixes
  - [x] 11.4.1 Handle `instructions` field (priority over system messages)
  - [x] 11.4.2 Implement local file path handling (file:// URLs → base64 inline data)
  - [x] 11.4.3 Add thoughtSignature to functionCall parts when thinking model
- [x] 11.5 Response processing fixes
  - [x] 11.5.1 Implement thought signature storage from response for reuse
  - [x] 11.5.2 Implement grounding metadata handling (webSearchQueries, groundingChunks → citations)
  - [x] 11.5.3 Implement `DecodeBase64Signature()` for signature handling
  - [x] 11.5.4 Implement `RemapFunctionCallArgs()` for tool parameter remapping (same as Claude)
- [x] 11.6 Write tests for OpenAI translator fixes
  - [x] 11.6.1 Test deep clean undefined
  - [x] 11.6.2 Test consecutive message merging
  - [x] 11.6.3 Test thinking model detection
  - [x] 11.6.4 Test placeholder thinking injection
  - [x] 11.6.5 Test tool schema cleaning and type enforcement
  - [x] 11.6.6 Test grounding metadata handling
  - [x] 11.6.7 Test function call args remapping

