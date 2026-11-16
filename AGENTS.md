# AGENTS.md – E-book Reader (Subagent Orchestration)

This repository consists of the `EbookReader.slnx` (.NET 10) solution and three separate projects, each managed by a dedicated **Antigravity subagent** in `.agents/agents/` running on **Gemini 4 Hydrogen (`flash`)**:

1. **`backend-agent` (`src/EbookReader.Backend`)**: [`.agents/agents/backend-agent.md`](.agents/agents/backend-agent.md)
   - C# ASP.NET Core Minimal Web API, authentication, authorization, PDF/EPUB integrity validation, Last-Write-Wins position saving, bookmarks, `EbookErrorCodes`, and JSON persistence.
2. **`frontend-agent` (`src/EbookReader.Frontend`)**: [`.agents/agents/frontend-agent.md`](.agents/agents/frontend-agent.md)
   - Blazor Interactive Server web application and single-command startup host (`http://localhost:5060` / `http://localhost:5050`).
3. **`tests-agent` (`tests/EbookReader.Tests`)**: [`.agents/agents/tests-agent.md`](.agents/agents/tests-agent.md)
   - Isolated MSTest project that knows only the specification, the interfaces under test, and the shared `EbookErrorCodes` constants.

## Subagent Orchestration Rules
1. **Direct Disk Write Access**: Each subagent has `write_file`, `edit_file`, `read_file`, and `run_command` enabled for its own project folder (`src/EbookReader.Backend/`, `src/EbookReader.Frontend/`, or `tests/EbookReader.Tests/`).
2. **Persistent Context Across Tasks**:
   - Before delegating a task, check `manage_subagents(Action: "list")`.
   - If an instance of `backend-agent`, `frontend-agent`, or `tests-agent` already exists, **reuse it via `send_message`** so it keeps its conversation context!
   - Only call `invoke_subagent` (`Model: "flash"`) when spawning the subagent for the first time or if explicitly asked to reset its context.
3. **Parallel Execution & Context Window Monitoring**:
   - Dispatch independent tasks to multiple subagents in parallel in a single turn.
   - Each subagent monitors its own `transcript_full.jsonl` size and reports its context window percentage whenever it approaches the limit (>80% of its max context window).

## Quick Commands from Root
```bash
# Start the entire application (Backend + Frontend) in one command
./run.sh
# or:
XDG_RUNTIME_DIR=/tmp dotnet run --project src/EbookReader.Frontend/EbookReader.Frontend.csproj

# Build the entire solution
XDG_RUNTIME_DIR=/tmp dotnet build EbookReader.slnx

# Run all automated tests
XDG_RUNTIME_DIR=/tmp dotnet test EbookReader.slnx --logger "console;verbosity=normal"
```
