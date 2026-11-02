# Car Rental API (nasza wypożyczalnia)

API naszej wypożyczalni w projekcie Car Rentals. Korzystają z niego brokerzy: nasz i broker innej grupy.
Zakres to KM2: epik **33 „API naszego CarRental”** i **#141 „przygotowanie CarDB”**, a także backend do panelu pracownika (#100, #101).

## Co jest zrobione

| Endpoint | Kto | Co robi | ADO |
|---|---|---|---|
| `GET /cars` | broker | lista aut, filtry `make`, `model`, `year` | #117, #118 |
| `GET /cars/{id}` | broker | jedno auto (strona szczegółów) | #117 |
| `POST /offers` | broker | oferta z wyceną, ważna 10 min | #119 |
| `POST /rentals` | broker | wniosek o wynajem, odpowiedź **202 Pending** | #120, #131 |
| `GET /rentals/{rentalId}` | broker | status wynajmu | #121, #132 |
| `POST /rentals/{rentalId}/return` | broker | zgłoszenie zwrotu | #125 |
| `GET /employee/rentals?status=` | pracownik | lista wynajmów naszej firmy z filtrem | #101, #129 |
| `GET /employee/rentals/{rentalId}` | pracownik | szczegóły | #101 |
| `POST /employee/rentals/{rentalId}/confirm-return` | pracownik | potwierdzenie zwrotu (notatka + jpg/png/pdf) | #126 |
| `GET /employee/rentals/{rentalId}/attachment` | pracownik | pobranie załącznika | #126 |
| `GET /health` | każdy | czy API żyje | |

**Najważniejsze zasady:**

- **Wycena.** Cena bazowa auta razy 1,25, gdy kierowca ma mniej niż 25 lat, i razy 1,2, gdy prawo jazdy ma krócej niż 3 lata. To samo dotyczy ubezpieczenia. Wartości są w `appsettings.json` w sekcji `Pricing`.
- **Oferta** jest ważna 10 minut (`Offers:ValidityMinutes`). Po tym czasie wynajem zwraca **410**.
- **Dostępność** sprawdzamy przy ofercie i drugi raz przy wniosku, w transakcji Serializable. Jeśli auto jest już zajęte, dostajesz **409**. Dzień oddania auta jest wolny dla kolejnego klienta.
- **Statusy:** `Pending → Confirmed / Rejected`, potem `ReturnRequested → Completed`. Wniosek nie jest potwierdzany od razu. Po około 10 s potwierdza go proces w tle (`RentalProcessing`), a broker w tym czasie odpytuje `GET /rentals/{id}`.
- **Klucze brokerów.** Broker łączy się kluczem `x-api-key`, każdy ma swój. Broker widzi tylko swoje oferty i wynajmy, cudzy wynajem zwraca 404.
- **Endpointy `/employee`** wymagają roli **Employee** (token z Entra ID). Sam klucz brokera daje 401, a zalogowany użytkownik bez roli dostaje 403.
- **Pliki zwrotu.** Tylko jpg, png i pdf do 10 MB. Sprawdzamy też pierwsze bajty pliku, więc przemianowany `.exe` nie przejdzie. Pliki zapisujemy lokalnie w `App_Data/attachments`. W KM3 podmienimy to na Azure Blob, bo interfejs `IFileStorage` jest już gotowy.
- **Audyt.** Każde wywołanie jest logowane (metoda, ścieżka, status, czas, który broker), bez nagłówków i kluczy.

## Wymagania

- **.NET 10 SDK.** Opis projektu pozwala na .NET 8–10. .NET 8 i .NET 9 kończą wsparcie 10.11.2026, dlatego domyślnie jest .NET 10. Kto ma tylko .NET 8 SDK, może zbudować i przetestować ten sam kod przełącznikiem: `dotnet test -p:AppTargetFramework=net8.0`. Skrypt `run-tests.bat` robi to sam. Wersja frameworka i paczek jest w jednym miejscu, w `Directory.Build.props`.
- **Baza**, do wyboru:
  - SQL Server LocalDB (instaluje się z Visual Studio), albo
  - Docker (wtedy `docker compose up`, opis niżej).

## Uruchomienie lokalnie

Wszystkie komendy uruchamiamy z katalogu głównego repo.

```bash
# 1. Testy (nie potrzebują żadnej bazy, używają SQLite w pamięci)
dotnet test

# 2. Migracja bazy (jednorazowo, potem przy każdej zmianie modelu)
dotnet tool restore
dotnet ef migrations add InitialCreate --project backend/CarRental.Api

# 3. Start API (baza tworzy się sama przy starcie w trybie Development)
dotnet run --project backend/CarRental.Api
```

