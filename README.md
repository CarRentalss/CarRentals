# Car Rentals

Aplikacja webowa do wyszukiwania i porównywania ofert wynajmu aut z kilku wypożyczalni. Projekt na przedmiot "Tworzenie aplikacji webowych z wykorzystaniem .NET" (PW, 2026).

Użytkownik wyszukuje auto bez logowania, sprawdza ceny u wypożyczalni, a po zalogowaniu ma profil, z którego wycena bierze dane. Pracownik naszej wypożyczalni ma panel z wynajmami naszej firmy. Oprócz aplikacji pośredniczącej (Broker) mamy własne Car Rental API, czyli naszą wypożyczalnię, z której korzysta nasz Broker i Broker innej grupy.

Stan na KM2 (19.11.2026): w kodzie są React, Broker API, Car Rental API, dwie bazy (EF, konfiguracja fluent, seed), panel pracownika (lista i szczegóły) i pipeline z etapami Build, Test i Deploy. Migracje generujemy skryptem (opis niżej), a deploy na Azure włączamy, jak będą zasoby i service connection. W KM3 dochodzą: wynajem przez Brokera (Rent Me), maile do klientów, pliki w Azure Blob, API prowadzącego (czekamy na dokumentację) i API innej grupy.

## Architektura

```
Przeglądarka
  |  HTTPS, ciasteczko sesji (ta sama domena)
  v
Broker API (BFF, .NET 10) + React z wwwroot ----OIDC----> Entra ID
  |  x-api-key            |                 \
  v                       v                  \--> API prowadzącego, API innej grupy (KM3)
Car Rental API (.NET 10)  Broker DB (Azure SQL)
  |
  v
CarRental DB (Azure SQL)
```

- React (Vite + TypeScript) woła tylko `/api/v1/...` Brokera. Broker serwuje zbudowany React z `wwwroot`, więc front i API są na jednej domenie i ciasteczko sesji jest first-party.
- Broker loguje użytkownika (Microsoft.Identity.Web, OIDC), trzyma profil i wyceny w Broker DB i pyta wypożyczalnie równolegle z limitem 15 s.
- Car Rental API to nasza wypożyczalnia: katalog, oferty, wynajmy, zwroty, panel pracownika. Brokerzy łączą się kluczem `x-api-key`, każdy ma swój.
- Car Rental API wysyła telemetrię do Application Insights, gdy jest ustawione `APPLICATIONINSIGHTS_CONNECTION_STRING`. Broker na razie zapisuje audyt tylko do zwykłego logu, Application Insights podpinamy w nim w KM3.

Diagram komponentów z podziałem na KM2 i KM3: [docs/architecture.md](docs/architecture.md). Diagram ER obu baz: [docs/er-diagram.md](docs/er-diagram.md). Decyzje i ryzyka: [docs/decisions-risks-km2.md](docs/decisions-risks-km2.md).

## Struktura repo

```
backend/
  CarRental.Api/                  nasze Car Rental API (wypożyczalnia)
  Broker.Api/                     Broker API (BFF): logowanie, profil, katalog, wycena, proxy panelu pracownika
frontend/                         React + TypeScript (Vite), build do frontend/dist
tests/
  CarRental.Api.Tests/            testy jednostkowe i integracyjne endpointów (WebApplicationFactory, SQLite w pamięci)
  CarRental.Api.IntegrationTests/ testy bazy na prawdziwym SQL Server (Testcontainers)
  Broker.Api.Tests/               testy Brokera (agregator, profil, integracja z Car Rental API)
docs/                             architektura, diagram ER, kontrakt Brokera, Postman, teksty do ADO, demo
scripts/
  add-migrations.ps1, .sh         generowanie migracji EF dla obu baz
  azure/                          zakładanie zasobów Azure (Cloud Shell) i instrukcja
azure-pipelines.yml               CI/CD (Build, Test, Deploy)
docker-compose.yml                SQL Server + Car Rental API + Broker z Reactem
Directory.Build.props             wspólne ustawienia (.NET 10, wersje paczek, analizatory)
```

## Uruchomienie lokalne

Potrzebne: .NET 10 SDK, Node.js 22, Docker Desktop. Komendy uruchamiamy z katalogu głównego repo.

### Najprościej: docker compose

```bash
docker compose up --build
```

