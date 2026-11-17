# Podpięcie zewnętrznego API wypożyczalni (KM3)

Broker pyta wypożyczalnie przez wspólny interfejs `ICarRentalProvider`. Nasze Car Rental API ma własną klasę (`OurCarRentalClient`), a API prowadzącego i drugiej grupy podpinamy samą konfiguracją. Robi to `ConfigurableHttpProvider` (`backend/Broker.Api/Providers/Generic`): ścieżki, metody HTTP, uwierzytelnienie i nazwy pól bierze z sekcji `Providers:<Klucz>`. Domyślne wartości są takie jak w naszym API, więc jak druga grupa ma ten sam kształt API co my, wystarczy adres, klucz i `Enabled = true`. Nowej klasy nie piszemy, chyba że mapowanie nie wystarczy (ostatnia sekcja).

## Co dostać od prowadzącego i od drugiej grupy

- Adres API (BaseUrl), np. `https://ich-api.azurewebsites.net`.
- Klucz i nazwę nagłówka, w którym go wysyłamy (u nas `x-api-key`). Jak mają tokeny: albo stały token Bearer, albo dane OAuth2 client credentials (adres tokena, ClientId, ClientSecret, scope).
- Dokumentację, najlepiej Swagger (`/swagger`) albo plik OpenAPI. Do każdej z 6 operacji potrzebujemy metody, ścieżki i przykładu żądania i odpowiedzi: lista aut, jedno auto, wycena (oferta), wynajem, status wynajmu, zgłoszenie zwrotu.
- Listę statusów wynajmu i to, czy wynajem jest od razu potwierdzony, czy dopiero po chwili (u nas Pending, a po około 10 s Confirmed).
- Jak zwracają błędy (kody HTTP i gdzie jest komunikat) i ile ważna jest oferta.
- Jeden działający przykład `curl` z ich strony. Najszybciej pokazuje, czy klucz działa.

Klucz trzymamy tylko w App Settings, User Secrets i menedżerze haseł. Nie wklejamy go do repo ani na kanał zespołu.

## Gdzie to wpisać

Konfiguracja Brokera ma gotowe, wyłączone sekcje `Providers:Lecturer` (kod `lecturer`) i `Providers:OtherGroup` (kod `other-group`) w `backend/Broker.Api/appsettings.json`. Uzupełniamy w nich:

- `BaseUrl` i `ApiKey`,
- `Auth:HeaderName`, jeśli nagłówek nie nazywa się `x-api-key`,
- `Enabled` na `true`.

Włączenie wypożyczalni to tylko `Providers:<Klucz>:Enabled`. To jedyne źródło prawdy. Tabela `Providers` w Broker DB jest słownikiem dla kluczy obcych (wyceny i wynajmy). Broker sam przepisuje do jej kolumny `IsEnabled` stan z konfiguracji (do podglądu), a dla nowego klucza, którego nie ma w seedzie, sam dopisuje wiersz. Zmiana `IsEnabled` w bazie niczego nie włącza. Wypożyczalnia jest aktywna, gdy ma `Enabled = true` i pełną konfigurację (poprawny adres i klucz albo dane OAuth2). Bez tego Broker ją pomija bez błędu.

Nowa wypożyczalnia (np. trzecia grupa) to nowa sekcja z `Type = Generic`. Kod powstaje z nazwy sekcji (`Km3Group` daje `km3-group`) albo z ustawienia `Code`. Program.cs się nie zmienia.

### Lokalnie (User Secrets)

Wartości z sekretami wpisujemy w User Secrets, nie w `appsettings.Development.json`:

```bash
dotnet user-secrets set "Providers:OtherGroup:Enabled" "true" --project backend/Broker.Api
dotnet user-secrets set "Providers:OtherGroup:BaseUrl" "https://ich-api.azurewebsites.net" --project backend/Broker.Api
dotnet user-secrets set "Providers:OtherGroup:ApiKey" "<klucz od nich>" --project backend/Broker.Api
# tylko jak nagłówek ma inną nazwę
dotnet user-secrets set "Providers:OtherGroup:Auth:HeaderName" "X-Api-Key" --project backend/Broker.Api
```

### Na Azure (App Settings)

