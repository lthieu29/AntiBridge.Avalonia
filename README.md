# 🚀 AntiBridge

**Công cụ quản lý tài khoản Antigravity** - Theo dõi quota AI models, đồng bộ token, multi-account.

## ✨ Tính năng

| Tính năng                      | Mô tả                                                    |
| ------------------------------ | -------------------------------------------------------- |
| 🔐 **Đăng nhập Google OAuth**  | Đăng nhập một chạm bằng tài khoản Google                 |
| 📊 **Theo dõi Quota**          | Xem quota Gemini 3 Pro và Claude 4.5 theo thời gian thực |
| 👥 **Multi-Account**           | Quản lý nhiều tài khoản, chuyển đổi nhanh chóng          |
| 🔄 **Auto-Refresh**            | Tự động cập nhật quota mỗi 15 phút                       |
| 📤 **Sync to Antigravity**     | Đồng bộ token và khởi động lại Antigravity IDE           |
| 📥 **Import from Antigravity** | Import token từ Antigravity IDE đã cài đặt               |
| 🔒 **Phát hiện 403**           | Hiển thị badge đỏ khi tài khoản bị chặn                  |
| 💾 **Lưu phiên đăng nhập**     | Không cần đăng nhập lại mỗi khi mở app                   |

## 📋 Yêu cầu

- **.NET 8.0 SDK** (để build từ source)
- **Linux / Windows / macOS**

## 🚀 Cài đặt & Chạy

### Cách 1: Chạy từ source

```bash
git clone <repo-url>
cd AntiBridge.Avalonia
dotnet restore
dotnet run --project src/AntiBridge.Avalonia
```

### Cách 2: Build bản publish (self-contained)

```bash
# Linux
dotnet publish src/AntiBridge.Avalonia -c Release -r linux-x64 --self-contained true -o ./publish

# Windows
dotnet publish src/AntiBridge.Avalonia -c Release -r win-x64 --self-contained true -o ./publish

# Chạy bản publish
./publish/AntiBridge.Avalonia
```

## 📖 Hướng dẫn sử dụng

### 1. Đăng nhập

- Nhấn **"+ Add Account"** để đăng nhập bằng Google
- Hoặc nhấn **"📥 Import"** nếu đã cài Antigravity IDE

### 2. Xem Quota

- Sau khi đăng nhập, quota sẽ tự động hiển thị
- **Gemini 3 Pro** và **Claude 4.5** hiển thị % còn lại
- Màu xanh = >70%, Vàng = 30-70%, Đỏ = <30%

### 3. Refresh Quota

- Nhấn **"🔄 Refresh All"** để cập nhật quota tất cả tài khoản
- Auto-refresh mỗi 15 phút (khi có tài khoản)

### 4. Sync to Antigravity

- Nhấn **📤** để đồng bộ token sang Antigravity IDE
- App sẽ tự động đóng và mở lại Antigravity

### 5. Quản lý tài khoản

- **🔄** Refresh quota của tài khoản
- **📤** Sync to Antigravity
- **🗑️** Xóa tài khoản

### 6. Trạng thái 403

- Khi API trả về 403 Forbidden, badge **🔒 403** màu đỏ sẽ hiển thị
- Nghĩa là tài khoản không có quyền truy cập Gemini Code Assist

## ❓ Xử lý lỗi thường gặp

### Lỗi "Account not eligible for Gemini Code and Antigravity"

Lỗi này xuất hiện khi Antigravity IDE sử dụng token cũ hoặc không hợp lệ. **AntiBridge giúp bạn sửa lỗi này chỉ với 3 bước:**

1. **Mở AntiBridge** và đăng nhập bằng tài khoản Google của bạn
2. **Nhấn nút �** (Sync to Antigravity) bên cạnh email tài khoản
3. **Antigravity sẽ tự động khởi động lại** với token mới

> 💡 **Mẹo:** Nếu vẫn gặp lỗi, thử đăng nhập lại trong AntiBridge rồi Sync to Antigravity.

### Lỗi 403 Forbidden

Khi thấy badge **🔒 403** màu đỏ bên cạnh email:

- Tài khoản không có quyền truy cập Gemini Code Assist
- Thử đăng nhập lại hoặc sử dụng tài khoản khác

---

## �📁 Cấu trúc dự án

```
src/
├── AntiBridge.Core/           # Business logic
│   ├── Models/                # Account, Token, Quota models
│   └── Services/              # OAuth, Quota, Storage services
├── AntiBridge.Avalonia/       # UI layer (Avalonia)
│   ├── ViewModels/            # MVVM ViewModels
│   └── Views/                 # AXAML views
└── AntiBridge.Tests/          # Unit tests (NUnit)
```

## 📂 Dữ liệu lưu trữ

Tài khoản được lưu tại:

- **Linux/macOS:** `~/.antibridge/`
- **Windows:** `%USERPROFILE%\.antibridge\`

## 🧪 Chạy Unit Tests

```bash
dotnet test
```

**42 tests** bao gồm:

- AccountStorageService, ProtobufHelper, Models
- AccountRowViewModel, QuotaService, AntigravityProcessService

## 🙏 Credits

Logic được port từ [Antigravity-Manager](https://github.com/lbjlaq/Antigravity-Manager) (Tauri/Rust).

## 📄 License

MIT License
