# KM2: co musi zrobić człowiek (kolejność)

Claude napisał kod, YAML, skrypty i teksty. Tego, co niżej, Claude nie zrobi, bo wymaga logowania, zgód albo konta konkretnej osoby. Kolejność ma znaczenie: wyżej są rzeczy, które blokują resztę.

## Pon 16.11 (wieczór)

1. Maks: Azure for Students na koncie PW, potem lista dozwolonych regionów (Policy, Assignments, "Allowed resource deployment regions"). Wynik na grupę.
2. Maks: .NET 10 SDK obok .NET 8 (migracje EF 10 nie wygenerują się na .NET 8).
3. Maks: 2-minutowy test w entra.microsoft.com, App registrations, New registration. Wynik na grupę.
4. Jaga: GitHub Education (Student Developer Pack), żeby ruleset działał w prywatnym repo.
5. Jaga: Settings, Collaborators: Maks i Karolina z rolą Write.
6. Jaga: ustalić, kto jest adminem organizacji ADO CarRentalJKM. Admin daje Maksowi Project Administrator (service connections, environments, Library).
7. Wszyscy: `git config user.email` = email konta GitHub (inaczej commity nie przypiszą się do osoby). Jaga i Karolina: .NET 10 SDK, Node 22, Docker, Git.
8. Jaga i Karolina: odpowiedź, czy macie subskrypcję Azure (plan B, gdyby u Maksa nie wyszło).

## Wt 17.11

9. Maks: gałąź develop z main, potem branch `feature/142-pipeline` z nowym azure-pipelines.yml i poprawką Pkcs. PR do develop, review Jaga. Odczytać % pokrycia (zakładka Code Coverage) i wpisać na grupę.
10. Maks: migracje skryptem `scripts/add-migrations.ps1` (Docker albo .NET 10 SDK). Powstają foldery `backend/CarRental.Api/Migrations` i `backend/Broker.Api/Migrations`. Usunąć starą lokalną bazę z EnsureCreated. Sprawdzić `docker compose up --build`. Commit migracji CarRental (AB#143), migracje Brokera commituje Jaga (AB#66).
11. Każdy commituje swoje części z AB#id i robi PR do develop z review innej osoby (min. 3 PR z Approve i jednym zdaniem komentarza): Maks Car Rental API i pipeline, Jaga Broker, Karolina front, Postman i docs.
12. Maks: skrypt `scripts/azure/setup-resources.sh` w Cloud Shell z regionem z punktu 2 (opis w scripts/azure/README.md). Sekrety z `~/carrentals-secrets.env` do menedżera haseł.
13. Maks: service connection `sc-azure-carrentals` (manual, workload identity federation, managed identity id-carrentals-ado), environment `dev` z Branch control na main, variable group `carrentals-dev` z CARRENTAL_DB_CONNECTION i BROKER_DB_CONNECTION jako secret.
14. Maks: rejestracje Entra carrentals-api i carrentals-broker (kroki w scripts/azure/README.md), clientId i tenantId dla Jagi. Jak tenant PW blokuje: własny tenant albo Google, decyzja z Jagą do śr 12:00.
15. Jaga: po pierwszym przebiegu pipeline'u na PR ruleset "main i develop" i ustawienia repo (docs/github-ruleset.md), zrzut ekranu.
16. Karolina: w ADO przypisanie stories KM2, opisy, AC, Story Points (docs/ado/ado-stories.json) i taski z estymacją (docs/ado/ado-tasks.json).
17. Maks: ADO, Project settings, Pipelines, Settings: "Limit job authorization scope" i "Protect access to repositories" włączone, brak override triggerów w UI.

## Śr 18.11

18. Maks: `deployToAzure: 'true'` (PR do develop, potem do main), "Permit" w ADO przy pierwszym przebiegu. Sprawdzić log migracji, /health, Swagger, APP_VERSION, tabelę Cars. Termin 20:00, potem plan B.
19. Karolina: kolekcja Postman (docs/postman) odpalona lokalnie, wszystkie testy zielone, commit i zrzut ekranu z Runnera.
20. Karolina: Test Plans, New Test Plan. Jak działa bez triala: suite "Feature: katalog aut i oferty" z TC1-TC12 (docs/ado/test-cases.md) i jeden Run.
21. Jaga: test logowania kontem PW, jeśli aplikacja jest w innym tenancie (zgoda admina). Jak do 20:00 nie ma Pro: decyzja o repo publicznym, potem ruleset.
22. Wszyscy: review cudzych PR (Approve + jedno zdanie). Freeze funkcji o 22:00.

## Czw 19.11 (przed checkpointem)

23. Maks: PR develop do main, review Jaga, merge, tag `v1.2.0` (GitHub, Releases, Draft a new release, tag na main, albo `git tag v1.2.0 && git push origin v1.2.0`).
24. Maks: ostatni deploy z main, rozgrzanie App Service 10 min przed demo.
25. Maks: zrzuty ekranu (ruleset, zielony run, zasoby w portalu, App Insights, Test Plans).
26. Karolina: domknięcie work itemów (stany, linki do PR, Completed Work), niezrobione stories do KM3. Slajdy Decisions & risks, Unknowns & questions, plan KM3.
27. Jaga: aktualny diagram architektury na slajd (docs/architecture.md).
28. Wszyscy: jedna próba demo wg docs/demo-km2.md.

## Na zajęciach

Pytania do prowadzącego zadajemy na zajęciach w czwartek. Lista jest w docs/unknowns-questions-km2.md (grupa do integracji, termin dokumentacji API prowadzącego, GitHub zamiast Azure Repos, .NET 10, cena przy wcześniejszym zwrocie). Odpowiedzi zapisujemy na tablicy i w ADO (#29).
