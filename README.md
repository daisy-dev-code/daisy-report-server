# DaisyReport — BI Reporting Platform

A ReportServer-equivalent Business Intelligence platform built in **two technology stacks** simultaneously:

- **PHP 8.3** — Apache/MySQL (extends existing Daisy dashboard infrastructure)
- **.NET 8** — ASP.NET Core/Kestrel/MySQL (extends existing DaisyAgent infrastructure)

Both implementations share a single MySQL database, expose identical REST APIs, and present the same React SPA frontend.

## Current Status

**As of 2026-04-13, only the .NET backend and the React SPA actually exist in this repo.** The PHP backend and the Java microservice described below are **aspirational** — they are part of the long-term dual-stack design but have not been implemented yet.

| Component | Status | Notes |
|-----------|--------|-------|
| `backend-dotnet/DaisyReport.Api` | IMPLEMENTED | ASP.NET Core (net10.0), JWT auth, Dapper, Serilog, migration runner, 18 endpoint files, 70+ OpenAPI endpoints |
| `frontend/` | IMPLEMENTED | React 18 + Vite + TypeScript + Tailwind — Dashboard, Reports, Users, Datasources, Scheduler, Settings, Login |
| `backend-php/` | NOT STARTED | Dual-stack parity deferred until .NET surface area stabilizes |
| `java-report-service/` | NOT STARTED | BIRT/Jasper/Mondrian sidecar deferred (Java not installed on dev box) |
| PDF / Excel export | STUBBED | Needs PuppeteerSharp + ClosedXML |
| Scripting (Lua), TeamSpaces, VFS/Terminal, I18n polish, VQB, semantic layer | DEFERRED | See `PROJECT-TRACKER.md` |

See `PROJECT-TRACKER.md` for the authoritative phase-by-phase breakdown (20,726 LOC, 148 files, 5 commits as of last count — now extended with DaisySheet phases 1-5).

## Integration with other Daisy systems

DaisyReport is a **separate product** from `daisy-agent` (endpoint RMM) and `daisy-intranet` (employee portal). They do not share code or runtime, but they coexist in the same Daisy operational footprint.

| Aspect | DaisyReport | daisy-agent / daisy-rmm | daisy-intranet |
|--------|-------------|-------------------------|----------------|
| Database | `daisy_report` (MySQL 8.0 in Docker, port 3307) | `daisy_agent` (MariaDB on production host) | `Daisy_Approvals` + others (MariaDB) |
| Auth | JWT (Argon2id, HS256, issuer/audience both `DaisyReport`, 60-min expiry) | API-key + session + bearer | session (LDAP) |
| Shared schema? | No — deliberately isolated (see `PROJECT-TRACKER.md` → "Separate database — daisy_report (not shared with daisy_agent)") | — | — |
| Shared auth? | No | — | — |

If DaisyReport ever needs agent telemetry, the integration point will be a **data connector** (see spec 07 Datasource/Datasink + 22 Data Connector Framework) that pulls read-only from the `daisy_agent` MySQL database — it will never share ACL, users, or JWT with daisy-agent.

The `daisy-intranet` dashboard may embed DaisyReport reports via iframe or future SSO, but no such integration exists today.

### OpenAPI contract + client generation
- Single source of truth: `shared/openapi/openapi.yaml` (OpenAPI 3.0.3, ~3,310 lines, 70+ endpoints, 50+ schemas)
- Both backends (when PHP is added) are expected to conform to this contract bit-for-bit
- No generated TypeScript client is checked in yet — the React SPA hand-writes its API calls. Future work: `openapi-typescript` + `openapi-fetch` to generate types

### Migration runner semantics
- Runner lives in the .NET backend: `dotnet run --project backend-dotnet/DaisyReport.Api migrate` (also wired up as `make migrate`)
- Raw SQL files in `shared/migrations/` — both stacks are intended to execute the same SQL
- Each migration is versioned (`V001_…`, `V002_…`), checksum-tracked, idempotent, and recorded in a `schema_version` table
- Current migrations: V001 (103 tables, closure tables, stored procs, 1,771 lines) and V002 (seed data: admin user, 33 permissions, 4 groups, config)

### Auth model (what's actually implemented)
| Setting | Value | Source |
|---------|-------|--------|
| Algorithm | JWT HS256 | `Jwt:Secret` from env (`JWT_SECRET`, ≥32 chars) |
| Issuer | `DaisyReport` | `appsettings.json` → `Jwt:Issuer` |
| Audience | `DaisyReport` | `appsettings.json` → `Jwt:Audience` |
| Expiry | 60 minutes | `Jwt:ExpiryMinutes` |
| Password hash | Argon2id (mem 64 MiB, 3 iters, parallelism 4, 32-byte hash, 16-byte salt) | `Argon2` section |

JWTs are issued by the .NET backend on `/auth/login`. No refresh-token rotation is implemented yet; clients reauthenticate on 401.

