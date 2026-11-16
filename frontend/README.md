# Frontend Car Rentals (React)

Front to Vite + React + TypeScript. Rozmawia tylko z Brokerem (`/api/v1/...`), nigdy bezpośrednio z Car Rental API. Logowanie trzyma Broker w ciasteczku HttpOnly, więc w JS nie ma żadnych tokenów. Kontrakt endpointów jest w `docs/broker-api-contract.md`.

## Wymagania

- Node.js 22 (Vite 8 potrzebuje 20.19+ albo 22.12+)
- działający Broker na `http://localhost:5100` (i za nim Car Rental API na `http://localhost:5080`), np. przez `docker compose up` w katalogu głównym

## Uruchomienie lokalnie

```
cd frontend
npm ci
npm run dev
```

Front wstaje na `http://localhost:5173`. Vite przekierowuje `/api` i `/health` na Brokera (`http://localhost:5100`), więc ciasteczko sesji jest z tego samego originu co front, tak samo jak w chmurze. Inny adres Brokera można podać zmienną `BROKER_URL`, np. `BROKER_URL=http://localhost:5200 npm run dev`.

Logowanie lokalnie: jak Broker nie ma ustawionego `AzureAd:ClientId` i ma `Auth:UseDevLogin=true` (Development), `/api/v1/auth/me` zwraca `authMode: "DevLogin"`. Wtedy w menu są przyciski "Klient" i "Pracownik", które logują bez hasła przez `/api/v1/auth/dev-login`. Z Entra jest zwykły przycisk "Zaloguj".

## Skrypty

- `npm run dev` serwer deweloperski z proxy
- `npm run build` sprawdzenie typów (`tsc -b`) i build do `frontend/dist`
- `npm test -- --run` testy (Vitest + Testing Library), jeden przebieg; samo `npm test` lokalnie działa w trybie watch, a przy `CI=true` też robi jeden przebieg
- `npm run lint` ESLint (TypeScript + reguły hooków Reacta)
- `npm run preview` podgląd zbudowanej wersji

## Produkcja

Robimy `npm run build`, a pipeline kopiuje zawartość `frontend/dist` do `backend/Broker.Api/wwwroot` przed `dotnet publish`. Broker serwuje pliki (`UseDefaultFiles`, `UseStaticFiles`) i dla tras spoza `/api` oddaje `index.html`, dlatego odświeżenie np. `/cars/our/1` działa. Ręcznie nic nie kopiujemy. Pipeline ustawia `VITE_APP_VERSION` (numer builda), front pokazuje go w stopce.

Nie robimy osobnego hostingu frontu (Static Web Apps), bo ciasteczko sesji Brokera z innej domeny byłoby third-party.

## Struktura

```
src/
  api/client.ts        fetch z credentials same-origin, X-CSRF: 1 dla POST/PUT/DELETE, ProblemDetails na czytelny błąd
  api/types.ts         typy 1:1 z kontraktem Brokera
  api/broker.ts        funkcja na każdy endpoint
  api/useApi.ts        hook do pobierania danych (AbortController, loading, błąd, ponów)
  auth/                AuthContext (GET /api/v1/auth/me, logowanie, wylogowanie)
  components/          Navbar, Pagination, Spinner, ErrorMessage, CookieBanner, RequireAuth, SkippedNotice
  pages/               Landing, Search, CarDetails (z wyceną), Profile, Privacy, NotFound
  pages/employee/      lista wypożyczeń naszej firmy i szczegóły z potwierdzeniem zwrotu
  index.css            jeden arkusz, flex/grid, układ pod telefon
```

Co robi front na KM2:
- strona główna z krótkim opisem i wyborem marki i modelu z `/api/v1/cars/makes`;
- wyszukiwarka z filtrem marka, model, rok, filtry są w adresie i zostają przy zmianie strony, stronicowanie po 5;
- szczegóły auta i wycena (daty, wiek, staż, miasto), zalogowanemu wypełniamy dane z profilu, spinner do 15 s, oferty z wypożyczalnią, ceną za dzień, ubezpieczeniem, sumą i godziną ważności, komunikat "brak ofert" i lista pominiętych wypożyczalni;
- profil z datą urodzenia, datą prawa jazdy i miastem, z informacją, czego brakuje;
- panel pracownika (tylko rola Employee): lista z filtrem wszystkie, aktywne, do odbioru, zakończone, szczegóły i potwierdzenie zwrotu z notatką i plikiem (jpg, png, pdf do 10 MB);
- baner o ciasteczkach (tylko niezbędne) i strona z polityką prywatności.

"Moje wypożyczenia" i przycisk "Wynajmij" są wyszarzone, bo to KM3.

Ukrywanie linków w menu to tylko wygoda. Uprawnienia sprawdza Broker (401 bez logowania, 403 bez roli).
