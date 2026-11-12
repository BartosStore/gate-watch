# Zadání pro Claude Code: Kontrola webů obchodníků (Comgate)

Tento soubor je kompletní zadání práce. Přečti ho celý, než začneš. Je psaný česky, aby mu rozuměl i zadavatel (vývojář, který s tebou pracuje krok za krokem).

## 0. Pravidla spolupráce (nejdůležitější část)

1. **Pracuj po krocích** podle sekce 7. Na konci každého kroku **zastav**. Nezačínej další krok, dokud zadavatel nenapíše "pokračuj".
2. **Nic necommituj.** Commity dělá zadavatel sám po kontrole.
3. Na konci každého kroku napiš stručný report: co se změnilo, jak to ověřit (konkrétní příkaz), jaká rozhodnutí jsi udělal, co je otevřené. K tomu navrhni text commit zprávy.
4. **Rychlost je priorita.** Cíl je hotové řešení za zhruba 4 hodiny (ne 8). Vybírej nejjednodušší řešení, které splňuje zadání. Žádné abstrakce, vrstvy ani knihovny navíc, pokud nejsou nutné.
5. **Nedělej nic, co v zadání není.** Volitelné kroky (sekce 8) začni jen na výslovný pokyn.
6. Když si nejsi jistý rozhodnutím, které ovlivní chování aplikace, zeptej se v reportu. Drobnosti rozhodni sám a zapiš do `docs/PREDPOKLADY.md` (jednořádkově).
7. Tři operace v `openapi.yaml` se nesmí změnit (cesty, schémata, kódy odpovědí). Každý přidaný endpoint se dopíše do téhož souboru i s popisem, co dělá a proč je potřeba.
8. Kód a identifikátory anglicky, README a dokumentace česky.
9. Pokud repozitář už obsahuje strukturu nebo šablonu (C# + React), použij ji místo vytváření nové.

## 1. O co jde (jednoduše)

Comgate provozuje platební bránu pro asi 20 000 e-shopů. Chceme u každého pravidelně zkontrolovat úvodní stránku, jestli nenačítá nebo nevede na škodlivou adresu ze známého blacklistu. Kolega, který to bude denně používat, potřebuje:

- rychle poznat, kde je problém, co se našlo a proč,
- nález vyřídit (s poznámkou) a později se k němu vrátit,
- aby ho stejný, už vyřízený nález při další kontrole netrápil znovu,
- aby se nová adresa neztratila jen proto, že starší nález už vyřídil.

Postavíme malou aplikaci: **backend (API + kontrola prohlížečem), databázi a webové UI**, vše spustitelné jedním příkazem `docker compose up --build`.

## 2. Technologie (už rozhodnuto, neměň)

- **Backend:** .NET (aktuální LTS), ASP.NET Core minimal API, EF Core, PostgreSQL.
- **Kontrola webů:** Microsoft Playwright pro .NET (Chromium, headless).
- **Frontend:** React + Vite + TypeScript, **MUI** (komponenty), TanStack Query, React Router.
- **Docker:** jeden kontejner aplikace (API + hotové statické UI z `wwwroot`) a jeden Postgres. **Jeden port 8080** pro API i UI. Aplikace v Dockeru je také bezpečnostní izolace kontroly (viz sekce 6).
- Playwright vyžaduje obraz s prohlížeči: použij `mcr.microsoft.com/playwright/dotnet` ve verzi shodné s NuGet balíčkem Playwright (zafixuj obě verze).

## 3. Jak funguje kontrola (pravidla domény)

Kontrola jednoho obchodu otevře **jen úvodní stránku** a zachytí:

- všechny skutečné **síťové požadavky** prohlížeče (skripty, styly, iframy, obrázky, XHR, fonty...), včetně těch, které vzniknou až po spuštění JavaScriptu,
- celý **řetěz přesměrování**,
- z DOM po načtení: `a[href]`, `form[action]`, `iframe[src]` (kam vedou odkazy a formuláře).

Pravidla:

- **Na nic neklikat, nic neodesílat, nepřihlašovat se, nechodit dál než na úvodní stránku.**
- Adresa jen napsaná v textu nebo v komentáři **není nález**. Nehledej regulárním výrazem v HTML.
- Celá kontrola jednoho webu (i s přesměrováním) končí **nejpozději do 10 s**. Po limitu se kontrola ukončí.
- Adresy z blacklistu **se nikdy nenavštěvují**: požadavek na ně se zaznamená a zablokuje (`page.route` → abort), než odejde. Zablokuj i požadavky na privátní/lokální adresy (localhost, 127.0.0.0/8, 10/8, 172.16/12, 192.168/16, 169.254/16, ::1, `file:`, ostatní než http/https).
- Chovej se jako běžný návštěvník: normální user agent, žádné zahlcování (výchozí souběžnost 8, konfigurovatelná proměnnou prostředí `SCAN_CONCURRENCY`).

### Normalizace URL (jedna sdílená funkce)

Každá URL (z requestu, odkazu, formuláře, blacklistu) projde stejnou normalizací:

- relativní adresy se převedou na absolutní vůči stránce, kde se našly,
- jen schémata `http` a `https` (ostatní zahodit),
- schéma a host malými písmeny, bez výchozího portu (80/443), bez fragmentu (`#...`),
- prázdná cesta = `/`, dotazový řetězec se zachová.

### Porovnání s blacklistem

Přesná shoda normalizované URL. (Rozhodnutí zapsat do předpokladů. Shoda na úrovni hostu je volitelné rozšíření.)

### Blacklist

- Výchozí zdroj: **URLhaus** (abuse.ch). Zkus stáhnout veřejný export. Pokud je vyžadován klíč, stahování selže nebo je nedostupné, použij uložený snapshot v repozitáři (`data/urlhaus-snapshot.txt`, stačí malý vzorek) a **napiš do reportu, co se stalo**.
- Vlastní testovací záznamy v `data/blacklist-custom.txt` (jedna URL na řádek). Aby měla aplikace co ukázat, přidej tam i URL skriptu, který některý ze vzorku skutečných e-shopů opravdu načítá (doplní se po kroku 7 měření, do té doby stačí příklad).
- Uloží se do DB. Při startu se načte do paměti (HashSet). Dokud blacklist není připravený, `POST /runs` vrací **503**.

### Nálezy (nejdůležitější pravidlo)

**Nález = dvojice (obchod, normalizovaná URL).** ID nálezu = první 32 znaků SHA-256 z `merchant_id + "\n" + normalizovaná_url` (stabilní napříč kontrolami).

- Stejný obchod + stejná URL při další kontrole = **pořád tentýž nález**: aktualizuje se jen `last_seen_at` (a `last_check_id`). `resolved` a `note` se **nikdy nepřepíšou kontrolou**.
- Stejná URL u jiného obchodu = **samostatný nález**.
- Nová URL u stejného obchodu = **nový nevyřízený nález**, i když starší nález už je vyřízený.
- Zápis přes `INSERT ... ON CONFLICT (merchant_id, url) DO UPDATE SET last_seen_at...` (unikátní index).

### Stavy kontroly

V DB (jemné): `ok`, `found`, `timeout`, `unreachable`. V API (`MerchantResult.status`) se mapují takto:

- `ok` a `found` → API `ok` (u `found` je `matches` neprázdné),
- `timeout` a `unreachable` (a HTTP chyba hlavní stránky) → API `error` s popisem v `error`.

Nedokončená kontrola **nikdy nesmí vypadat jako "bez nálezu"**. Pokud se u timeoutu stihl najít nález, uloží se a vrátí v `matches`, ale kontrola zůstává `timeout`. Kontrola, která skončí chybou, nálezy nijak neruší ani nemaže.

## 4. Datový model (PostgreSQL, EF Core migrace)

- `merchants`: `id` (text, PK, z API), `url`, `created_at`
- `runs`: `id` (uuid, PK), `status` (running/done/failed), `error`, `created_at`, `finished_at`
- `run_items`: `run_id`, `merchant_id`, `state` (pending/done), `check_id` (nullable); PK (`run_id`, `merchant_id`)
- `checks`: `id`, `run_id`, `merchant_id`, `outcome` (ok/found/timeout/unreachable), `error`, `duration_ms`, `started_at`, `urls_seen` (počet)
- `findings`: `id` (text PK, hash), `merchant_id`, `url`, `kind` (script/stylesheet/image/iframe/link/form/redirect/other), `blacklist_source`, `first_seen_at`, `last_seen_at`, `first_check_id`, `last_check_id`, `resolved` (bool), `resolved_at`, `note`; **unikátní index (`merchant_id`, `url`)**
- `blacklist_entries`: `url` (normalizovaná, PK), `source`, `added_at`
- `blacklist_meta`: jeden řádek: `last_import_at`, `entry_count`, `source_note`

Všechno musí přežít restart aplikace (volume pro Postgres v compose).

## 5. API

Soubor `openapi.yaml` v kořeni repozitáře je závazný. Tři operace beze změny:

1. `POST /runs`: spustí kontrolu seznamu obchodů, vrací `202 {run_id}` do 5 s (běží na pozadí). `400` při špatném vstupu (prázdné pole, neplatná URL), `503` dokud není blacklist připravený.
2. `GET /runs/{run_id}`: `status` (running/done/failed), `results` (při `running` jen dokončené obchody, při `done` právě jeden výsledek na obchod), `error` (jen u failed). `404` pro neznámé id.
3. `POST /alerts/{id}/resolve` s `{note}`: vyřídí nález, opakované volání je idempotentní (jen přepíše poznámku, nevytvoří nález). `404` pro neznámé id, `400` pro špatný vstup.

`MerchantResult`: `merchant_id`, `status` (ok/error), `matches` (unikátní absolutní URL skutečných shod s blacklistem, zůstávají i po vyřízení), `alerts` (právě jeden záznam ke každé URL v `matches`: `id`, `url`, `resolved`, `note` podle **aktuální** evidence), `error`.

Z DB se čte vždy, ne z paměti (kvůli restartu a shodě API s UI).

### Další endpointy (minimum pro UI, dopsat do openapi.yaml s vysvětlením)

- `GET /alerts?resolved=&merchant_id=&page=&pageSize=`: seznam nálezů pro hlavní obrazovku (včetně `kind`, `blacklist_source`, `first_seen_at`, `last_seen_at`, `merchant_id`, `note`).
- `GET /merchants?search=&status=&page=&pageSize=`: obchody s výsledkem poslední kontroly, počtem nevyřízených nálezů a časem poslední kontroly. Řazení: nejdřív nevyřízené nálezy, pak nedokončené/nedostupné, pak OK, nakonec nezkontrolované.

## 6. Bezpečnost

- Kontrola běží uvnitř Docker kontejneru, ne na hostiteli. Chromium se spouští s nutnými parametry pro kontejner. Kontejner nedostává žádná tajemství ani přístup k hostiteli. V dokumentaci stručně popiš model hrozeb (napadený web, škodlivý skript) a opatření.
- Každý web v novém `BrowserContext` (žádné sdílené cookies), bez stahování souborů, bez povolení (geolokace, kamera...), dialogy automaticky zavřít.
- Blokace blacklistu a privátních sítí viz sekce 3. Omezená souběžnost.
- Žádné odesílání formulářů, žádné přihlašování, žádné procházení dál než úvodní stránka.

## 7. Kroky práce (po každém krokem STOP)

Odhad času je orientační cíl pro celé řešení (~4 h).

### Krok 0: Kostra a Docker (≈ 20 min)
Solution (`src/Api` + testy), `Dockerfile` (multi-stage: build frontendu → publish .NET → runtime na Playwright image), `docker-compose.yml` (app + Postgres s healthcheckem a volume), `.gitignore`, EF Core napojení, migrace při startu, `GET /health`, výchozí `README.md` se spuštěním.
**Hotovo když:** `docker compose up --build` z čistého klonu nastartuje a `GET /health` vrací 200.

### Krok 1: Doména, DB a normalizace URL (≈ 25 min)
Entity a migrace podle sekce 4, `UrlNormalizer`, funkce pro ID nálezu. Unit testy normalizace (relativní URL, port, fragment, velikost písmen, nepodporovaná schémata).
**Hotovo když:** migrace projde na čisté DB, testy normalizace zelené.

### Krok 2: Blacklist (≈ 20 min)
Import URLhaus (nebo snapshot, viz sekce 3) + vlastní záznamy do DB, načtení do paměti při startu na pozadí, příznak `Ready`, `POST /runs` vrací 503 před připraveností.
**Hotovo když:** po startu je blacklist připravený, počty záznamů jsou v logu a v `blacklist_meta`.

### Krok 3: PageScanner (≈ 35 min)
Služba otevře URL v Playwrightu a vrátí seznam zachycených normalizovaných URL s typem (`kind`) a informaci o dokončení/timeoutu/nedostupnosti. Zachycení requestů a přesměrování, vyčtení `a[href]`, `form[action]`, `iframe[src]` po načtení, blokace blacklistu a privátních sítí přes `page.route`, celkový limit 10 s přes `CancellationToken`. Sdílený browser, nový context na web.
**Hotovo když:** dočasný vývojářský příkaz nebo test projde jeden skutečný web a vypíše zachycené adresy a výsledek. Zkontroluj ručně, že blacklistovaná URL není stažena (jen zaznamenána).

### Krok 4: Běhy, nálezy a tři povinné endpointy (≈ 45 min)
`FindingService` (upsert podle sekce 3), `RunService`, `RunWorker` (`BackgroundService`, souběžnost přes `SemaphoreSlim`), `POST /runs`, `GET /runs/{id}`, `POST /alerts/{id}/resolve`. Při startu uzavři běhy ve stavu `running` z minulého procesu jako `failed` s popisem "přerušeno restartem" (neposílej je znovu). Výjimka u jednoho obchodu = chyba jen tohoto obchodu, ne celého běhu.
Testy (alespoň integrační nad Postgresem nebo in-memory fake scannerem): stejný nález po druhé kontrole se nezdvojí a zůstává vyřízený; nová URL vytvoří nový nález; stejná URL u jiného obchodu je samostatný nález; timeout nevrací čistý `ok`; idempotentní resolve.
**Hotovo když:** `curl` na všech třech endpointech odpovídá přesně podle `openapi.yaml`, scénář "vyřídit → znovu zkontrolovat" funguje, výsledky přežijí restart kontejneru.

### Krok 5: Pomocné endpointy (≈ 15 min)
`GET /alerts` a `GET /merchants` podle sekce 5. Seed vzorku obchodů z `data/merchants-sample.json` do tabulky `merchants` při startu (pokud je prázdná). Dopsat oba endpointy do `openapi.yaml` s popisem a důvodem.
**Hotovo když:** oba endpointy vrací stránkovaná data a jsou zdokumentované.

### Krok 6: Frontend (≈ 60 min)
Vite + React + TS + MUI, build do `wwwroot`. Obrazovky:
- **Nálezy (úvodní):** `DataGrid` se serverovým stránkováním, výchozí filtr "nevyřízené", sloupce: obchod, škodlivá adresa (zkrácená uprostřed), typ, poprvé/naposledy, stav. Klik otevře **boční panel (Drawer)**: co se našlo a kde (typ prvku, celá URL s kopírováním), se kterým záznamem blacklistu a z jakého zdroje, formulář "Vyřídit" s poznámkou (jedním klikem), u vyřízeného nálezu kdy a s jakou poznámkou.
- **Obchody:** `DataGrid` (obchod, stav poslední kontroly, počet nevyřízených nálezů, čas poslední kontroly), hledání, filtr podle stavu, tlačítko **"Spustit kontrolu"** (odešle obchody ze vzorku do `POST /runs`), průběh (progress podle `GET /runs/{id}`, polling) a po dokončení obnovení dat.
- `StatusChip` jednotný pro stavy: červená = nalezeno/nevyřízeno, žlutá = nedokončeno (timeout), oranžová = nedostupné, zelená = OK, šedá = nezkontrolováno/vyřízeno. **Timeout a nedostupné nesmí být nikdy zelené.**
- Prázdné stavy, chybové stavy, načítání, toast po vyřízení.
**Hotovo když:** `docker compose up --build` zobrazí UI na `http://localhost:8080`, kolega dokáže spustit kontrolu, projít nález a vyřídit ho, změna je vidět i v `GET /runs/{id}`.

### Krok 7: Vzorek a měření (≈ 25 min)
Vyber vzorek **50 až 100 skutečných českých e-shopů** (`data/merchants-sample.json`, vyber různé platformy a velikosti). Spusť kontrolu, změř: celkový čas, průměrný a 95. percentil času na web, podíl `ok`/`found`/`timeout`/`unreachable`, využití CPU a paměti kontejneru. Vlastní testovací záznam do blacklistu přidej podle skutečně načítaného skriptu. Z měření odhadni dobu a cenu kontroly **všech 20 000** (vzorec: čas na web × 20 000 ÷ souběžnost; cena podle velikosti instance, předpoklady uvést).
**Hotovo když:** `docs/VYSLEDKY.md` obsahuje změřená čísla, odhad a poctivě odlišené "změřeno" vs. "odhad".

### Krok 8: Dokumentace a finální kontrola (≈ 20 min)
- `README.md`: co aplikace dělá, spuštění jedním příkazem, porty, adresy, jak spustit kontrolu, jak spustit testy.
- `docs/VYSLEDKY.md`: které weby se skenovaly, k čemu jsme došli, odhad doby a nákladů, co se povedlo a co ne.
- `docs/PREDPOKLADY.md`: zásadní předpoklady a rozhodnutí (identita nálezu, normalizace, shoda s blacklistem, mapování stavů, timeout s nálezem, model hrozeb, co se děje při restartu, atd.).
- Finální kontrola: čistý klon → `docker compose up --build` → vše funguje; `openapi.yaml` odpovídá implementaci včetně přidaných endpointů; testy zelené.
**Hotovo když:** zadavatel může odevzdat (repo, SHA commitu, screenshoty, konverzace s AI).

## 8. Volitelné kroky (jen na pokyn, podle zbylého času)

Pořadí podle přínosu:

- **A. Obrazovka Kontroly:** seznam běhů, souhrn (počty po stavech, průměrný čas), nový endpoint `GET /runs`.
- **B. Detail obchodu:** historie kontrol (čas, délka, výsledek), nálezy obchodu, endpoint `GET /merchants/{id}/checks`.
- **C. Testovací web:** malý lokální "škodlivý" web v compose (načítá skript a formulář na adresu z blacklistu), aby šel nález předvést bez skutečného obchodu.
- **D. Blacklist v UI:** stav importu, tlačítko aktualizovat, správa vlastních záznamů (`GET /blacklist/status`, `POST /blacklist/refresh`).
- **E. Znovu otevřít nález** (`POST /alerts/{id}/reopen`) a časová osa nálezu.
- **F. Shoda blacklistu na úrovni hostu**, kontrola DNS (rozlišení na privátní IP) proti SSRF.
- **G. Export nálezů do CSV.**

## 9. Co nedělat

- Žádné přihlašování, role, plánovač ani napojení na systémy Comgate.
- Neměnit tři povinné operace v `openapi.yaml`.
- Nenavštěvovat adresy z blacklistu, neklikat, neodesílat formuláře, nezahlcovat weby.
- Neprodlužovat řešení zbytečnými abstrakcemi. Když je jednodušší cesta, jdi jí a zapiš to do předpokladů.
