# FutsalPro

FutsalPro is a multi-tenant futsal operations and booking system for Nepal. The product brief and implementation rules are in [AGENTS.md](AGENTS.md).

## Local development

Requirements: .NET 10 SDK and PostgreSQL 17 running locally. The API uses the `ConnectionStrings:FutsalPro` key. Keep the full connection string, including its password, in User Secrets; the checked-in development config contains only the local host/database/user defaults.

The connection string value format is `Host=localhost;Port=5432;Database=futsalpro;Username=postgres;Password=<your PostgreSQL password>`.

From the repository root, define a helper that prompts without echoing sensitive values, then configure the PostgreSQL connection string and development secrets:

```powershell
function Set-FutsalSecret {
    param([string]$Name, [string]$Prompt)
    $secureValue = Read-Host $Prompt -AsSecureString
    $valuePointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureValue)
    try {
        $plainValue = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($valuePointer)
        dotnet user-secrets set $Name $plainValue --project src/Futsal.Api/Futsal.Api.csproj
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($valuePointer)
        Remove-Variable plainValue, secureValue, valuePointer -ErrorAction SilentlyContinue
    }
}

Set-FutsalSecret "ConnectionStrings:FutsalPro" "Full PostgreSQL connection string for futsalpro"

$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
$keyBytes = New-Object byte[] 48
$rng.GetBytes($keyBytes)
$jwtKey = [Convert]::ToBase64String($keyBytes)
dotnet user-secrets set "Jwt:SigningKey" $jwtKey --project src/Futsal.Api/Futsal.Api.csproj
$rng.Dispose()
Remove-Variable jwtKey, keyBytes, rng

Set-FutsalSecret "Seed:SuperAdminPassword" "Development SuperAdmin password"
Set-FutsalSecret "Seed:OwnerPassword" "Sample Owner password"
```

The default development seed emails are `superadmin@futsalpro.local` and `owner@sample-futsal.local`. Seed emails can be overridden with `Seed:SuperAdminEmail` and `Seed:OwnerEmail` User Secrets. Seed passwords are required for the development seeder to create the SuperAdmin and sample organization Owner.

Apply the PostgreSQL migrations (the API also applies pending migrations at startup):

```powershell
dotnet-ef database update --project src/Futsal.Infrastructure/Futsal.Infrastructure.csproj --startup-project src/Futsal.Api/Futsal.Api.csproj
```

Build, test, and start the API from the repository root:

```powershell
dotnet build FutsalPro.slnx
dotnet test FutsalPro.slnx
dotnet run --project src/Futsal.Api/Futsal.Api.csproj
```

Liveness is available at `http://localhost:8080/health/live`; database readiness is at `http://localhost:8080/health/ready`.

## Projects

- `Futsal.Domain`: domain model and rules
- `Futsal.Application`: use cases and application ports
- `Futsal.Infrastructure`: EF Core/PostgreSQL and external adapters
- `Futsal.Api`: HTTP API, authentication, middleware, and composition root
- `Futsal.Contracts`: API contracts shared with clients
- `Futsal.Web`: Blazor client
- `Futsal.Tests`: xUnit milestone tests