## Deployment

**No production deployment exists yet.** DaisyReport is still in active development on the dev workstation. When production rolls out, it will be a **separate host or separate port from the existing daisy-rmm / daisy-intranet stack** — it does not live at `intranet.daisy.co.za:456`.

| Aspect | Current | Production plan (TBD) |
|--------|---------|------------------------|
| Host | Local dev workstation | TBD — likely dedicated VM or container host; not the Rocky Linux XAMPP host |
| MySQL | Docker container, port 3307 (mapped from container 3306), root creds in `.env` | TBD — managed MySQL or Docker on the target host |
| Redis | Docker container, port 6380 | TBD |
| API | `dotnet run --project backend-dotnet/DaisyReport.Api` (Kestrel) | TBD — likely systemd service or container; OpenAPI `servers:` block currently lists `https://reports.daisybusiness.com` as the eventual prod URL |
| Frontend | `cd frontend && npm run dev` (Vite on localhost:5173) | TBD — static build served by nginx/Apache |
| Release channel | Manual — commit to `master` and rebuild | No CI/CD pipeline yet |

**TBD items** that need decisions before first deploy:
- Production hostname / DNS (`reports.daisybusiness.com` is a placeholder in `openapi.yaml`)
- Reverse proxy (nginx vs Apache vs Caddy)
- TLS termination
- Backup strategy for `daisy_report` MySQL
- Log aggregation (Serilog currently writes to console only)

## Architecture

```
                    React SPA (TypeScript + Vite + Tailwind)
                                    |
                           OpenAPI 3.0 Contract
                          /                    \
                PHP 8.3 Backend          .NET 8 Backend
                (Apache + Slim 4)        (Kestrel + Dapper)
                          \                    /
                           Shared MySQL 8.0+
                           Shared Redis 7.0+
                                    |
                    Java Report Microservice (BIRT/Jasper/Mondrian)
```

## Project Structure

```
DaisyReportServer/
├── frontend/                    # React 18 SPA (Vite + TypeScript)
├── backend-php/                 # PHP 8.3 REST API backend
├── backend-dotnet/              # .NET 8 REST API backend
├── java-report-service/         # Java sidecar for BIRT/Jasper/Mondrian
├── shared/
│   ├── openapi/                 # OpenAPI 3.0 API contract (THE contract)
│   ├── migrations/              # Raw SQL migration files (both stacks execute same SQL)
│   ├── expression-tests/        # 112+ expression language parity tests (JSON)
│   └── fixtures/                # Shared test data
├── tests/
│   ├── parity/                  # Tests that hit BOTH backends and compare output
│   ├── php/                     # PHP-specific unit tests
│   └── dotnet/                  # .NET-specific unit tests
└── docs/engineering/            # 30 specification documents (87,467 lines)
```

## Quick Start

```bash
# Start all services
docker compose up -d

# Run shared migrations
make migrate

# Start frontend dev server
cd frontend && npm run dev

# Run parity tests
make test-parity
```

## Specification Suite

30 engineering documents totaling 87,467 lines define every subsystem:

| Category | Documents |
|----------|-----------|
| Core Platform | Schema (01), ACL (03), Security (14), Expression Language (16), I18n (17), Search/Events (18), Migration (19) |
| Reporting | Dynamic Lists (02), OLAP/Engines (13), Scheduling (04), Scripting (06) |
| Data Layer | Datasource/Datasink (07), Data Connector Framework (22), Query Caching (25) |
| UI/UX | UI Spec (12), Dashboard/Dadgets (10), Visual Query Builder (26) |
| Collaboration | TeamSpaces (09), Import/Export (11) |
| Infrastructure | Terminal/VFS (08), API Contracts (05), Testing/Deployment (15) |
| Competitive | Semantic Layer (23), Alerting (24), Competitive Analysis (21) |
| Planning | Master Build Plan (30), Issue Registry (31), Genesis (20) |

## Technology Stack

| Concern | PHP | .NET | Shared |
|---------|-----|------|--------|
| Web Framework | Slim 4 | ASP.NET Core Minimal API | OpenAPI contract |
| Data Access | PDO (parameterized) | Dapper (parameterized) | Same SQL |
| Job Queue | Laravel Queue (Redis) | Hangfire (Redis) | Same Redis |
| Cache | phpredis | StackExchange.Redis | Same Redis keys |
| PDF | Puppeteer (chrome-php) | Puppeteer (PuppeteerSharp) | Same Chrome |
| Excel | PhpSpreadsheet | ClosedXML | Parity tested |
| WebSocket | Ratchet (separate process) | Raw WebSocket middleware | Same JSON protocol |
| Auth | Argon2id + JWT | Argon2id + JWT | Same tokens |
| Frontend | — | — | React 18 + TypeScript |

## License

Proprietary — Daisy Business Solutions (Pty) Ltd
