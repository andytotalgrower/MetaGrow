# Test template manager

Access: **Samples → Test templates**, `/surveys/samples/test-templates`.
Both the page and every API endpoint require the MetaGrow Admin or Agriculture Manager role.
The outer API credential is insufficient without the existing MetaGrow authorization header.

Supports the website's Soil (10), Tissue (20), Quick Soil (30) and Sap (40) types.
Names beginning with `x` or `X` are excluded from listing, opening and copying; new names beginning with x are rejected.
Existing website procedures and calculations are unchanged.

## Deployment files

- `Database-totalgs/sql/ReleaseScripts/MetaGrowSoilTestTemplateManager.sql`: one self-contained, re-runnable release.
- `Database-totalgs/sql/StoredProcedures/Tgs/*SoilTemplate*ForMetaGrow.sql`: corresponding individual procedures.
- `ApiModels/MetaGrowSoilTestTemplateContracts.cs`: shared contracts and validation.
- `Metagen.Shared/Services/TgsApiService.SoilTestTemplates.cs`: shared API client.
- TgsApi.Core: template controller, store interface, database-handler partial and DI registration.
- MetaGrow: DevExpress editor, Samples navigation entry and page-access tests.

SQL files are for human review and deployment. **Do not execute them against totalgs from an agent.**
The release creates/updates only the six new procedures; it does not alter tables, legacy procedures or existing data.
Its prerequisite checks deliberately reject outdated schemas rather than recreating tables.
Use an error-stopping SQL deployment runner and deploy the reviewed procedures before the API/UI.
The API database principal needs EXECUTE permission on the five public manager procedures; the version helper is called internally through dbo ownership chaining.

## Compatibility and editing

- Updating a target preserves its ID, BalanceParams, BelowTargetIsGood and TargetRaise.
- Updating a template preserves its report filename and gypsum settings.
- Unchanged formula/parameter pairs on existing target identities are preserved even when legacy parameters fail current semantic validation. Deleting another target or changing a header/range must not force unrelated calculation changes. New rows, copied templates and changed formulas/parameters still require valid input; the API loads the trusted baseline from storage.
- Copying reads hidden settings on the server from the source template; source target IDs are retained only in the unsaved request.
- A fingerprint of the full header and targets detects changes, including website changes, before saving/copying/deleting.
- A transaction contains header and target changes; failed writes roll back together.
- Deletion checks both TgsSoilReport and PlpProductLimit under the deletion transaction.
- Rule 1 accepts `[ACTUAL]`; its target and maximum also accept `[MINORACTUAL]`. Rules 2 and 3 require numeric values, matching LabSoil.cs.
- Valid header, range, formula and boron edits auto-save after a 900 ms pause. Name, crop and type are required; an empty target list is valid.
- Target item identities are fixed in the editor, API and SQL. Adding uses a DevExpress popup, excludes used items and requires explicit minimum, target and maximum values (including deliberate zeroes).
- Removing a target uses the shared DevExpress confirmation popup and immediately saves the deletion against the last confirmed version. Unfinished edits elsewhere in the form remain in the editor.
- Existing duplicate groups are grandfathered only for their unchanged persisted row identities, allowing cleanup one row at a time. New duplicates and copies containing duplicates are rejected. A warning icon identifies the second and subsequent occurrences.
- A resizable DevExpress splitter places templates on the left (initially 34%) and details on the right (66%), with independent scrolling. The compact template grid shows Template, Crop, Refs and icon actions. Targets are sorted by item name, with all rows shown together.
- Navigation first flushes valid edits and confirms before discarding incomplete edits. An uncertain save response requires a reload to prevent accidental duplicate creation or blind retries.

## Verification

The feature's API tests use fake stores and identities and never connect to a database.
`TgsApi.Core/Tests/Test-SoilTestTemplatesSql.ps1` creates a randomly named `MetaGrowSoilTemplatesTest_<guid>` LocalDB fixture, installs the release twice, checks compatibility/rollback/deletion behaviour and removes that fixture in finally.
It has no configurable connection or database name and does not use application connection strings.
Its schema is a minimal fixture, not a claim that the live schema has been inspected.

The MetaGrow build, 22 API tests, 11 editor-session tests, five page/filter tests and isolated LocalDB integration checks passed during implementation.
The SQL fixture also checks empty headers, required target columns, fixed item identities, duplicate creation/copy rejection, incremental legacy cleanup and preservation of unrelated legacy TEC parameters during deletion.
Browser interaction checks use a disposable in-memory preview for auto-save, confirmation, adding targets, duplicate warnings, independent scrolling and both themes. Live writes against totalgs are never part of agent verification.

