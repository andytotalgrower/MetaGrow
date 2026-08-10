# MetaGrow

MetaGrow is the new Metagen-branded internal application for survey reporting and farm information. It is a .NET 10 Blazor Web App using DevExpress Blazor components.

## Solution structure

```text
MetaGrow/
  src/
    MetaGrow.Api/       Identity and application API
    MetaGrow.Web/       Blazor UI and application shell
    MetaGrow.Shared/    MetaGrow-specific shared types
  tests/
    MetaGrow.Api.Tests/ API and persistence tests
    MetaGrow.Web.Tests/ Web and shared-contract tests
  MetaGrow.slnx
```

The TGS API and transport contracts remain in the separate `TgsApi.Core` and `ApiModels` repositories.

## Currently implemented

- MetaChange-style DevExpress drawer and header
- Metagen branding and light/dark themes
- Registration-code login, email confirmation, MFA and recovery flows
- Role authorization for Admin, Agriculture Manager and Agronomist
- Multi-crop survey finder with a two-month default range, property and survey-type filters, sorting and optional audit columns
- Responsive DevExpress grid on desktop and simplified survey cards on phones
- Additive TGS API finder endpoints, consumed only through the authenticated MetaGrow API
- Serilog rolling text logs under `C:\Logs\MetaGrow`

The online and printable report links are routed but report rendering is the next phase.

## Run locally

Start the TGS API first (its HTTPS development profile uses port 7095), then the MetaGrow API and Web app:

```powershell
dotnet run --project ..\TgsApi.Core\TgsApi.Core.csproj --launch-profile https
dotnet run --project src\MetaGrow.Api\MetaGrow.Api.csproj --launch-profile https
dotnet run --project src\MetaGrow.Web\MetaGrow.Web.csproj
```

The Web app is available at `https://localhost:7207`. In Development, MetaGrow.Api uses the local TGS API; production continues to use the encrypted `MySettings:TgsApiUrl` value.
