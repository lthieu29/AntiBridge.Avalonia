@echo off
REM ============================================
REM AntiBridge - Open Antigravity with CDP
REM For Windows
REM ============================================

setlocal enabledelayedexpansion

set CDP_PORT=9222
set HEALTH_PORT=8080
set ANTIGRAVITY_PATH=C:\Users\lehie\AppData\Local\Programs\Antigravity\bin\antigravity.cmd
set MAX_WAIT=15

echo ================================================================
echo        AntiBridge - Antigravity CDP Launcher (Windows)
echo ================================================================
echo.

REM 0. Kill any process using health port (8080) to prevent conflicts
echo Checking port %HEALTH_PORT% (Health Check port)...
for /f "tokens=5" %%a in ('netstat -ano ^| findstr ":%HEALTH_PORT%"') do (
    echo Port %HEALTH_PORT% is in use, killing process...
    taskkill /F /PID %%a >nul 2>&1
)
timeout /t 1 /nobreak >nul
echo.

REM 1. Check if Antigravity is installed
if not exist "%ANTIGRAVITY_PATH%" (
    echo [ERROR] Antigravity not found at: %ANTIGRAVITY_PATH%
    echo Please update ANTIGRAVITY_PATH in this script.
    pause
    exit /b 1
)

REM 2. Check if already running with CDP
netstat -ano | findstr ":%CDP_PORT%" >nul 2>&1
if %errorlevel% equ 0 (
    echo [OK] CDP already listening on port %CDP_PORT%
    echo.
    echo Testing connection...
    curl -s "http://127.0.0.1:%CDP_PORT%/json/version" >nul 2>&1
    if %errorlevel% equ 0 (
        echo [OK] CDP Connection successful!
        curl -s "http://127.0.0.1:%CDP_PORT%/json/version"
        echo.
        pause
        exit /b 0
    )
)

REM 3. Check if Antigravity is running WITHOUT CDP
tasklist /FI "IMAGENAME eq antigravity.exe" 2>nul | find /I "antigravity.exe" >nul
if %errorlevel% equ 0 (
    echo [WARNING] Antigravity is running but CDP is NOT enabled.
    echo.
    set /p CONFIRM="Do you want to restart Antigravity with CDP? (y/n): "
    if /i not "!CONFIRM!"=="y" (
        echo [ABORTED] Please close Antigravity manually and run this script again.
        pause
        exit /b 1
    )
    
    echo Closing existing Antigravity instances...
    taskkill /F /IM antigravity.exe >nul 2>&1
    timeout /t 2 /nobreak >nul
)

REM 4. Launch Antigravity with CDP
echo Launching Antigravity with CDP on port %CDP_PORT%...
start "" "%ANTIGRAVITY_PATH%" --remote-debugging-port=%CDP_PORT%
echo.

REM 5. Wait for CDP to become available
echo Waiting for CDP to start (max %MAX_WAIT%s)...
set /a COUNTER=0
:WAIT_LOOP
timeout /t 1 /nobreak >nul
set /a COUNTER+=1

curl -s "http://127.0.0.1:%CDP_PORT%/json/version" >nul 2>&1
if %errorlevel% equ 0 (
    echo.
    echo ================================================================
    echo              [OK] CDP Connection Successful!
    echo ================================================================
    echo   CDP Endpoint: http://127.0.0.1:%CDP_PORT%
    echo   WebSocket:    ws://127.0.0.1:%CDP_PORT%
    echo ================================================================
    echo.
    echo CDP Info:
    curl -s "http://127.0.0.1:%CDP_PORT%/json/version"
    echo.
    echo Available pages:
    curl -s "http://127.0.0.1:%CDP_PORT%/json"
    echo.
    echo [OK] You can now start the Telegram bot: npm start
    echo.
    pause
    exit /b 0
)

if %COUNTER% lss %MAX_WAIT% (
    echo|set /p="."
    goto WAIT_LOOP
)

REM 6. Failed to connect
echo.
echo [ERROR] Failed to connect to CDP after %MAX_WAIT% seconds.
echo.
echo Troubleshooting:
echo    1. Check if Antigravity window opened
echo    2. Check port: netstat -ano ^| findstr ":%CDP_PORT%"
echo    3. Try manually: "%ANTIGRAVITY_PATH%" --remote-debugging-port=%CDP_PORT%
echo.
pause
exit /b 1
