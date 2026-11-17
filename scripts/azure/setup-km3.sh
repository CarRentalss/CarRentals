#!/usr/bin/env bash
# KM3: usługi Azure dla Car Rentals. Uruchamiamy w Azure Cloud Shell (Bash) PO setup-resources.sh
# (potrzebne są już rg-carrentals, plan asp-carrentals, app-carrentals-api i jego connection string CarRentalDb).
#
#   bash setup-km3.sh [region]          # domyślnie swedencentral
#
# Zakłada:
#   - Storage Account stcarrentalsjkm: kontener attachments (prywatny, załączniki zwrotów) i cars (publiczny odczyt blobów, zdjęcia aut),
#   - Service Bus Basic sb-carrentals-jkm z kolejką rental-requests i dwoma kluczami SAS (API tylko Send, Worker tylko Listen),
#   - Azure Communication Services acs-carrentals-jkm + Email Service email-carrentals-jkm z domeną Azure managed,
#   - App Service app-carrentals-worker (CarRental.Worker) na planie asp-carrentals, Always On, health check /health,
#   - App Settings w app-carrentals-api i app-carrentals-worker.
# Skrypt można puścić drugi raz: istniejące zasoby pomija, ustawienia nadpisuje.
# Region musi być na liście z polityki subskrypcji: germanywestcentral, swedencentral, switzerlandnorth, italynorth, denmarkeast.
# Klucze trafiają tylko do App Settings i do ~/carrentals-km3-secrets.env (Cloud Shell), nie do repo.

set -euo pipefail

LOC="${1:-swedencentral}"
RG=rg-carrentals
PLAN=asp-carrentals
API_APP="${API_APP:-app-carrentals-api}"
WORKER_APP="${WORKER_APP:-app-carrentals-worker}"
STORAGE="${STORAGE:-stcarrentalsjkm}"          # globalnie unikalna, 3-24 znaki, małe litery i cyfry
SB_NAMESPACE="${SB_NAMESPACE:-sb-carrentals-jkm}"
SB_QUEUE=rental-requests
ACS="${ACS:-acs-carrentals-jkm}"
EMAIL_SERVICE="${EMAIL_SERVICE:-email-carrentals-jkm}"
DATA_LOCATION="${DATA_LOCATION:-Europe}"       # gdzie ACS trzyma dane (zasób jest "global")
RUNTIME="DOTNETCORE:10.0"
SECRETS_FILE="$HOME/carrentals-km3-secrets.env"

step() { echo; echo "=== $* ==="; }

step "0. Subskrypcja i providery"
az account show --query "{name:name, id:id}" -o table
for p in Microsoft.Storage Microsoft.ServiceBus Microsoft.Communication Microsoft.Web; do
  az provider register -n "$p" --wait
done
az extension add -n communication --upgrade -y -o none
az group show -n "$RG" -o none || { echo "Brak $RG. Najpierw setup-resources.sh."; exit 1; }

# ---------- 1. Blob Storage ----------
step "1. Storage $STORAGE (kontenery attachments i cars)"
if ! az storage account show -g "$RG" -n "$STORAGE" -o none 2>/dev/null; then
  az storage account create -g "$RG" -n "$STORAGE" -l "$LOC" \
    --sku Standard_LRS --kind StorageV2 --min-tls-version TLS1_2 --https-only true \
    --allow-blob-public-access true -o none
else
  # Publiczny odczyt potrzebny tylko dla kontenera cars (zdjęcia aut na froncie)
  az storage account update -g "$RG" -n "$STORAGE" --allow-blob-public-access true -o none
fi
STORAGE_CS=$(az storage account show-connection-string -g "$RG" -n "$STORAGE" --query connectionString -o tsv)
az storage container create --name attachments --public-access off --connection-string "$STORAGE_CS" -o none
az storage container create --name cars --public-access blob --connection-string "$STORAGE_CS" -o none
# Jak kontener cars już był prywatny, ustawiamy publiczny odczyt samych blobów (bez listowania)
az storage container set-permission --name cars --public-access blob --connection-string "$STORAGE_CS" -o none
IMAGES_BASE_URL="https://$STORAGE.blob.core.windows.net/cars/"
echo "Zdjęcia aut wgrywamy do kontenera cars (nazwy w docs/zdjecia-aut.md), np.:"
echo "  az storage blob upload-batch -d cars -s ./zdjecia --pattern '*.jpg' --content-type image/jpeg --connection-string \"\$STORAGE_CS\""

