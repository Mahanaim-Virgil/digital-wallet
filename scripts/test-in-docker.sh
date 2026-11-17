#!/usr/bin/env bash
# Testleri Linux container'ında çalıştırır (Windows'ta `dotnet test` Akıllı Uygulama Denetimine takılır).
# Kullanım (repo kökünden, Git Bash):  bash scripts/test-in-docker.sh [test projesi yolu ...]
# Varsayılan: tests/ altındaki tüm projeler. Testcontainers için Docker soketi container'a bağlanır.
set -euo pipefail

# Git Bash'te Windows yolu (C:/...) gerekir (pwd -W); Linux/macOS'ta düz pwd.
ROOT="$(cd "$(dirname "$0")/.." && (pwd -W 2>/dev/null || pwd))"
PROJECTS=("$@")
[ ${#PROJECTS[@]} -eq 0 ] && PROJECTS=(tests/*/*.csproj)

CMD="set -e; cp -r /src /work; cd /work; find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +; set +e; rc=0"
for p in "${PROJECTS[@]}"; do
  # rc alt kabukta değil, aynı kabukta atanmalı; yoksa başarısız test çıkış kodunu 0 bırakır.
  CMD+="; echo '=== $p'; dotnet test --project '$p' || rc=1"
done
CMD+="; exit \$rc"

MSYS_NO_PATHCONV=1 docker run --rm \
  -v "$ROOT:/src:ro" \
  -v dw-nuget:/root/.nuget/packages \
  -v //var/run/docker.sock:/var/run/docker.sock \
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  bash -c "$CMD"
