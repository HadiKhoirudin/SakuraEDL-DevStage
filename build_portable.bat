@echo off
chcp 65001 >nul
echo ============================================
echo   MultiFlash TOOL Portable Version Packaging Script
echo ============================================
echo.

set VERSION=2.2.0
set OUTPUT_DIR=portable
set PACKAGE_NAME=MultiFlash_Portable_v%VERSION%

:: Clean old output directory
if exist "%OUTPUT_DIR%\%PACKAGE_NAME%" rd /s /q "%OUTPUT_DIR%\%PACKAGE_NAME%"
if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"
mkdir "%OUTPUT_DIR%\%PACKAGE_NAME%"

echo [Copy] Copying files...

:: Copy main executable
copy "bin\Release\MultiFlash.exe" "%OUTPUT_DIR%\%PACKAGE_NAME%\" >nul
copy "bin\Release\MultiFlash.exe.config" "%OUTPUT_DIR%\%PACKAGE_NAME%\" >nul

:: Copy UI libraries
copy "bin\Release\AntdUI.dll" "%OUTPUT_DIR%\%PACKAGE_NAME%\" >nul
copy "bin\Release\SunnyUI.dll" "%OUTPUT_DIR%\%PACKAGE_NAME%\" >nul
copy "bin\Release\SunnyUI.Common.dll" "%OUTPUT_DIR%\%PACKAGE_NAME%\" >nul
copy "bin\Release\HandyControl.dll" "%OUTPUT_DIR%\%PACKAGE_NAME%\" >nul

:: Copy resource packages (if exists)
if exist "bin\Release\edl_loaders.pak" copy "bin\Release\edl_loaders.pak" "%OUTPUT_DIR%\%PACKAGE_NAME%\" >nul
if exist "bin\Release\firehose.pak" copy "bin\Release\firehose.pak" "%OUTPUT_DIR%\%PACKAGE_NAME%\" >nul

:: Copy icon
copy "MultiFlash TOOL.ico" "%OUTPUT_DIR%\%PACKAGE_NAME%\" >nul

:: Create instructions file
echo MultiFlash TOOL v%VERSION% Portable Version > "%OUTPUT_DIR%\%PACKAGE_NAME%\Instructions.txt"
echo. >> "%OUTPUT_DIR%\%PACKAGE_NAME%\说明.txt"
echo Usage Instructions: >> "%OUTPUT_DIR%\%PACKAGE_NAME%\Instructions.txt"
echo 1. Ensure .NET Framework 4.8 is installed >> "%OUTPUT_DIR%\%PACKAGE_NAME%\Instructions.txt"
echo 2. Run MultiFlash.exe directly >> "%OUTPUT_DIR%\%PACKAGE_NAME%\Instructions.txt"
echo. >> "%OUTPUT_DIR%\%PACKAGE_NAME%\说明.txt"
echo System Requirements: >> "%OUTPUT_DIR%\%PACKAGE_NAME%\Instructions.txt"
echo - Windows 7/8/10/11 >> "%OUTPUT_DIR%\%PACKAGE_NAME%\Instructions.txt"
echo - .NET Framework 4.8 >> "%OUTPUT_DIR%\%PACKAGE_NAME%\Instructions.txt"
echo. >> "%OUTPUT_DIR%\%PACKAGE_NAME%\说明.txt"
echo GitHub: https://github.com/xiriovo/edltool >> "%OUTPUT_DIR%\%PACKAGE_NAME%\Instructions.txt"

echo.
echo [Info] Files copied to: %OUTPUT_DIR%\%PACKAGE_NAME%\
echo.

:: Check if 7z is available
where 7z >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo [Packing] Creating archive...
    cd "%OUTPUT_DIR%"
    7z a -t7z -mx=9 "%PACKAGE_NAME%.7z" "%PACKAGE_NAME%\*" >nul
    cd ..
    echo [完成] 压缩包: %OUTPUT_DIR%\%PACKAGE_NAME%.7z
) else (
    echo [Tip] 7z not found, skipping compression.
    echo        Please manually compress the %OUTPUT_DIR%\%PACKAGE_NAME% folder
)

echo.
echo ============================================
echo   Portable version packaging complete!
echo ============================================
echo.
pause
