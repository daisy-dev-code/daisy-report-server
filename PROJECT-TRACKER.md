# DaisyReport BI Platform — Project Tracker

**Created:** 2026-03-30
**Repo:** https://github.com/daisy-dev-code/daisy-report-server
**Stack:** .NET 10 (primary) → PHP 8.3 (later) | React 18 | MySQL 8.0 | Redis 7.0

---

## Phase 0: Foundation Infrastructure
| Task | Status | Notes |
|------|--------|-------|
| GitHub repo created | DONE | daisy-dev-code/daisy-report-server |
| Node.js installed | DONE | v24.14.1 |
| Docker Compose (MySQL + Redis) | IN PROGRESS | |
| Makefile | IN PROGRESS | |
| .NET backend foundation | IN PROGRESS | Replace template with real app |
| OpenAPI 3.0 contract | IN PROGRESS | shared/openapi/openapi.yaml |
| SQL migration V001 (92 tables) | IN PROGRESS | shared/migrations/ |
| React SPA scaffold | IN PROGRESS | Vite + TS + Tailwind + shadcn |
| Migration runner (.NET) | PENDING | |
| Seed data | PENDING | |

## Phase 1: Schema & Data Layer
| Task | Status | Notes |
|------|--------|-------|
| 92-table DDL (closure tables) | IN PROGRESS | Part of V001 migration |
| Repository pattern (.NET) | PENDING | Dapper-based |
| Seed data (root user, default OU) | PENDING | |
| Data type parity tests | PENDING | |

## Phase 2: Security & Authentication
| Task | Status | Notes |
|------|--------|-------|
| Argon2id password hashing | PENDING | Konscious library installed |
| JWT token service | PENDING | |
| ACL engine (closure table traversal) | PENDING | |
| PAM chain | PENDING | |
| Login/logout endpoints | PENDING | |
| RBAC middleware | PENDING | |
| Rate limiting (Redis sliding window) | PENDING | |
| CSRF protection | PENDING | |

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

## Phase 5: Datasource/Datasink
| Task | Status | Notes |
|------|--------|-------|
| Database datasource (connection pool) | PENDING | |
| CSV datasource | PENDING | |
| Email datasink (SMTP) | PENDING | |
| SFTP datasink | PENDING | |
| S3 datasink | PENDING | |

## Phase 6: Report Engine Orchestration
| Task | Status | Notes |
|------|--------|-------|
| 8-phase execution pipeline | PENDING | |
| Dynamic List engine | PENDING | |
| Output format negotiation | PENDING | |
| Java microservice (BIRT/Jasper) | DEFERRED | Java not installed |

## Phase 7: Scheduling & Automation
| Task | Status | Notes |
|------|--------|-------|
| Cron/interval scheduler | PENDING | |
| Job state machine | PENDING | |
| Retry with exponential backoff | PENDING | |
| Heartbeat + crash recovery | PENDING | |

## Phase 8-14: Advanced Features
| Phase | Feature | Status |
|-------|---------|--------|
| 8 | Scripting engine | DEFERRED |
| 9 | Terminal & VFS | DEFERRED |
| 10 | Dashboards & visualization | PENDING |
| 11 | TeamSpaces & collaboration | DEFERRED |
| 12 | Import/Export & transport | DEFERRED |
| 13 | I18n, search & polish | DEFERRED |
| 14 | Competitive (caching, alerting, VQB, semantic) | DEFERRED |

---

## Environment
| Tool | Version | Status |
|------|---------|--------|
| .NET | 10.0.200 | INSTALLED |
| Node.js | 24.14.1 | INSTALLED |
| Docker | 28.3.0 | INSTALLED |
| Docker Compose | 2.38.2 | INSTALLED |
| PHP | 8.2.12 (XAMPP) | AVAILABLE |
| MySQL | 8.0 (via Docker) | PENDING |
| Redis | 7.0 (via Docker) | PENDING |
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
