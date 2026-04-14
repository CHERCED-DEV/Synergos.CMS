# AGENTS.md

This repo's canonical agent guidance lives in [`CLAUDE.md`](./CLAUDE.md).

Any LLM, agent, or skill tasked with reading, writing, or reviewing code in
this workspace must read `CLAUDE.md` **before** making changes. It defines:

- The Ten Commandments (hard rules every change must respect)
- Layer architecture and dependency rule
- Folder layout and naming conventions
- Schema pipeline phase order + extension recipes
- CDN, macros, and rendering model
- Configuration boundaries (what goes in appsettings vs SeedConfig vs Umbraco)
- Service / DI / lifetime rules
- Presentation patterns
- i18n / Dictionary policy (no hardcoded user-facing strings)
- Commit conventions
- Build and run (IIS Express vs Kestrel, fresh boot, uSync lifecycle)
- Forbidden patterns (anti-examples with reasons)

`CLAUDE.md` is auto-loaded by Claude Code. For other agents (Codex, Cursor,
cloud agents, custom LLM wrappers), either load `CLAUDE.md` into context
manually or configure your agent runtime to read it alongside user prompts.

The workspace also contains planning / audit documents at the root
(`plan-maestro-*.md`, `auditoria-*.md`, `synergos-*.md`) — these are historical
context for why decisions were made. `CLAUDE.md` supersedes anything that
conflicts with it.
