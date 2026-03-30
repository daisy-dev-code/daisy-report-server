# DaisyReport BI Platform — Project Tracker

**Created:** 2026-03-30
**Repo:** https://github.com/daisy-dev-code/daisy-report-server
**Stack:** .NET 10 (primary) | React 18 | MySQL 8.0 | Redis 7.0
**Commits:** 5

---

## Phase 0: Foundation Infrastructure — COMPLETE
| Task | Status | Notes |
|------|--------|-------|
| GitHub repo created | DONE | daisy-dev-code/daisy-report-server |
| Node.js installed | DONE | v24.14.1 |
| Docker Compose (MySQL + Redis) | DONE | MySQL:3307, Redis:6380 |
| Makefile (12 targets) | DONE | up, down, migrate, build, test, etc. |
| .NET backend foundation | DONE | JWT, Argon2id, Dapper, Serilog |
| OpenAPI 3.0 contract | DONE | 3,310 lines, 70+ endpoints, 50+ schemas |
| SQL migration V001 (103 tables) | DONE | 1,771 lines, closure tables, stored procs |
| React SPA scaffold | DONE | Vite + TS + Tailwind, 7 pages, auth store |
| Migration runner (.NET) | DONE | Checksums, version tracking |

## Phase 1-2: Schema, Data Layer & Security — COMPLETE
| Task | Status | Notes |
|------|--------|-------|
| V002 seed data migration | DONE | Admin user, 33 permissions, 4 groups, config |
| Group/OrgUnit CRUD endpoints | DONE | + closure table operations |
| Report/Folder CRUD endpoints | DONE | + parameter management |
| Datasource/Datasink endpoints | DONE | + test connection |
| ACL engine (closure traversal) | DONE | Folk set, Redis cache, inheritance chain |
| Dashboard CRUD endpoints | DONE | + dadget management + bookmarks |
| Scheduler BackgroundService | DONE | Cronos, heartbeat, stale detection |
| Permission endpoints | DONE | check, ACL/ACE CRUD, invalidation |

## Phase 3: Expression Language — COMPLETE
| Task | Status | Notes |
|------|--------|-------|
| Tokenizer (30 token types) | DONE | |
| Recursive descent parser | DONE | 10-level precedence |
| Evaluator + built-in objects | DONE | today (18 methods), math (15), sutils (12) |
| SQL compilation mode | DONE | AST → MySQL SQL + bind params |
| Template processor | DONE | ${...} with brace depth tracking |
| LRU AST cache | DONE | 10,000 entries, thread-safe |

## Phase 4: Dynamic Lists — COMPLETE
| Task | Status | Notes |
|------|--------|-------|
| SQL generation engine | DONE | CTE, derived table, full assembly |
| 7 filter types | DONE | Inclusion, exclusion, range, wildcard, null, regex, prefilter tree |
| Aggregation + GROUP BY | DONE | SUM/AVG/COUNT/MIN/MAX/VARIANCE, auto GROUP BY |
| Computed columns | DONE | Whitelist validation, DML/DDL blocking |
| Pivot transformation | DONE | Flat → crosstab with dynamic columns |
| Export pipeline (CSV/JSON/HTML) | DONE | RFC 4180 CSV, PDF/Excel stubbed |

## Phase 6: Report Execution Pipeline — COMPLETE
| Task | Status | Notes |
|------|--------|-------|
| 8-phase execution pipeline | DONE | Init→Auth→Params→DS→Execute→Output→Audit→Cleanup |
| Parameter resolver | DONE | Supplied→default→expression→coercion→required |
| Engine router | DONE | DYNAMIC_LIST full, SQL basic, BIRT/JASPER stub |
| Output formatter | DONE | HTML, JSON, CSV, Excel/PDF stub |
| Export endpoint | DONE | GET /api/reports/{id}/export?format=csv |

## Additional Systems — COMPLETE
| Task | Status | Notes |
|------|--------|-------|
| Full-text search engine | DONE | MATCH AGAINST + LIKE fallback, scoring |
| Audit log endpoints | DONE | Paginated with action/user/entity/date filters |
| System config CRUD | DONE | Category grouping, upsert |
| Notification system | DONE | Per-user, unread count, mark read |
| Global constants CRUD | DONE | |

## React Frontend — COMPLETE
| Page | Status | Features |
|------|--------|----------|
| Dashboard | DONE | 4 live KPI cards, system health, activity feed |
| Reports | DONE | CRUD table, create/edit modal, delete confirm |
| Users | DONE | CRUD, enable/disable toggle, password mgmt |
| Datasources | DONE | CRUD, inline connection test results |
| Scheduler | DONE | Status badges, Run Now, execution history |
| Settings | DONE | Grouped config editor, inline save, system info |
| Login | DONE | JWT auth flow |
| Components | DONE | DataTable, Modal, Badge (reusable) |

## Phase 8-14: Advanced Features
| Phase | Feature | Status |
|-------|---------|--------|
| 5 | CSV/Script datasources | PENDING |
| 7 | Email/SFTP/S3 datasinks | PENDING |
| 8 | Scripting engine (Lua) | DEFERRED |
| 9 | Terminal & VFS | DEFERRED |
| 11 | TeamSpaces & collaboration | DEFERRED |
| 12 | Import/Export & transport | DEFERRED |
| 13 | I18n, search & polish | DEFERRED |
| 14 | Competitive (caching, alerting, VQB, semantic) | DEFERRED |

---

## Build Stats
| Metric | Value |
|--------|-------|
| Total commits | 5 |
| Total files | 148 |
| Lines of code | 20,726 |
| .NET source files | 108 |
| React/TS files | 16 |
| SQL migration files | 2 |
| Tables defined | 103 |
| API endpoint files | 18 |
| OpenAPI endpoints | 70+ |
| NuGet packages | 13 |
| npm packages | 10 |
| Build agents spawned | 21 |

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
- Java not installed — blocks report microservice (Phase 6 BIRT/Jasper)
- PHP backend deferred — .NET first approach
- PDF/Excel export — needs PuppeteerSharp and ClosedXML packages
- BIRT/Jasper/Mondrian — requires Java sidecar (deferred)
