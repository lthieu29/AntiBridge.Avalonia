# 🚀 AntiBridge

**Proxy Server & Account Manager cho Antigravity** - Sử dụng subscription Antigravity với Claude Code, OpenCode và các AI coding tools khác.

## ✨ Tính năng

| Tính năng | Mô tả |
|-----------|-------|
| 🌐 **Proxy Server** | HTTP proxy chuyển đổi Claude/OpenAI API sang Antigravity |
| 🔐 **Đăng nhập Google OAuth** | Đăng nhập một chạm bằng tài khoản Google |
| 📊 **Theo dõi Quota** | Xem quota Gemini 3 Pro và Claude 4.5 theo thời gian thực |
| 👥 **Multi-Account** | Quản lý nhiều tài khoản, chuyển đổi nhanh chóng |
| 🔄 **Auto-Refresh** | Tự động cập nhật quota mỗi 15 phút |
| 📤 **Sync to Antigravity** | Đồng bộ token và khởi động lại Antigravity IDE |
| 📥 **Import from Antigravity** | Import token từ Antigravity IDE đã cài đặt |

## 🎯 Hỗ trợ Clients

- **Claude Code** - Sử dụng với `ANTHROPIC_BASE_URL=http://127.0.0.1:8081`
- **OpenCode** - Sử dụng OpenAI-compatible endpoint
- **Cursor, Continue, Cline** - Bất kỳ client OpenAI-compatible

## 📋 Yêu cầu

- **.NET 8.0 SDK** (để build từ source)
- **Linux / Windows / macOS**
- **Antigravity subscription** (Google account)

## 🚀 Quick Start

### 1. Build & Run

```bash
git clone <repo-url>
cd AntiBridge.Avalonia
dotnet run --project src/AntiBridge.Avalonia
```

### 2. Add Account

Click **"+ Add Account"** để đăng nhập bằng Google account có Antigravity subscription.

### 3. Start Proxy

Click **"▶ Start"** trong Proxy Server panel. Default port: 8081.

### 4. Cấu hình Claude Code

```bash
# Set environment variables
export ANTHROPIC_BASE_URL=http://127.0.0.1:8081
export ANTHROPIC_API_KEY=dummy

# Run Claude Code
claude
```

### 5. Cấu hình OpenCode

Trong config OpenCode:
```yaml
provider:
  type: openai
  base_url: http://127.0.0.1:8081/v1
  api_key: dummy
```

## 🔌 API Endpoints

| Endpoint | Description |
|----------|-------------|
| `GET /v1/models` | List available models |
| `POST /v1/chat/completions` | OpenAI Chat Completions API |
| `POST /v1/messages` | Claude Messages API |
| `POST /v1/messages/count_tokens` | Claude Token Count API |

## 🤖 Available Models

Proxy expose tất cả models từ Antigravity subscription:
- `gemini-3-pro-high` - Gemini 3 Pro
- `claude-sonnet-4-20250514` - Claude 4.5 Sonnet
- `claude-opus-4-20250514` - Claude 4.5 Opus
- Và nhiều models khác...

## 📖 Hướng dẫn chi tiết

### Xem Quota

- Sau khi đăng nhập, quota sẽ tự động hiển thị
- **Gemini 3 Pro** và **Claude 4.5** hiển thị % còn lại
- Màu xanh = >50%, Vàng = 20-50%, Đỏ = <20%

### Proxy Logs

- Xem real-time logs của requests đến proxy
- Click **🗑️** để clear logs

### Sync to Antigravity

- Nhấn **📤** để đồng bộ token sang Antigravity IDE
- App sẽ tự động đóng và mở lại Antigravity

## 🏗️ Build & Publish

```bash
# Build
dotnet build

# Publish (Windows)
dotnet publish src/AntiBridge.Avalonia -c Release -r win-x64 --self-contained -o publish

# Publish (Linux)
dotnet publish src/AntiBridge.Avalonia -c Release -r linux-x64 --self-contained -o publish

# Publish (macOS)
dotnet publish src/AntiBridge.Avalonia -c Release -r osx-x64 --self-contained -o publish
```

## 📁 Cấu trúc dự án

```
src/
├── AntiBridge.Core/           # Business logic
│   ├── Models/                # Account, Token, Quota, ProxyConfig
│   ├── Services/              # OAuth, Quota, Proxy, Executor
│   └── Translator/            # Claude ↔ Antigravity, OpenAI ↔ Antigravity
├── AntiBridge.Avalonia/       # UI layer (Avalonia)
│   ├── ViewModels/            # MVVM ViewModels
│   └── Views/                 # AXAML views
└── AntiBridge.Tests/          # Unit tests
```

## 📂 Dữ liệu lưu trữ

Tài khoản được lưu tại:
- **Linux/macOS:** `~/.antibridge/`
- **Windows:** `%USERPROFILE%\.antibridge\`

## 🧪 Chạy Unit Tests

```bash
dotnet test
```

## 🙏 Credits

- Logic proxy được port từ [CLIProxyAPI](https://github.com/router-for-me/CLIProxyAPI)
- Account management từ [Antigravity-Manager](https://github.com/lbjlaq/Antigravity-Manager)

## 📄 License

MIT License
