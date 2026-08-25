# MetaGrow authentication and authorization

MetaGrow separates browser authentication, application identity and TGS business-data access so that API tokens, service credentials and database connections remain on the server.

## Components

- `MetaGrow.Api` owns ASP.NET Core Identity, registration, roles, MFA, JWT access tokens, rotating refresh tokens, email confirmation, password reset and additional email addresses.
- `MetaGrow.Api` also authorizes report sharing and reviewed property/sample operations. The related bearer and approval records are stored in the MetaGrow database.
- `MetaGrow.Web` authenticates the browser with a secure local cookie and keeps MetaGrow API access and refresh tokens in an encrypted, SQL-backed server cache.
- `TgsApi.Core` remains the business-data API. It validates MetaGrow registration codes and supplies farm, survey, laboratory and report data through server-to-server calls.
- `ApiModels` owns the authentication and business transport contracts shared by the applications.

## Sign-up and sign-in flow

1. A user registers with an active TGS registration code, email address and password.
2. The API validates that the code is for MetaGrow and maps every role on it to a known MetaGrow role.
3. The API creates the Identity user and sends an email-confirmation link through Microsoft Graph.
4. After confirmation, the user signs in. MFA enrolment and verification are required when configured.
5. The Web app issues its HTTP-only application cookie and stores the returned JWT access and refresh tokens on the server.
6. The Web app refreshes access tokens through the API without exposing either token to browser code.

No application user is seeded. Registration codes control initial role assignment.

## Roles

| Role | Primary access |
| --- | --- |
| `Admin` | Full application access, Administration, reporting, farm management and approvals |
| `Agriculture Manager` | Survey/report workflows, farm management and operational approvals |
| `Agronomist` | Survey/report workflows; may submit property merge/deletion requests |
| `Accountant` | Sample and laboratory workflows, including eligible sample-deletion review tasks |

Authorization is enforced by both Razor page attributes and API endpoint role policies. Navigation visibility is only a convenience and is not the security boundary.

Current approval rules include:

- Admin and Agriculture Manager users review property merge and property deletion requests.
- Agronomists can submit property merge and property deletion requests.
- Admin, Agriculture Manager and Accountant users can review sample-survey deletion requests; Admin and Agriculture Manager users may approve their own requests where the endpoint permits it.
- Admin, Agriculture Manager and Agronomist users can create, list and revoke report shares.

## Security controls

- Identity requires confirmed accounts and unique email addresses.
- Passwords must contain at least eight characters.
- Five failed sign-in attempts trigger a 15-minute lockout.
- Access tokens are short lived; refresh tokens rotate and are stored hashed.
- The browser cookie is HTTP-only, secure, same-site `Lax`, sliding and valid for seven days.
- Authenticator MFA supports recovery codes, self-service reset/disable controls and a 30-day trusted-device token.
- Current and historical email mappings are unique, with confirmation required for additional addresses.
- Authentication and anonymous report-share resolution endpoints are rate limited.
- Data Protection keys are persisted separately for the API and Web applications under each application's `App_Data\keys` directory.
- Anonymous shared-report responses use `no-store`, `noindex` and a no-referrer policy.
- Rolling security and request logs are written under `C:\Logs\MetaGrow`.

## Configuration

The important configuration sections are:

| Setting | Purpose |
| --- | --- |
| `ConnectionStrings:DefaultConnection` or encrypted `MySettings` database values | Identity, approval records and the Web token cache |
| `Jwt` | Issuer, audience, signing key, access-token lifetime and refresh-token lifetime |
| `Registration:RequireEmailConfirmation` | Whether sign-in requires email confirmation |
| `Mfa` | MFA requirement and issuer name |
| `GraphMail` | Microsoft Graph sender and application credentials |
| `Api:BaseUrl` | MetaGrow API URL used by the Web app |
| `TgsApi:DevelopmentBaseUrl` / encrypted `MySettings:TgsApiUrl` | TGS API address |
| `Cors:WebOrigin` | Allowed Web origin for the API |
| `RateLimiting:AuthPermitPerMinute` | Per-client authentication request limit |

Use development secrets or environment-specific encrypted settings for sensitive values. `Jwt:SigningKey` must be at least 64 characters and must not use the development value in production.

## Validation checklist

Before promoting an environment, verify:

- An active MetaGrow registration code can create a user with each intended role.
- Microsoft Graph sends confirmation, password-reset and additional-email messages.
- Login, lockout, authenticator enrolment, trusted-device, recovery-code and token-refresh flows work end to end.
- Role-restricted pages and APIs reject users without the required role.
- Property and sample approval workflows enforce reviewer and self-approval rules.
- Report shares resolve only for active records and revoked links stop working.
- Data Protection keys and the token-cache database persist across application restarts.
