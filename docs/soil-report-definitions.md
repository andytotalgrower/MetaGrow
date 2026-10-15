# Soil report definitions

Administration → **Soil report definitions** (`/admin/soil-report-definitions`) maintains the soil report layouts. Access is restricted to Admin and Agriculture Manager in both the page and API. Calculation targets remain under Samples → Test templates.

The MetaGrow EF migration `20261014062431_AddSoilReportDefinitions` converts 12 legacy XML definitions into 50 sections and 432 measure rows, with 54 known field bindings. It creates only MetaGrow tables:

- `SoilReportDefinitions`: stable selection key, editable name and concurrency version.
- `SoilReportDefinitionSections`: name, colour, display order and visibility on Sample survey reports.
- `SoilReportDefinitionItems`: field binding, label, optional overview short name, order, number format, zero visibility and optional background colour.
- `SoilReportDefinitionFields`: known laboratory/derived field bindings offered by the editor.
- `SoilReportDefinitionAudits`: actor, time, reason and complete before/after layouts.

The existing API startup migration process installs the tables and initial data once. Later starts do not replace maintained definitions. There is no separate MetaGrow release script, following the repository migration policy. No TotalGS schema or data changes are involved.

Migration `20261014105006_AddSoilReportShortNames` adds a nullable `ShortName` (40 characters) to the MetaGrow measure table. Administration → Soil report definitions → Edit measure exposes **Overview short name**, for example `Mg ppm`. Blank uses the full display label. These names only affect Status overview headings; detailed results and hover descriptions keep full labels. Saves require the existing reason/version and retain before/after audit records. New calculations freeze short names with their revision; old snapshots fall back to full labels. Null short names are omitted from snapshot JSON so merely adding the column does not change existing definition hashes.

Legacy XML is retained only in the API test fixtures. The production reader has no XML fallback. Legacy `TotalBgColor` is converted to its actual `#ffffff` value; omitted `ShowIfZero` means true. Reused XML item IDs are retained as `LegacyItemId`, with distinct database IDs preventing data loss. Finger Count sections are preserved but hidden for Sample surveys.

The existing crop/result-based selection rules remain in `SoilReportDefinitions.Select`; editing a definition's display name does not alter its stable selection key. All legacy variants are retained, including sugar total variants not currently selected by that legacy path. New selection behaviour should be reviewed separately from layout edits.

Maintenance popups save each confirmed change immediately. Every write requires a reason and the version originally loaded. Header, sections, measures and audit are committed in one transaction. Duplicate field bindings, unknown fields, invalid numeric formats, invalid colours and an empty visible report are rejected. A conflicting edit requires refreshing the page.

The calculator accepts a frozen database definition snapshot and includes that snapshot and its hash in the report document. The [soil report revision workflow](soil-reports.md) persists this document and displays approved revisions without rereading current definitions. Definition edits cause a new calculation only while a report is being edited; submitted and approved revisions stay frozen.

Tests compare every converted XML row and attribute, verify stable snapshot hashes and unchanged old snapshots, and exercise in-memory persistence, stale edits, validation and rollback after an audit insertion failure. XML files are not included as application resources.
