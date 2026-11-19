# E-book Reader – AI-Assisted Software Development (Házi Feladat)

Client-server web-based **E-book Reader** application (`.NET 10`, [`EbookReader.slnx`](EbookReader.slnx)) built with an orchestrated multi-agent architecture using **Google Antigravity** and three specialized subagents (`backend-agent`, `frontend-agent`, `tests-agent` running on **Gemini 4 Hydrogen / `flash`**):

- **[`src/EbookReader.Backend/`](src/EbookReader.Backend/)**: C# ASP.NET Core Minimal Web API, PBKDF2-SHA256 authentication & session tokens, per-user ownership authorization (`401`/`403`), PDF (`UglyToad.PdfPig`) and EPUB (`VersOne.Epub`) structural validation, metadata & Table of Contents extraction, Last-Write-Wins (LWW) reading position & zoom persistence, bookmarks, `EbookErrorCodes`, and EF Core SQLite storage with on-demand lazy content loading. Subagent: [`.agents/agents/backend-agent.md`](.agents/agents/backend-agent.md)
- **[`src/EbookReader.Frontend/`](src/EbookReader.Frontend/)**: Blazor Interactive Server web UI, `PDF.js` canvas + selectable text layer renderer, CSS multi-column screen-locked EPUB paginator, persistent browser session & dark mode, library search/format filters, multi-file upload, and single-command startup host. Subagent: [`.agents/agents/frontend-agent.md`](.agents/agents/frontend-agent.md)
- **[`tests/EbookReader.Tests/`](tests/EbookReader.Tests/)**: Isolated MSTest unit, integration, and regression test suite (20 tests across 5 test classes) written strictly against the specification, public interfaces, DTOs, and `EbookErrorCodes`. Subagent: [`.agents/agents/tests-agent.md`](.agents/agents/tests-agent.md)
- **Orchestration Rules**: [`AGENTS.md`](AGENTS.md)
- **Specification**: [`SPECIFICATION.md`](SPECIFICATION.md) ([`vibecodehfspec.pdf`](vibecodehfspec.pdf))

---

## 1. Startup & Test Execution Guide

### 1.1. Start the Application (Single Command)
From the repository root, start the entire system (co-hosting Backend API + Blazor Frontend):
```bash
./run.sh
```
or directly via the .NET CLI:
```bash
XDG_RUNTIME_DIR=/tmp dotnet run --project src/EbookReader.Frontend/EbookReader.Frontend.csproj
```
Once started, open in your browser:
- **Web UI**: `http://localhost:5060` (or `http://localhost:5050`)
- **Backend Health Check**: `http://localhost:5050/api/health`

### 1.2. Run All Automated Tests
```bash
XDG_RUNTIME_DIR=/tmp dotnet test EbookReader.slnx --logger "console;verbosity=normal"
```

---

## 2. Acceptance Criteria & Verification Mapping

