#!/usr/bin/env bash
set -e

cd "$(dirname "$0")"

PROJ="src/copilot-auto-byok.csproj"
OUT="publish"

# 最小化运行时占用
export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS="http://localhost:5000"
export Logging__LogLevel__Default=Warning
export Logging__LogLevel__Microsoft=Warning
export Logging__LogLevel__Microsoft.AspNetCore=Warning
export DOTNET_EnableDiagnostics=0

# 首次运行或代码变更时发布 Release 版本
if [ ! -f "$OUT/copilot-auto-byok.dll" ] || [ "$PROJ" -nt "$OUT/copilot-auto-byok.dll" ]; then
    echo "[publish] Release build -> $OUT"
    dotnet publish "$PROJ" -c Release -o "$OUT" --self-contained false --no-restore 2>/dev/null \
        || dotnet publish "$PROJ" -c Release -o "$OUT" --self-contained false
fi

echo "[start] http://localhost:5000 (minimal)"
exec dotnet "$OUT/copilot-auto-byok.dll"
