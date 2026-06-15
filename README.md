# Finančný plán

Webová aplikácia na evidenciu pravidelných aj variabilných príjmov, výdavkov,
viacerých sporiacich účtov, investícií a plánovanej hypotéky. Zvolené percento
kladného mesačného prebytku automaticky rozdeľuje medzi sporenie a investície.
Používateľ môže manuálne potvrdiť aktuálne zostatky sporiacich účtov aj investícií.
Pri každom načítaní aplikácia od posledného potvrdenia dopočíta očakávaný stav
k dnešnému dňu podľa cash flow a následné prognózy začína z tohto stavu.
Nové vklady do sporenia posiela do dostupných pásiem podľa najvýhodnejšej čistej
úrokovej sadzby. Investície sa v odhadoch počítajú bez výnosu. Predvolene ide
`100 %` prebytku do sporenia a `0 %` do investícií.

Projekt používa:

- .NET 10 a Blazor Server
- ASP.NET Core Identity na registráciu a prihlásenie
- SQLite databázu
- xUnit testy finančných výpočtov

## Požiadavky

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Rider, Visual Studio alebo terminál

Overenie nainštalovaného .NET:

```powershell
dotnet --version
```

Výsledok musí začínať číslom `10`.

## Spustenie cez terminál

Otvor PowerShell v koreňovom priečinku projektu:

```powershell
cd C:\Personal\MortgageApp
```

Obnov závislosti a spusti aplikáciu:

```powershell
dotnet restore
dotnet run --project MortgageApp
```

Aplikácia sa otvorí automaticky alebo ju otvor manuálne:

- HTTP: http://localhost:5116
- HTTPS: https://localhost:7261

Proces zastavíš v termináli klávesovou skratkou `Ctrl+C`.

Pri ďalších spusteniach zvyčajne stačí:

```powershell
dotnet run --project MortgageApp
```

## Spustenie cez Rider

1. Otvor súbor `MortgageApp.sln`.
2. Ako spúšťací projekt vyber `MortgageApp`.
3. Vyber profil `https` alebo `http`.
4. Klikni na **Run**.

## Prvé použitie

1. Otvor aplikáciu.
2. Klikni na vytvorenie nového účtu.
3. Zaregistruj sa pomocou e-mailu a hesla.
4. Pridaj príjmy, výdavky, sporiace účty a údaje hypotéky.
5. Potvrď reálne zostatky sporiacich účtov a investícií.

Pri príjmoch a výdavkoch nastavuješ sumu pri jednom pripísaní a prvý mesiac.
Periodicita v mesiacoch je voliteľná. Prázdna periodicita znamená jednorazovú
položku, mesačný plat má periodicitu `1` a kvartálny bonus periodicitu `3`.
Ručne zadané príjmy aj výdavky môžeš následne upraviť priamo v zozname cash flow.

Každý používateľ vidí iba svoje vlastné dáta.

## Databáza a záloha

SQLite databáza sa automaticky vytvorí pri prvom spustení:

```text
MortgageApp\Data\mortgage-app.db
```

Pri spustení aplikácia automaticky aktualizuje staršiu databázovú schému a zachová
existujúce finančné dáta.

Pre jednoduchú zálohu zastav aplikáciu a skopíruj tento súbor na bezpečné miesto.

Zmazaním databázového súboru odstrániš všetkých používateľov aj všetky uložené
finančné dáta. Aplikácia pri ďalšom spustení vytvorí novú prázdnu databázu.

## Testy

Spustenie všetkých testov:

```powershell
dotnet test
```

Testy overujú najmä:

- pásmové úročenie sporiacich účtov,
- optimalizované rozdelenie zvoleného percenta mesačného prebytku,
- rozdelenie prebytku medzi sporiace účty a investície bez výnosu,
- dopočet očakávaného dnešného stavu od posledných manuálne potvrdených zostatkov,
- projekciu variabilných príjmov, bonusov a výdavkov na 12 mesiacov,
- automatické započítanie hypotekárnej splátky do cash flow od dátumu prvej splátky,
- odpočítanie spoločného mesačného vkladu do sporenia od disponibilného cash flow,
- odhad celkových dostupných peňazí k zvolenému dátumu vrátane sporenia a úrokov,
- projekciu sporenia,
- výpočet anuitnej hypotekárnej splátky,
- podporu viacerých sporiacich účtov používateľa.

## Zostavenie projektu

```powershell
dotnet build
```

Release zostavenie:

```powershell
dotnet build --configuration Release
```

## Dôležité poznámky

- Mena v nastaveniach mení označenie súm, ale nevykonáva kurzový prepočet.
- Úrokové pásma a sadzby sporiacich účtov zadáva používateľ manuálne.
- Bonusová sadzba sa pripočítava k základnej sadzbe.
- Hypotekárna kalkulačka používa anuitnú splátku bez poplatkov a poistenia.
- Uložená hypotéka sa automaticky započítava ako mesačný výdavok od dátumu prvej splátky.
- Predvolene sa do sporenia presunie `100 %` kladného mesačného prebytku a do investícií `0 %`.
- Súčet percenta sporenia a investovania je vždy `100 %`; zmena jedného automaticky dopočíta druhé.
- Pri zápornom mesačnom cash flow sa do sporenia ani investícií neposiela nič.
- Presun do sporenia alebo investícií znižuje disponibilné peniaze, ale nie celkový majetok.
- Odhad k dátumu zobrazuje sporiace účty, aktuálny zostatok investícií, budúce investičné vklady, hotovosť mimo sporenia, úroky a ich súčet.
- Manuálne potvrdený zostatok je snapshot k času potvrdenia. Od nasledujúceho mesiaca aplikácia pri načítaní dopočíta plánované vklady; pri sporení dopočíta aj úroky.
- Budúce prognózy začínajú z očakávaného aktuálneho stavu, nie zo starého manuálneho snapshotu.
- Koláčový graf v odhade porovnáva zostatok sporiacich účtov s investovanou sumou.
- Projekcia počíta mesačný úrok zo zostatku na začiatku mesiaca a plánovaný vklad pridá na konci mesiaca.
