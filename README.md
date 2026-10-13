# FutsalPro

FutsalPro is a multi-tenant futsal operations and booking system for Nepal. The product brief and implementation rules are in [AGENTS.md](AGENTS.md).

## Local development with PostgreSQL

Requirements: .NET 10 SDK and PostgreSQL 17 running locally with a database named `futsalpro` and a `postgres` user. The API uses this development connection string format, without a password:

```text
Host=localhost;Port=5432;Database=futsalpro;Username=postgres
```

Set the PostgreSQL password in .NET User Secrets for the API project. From the repository root, run this in PowerShell; it prompts for the password without putting the literal value in your command history or a tracked file:

```powershell
dotnet user-secrets set "Database:Password" (Read-Host "PostgreSQL password for user postgres") --project src/Futsal.Api/Futsal.Api.csproj
```

Apply the EF Core migrations to the configured local database:

```powershell
dotnet-ef database update --project src/Futsal.Infrastructure/Futsal.Infrastructure.csproj --startup-project src/Futsal.Api/Futsal.Api.csproj
```

The API also applies pending migrations on startup. Start it from the repository root with:

```powershell
dotnet run --project src/Futsal.Api/Futsal.Api.csproj
```

Check API liveness at `http://localhost:8080/health/live` and database readiness at `http://localhost:8080/health/ready`. Readiness is healthy only when PostgreSQL accepts connections.

## Projects

- `Futsal.Domain`: domain model and rules
- `Futsal.Application`: use cases and application ports
- `Futsal.Infrastructure`: EF Core/PostgreSQL and external adapters
- `Futsal.Api`: HTTP API, health endpoints, middleware, and composition root
- `Futsal.Contracts`: API contracts shared with clients
- `Futsal.Web`: Blazor client
