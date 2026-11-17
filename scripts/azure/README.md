# Azure dla Car Rentals (KM2)

Tu jest wszystko, co trzeba kliknąć albo uruchomić, żeby pipeline mógł wdrożyć aplikację na Azure. Skrypt zakłada zasoby, ale logowania, zgody i kreatory w ADO robi człowiek (Maks). Całość to jedno popołudnie, około 3 godzin.

Nazwy zasobów są stałe i takie same w `azure-pipelines.yml`. Jak nazwa App Service albo serwera SQL jest zajęta (są globalnie unikalne), dokładamy sufiks `-jkm` i zmieniamy ją też w YAML (zmienne `apiAppName`, `brokerAppName`).

| Zasób | Nazwa |
|---|---|
| Resource group | rg-carrentals |
| App Service Plan | asp-carrentals (Linux, B1) |
| Car Rental API | app-carrentals-api (.NET 10) |
| Broker API + React | app-carrentals-broker (.NET 10) |
| Azure SQL | sql-carrentals-jkm, bazy sqldb-carrental i sqldb-broker (Basic 5 DTU) |
| Monitoring | log-carrentals + appi-carrentals |
| Tożsamość dla ADO | id-carrentals-ado (Contributor na rg-carrentals) |
| Service connection w ADO | sc-azure-carrentals |
| Environment w ADO | dev |
| Variable group w ADO | carrentals-dev |

Koszt: około 23 USD miesięcznie (B1 około 13 USD i dwie bazy Basic po około 5 USD), z kredytu Azure for Students 100 USD. Kwoty sprawdzić w kalkulatorze dla wybranego regionu.

## Kolejność

### 1. Subskrypcja i region (Maks, 15 min)

1. Azure for Students na koncie PW: https://azure.microsoft.com/free/students, "Start free", weryfikacja mailem uczelnianym.
2. Dozwolone regiony: portal, Policy, Assignments, "Allowed resource deployment regions", parametr Allowed locations. Albo w Cloud Shell:
   ```bash
   az policy assignment list --disable-scope-strict-match --query "[].{name:displayName, params:parameters}" -o json
   ```
3. Bierzemy pierwszy z tej kolejności, który jest na liście: polandcentral, swedencentral, northeurope, westeurope, eastus. Wszystko w jednym regionie. Faktyczny region wpisujemy do README w sekcji o Azure.

Jak u Maksa nie wyjdzie: subskrypcja Jagi albo Karoliny, a Maks dostaje rolę Owner na rg-carrentals (Access control (IAM), Add role assignment). Owner, a nie Contributor, bo Maks nadaje rolę tożsamości dla ADO.

### 2. Skrypt w Cloud Shell (Maks, 20 min)

shell.azure.com, tryb Bash. Wgrywamy `setup-resources.sh` (Upload w Cloud Shell albo `git clone` repo) i uruchamiamy z regionem z kroku 1:

```bash
bash setup-resources.sh polandcentral jkm
```

Skrypt zakłada: providery, rg-carrentals, log-carrentals i appi-carrentals (limit 0,1 GB dziennie), serwer SQL z TLS 1.2, regułę firewall AllowAzureServices, dwie bazy Basic, plan B1, dwie aplikacje .NET 10 z HTTPS only, TLS 1.2, wyłączonym FTP i health checkiem na /health, App Settings obu aplikacji, tożsamość id-carrentals-ado z rolą Contributor na rg-carrentals.

Hasło SQL i klucze brokerów generuje sam. Zapisuje je w `~/carrentals-secrets.env` w Cloud Shell. Przenosimy je do menedżera haseł i do ADO Library, potem plik usuwamy. Nic z tego nie idzie do repo.

Opcje:
- `MY_IP=1.2.3.4 bash setup-resources.sh ...` dodaje regułę firewall dla twojego IP (podgląd bazy w SSMS albo Azure Data Studio).
- `API_APP=app-carrentals-api-jkm BROKER_APP=app-carrentals-broker-jkm bash setup-resources.sh ...` gdy nazwy są zajęte.
- Drugie uruchomienie pomija istniejące zasoby. Żeby nie zmieniać hasła i kluczy, najpierw `source ~/carrentals-secrets.env`, potem `SQL_PASS="$SQL_PASS" KEY_BROKER=$KEY_BROKER KEY_OTHER=$KEY_OTHER bash setup-resources.sh ...`.

