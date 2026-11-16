#!/usr/bin/env bash
# Generuje migracje EF Core (domyślnie InitialCreate) dla Car Rental API i Broker API. Linux / macOS.
# Uruchamiać z katalogu repo:
#   ./scripts/add-migrations.sh                  # przez Dockera (mcr.microsoft.com/dotnet/sdk:10.0)
#   ./scripts/add-migrations.sh --local          # lokalny .NET 10 SDK
#   ./scripts/add-migrations.sh --name NazwaMigracji
# Migracje trafiają do backend/CarRental.Api/Migrations i backend/Broker.Api/Migrations (domyślne foldery).
# Baza nie jest potrzebna: dotnet ef używa fabryk design-time, nie Program.cs.
set -euo pipefail

NAME="InitialCreate"
LOCAL=0
IMAGE="mcr.microsoft.com/dotnet/sdk:10.0"

while [ $# -gt 0 ]; do
  case "$1" in
    --local) LOCAL=1 ;;
    --name) NAME="$2"; shift ;;
    --image) IMAGE="$2"; shift ;;
    -h|--help) sed -n '2,8p' "$0"; exit 0 ;;
    *) echo "Nieznany parametr: $1" >&2; exit 1 ;;
  esac
  shift
done

REPO="$(cd "$(dirname "$0")/.." && pwd)"
if [ ! -f "$REPO/CarRentals.sln" ]; then
  echo "Nie znaleziono CarRentals.sln w $REPO." >&2
  exit 1
fi

TARGETS=(
  "backend/CarRental.Api/CarRental.Api.csproj:CarRentalDbContext"
  "backend/Broker.Api/Broker.Api.csproj:BrokerDbContext"
)

COMMANDS=("dotnet tool restore")
DIRS=()
for target in "${TARGETS[@]}"; do
  project="${target%%:*}"
  context="${target##*:}"
  dir="$(dirname "$project")"
  if [ ! -f "$REPO/$project" ]; then
    echo "Pomijam $project: projektu jeszcze nie ma."
    continue
  fi
  if ls "$REPO/$dir/Migrations/"*_"$NAME".cs >/dev/null 2>&1; then
    echo "Pomijam $project: migracja $NAME już jest."
    continue
  fi
  # dotnet ef sam nie robi restore przed odczytem metadanych projektu
  COMMANDS+=("dotnet restore $project")
  COMMANDS+=("dotnet ef migrations add $NAME --project $project --startup-project $project --context $context")
  DIRS+=("$dir")
done

if [ ${#DIRS[@]} -eq 0 ]; then
  echo "Nic do zrobienia."
  exit 0
fi

cd "$REPO"

if [ "$LOCAL" -eq 1 ]; then
  if ! dotnet --list-sdks | grep -q '^10\.'; then
    echo "Brak .NET 10 SDK. Zainstaluj go albo uruchom bez --local (przez Dockera)." >&2
    exit 1
  fi
  for command in "${COMMANDS[@]}"; do
    echo "> $command"
    $command
  done
else
  command -v docker >/dev/null || { echo "Nie znaleziono dockera. Użyj --local (wymaga .NET 10 SDK)." >&2; exit 1; }

  SCRIPT="$(printf '%s && ' "${COMMANDS[@]}")true"
  CLEANUP=""
  for dir in "${DIRS[@]}"; do CLEANUP="$CLEANUP rm -rf $dir/bin $dir/obj;"; done

  echo "> docker run $IMAGE"
  printf '  %s\n' "${COMMANDS[@]}"
  # --user: pliki migracji należą do nas, a nie do roota; HOME=/tmp, bo użytkownik nie ma katalogu domowego w obrazie
  docker run --rm \
    --user "$(id -u):$(id -g)" \
    -e HOME=/tmp \
    -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    -e DOTNET_NOLOGO=1 \
    -v "$REPO:/src" \
    -w /src \
    "$IMAGE" sh -c "$CLEANUP $SCRIPT; status=\$?; $CLEANUP exit \$status"
fi

echo
echo "Gotowe. Sprawdź nowe pliki w folderach Migrations (git status) i zrób commit."
echo "Jeśli lokalna baza powstała wcześniej przez EnsureCreated, usuń ją przed pierwszym uruchomieniem z migracjami."
