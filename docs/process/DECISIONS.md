# Decision log

Newest first. Append only: a changed decision gets a new entry; old entries are never edited. Photo shoot decisions made before this log are in [docs/photoshoot/POC-DECISIONS.md](../photoshoot/POC-DECISIONS.md).

## 2026-11-13: Uses the shared agent-team plugin, version 1.2.0+202611132243
- **Decided (Luke):** this project uses the shared `agent-team` plugin (`lukehammer/agent-team-playbook`). The main Claude session is a dispatcher that hands work to child sessions and pushes only after Luke's OK.
- **Project facts:** `docs/process/PROJECT_PROFILE.md`. The plugin is switched on in `.claude/settings.json`.
- **Retired copies:** none; the project had no copied playbook or review skills.
