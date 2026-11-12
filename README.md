# Kontrola webů obchodníků

Aplikace pravidelně kontroluje úvodní stránky e-shopů. Hledá, jestli stránka nenačítá škodlivou adresu z blacklistu (URLhaus) nebo na ni nevede. Nálezy se dají vyřídit s poznámkou. Zadání je v [docs/ZADANI.md](docs/ZADANI.md).

> Stav: kostra (krok 0). Funkce přibývají postupně.

## Spuštění

Potřebujete Docker s Compose.

```bash
docker compose up --build
```

- UI i API: <http://localhost:8080>
- Kontrola stavu: `GET http://localhost:8080/health` (vrací 200, pokud je databáze dostupná)
- Data Postgresu jsou ve volume `pgdata` a přežijí restart. Smazání dat: `docker compose down -v`.

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

## Testy

```bash
dotnet test
```

## Struktura

- `src/Api`: ASP.NET Core minimal API, EF Core (PostgreSQL), Playwright
- `tests/Api.Tests`: testy (xUnit)
- `frontend`: React + Vite + TypeScript; build jde do `src/Api/wwwroot`
- `openapi.yaml`: závazný popis API
- `docs/`: zadání, předpoklady a výsledky
