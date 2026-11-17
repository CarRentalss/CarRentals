# Car Rental API (nasza wypożyczalnia)

API naszej wypożyczalni w projekcie Car Rentals. Korzystają z niego brokerzy: nasz i broker innej grupy.
Zakres na KM2: epik 33 „API naszego CarRental” i #141 „przygotowanie CarDB”, do tego backend do panelu pracownika (#100, #101).

## Co jest zrobione

| Endpoint | Kto | Co robi | ADO |
|---|---|---|---|
| `GET /api/v1/cars` | broker | lista aut, filtry `make`, `model`, `year` | #117, #118 |
| `GET /api/v1/cars/{id}` | broker | jedno auto (strona szczegółów) | #117 |
| `POST /api/v1/offers` | broker | oferta z wyceną, ważna 10 min | #119 |
| `POST /api/v1/rentals` | broker | wniosek o wynajem, odpowiedź 202 Pending | #120, #131 |
| `GET /api/v1/rentals/{rentalId}` | broker | status wynajmu | #121, #132 |
| `POST /api/v1/rentals/{rentalId}/return` | broker | zgłoszenie zwrotu | #125 |
| `GET /api/v1/employee/rentals?status=` | pracownik | lista wynajmów naszej firmy z filtrem | #101, #129 |
| `GET /api/v1/employee/rentals/{rentalId}` | pracownik | szczegóły | #101 |
| `POST /api/v1/employee/rentals/{rentalId}/confirm-return` | pracownik | potwierdzenie zwrotu (notatka + jpg/png/pdf) | #126 |
| `GET /api/v1/employee/rentals/{rentalId}/attachment` | pracownik | pobranie załącznika | #126 |
| `GET /health` | każdy | czy API żyje | |

Konwencja tras: wszystkie endpointy są pod `/api/v1/...` (tak samo w Brokerze). Rzeczowniki w liczbie mnogiej, małe litery, akcje w kebab-case (`confirm-return`), parametry nazwane (`{rentalId:guid}`, `{id:int}`). Tylko `/health` jest bez prefiksu, bo sprawdza go App Service i pipeline.

Najważniejsze zasady:

- Wycena: cena bazowa auta razy 1,25, gdy kierowca ma mniej niż 25 lat, i razy 1,2, gdy prawo jazdy ma krócej niż 3 lata. To samo dotyczy ubezpieczenia. Wartości są w `appsettings.json` w sekcji `Pricing`.
- Oferta jest ważna 10 minut (`Offers:ValidityMinutes`). Po tym czasie wynajem zwraca 410.
- Dostępność sprawdzamy przy ofercie i drugi raz przy wniosku, w transakcji Serializable. Jeśli auto jest już zajęte, dostajesz 409. Dzień oddania auta jest wolny dla kolejnego klienta.
- Statusy: najpierw `Pending`, potem `Confirmed` albo `Rejected`, a po zgłoszeniu zwrotu `ReturnRequested` i na końcu `Completed`. Wniosek nie jest potwierdzany od razu. Po około 10 s potwierdza go proces w tle (`RentalProcessing`), a broker w tym czasie odpytuje `GET /api/v1/rentals/{id}`. Przejścia statusów są w encji `Rental` (`RequestReturn`, `CompleteReturn`, `Process`), serwisy tylko je wywołują.
- Klucze brokerów: broker łączy się kluczem `x-api-key`, każdy ma swój. Broker widzi tylko swoje oferty i wynajmy, cudzy wynajem zwraca 404.
- Endpointy `/api/v1/employee` wymagają roli Employee (token z Entra ID). Sam klucz brokera daje 401, a zalogowany użytkownik bez roli dostaje 403. Jak poza Development nie ma `AzureAd:ClientId` i `AzureAd:TenantId`, API i tak wstaje (brokerzy działają), w logu jest ostrzeżenie, a panel pracownika jest zamknięty (zawsze 401).
- Pliki zwrotu: tylko jpg, png i pdf do 10 MB. Sprawdzamy też pierwsze bajty pliku, więc przemianowany `.exe` nie przejdzie. Pliki zapisujemy lokalnie w `App_Data/attachments`. W KM3 podmienimy to na Azure Blob, bo interfejs `IFileStorage` jest już gotowy.
- Audyt: każde wywołanie jest logowane przez `AuditMiddleware`: metoda, ścieżka, status, czas w ms, który broker albo pracownik, nagłówki z listy dozwolonych (`User-Agent`, `Content-Type`, `Content-Length`, `X-Correlation-Id`, `traceparent`) i treść JSON do 4 KB. E-mail, imię i nazwisko są maskowane (`j***@example.com`, `K***`). Nigdy nie logujemy `x-api-key`, `Authorization` ani `Cookie`, a query stringa i plików w ogóle.
- Application Insights: gdy jest ustawione `APPLICATIONINSIGHTS_CONNECTION_STRING`, API wysyła requesty, zależności (SQL) i logi do Application Insights (`UseAzureMonitor`, OpenTelemetry). Bez tej zmiennej nic nie jest wysyłane, więc lokalnie i w testach nic nie trzeba ustawiać.
- HTTPS: poza Development API wysyła nagłówek HSTS (`UseHsts`). Na App Service włączamy HTTPS Only (robi to skrypt `scripts/azure/setup-resources.sh`).