W Cloud Shell skrypt ustawia wpisy `Providers__<Klucz>__...` w app-carrentals-broker i restartuje aplikację:

```bash
bash scripts/azure/enable-provider.sh OtherGroup https://ich-api.azurewebsites.net '<klucz od nich>'
bash scripts/azure/enable-provider.sh Lecturer https://api-prowadzacego.example.com '<klucz>' X-Api-Key
# wyłączenie
bash scripts/azure/enable-provider.sh Lecturer --disable
```

Skrypt ustawia `Type`, `Enabled`, `DisplayName`, `BaseUrl`, `ApiKey`, `TimeoutSeconds`, `Auth__Type=ApiKey`, `Auth__HeaderName` i `Code`. Resztę (inne ścieżki, pola, Bearer, OAuth2) dopisujemy ręcznie w portalu (App Service, Environment variables) albo przez `az webapp config appsettings set`. Nazwy są takie same jak w JSON, tylko z `__` zamiast `:`, np. `Providers__Lecturer__Operations__Cars__ResultPath=data.items[]`. Grupę zasobów i nazwę aplikacji zmieniamy zmiennymi `RG` i `BROKER_APP`.

### Wszystkie ustawienia sekcji

| Ustawienie | Domyślnie | Opis |
|---|---|---|
| `Type` | | musi być `Generic` |
| `Enabled` | `false` | włączenie wypożyczalni |
| `Code` | z nazwy sekcji | kod w API Brokera i w tabeli Providers |
| `DisplayName` | nazwa sekcji | nazwa na froncie i w `skippedProviders` |
| `BaseUrl` | | adres API |
| `ApiKey` | | klucz (ApiKey) albo token (Bearer) |
| `TimeoutSeconds` | `15` | limit jednego wywołania; całe wyszukiwanie i tak ma limit `Providers:SearchTimeoutSeconds` |
| `Auth:Type` | `ApiKey` | `ApiKey`, `Bearer`, `OAuth2` albo `None` |
| `Auth:HeaderName` | `x-api-key` | nagłówek z kluczem dla `ApiKey` |
| `Auth:TokenUrl`, `Auth:ClientId`, `Auth:ClientSecret`, `Auth:Scope` | | OAuth2 client credentials |
| `Operations:<Operacja>:Method` | jak u nas | `GET`, `POST`, `PUT` |
| `Operations:<Operacja>:Path` | jak u nas | ścieżka względem BaseUrl, z placeholderami |
| `Operations:<Operacja>:ResultPath` | pusta | gdzie w odpowiedzi jest wynik |
| `Mapping:<Model>:<Pole>` | jak u nas | nazwa albo ścieżka pola po ich stronie |
| `Mapping:Statuses:<ich status>` | | nasz status (`Pending`, `Confirmed`, `Rejected`, `ReturnRequested`, `Completed`) |
| `Mapping:DefaultCurrency` | `PLN` | gdy odpowiedź nie ma waluty |
| `Mapping:OfferValidityMinutes` | `10` | gdy oferta nie ma daty wygaśnięcia |
| `Mapping:NumericIds` | `true` | id z samych cyfr wysyłamy w body jako liczbę |

Operacje i ich domyślne wartości:

| Operacja | Domyślnie | Wynik |
|---|---|---|
| `Cars` | `GET api/v1/cars` | lista aut |
| `Car` | `GET api/v1/cars/{carId}` | jedno auto, 404 = brak |
| `Offer` | `POST api/v1/offers` | oferta |
| `Rental` | `POST api/v1/rentals` | wynajem (id i status) |
| `RentalStatus` | `GET api/v1/rentals/{rentalId}` | wynajem, 404 = brak |
| `Return` | `POST api/v1/rentals/{rentalId}/return` | wynajem po zgłoszeniu zwrotu |

## Jak przetestować

1. Najpierw samo ich API, bez Brokera. Jak to nie działa, Broker też nie zadziała:
   ```bash
   curl -i -H "x-api-key: <klucz>" "https://ich-api.azurewebsites.net/api/v1/cars"
   ```
