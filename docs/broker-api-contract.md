# Kontrakt HTTP Broker API (KM2)

Broker API (BFF) to jedyny backend, z którym rozmawia React. Front woła tylko ścieżki `/api/v1/...` Brokera na tym samym originie (w Development przez proxy Vite, które przekazuje `/api` do `http://localhost:5100`). Broker sam woła Car Rental API (nasze, później API prowadzącego i innej grupy).

Kod kontraktów: `backend/Broker.Api/Contracts/*.cs`. Swagger Brokera: `http://localhost:5100/swagger` (gdy `Swagger:Enabled=true`, w Development domyślnie tak).

## Zasady ogólne

- JSON w camelCase, enumy jako stringi (`"ReturnRequested"`), daty bez godziny jako `"2026-11-20"` (DateOnly), znaczniki czasu jako ISO 8601 w UTC z `Z` (`"2026-11-16T18:10:00Z"`), kwoty jako liczby (`349.5`).
- Logowanie: ciasteczko sesji Brokera (HttpOnly, Secure, SameSite=Lax). Front wysyła `fetch(..., { credentials: 'same-origin' })`. Żadnych tokenów w JS.
- CSRF: każde `POST`, `PUT`, `DELETE` do `/api/...` musi mieć nagłówek `X-CSRF: 1`. Bez niego Broker zwraca `400` (ProblemDetails, title `Brak nagłówka X-CSRF: 1.`).
- Endpointy z `/api` nigdy nie przekierowują na stronę logowania. Brak sesji = `401`, brak roli = `403` (ProblemDetails). Wyjątek: `GET /api/v1/auth/login` i `GET /api/v1/auth/dev-login?returnUrl=...` (nawigacja przeglądarki, nie fetch).
- Błędy: `application/problem+json`.
  - zwykły błąd: `{ "type": "...", "title": "Nie znaleziono samochodu.", "status": 404, "traceId": "..." }`
  - walidacja (400): `{ "title": "One or more validation errors occurred.", "status": 400, "errors": { "endDate": ["Data zakończenia musi być późniejsza niż data rozpoczęcia."] } }`. Klucze w `errors` są w camelCase (tak jak pola JSON).
- Kody używane w Brokerze: `200`, `204`, `302` (tylko login), `400`, `401`, `403`, `404`, `409`, `503` (wypożyczalnia nie odpowiada albo zwróciła błąd).
- `providerCode` (pole `provider`): `"our"` (nasza wypożyczalnia, włączona na KM2), `"lecturer"` i `"other-group"` (KM3, na KM2 wyłączone). `carId` jest stringiem, bo każda wypożyczalnia ma swoje identyfikatory.
- `skippedProviders`: lista wypożyczalni, które nie odpowiedziały w limicie 15 s albo zwróciły błąd. Front może pokazać np. "Wypożyczalnia X nie odpowiedziała, pokazujemy oferty pozostałych".

```json
{ "provider": "our", "providerName": "Car Rentals", "reason": "Timeout", "message": "Brak odpowiedzi w ciągu 15 s." }
```

`reason`: `"Timeout"` (nie zdążyła), `"Error"` (błąd techniczny, np. 500 albo brak połączenia), `"Rejected"` (wypożyczalnia odmówiła oferty, np. auto zajęte w terminie albo zła lokalizacja; w `message` jest jej komunikat).

## Endpointy

| Metoda i ścieżka | Dostęp | Opis |
|---|---|---|
| `GET /api/v1/cars` | anonim | lista aut ze wszystkich wypożyczalni, filtr, stronicowanie |
| `GET /api/v1/cars/makes` | anonim | marki i modele (landing page) |
| `GET /api/v1/cars/{provider}/{carId}` | anonim | szczegóły auta |
| `POST /api/v1/offers` | anonim albo zalogowany | wycena (oferty od wypożyczalni) |
| `GET /api/v1/auth/login?returnUrl=/` | anonim | start logowania (przekierowanie) |
| `GET /api/v1/auth/dev-login?name=&role=&returnUrl=` | anonim, tylko Development | logowanie deweloperskie |
| `POST /api/v1/auth/logout` | każdy | wylogowanie |
| `GET /api/v1/auth/me` | każdy | stan logowania |
| `GET /api/v1/profile` | zalogowany | profil |
| `PUT /api/v1/profile` | zalogowany | zapis profilu |
| `GET /api/v1/employee/rentals?status=` | rola Employee | lista wynajmów naszej firmy |
| `GET /api/v1/employee/rentals/{rentalId}` | rola Employee | szczegóły wynajmu |
| `POST /api/v1/employee/rentals/{rentalId}/confirm-return` | rola Employee | P2: potwierdzenie zwrotu (multipart) |
| `GET /api/v1/employee/rentals/{rentalId}/attachment` | rola Employee | P2: pobranie załącznika |
| `GET /health` | anonim | stan aplikacji i wersja, np. `{"status":"Healthy","version":"1.2.15"}`, bez prefiksu |

