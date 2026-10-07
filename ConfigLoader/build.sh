#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
dotnet_command="${DOTNET:-dotnet}"
game_path="${GAME_PATH:-/home/ATT/a-township-container/game-source}"
api_path="${CUSTOM_INGOTS_API_PATH:-$project_root/../bin/CustomIngots.API.dll}"

if ! command -v "$dotnet_command" >/dev/null 2>&1; then
    if [[ -x /opt/repair-hammer-dotnet/dotnet ]]; then
        dotnet_command="/opt/repair-hammer-dotnet/dotnet"
    else
        echo "Could not find '$dotnet_command' or /opt/repair-hammer-dotnet/dotnet." >&2
        exit 1
    fi
fi

exec "$dotnet_command" build "$project_root/CustomIngots.Config.csproj" \
    -c Release \
    "-p:GamePath=$game_path" \
    "-p:CustomIngotsApiPath=$api_path"
