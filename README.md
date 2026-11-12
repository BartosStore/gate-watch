# Kontrola webů obchodníků

Aplikace pravidelně kontroluje úvodní stránky e-shopů. Hledá, jestli stránka nenačítá škodlivou adresu z blacklistu (URLhaus) nebo na ni nevede. Nálezy se dají vyřídit s poznámkou. Zadání je v [docs/ZADANI.md](docs/ZADANI.md).

> Stav: hotové API, UI a měření (kroky 0–7). Výsledky jsou v [docs/VYSLEDKY.md](docs/VYSLEDKY.md).

## Spuštění

Potřebujete Docker s Compose.

```bash
docker compose up --build
```

- UI i API: <http://localhost:8080>
- Kontrola stavu: `GET http://localhost:8080/health` (vrací 200, pokud je databáze dostupná)
- Data Postgresu jsou ve volume `pgdata` a přežijí restart. Smazání dat: `docker compose down -v`.

**Windows: `failed to execute bake: exit status 1`.** Tuto chybu hlásí Docker Compose 5 s buildx 0.38, když je složka v příkazové řádce zapsaná s malým písmenem disku (`c:\dev\…`). Buildx pak bere Dockerfile (`C:\dev\…\Dockerfile`) jako soubor mimo povolenou složku. V `docker --debug compose build` je vidět hláška `pass "--allow=fs.read=Dockerfile"`. Řešení: přejděte do složky s velkým písmenem disku, nebo jednorázově vypněte tuto kontrolu buildx:

```bash
cd /d C:\dev\cs\gate-watch
```

```bash
set BUILDX_BAKE_ENTITLEMENTS_FS=0
```

V PowerShellu (`$env:BUILDX_BAKE_ENTITLEMENTS_FS="0"`) a Git Bashi se problém obvykle neprojeví, protože cestu zapisují s velkým `C:`.

## API (popis v `openapi.yaml`)

Příklady jsou pro **bash** (Linux, macOS, Git Bash). Pod nimi je stejný postup pro **Windows PowerShell**, kde je `curl` jen alias pro `Invoke-WebRequest`.

Spuštění kontroly. Odpověď `202 {"run_id": "..."}` přijde hned a kontrola běží na pozadí:

```bash
curl -X POST http://localhost:8080/runs -H "Content-Type: application/json" -d '{"merchants":[{"id":"czc","url":"https://www.czc.cz/"}]}'
```

Průběh a výsledky. Při `running` obsahuje jen dokončené obchody, při `done` právě jeden výsledek na obchod:

```bash
curl http://localhost:8080/runs/<run_id>
```

Vyřízení nálezu. `id` je z `results[].alerts[].id` a tělo musí být v UTF-8:

```bash
curl -X POST http://localhost:8080/alerts/<id>/resolve -H "Content-Type: application/json" -d '{"note":"overeno"}'
```

Windows PowerShell:

```powershell
$run = Invoke-RestMethod -Method Post -Uri http://localhost:8080/runs -ContentType "application/json; charset=utf-8" -Body '{"merchants":[{"id":"czc","url":"https://www.czc.cz/"}]}'
Invoke-RestMethod "http://localhost:8080/runs/$($run.run_id)" | ConvertTo-Json -Depth 5
Invoke-RestMethod -Method Post -Uri http://localhost:8080/alerts/<id>/resolve -ContentType "application/json; charset=utf-8" -Body '{"note":"Ověřeno s obchodem"}'
```

Pomocné endpointy pro UI: `GET /alerts?resolved=false&page=1&pageSize=25` (nálezy) a `GET /merchants?search=&status=&page=1&pageSize=25` (obchody s poslední kontrolou). Při prvním startu se tabulka obchodů naplní z `data/merchants-sample.json`.

Souběžnost kontrol nastavuje proměnná `SCAN_CONCURRENCY` v `docker-compose.yml` (výchozí 8).

## Blacklist

Při startu se načte do DB a do paměti. Stav ukazuje `GET /health` (`blacklist_ready`, `blacklist_entries`).

- **URLhaus** vyžaduje bezplatný Auth-Key z [auth.abuse.ch](https://auth.abuse.ch/). Pokud ho máte, vložte do souboru `.env` vedle `docker-compose.yml` řádek `URLHAUS_AUTH_KEY=váš-klíč`. Bez klíče se použije `data/urlhaus-snapshot.txt`.
- **Vlastní záznamy:** `data/blacklist-custom.txt`, jedna URL na řádek. Projeví se po restartu (`docker compose up --build`).

## Vývoj bez Dockeru (volitelné)

Spusťte jen databázi a API běží lokálně (.NET 10 SDK, Node 24):

```bash
docker compose up -d db
dotnet run --project src/Api
cd frontend && npm install && npm run dev
```

Pro lokální běh je potřeba vystavit port Postgresu `5432` (např. přes `docker-compose.override.yml`). API pak běží na `http://localhost:8080` a Vite dev server přeposílá API volání na něj.

## Kontrola jedné stránky z příkazové řádky (vývoj)

Vypíše všechny zachycené adresy, jejich typ, blokované požadavky a shody s blacklistem. Nepotřebuje DB:

```bash
docker compose run --rm --no-deps app scan https://www.czc.cz/
```

## Testy

Celá sada se spouští v kontejneru: testy s Chromiem i integrační testy nad Postgresem. Příkaz se spouští z kořene repozitáře a funguje v PowerShellu, cmd i bashi:

```bash
docker compose run --rm --build tests
```

Na konci výpisu je počet úspěšných a neúspěšných testů. Testy používají samostatnou databázi `gatewatch_test`, která se při každém spuštění vytvoří znovu, takže data aplikace zůstanou netknutá.

Rychlé unit testy na hostiteli (testy s prohlížečem a s DB se přeskočí):

```bash
dotnet test
```

## Struktura

- `src/Api`: ASP.NET Core minimal API, EF Core (PostgreSQL), Playwright
- `tests/Api.Tests`: testy (xUnit)
- `frontend`: React + Vite + TypeScript; build jde do `src/Api/wwwroot`
- `openapi.yaml`: závazný popis API
- `docs/`: zadání, předpoklady a výsledky