- Broker z Reactem: http://localhost:5100 (Swagger: http://localhost:5100/swagger)
- Car Rental API: http://localhost:5080/swagger (Authorize, klucz `dev-our-broker-key-change-me`)
- SQL Server: localhost,1433, użytkownik `sa`, hasło z `SQL_SA_PASSWORD` (domyślnie `Local_dev_Passw0rd!`, tylko lokalnie)

Bazy CarRentalDb i BrokerDb tworzą się same przy starcie (w Development API robi Migrate, a jak w projekcie nie ma jeszcze migracji, EnsureCreated). Po dodaniu migracji usuń stare bazy: `docker compose down -v`.

Logowanie lokalnie działa bez Entra ID (dev-login): przycisk Zaloguj w aplikacji albo np. http://localhost:5100/api/v1/auth/dev-login?name=anna&role=Employee&returnUrl=/employee/rentals (pracownik). Działa tylko w Development.

### Każda część osobno (do pracy nad kodem)

```bash
# 1. Car Rental API, http://localhost:5080 (baza: LocalDB albo SQL z docker compose)
dotnet run --project backend/CarRental.Api

# 2. Broker API, http://localhost:5100 (woła Car Rental API na localhost:5080)
dotnet run --project backend/Broker.Api

# 3. React, http://localhost:5173 (Vite przekazuje /api do Brokera na 5100)
cd frontend
npm ci
npm run dev
```

Bez LocalDB (np. na Linuxie albo Macu) można postawić sam SQL z compose (`docker compose up sql`) i podać connection string:

```bash
ConnectionStrings__CarRentalDb="Server=localhost,1433;Database=CarRentalDb;User Id=sa;Password=Local_dev_Passw0rd!;TrustServerCertificate=True" dotnet run --project backend/CarRental.Api
ConnectionStrings__BrokerDb="Server=localhost,1433;Database=BrokerDb;User Id=sa;Password=Local_dev_Passw0rd!;TrustServerCertificate=True" dotnet run --project backend/Broker.Api
```

### Migracje EF

Migracje mają być w domyślnych folderach `backend/CarRental.Api/Migrations` (CarRentalDbContext) i `backend/Broker.Api/Migrations` (BrokerDbContext). Generujemy je skryptem, który robi to dla obu baz (pierwszą, InitialCreate, odpala Maks i commituje foldery):

```powershell
# Windows, przez Dockera (nie trzeba .NET 10 SDK)
.\scripts\add-migrations.ps1
# z lokalnym .NET 10 SDK
.\scripts\add-migrations.ps1 -Local
# kolejna migracja
.\scripts\add-migrations.ps1 -Name AddRentalDocuments
```

```bash
# Linux, macOS
./scripts/add-migrations.sh [--local] [--name InitialCreate]
```

Baza nie jest do tego potrzebna, bo `dotnet ef` korzysta z fabryk design-time (`CarRentalDbContextFactory`, `BrokerDbContextFactory`), a nie z Program.cs. Migracji nie generujemy na .NET 8, bo snapshot z EF 8 różni się od modelu w EF 10.

Na Azure bazy aktualizuje pipeline (EF migrations bundle), a nie start aplikacji (`Database__ApplyMigrationsOnStartup=false`).

### .NET 8 zamiast 10

Domyślnie wszystko jest na .NET 10 (LTS). Opis projektu mówi o .NET 8 lub 9, więc mamy przełącznik w `Directory.Build.props`. Kto ma tylko .NET 8 SDK, buduje i testuje ten sam kod tak:

```bash
dotnet build -p:AppTargetFramework=net8.0
dotnet test -p:AppTargetFramework=net8.0
```

## Jak pracujemy z Gitem

Kod jest na GitHubie (Jagna676/CarRentals), a zadania, sprinty i pipeline w Azure DevOps (CarRentalJKM/CarRental).
Używamy uproszczonego gitflow. Nie robimy gałęzi release, bo jest nas troje i mamy krótkie sprinty.

Gałęzie:
- main to wersja pokazywana na checkpoincie. Z main pipeline wdraża aplikację na Azure.
- develop to bieżąca praca w sprincie. Tu trafiają wszystkie funkcje.
- feature/<numer z ADO>-<krótki opis>, np. feature/84-wyszukiwanie. Zakładamy z develop.
- hotfix/<opis>, gdy trzeba szybko poprawić coś na main. Po merge'u do main scalamy też do develop.

