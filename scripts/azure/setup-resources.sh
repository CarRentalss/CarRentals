#!/usr/bin/env bash
# Zakłada zasoby Azure dla Car Rentals (KM2). Uruchamiamy w Azure Cloud Shell (Bash), shell.azure.com.
#
#   bash setup-resources.sh <region> [sufiks]
#   np. bash setup-resources.sh polandcentral jkm
#
# Region bierzemy z polityki "Allowed resource deployment regions" (opis w scripts/azure/README.md).
# Skrypt można puścić drugi raz: istniejące zasoby pomija, ustawienia nadpisuje tymi samymi wartościami.
# Uwaga: przy drugim uruchomieniu generuje NOWE hasło SQL i NOWE klucze, chyba że podasz je w zmiennych
# SQL_PASS, KEY_BROKER, KEY_OTHER (np. z pliku ~/carrentals-secrets.env: source ~/carrentals-secrets.env).
#
# Hasło SQL i klucze API powstają tutaj i NIE trafiają do repo. Zapisujemy je w ~/carrentals-secrets.env
# (katalog domowy Cloud Shell), a potem przenosimy do menedżera haseł i ADO Library.

set -euo pipefail

# ---------- Ustawienia (zmień tylko tutaj) ----------
LOC="${1:-}"
SFX="${2:-jkm}"

RG=rg-carrentals
PLAN=asp-carrentals
API_APP="${API_APP:-app-carrentals-api}"          # jak zajęte: API_APP=app-carrentals-api-jkm bash setup-resources.sh ...
BROKER_APP="${BROKER_APP:-app-carrentals-broker}" # jak zajęte: BROKER_APP=app-carrentals-broker-jkm
SQL_SERVER="sql-carrentals-$SFX"
SQL_DB_CARRENTAL=sqldb-carrental
SQL_DB_BROKER=sqldb-broker
SQL_ADMIN=carrentaladmin
LOG_WS=log-carrentals
APPI=appi-carrentals
ADO_IDENTITY=id-carrentals-ado
RUNTIME="DOTNETCORE:10.0"
MY_IP="${MY_IP:-}"                                 # opcjonalnie IP do firewalla SQL (żeby wejść z SSMS/Azure Data Studio)
SECRETS_FILE="$HOME/carrentals-secrets.env"

# Sekrety: generowane, chyba że podane z zewnątrz
SQL_PASS="${SQL_PASS:-$(openssl rand -base64 24 | tr -d '/+=')Aa1!}"
KEY_BROKER="${KEY_BROKER:-$(openssl rand -hex 32)}"
KEY_OTHER="${KEY_OTHER:-$(openssl rand -hex 32)}"

# Entra (opcjonalnie, po rejestracji aplikacji). Bez tych zmiennych AzureAd nie ustawiamy:
# Car Rental API wstaje, ale panel pracownika jest zamknięty (fail-safe), Broker działa bez logowania Entra.
AZURE_AD_TENANT_ID="${AZURE_AD_TENANT_ID:-}"
AZURE_AD_API_CLIENT_ID="${AZURE_AD_API_CLIENT_ID:-}"
AZURE_AD_BROKER_CLIENT_ID="${AZURE_AD_BROKER_CLIENT_ID:-}"
AZURE_AD_BROKER_SECRET="${AZURE_AD_BROKER_SECRET:-}"

if [ -z "$LOC" ]; then
  echo "Podaj region, np.: bash setup-resources.sh polandcentral jkm"
  echo "Dozwolone regiony w tej subskrypcji:"
  az policy assignment list --query "[].{name:displayName, params:parameters}" -o json || true
  exit 1
fi

step() { echo; echo "=== $* ==="; }

# ---------- 0. Kontekst ----------
step "0. Subskrypcja"
az account show --query "{name:name, id:id, tenant:tenantId}" -o table
SUB_ID=$(az account show --query id -o tsv)
SUB_NAME=$(az account show --query name -o tsv)
TENANT_ID=$(az account show --query tenantId -o tsv)

# ---------- 1. Providery i resource group ----------
step "1. Providery i resource group $RG ($LOC)"
for p in Microsoft.Web Microsoft.Sql Microsoft.Insights Microsoft.OperationalInsights Microsoft.ManagedIdentity; do
  az provider register -n "$p" --wait
