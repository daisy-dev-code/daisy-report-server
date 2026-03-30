.PHONY: up down logs migrate build-dotnet run-dotnet dev-frontend test clean reset-db status shell-mysql shell-redis

up:
	docker compose up -d

down:
	docker compose down

logs:
	docker compose logs -f

migrate:
	dotnet run --project backend-dotnet/DaisyReport.Api migrate

build-dotnet:
	dotnet build backend-dotnet/DaisyReport.Api/DaisyReport.Api.csproj -c Release

run-dotnet:
	dotnet run --project backend-dotnet/DaisyReport.Api

dev-frontend:
	cd frontend && npm run dev

test:
	dotnet test

clean:
	docker compose down -v

reset-db: clean up migrate

status:
	docker compose ps

shell-mysql:
	docker compose exec mysql mysql -uroot -pDaisyReport2026! daisy_report

shell-redis:
	docker compose exec redis redis-cli
