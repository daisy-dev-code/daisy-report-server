# DaisyReport BI Platform — Project Tracker

**Created:** 2026-03-30
**Repo:** https://github.com/daisy-dev-code/daisy-report-server
**Stack:** .NET 10 (primary) | React 18 | MySQL 8.0 | Redis 7.0
**Commits:** 2 (Phase 0 complete)

---

## Phase 0: Foundation Infrastructure — COMPLETE
| Task | Status | Notes |
|------|--------|-------|
| GitHub repo created | DONE | daisy-dev-code/daisy-report-server |
| Node.js installed | DONE | v24.14.1 |
| Docker Compose (MySQL + Redis) | DONE | MySQL:3307, Redis:6380 |
| Makefile (12 targets) | DONE | up, down, migrate, build, test, etc. |
| .NET backend foundation | DONE | 24 files: JWT, Argon2id, Dapper, Serilog |
| OpenAPI 3.0 contract | DONE | 3,310 lines, 70+ endpoints, 50+ schemas |
| SQL migration V001 (103 tables) | DONE | 1,771 lines, closure tables, stored procs |
| React SPA scaffold | DONE | Vite + TS + Tailwind, 7 pages, auth store |
| Migration runner (.NET) | DONE | Checksums, version tracking |

## Phase 1-2: Schema, Data Layer & Security — IN PROGRESS
| Task | Status | Notes |
|------|--------|-------|
| V002 seed data migration | IN PROGRESS | Admin user, permissions, config |
| Group/OrgUnit CRUD endpoints | IN PROGRESS | + closure table operations |
| Report/Folder CRUD endpoints | IN PROGRESS | + parameter management |
| Datasource/Datasink endpoints | IN PROGRESS | + test connection |
| ACL engine (closure traversal) | IN PROGRESS | Folk set, Redis cache, inheritance |
| Dashboard CRUD endpoints | IN PROGRESS | + dadget management |
| Scheduler BackgroundService | IN PROGRESS | Cron, heartbeat, state machine |
| PAM chain | PENDING | |
| Rate limiting | PENDING | |

## Phase 3: Expression Language
| Task | Status | Notes |
|------|--------|-------|
| Tokenizer | PENDING | |
| Recursive descent parser | PENDING | |
| Evaluator + built-in objects | PENDING | |
| SQL compilation mode | PENDING | |
| 112 parity test cases | PENDING | |

## Phase 4: Dynamic Lists
| Task | Status | Notes |
|------|--------|-------|
| SQL generation engine | PENDING | |
| 7 filter types | PENDING | |
| Aggregation + GROUP BY | PENDING | |
| Computed columns | PENDING | |
| Pivot transformation | PENDING | |
| Export pipeline (PDF/Excel/CSV) | PENDING | |

## Phase 5-7: Datasource Engines, Reports, Scheduling
| Task | Status | Notes |
|------|--------|-------|
| Database datasource (connection pool) | PENDING | |
| CSV datasource | PENDING | |
| 8-phase report execution pipeline | PENDING | |
| Output format negotiation | PENDING | |
| Email/SFTP/S3 datasinks | PENDING | |

## Phase 8-14: Advanced Features
| Phase | Feature | Status |
|-------|---------|--------|
| 8 | Scripting engine | DEFERRED |
| 9 | Terminal & VFS | DEFERRED |
| 10 | Dashboards & visualization | IN PROGRESS |
| 11 | TeamSpaces & collaboration | DEFERRED |
| 12 | Import/Export & transport | DEFERRED |
| 13 | I18n, search & polish | DEFERRED |
| 14 | Competitive (caching, alerting, VQB, semantic) | DEFERRED |

---

## Build Stats
| Metric | Value |
|--------|-------|
| Total commits | 2 |
| Files created | 63+ |
| Lines of code | 9,472+ |
| Tables defined | 103 |
| API endpoints | 70+ |
| React pages | 7 |
| NuGet packages | 12 |
| npm packages | 10 |
| Build agents run | 12 |

## Environment
| Tool | Version | Status |
|------|---------|--------|
| .NET | 10.0.200 | INSTALLED |
| Node.js | 24.14.1 | INSTALLED |
| Docker | 28.3.0 | INSTALLED |
| Docker Compose | 2.38.2 | INSTALLED |
| PHP | 8.2.12 (XAMPP) | AVAILABLE |
| MySQL | 8.0 (via Docker) | READY |
| Redis | 7.0 (via Docker) | READY |
| Java | — | NOT INSTALLED |

## Architecture Decisions
- **.NET first** — PHP added later for dual-stack parity
- **Closure tables** — Not nested sets (concurrent-safe, per spec amendment)
- **Argon2id** — Not bcrypt (cross-stack compatible)
- **AES-256-GCM** — Not CBC (no padding oracle vulnerability)
- **Apache ECharts** — Not jqPlot (abandoned since 2013)
- **Raw WebSocket + JSON** — Not SignalR (cross-stack compatible)
- **Separate database** — daisy_report (not shared with daisy_agent)

## Known Issues & Blockers
- Java not installed — blocks report microservice (Phase 6)
- PHP backend deferred — .NET first approach
- BIRT/Jasper/Mondrian — requires Java sidecar (deferred)
