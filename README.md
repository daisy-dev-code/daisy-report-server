# DaisyReport — BI Reporting Platform

A ReportServer-equivalent Business Intelligence platform built in **two technology stacks** simultaneously:

- **PHP 8.3** — Apache/MySQL (extends existing Daisy dashboard infrastructure)
- **.NET 8** — ASP.NET Core/Kestrel/MySQL (extends existing DaisyAgent infrastructure)

Both implementations share a single MySQL database, expose identical REST APIs, and present the same React SPA frontend.

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