## Wymagania

- .NET 10 SDK. Opis projektu mówi o .NET 8 lub 9, ale te wersje kończą wsparcie 10.11.2026, dlatego domyślnie mamy .NET 10. Kto ma tylko .NET 8 SDK, może zbudować i przetestować ten sam kod przełącznikiem: `dotnet test -p:AppTargetFramework=net8.0`. Migracji na net8 nie generujemy. Wersja frameworka i paczek (też poprawionej `System.Security.Cryptography.Pkcs`) jest w jednym miejscu, w `Directory.Build.props`.
- Docker do testów bazy (`tests/CarRental.Api.IntegrationTests`, SQL Server w kontenerze) i do skryptu migracji, jak ktoś nie ma .NET 10 SDK.
- Baza, do wyboru:
  - SQL Server LocalDB (instaluje się z Visual Studio), albo
  - Docker (wtedy `docker compose up`, opis niżej).

## Uruchomienie lokalnie

Wszystkie komendy uruchamiamy z katalogu głównego repo.

```bash
# 1. Testy endpointów i wyceny (bez bazy, SQLite w pamięci)
dotnet test tests/CarRental.Api.Tests

# 2. Testy bazy na SQL Server w kontenerze (potrzebny działający Docker)
dotnet test tests/CarRental.Api.IntegrationTests

# 3. Start API (w Development baza tworzy się sama przy starcie: z migracji, a dopóki ich nie ma, EnsureCreated)
dotnet run --project backend/CarRental.Api
```

Swagger jest pod adresem http://localhost:5080/swagger. Kliknij Authorize i wpisz klucz `dev-our-broker-key-change-me`. Swagger włącza flaga `Swagger:Enabled`: w Development jest `true`, w `appsettings.json` jest `false`, a w Azure włączamy ją App Settingiem `Swagger__Enabled=true`. Swagger nie otwiera API, bo każde wywołanie i tak wymaga klucza.

### Migracje EF

Baza ma powstawać z migracji EF (folder `backend/CarRental.Api/Migrations`). Folderu jeszcze nie ma w repo, migrację generuje Maks skryptem niżej. Seed 12 aut jest w `CarConfiguration` (`HasData`), więc trafia do migracji. Przy starcie w Development API robi `Migrate`, a jak migracji jeszcze nie ma, tworzy bazę z modelu (`EnsureCreated`). Na Azure bazę aktualizuje bundle migracji w etapie Deploy, a nie start aplikacji (`Database__ApplyMigrationsOnStartup=false`).