Zasady:
- Nikt nie pushuje bezpośrednio do main ani do develop. Wszystko idzie przez Pull Request.
- PR musi mieć zielony pipeline (Jagna676.CarRentals) i co najmniej jedno zatwierdzenie od innej osoby z zespołu.
- Scalamy przez merge commit, żeby w historii zostały wszystkie commity.
- W commitach i w opisie PR podajemy numer zadania, np. "AB#84 lista marek z API". Dzięki temu commit i PR podpinają się pod zadanie w ADO.
- Na koniec sprintu robimy PR z develop do main i dajemy tag v1.<numer checkpointu>.0, np. v1.2.0 po KM2.

Reguły wymusza ruleset na GitHubie (main i develop), który ustawia Jaga: PR z 1 approve, wymagany check z pipeline'u, zakaz force push i usuwania gałęzi, tylko merge commit. Kroki: [docs/github-ruleset.md](docs/github-ruleset.md). Szablon opisu PR jest w `.github/pull_request_template.md`.

## Wersje

Numer builda ma format 1.<numer checkpointu>.<kolejny build>, np. 1.2.17. Ustawiamy go w `azure-pipelines.yml` (`name: $(Major).$(Minor).$(Rev:r)`, Minor = numer checkpointu, licznik zeruje się sam po zmianie Minor). Ten sam numer idzie do assembly (`-p:Version`), do stopki frontu (`VITE_APP_VERSION`) i do ustawienia `APP_VERSION` na App Service, więc widać, która wersja jest wdrożona. Na koniec sprintu tag `v1.<KM>.0` na main.

## Konwencja tras

Oba API mają ten sam styl:
- prefiks z wersją `/api/v1/...`,
- rzeczowniki w liczbie mnogiej, małe litery: `/api/v1/cars`, `/api/v1/offers`, `/api/v1/rentals`,
- akcje, które nie są zwykłym CRUD, jako podzasób w kebab-case: `/api/v1/rentals/{rentalId}/return`, `/api/v1/employee/rentals/{rentalId}/confirm-return`,
- endpointy pracownika pod `/api/v1/employee/...`,
- parametry nazwane i z typem: `{id:int}`, `{rentalId:guid}`,
- `/health` bez prefiksu (dla App Service i smoke testu),
- JSON w camelCase, enumy jako tekst, błędy jako ProblemDetails (`application/problem+json`).

Kontrakt Brokera dla frontu: [docs/broker-api-contract.md](docs/broker-api-contract.md). Swagger: `/swagger` w obu API (w Development zawsze, na Azure gdy `Swagger__Enabled=true`).

## Dla innej grupy (nasze Car Rental API)

Adres na Azure: https://app-carrentals-api.azurewebsites.net (Swagger: `/swagger`), działa po pierwszym deployu. Lokalnie: http://localhost:5080.

Uwierzytelnienie: nagłówek `x-api-key` z kluczem, który dostajecie od nas prywatnie (każda grupa ma swój klucz). Bez klucza albo ze złym kluczem jest 401. Każdy broker widzi tylko swoje oferty i wynajmy, cudzy wynajem zwraca 404. Klucz dla was możemy wymienić w każdej chwili, bo trzymamy go w ustawieniach App Service, a nie w kodzie.

| Metoda i ścieżka | Co robi | Odpowiedź |
|---|---|---|
| `GET /api/v1/cars?make=&model=&year=` | lista aut (id, make, model, year, location, available) | 200 |
| `GET /api/v1/cars/{id}` | jedno auto | 200, 404 |
| `POST /api/v1/offers` | oferta z wyceną, ważna 10 minut | 201, 400, 404, 409 |
| `POST /api/v1/rentals` | wniosek o wynajem na podstawie oferty | 202 (Pending), 400, 404, 409, 410 |
| `GET /api/v1/rentals/{rentalId}` | status wynajmu | 200, 404 |
| `POST /api/v1/rentals/{rentalId}/return` | zgłoszenie zwrotu | 200, 403, 404, 409 |

Wynajem jest asynchroniczny: `POST /api/v1/rentals` zwraca 202 ze statusem `Pending`, a po około 10 s status zmienia się na `Confirmed` (albo `Rejected` z powodem). Statusu trzeba pytać przez `GET /api/v1/rentals/{rentalId}`. Statusy: `Pending`, `Confirmed`, `Rejected`, `ReturnRequested`, `Completed`.

