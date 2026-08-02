@echo off
title Karachi Railway System Launcher
echo ===================================================
echo   Karachi Railway System - Build and Run Script
echo ===================================================
echo.

echo Restoring dependencies and building the project...
dotnet build src\KarachiRailway.Desktop\KarachiRailway.Desktop.csproj -c Release

if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Build failed. Please check the errors above.
    pause
    exit /b %errorlevel%
)

echo.
echo Build successful! Launching the application...
start "" "src\KarachiRailway.Desktop\bin\Release\net8.0-windows\KarachiRailway.Desktop.exe"
