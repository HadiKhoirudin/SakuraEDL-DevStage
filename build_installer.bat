@echo off
chcp 65001 >nul
echo ============================================
echo   MultiFlash TOOL Installer Build Script
echo ============================================
echo.

:: Check if Inno Setup is installed
set ISCC_PATH=
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" (
    set "ISCC_PATH=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
) else if exist "C:\Program Files\Inno Setup 6\ISCC.exe" (
    set "ISCC_PATH=C:\Program Files\Inno Setup 6\ISCC.exe"
) else if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" (
    set "ISCC_PATH=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
)

if "%ISCC_PATH%"=="" (
    echo [Error] Inno Setup 6 not found
    echo.
    echo Please download and install Inno Setup from:
    echo https://jrsoftware.org/isdl.php
    echo.
    pause
    exit /b 1
)

echo [Info] Found Inno Setup: %ISCC_PATH%
echo.

:: Create output directory
if not exist "installer" mkdir installer

:: Compile installer
echo [Build] Compiling installer...
"%ISCC_PATH%" setup.iss

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [Error] Compilation failed!
    pause
    exit /b 1
)

echo.
echo ============================================
echo   Build completed!
echo   输出文件: installer\MultiFlash_Setup_v2.2.0.exe
echo ============================================
echo.
pause