```bash
API=https://app-carrentals-api.azurewebsites.net
KEY="x-api-key: <wasz klucz>"

# lista aut, filtr po marce
curl -H "$KEY" "$API/api/v1/cars?make=Toyota"

# oferta (daty w formacie RRRR-MM-DD, lokalizacja = miasto auta)
curl -H "$KEY" -H "Content-Type: application/json" -X POST "$API/api/v1/offers" \
  -d '{"carId":1,"age":30,"licenseYears":10,"location":"Warszawa","startDate":"2026-12-01","endDate":"2026-12-04"}'

# wniosek o wynajem (offerId z poprzedniej odpowiedzi)
curl -H "$KEY" -H "Content-Type: application/json" -X POST "$API/api/v1/rentals" \
  -d '{"offerId":"<offerId>","firstName":"Jan","lastName":"Kowalski","email":"jan@example.com"}'

# status (po około 10 s: Confirmed)
curl -H "$KEY" "$API/api/v1/rentals/<rentalId>"

# zgłoszenie zwrotu (email klienta z wniosku)
curl -H "$KEY" -H "Content-Type: application/json" -X POST "$API/api/v1/rentals/<rentalId>/return" \
  -d '{"email":"jan@example.com"}'
```

Lokalnie klucz drugiej grupy to `dev-other-group-key-change-me` (tylko Development).

## Założenia biznesowe

- Wycena: cena bazowa auta razy 1,25, gdy kierowca ma mniej niż 25 lat, i razy 1,2, gdy ma prawo jazdy krócej niż 3 lata. To samo dotyczy ubezpieczenia. Wynajem trwa najwyżej 30 dni, dzień oddania auta jest wolny dla kolejnego klienta.
- Oferta jest ważna 10 minut. Po tym czasie wynajem zwraca 410 i trzeba wziąć nową ofertę.
- Auto odbieramy w mieście, w którym stoi (inne miasto w ofercie daje 400).
- Zgłoszenie zwrotu nie kończy wynajmu. Kończy go pracownik, gdy potwierdzi odbiór z notatką i zdjęciem albo PDF-em.
- Profil trzyma datę urodzenia i datę prawa jazdy, a nie wiek i staż, bo te się zmieniają. Wiek i staż liczymy na dzień wyceny.
- Katalog aut Broker trzyma w cache (do 30 minut), więc dostępność sprawdza dopiero wycena.
- Cena końcowa przy wcześniejszym zwrocie i sposób odbioru auta: nasza propozycja to cena za faktyczne dni wynajmu, minimum 1 dzień. Pytamy prowadzącego na zajęciach.

## Wzorce w kodzie

- Adapter: `backend/Broker.Api/Providers/OurCarRental/OurCarRentalClient.cs` tłumaczy kontrakt naszego Car Rental API na wspólny model wypożyczalni (`ICarRentalProvider`). API prowadzącego i innej grupy dostaną swoje adaptery w KM3.
- Registry: `backend/Broker.Api/Providers/ProviderRegistry.cs` zna włączone wypożyczalnie i wybiera adapter po kodzie (`our`, `lecturer`, `other-group`).
- Aggregator: `backend/Broker.Api/Providers/ProviderAggregator.cs` pyta wszystkie wypożyczalnie naraz (Task.WhenAll) z limitem 15 s, a te, które nie zdążą, zwraca jako pominięte.
- Strategy: `backend/CarRental.Api/Storage/FileStorage.cs`, interfejs `IFileStorage` z implementacją `LocalFileStorage`. W KM3 dochodzi `BlobFileStorage` bez zmian w serwisach.
- Options: ustawienia jako klasy z walidacją przy starcie (`Options/AppOptions.cs` w obu API, `Providers/ProviderOptions.cs`).
- Result: `Services/ServiceResult.cs` w obu API. Serwisy zwracają wynik albo błąd (NotFound, Validation, Conflict), a kontroler zamienia go na odpowiedni kod HTTP, bez wyjątków do sterowania przepływem.
- Factory: `Data/CarRentalDbContextFactory.cs` i `Data/BrokerDbContextFactory.cs` (`IDesignTimeDbContextFactory`) tworzą DbContext dla narzędzi EF (migracje, bundle) bez uruchamiania Program.cs.
- Do tego Middleware (audyt wywołań w `Middleware/AuditMiddleware.cs`) i usługi w tle (`BackgroundService`): potwierdzanie wynajmów w Car Rental API i sprzątanie starych wycen w Brokerze.

## Baza danych i EF Core