done
az group create -n "$RG" -l "$LOC" -o none

# ---------- 2. Monitoring: Log Analytics + Application Insights ----------
step "2. $LOG_WS i $APPI"
az extension add -n application-insights --upgrade -y -o none
if ! az monitor log-analytics workspace show -g "$RG" -n "$LOG_WS" -o none 2>/dev/null; then
  az monitor log-analytics workspace create -g "$RG" -n "$LOG_WS" -l "$LOC" -o none
fi
az monitor log-analytics workspace update -g "$RG" -n "$LOG_WS" --quota 0.1 -o none   # dzienny limit 0,1 GB
if ! az monitor app-insights component show -g "$RG" -a "$APPI" -o none 2>/dev/null; then
  az monitor app-insights component create -g "$RG" -a "$APPI" -l "$LOC" --workspace "$LOG_WS" --application-type web -o none
fi
AI_CS=$(az monitor app-insights component show -g "$RG" -a "$APPI" --query connectionString -o tsv)

# ---------- 3. Azure SQL: serwer, firewall, 2 bazy Basic ----------
step "3. $SQL_SERVER z bazami $SQL_DB_CARRENTAL i $SQL_DB_BROKER"
if ! az sql server show -g "$RG" -n "$SQL_SERVER" -o none 2>/dev/null; then
  az sql server create -g "$RG" -n "$SQL_SERVER" -l "$LOC" \
    --admin-user "$SQL_ADMIN" --admin-password "$SQL_PASS" --minimal-tls-version 1.2 -o none
else
  echo "Serwer już jest. Ustawiam hasło admina na to z tego uruchomienia (zapisane w $SECRETS_FILE)."
  az sql server update -g "$RG" -n "$SQL_SERVER" --admin-password "$SQL_PASS" -o none
fi
# App Service i agent ADO (migracje z pipeline'u) łączą się przez regułę "Allow Azure services"
az sql server firewall-rule create -g "$RG" -s "$SQL_SERVER" -n AllowAzureServices \
  --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0 -o none
if [ -n "$MY_IP" ]; then
  az sql server firewall-rule create -g "$RG" -s "$SQL_SERVER" -n dev-ip \
    --start-ip-address "$MY_IP" --end-ip-address "$MY_IP" -o none
fi
for DB in "$SQL_DB_CARRENTAL" "$SQL_DB_BROKER"; do
  if ! az sql db show -g "$RG" -s "$SQL_SERVER" -n "$DB" -o none 2>/dev/null; then
    az sql db create -g "$RG" -s "$SQL_SERVER" -n "$DB" \
      --edition Basic --capacity 5 --max-size 2GB --backup-storage-redundancy Local -o none
  fi
done
SQL_HOST="$SQL_SERVER.database.windows.net"
CS_CARRENTAL="Server=tcp:$SQL_HOST,1433;Database=$SQL_DB_CARRENTAL;User ID=$SQL_ADMIN;Password=$SQL_PASS;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
CS_BROKER="Server=tcp:$SQL_HOST,1433;Database=$SQL_DB_BROKER;User ID=$SQL_ADMIN;Password=$SQL_PASS;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

# ---------- 4. App Service: plan Linux B1 i 2 aplikacje .NET 10 ----------
step "4. $PLAN (Linux B1), $API_APP, $BROKER_APP"
if ! az appservice plan show -g "$RG" -n "$PLAN" -o none 2>/dev/null; then
  az appservice plan create -g "$RG" -n "$PLAN" -l "$LOC" --is-linux --sku B1 -o none
fi
if ! az webapp list-runtimes --os-type linux -o tsv | grep -qi "DOTNETCORE:10.0"; then
  echo "UWAGA: w tym regionie/CLI nie widać runtime $RUNTIME. Sprawdź: az webapp list-runtimes --os-type linux"
