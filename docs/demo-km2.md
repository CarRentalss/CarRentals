# Demo na KM2 (19.11.2026), 9-11 minut

Scenariusz zakłada, że rzeczy z docs/km2-checklista-ludzie.md są zrobione (deploy, ruleset, Entra, Test Plans). Czego nie zrobimy, pokazujemy wg planu B na dole i mówimy wprost, że to czeka. Każdy pokazuje swoją część kodu i umie o niej powiedzieć dwa zdania. Próba w czwartek rano. Przed zajęciami: App Service rozgrzane 10 minut wcześniej, lokalnie `docker compose up --build` już działa (plan B), w przeglądarce otwarte karty z listy niżej.

Karty do otwarcia: aplikacja (https://app-carrentals-broker.azurewebsites.net albo http://localhost:5100), Swagger Car Rental API, ADO (Boards, Pipelines, ostatni run), GitHub (PR, ruleset), portal Azure (rg-carrentals, App Insights), docs/architecture.md.

## 1. Architektura i plan (Jaga, 1 min)

- Pokazać diagram z docs/architecture.md: React w Brokerze, Broker, Car Rental API, dwie bazy, App Insights. Szare pola to KM3.
- Powiedzieć: "Front woła tylko Brokera. Broker woła nasze Car Rental API kluczem x-api-key. W KM3 dochodzą API prowadzącego i innej grupy jako kolejne adaptery."
- Decyzja: React serwowany przez Brokera zamiast Static Web Apps, bo ciasteczko sesji między dwiema domenami byłoby third-party.

## 2. Przepływ użytkownika w React (Karolina, 2 min)

1. Strona główna: krótki opis, wybór marki i modelu (marki przychodzą z API, nie są w kodzie).
2. Wyszukiwarka: filtr po roku, stronicowanie po 5, zmiana strony nie kasuje filtrów.
3. Szczegóły auta, formularz wyceny (daty, wiek, staż, miasto), spinner, oferta: wypożyczalnia, cena dzienna, ubezpieczenie, ważność 10 minut.
4. DevTools, zakładka Network: front woła tylko `/api/v1/...` na tej samej domenie (AJAX).
5. Wycena dla zajętego terminu albo złego miasta: komunikat "brak ofert" z powodem.

## 3. Logowanie, profil, wynajem i panel pracownika (Jaga i Karolina, 3 min)

Cały przepływ idzie przez UI, bez Postmana. Najlepiej w dwóch oknach: zwykłe okno to klient, okno incognito to pracownik (osobne ciasteczka sesji).

1. Klient: Zaloguj (Entra ID albo lokalnie dev-login "Klient"). Pokazać, że ciasteczko jest HttpOnly.
2. Profil: komunikat o brakujących danych, uzupełnienie, zapis. Powrót do auta: formularz wyceny wypełnia się z profilu.
3. Sprawdź ceny, przy ofercie "Wynajmij". Formularz z imieniem, nazwiskiem i emailem z profilu, "Potwierdź wynajem".
4. Ekran statusu: najpierw "Czeka na potwierdzenie" (Pending), strona sama odświeża status co 3 s, po około 10 s "Potwierdzone". W Network widać `POST /api/v1/rentals` (202) i kolejne `GET /api/v1/rentals/{id}`.
5. Moje wypożyczenia (menu): wynajem ze statusem Potwierdzone, "Zgłoś zwrot". Status zmienia się na "Zwrot zgłoszony".
6. Pracownik (incognito, dev-login "Pracownik" albo konto z rolą Employee): Panel pracownika, filtr "Do odbioru", ten sam wynajem z danymi klienta. Szczegóły, notatka i zdjęcie (jpg) albo PDF, potwierdzenie zwrotu. Status Zakończone.
7. Klient odświeża Moje wypożyczenia: status "Zakończone".
8. Użytkownik bez roli: brak linku do panelu w menu, a wejście na adres panelu daje 403. Niezalogowany przy "Wynajmij" idzie do logowania i wraca na stronę auta.

Jak coś nie działa w UI, plan B dla danych do panelu: folder "Car Rental API: proces wynajmu" z Postmana albo kroki ze Swaggera z punktu 4.

## 4. Car Rental API (Maks, 2 min)

1. Swagger, Authorize z kluczem brokera.
2. `POST /api/v1/offers`, potem `POST /api/v1/rentals`: odpowiedź 202 Pending. Po około 10 s `GET /api/v1/rentals/{id}`: Confirmed (proces w tle).
3. Bezpieczeństwo: ten sam wynajem kluczem drugiej grupy daje 404, `/api/v1/employee/rentals` z samym kluczem brokera 401.
4. Plik `.exe` przemianowany na `.pdf` w potwierdzeniu zwrotu: 400 (sprawdzamy pierwsze bajty pliku).
5. Postman: Runner na kolekcji z docs/postman, wszystkie testy zielone (Karolina może pokazać zamiast Swaggera).

## 5. Baza i testy (Maks, 1 min)

- Foldery Migrations w obu API, tabele i indeksy w Query editor (Azure) albo w SSMS (lokalnie), 12 aut z seeda.
- Diagram ER (docs/er-diagram.md).
- Testy bazy na prawdziwym SQL Server w Testcontainers (osobny projekt), konfiguracja EF tylko fluent w Data/Configurations.

## 6. CI/CD i ADO (Maks i Karolina, 2 min)

1. Pipeline: czerwony build #3 na skanie paczek (Pkcs, podatność High), poprawka, zielony run z numerem 1.2.N.
2. Zakładki Tests i Code Coverage, próg 40%.
3. Etap Deploy: log migracji (bundle), App Service z `APP_VERSION` = numer builda, `/health` zwraca 200.
4. GitHub: ruleset na main i develop, PR z review i wymaganym checkiem, tag v1.2.0.
5. ADO Boards: stories KM2 z osobą, AC i podpiętym PR (zakładka Development), taski z godzinami. Test Plans: suite z TC1-TC12 i wynik Run (jeśli jest licencja).
6. Slajdy: Decisions & risks, Unknowns & questions (3 pytania), plan KM3.

## Plan B

- Brak deployu: pokazujemy zielony pipeline (Build, Test, wersja, skan, pokrycie), YAML z gotowym etapem Deploy i artefakt efbundles. Demo z docker compose lokalnie. Mówimy wprost: "Build, testy i paczka do wdrożenia działają. Deploy nie poszedł, bo [powód]. Robimy go do 26.11, task #143."
- API w Azure jest, Broker nie: Car Rental API w Swaggerze na azurewebsites.net, Broker i React lokalnie (slajd 14 nie wymaga frontu w chmurze na KM2).
- Entra nie działa: dev-login lokalnie i zrzut ekranu z błędem rejestracji, pytanie do prowadzącego o Google albo własny tenant.
- Brak sieci albo zimny start: zrzuty ekranu (ruleset, zielony run, zasoby w portalu, App Insights, Test Plans) w folderze na pulpicie.
- Nie pokazujemy mocków jako prawdziwej integracji i nie mówimy, że coś działa w chmurze, jeśli działa tylko lokalnie.