2. Lokalnie: User Secrets jak wyżej, uruchamiamy Brokera (`dotnet run --project backend/Broker.Api`) i sprawdzamy katalog:
   ```bash
   curl -s "http://localhost:5100/api/v1/cars?pageSize=50" | jq '{providers: [.items[].provider] | unique, skipped: .skippedProviders}'
   ```
   W `providers` powinno być `other-group` (albo `lecturer`). Jak wypożyczalnia jest w `skipped`, `reason` mówi, co się stało: `Timeout` (nie zdążyła), `Error` (np. 401 przy złym kluczu, 500, odpowiedź bez pól, które mapujemy), `Rejected` (odmowa z ich komunikatem). Szczegóły są w logu Brokera (wpisy "Wypożyczalnia ...").
3. Na froncie: auto tej wypożyczalni, wycena, Wynajmij, Moje wypożyczenia (status), zgłoszenie zwrotu. Wycena auta z naszej wypożyczalni pyta też pozostałe włączone o to samo auto (marka, model, miasto), więc przy wspólnym aucie widać kilka ofert.
4. Na Azure: `enable-provider.sh`, po minucie ten sam `curl` na `https://app-carrentals-broker.azurewebsites.net/api/v1/cars?pageSize=50`. Błędy w App Service, Log stream albo w Application Insights (Transaction search, zależności HTTP do ich adresu).
5. Test w kodzie: w `tests/Broker.Api.Tests/ConfigurableHttpProviderTests.cs` jest wzór (`CustomMapping_DifferentShape_MapsFieldsPathsAndStatuses`). Wklejamy do `StubHttpHandler` prawdziwą odpowiedź z ich dokumentacji i te same ustawienia co w konfiguracji. Wtedy mapowanie sprawdzamy bez ich serwera.

## Gdy ich API ma inny kształt

Najpierw próbujemy samą konfiguracją. Po kolei:

- Inne ścieżki albo metody: `Operations:<Operacja>:Path` i `Method`. W ścieżce działają `{carId}`, `{rentalId}`, `{offerId}`, `{email}`, `{location}`, `{startDate}`, `{endDate}`, `{age}`, `{licenseYears}` (wartości są kodowane do URL). Wycena przez GET z parametrami w adresie: `Method = GET`, `Path = api/quote?car={carId}&from={startDate}&to={endDate}`. Dla GET body nie wysyłamy.
- Wynik opakowany w obiekt: `ResultPath`, np. `data` albo dla listy `data.items[]`.
- Inne nazwy pól: `Mapping:<Model>:<Pole>` z nazwą albo ścieżką (`brand.name`, `price.daily`, `items[0].id`). Wielkość liter nie ma znaczenia. Modele odpowiedzi: `Car`, `Offer`, `Rental`. Modele żądań: `OfferRequest`, `RentalRequest`, `ReturnRequest`. W żądaniu ścieżka z kropką zakłada obiekt (`customer.email` daje `{"customer":{"email":...}}`).
- Pola, których nie mają: wpisujemy pusty tekst. Broker wtedy bierze wartość domyślną: `Available` = true, `Location` i `Reason` puste, `Year` = 0, ubezpieczenie 0, waluta z `DefaultCurrency`, daty i id auta z naszego zapytania, liczba dni z dat, cena dzienna z całkowitej (albo odwrotnie), wygaśnięcie oferty za `OfferValidityMinutes`, status nowego wynajmu `Pending`. Pole żądania z pustą nazwą nie idzie w body.
- Brak endpointu szczegółów auta: `Operations:Car:Path` pusty, Broker szuka auta na liście. Brak zgłoszenia zwrotu: `Operations:Return:Path` pusty, Broker odpowiada użytkownikowi, że ta wypożyczalnia tego nie obsługuje.
- Inne statusy: `Mapping:Statuses:accepted = Confirmed` itd. Nasze nazwy działają bez wpisu. Nieznany status Broker traktuje jak `Pending` i loguje ostrzeżenie.
- Inne uwierzytelnienie: `Auth:Type = Bearer` (token w `ApiKey`) albo `Auth:Type = OAuth2` z `TokenUrl`, `ClientId`, `ClientSecret`, `Scope`. Token OAuth2 jest trzymany w pamięci do minuty przed wygaśnięciem, a po 401 Broker bierze nowy i ponawia wywołanie raz.