# ---------- 2. Service Bus ----------
step "2. Service Bus $SB_NAMESPACE (Basic), kolejka $SB_QUEUE"
if ! az servicebus namespace show -g "$RG" -n "$SB_NAMESPACE" -o none 2>/dev/null; then
  az servicebus namespace create -g "$RG" -n "$SB_NAMESPACE" -l "$LOC" --sku Basic -o none
fi
if ! az servicebus queue show -g "$RG" --namespace-name "$SB_NAMESPACE" -n "$SB_QUEUE" -o none 2>/dev/null; then
  # 10 prób, potem dead-letter. Wiadomość żyje 1 dzień (Worker i tak zbiera zaległe wnioski z bazy).
  az servicebus queue create -g "$RG" --namespace-name "$SB_NAMESPACE" -n "$SB_QUEUE" \
    --max-delivery-count 10 --default-message-time-to-live P1D --lock-duration PT1M -o none
fi
# Osobne klucze na poziomie kolejki: API tylko wysyła, Worker tylko odbiera
for RULE in api-send:Send worker-listen:Listen; do
  NAME="${RULE%%:*}"; RIGHT="${RULE##*:}"
  if ! az servicebus queue authorization-rule show -g "$RG" --namespace-name "$SB_NAMESPACE" --queue-name "$SB_QUEUE" -n "$NAME" -o none 2>/dev/null; then
    az servicebus queue authorization-rule create -g "$RG" --namespace-name "$SB_NAMESPACE" --queue-name "$SB_QUEUE" \
      -n "$NAME" --rights "$RIGHT" -o none
  fi
done
SB_SEND_CS=$(az servicebus queue authorization-rule keys list -g "$RG" --namespace-name "$SB_NAMESPACE" --queue-name "$SB_QUEUE" -n api-send --query primaryConnectionString -o tsv)
SB_LISTEN_CS=$(az servicebus queue authorization-rule keys list -g "$RG" --namespace-name "$SB_NAMESPACE" --queue-name "$SB_QUEUE" -n worker-listen --query primaryConnectionString -o tsv)

# ---------- 3. Azure Communication Services + Email ----------
step "3. ACS $ACS i Email Service $EMAIL_SERVICE (domena Azure managed)"
if ! az communication show -g "$RG" -n "$ACS" -o none 2>/dev/null; then
  az communication create -g "$RG" -n "$ACS" --location global --data-location "$DATA_LOCATION" -o none
fi
if ! az communication email show -g "$RG" -n "$EMAIL_SERVICE" -o none 2>/dev/null; then
  az communication email create -g "$RG" -n "$EMAIL_SERVICE" --location global --data-location "$DATA_LOCATION" -o none
fi
if ! az communication email domain show -g "$RG" --email-service-name "$EMAIL_SERVICE" -n AzureManagedDomain -o none 2>/dev/null; then
  az communication email domain create -g "$RG" --email-service-name "$EMAIL_SERVICE" -n AzureManagedDomain \
    --location global --domain-management AzureManaged -o none
fi
DOMAIN_ID=$(az communication email domain show -g "$RG" --email-service-name "$EMAIL_SERVICE" -n AzureManagedDomain --query id -o tsv)
MAIL_FROM_DOMAIN=$(az communication email domain show -g "$RG" --email-service-name "$EMAIL_SERVICE" -n AzureManagedDomain --query mailFromSenderDomain -o tsv)
az communication update -g "$RG" -n "$ACS" --linked-domains "$DOMAIN_ID" -o none
ACS_CS=$(az communication list-key -g "$RG" -n "$ACS" --query primaryConnectionString -o tsv)
EMAIL_SENDER="DoNotReply@$MAIL_FROM_DOMAIN"

