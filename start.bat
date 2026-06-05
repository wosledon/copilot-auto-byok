@echo off
setlocal

cd /d "%~dp0"

set ASPNETCORE_ENVIRONMENT=Production
set ASPNETCORE_URLS=http://localhost:5000
set Logging__LogLevel__Default=Warning
set Logging__LogLevel__Microsoft=Warning
set Logging__LogLevel__Microsoft.AspNetCore=Warning
set DOTNET_EnableDiagnostics=0

set PROJ=src\copilot-auto-byok.csproj
set OUT=publish

if not exist "%OUT%\copilot-auto-byok.dll" (
    echo [publish] Release build -^> %OUT%
    dotnet publish "%PROJ%" -c Release -o "%OUT%" --self-contained false
)

echo [start] http://localhost:5000 (minimal)
cd /d "%OUT%"
dotnet "copilot-auto-byok.dll"