Na koniec skrypt wypisuje Subscription ID, Tenant ID i Client ID tożsamości. Będą potrzebne w kroku 3.

### 3. Service connection sc-azure-carrentals w ADO (Maks, 20 min)

Nie wymaga prawa do rejestracji aplikacji w Entra, bo używamy managed identity.

1. ADO, Project settings, Service connections, New service connection, Azure Resource Manager.
2. Identity type: "App registration or Managed identity (manual)". Credential: Workload identity federation.
3. Service connection name: `sc-azure-carrentals`. Scope level: Subscription. Subscription ID i nazwa, Tenant ID i Client ID (tożsamości) z wyniku skryptu.
4. Skopiować "Issuer" i "Subject identifier". Kliknąć "Keep as draft".
5. W Cloud Shell:
   ```bash
   az identity federated-credential create -g rg-carrentals --identity-name id-carrentals-ado -n ado-sc-azure-carrentals \
     --issuer "<ISSUER>" --subject "<SUBJECT>" --audiences api://AzureADTokenExchange
   ```
6. Wrócić do połączenia, "Verify and save".
7. Security połączenia: NIE zaznaczać "Grant access permission to all pipelines". Pipeline Jagna676.CarRentals dostanie dostęp przyciskiem "Permit" przy pierwszym przebiegu.

Jak przycisk "New service connection" jest wyszarzony, Maks nie ma uprawnień. Admin organizacji CarRentalJKM dodaje go do Project Administrators.

### 4. Environment i variable group w ADO (Maks, 10 min)

- Pipelines, Environments, New environment: `dev`, Resource: None. W "Approvals and checks" dodajemy "Branch control" z Allowed branches `refs/heads/main`. Dzięki temu na Azure trafia tylko main.
- Pipelines, Library, + Variable group: `carrentals-dev`. Zmienne (każda z kłódką, czyli secret):
  - `CARRENTAL_DB_CONNECTION` = wartość z `~/carrentals-secrets.env`
  - `BROKER_DB_CONNECTION` = wartość z `~/carrentals-secrets.env`
- W "Pipeline permissions" grupy dodajemy Jagna676.CarRentals (albo "Permit" przy pierwszym przebiegu).

Environment i variable group da się założyć nawet przed subskrypcją Azure.

### 5. Rejestracja aplikacji w Entra (Maks, 30 min)

Najpierw test: entra.microsoft.com, App registrations, New registration (kontem PW). Jak przechodzi, robimy dwie rejestracje w tenancie PW. Jak "brak uprawnień", patrz plan B niżej.

carrentals-api (Car Rental API, przyjmuje tokeny pracownika):
1. New registration, nazwa `carrentals-api`, Supported account types: Single tenant. Bez redirect URI.
2. Expose an API: Application ID URI `api://<clientId>` (domyślne). Add a scope: `access_as_user`, Who can consent: Admins and users, opis "Dostęp do Car Rental API w imieniu użytkownika".
3. App roles, Create app role: Display name `Employee`, Allowed member types: Users/Groups, Value `Employee`, opis "Pracownik wypożyczalni".
4. Manifest: `"requestedAccessTokenVersion": 2` (w nowym manifeście `api.requestedAccessTokenVersion`). Car Rental API sprawdza issuer w wersji v2.0, więc z tokenem v1 walidacja padnie.
5. Enterprise applications, carrentals-api, Users and groups, Add user/group: osoba, która gra pracownika na demo, z rolą Employee.
6. Do App Settings app-carrentals-api: `AzureAd__TenantId`, `AzureAd__ClientId` (z Overview).

carrentals-broker (Broker, logowanie użytkownika, OIDC):
1. New registration, nazwa `carrentals-broker`, Single tenant, platforma Web.
2. Redirect URIs (Authentication, Web):
   - `https://app-carrentals-broker.azurewebsites.net/signin-oidc`
   - `http://localhost:5100/signin-oidc` (Entra pozwala na http tylko dla localhost)
   - `https://localhost:7100/signin-oidc`
   Front-channel logout URL: `https://app-carrentals-broker.azurewebsites.net/signout-callback-oidc`. Zaznaczyć "ID tokens".
