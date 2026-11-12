# Výsledky: vzorek, měření a odhad pro 20 000 e-shopů

Měřeno 2026-11-12. U každého čísla je uvedeno, jestli je **změřené**, nebo **odhad**.

## Vzorek

- `data/merchants-sample.json` obsahuje **93 skutečných českých e-shopů** různých velikostí a oborů: velké obchody s elektronikou (czc, datart, alza), potraviny (rohlik, kosik, itesco), lékárny (benu, pilulka, drmax), módu a obuv (zoot, footshop, bibloo), sport a outdoor (sportisimo, hudy, 4camping), knihy (kosmas, knihydobrovsky, martinus), nábytek a hobby (ikea, jysk, hornbach, obi), zvířata, kosmetiku, hudební nástroje, autodíly a menší specializované obchody (hadex, astratex, ruzovyslon…).
- Před zařazením jsem u každé domény ověřil DNS a 3 neexistující domény vyřadil. Odpověď na prostý `curl` (200 u 69 z 93) je jen orientační.
- Platformy (Shoptet, Magento, vlastní řešení…) jsem cíleně nevybíral ani nevyhodnocoval, protože aplikace zachycené URL neukládá. Vzorek je proto pestrý hlavně podle velikosti a oboru.

## Prostředí měření (změřeno)

- Docker Desktop na Windows 11 a vývojářský počítač. Docker VM měla 12 vCPU a 31 GiB RAM, připojení bylo domácí nebo kancelářské.
- Aplikace běžela v jednom kontejneru: jeden proces, jeden Chromium a jeden Playwright driver. Výchozí souběžnost je 8 (`SCAN_CONCURRENCY`) a limit je 10 s na web.
- Blacklist obsahoval ukázkový snapshot URLhaus (5 záznamů) a 3 vlastní záznamy. Velikost blacklistu výkon prakticky neovlivňuje, protože jde o vyhledání v hashovací tabulce.
- CPU a paměť jsem vzorkoval přes `docker stats` během běhu. 100 % odpovídá 1 jádru.

## Výsledky (změřeno)

| Souběžnost | Celkem 93 webů | Průměr na web | Medián | 95. percentil | ok + found | timeout | unreachable | CPU průměr / max | Paměť max |
|---|---|---|---|---|---|---|---|---|---|
| 4 | 106,2 s | 4,39 s | 4,13 s | 10,0 s | 59 (63,4 %) | 7 (7,5 %) | 27 (29,0 %) | 474 % / 727 % | 987 MiB |
| **8 (výchozí)** | **79,6 s** | **6,46 s** | 6,56 s | 10,0 s | **51 (54,8 %)** | 14 (15,1 %) | 28 (30,1 %) | 635 % / 951 % | 884 MiB |
| 16 | 57,9 s | 8,91 s | 10,0 s | 10,0 s | 8 (8,6 %) | 74 (79,6 %) | 11 (11,8 %) | 575 % / 815 % | 994 MiB |

- Kontrolní běh se souběžností 8, který spustil zadavatel z UI, dopadl prakticky stejně: 78,9 s, průměr 6,45 s, 51 ok, 15 timeout, 27 unreachable. **Výsledek je opakovatelný.**
- U úspěšně dokončených webů prohlížeč zachytil v průměru 488 adres (nejvýš 2 290).
- Počet `found` a nálezy: czc.cz (skript `ngastatic.com/s4c/tracker.js`) skončil jako `found`. knihydobrovsky.cz (skript `cdn.retino.com/badgeInjector.js`) skončil jako `timeout` **s uloženým nálezem**, takže se ukázalo pravidlo „nedokončená kontrola s nálezem zůstává timeout, ale nález se uloží“. Oba skripty jsou neškodné a do `data/blacklist-custom.txt` jsou přidané jen kvůli ukázce.

### Proč tolik chyb (změřeno a rozbor)

Rozpad chyb při souběžnosti 8:

| Chyba | Počet |
|---|---|
| HTTP 403 Forbidden | 21 |
| Kontrola nedokončena do 10 s | 14 |
| `net::ERR_CONNECTION_FAILED` | 3 |
| HTTP 429 Too Many Requests | 2 |
| `net::ERR_FAILED` | 2 |