| # | Category | Acceptance Criterion Summary | Automated Tests ([`tests/EbookReader.Tests/`](tests/EbookReader.Tests/)) | Manual UI Verification Steps |
|---|---|---|---|---|
| **AC1** | Normal | Visitor registers with unique credentials, logs in, is redirected to `/` and sees their book list (unauthenticated visitors on `/` see the public welcome screen). | `AC1_Normal_RegisterLoginAndListUserBooks`<br>`EndToEnd_FrontendClientAndBackendWorkflow` | Visit `/` as visitor -> Welcome card shown -> Click **Register** (`/register`) -> Create account -> Redirected to `/` (`My Library`). |
| **AC2** | Normal | Authenticated user uploads valid `.pdf` or `.epub` files; books appear in their personal library with parsed metadata or filename/custom title fallback. | `AC2_Normal_UploadValidPdfAndEpubFiles`<br>`Library_MetadataExtractionAndFilenameFallback` | In the right sidebar on `/`, select one or more `.pdf`/`.epub` files -> Click **Upload** -> Book card appears in grid. |
| **AC3** | Error | Uploading unsupported formats (`.txt`), oversized files (`> 10 MB`), or corrupted PDF/EPUB structures is rejected (`400`) with an error alert. | `AC3_Error_RejectInvalidFormatOversizedAndCorruptedFiles`<br>`Validator_RejectsEmptyFileInvalidFilenameAndCorruptedHeadersOrStructure` | Attempt to upload a `.txt` or corrupted `.pdf`/`.epub` file -> Server rejects (`400`), red error alert is shown, library is unchanged. |
| **AC4** | Normal | Creating a named bookmark while reading saves the bookmark (`201`), and selecting it from the sidebar jumps directly to that saved page. | `AC4_Normal_CreateBookmarkAndJumpToSavedContent`<br>`Bookmarks_ValidationCaseInsensitiveDuplicatesAndDeletion` | Open a book (`/reader/{id}`) -> Turn to page `3` -> Enter name & click **Create Bookmark** -> Turn to page `1` -> Click the saved bookmark in the right sidebar -> Jumps to page `3`. |
| **AC5** | Normal | Opening a book loads page `1`; turning pages updates the UI and saves progress on the server; closing on page `x` and reopening later automatically resumes at page `x`. | `AC5_Normal_ReadingProgressionPersistsAcrossReopen`<br>`Persistence_DataSurvivesStoreReload` | Open a book -> Navigate to page `3` -> Click **← Back to Library** -> Reopen the book -> Reader automatically loads page `3`. |
| **AC6** | Error | Invalid login credentials (`401`) or manipulated requests to read/modify/delete another user's book (`403`) are rejected. | `AC6_Error_InvalidLoginAndCrossUserUnauthorizedAccessRejected`<br>`Auth_ValidationErrorsAndLogout_HandledCorrectly` | Enter wrong password on `/login` -> `401` error alert; navigate to `/reader/{otherUsersBookId}` -> `403` error alert. |
| **AC7** | Boundary | Navigating to an out-of-range page (`< 1` or `> TotalPages`) clamps to the first (`1`) or last (`TotalPages`) page without error (`200`, `WasClamped = true`). | `AC7_Boundary_NavigateToOutOfRangePageClampsToFirstOrLastPageWithoutError`<br>`Reader_ZoomLevelPersistsAndClampsAndProgressPageClamps` | In `/reader/{id}`, type `-5` or `999` into **Jump** and click **Go** -> Reader displays page `1` or `TotalPages` with no error alert and snaps input to the valid page. |
| **AC8** | Boundary | Uploading a duplicate book (same filename or SHA-256 content hash) or duplicate bookmark (same page or name) is rejected (`409`) with an error while the system continues working. | `AC8_Boundary_DuplicateBookAndDuplicateBookmarkRejectedAndSystemContinuesWorking` | Upload the same file twice or create two bookmarks on the same page -> `409` error alert shown; subsequent valid actions succeed normally. |

---

## 3. AI Usage & Development Phases

AI tools (**Google Antigravity** orchestrator + **Gemini 4 Hydrogen / `flash`** domain subagents) were used across four distinct phases of development:

1. **Requirements Analysis & Multi-Agent Architecture Design**:
   - Analyzed [`vibecodehfspec.pdf`](vibecodehfspec.pdf) to resolve specification ambiguities (supported formats `.pdf`/`.epub`, session token authorization, Last-Write-Wins timestamp resolution, structural file validation) and defined [`SPECIFICATION.md`](SPECIFICATION.md), [`AGENTS.md`](AGENTS.md), and three isolated subagents in [`.agents/agents/`](.agents/agents/).
2. **Iterative Implementation (Backend, Storage, Rendering & UI)**:
   - Built the Minimal API backend, upgraded storage from flat JSON to **EF Core + SQLite**, replaced basic regex parsing with **`UglyToad.PdfPig`** (PDF) and **`VersOne.Epub`** (EPUB), integrated **`PDF.js`** (2x HiDPI `<canvas>` + selectable `.textLayer`) and CSS multi-column screen-locked EPUB pagination, added Table of Contents extraction, library search/format filters, multi-file upload, persistent sessions/dark mode, and a purple-accented UI theme.
3. **Codebase Refactoring & Performance Optimization**:
   - Refactored all projects into clean single-responsibility files (`<= 250` lines per file) with zero test-only code in `src/` and comprehensive English XML doc + inline comments.
   - Eliminated startup/login latency for larger libraries (30+ books) by implementing differential SQLite synchronization ([`DataStoreSyncHelper.cs`](src/EbookReader.Backend/Services/DataStoreSyncHelper.cs)) and **on-demand (lazy) book content loading** ([`JsonDataStore.cs`](src/EbookReader.Backend/Services/JsonDataStore.cs), [`Book.cs`](src/EbookReader.Backend/Models/Entities/Book.cs)).
