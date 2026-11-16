# Decisions & risks (KM2, 19.11.2026)

## Decyzja 1: React serwowany przez Broker API zamiast Azure Static Web Apps

Kontekst: na KM1 zakładaliśmy React na Static Web Apps i Broker API jako BFF z ciasteczkiem sesji. Static Web Apps w planie Free nie ma "linked backend". Front na *.azurestaticapps.net i Broker na *.azurewebsites.net to dwie różne strony, więc ciasteczko sesji Brokera byłoby third-party. Safari i Firefox takie ciasteczka blokują, a wtedy logowanie się sypie.

Decyzja: pipeline buduje React (`npm run build`) i kopiuje `frontend/dist` do `wwwroot` Brokera przed publish. Broker serwuje pliki (UseStaticFiles, MapFallbackToFile dla tras spoza /api). Jedna domena, ciasteczko first-party (HttpOnly, Secure, SameSite=Lax), bez CORS i bez dodatkowych kosztów. Lokalnie Vite działa z proxy /api na Brokera.

Konsekwencje: nie zakładamy zasobu Static Web Apps. Jak w KM3 będzie potrzeba, wracamy do SWA Standard z linked backend (około 9 USD miesięcznie), w YAML jest na to przełącznik `frontendHosting`.

## Decyzja 2: kod na GitHubie, Boards i Pipelines w Azure DevOps

Kontekst: kod od początku jest na GitHubie (Jagna676/CarRentals), a repo w ADO jest puste. Integracja Boards z GitHubem działa (AB#id w commitach i PR podpina je pod work itemy).

Decyzja: kodu nie przenosimy do Azure Repos, bo import nie przeniesie historii PR. Reguły gałęzi ustawiamy w GitHubie: ruleset na main i develop (PR z 1 approve, wymagany zielony pipeline Jagna676.CarRentals, bez force push, tylko merge commit). Pipeline jest w ADO i raportuje status do PR na GitHubie. Flow to uproszczony gitflow (main, develop, feature/*, hotfix/*), opisany w README.

Konsekwencje: w prywatnym repo ruleset wymaga GitHub Pro. Jaga zgłasza GitHub Education. Na zajęciach pytamy prowadzącego, czy to jest OK zamiast Azure Repos.

## Najważniejsze ryzyka

| Ryzyko | Mitygacja |
|---|---|
| Subskrypcja Azure (Azure for Students) aktywowana dopiero w tym tygodniu. Bez niej nie ma deployu ani bazy w chmurze. | Build i Test działają bez Azure. Deploy jest gotowy w YAML za przełącznikiem `deployToAzure`. Zasoby zakłada jeden skrypt (scripts/azure). Jak deploy nie zdąży, pokazujemy pipeline i lokalnie docker compose, a deploy robimy do 26.11 (#143). |
| Rejestracja aplikacji w Entra w tenancie PW może być zablokowana dla studentów. | Logowanie nie blokuje demo: lokalnie działa dev-login, a Car Rental API bez Entra działa dla brokerów i zamyka panel pracownika. Plan B to własny tenant albo Google OIDC (decyzja do śr 12:00). |
| Nie znamy API prowadzącego ani grupy do integracji. | Broker ma interfejs dostawcy i rejestr dostawców, więc nowe API to nowa klasa adaptera. Dostawca "lecturer" jest przygotowany i wyłączony. Na zajęciach pytamy o grupę i termin dokumentacji. Integracje to KM3. |
| Historia commitów tylko jednej osoby (PR #1 z 2.11). | Od 17.11 każdy commituje swoje części z AB#id ze swojego konta, PR do develop z review innej osoby, na koniec PR develop do main i tag v1.2.0. |
| Pokrycie testami może spaść poniżej 40% po dodaniu Brokera. | Testy Brokera piszemy razem z kodem (agregator, profil, integracyjne z Car Rental API). Próg jest w zmiennej `minCoverage`. Jak spadnie, obniżamy go świadomie i zapisujemy w ADO, kroku nie wyłączamy. |
| Branch protection w prywatnym repo na GitHub Free nie działa. | GitHub Education u Jagi. Jak Pro nie przyjdzie do śr 20:00, decyzja o repo publicznym. Review i tak robimy w każdym PR. |
