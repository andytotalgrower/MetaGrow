# MetaGrow

MetaGrow is Metagen's internal application for farm information, field and laboratory survey workflows, and agronomic reporting. It is a .NET 10 solution with an interactive server-side Blazor UI built with DevExpress Blazor components.

## Solution structure

```text
MetaGrow/
  src/
    MetaGrow.Api/       Identity, access, approvals and report-share API
    MetaGrow.Web/       Interactive server-side Blazor application
    MetaGrow.Shared/    MetaGrow-specific filters and shared types
  tests/
    MetaGrow.Api.Tests/ API, authorization and persistence tests
    MetaGrow.Web.Tests/ UI workflow and shared-contract tests
  MetaGrow.slnx
```

The solution also references the sibling `ApiModels` and `Metagen.Shared` projects. Survey and report data is provided by the separately hosted `TgsApi.Core` service.

## Capabilities

### Application shell and access

- Metagen-branded, responsive DevExpress navigation with light and dark themes
- Registration-code onboarding, email confirmation, password reset, MFA, recovery codes and trusted devices
- Role-based access for Admin, Agriculture Manager, Agronomist and Accountant users
- Dashboard summaries and approval tasks appropriate to the signed-in role

See [AUTHENTICATION.md](AUTHENTICATION.md) for the authentication architecture and role matrix.

### Surveys and laboratory results

- Responsive multi-crop and banana survey finders with saved filters and mobile card views
- Multi-crop editing for survey details, block groups, recommendations and photos
- Banana survey editing, workflow transitions and reporting
- Sample survey workflow, including CSV-based soil survey generation and CSV laboratory-result import
- Unified current and historical laboratory-result search for soil, leaf, nematode and other result types
- Reviewed deletion workflow for sample surveys

### Reporting

- Multi-crop and banana reporting hubs with workflow-specific views
- Responsive, print-friendly reports with charts, summaries, narratives, recommendations and photos
- Browser print/save-to-PDF and generated report filenames
- PBD workbook downloads for multi-crop surveys
- Named, revocable report links with downloadable QR codes and an anonymous read-only shared-report route

### Farm administration

- Farm/grower search and reporting groups
- Duplicate-property detection and dependency summaries
- Reviewed property merge and deletion workflows with role-based approvals

## Architecture and security

`MetaGrow.Web` uses a secure, HTTP-only application cookie. API access and refresh tokens are retained server-side in an encrypted SQL-backed cache; TGS service credentials and database connections are never sent to the browser.

`MetaGrow.Api` owns ASP.NET Core Identity, refresh tokens, email mappings, report-share bearer records, and approval records for property merges, property deletions and sample-survey deletions. It validates JWTs and role requirements before executing protected operations.

The Blazor server calls `TgsApi.Core` directly for survey, farm, laboratory and report data. Anonymous shared-report requests are restricted to the survey identified by a valid, non-revoked share record.

Both applications write rolling Serilog files under `C:\Logs\MetaGrow`. Shared-report responses also disable caching and search indexing.

## Prerequisites

- .NET 10 SDK
- Access to the DevExpress NuGet feed used by the Web project
- SQL Server or SQL Server LocalDB for Identity, approvals and the server token cache
- Sibling checkouts of `ApiModels` and `Metagen.Shared` at the paths referenced by `MetaGrow.slnx`
- A runnable `TgsApi.Core` instance and valid TGS service settings
- Microsoft Graph mail settings for account confirmation and password-reset email

Do not commit plaintext production credentials. The applications support encrypted values under `MySettings`; the API also requires a JWT signing key of at least 64 characters.

## Run locally

From the MetaGrow repository, start the TGS API first, then the MetaGrow API and Web app in separate terminals:

```powershell
dotnet run --project ..\TgsApi.Core\TgsApi.Core.csproj --launch-profile https
dotnet run --project src\MetaGrow.Api\MetaGrow.Api.csproj --launch-profile https
dotnet run --project src\MetaGrow.Web\MetaGrow.Web.csproj
```

Development endpoints:

| Service | URL |
| --- | --- |
| MetaGrow Web | `https://localhost:7207` |
| MetaGrow API | `https://localhost:7222` |
| TGS API | `https://localhost:7095` |
| MetaGrow API OpenAPI UI | `https://localhost:7222/swagger` |

The development settings point the Web app to the local MetaGrow and TGS APIs. Outside Development, the applications use their configured API URLs and encrypted TGS settings.

The API applies its EF Core migrations and ensures the four application roles exist when it starts. The Web app creates its SQL-backed token-cache table when required.

### Database migrations

`MetaGrow.Api` is the sole owner of its Identity, approval and report-sharing database schema. Changes to that schema must be represented by a checked-in EF Core migration and are applied by the API at startup:

```powershell
cd src\MetaGrow.Api
dotnet ef migrations add DescriptiveMigrationName
dotnet ef migrations has-pending-model-changes
```

Do not create a second release script for the same MetaGrow-owned schema change. Changes to the separate TotalGS operational database—tables, stored procedures, outbox objects and survey data—belong in the `Database-totalgs` repository and must include both the individual object definition and a repeatable release script. MetaGrow EF migrations must never contain TotalGS objects.

## Tests

Run the complete suite from the repository root:

```powershell
dotnet test MetaGrow.slnx
```

The API tests cover role contracts, persistence, report shares and approval endpoints. The Web tests cover authentication contracts, responsive layouts, filters, reporting, property workflows, sample workflows and laboratory-result presentation.