1. **Ochrana proti botům (403/429).** Kvůli zadání „adresy z blacklistu se nenavštěvují ani přes přesměrování“ jde každý požadavek přes `route.FetchAsync`. Requesty tedy neposílá Chromium, ale Node driver Playwrightu, a ochrany typu Cloudflare nebo Akamai ho podle otisku TLS a HTTP poznají. Stejné weby odmítají i prostý `curl`. Řešení je popsané v [PREDPOKLADY.md](PREDPOKLADY.md) jako otevřený bod.
2. **Úzké hrdlo driveru.** Se souběžností 16 skončí 80 % webů timeoutem, přestože CPU využívá jen asi 6 z 12 jader. Všechny požadavky totiž prochází jedním Node procesem driveru. Se souběžností 4 je timeoutů 7 a jsou to stejné pomalé weby jako při souběžnosti 8 (aurum, hadex, istyle, knihydobrovsky, luxor, nay a další). Zhruba polovina timeoutů při souběžnosti 8 je tedy způsobená zátěží a polovina samotnými weby.
3. **Spotřeba CPU je stabilní, asi 5,4 CPU-sekundy na web** při souběžnosti 4 i 8 (474 % × 106 s ≈ 635 % × 80 s ≈ 505 CPU-s na 93 webů).

Žádná chyba se nezobrazuje jako „bez nálezu“: v API má stav `error` s popisem, v UI je žlutá nebo oranžová.

## Odhad pro 20 000 e-shopů (odhad)

Vzorec ze zadání: **čas na web × 20 000 ÷ souběžnost.**

| Varianta | Výpočet | Doba |
|---|---|---|
| Jedna instance, souběžnost 8 | 6,46 s × 20 000 ÷ 8 | **16 150 s ≈ 4,5 h** |
| Kontrola podle změřené propustnosti (93 webů / 79,6 s) | 20 000 ÷ 1,17 webů/s | 17 100 s ≈ 4,8 h |
| Jedna instance, souběžnost 4 (méně timeoutů) | 4,39 s × 20 000 ÷ 4 | 21 950 s ≈ 6,1 h |
| 5 instancí po souběžnosti 8 | 4,5 h ÷ 5 | ≈ 0,9 h |

Potřebný výkon: zhruba 5,4 CPU-s × 20 000 ≈ 108 000 CPU-s ≈ **30 CPU-hodin** na celý běh. Jedna instance se souběžností 8 průměrně využije 6–7 jader, takže potřebuje **8 vCPU a 2–4 GB RAM**. Kontejner měl nejvýš 1 GiB. Paralelizace jde škálovat jen horizontálně (víc kontejnerů nebo instancí), protože jeden driver víc než asi 8 souběžných kontrol nezvládne.

### Cena (odhad)

Předpoklady:
- jedna instance s 8 vCPU běží asi 4,8 h;
- DB, úložiště a provoz jsou zanedbatelné. Data se nestahují a odpovědi jdou jen přes RAM. Síťový provoz jsem neměřil;
- ceny jsou z veřejných ceníků a srovnávačů ke 2026-11 a před nasazením je potřeba je ověřit u poskytovatele.

| Instance (8 vCPU) | Cena za hodinu | 1 kompletní běh (≈ 4,8 h) | Denně po dobu 30 dní |
|---|---|---|---|
| AWS c7i.2xlarge, eu-central-1, on-demand | ≈ $0,41 | **≈ $2,0** | ≈ $59 |
| AWS c7i.2xlarge, spot | ≈ $0,20 | ≈ $1,0 | ≈ $29 |
| Hetzner CCX33 (8 dedikovaných vCPU, 32 GB), fakturace po hodinách | ≈ $0,22 | **≈ $1,1** | ≈ $32 |

Při 5 instancích současně je celková cena stejná a běh trvá asi hodinu. Platí se za CPU-hodiny, ne za počet strojů.

## Co se povedlo a co ne

**Povedlo se**
- Kontrola respektuje zadání. Blacklistované adresy se nestahují ani jako cíl přesměrování; ověřuje to automatický test s lokálním webem. Na nic se neklikalo, nic se neodesílalo, limit 10 s se dodržel (max. 10,0 s).
- Nedokončené kontroly nikdy nevypadají jako „bez nálezu“ a nález zachycený během timeoutu se uloží.
- Výsledky jsou opakovatelné a měření dvou nezávislých běhů se souběžností 8 se shoduje.

**Nepovedlo se nebo zůstává otevřené**
- **Asi 45 % webů ve vzorku kontrola nedokončí** (403, 429 a timeouty). To je hlavní slabina současného řešení. Pravděpodobné zlepšení: posílat požadavky přímo z Chromia a cíle přesměrování blokovat jinak (např. CDP `Fetch`). Měl by klesnout počet 403 i spotřeba CPU a jeden proces by zvládl vyšší souběžnost. Zatím jsem to nezkoušel (otevřený bod).
- URLhaus vyžaduje Auth-Key, takže se používá jen ukázkový snapshot. Skutečná data z URLhaus jsem nestahoval.
- Odhad doby a ceny vychází z vývojářského počítače, ne z cílové cloudové instance. Rozdíl v síti a CPU může být ±50 %.