---

### GET /api/v1/cars

Query (wszystko opcjonalne):

| Parametr | Typ | Opis |
|---|---|---|
| `make` | string | marka, dokładne dopasowanie bez wielkości liter (`toyota` = `Toyota`) |
| `model` | string | model, jak wyżej |
| `year` | int | rok produkcji |
| `location` | string | miasto, jak wyżej |
| `page` | int | numer strony od 1, domyślnie 1 |
| `pageSize` | int | 1..50, domyślnie 5 |

Sortowanie: marka, model, rok malejąco, wypożyczalnia. Katalog jest w cache Brokera (domyślnie 10 min, maks. 30 min), więc `available` nie gwarantuje dostępności w terminie; sprawdza to dopiero wycena.

200:
```json
{
  "items": [
    {
      "provider": "our",
      "providerName": "Car Rentals",
      "carId": "1",
      "make": "Toyota",
      "model": "Corolla",
      "year": 2022,
      "location": "Warszawa",
      "available": true
    }
  ],
  "page": 1,
  "pageSize": 5,
  "totalCount": 12,
  "totalPages": 3,
  "skippedProviders": []
}
```

Strona za ostatnią daje `items: []` (z poprawnym `totalCount`). Błędy: `400` (np. `page=0`, `pageSize=100`).

### GET /api/v1/cars/makes

200 (posortowane alfabetycznie):
```json
[
  { "make": "Skoda", "models": ["Fabia", "Octavia"] },
  { "make": "Toyota", "models": ["Corolla", "Yaris"] }
]
```

### GET /api/v1/cars/{provider}/{carId}

Przykład: `GET /api/v1/cars/our/1`. Dane są pobierane od wypożyczalni na bieżąco (nie z cache).

200:
```json
{
  "provider": "our",
  "providerName": "Car Rentals",
  "carId": "1",
  "make": "Toyota",
  "model": "Corolla",
  "year": 2022,
  "location": "Warszawa",
  "available": true
}
```

Błędy: `404` (nieznana albo wyłączona wypożyczalnia, nie ma auta), `503` (wypożyczalnia nie odpowiada).

### POST /api/v1/offers

Nagłówki: `Content-Type: application/json`, `X-CSRF: 1`.

Body:
```json
{
  "provider": "our",
  "carId": "1",
  "startDate": "2026-11-20",
  "endDate": "2026-11-23",
  "age": 30,
  "licenseYears": 8,
  "location": "Warszawa"
}
```

| Pole | Typ | Wymagane | Opis |
|---|---|---|---|
| `provider` | string | tak | wypożyczalnia auta ze strony szczegółów |
| `carId` | string | tak | id auta u tej wypożyczalni |
| `startDate` | date | tak | nie wcześniej niż dziś |
| `endDate` | date | tak | później niż `startDate` |
| `age` | int? | anonim: tak | 18..99 |
| `licenseYears` | int? | anonim: tak | 0..81, nie więcej niż `age - 18` |
| `location` | string? | anonim: tak | miasto odbioru, 2..100 znaków |

Zalogowany może pominąć `age`, `licenseYears`, `location` (albo dać `null`): Broker liczy wiek i staż na dziś z daty urodzenia i daty prawa jazdy z profilu i bierze lokalizację z profilu. Wartości podane w żądaniu mają pierwszeństwo przed profilem. Jak dalej czegoś brakuje, jest `400` z kluczem pola w `errors`.

Broker pyta wypożyczalnię auta, a od KM3 równolegle także pozostałe włączone wypożyczalnie o auto tej samej marki i modelu w tej samej lokalizacji. Całość trwa najwyżej 15 s, kto nie zdąży, trafia do `skippedProviders`. Każda oferta jest zapisywana w Broker DB (tabela `Quotes`), `quoteId` to jej numer w Brokerze (przyda się w KM3 do "Rent me").

200 (oferty posortowane po `totalPrice` rosnąco):
```json
{
  "offers": [
    {
      "quoteId": "5b0f6f8e-3f8a-4a51-9d55-0a4f1c7b2d11",
      "provider": "our",
      "providerName": "Car Rentals",
      "offerId": "c1d2e3f4-0000-4000-8000-000000000001",
      "carId": "1",
      "carLabel": "Toyota Corolla 2022",
      "dailyPrice": 180.0,
      "dailyInsurancePrice": 35.0,
      "totalPrice": 645.0,
      "currency": "PLN",
      "startDate": "2026-11-20",
      "endDate": "2026-11-23",
      "days": 3,
      "expiresAt": "2026-11-16T18:10:00Z"
    }
  ],
  "skippedProviders": []
}
```