3. Certificates & secrets, New client secret na 6 miesięcy. Wartość od razu do App Settings (`AzureAd__ClientSecret`), nie do repo.
4. API permissions, Add a permission, My APIs, carrentals-api, Delegated, `access_as_user`. Jak się da: "Grant admin consent".
5. App roles: tak samo jak wyżej, rola `Employee`. Broker sprawdza rolę z własnego ID tokena, a ID token ma tylko role nadane w tej rejestracji. Pracownikowi przypisujemy rolę Employee w obu Enterprise applications (carrentals-api i carrentals-broker).
6. Do App Settings app-carrentals-broker: `AzureAd__Instance=https://login.microsoftonline.com/`, `AzureAd__TenantId`, `AzureAd__ClientId`, `AzureAd__ClientSecret`, `Providers__Our__Scope=api://<clientId carrentals-api>/access_as_user`.

Wszystkie wpisy z punktu 6 robi też skrypt, jeśli podamy zmienne:

```bash
source ~/carrentals-secrets.env
AZURE_AD_TENANT_ID=... AZURE_AD_API_CLIENT_ID=... AZURE_AD_BROKER_CLIENT_ID=... AZURE_AD_BROKER_SECRET='...' \
SQL_PASS="$SQL_PASS" KEY_BROKER=$KEY_BROKER KEY_OTHER=$KEY_OTHER bash setup-resources.sh polandcentral jkm
```

Lokalnie Broker dostaje te same wartości przez User Secrets (`dotnet user-secrets set "AzureAd:ClientId" "..." --project backend/Broker.Api`). Do czasu rejestracji lokalnie działa dev-login.

Plan B, gdy tenant PW blokuje rejestrację (decyzja z Jagą do śr 12:00):
- Własny tenant: entra.microsoft.com, Manage tenants, Create, Microsoft Entra ID, nazwa `carrentalsjkm`. W swoim tenancie jesteśmy Global Admin, więc rejestracje robimy jak wyżej. Supported account types: "Any organizational directory and personal Microsoft accounts", żeby dało się zalogować kontem prywatnym. Konta PW mogą wtedy dostać "Need admin approval", więc na demo logujemy się kontem z naszego tenanta albo prywatnym.
- Google: console.cloud.google.com, nowy projekt, OAuth consent screen "External" w trybie Testing z mailami testowymi, Credentials, OAuth client ID typu Web, redirect URI te same adresy `/signin-oidc`. Rola pracownika wtedy z listy maili w konfiguracji Brokera, a Car Rental API trzeba przełączyć z tokenów Entra na inny mechanizm dla /employee. To większa zmiana w kodzie, więc wybieramy ją tylko, jak własny tenant też nie wyjdzie.

Tak czy inaczej demo nie zależy od Entra. Lokalnie działa dev-login.

### 6. Role dla zespołu (Maks, 5 min)

```bash
az role assignment create --assignee <login_jagi>@pw.edu.pl --role Contributor --scope $(az group show -n rg-carrentals --query id -o tsv)
az role assignment create --assignee <login_karoliny>@pw.edu.pl --role Contributor --scope $(az group show -n rg-carrentals --query id -o tsv)
```

### 7. Włączenie Deploy w pipeline (Maks, 30-60 min)

1. W `azure-pipelines.yml` zmienić `deployToAzure: 'false'` na `'true'`. PR do develop, potem PR develop do main.
2. Przy pierwszym przebiegu na main kliknąć "Permit" dla sc-azure-carrentals, carrentals-dev i dev.
3. Sprawdzić:
   - log kroku "Aktualizacja CarRental DB" (bundle stosuje InitialCreate),
   - `https://app-carrentals-api.azurewebsites.net/health` zwraca 200,
   - Swagger pod `/swagger` (Swagger__Enabled=true),
   - App Service, Configuration: `APP_VERSION` ma numer builda (np. 1.2.7),
   - portal, sqldb-carrental, Query editor: `SELECT COUNT(*) FROM Cars` daje 12,
   - Application Insights, Live metrics albo Transaction search: widać requesty.
4. 10 minut przed demo otworzyć oba adresy, żeby aplikacje się rozgrzały.

## Co sprawdzić, jak coś nie działa

- Krok migracji: "Login failed" albo timeout. Sprawdź regułę AllowAzureServices na serwerze SQL i wartość CARRENTAL_DB_CONNECTION w variable group.
- AzureWebApp@1: "Resource not found". Nazwa aplikacji w YAML inna niż w portalu (sufiks).
- /health nie odpowiada: App Service, Log stream. Najczęściej brak kluczy ApiKeys__Clients (API nie wstaje bez nich) albo connection stringa.
- Pipeline w ogóle nie rusza po zmianie na 'true': nie ma service connection, variable group albo environment o tych nazwach.