Migrację generujemy skryptem z katalogu repo. Robi ją dla obu baz: CarRental DB (`CarRentalDbContext`) i Broker DB (`BrokerDbContext`, jak projektu Brokera jeszcze nie ma, skrypt go pomija):

```powershell
# Windows, przez Dockera (obraz mcr.microsoft.com/dotnet/sdk:10.0), nie trzeba .NET 10 SDK
.\scripts\add-migrations.ps1

# Windows, z lokalnym .NET 10 SDK
.\scripts\add-migrations.ps1 -Local

# Kolejna migracja po zmianie modelu
.\scripts\add-migrations.ps1 -Name AddRentalDocuments
```

Na Linuksie i macOS to samo robi `./scripts/add-migrations.sh` (`--local`, `--name`). Skrypt woła `dotnet tool restore` (dotnet-ef 10.0 z `.config/dotnet-tools.json`) i `dotnet ef migrations add`. Baza nie jest do tego potrzebna, bo `dotnet ef` używa fabryki `Data/CarRentalDbContextFactory.cs` zamiast `Program.cs` (connection string bierze ze zmiennej `CARRENTAL_DB_CONNECTION` albo `ConnectionStrings__CarRentalDb`, a domyślnie z LocalDB).

Jeśli lokalna baza powstała wcześniej przez `EnsureCreated`, usuń ją przed pierwszym startem z migracjami: `dotnet ef database drop --project backend/CarRental.Api` (LocalDB) albo `docker compose down -v` (Docker).

### Albo przez Dockera (bez LocalDB)

```bash
docker compose up --build
```