Brak ofert to dalej `200`, tylko `offers: []` (front pokazuje "Brak ofert" i ewentualnie powody z `skippedProviders`, np. `reason: "Rejected", message: "Samochód jest zajęty w wybranym terminie."`).

Błędy: `400` (walidacja, brak `X-CSRF`), `404` (nieznana albo wyłączona wypożyczalnia).

---

### GET /api/v1/auth/login?returnUrl=/profile

Nawigacja przeglądarki (`window.location.href = '/api/v1/auth/login?returnUrl=' + encodeURIComponent(path)`), nie fetch.
- Entra skonfigurowane (`AzureAd:ClientId`): `302` do logowania Microsoft, po powrocie `302` na `returnUrl`.
- Bez Entra, w Development z `Auth:UseDevLogin=true`: `302` na `/api/v1/auth/dev-login?name=jan.kowalski&returnUrl=...` (zwykły użytkownik bez roli).
- Bez żadnego logowania: `503`.

`returnUrl` musi być lokalny (zaczyna się od `/`, nie od `//`), inaczej Broker używa `/`.

### GET /api/v1/auth/dev-login

Tylko w Development i tylko gdy `Auth:UseDevLogin=true` (w innym przypadku `404`). Ustawia ciasteczko sesji dla podanego użytkownika, bez hasła. W chmurze tego nie ma.

| Parametr | Typ | Opis |
|---|---|---|
| `name` | string | login, np. `anna` (wymagany, 2..50 znaków: litery, cyfry, `.`, `-`, `_`) |
| `role` | string? | np. `Employee` (kilka po przecinku) |
| `returnUrl` | string? | lokalny adres; jak jest, odpowiedź to `302` na niego |

Bez `returnUrl` odpowiedź to `200` z tym samym JSON-em co `GET /api/v1/auth/me`. Użytkownik dostaje `email` = `<name>@dev.local`, imię = `name`, nazwisko `Dev`.

Przykłady: `/api/v1/auth/dev-login?name=anna&role=Employee&returnUrl=/employee/rentals`, `/api/v1/auth/dev-login?name=jan&returnUrl=/profile`.

### POST /api/v1/auth/logout

Nagłówek `X-CSRF: 1`, bez body. Usuwa ciasteczko Brokera. `204`. Sesja w Entra zostaje (kolejne logowanie może przejść bez hasła). Front po wylogowaniu odświeża stan z `/auth/me`.

### GET /api/v1/auth/me

Zawsze `200`.

Niezalogowany:
```json
{
  "isAuthenticated": false,
  "name": null,
  "email": null,
  "roles": [],
  "isEmployee": false,
  "profileComplete": false,
  "authMode": "Entra"
}
```

Zalogowany:
```json
{
  "isAuthenticated": true,
  "name": "Anna Nowak",
  "email": "anna@example.com",
  "roles": ["Employee"],
  "isEmployee": true,
  "profileComplete": false,
  "authMode": "DevLogin"
}
```

`authMode`: `"Entra"`, `"DevLogin"` (lokalnie, front może pokazać szybkie logowanie jako pracownik), `"None"` (logowanie niedostępne, przycisk Zaloguj ukryty albo wyszarzony).

---

### GET /api/v1/profile

Zalogowany. Przy pierwszym wejściu Broker tworzy profil z danymi z konta (imię, nazwisko, email).

200:
```json
{
  "firstName": "Anna",
  "lastName": "Nowak",
  "email": "anna@example.com",
  "dateOfBirth": "1998-05-14",
  "licenseObtainedOn": null,
  "location": "Warszawa",
  "age": 28,
  "licenseYears": null,
  "isComplete": false,
  "missingFields": ["licenseObtainedOn"],
  "updatedAt": "2026-11-16T18:00:00Z"
}
```

`age` i `licenseYears` są liczone na dziś (w bazie trzymamy daty, bo wiek i staż zmieniają się z czasem). `missingFields` może zawierać: `firstName`, `lastName`, `email`, `dateOfBirth`, `licenseObtainedOn`, `location`. Błędy: `401`.

### PUT /api/v1/profile

Zalogowany, nagłówek `X-CSRF: 1`. Zapisuje wszystkie pola (pole `null` = wyczyszczone).

```json
{
  "firstName": "Anna",
  "lastName": "Nowak",
  "email": "anna@example.com",
  "dateOfBirth": "1998-05-14",
  "licenseObtainedOn": "2017-06-01",
  "location": "Warszawa"
}
```

| Pole | Typ | Zasady |
|---|---|---|
| `firstName`, `lastName` | string | wymagane, 1..100 znaków |
| `email` | string | wymagany, poprawny email, maks. 254 |
| `dateOfBirth` | date? | nie w przyszłości, wiek co najmniej 18 lat |
| `licenseObtainedOn` | date? | nie w przyszłości, nie wcześniej niż 16. urodziny |
| `location` | string? | 2..100 znaków |