fi
for APP in "$API_APP" "$BROKER_APP"; do
  if ! az webapp show -g "$RG" -n "$APP" -o none 2>/dev/null; then
    az webapp create -g "$RG" -p "$PLAN" -n "$APP" --runtime "$RUNTIME" -o none
  fi
  az webapp update -g "$RG" -n "$APP" --https-only true -o none
  az webapp config set -g "$RG" -n "$APP" --ftps-state Disabled --min-tls-version 1.2 \
    --generic-configurations '{"healthCheckPath": "/health"}' -o none
done
# Always On dla Car Rental API: na B1 nic nie kosztuje, a proces w tle (potwierdzanie wynajmów) nie zasypia
az webapp config set -g "$RG" -n "$API_APP" --always-on true -o none

API_HOST=$(az webapp show -g "$RG" -n "$API_APP" --query defaultHostName -o tsv)
BROKER_HOST=$(az webapp show -g "$RG" -n "$BROKER_APP" --query defaultHostName -o tsv)

# ---------- 5. Ustawienia aplikacji (App Settings) ----------
step "5. App Settings"
# Car Rental API
az webapp config connection-string set -g "$RG" -n "$API_APP" -t SQLAzure \
  --settings CarRentalDb="$CS_CARRENTAL" -o none
az webapp config appsettings set -g "$RG" -n "$API_APP" -o none --settings \
  ASPNETCORE_ENVIRONMENT=Production \
  Database__ApplyMigrationsOnStartup=false \
  ApiKeys__Clients__0__Name=OurBroker \
  ApiKeys__Clients__0__Key="$KEY_BROKER" \
  ApiKeys__Clients__1__Name=OtherGroupBroker \
  ApiKeys__Clients__1__Key="$KEY_OTHER" \
  APPLICATIONINSIGHTS_CONNECTION_STRING="$AI_CS" \
  Storage__LocalPath=/home/data/attachments \
  Swagger__Enabled=true

# Broker API (React jest w wwwroot, więc to też adres frontu).
# ASPNETCORE_FORWARDEDHEADERS_ENABLED: App Service kończy TLS na proxy, bez tego redirect_uri do Entra wyszedłby jako http://
az webapp config connection-string set -g "$RG" -n "$BROKER_APP" -t SQLAzure \
  --settings BrokerDb="$CS_BROKER" -o none
az webapp config appsettings set -g "$RG" -n "$BROKER_APP" -o none --settings \
  ASPNETCORE_ENVIRONMENT=Production \
  ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
  Database__ApplyMigrationsOnStartup=false \
  APPLICATIONINSIGHTS_CONNECTION_STRING="$AI_CS" \
  Providers__Our__BaseUrl="https://$API_HOST" \
  Providers__Our__ApiKey="$KEY_BROKER" \
  Providers__Our__TimeoutSeconds=15 \
  Auth__UseDevLogin=false \
  Swagger__Enabled=true

# Entra: tylko gdy podane (po rejestracji aplikacji, krok w README)
if [ -n "$AZURE_AD_TENANT_ID" ] && [ -n "$AZURE_AD_API_CLIENT_ID" ]; then
  az webapp config appsettings set -g "$RG" -n "$API_APP" -o none --settings \
    AzureAd__Instance=https://login.microsoftonline.com/ \
    AzureAd__TenantId="$AZURE_AD_TENANT_ID" \
    AzureAd__ClientId="$AZURE_AD_API_CLIENT_ID"
  echo "Car Rental API: AzureAd ustawione."
else
  echo "Car Rental API: AzureAd pominięte (brak AZURE_AD_TENANT_ID / AZURE_AD_API_CLIENT_ID). Dopisz po rejestracji w Entra."
fi
if [ -n "$AZURE_AD_TENANT_ID" ] && [ -n "$AZURE_AD_BROKER_CLIENT_ID" ] && [ -n "$AZURE_AD_BROKER_SECRET" ]; then
  az webapp config appsettings set -g "$RG" -n "$BROKER_APP" -o none --settings \
    AzureAd__Instance=https://login.microsoftonline.com/ \
    AzureAd__TenantId="$AZURE_AD_TENANT_ID" \
    AzureAd__ClientId="$AZURE_AD_BROKER_CLIENT_ID" \
    AzureAd__ClientSecret="$AZURE_AD_BROKER_SECRET" \
    AzureAd__CallbackPath=/signin-oidc
  if [ -n "$AZURE_AD_API_CLIENT_ID" ]; then
    # Broker prosi o token do Car Rental API (panel pracownika) z tym scope
    az webapp config appsettings set -g "$RG" -n "$BROKER_APP" -o none --settings \
      Providers__Our__Scope="api://$AZURE_AD_API_CLIENT_ID/access_as_user"
  fi
  echo "Broker API: AzureAd ustawione."