Przykład dla API, które ma auta w `data.items`, cenę w obiekcie `price`, wycenę pod `quotes/{carId}` i token OAuth2:

```json
"Providers": {
  "Lecturer": {
    "Type": "Generic",
    "Code": "lecturer",
    "Enabled": true,
    "DisplayName": "Wypożyczalnia prowadzącego",
    "BaseUrl": "https://api-prowadzacego.example.com",
    "Auth": {
      "Type": "OAuth2",
      "TokenUrl": "https://login.example.com/oauth2/token",
      "ClientId": "carrentals-broker",
      "ClientSecret": "(User Secrets / App Settings)",
      "Scope": "rentals"
    },
    "Operations": {
      "Cars": { "Path": "v2/vehicles", "ResultPath": "data.items[]" },
      "Car": { "Path": "v2/vehicles/{carId}", "ResultPath": "data" },
      "Offer": { "Method": "POST", "Path": "v2/quotes/{carId}", "ResultPath": "quote" },
      "Rental": { "Path": "v2/bookings" },
      "RentalStatus": { "Path": "v2/bookings/{rentalId}" },
      "Return": { "Path": "v2/bookings/{rentalId}/return" }
    },
    "Mapping": {
      "Car": { "Id": "vehicleId", "Make": "brand.name", "Model": "brand.model", "Year": "productionYear", "Location": "city", "Available": "" },
      "OfferRequest": { "CarId": "", "StartDate": "period.from", "EndDate": "period.to" },
      "Offer": { "OfferId": "id", "DailyPrice": "price.daily", "TotalPrice": "price.total", "DailyInsurancePrice": "" },
      "RentalRequest": { "Email": "customer.email" },
      "Rental": { "RentalId": "bookingId", "Status": "state" },
      "Statuses": { "accepted": "Confirmed", "declined": "Rejected", "finished": "Completed" }
    }
  }
}
```

Jak Broker traktuje odpowiedzi (tak samo jak w `OurCarRentalClient`):

- 2xx: mapujemy odpowiedź. Brak wymaganego pola (id, marka, model auta, id i cena oferty, id wynajmu) to błąd techniczny.
- 400, 404, 409, 410, 422 przy wycenie i wynajmie oraz 400, 403, 404, 409, 422 przy zwrocie: odmowa. Użytkownik widzi ich komunikat (z ProblemDetails `title`/`errors` albo z pól `message`, `error`, `detail`).
- 404 przy szczegółach auta i statusie wynajmu: "nie ma".
- 401 i pozostałe kody: błąd techniczny. W wyszukiwaniu wypożyczalnia trafia do `skippedProviders` z `reason: "Error"`, a przy wynajmie użytkownik dostaje 503.
- Brak odpowiedzi w `TimeoutSeconds` (albo w 15 s całego wyszukiwania): `skippedProviders` z `reason: "Timeout"`.

## W ostateczności własna klasa

Własny adapter piszemy tylko wtedy, gdy konfiguracja nie wystarczy, np. API zwraca XML, jedna nasza operacja to u nich kilka wywołań (najpierw rezerwacja, potem potwierdzenie), lista aut jest stronicowana i trzeba pobrać kilka stron, albo żądania trzeba podpisywać (HMAC). Wtedy:

1. Klasa w `backend/Broker.Api/Providers/<Nazwa>/`, która implementuje `ICarRentalProvider`. Wzór to `OurCarRentalClient` (typed HttpClient, odmowy jako `ProviderRejectedException` z kodem HTTP, awarie jako `HttpRequestException`). `Code` taki sam jak w tabeli Providers, `IsEnabled => Settings.Enabled ?? false`.
2. W `ProvidersOptions` właściwość na jej ustawienia, np. `public ProviderEndpointOptions Lecturer { get; set; } = new();`.
3. W Program.cs jedna linia: `builder.Services.AddCarRentalProvider<LecturerCarRentalClient>(providers => providers.Lecturer);`.
4. Z sekcji w konfiguracji usuwamy `Type = Generic`, żeby nie było dwóch dostawców o tym samym kodzie.
5. Testy na `StubHttpHandler` jak w `ConfigurableHttpProviderTests`.

