#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [ -z "${XDG_RUNTIME_DIR:-}" ] || [ ! -d "${XDG_RUNTIME_DIR}" ]; then
    export XDG_RUNTIME_DIR=/tmp
fi
exec dotnet run --project "$SCRIPT_DIR/src/EbookReader.Frontend/EbookReader.Frontend.csproj" "$@"