- EF tylko fluent, atrybuty tylko w DTO do walidacji. Konfiguracja encji jest w `Data/Configurations` (`IEntityTypeConfiguration<T>`, po jednym pliku na encję, `ApplyConfigurationsFromAssembly`). Na encjach w `Domain/` nie ma żadnych atrybutów (`[Key]`, `[Required]`, `[MaxLength]` itd.).
- Atrybuty walidacji (`[Required]`, `[Range]`, `[EmailAddress]`) są tylko w kontraktach żądań w `Contracts/`, bo `[ApiController]` sam zwraca wtedy 400 z listą błędów.
- Encje (`Domain/`) i kontrakty API (`Contracts/`) to osobne klasy, mapujemy je ręcznie w serwisach.
- CarRental DB: Cars, Offers, Rentals. Broker DB: Providers, UserProfiles, Quotes. Indeksy i klucze obce są opisane w [docs/er-diagram.md](docs/er-diagram.md).
- Seed: 12 aut w CarRental DB i 3 wypożyczalnie w Broker DB (`HasData`, więc seed jest w migracji).

## Testy

```bash
# wszystkie testy .NET (testy bazy potrzebują Dockera, bo stawiają SQL Server w kontenerze)
dotnet test

# bez testów bazy (np. bez Dockera)
dotnet test --filter "Category!=Db"

# frontend
cd frontend
npm test -- --run
npm run lint
```

- `tests/CarRental.Api.Tests`: wycena (unit) i każdy endpoint Car Rental API (WebApplicationFactory, SQLite w pamięci), w tym pełny scenariusz oferta, wynajem, potwierdzenie, zwrot, potwierdzenie zwrotu.
- `tests/CarRental.Api.IntegrationTests`: baza na SQL Server 2022 w Testcontainers (migracje i seed, unikalny OfferId, status zapisany jako tekst, Restrict na kluczach obcych, transakcja przy wynajmie).
- `tests/Broker.Api.Tests`: agregator (limit 15 s, pominięcie wolnej wypożyczalni), profil, testy integracyjne Broker z prawdziwym Car Rental API uruchomionym w pamięci.
- Frontend: Vitest + Testing Library.
- Postman: proces wynajmu na lokalnym Car Rental API i wywołania Brokera z testami w zakładce Tests: [docs/postman](docs/postman). Import kolekcji i environmentu `local`, potem Runner.
- Test case'y do ADO Test Plans: [docs/ado/test-cases.md](docs/ado/test-cases.md).

## CI/CD (azure-pipelines.yml)

Pipeline Jagna676.CarRentals w ADO uruchamia się przy każdym PR do develop i main oraz po każdym merge'u.

- Build: npm ci, skan paczek npm (`npm audit --audit-level=high`), lint, build Reacta i kopia `frontend/dist` do `wwwroot` Brokera; restore, skan paczek NuGet (`dotnet list package --vulnerable --include-transitive`, przerywa build), build z analizatorami Roslyn i wersją, publish obu API, bundle migracji EF dla obu baz. Artefakty: `carrental-api`, `broker-api`, `efbundles`.
- Test: `dotnet test` całej solucji (też testy bazy w Testcontainers, agent ubuntu-latest ma Dockera), raport pokrycia (coverlet) w zakładce Code Coverage, próg pokrycia liniami 40% (`minCoverage`), testy frontu (Vitest) w zakładce Tests.
- Deploy (tylko z main, environment `dev`): migracje obu baz przez bundle, Car Rental API i Broker API (z Reactem) na App Service, `APP_VERSION`, smoke test `GET /health`.

Deploy włącza zmienna `deployToAzure` (warunek przy kompilacji YAML). Na razie jest `'false'`, bo wymaga service connection `sc-azure-carrentals`, variable group `carrentals-dev` i environment `dev`. Zasoby Azure i kolejność kroków: [scripts/azure/README.md](scripts/azure/README.md).

Sekrety (klucze brokerów, connection stringi, client secret Entra) są w ustawieniach App Service, w ADO Library i lokalnie w User Secrets. W repo są tylko klucze deweloperskie z końcówką "change-me".

## Zespół

- Maksymilian Paczyński: Car Rental API, pipeline, Azure, baza CarRental, testy bazy, rejestracja Entra
- Jagna Jażdżyk: Broker API, Broker DB, logowanie, ustawienia GitHuba
- Karolina Strzelecka: React, Postman, Azure Boards, test suite w ADO