4. **Isolated Automated Testing & Bug Discovery**:
   - `tests-agent` operates in strict isolation ([`.agents/agents/tests-agent.md`](.agents/agents/tests-agent.md)) with access only to `tests/EbookReader.Tests/`, the specification, public service interfaces, DTOs, and `EbookErrorCodes`, expanding the suite from 8 initial acceptance tests to 20 unit, integration, and regression tests.

### Methodology Comparison & Limitations
- **Single-Agent vs. Multi-Subagent Orchestration**: Having a single agent edit Backend, Frontend, and Tests in one context quickly consumes the context window and risks coupling tests to private implementation details. Splitting the repository into `backend-agent`, `frontend-agent`, and an isolated `tests-agent` kept each subagent under 45% context capacity and ensured black-box interface testing.
- **Observed Limitations**:
  - Subagents can introduce subtle DOM/state conflicts across boundaries (e.g., Blazor's Virtual DOM colliding with JS DOM mutations inside `#ebook-render-target`, or CSS selector specificity `.auth-card a` overriding `.btn-primary` text color).
  - Heuristic backfills (such as checking raw HTML page length `p.Length > 5000` when pages contain embedded Base64 images) caused false-positive repagination loops on 30-book libraries until profiled and replaced with on-demand lazy loading.

---

## 4. Documented AI Decision Cases

### Decision Case 1: Enforcing Strictly Paged EPUB Reading (Rejecting Vertical Scrolling on Zoom)
- **Problem**: The initial AI implementation paginated EPUB chapters by character count on the backend (`~2,600` characters per page), but rendered the active page inside a standard HTML container (`overflow-y: auto`). When testing the reader at larger font sizes (`150%`–`300%`), the text overflowed the screen height and turned the e-book reader into a vertical scrolling webpage.
- **Tool, Context & Instruction**: Google Antigravity (`frontend-agent` on Gemini 4 Hydrogen / `flash`), provided with [`Reader.razor`](src/EbookReader.Frontend/Components/Pages/Reader.razor), [`ReaderControlsSidebar.razor`](src/EbookReader.Frontend/Components/Reader/ReaderControlsSidebar.razor), [`ebook-renderer.js`](src/EbookReader.Frontend/wwwroot/ebook-renderer.js), and [`app.css`](src/EbookReader.Frontend/wwwroot/app.css), with the instruction that EPUBs must remain strictly paged at all font sizes without ever falling back to vertical scrolling.
- **AI Suggestion**: Keep backend chapter pagination intact for persistent page numbers/bookmarks, and wrap the client-side EPUB viewport in a fixed-height (`overflow: hidden`) CSS multi-column track (`.epub-column-track` with `column-width` equal to the page sheet width). When increased font size causes a page to span `N` columns horizontally, navigate across `Screen 1 / N ... Screen N / N` via `transform: translateX(...)` before advancing to the next backend page.
- **My Decision & Rationale**: **Modified and accepted after rejecting the initial scrollable layout**.
  - A real e-book reader must never degrade into a vertical scroll container when font size changes, yet re-paginating the entire book on the backend on every zoom click would invalidate saved page numbers and bookmark positions.
  - Using client-side CSS multi-column sub-pages (`Screen m / n`) inside a fixed-viewport sheet solved both requirements: bookmarks and backend progress remain anchored to stable page numbers while the user always flips through discrete, non-scrolling screens at any font size (`50%`–`300%`).
- **Verification Evidence**:
  - Code: [`ebook-renderer.js`](src/EbookReader.Frontend/wwwroot/ebook-renderer.js) (`recalculateEpubColumns`, `stepEpubSubPage`), [`Reader.razor`](src/EbookReader.Frontend/Components/Pages/Reader.razor#L261-L320), [`ReaderControlsSidebar.razor`](src/EbookReader.Frontend/Components/Reader/ReaderControlsSidebar.razor#L27-L39).
  - Automated Tests: `Reader_ZoomLevelPersistsAndClampsAndProgressPageClamps`, `AC5_Normal_ReadingProgressionPersistsAcrossReopen`.

### Decision Case 2: Strict Production/Test Separation & Black-Box Interface Auditing
- **Problem**: Early AI-generated code mixed multiple classes into large files and included a fallback ZIP parser (`EpubZipFallbackParser`) in `src/EbookReader.Backend` that existed solely so synthetic test byte arrays could pass validation without being real EPUB archives.
- **Tool, Context & Instruction**: Google Antigravity orchestrating `backend-agent`, `frontend-agent`, and the isolated `tests-agent` ([`.agents/agents/tests-agent.md`](.agents/agents/tests-agent.md)), instructed to modularize all multi-class files into single-responsibility files (`<= 250` lines), remove all test-only code from `src/`, sync the test agent's interface definitions, and audit test coverage against the specification.
- **AI Suggestion**:
  - Split the backend and frontend into single-responsibility files, delete `EpubZipFallbackParser.cs` from the backend, and update [`TestEbookFactory.cs`](tests/EbookReader.Tests/Helpers/TestEbookFactory.cs) in the test project to generate valid EPUB 2/3 archives (`META-INF/container.xml`, `content.opf`, and XHTML chapters) and valid PDF documents.
  - Sync [`.agents/agents/tests-agent.md`](.agents/agents/tests-agent.md) with the exact public interfaces (`IDataStore`, `IEbookValidatorAndParser`, `IAuthService`, `IBookService`, `IUserSessionService`, `IEbookApiClient`) and expand the test suite from 8 to 20 tests.
- **My Decision & Rationale**: **Enforced strict boundary separation**.
  - Production code should never contain backdoors or fallback parsers just to make unit tests easier to write; instead, the test suite must construct standards-compliant inputs.
  - Giving `tests-agent` only the specification and public interface contracts (with zero read access to `src/`) prevented implementation-coupled tests and immediately exposed a real Last-Write-Wins concurrency bug in [`BookService.UpdateReadingProgressAsync`](src/EbookReader.Backend/Services/BookService.cs#L137-L147) where stale out-of-order requests could overwrite `ZoomLevel`.
- **Verification Evidence**:
  - Code: [`BookService.cs`](src/EbookReader.Backend/Services/BookService.cs#L137-L147), [`TestEbookFactory.cs`](tests/EbookReader.Tests/Helpers/TestEbookFactory.cs), [`.agents/agents/tests-agent.md`](.agents/agents/tests-agent.md).
  - Automated Tests: 20/20 tests across 5 test classes in [`tests/EbookReader.Tests/`](tests/EbookReader.Tests/), including [`Concurrency_LastWriteWins_PreservesFlagsTimestampAndZoomLevel`](tests/EbookReader.Tests/PersistenceAndConcurrencyTests.cs#L44-L75) (see **Section 5**).

### Decision Case 3: Rejecting Eager In-Memory Preloading in Favor of On-Demand (Lazy) Book Loading
- **Problem**: When testing the application with a larger personal library of ~30 books, logging in for the first time after server startup was noticeably slow.
- **Tool, Context & Instruction**: Google Antigravity (`backend-agent` on Gemini 4 Hydrogen / `flash`) with [`JsonDataStore.cs`](src/EbookReader.Backend/Services/JsonDataStore.cs), [`DataStoreSyncHelper.cs`](src/EbookReader.Backend/Services/DataStoreSyncHelper.cs), [`DataStoreBackfillHelper.cs`](src/EbookReader.Backend/Services/DataStoreBackfillHelper.cs), [`Book.cs`](src/EbookReader.Backend/Models/Entities/Book.cs), and [`BookMapper.cs`](src/EbookReader.Backend/Models/Dtos/BookMapper.cs), instructed to diagnose the 30-book login bottleneck and load book contents into memory only when a book is actually opened in the library.
- **AI Suggestion**:
  - *Initial AI Suggestion*: Fix the false-positive repagination check in `DataStoreBackfillHelper`, add differential table sync in `DataStoreSyncHelper`, and pre-warm the full database snapshot in a background thread (`Task.Run`) when the server starts.
  - *Revised AI Suggestion (after rejecting eager preloading)*: Remove the background full-snapshot preloader, add a persisted `TotalPages` column to `Books`, query only lightweight scalar metadata columns during `EnsureLoadedAsync()` (excluding `FileBytes`, `Pages`, and `TableOfContents` from SQLite queries on startup/login/library view), and attach an on-demand content loader (`Book.EnsureContentLoaded()`) triggered only when a specific book is opened in the reader.
- **My Decision & Rationale**: **Rejected eager background preloading of full books and directed the switch to on-demand lazy loading**.
  - Pre-warming all 30 books in a background thread on startup still wasted tens or hundreds of megabytes of RAM and disk I/O reading every book's binary `FileBytes` BLOB and `Pages` JSON array before any book was even opened.
  - By projecting only lightweight metadata for library cards and loading `FileBytes`, `Pages`, and `TableOfContents` lazily per book via `EnsureContentLoaded()`, login and library browsing became instantaneous (`< 2 ms`) and memory-efficient regardless of library size, while keeping `IDataStore` and `IBookService` unchanged.
- **Verification Evidence**:
  - Code: [`JsonDataStore.cs`](src/EbookReader.Backend/Services/JsonDataStore.cs#L71-L194), [`Book.cs`](src/EbookReader.Backend/Models/Entities/Book.cs#L58-L73), [`BookMapper.cs`](src/EbookReader.Backend/Models/Dtos/BookMapper.cs#L15-L42), [`DataStoreSyncHelper.cs`](src/EbookReader.Backend/Services/DataStoreSyncHelper.cs).
  - Automated Tests: `Persistence_DataSurvivesStoreReload`, `Library_GetBookFileAndDeleteBookLifecycle`, `EndToEnd_FrontendClientAndBackendWorkflow`.

---

## 5. Fault Discovery & Regression Proof (Failing vs. Fixed Test Run)

In accordance with the testing requirements (*"Legalább egy tesztnél őrizd meg a hibás és a javított változatot, valamint a két futás eredményét"*), below is the real concurrency defect discovered by `tests-agent` in [`BookService.UpdateReadingProgressAsync`](src/EbookReader.Backend/Services/BookService.cs#L118-L152) and verified by [`Concurrency_LastWriteWins_PreservesFlagsTimestampAndZoomLevel`](tests/EbookReader.Tests/PersistenceAndConcurrencyTests.cs#L44-L75):

### 5.1. What the Bug Looked Like from a User's Perspective
1. **Multi-Device / Multi-Tab Reading**: A user has the same book open on a laptop (**Tab A**, Page 2, **80% zoom** at `14:00`) and a tablet (**Tab B**, turns to Page 4 and sets **160% zoom** at `14:05`). If Tab A's network request is delayed and arrives at the server *after* Tab B's request, the server's Last-Write-Wins guard (`14:00 < 14:05`) correctly kept **Page 4**, **but still overwrote `ZoomLevel` back to `80%`**. On the next reload, the book opened on Page 4 with its zoom unexpectedly reset to 80%.
2. **Rapidly Clicking Zoom `+` / `−` on One Device**: Quickly clicking `+` (`100%` -> `115%` -> `130%` -> `145%` -> `160%`) fires concurrent async HTTP `PUT /api/books/{id}/progress` requests. If an earlier request (`115%`) reached the database slightly after the final request (`160%`), the late-arriving stale request overwrote the saved `160%` zoom level.

### 5.2. Faulty Version vs. Fixed Version in `BookService.cs`
- **Faulty Version** (updated `book.ZoomLevel` *outside* the `if (shouldUpdate)` Last-Write-Wins timestamp guard):
```csharp
// FAULTY VERSION:
bool shouldUpdate = timestamp >= book.LastPositionUpdatedAtUtc;
if (shouldUpdate)
{
    book.CurrentPage = clampedPage;
    book.LastPositionUpdatedAtUtc = timestamp;
}
if (zoomLevel.HasValue)
{
    book.ZoomLevel = Math.Clamp(zoomLevel.Value, 50, 300); // BUG: mutates zoom even when shouldUpdate is false!
}
```
- **Fixed Version** ([`src/EbookReader.Backend/Services/BookService.cs`](src/EbookReader.Backend/Services/BookService.cs#L137-L147) — `book.ZoomLevel` is only updated when `shouldUpdate` is `true`):
```csharp
// FIXED VERSION:
bool shouldUpdate = timestamp >= book.LastPositionUpdatedAtUtc;
if (shouldUpdate)
{
    book.CurrentPage = clampedPage;
    book.LastPositionUpdatedAtUtc = timestamp;
    if (zoomLevel.HasValue)
    {
        book.ZoomLevel = Math.Clamp(zoomLevel.Value, 50, 300);
    }
}
```

### 5.3. Test Execution Result: Faulty (FAIL) vs. Fixed (PASS)
```text
# 1. Faulty Version (FAIL):
$ XDG_RUNTIME_DIR=/tmp dotnet test EbookReader.slnx --filter Concurrency_LastWriteWins_PreservesFlagsTimestampAndZoomLevel
  Failed Concurrency_LastWriteWins_PreservesFlagsTimestampAndZoomLevel [18 ms]
  Error Message:
   Assert.AreEqual failed. Expected:<160>. Actual:<80>.
Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1

# 2. Fixed Version (PASS):
$ XDG_RUNTIME_DIR=/tmp dotnet test EbookReader.slnx --filter Concurrency_LastWriteWins_PreservesFlagsTimestampAndZoomLevel
  Passed Concurrency_LastWriteWins_PreservesFlagsTimestampAndZoomLevel [3 ms]
Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1 (Full suite: 20/20 Passed)
```

---

## 6. Chronological Summary of Development & Modifications

Below is a concise summary of the iterative feature additions, refinements, and architectural improvements made from the initial version to the final application:

1. **Repository & Solution Restructure**:
   - Reorganized the project into standard `/src` (`EbookReader.Backend`, `EbookReader.Frontend`) and `/tests` (`EbookReader.Tests`) directories under [`EbookReader.slnx`](EbookReader.slnx) and added [`run.sh`](run.sh) so the entire application starts with a single command.
2. **Subagent Orchestration & Persistent Context**:
   - Configured [`AGENTS.md`](AGENTS.md) and [`.agents/agents/`](.agents/agents/) (`backend-agent`, `frontend-agent`, `tests-agent`) with direct disk write permissions scoped to each subagent's own project folder and persistent context reuse across tasks.
3. **Database Upgrade (EF Core + SQLite) & Real PDF/EPUB Engines**:
   - Upgraded persistence from a flat JSON file to **EF Core + SQLite** (`App_Data/ebook_store.db`).
   - Replaced basic text/regex parsing with **`UglyToad.PdfPig`** for PDFs and **`VersOne.Epub`** for EPUBs, adding `GET /api/books/{id}/file` to stream raw binary files to the client reader.
4. **High-Fidelity Reader Rendering (`PDF.js` & CSS Multi-Column EPUB Pagination)**:
   - Integrated Mozilla **`PDF.js`** in [`ebook-renderer.js`](src/EbookReader.Frontend/wwwroot/ebook-renderer.js) to render PDF pages onto a 2x HiDPI `<canvas>` with an aligned, selectable `.textLayer` overlay.
   - Built a screen-locked CSS multi-column EPUB paginator (`.epub-column-track`) so EPUB pages never scroll vertically—even when font size is zoomed up to `300%`, overflowing content splits into horizontal screen sub-pages (`Screen m / n`).
5. **Metadata Extraction, Table of Contents & UI Enhancements**:
   - Extracted document Title, Author, and hierarchical Table of Contents (`TocItemDto`) from PDF outlines and EPUB navigation (`nav.xhtml`/`toc.ncx` and HTML headings).
   - Added a left **Table of Contents** sidebar ([`TocSidebar.razor`](src/EbookReader.Frontend/Components/Reader/TocSidebar.razor)) and right **Reader Controls & Bookmarks** sidebar ([`ReaderControlsSidebar.razor`](src/EbookReader.Frontend/Components/Reader/ReaderControlsSidebar.razor)) with persistent per-book zoom/font-size controls (`50%`–`300%`) and an auto-hiding top navbar in reader mode.
   - Added a left **Search & Format Filter** sidebar ([`SearchSidebar.razor`](src/EbookReader.Frontend/Components/Library/SearchSidebar.razor)) and right **Multi-File Upload** sidebar ([`UploadSidebar.razor`](src/EbookReader.Frontend/Components/Library/UploadSidebar.razor)) in the library view.
   - Updated custom title behavior so uploading a single book with a custom title sets the author text to the filename instead of pulling metadata author, and disabled the custom title input when multiple files are selected.
   - Localized the entire UI to English, added persistent browser session & Dark Mode (`ProtectedLocalStorage`), applied a purple-accented color scheme (`#7c3aed` / `#8b5cf6`) with visually distinct buttons, and restored the public **Welcome Screen** for unauthenticated visitors on [`Home.razor`](src/EbookReader.Frontend/Components/Pages/Home.razor).
6. **Clean Code Refactoring, Documentation & Lazy Content Loading**:
   - Split all multi-class and long files into modular, single-responsibility files (`<= 250` lines each), removed all unused and test-only code from `src/`, and added XML doc and inline comments across every class, method, and major branch.
   - Implemented differential SQLite sync ([`DataStoreSyncHelper.cs`](src/EbookReader.Backend/Services/DataStoreSyncHelper.cs)) and on-demand lazy loading of `FileBytes`, `Pages`, and `TableOfContents` ([`JsonDataStore.cs`](src/EbookReader.Backend/Services/JsonDataStore.cs)) so logging in and browsing 30+ books in the library is instantaneous.
   - Synced [`.agents/agents/tests-agent.md`](.agents/agents/tests-agent.md) with all final interfaces and verified full specification conformance across **20/20 automated MSTest tests**.
