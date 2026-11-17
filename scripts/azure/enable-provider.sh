#!/usr/bin/env bash
# Włącza zewnętrzną wypożyczalnię w Brokerze na Azure (App Settings + restart). Uruchamiamy w Azure Cloud Shell (Bash).
#
#   bash enable-provider.sh <klucz> <baseUrl> <apiKey> [nazwaNagłówka]
#   bash enable-provider.sh <klucz> --disable
#
# Przykłady:
#   bash enable-provider.sh OtherGroup https://ich-api.azurewebsites.net 'klucz-od-nich'
#   bash enable-provider.sh Lecturer https://api-prowadzacego.example.com 'klucz' X-Api-Key
#   bash enable-provider.sh Lecturer --disable
#
# Klucz to nazwa sekcji Providers:<Klucz> w konfiguracji Brokera: Lecturer (prowadzący) albo OtherGroup (druga grupa).
# Można też podać nowy klucz, wtedy Broker sam dopisze wypożyczalnię do tabeli Providers (kod = klucz małymi literami).
# Ustawia tylko wpisy dla dostawcy z domyślnym mapowaniem (kształt jak nasze Car Rental API). Inne ścieżki, pola,
# Bearer albo OAuth2 dopisujemy osobno: opis w docs/podpiecie-zewnetrznego-api.md.
#
# Zmienne (opcjonalnie): RG, BROKER_APP, DISPLAY_NAME, TIMEOUT_SECONDS.

set -euo pipefail

RG="${RG:-rg-carrentals}"
BROKER_APP="${BROKER_APP:-app-carrentals-broker}"
TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-15}"

KEY_IN="${1:-}"
BASE_URL="${2:-}"
API_KEY="${3:-}"
HEADER_NAME="${4:-x-api-key}"

if [ -z "$KEY_IN" ] || { [ "$BASE_URL" != "--disable" ] && { [ -z "$BASE_URL" ] || [ -z "$API_KEY" ]; }; }; then
  sed -n '2,12p' "$0"
  exit 1
fi

# Nazwy sekcji z appsettings.json i ich kody w tabeli Providers
case "$(echo "$KEY_IN" | tr '[:upper:]' '[:lower:]')" in
  lecturer)                KEY=Lecturer;   CODE=lecturer;    NAME="Wypożyczalnia prowadzącego" ;;
  othergroup|other-group)  KEY=OtherGroup; CODE=other-group; NAME="Wypożyczalnia innej grupy" ;;
  our)
    echo "Our to nasze Car Rental API z własną klasą, tym skryptem go nie zmieniamy."; exit 1 ;;
  *)
    if ! echo "$KEY_IN" | grep -Eq '^[A-Za-z][A-Za-z0-9]*$'; then
      echo "Klucz może mieć tylko litery i cyfry (np. Km3Group)."; exit 1
    fi
    KEY="$KEY_IN"; CODE=""; NAME="$KEY_IN" ;;
esac
NAME="${DISPLAY_NAME:-$NAME}"
P="Providers__${KEY}__"

if [ "$BASE_URL" = "--disable" ]; then
  echo "Wyłączam $KEY w $BROKER_APP"
  az webapp config appsettings set -g "$RG" -n "$BROKER_APP" -o none --settings "${P}Enabled=false"
else
  case "$BASE_URL" in
    https://*|http://*) ;;
    *) echo "baseUrl musi zaczynać się od https:// (albo http://)."; exit 1 ;;
  esac

  echo "Włączam $KEY ($BASE_URL, nagłówek $HEADER_NAME) w $BROKER_APP"
  SETTINGS=(
    "${P}Type=Generic"
    "${P}Enabled=true"
    "${P}DisplayName=$NAME"
    "${P}BaseUrl=$BASE_URL"
    "${P}ApiKey=$API_KEY"
    "${P}TimeoutSeconds=$TIMEOUT_SECONDS"
    "${P}Auth__Type=ApiKey"
    "${P}Auth__HeaderName=$HEADER_NAME"
  )
  if [ -n "$CODE" ]; then
    SETTINGS+=("${P}Code=$CODE")
  fi
  az webapp config appsettings set -g "$RG" -n "$BROKER_APP" -o none --settings "${SETTINGS[@]}"
fi

echo "Restart $BROKER_APP"
az webapp restart -g "$RG" -n "$BROKER_APP" -o none

HOST=$(az webapp show -g "$RG" -n "$BROKER_APP" --query defaultHostName -o tsv)
echo
echo "Ustawienia dostawcy (bez wartości klucza):"
az webapp config appsettings list -g "$RG" -n "$BROKER_APP" \
  --query "[?starts_with(name, '${P}') && name != '${P}ApiKey'].{name:name, value:value}" -o table
cat <<EOF

Sprawdzenie po około minucie (aplikacja wstaje po restarcie):
  curl -s "https://$HOST/health"
  curl -s "https://$HOST/api/v1/cars?pageSize=50" | jq '{providers: [.items[].provider] | unique, skipped: .skippedProviders}'

Jak wypożyczalnia jest w "skipped", powód jest w "message", a szczegóły w Log stream albo Application Insights.
EOF
