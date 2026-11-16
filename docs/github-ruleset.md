# Reguły gałęzi na GitHubie (ruleset "main i develop")

Kod jest na GitHubie (Jagna676/CarRentals), więc reguły gałęzi ustawiamy w GitHubie, a nie w ADO. Repo w ADO jest puste i polityki ADO niczego by nie chroniły. Ustawia Jaga, bo jest właścicielką repo. Około 30 minut.

W prywatnym repo rulesety działają tylko na GitHub Pro, Team albo Enterprise. Jaga zgłasza GitHub Education (Student Developer Pack daje Pro). Jak Pro nie będzie do środy 20:00, zespół decyduje, czy repo robimy publicznym (wcześniej sprawdzamy historię pod kątem sekretów, dziś w repo są tylko klucze deweloperskie "change-me").

## Przed rulesetem

Kolejność ma znaczenie. GitHub musi już znać nazwę checka z Azure Pipelines, inaczej nie da się go wybrać.

1. Gałąź develop: Code, lista gałęzi, wpisać `develop`, "Create branch develop from main". Albo w terminalu:
   ```bash
   git checkout main && git pull && git checkout -b develop && git push -u origin develop
   ```
2. Pierwszy PR do develop (np. `feature/142-pipeline` z nowym azure-pipelines.yml). Czekamy, aż w PR pojawi się check z Azure Pipelines. Nazwa to `Jagna676.CarRentals` (nazwa pipeline'u w ADO). Mogą być też pod-checki etapów, wybieramy główny.

## Ustawienia repo

Settings, Collaborators and teams (Collaborators):
- Add people: Maks i Karolina z rolą Write (nie Admin), żeby reguły ich obejmowały.

Settings, General, sekcja Pull Requests:
- Allow merge commits: włączone
- Allow squash merging: wyłączone
- Allow rebase merging: wyłączone
- Automatically delete head branches: włączone

Merge commit zostawia w historii wszystkie commity z autorami i datami. To jest nam potrzebne do punktu "historia w sprintach".

Settings, Code security (Advanced Security):
- Dependabot alerts: Enable. Działa też w prywatnym repo za darmo i uzupełnia skan paczek w pipeline.

## Ruleset

Settings, Rules, Rulesets, New ruleset, New branch ruleset.

- Ruleset Name: `main i develop`
- Enforcement status: Active
- Bypass list: puste (Jaga nie dodaje siebie)
- Target branches:
  - Add target, Include default branch (main)
  - Add target, Include by pattern, `develop`
- Rules (zaznaczyć):
  - Restrict deletions
  - Block force pushes
  - Require a pull request before merging:
    - Required approvals: 1
    - Dismiss stale pull request approvals when new commits are pushed
    - Require conversation resolution before merging
    - Allowed merge methods: tylko Merge
  - Require status checks to pass:
    - Add checks, wpisać `Jagna676.CarRentals`, wybrać z listy (źródło Azure Pipelines)
    - "Require branches to be up to date before merging" zostawiamy wyłączone, żeby nie tracić czasu i minut agenta
- Create

Jak GitHub pokazuje tylko klasyczną ochronę: Settings, Branches, Add classic branch protection rule. Branch name pattern `main`, te same opcje (Require a pull request, 1 approval, dismiss stale, conversation resolution, Require status checks z `Jagna676.CarRentals`), do tego "Do not allow bypassing the above settings". Potem to samo dla `develop`.

## Sprawdzenie i dowód na demo

- Push prosto do main ma się nie udać ("protected branch").
- W PR bez review przycisk Merge jest zablokowany ("Review required"). Tak samo przy czerwonym pipeline.
- Zrzut ekranu rulesetu i jednego PR z zablokowanym merge (do slajdu, na wypadek braku sieci).
- Zakładka Insights, Contributors pokazuje commity od trzech osób.

## Po stronie ADO

To nie jest ruleset, ale pasuje do "security with build":
- Project settings, Pipelines, Settings: włączone "Limit job authorization scope to current project for non-release pipelines" i "Protect access to repositories in YAML pipelines".
- Pipelines, Jagna676.CarRentals, Edit, Triggers: "Override the YAML continuous integration trigger" i "Override the YAML pull request trigger" wyłączone (liczy się YAML).
- Environment `dev` z "Branch control" na `refs/heads/main` (opis w scripts/azure/README.md).