else
  echo "Broker API: AzureAd pominięte (brak AZURE_AD_TENANT_ID / AZURE_AD_BROKER_CLIENT_ID / AZURE_AD_BROKER_SECRET)."
fi

# ---------- 6. Tożsamość dla ADO (service connection sc-azure-carrentals) ----------
step "6. Managed identity $ADO_IDENTITY z rolą Contributor na $RG"
if ! az identity show -g "$RG" -n "$ADO_IDENTITY" -o none 2>/dev/null; then
  az identity create -g "$RG" -n "$ADO_IDENTITY" -l "$LOC" -o none
fi
MI_PRINCIPAL=$(az identity show -g "$RG" -n "$ADO_IDENTITY" --query principalId -o tsv)
MI_CLIENT=$(az identity show -g "$RG" -n "$ADO_IDENTITY" --query clientId -o tsv)
RG_ID=$(az group show -n "$RG" --query id -o tsv)
if [ -z "$(az role assignment list --assignee "$MI_PRINCIPAL" --scope "$RG_ID" --role Contributor --query "[].id" -o tsv)" ]; then
  # Świeża tożsamość czasem nie jest jeszcze widoczna w Entra, wtedy chwilę czekamy i próbujemy znowu
  for i in 1 2 3 4 5 6; do
    if az role assignment create --assignee-object-id "$MI_PRINCIPAL" --assignee-principal-type ServicePrincipal \
         --role Contributor --scope "$RG_ID" -o none; then break; fi
    echo "Czekam na tożsamość ($i)..."; sleep 10
  done
fi

# ---------- 7. Zapis sekretów (poza repo) ----------
step "7. Sekrety do $SECRETS_FILE"
umask 077
cat > "$SECRETS_FILE" <<EOF
# Car Rentals, sekrety z $(date -u +%Y-%m-%dT%H:%MZ). Przenieś do menedżera haseł, potem usuń ten plik.
SQL_ADMIN=$SQL_ADMIN
SQL_PASS='$SQL_PASS'
KEY_BROKER=$KEY_BROKER
KEY_OTHER=$KEY_OTHER
CARRENTAL_DB_CONNECTION='$CS_CARRENTAL'
BROKER_DB_CONNECTION='$CS_BROKER'
APPLICATIONINSIGHTS_CONNECTION_STRING='$AI_CS'
EOF
echo "Zapisane. Podgląd: cat $SECRETS_FILE"

# ---------- Podsumowanie ----------
step "Gotowe"
cat <<EOF
Region:            $LOC
Resource group:    $RG
Car Rental API:    https://$API_HOST   (health: /health, Swagger: /swagger)
Broker + React:    https://$BROKER_HOST
SQL:               $SQL_HOST  (bazy: $SQL_DB_CARRENTAL, $SQL_DB_BROKER)

Do kreatora service connection w ADO (sc-azure-carrentals, "App registration or Managed identity (manual)",
Workload identity federation):
  Subscription ID:  $SUB_ID
  Subscription:     $SUB_NAME
  Tenant ID:        $TENANT_ID
  Client ID (MI):   $MI_CLIENT

Po skopiowaniu Issuer i Subject identifier z kreatora ADO wykonaj:
  az identity federated-credential create -g $RG --identity-name $ADO_IDENTITY -n ado-sc-azure-carrentals \\
    --issuer "<ISSUER>" --subject "<SUBJECT>" --audiences api://AzureADTokenExchange

Variable group "carrentals-dev" w ADO Library (oba jako secret):
  CARRENTAL_DB_CONNECTION, BROKER_DB_CONNECTION  (wartości w $SECRETS_FILE)

Klucz KEY_OTHER przekazujemy drugiej grupie prywatnie. Nie wklejamy go do repo ani na publiczny kanał.
EOF
