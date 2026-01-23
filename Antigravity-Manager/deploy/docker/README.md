# Docker Deployment for Antigravity Manager

> Run Antigravity Manager in a containerized environment with web-based VNC access

## Quick Start

```bash
cd deploy/docker
docker compose up -d
```

Access the web interface at: **http://localhost:6080/vnc_lite.html**

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Docker Container                      │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────┐  │
│  │  TigerVNC   │→ │   Openbox   │→ │ Antigravity App │  │
│  │  (Display)  │  │    (WM)     │  │  + Firefox ESR  │  │
│  └─────────────┘  └─────────────┘  └─────────────────┘  │
│         ↓                                                │
│  ┌─────────────┐                                        │
│  │   noVNC     │ ←──── Port 6080 (Web Access)           │
│  │ (Websocket) │                                        │
│  └─────────────┘                                        │
│                      Port 8045 (Proxy API) ────────────→│
└─────────────────────────────────────────────────────────┘
```

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `VNC_PASSWORD` | `password` | VNC access password |
| `RESOLUTION` | `1280x800` | Virtual display resolution |
| `NOVNC_PORT` | `6080` | noVNC web interface port |

## Usage Examples

### Basic Usage

```bash
docker compose up -d
```

### Custom Configuration

```bash
VNC_PASSWORD=mysecret RESOLUTION=1920x1080 docker compose up -d
```

### With Resource Limits

```bash
docker compose up -d --memory="512m" --cpus="1.0"
```

### View Logs

```bash
docker compose logs -f
```

### Stop Container

```bash
docker compose down
```

## Features

- **🖥️ Web-based VNC Access** - Access full GUI via noVNC from any browser
- **🌐 Browser Auth Support** - Firefox ESR included for OAuth/authentication flows
- **🌍 Multi-language Support** - CJK fonts and full locale support
- **📦 Process Isolation** - Complete sandboxing from host system
- **🔄 Auto-update** - Automatically pulls latest release on container start
- **💾 Persistent Storage** - Volume mount preserves your data
- **❤️ Health Checks** - Built-in container health monitoring

## Data Persistence

Your account data is stored in a Docker volume (`antigravity_data`) and persists across container restarts.

To backup your data:

```bash
docker run --rm -v antigravity_data:/data -v $(pwd):/backup alpine tar czf /backup/antigravity-backup.tar.gz /data
```

To restore:

```bash
docker run --rm -v antigravity_data:/data -v $(pwd):/backup alpine tar xzf /backup/antigravity-backup.tar.gz -C /
```

## Comparison with Xvfb Solution

| Aspect | Xvfb (headless-xvfb) | Docker (This) |
|--------|----------------------|---------------|
| Isolation | ❌ None — runs on host | ✅ Full container sandbox |
| Web Access | ❌ None | ✅ noVNC web interface |
| Browser Auth | ❌ No browser | ✅ Firefox ESR included |
| Resource Limits | ❌ Unlimited | ✅ Configurable |
| Multi-instance | ⚠️ Manual setup | ✅ Easy scaling |
| Auto-update | ❌ Manual | ✅ On every restart |

## Troubleshooting

### Container won't start

Check the logs:
```bash
docker compose logs
```

### VNC connection refused

Ensure the container is healthy:
```bash
docker compose ps
```

### GitHub rate limit

If auto-update fails due to rate limiting, the container will use the cached version.

## System Requirements

- Docker 20.10+
- Docker Compose v2
- 512MB RAM minimum
- x86_64 or ARM64 architecture
