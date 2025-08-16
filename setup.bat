@echo off
echo Universal Game Translator - Setup Script
echo ========================================
echo.

:: Check if Visual Studio is available
where devenv >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Visual Studio 2022 not found in PATH
    echo Please install Visual Studio 2022 with C++ and .NET workloads
    pause
    exit /b 1
)

:: Check if .NET 8.0 SDK is available
dotnet --version | findstr "8." >nul
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: .NET 8.0 SDK not found
    echo Please install .NET 8.0 SDK from https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

echo ✓ Visual Studio 2022 found
echo ✓ .NET 8.0 SDK found
echo.

:: Create directory structure
echo Creating directory structure...
if not exist "libs\MinHook\include" mkdir "libs\MinHook\include"
if not exist "libs\MinHook\lib" mkdir "libs\MinHook\lib"
echo ✓ Directory structure created
echo.

:: Download MinHook (you would need to do this manually)
echo MANUAL STEP REQUIRED:
echo Please download MinHook from: https://github.com/TsudaKageyu/minhook/releases
echo Extract to: libs\MinHook\
echo - MinHook.h goes to: libs\MinHook\include\
echo - libMinHook.x64.lib goes to: libs\MinHook\lib\
echo - libMinHook.x86.lib goes to: libs\MinHook\lib\
echo.

:: Wait for user confirmation
echo Press any key after downloading MinHook...
pause >nul

:: Check if MinHook files exist
if not exist "libs\MinHook\include\MinHook.h" (
    echo ERROR: MinHook.h not found in libs\MinHook\include\
    echo Please download and extract MinHook first
    pause
    exit /b 1
)

if not exist "libs\MinHook\lib\libMinHook.x64.lib" (
    echo ERROR: libMinHook.x64.lib not found in libs\MinHook\lib\
    echo Please download and extract MinHook first
    pause
    exit /b 1
)

echo ✓ MinHook files found
echo.

:: Restore NuGet packages
echo Restoring NuGet packages...
dotnet restore src\UniversalGameTranslator\UniversalGameTranslator.csproj
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Failed to restore NuGet packages
    pause
    exit /b 1
)
echo ✓ NuGet packages restored
echo.

echo Setup completed successfully!
echo.
echo Next steps:
echo 1. Open UniversalGameTranslator.sln in Visual Studio 2022
echo 2. Build Solution (Ctrl+Shift+B)
echo 3. Run the application (F5)
echo.
echo For best results, run as Administrator when hooking games.
echo.
pause