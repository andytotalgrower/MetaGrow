# MetaGrow authentication foundation

MetaGrow uses the same separation as MetaChange:

- `MetaGrow.Api` owns the new ASP.NET Core Identity database, registration, roles, MFA, JWT access tokens, rotating refresh tokens, email confirmation and password reset.
- `TgsApi.Core` remains the existing TGS business-data API. MetaGrow registration codes are validated through its existing registration endpoints.
- `ApiModels` owns transport contracts shared by MetaGrow clients and APIs.
- `MetaGrow.Web` will use a secure local application cookie and keep API access and refresh tokens server-side.

## Implemented foundation

- Roles: `Admin`, `Agriculture Manager`, and `Agronomist`; no user is seeded.
- Registration codes are validated against TGS and must name one or more valid MetaGrow roles.
- MetaGrow Identity tables, hashed rotating refresh tokens, and unique current/historical user email mappings.
- Email confirmation, password reset and additional-email confirmation through Microsoft Graph mail.
- Authenticator MFA with recovery codes and 30-day trusted-device tokens.
- Serilog rolling files under `C:\Logs\MetaGrow`.
- Initial EF Core migration applied to the MetaGrow development database.

## Implemented Web client

- Secure application cookie with JWT access and refresh tokens retained in an encrypted, SQL-backed server token cache.
- Registration, confirmation, login, password reset, authenticator enrolment, MFA and recovery-code pages.
- Thirty-day trusted-device cookie and self-service recovery-code/authenticator controls.
- Self-service current and historical email-address mapping with confirmation links.
- Authenticated-by-default application pages, with role restrictions for Administration and Farm setup.

## Next authentication validation

Create an active `MetaGrow` registration code when convenient and exercise the complete Admin registration, Graph confirmation, MFA enrolment and login journey. The code is not required for further feature development.
