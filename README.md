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

## Locations and public venue discovery

Development startup seeds editable Nepal locations (provinces, districts, cities, sample areas) and the amenity catalog. Seeded locations have `is_seeded=true`; edits through the location management API keep this marker so imported rows are identifiable. Location data is global and has no organization tenant filter.

Public endpoints:

- `GET /api/v1/public/locations?parentId=&type=` returns one level of active locations (omit `parentId` for provinces).
- `GET /api/v1/public/locations/search?q=` searches English and Nepali names.
- `GET /api/v1/public/locations/{slug}/venues` returns public venues in that location and descendants.
- `GET /api/v1/public/venues?city=kathmandu&area=baneshwor&near=27.7,85.3&radiusKm=5&surface=Artificial%20Grass&minPrice=1000&maxPrice=2500&amenities=parking,wifi&sort=distance` supports optional filters. `city` and `area` accept location slugs or UUIDs; sort accepts `distance`, `price`, or `name`.
- `POST /api/v1/admin/locations` lets SuperAdmin add location levels; an Owner can add an Area only under a City where their organization operates.
- `PUT /api/v1/admin/locations/{id}` and `DELETE /api/v1/admin/locations/{id}` are SuperAdmin-only (delete deactivates).
- `PUT /api/v1/admin/venues/{venueId}/amenities` lets an Owner replace the venue's amenity codes.

Public results include the venue name, materialized location path, distance when requested, lowest active-court price, photo placeholder, court count, surfaces, and amenities. The photo field remains a placeholder until venue image management is implemented. PostgreSQL migration `LocationsAndAmenities` creates the location, venue/court listing and amenity tables; the location and amenity catalog rows are inserted idempotently by the development seeder.

## Projects

- `Futsal.Domain`: domain model and rules
- `Futsal.Application`: use cases and application ports
- `Futsal.Infrastructure`: EF Core/PostgreSQL and external adapters
- `Futsal.Api`: HTTP API, authentication, middleware, and composition root
- `Futsal.Contracts`: API contracts shared with clients
- `Futsal.Web`: Blazor client
- `Futsal.Tests`: xUnit milestone tests


## Milestone 8 — notifications and reports

Added persistent transactional SMS queue, console/HTTP provider selection, venue templates/settings/log/usage UI, reminders/retries/caps/opt-out, and tenant-scoped daily/monthly/dues/utilization/customer/shift reports with CSV/PDF exports. Open `/admin/notifications` and `/admin/reports`.

See [setup and provider confirmation instructions](docs/notifications-reports.md) and [accounting/notification assumptions](docs/DECISIONS.md). Development defaults to the console sender. PostgreSQL acceptance tests use Docker/Testcontainers. Milestone 9 has not been started.
