# Inkwell

> Self-hosted blogging, beautifully crafted.

Inkwell is a free, open-source, self-hosted blogging platform built on ASP.NET Core and .NET 8.
Multi-tenant by default, fully themeable, and designed for writers who care about typography,
content ownership, and the long-form web.

**Website:** [useinkwell.app](https://useinkwell.app)  
**Docs:** [useinkwell.app/docs](https://useinkwell.app/docs)  
**License:** MIT

---

## Features

- **Multi-tenant by default** — one binary, many blogs; each with its own domain, theme, and authors
- **Layouts & Presets** — four editorial Layouts (Magazine, Journal, Notebook, Studio) × four color Presets
- **Keyboard-first Desk editor** — Markdown with live preview, autosave, footnotes, and pull quotes
- **Server-rendered HTML** — fast cold starts, clean markup, great SEO out of the box
- **Single binary deploy** — no Node.js, no PHP; runs on Linux, Windows, or macOS
- **PostgreSQL & SQLite** — production-ready with EF Core migrations
- **Docker support** — Dockerfile and Compose recipe included
- **No telemetry, no vendor lock-in** — MIT licensed, yours to fork

## Quick start

```bash
git clone https://github.com/marutisoftwaresolutions/inkwell
cd inkwell
dotnet ef database update
dotnet run
