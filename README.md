# E-book Olvasó – Szoftverfejlesztés MI támogatással Házi Feladat

Kliens-szerver architektúrájú webes **E-book Olvasó** alkalmazás (.NET 10, `EbookReader.slnx`), amely 3 különálló projektből és 3 dedikált Antigravity subagentből (`backend-agent`, `frontend-agent`, `tests-agent`, Gemini 4 Hydrogen / `flash` modellel) áll:

- **`src/EbookReader.Backend/`**: C# ASP.NET Core Minimal Web API (autentikáció, jogosultságkezelés, PDF/EPUB integritás-ellenőrzés és oldal-kinyerés, Last-Write-Wins pozíciómentés, könyvjelzők, `EbookErrorCodes`, JSON-fájl perzisztencia). Subagent: [`.agents/agents/backend-agent.md`](.agents/agents/backend-agent.md)
- **`src/EbookReader.Frontend/`**: Blazor Interactive Server webes felhasználói felület és egyparancsos indítási hoszt. Subagent: [`.agents/agents/frontend-agent.md`](.agents/agents/frontend-agent.md)
- **`tests/EbookReader.Tests/`**: Izolált MSTest automatizált egység-, integrációs és regressziós tesztprojekt. Subagent: [`.agents/agents/tests-agent.md`](.agents/agents/tests-agent.md)
- **Orchestrator szabályok**: [`AGENTS.md`](AGENTS.md)
- **Specifikáció**: [`SPECIFICATION.md`](SPECIFICATION.md)

---

## 1. Indítási és tesztfuttatási útmutató

### 1.1. Teljes alkalmazás indítása egyetlen paranccsal
A gyökérmappából egyetlen paranccsal elindítható a teljes rendszer (a Backend API és a Blazor Frontend együtt):
```bash
./run.sh
```
vagy közvetlenül `dotnet run` paranccsal:
```bash
XDG_RUNTIME_DIR=/tmp dotnet run --project src/EbookReader.Frontend/EbookReader.Frontend.csproj
```
Indítás után a webalkalmazás azonnal elérhető a böngészőben:
- **`http://localhost:5060`** (vagy **`http://localhost:5050`**)
- Backend REST API: **`http://localhost:5050/api/health`**

### 1.2. Automatizált tesztek futtatása
```bash
XDG_RUNTIME_DIR=/tmp dotnet test EbookReader.slnx --logger "console;verbosity=normal"
```

---

## 2. Elfogadási feltételek és ellenőrzések hozzárendelése

| # | Típus | Elfogadási feltétel rövid leírása | Automatizált teszt (`tests/EbookReader.Tests`) | Kézi ellenőrzés lépései a felületen |
|---|---|---|---|---|
| **1.** | Normál | Egyedi regisztráció, bejelentkezés, átirányítás a főoldalra, saját könyvek listázása | `AC1_Normal_RegisterLoginAndListUserBooks` + `EndToEnd_FrontendClientAndBackendWorkflow` | `/register` oldalon fiók létrehozása -> automatikus átirányítás a `/` főoldalra -> kijelentkezés és újra belépés `/login` alatt. |
| **2.** | Normál | Érvényes PDF és EPUB feltöltése és megjelenése a saját listában | `AC2_Normal_UploadValidPdfAndEpubFiles` | A főoldalon érvényes `.pdf` vagy `.epub` fájl kiválasztása és feltöltése -> megjelenik a kártya a listában. |
| **3.** | Hibás | Rossz formátumú (`.txt`), túl nagy méretű vagy sérült PDF/EPUB elutasítása hibaüzenettel | `AC3_Error_RejectInvalidFormatOversizedAndCorruptedFiles` | Nem `.pdf`/`.epub` vagy sérült fájl feltöltésekor piros hibaüzenet jelenik meg, a könyv nem kerül a listába. |
| **4.** | Normál | Könyvjelző létrehozása olvasás közben és egykattintásos visszaugrás a mentett oldalra | `AC4_Normal_CreateBookmarkAndJumpToSavedContent` | Könyv megnyitása (`/reader/{id}`) -> lapozás -> név megadása + "Könyvjelző létrehozása" -> más oldalra lapozás -> kattintás a könyvjelzőre. |
| **5.** | Normál | Olvasási pozíció mentése lapozáskor (`x.` oldal), bezárás és újranyitás után automatikus ugrás az `x.` oldalra | `AC5_Normal_ReadingProgressionPersistsAcrossReopen` | Könyvben lapozás a 3. oldalra -> "Vissza a könyvtárhoz" -> könyv újbóli megnyitása -> automatikusan a 3. oldal nyílik meg. |
| **6.** | Hibás | Hibás bejelentkezés (`401`) és másik felhasználó dokumentumaihoz való hozzáférés (`403`) elutasítása | `AC6_Error_InvalidLoginAndCrossUserUnauthorizedAccessRejected` | `/login` alatt hibás jelszó megadása -> hibaüzenet; másik felhasználó könyv-azonosítójának megnyitása `/reader/{id}` alatt -> hozzáférés megtagadva. |
| **7.** | Határeset | Nem létező oldalra (`< 1` vagy `> TotalPages`) navigáláskor az első / utolsó oldal megjelenítése hiba nélkül | `AC7_Boundary_NavigateToOutOfRangePageClampsToFirstOrLastPageWithoutError` | Olvasóban az "Ugrás oldalra" mezőbe `-5` vagy `999` beírása és "Ugrás" -> hiba nélkül az 1. vagy az utolsó oldal jelenik meg. |
| **8.** | Határeset | Duplikált könyv vagy duplikált könyvjelző elutasítása (`409`) hibaüzenettel, a rendszer tovább működik | `AC8_Boundary_DuplicateBookAndDuplicateBookmarkRejectedAndSystemContinuesWorking` | Ugyanazon könyv vagy ugyanazon oldalon lévő könyvjelző kétszeri létrehozása -> hibaüzenet, majd új művelet sikeresen végezhető. |