To uruchomi SQL Server, Car Rental API (http://localhost:5080/swagger) i Brokera z Reactem (http://localhost:5100). Obie bazy, `CarRentalDb` i `BrokerDb`, są na tym samym SQL Serverze w kontenerze. Broker woła nasze API pod `http://carrental-api:8080` kluczem dev.

## Przykładowy przebieg (curl)

```bash
KEY="x-api-key: dev-our-broker-key-change-me"

curl -H "$KEY" http://localhost:5080/api/v1/cars

curl -H "$KEY" -H "Content-Type: application/json" -X POST http://localhost:5080/api/v1/offers \
  -d '{"carId":1,"age":22,"licenseYears":2,"location":"Warszawa","startDate":"2026-11-20","endDate":"2026-11-23"}'

curl -H "$KEY" -H "Content-Type: application/json" -X POST http://localhost:5080/api/v1/rentals \
  -d '{"offerId":"<offerId z poprzedniej odpowiedzi>","firstName":"Jan","lastName":"Kowalski","email":"jan@example.com"}'

curl -H "$KEY" http://localhost:5080/api/v1/rentals/<rentalId>     # po ~10 s status = Confirmed
```

### Pracownik lokalnie (zanim będzie Entra ID)

Dopóki aplikacja nie jest zarejestrowana w Entra ID (#58), lokalnie zamiast tokena działają dwa nagłówki:

```bash
curl -H "X-Dev-Employee: anna" -H "X-Dev-Roles: Employee" "http://localhost:5080/api/v1/employee/rentals?status=ReturnRequested"
```

Działa to tylko w środowisku Development (i w testach) i tylko wtedy, gdy `AzureAd:ClientId` jest puste. Gdy wpiszemy `AzureAd:TenantId` i `AzureAd:ClientId`, API przyjmuje już wyłącznie prawdziwe tokeny z rolą `Employee` (claim `roles`).

## Konfiguracja i sekrety

- Klucze w repo: `appsettings.Development.json` zawiera tylko klucze deweloperskie.
- Klucze na Azure: prawdziwe klucze ustawiamy w App Service (Configuration):

  ```
  ApiKeys__Clients__0__Name = OurBroker
  ApiKeys__Clients__0__Key  = <długi losowy klucz>
  ApiKeys__Clients__1__Name = OtherGroupBroker
  ApiKeys__Clients__1__Key  = <drugi klucz>
  ConnectionStrings__CarRentalDb = <connection string do Azure SQL>   (skrypt Azure ustawia go jako Connection string typu SQLAzure, działa tak samo)
  AzureAd__TenantId = ...
  AzureAd__ClientId = ...
  Database__ApplyMigrationsOnStartup = false
  Swagger__Enabled = true          (na demo)
  APPLICATIONINSIGHTS_CONNECTION_STRING = <z zasobu Application Insights>
  ```

- Klucze lokalnie: można je trzymać w User Secrets: `dotnet user-secrets set "ApiKeys:Clients:0:Key" "..." --project backend/CarRental.Api`.
- Walidacja przy starcie: bez kluczy, albo z kluczem krótszym niż 16 znaków, API nie wystartuje. To celowe.

## CI (Azure Pipelines)

`azure-pipelines.yml` w katalogu głównym robi:

- restore, skan podatnych paczek (`dotnet list package --vulnerable --include-transitive`, przerywa build, gdy coś znajdzie) i build z analizatorami Roslyn,
- publish API jako artefakt i bundle migracji EF (budowany zawsze, ale bez migracji w repo błąd jest tylko ostrzeżeniem, a Deploy go nie uruchamia),
- testy z raportem pokrycia i progiem 40%,
- deploy na App Service, ale dopiero po włączeniu `deployToAzure` (czeka na subskrypcję Azure i service connection).

Pipeline „Jagna676.CarRentals” w ADO jest podpięty do repo na GitHubie i czyta ten plik. Nowej wersji YAML nikt jeszcze nie odpalił w ADO, pierwszy przebieg będzie po pushu. Cały opis jest w głównym README.

## Struktura

```
backend/CarRental.Api
  Controllers/   endpointy (cienkie, logika jest w Services)
  Services/      oferty, wynajmy, panel pracownika, walidacja plików
  Pricing/       wycena
  Domain/        encje (osobny plik na klasę): Car, Offer, Rental, RentalStatus, z metodami domenowymi
  Data/          DbContext (EF Core), fabryka design-time, auta na start (seed)
  Data/Configurations/  mapowanie tabel, indeksy i seed (IEntityTypeConfiguration, tylko fluent)
  Migrations/    migracje EF (generowane skryptem scripts/add-migrations.*)
  Auth/          klucz API brokera, zastępczy login pracownika do pracy lokalnej
  Background/    potwierdzanie wniosków w tle
  Storage/       zapis załączników (lokalnie, później Azure Blob)
  Middleware/    audyt wywołań i maskowanie danych osobowych
tests/CarRental.Api.Tests
  testy jednostkowe (wycena, encje, audyt) + testy integracyjne każdego endpointu (WebApplicationFactory + SQLite w pamięci)
tests/CarRental.Api.IntegrationTests
  testy bazy na SQL Server 2022 w kontenerze (Testcontainers): seed 12 aut, indeksy, unikalny OfferId,
  Status jako tekst, Restrict przy usuwaniu auta, RentalService w transakcji Serializable
```

EF konfigurujemy tylko fluent API w `Data/Configurations`, encje w `Domain` nie mają żadnych atrybutów. Atrybuty (`[Required]`, `[Range]`, `[EmailAddress]`) są tylko w DTO w `Contracts` i w klasach opcji, do walidacji żądań i ustawień.

## Co dalej (KM3)

- `IFileStorage` na Azure Blob Storage (prywatny kontener) (#81, #104)
- Faktura PDF (QuestPDF) i mail po potwierdzeniu zwrotu (#82, #107, #108)
- Korelacja wywołań Broker do Car Rental API w Application Insights (#39)
- Rejestracja w Entra ID i rola Employee (#58, #100)