200: ten sam JSON co `GET /api/v1/profile`. Błędy: `400` (walidacja), `401`.

---

### GET /api/v1/employee/rentals?status=ReturnRequested

Rola `Employee` (brak sesji `401`, brak roli `403`). Broker woła `GET /api/v1/employee/rentals` w Car Rental API z tokenem pracownika (Entra) albo lokalnie z nagłówkami `X-Dev-Employee`/`X-Dev-Roles`.

`status` (opcjonalny): `Pending`, `Confirmed` (aktywne), `Rejected`, `ReturnRequested` (do odbioru), `Completed`.

200:
```json
[
  {
    "rentalId": "8f6c1c1e-4c43-4b8e-9b1a-1d2f3a4b5c6d",
    "carId": 1,
    "car": "Toyota Corolla (2022)",
    "customerName": "Jan Kowalski",
    "email": "jan.kowalski@example.com",
    "startDate": "2026-11-20",
    "endDate": "2026-11-23",
    "status": "ReturnRequested",
    "totalPrice": 645.0
  }
]
```

Błędy: `400` (zły status), `401`, `403`, `503` (Car Rental API nie odpowiada).

### GET /api/v1/employee/rentals/{rentalId}

200:
```json
{
  "rentalId": "8f6c1c1e-4c43-4b8e-9b1a-1d2f3a4b5c6d",
  "carId": 1,
  "car": "Toyota Corolla (2022)",
  "location": "Warszawa",
  "firstName": "Jan",
  "lastName": "Kowalski",
  "email": "jan.kowalski@example.com",
  "startDate": "2026-11-20",
  "endDate": "2026-11-23",
  "status": "ReturnRequested",
  "reason": null,
  "totalPrice": 645.0,
  "brokerName": "OurBroker",
  "createdAt": "2026-11-16T17:00:00Z",
  "returnRequestedAt": "2026-11-16T17:30:00Z",
  "completedAt": null,
  "completedBy": null,
  "returnNote": null,
  "hasAttachment": false
}
```

Błędy: `401`, `403`, `404`, `503`.

### POST /api/v1/employee/rentals/{rentalId}/confirm-return (P2)

Rola `Employee`, nagłówek `X-CSRF: 1`, `multipart/form-data` (front: `FormData`, bez ręcznego `Content-Type`):
- `note`: string, 3..1000 znaków
- `file`: jpg, png albo pdf, do 10 MB

200:
```json
{ "rentalId": "8f6c1c1e-4c43-4b8e-9b1a-1d2f3a4b5c6d", "status": "Completed" }
```

Błędy: `400` (walidacja, np. zły typ pliku, komunikat z Car Rental API), `401`, `403`, `404`, `409` (wynajem nie czeka na odbiór), `503`.

### GET /api/v1/employee/rentals/{rentalId}/attachment (P2)

Rola `Employee`. `200` z plikiem (`Content-Type` jak w Car Rental API, `Content-Disposition: attachment`). Błędy: `401`, `403`, `404`, `503`.

---

## Konfiguracja Brokera (nazwy ustawień)

| Ustawienie | Przykład | Opis |
|---|---|---|
| `ConnectionStrings__BrokerDb` | `Server=...;Database=BrokerDb;...` | Broker DB |
| `Database__ApplyMigrationsOnStartup` | `false` w chmurze | migracje robi pipeline (bundle) |
| `Providers__SearchTimeoutSeconds` | `15` | limit na całe odpytywanie wypożyczalni |
| `Providers__Our__BaseUrl` | `https://app-carrentals-api.azurewebsites.net` | nasze Car Rental API |
| `Providers__Our__ApiKey` | (sekret) | klucz `OurBroker` z Car Rental API |
| `Providers__Our__TimeoutSeconds` | `15` | limit pojedynczego wywołania HttpClient |
| `Providers__Our__Scope` | `api://<clientId>/access_as_user` | scope do tokena pracownika; puste = liczone z `AzureAd__ClientId` |
| `Providers__Lecturer__BaseUrl/ApiKey`, `Providers__OtherGroup__BaseUrl/ApiKey` | | KM3 |
| `Catalog__CacheMinutes` | `10` | cache katalogu, 1..30 |
| `AzureAd__Instance` | `https://login.microsoftonline.com/` | |
| `AzureAd__TenantId` | | |
| `AzureAd__ClientId` | | puste = bez Entra |
| `AzureAd__ClientSecret` | (sekret) | |
| `AzureAd__CallbackPath` | `/signin-oidc` | domyślne |
| `Auth__UseDevLogin` | `false` | tylko Development |
| `Swagger__Enabled` | `true` na demo | |
| `Quotes__CleanupIntervalMinutes`, `Quotes__RetentionHours` | `10`, `24` | sprzątanie starych wycen w tle |