# ---------- 4. App Service dla Workera ----------
step "4. $WORKER_APP na planie $PLAN"
if ! az webapp show -g "$RG" -n "$WORKER_APP" -o none 2>/dev/null; then
  az webapp create -g "$RG" -p "$PLAN" -n "$WORKER_APP" --runtime "$RUNTIME" -o none
fi
az webapp update -g "$RG" -n "$WORKER_APP" --https-only true -o none
# Always On: Worker nie ma ruchu HTTP, bez tego App Service by go uśpił i kolejka stałaby
az webapp config set -g "$RG" -n "$WORKER_APP" --always-on true --ftps-state Disabled --min-tls-version 1.2 \
  --generic-configurations '{"healthCheckPath": "/health"}' -o none

# ---------- 5. App Settings ----------
step "5. App Settings ($API_APP, $WORKER_APP)"
CS_CARRENTAL=$(az webapp config connection-string list -g "$RG" -n "$API_APP" --query "[?name=='CarRentalDb'].value | [0]" -o tsv)
AI_CS=$(az webapp config appsettings list -g "$RG" -n "$API_APP" --query "[?name=='APPLICATIONINSIGHTS_CONNECTION_STRING'].value | [0]" -o tsv)
if [ -z "$CS_CARRENTAL" ]; then
  echo "Brak connection stringa CarRentalDb w $API_APP. Najpierw setup-resources.sh."; exit 1
fi

az webapp config appsettings set -g "$RG" -n "$API_APP" -o none --settings \
  Storage__BlobConnectionString="$STORAGE_CS" \
  Storage__BlobContainer=attachments \
  ServiceBus__ConnectionString="$SB_SEND_CS" \
  ServiceBus__QueueName="$SB_QUEUE" \
  Email__ConnectionString="$ACS_CS" \
  Email__Sender="$EMAIL_SENDER" \
  Images__BaseUrl="$IMAGES_BASE_URL"

az webapp config connection-string set -g "$RG" -n "$WORKER_APP" -t SQLAzure \
  --settings CarRentalDb="$CS_CARRENTAL" -o none
az webapp config appsettings set -g "$RG" -n "$WORKER_APP" -o none --settings \
  ASPNETCORE_ENVIRONMENT=Production \
  ServiceBus__ConnectionString="$SB_LISTEN_CS" \
  ServiceBus__QueueName="$SB_QUEUE" \
  Email__ConnectionString="$ACS_CS" \
  Email__Sender="$EMAIL_SENDER" \
  APPLICATIONINSIGHTS_CONNECTION_STRING="$AI_CS"

# ---------- 6. Sekrety (poza repo) ----------
step "6. Sekrety do $SECRETS_FILE"
umask 077
cat > "$SECRETS_FILE" <<EOF
# Car Rentals KM3, $(date -u +%Y-%m-%dT%H:%MZ). Przenieś do menedżera haseł, potem usuń ten plik.
STORAGE_CS='$STORAGE_CS'
SB_SEND_CS='$SB_SEND_CS'
SB_LISTEN_CS='$SB_LISTEN_CS'
ACS_CS='$ACS_CS'
EMAIL_SENDER=$EMAIL_SENDER
EOF

step "Gotowe"
cat <<EOF
Zdjęcia aut:  $IMAGES_BASE_URL<plik>.jpg (kontener cars, publiczny odczyt)
Załączniki:   kontener attachments (prywatny, tylko przez API)
Kolejka:      $SB_NAMESPACE / $SB_QUEUE
Nadawca:      $EMAIL_SENDER
Worker:       https://$(az webapp show -g "$RG" -n "$WORKER_APP" --query defaultHostName -o tsv)/health

Kod Workera wgrywa pipeline (etap Deploy, artefakt carrental-worker). Po wdrożeniu sprawdź w logach API:
"Wnioski o wynajem: Azure Service Bus" i w Log stream Workera "Nasłuchuję kolejki rental-requests".
EOF
