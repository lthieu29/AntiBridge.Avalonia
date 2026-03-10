<#
.SYNOPSIS
    Antigravity IDE Diagnostic Tool - Thu thập thông tin format token để debug/reverse-engineer.
    
.DESCRIPTION
    Script này thu thập:
    1. Phiên bản Antigravity đang cài đặt
    2. Tất cả keys liên quan trong database state.vscdb
    3. Cấu trúc protobuf đã decode (với token/cookie đã được mã hóa SHA256)
    4. Xuất ra file report trong thư mục chứa version Antigravity
    
.NOTES
    Output an toàn để share - tất cả access_token, refresh_token, cookie đều được hash SHA256.
    Chỉ giữ lại 8 ký tự đầu của hash để nhận dạng.
#>

param(
    [string]$OutputDir = ".\antigravity-diagnostic"
)

$ErrorActionPreference = "Stop"

# ============================================================
# Helper functions
# ============================================================

function Get-Sha256Short {
    param([string]$Text)
    if ([string]::IsNullOrEmpty($Text)) { return "[empty]" }
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $hash = [System.Security.Cryptography.SHA256]::HashData($bytes)
    $hex = [BitConverter]::ToString($hash).Replace("-", "").ToLower()
    return "sha256:$($hex.Substring(0, 8))...(len=$($Text.Length))"
}

function Mask-TokenInString {
    param([string]$Value)
    # Mask anything that looks like a JWT or long base64 token (>40 chars of base64)
    $masked = $Value -replace '[A-Za-z0-9_-]{40,}', { Get-Sha256Short $_.Value }
    return $masked
}

function Get-AntigravityDbPath {
    $appData = $env:APPDATA
    $path = Join-Path $appData "Antigravity\User\globalStorage\state.vscdb"
    if (Test-Path $path) { return $path }
    
    # Fallback paths
    $localAppData = $env:LOCALAPPDATA
    $path2 = Join-Path $localAppData "Antigravity\User\globalStorage\state.vscdb"
    if (Test-Path $path2) { return $path2 }
    
    return $null
}

function Get-AntigravityVersion {
    # Method 1: PowerShell VersionInfo
    $exePaths = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Antigravity\Antigravity.exe"),
        "C:\Program Files\Antigravity\Antigravity.exe"
    )
    
    foreach ($exePath in $exePaths) {
        if (Test-Path $exePath) {
            try {
                $ver = (Get-Item $exePath).VersionInfo.FileVersion
                if ($ver) { return @{ Version = $ver; Source = "FileVersionInfo"; Path = $exePath } }
            } catch {}
        }
    }
    
    # Method 2: package.json
    $packagePaths = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Antigravity\resources\app\package.json"),
        "C:\Program Files\Antigravity\resources\app\package.json"
    )
    
    foreach ($pkgPath in $packagePaths) {
        if (Test-Path $pkgPath) {
            try {
                $json = Get-Content $pkgPath -Raw | ConvertFrom-Json
                if ($json.version) { return @{ Version = $json.version; Source = "package.json"; Path = $pkgPath } }
            } catch {}
        }
    }
    
    return @{ Version = "UNKNOWN"; Source = "not found"; Path = $null }
}