Swagger jest pod adresem http://localhost:5080/swagger. Kliknij **Authorize** i wpisz klucz `dev-our-broker-key-change-me`.

Bez migracji też zadziała, bo API utworzy bazę prosto z modelu. Jeśli jednak najpierw uruchomisz bez migracji, a potem je dodasz, usuń lokalną bazę:

```bash
dotnet ef database drop --project backend/CarRental.Api
```

### Albo przez Dockera (bez LocalDB)

```bash
docker compose up --build
```

To uruchomi SQL Server i API. Swagger jest pod http://localhost:5080/swagger.

## Przykładowy przebieg (curl)

```bash
KEY="x-api-key: dev-our-broker-key-change-me"

curl -H "$KEY" http://localhost:5080/cars

curl -H "$KEY" -H "Content-Type: application/json" -X POST http://localhost:5080/offers \
  -d '{"carId":1,"age":22,"licenseYears":2,"location":"Warszawa","startDate":"2026-11-20","endDate":"2026-11-23"}'

curl -H "$KEY" -H "Content-Type: application/json" -X POST http://localhost:5080/rentals \
  -d '{"offerId":"<offerId z poprzedniej odpowiedzi>","firstName":"Jan","lastName":"Kowalski","email":"jan@example.com"}'

curl -H "$KEY" http://localhost:5080/rentals/<rentalId>     # po ~10 s status = Confirmed
```

### Pracownik lokalnie (zanim będzie Entra ID)

Dopóki aplikacja nie jest zarejestrowana w Entra ID (#58), lokalnie zamiast tokena działają dwa nagłówki:

```bash
curl -H "X-Dev-Employee: anna" -H "X-Dev-Roles: Employee" "http://localhost:5080/employee/rentals?status=ReturnRequested"
```

Działa to **tylko** w środowisku Development i tylko wtedy, gdy `AzureAd:ClientId` jest puste. Gdy wpiszemy `AzureAd:TenantId` i `AzureAd:ClientId`, API przyjmuje już wyłącznie prawdziwe tokeny z rolą `Employee` (claim `roles`).

## Konfiguracja i sekrety

- **Klucze w repo.** `appsettings.Development.json` zawiera tylko klucze deweloperskie.
- **Klucze na Azure.** Prawdziwe klucze ustawiamy w App Service (Configuration):

  ```
  ApiKeys__Clients__0__Name = OurBroker
  ApiKeys__Clients__0__Key  = <długi losowy klucz>
  ApiKeys__Clients__1__Name = OtherGroupBroker
  ApiKeys__Clients__1__Key  = <drugi klucz>
  ConnectionStrings__CarRentalDb = <connection string do Azure SQL>
  AzureAd__TenantId = ...
  AzureAd__ClientId = ...
  ```

- **Klucze lokalnie.** Można je trzymać w User Secrets: `dotnet user-secrets set "ApiKeys:Clients:0:Key" "..." --project backend/CarRental.Api`.
- **Walidacja przy starcie.** Bez kluczy, albo z kluczem krótszym niż 16 znaków, API nie wystartuje. To celowe.

## CI (Azure Pipelines)

`azure-pipelines.yml` robi po kolei:

- restore i build (z analizatorami Roslyn),
- testy z raportem pokrycia,
- skan podatnych paczek (`dotnet list package --vulnerable`, który przerywa build, gdy coś znajdzie),
- publish API jako artefakt.

Pipeline „Jagna676.CarRentals” w ADO jest już podpięty do repo na GitHubie i czyta ten plik z katalogu głównego. Zacznie działać, gdy kod trafi do repo. Plik przeszedł walidację w ADO.

## Struktura

```
backend/CarRental.Api
  Controllers/   endpointy (cienkie, logika jest w Services)
  Services/      oferty, wynajmy, panel pracownika, walidacja plików
  Pricing/       wycena
  Domain/        encje: Car, Offer, Rental, RentalStatus
  Data/          DbContext (EF Core) + auta na start (seed)
  Auth/          klucz API brokera, zastępczy login pracownika do pracy lokalnej
  Background/    potwierdzanie wniosków w tle
  Storage/       zapis załączników (lokalnie, później Azure Blob)
  Middleware/    audyt wywołań
tests/CarRental.Api.Tests
  testy jednostkowe wyceny + testy integracyjne każdego endpointu (WebApplicationFactory + SQLite w pamięci)
```

## Co dalej (KM3)

- `IFileStorage` na Azure Blob Storage (prywatny kontener) (#81, #104)
- Faktura PDF (QuestPDF) i mail po potwierdzeniu zwrotu (#82, #107, #108)
- Logi i audyt do Application Insights (#39)
- Rejestracja w Entra ID i rola Employee (#58, #100)
