# MetaGrow

MetaGrow is the new Metagen-branded internal application for survey reporting and farm information. It is a .NET 10 Blazor Web App using DevExpress Blazor components.

## Solution structure

```text
MetaGrow/
  src/
    MetaGrow.Web/       Blazor UI and application shell
    MetaGrow.Shared/    MetaGrow-specific shared types
  tests/
    MetaGrow.Web.Tests/ Web and shared-contract tests
  MetaGrow.slnx
```

The TGS API remains in the separate `TgsApi.Core` repository. New API transport contracts belong in that repository's `ApiModels` project.

## Current shell

- MetaChange-style DevExpress drawer and header
- Metagen branding and light/dark themes
- Home tiles and separate placeholder dashboards for Multi-crop, Banana, Sample surveys, Farm setup and Administration
- Serilog rolling text logs under `C:\Logs\MetaGrow`
- Placeholder configuration sections for the TGS API, Microsoft Graph mail and existing Multi-crop survey image origin

Authentication, API integration and survey/report functionality are intentionally not implemented yet.

## Run locally

```powershell
dotnet run --project src\MetaGrow.Web\MetaGrow.Web.csproj
```