function Decode-ProtobufFields {
    param([byte[]]$Data, [int]$Depth = 0)
    
    $fields = @()
    $offset = 0
    $indent = "  " * $Depth
    
    while ($offset -lt $Data.Length) {
        try {
            # Read varint tag
            $tag = 0; $shift = 0; $startOffset = $offset
            do {
                $b = $Data[$offset]; $offset++
                $tag = $tag -bor (($b -band 0x7F) -shl $shift)
                $shift += 7
            } while (($b -band 0x80) -ne 0 -and $offset -lt $Data.Length)
            
            $wireType = $tag -band 7
            $fieldNum = $tag -shr 3
            
            if ($fieldNum -eq 0) { break }
            
            switch ($wireType) {
                0 { # Varint
                    $value = 0; $shift = 0
                    do {
                        $b = $Data[$offset]; $offset++
                        $value = $value -bor (($b -band 0x7F) -shl $shift)
                        $shift += 7
                    } while (($b -band 0x80) -ne 0 -and $offset -lt $Data.Length)
                    
                    $fields += "${indent}Field $fieldNum (varint): $value"
                }
                2 { # Length-delimited
                    $length = 0; $shift = 0
                    do {
                        $b = $Data[$offset]; $offset++
                        $length = $length -bor (($b -band 0x7F) -shl $shift)
                        $shift += 7
                    } while (($b -band 0x80) -ne 0 -and $offset -lt $Data.Length)
                    
                    $content = $Data[$offset..($offset + $length - 1)]
                    $offset += $length
                    
                    # Try to interpret as UTF-8 string
                    try {
                        $str = [System.Text.Encoding]::UTF8.GetString($content)
                        $isPrintable = $true
                        foreach ($c in $str.ToCharArray()) {
                            if ([int]$c -lt 32 -and $c -ne "`n" -and $c -ne "`r" -and $c -ne "`t") {
                                $isPrintable = $false; break
                            }
                        }
                        
                        if ($isPrintable -and $str.Length -gt 0 -and $str.Length -eq $length) {
                            # Mask sensitive strings (tokens, long base64-like strings)
                            $maskedStr = Mask-TokenInString $str
                            $fields += "${indent}Field $fieldNum (string, len=$length): `"$maskedStr`""
                            
                            # If it looks like base64, try to decode and parse as protobuf
                            if ($str -match '^[A-Za-z0-9+/=]{20,}$') {
                                try {
                                    $innerBytes = [Convert]::FromBase64String($str)
                                    $fields += "${indent}  [base64 decoded → protobuf:]"
                                    $innerFields = Decode-ProtobufFields -Data $innerBytes -Depth ($Depth + 2)
                                    $fields += $innerFields
                                } catch {}
                            }
                        } else {
                            # Try as nested protobuf message
                            $fields += "${indent}Field $fieldNum (bytes, len=$length):"
                            try {
                                $innerFields = Decode-ProtobufFields -Data $content -Depth ($Depth + 1)
                                if ($innerFields.Count -gt 0) {
                                    $fields += $innerFields
                                } else {
                                    $hex = ($content | ForEach-Object { $_.ToString("x2") }) -join " "
                                    if ($hex.Length -gt 100) { $hex = $hex.Substring(0, 100) + "..." }
                                    $fields += "${indent}  [raw hex]: $hex"
                                }
                            } catch {
                                $hex = ($content | ForEach-Object { $_.ToString("x2") }) -join " "
                                if ($hex.Length -gt 100) { $hex = $hex.Substring(0, 100) + "..." }
                                $fields += "${indent}  [raw hex]: $hex"
                            }
                        }
                    } catch {
                        $hex = ($content | ForEach-Object { $_.ToString("x2") }) -join " "
                        if ($hex.Length -gt 100) { $hex = $hex.Substring(0, 100) + "..." }
                        $fields += "${indent}Field $fieldNum (bytes, len=$length): $hex"
                    }
                }
                1 { # 64-bit
                    $fields += "${indent}Field $fieldNum (64-bit): [skipped]"
                    $offset += 8
                }
                5 { # 32-bit
                    $fields += "${indent}Field $fieldNum (32-bit): [skipped]"
                    $offset += 4
                }
                default {
                    $fields += "${indent}Field $fieldNum (wire=$wireType): [unknown, stopping]"
                    break
                }
            }
        } catch {
            break
        }
    }
    
    return $fields
}

# ============================================================
# Main
# ============================================================

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Antigravity Diagnostic Tool" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 1. Detect version
Write-Host "[1/4] Detecting Antigravity version..." -ForegroundColor Yellow
$versionInfo = Get-AntigravityVersion
$versionTag = $versionInfo.Version -replace '[^0-9.]', ''
if ([string]::IsNullOrEmpty($versionTag)) { $versionTag = "unknown" }

Write-Host "  Version: $($versionInfo.Version)" -ForegroundColor Green
Write-Host "  Source:  $($versionInfo.Source)"
Write-Host "  Path:    $($versionInfo.Path)"
Write-Host ""

# 2. Create output directory with version
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$outputFolder = Join-Path $OutputDir "antigravity-v${versionTag}_${timestamp}"
New-Item -ItemType Directory -Path $outputFolder -Force | Out-Null
Write-Host "[2/4] Output folder: $outputFolder" -ForegroundColor Yellow
Write-Host ""

# 3. Find and dump database
Write-Host "[3/4] Reading database..." -ForegroundColor Yellow
$dbPath = Get-AntigravityDbPath

if (-not $dbPath) {
    Write-Host "  ERROR: Antigravity database not found!" -ForegroundColor Red
    "Database not found" | Out-File (Join-Path $outputFolder "error.txt")
    exit 1
}

Write-Host "  Database: $dbPath" -ForegroundColor Green

# Check if sqlite3 is available, otherwise use dotnet
$useSqlite3 = $null -ne (Get-Command sqlite3 -ErrorAction SilentlyContinue)

$report = @()
$report += "# Antigravity Diagnostic Report"
$report += "Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
$report += "Version: $($versionInfo.Version) (source: $($versionInfo.Source))"
$report += "Database: $dbPath"
$report += ""

# Keywords to search for in keys
$keyPatterns = @(
    "%oauth%", "%token%", "%Sync%", "%State%", "%state%",
    "%onboarding%", "%auth%", "%login%", "%session%", "%unified%"
)

$allRows = @()

if ($useSqlite3) {
    Write-Host "  Using sqlite3 CLI..." 
    
    # Get ALL keys first for overview
    $allKeys = & sqlite3 $dbPath "SELECT key FROM ItemTable ORDER BY key;" 2>$null
    $report += "## All Keys in ItemTable ($($allKeys.Count) total)"
    $report += '```'
    foreach ($key in $allKeys) {
        $report += $key
    }
    $report += '```'
    $report += ""
    
    # Get relevant rows with values
    $report += "## Relevant Keys (token/state/oauth related)"
    $report += ""
    
    foreach ($pattern in $keyPatterns) {
        $rows = & sqlite3 -separator "|" $dbPath "SELECT key, value FROM ItemTable WHERE key LIKE '$pattern';" 2>$null
        foreach ($row in $rows) {
            if ($row -and $row.Contains("|")) {
                $parts = $row -split '\|', 2
                $key = $parts[0]
                $value = $parts[1]
                
                # Avoid duplicates
                if ($allRows | Where-Object { $_.Key -eq $key }) { continue }
                $allRows += @{ Key = $key; Value = $value }
            }
        }
    }
} else {
    Write-Host "  sqlite3 not found, using dotnet SqliteHelper..." 
    
    $report += "## Keys queried via .NET SqliteHelper"
    $report += ""
    
    $helperProject = Join-Path $PSScriptRoot "SqliteHelper\SqliteHelper.csproj"
    
    if (-not (Test-Path $helperProject)) {
        Write-Host "  ERROR: SqliteHelper project not found at $helperProject" -ForegroundColor Red
        $report += "## Error: SqliteHelper project not found"
    } else {
        try {
            # Temporarily allow stderr (dotnet writes build output to stderr)
            $prevEAP = $ErrorActionPreference
            $ErrorActionPreference = "Continue"
            
            # Build first to avoid mixing build output with query results
            Write-Host "  Building SqliteHelper..."
            & dotnet build $helperProject --nologo -q 2>&1 | Out-Null
            
            if ($LASTEXITCODE -ne 0) {
                $ErrorActionPreference = $prevEAP
                throw "Failed to build SqliteHelper"
            }
            
            # Get all keys
            Write-Host "  Querying all keys..."
            $allKeys = & dotnet run --project $helperProject --no-build -- $dbPath keys 2>&1 | Where-Object { $_ -is [string] -or $_ -isnot [System.Management.Automation.ErrorRecord] }
            
            $ErrorActionPreference = $prevEAP
            
            $report += "## All Keys in ItemTable ($($allKeys.Count) total)"
            $report += '```'
            foreach ($key in $allKeys) { $report += $key }
            $report += '```'
            $report += ""
            $report += "## Relevant Keys (token/state/oauth related)"
            $report += ""
            
            # Query relevant keys
            $prevEAP2 = $ErrorActionPreference
            $ErrorActionPreference = "Continue"
            foreach ($pattern in $keyPatterns) {
                $rows = & dotnet run --project $helperProject --no-build -- $dbPath query "SELECT key, value FROM ItemTable WHERE key LIKE @p1" $pattern 2>&1 | Where-Object { $_ -is [string] -or $_ -isnot [System.Management.Automation.ErrorRecord] }
                foreach ($row in $rows) {
                    if (-not $row) { continue }
                    $parts = $row -split '\|\|\|', 2
                    if ($parts.Count -lt 2) { continue }
                    $key = $parts[0]
                    $value = $parts[1]
                    if (-not ($allRows | Where-Object { $_.Key -eq $key })) {
                        $allRows += @{ Key = $key; Value = $value }
                    }
                }
            }
            $ErrorActionPreference = $prevEAP2
            
            Write-Host "  Successfully queried $($allRows.Count) relevant keys" -ForegroundColor Green
        } catch {
            Write-Host "  ERROR querying database: $_" -ForegroundColor Red
            $report += "## Error: Failed to query database: $_"
        }
    }
}

# 4. Decode and write report
Write-Host "[4/4] Decoding protobuf structures..." -ForegroundColor Yellow
Write-Host ""

foreach ($row in $allRows) {
    $key = $row.Key
    $value = $row.Value
    
    $report += "### Key: ``$key``"
    $report += "- Value length: $($value.Length) chars"
    
    # Try base64 decode
    try {
        $bytes = [Convert]::FromBase64String($value)
        $report += "- Base64 decoded: $($bytes.Length) bytes"
        $report += ""
        
        # Decode protobuf
        $report += '```protobuf'
        $fields = Decode-ProtobufFields -Data $bytes
        if ($fields.Count -gt 0) {
            $report += $fields
        } else {
            $report += "[Could not parse as protobuf]"
            # Show masked raw value
            $maskedValue = Mask-TokenInString $value
            $report += "Raw (masked): $maskedValue"
        }
        $report += '```'
    } catch {
        # Not base64, show masked value
        $maskedValue = Mask-TokenInString $value
        $report += "- Not base64, raw value (masked): ``$maskedValue``"
    }
    
    $report += ""
}

# Write report
$reportPath = Join-Path $outputFolder "report.md"
$report | Out-File -FilePath $reportPath -Encoding utf8

# Also save version info separately
$versionInfo | ConvertTo-Json | Out-File (Join-Path $outputFolder "version.json") -Encoding utf8

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Done!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Report saved to: $reportPath" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Open the report file and review"
Write-Host "  2. Paste content into Kiro chat with #File reference"
Write-Host "  3. Or copy-paste the report.md content directly"
Write-Host ""