## Dla drugiej grupy: jak podpiąć się do naszego API

Adres na Azure: `https://app-carrentals-api.azurewebsites.net` (Swagger: `/swagger`, health: `/health`).

Uwierzytelnienie: nagłówek `x-api-key` z kluczem, który dostajecie od nas prywatnie. Bez klucza albo ze złym kluczem jest 401. Każdy broker widzi tylko swoje oferty i wynajmy, cudzy wynajem zwraca 404.

JSON w camelCase, daty jako `"2026-12-01"`, statusy jako tekst. Błędy jako ProblemDetails (`application/problem+json`): komunikat jest w `title`, a przy walidacji (400) w `errors`, np. `{"title":"Samochód jest zajęty w wybranym terminie.","status":409}`.

| Metoda i ścieżka | Body | Odpowiedź |
|---|---|---|
| `GET /api/v1/cars?make=&model=&year=` | | 200, lista `{id, make, model, year, location, available, imageUrl, imageCredit}` |
| `GET /api/v1/cars/{id}` | | 200 jedno auto, 404 |
| `POST /api/v1/offers` | `{carId, age, licenseYears, location, startDate, endDate}` | 201 `{offerId, carId, dailyPrice, dailyInsurancePrice, totalPrice, currency, startDate, endDate, days, expiresAt}`, 400, 404, 409 |
| `POST /api/v1/rentals` | `{offerId, firstName, lastName, email}` | 202 `{rentalId, status: "Pending"}`, 400, 404, 409, 410 (oferta wygasła) |
| `GET /api/v1/rentals/{rentalId}` | | 200 `{rentalId, status, reason, carId, startDate, endDate, totalPrice}`, 404 |
| `POST /api/v1/rentals/{rentalId}/return` | `{email}` | 200 `{rentalId, status: "ReturnRequested"}`, 403 (inny email), 404, 409 |

Zasady:

- `location` w ofercie to miasto, w którym stoi auto. Inne miasto daje 400.
- Oferta jest ważna 10 minut (`expiresAt`, UTC). Po tym czasie wynajem zwraca 410 i trzeba wziąć nową ofertę. Wynajem trwa najwyżej 30 dni.
- Wynajem jest asynchroniczny: 202 ze statusem `Pending`, po około 10 s `Confirmed` (albo `Rejected` z powodem w `reason`). Statusu pytacie przez `GET /api/v1/rentals/{rentalId}`. Statusy: `Pending`, `Confirmed`, `Rejected`, `ReturnRequested`, `Completed` (zwrot potwierdzony przez naszego pracownika).

```bash
API=https://app-carrentals-api.azurewebsites.net
KEY="x-api-key: <wasz klucz>"

# lista aut
curl -H "$KEY" "$API/api/v1/cars?make=Toyota"

# oferta
curl -H "$KEY" -H "Content-Type: application/json" -X POST "$API/api/v1/offers" \
  -d '{"carId":1,"age":30,"licenseYears":10,"location":"Warszawa","startDate":"2026-12-01","endDate":"2026-12-04"}'

# wynajem (offerId z oferty)
curl -H "$KEY" -H "Content-Type: application/json" -X POST "$API/api/v1/rentals" \
  -d '{"offerId":"<offerId>","firstName":"Jan","lastName":"Kowalski","email":"jan@example.com"}'

# status (po około 10 s Confirmed)
curl -H "$KEY" "$API/api/v1/rentals/<rentalId>"

# zgłoszenie zwrotu (email z wniosku)
curl -H "$KEY" -H "Content-Type: application/json" -X POST "$API/api/v1/rentals/<rentalId>/return" \
  -d '{"email":"jan@example.com"}'
```

Jak wasz Broker jest też w .NET, możecie skopiować nasz `ConfigurableHttpProvider` z domyślnymi ustawieniami: pasują do naszego API bez mapowania. Klucz dla was możemy wymienić w każdej chwili, bo jest w App Settings (`ApiKeys__Clients__1__Key` w app-carrentals-api), a nie w kodzie. Od was potrzebujemy tego samego: adresu, klucza z nazwą nagłówka i dokumentacji (sekcja na początku).
