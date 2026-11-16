# Architektura (KM2, 19.11.2026)

Aktualny diagram komponentów. Linie ciągłe to to, co jest w kodzie na KM2, przerywane (szare pola "KM3") robimy w następnym sprincie. Część "Azure" zacznie działać po pierwszym deployu (czeka na subskrypcję i service connection), do tego czasu to samo uruchamiamy lokalnie w docker compose. Źródło diagramu: `docs/diagrams/architecture.mmd`, obrazek: `docs/diagrams/architecture.png` (`mmdc -i architecture.mmd -o architecture.png`).

![Architektura](diagrams/architecture.png)

```mermaid
flowchart TB
  user([Użytkownik / pracownik<br/>przeglądarka])
  other([Broker innej grupy])

  subgraph azure["Azure, rg-carrentals (KM2)"]
    subgraph broker_app["App Service: app-carrentals-broker"]
      react["React SPA<br/>(wwwroot)"]
      broker["Broker API (BFF)<br/>.NET 10, /api/v1"]
    end
    subgraph api_app["App Service: app-carrentals-api"]
      carapi["Car Rental API<br/>.NET 10, /api/v1"]
      worker["Potwierdzanie wynajmów<br/>(BackgroundService)"]
    end
    brokerdb[("Azure SQL<br/>sqldb-broker<br/>Providers, UserProfiles, Quotes")]
    cardb[("Azure SQL<br/>sqldb-carrental<br/>Cars, Offers, Rentals")]
    appi["Application Insights<br/>appi-carrentals"]
  end

  subgraph km3["KM3 (planowane)"]
    blob[("Blob Storage<br/>załączniki, faktury")]
    mail["ACS Email<br/>maile"]
    sb["Service Bus<br/>+ worker"]
    lect["API prowadzącego"]
    otherapi["API innej grupy"]
  end

  entra["Entra ID<br/>(OIDC, rola Employee)"]

  subgraph cicd["CI/CD (KM2)"]
    gh["GitHub<br/>Jagna676/CarRentals"]
    ado["Azure Pipelines<br/>Build, Test, Deploy"]
  end

  user -- "HTTPS, ciasteczko sesji" --> react
  react -- "fetch /api/v1" --> broker
  broker -- "OIDC" --> entra
  broker -- "x-api-key" --> carapi
  broker --> brokerdb
  carapi --> cardb
  worker --> cardb
  other -- "x-api-key (OtherGroupBroker)" --> carapi
  broker -.-> appi
  carapi -.-> appi

  broker -. "KM3" .-> lect
  broker -. "KM3" .-> otherapi
  carapi -. "KM3" .-> blob
  carapi -. "KM3" .-> mail
  carapi -. "KM3" .-> sb

  gh -- "PR, push" --> ado
  ado -- "sc-azure-carrentals<br/>bundle migracji + deploy" --> azure

  classDef planned stroke-dasharray: 5 5,fill:#f4f4f4,color:#555;
  class blob,mail,sb,lect,otherapi planned;
```

## Co jest na KM2

- React (Vite + TypeScript) serwowany przez Broker API z `wwwroot`. Jedna domena, więc ciasteczko sesji Brokera jest first-party (decyzja 1 w decisions-risks-km2.md). Lokalnie Vite na 5173 z proxy `/api` do Brokera.
- Broker API (BFF): logowanie (Entra ID przez OIDC, lokalnie dev-login), profil, katalog z cache, wycena z limitem 15 s, proxy panelu pracownika. Woła nasze Car Rental API przez HTTP z kluczem `x-api-key` (broker OurBroker).
- Car Rental API: katalog, oferty, wynajmy (202 Pending, potwierdzenie w tle po około 10 s), zwroty, panel pracownika z rolą Employee. Broker innej grupy łączy się swoim kluczem (OtherGroupBroker) i widzi tylko swoje dane.
- Dwie bazy Azure SQL (Basic): sqldb-carrental i sqldb-broker. Schemat z migracji EF, aktualizowany przez pipeline (EF migrations bundle) przed wdrożeniem kodu. Migracje generuje Maks skryptem `scripts/add-migrations.ps1`, lokalnie te same bazy są w docker compose.
- Application Insights: Car Rental API wysyła requesty, zależności i logi audytu (bez kluczy i z maskowaniem danych osobowych), gdy jest ustawione `APPLICATIONINSIGHTS_CONNECTION_STRING`. Broker ma na razie audyt tylko w logu, podpięcie do Application Insights robimy w KM3.
- CI/CD: GitHub (ruleset na main i develop, ustawia Jaga) i Azure Pipelines (Build, Test, Deploy). Deploy przez service connection sc-azure-carrentals włączamy zmienną `deployToAzure`, jak będą zasoby.

## KM3

- API prowadzącego i API innej grupy jako kolejne adaptery `ICarRentalProvider` w Brokerze (dostawcy są już w tabeli Providers, wyłączeni).
- Wynajem przez Brokera (Rent Me), historia wynajmów użytkownika.
- Blob Storage na załączniki zwrotu i faktury (`BlobFileStorage` za interfejsem `IFileStorage`).
- Maile przez Azure Communication Services Email, przetwarzanie w tle przez Service Bus.

## Gdzie są dane osobowe

- Broker DB, tabela UserProfiles: imię, nazwisko, email, data urodzenia, data prawa jazdy, lokalizacja. Czyta tylko zalogowany użytkownik (swój profil).
- CarRental DB, tabela Rentals: imię, nazwisko i email z wniosku o wynajem. Czyta broker, który złożył wniosek (tylko status), i pracownik naszej wypożyczalni (rola Employee).
- Logi audytu: email i nazwiska maskowane, klucze, tokeny i ciasteczka pomijane.

## SLA snapshot (czas do pierwszej oferty)

Wymaganie: oferty mają się pokazać najpóźniej po 15 s. Broker pyta wszystkie wypożyczalnie naraz i po 15 s kończy, a te, które nie zdążyły, wpisuje do `skippedProviders`. Front czeka najwyżej 20 s (15 s Brokera plus zapas na odpowiedź).

Jak mierzymy: DevTools, zakładka Network, czas `POST /api/v1/offers` na stronie auta. Mierzymy trzy razy lokalnie (docker compose) i, jak będzie deploy, trzy razy na Azure. Wynik wpisujemy tutaj przed demo:

- lokalnie: do uzupełnienia
- Azure: do uzupełnienia (po pierwszym deployu)
