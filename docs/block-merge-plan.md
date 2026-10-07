# Multi-crop block merging

Implemented scope, 29 September 2026. This replaces the initial proposal that included banana blocks.

## Using the feature

On `/farms`, select a farm and the **Multi-crop** tab, then choose **Merge blocks**. Administrators and agriculture managers can select a duplicate to remove and a block to keep. All the farm's multi-crop blocks are available, including inactive records and names hidden by the page filter.

The two blocks must belong to the same farm and crop type. Their names can differ completely. Review the direction, block fields and linked-record counts, then explicitly confirm removal of the duplicate. Keeping an inactive block requires a second acknowledgement. The kept block retains its ID, name, crop, plant count, area, short code, status, order, and other metadata; these values are not added together.

The preview blocks overlapping surveys, unique-key collisions, competing map/report assignments, ambiguous block types, wrong-farm references and unreviewed schema dependencies. This release does not reconcile conflicting measurements or discard duplicate child rows. If the underlying data changes after review, refresh the preview and confirm again.

Only multi-crop blocks can be merged. No banana merge action or banana parent-table writes were added. In polymorphic tables, biology mappings use `IsBananaBlock = 0`, and survey actions require multi-crop/sample survey ownership without banana visit ownership.

## Deployment: source scripts only

The implementation did not execute SQL or modify any database. The self-contained manual release script is:

`Database-totalgs/sql/ReleaseScripts/MetaGrowMultiCropBlockMerge.sql`

Matching object scripts are in:

- `sql/Tables/Tgs/TableTgsMultiCropBlockMerge.sql`
- `sql/Functions/Tgs/TgsMultiCropBlockMergeManifest.sql`
- `sql/StoredProcedures/Tgs/TgsPreviewMultiCropBlockMerge.sql`
- `sql/StoredProcedures/Tgs/TgsMergeMultiCropBlock.sql`
- `sql/StoredProcedures/Tgs/TgsGetMultiCropBlockMergeStatus.sql`
- `sql/StoredProcedures/Tgs/TgsResolveMultiCropBlockMerge.sql`
- `sql/Triggers/Tgs/TR_MultiCropMerge_*.sql` (27 dependency guards)

The release is additive and repeatable. It creates guards for reviewed tables present in the target schema; previews refuse to proceed if an existing reviewed table is missing its guard. If an optional reviewed table is added later, reapply the release. The scripts are registered as source items in the database project, consistent with its manual release workflow.

Apply the complete script manually to the intended database before deploying TgsApi.Core and MetaGrow. Rebuild their local `ApiModels` and `Metagen.Shared` dependencies with the new contracts/client. The UI cannot execute without a successful backend preview; an unavailable backend or missing database release produces an error.

## Execution and recovery

MetaGrow exposes authenticated preview, execute and status endpoints under `/block-merges`. TgsApi.Core independently verifies the forwarded MetaGrow identity and administrator/manager role. Audit identity comes from the verified user, not the request body.

SQL recomputes the preview inside the write transaction, holding the same property application lock used by property merging and update/range locks on block dependencies. The review fingerprint hashes full row images, including IDs, values and nulls, rather than counts alone. All dependency writes and the source deletion commit together; a second reference scan runs after update/delete triggers before commit.

The reviewed function is the only write manifest. Schema discovery rejects unknown operational references instead of expanding write scope. Backup tables follow the existing property-merge exclusion policy. Sample lab result snapshots and immutable history retain their original identities; operational block references and reviewed audit rows are remapped. Both original block snapshots and exact per-table row totals are retained in the merge receipt.

The receipt stores `OriginalPropertyId`, `SourceId` and `TargetId` as historical identities. Naming the historical farm field explicitly avoids introducing an apparent live property dependency that would block the existing property-merge workflow.

Every attempt has a durable request ID. An identical retry returns its receipt; changed inputs under the same ID are rejected. The browser saves an in-flight request in session storage. Reopen **Merge blocks** after a page reload to check the result, including when only the kept block remains. A missing status permits retrying the same request; an unavailable status never permits creating another request in that dialog. An interrupted SQL connection releases its session lock, allowing status to report that the transaction did not commit. Failed attempts require a new preview.

The 27 write guards reject later inserts or updates referencing a completed source block, including writes from stale survey editors or imports. A guard error rolls back that writer's transaction and asks it to reload. New records must select the kept block. Existing lab-name mappings move to the survivor, preserving alternate imported names; historical item/result names remain snapshots.

The read-only `TgsResolveMultiCropBlockMerge` procedure traces a historical ID through successive block merges and existing property merge mappings, including A → B → C. It returns the current block and the mapping path, with cycle protection.

## Verification

The implementation was built and checked without connecting to a database:

- MetaGrow web build and the API/TgsApi.Core builds performed by the focused tests.
- API authorization, invalid plans, forwarding the reviewed request/token, and unavailable status handling.
- Dialog confirmation, inactive destination acknowledgement, duplicate-submission prevention, and mismatched status receipts.
- TgsApi.Core verified-actor enforcement and execution without a pre-preview that would break idempotent retries.
- Existing property merge and farms tests.
- SQL Server 2022 ScriptDom syntax parsing of the release and all object scripts, plus release/object/guard consistency checks.

Run the offline SQL checks with `scripts/verify-block-merge-sql.ps1`. Supply `-ScriptDomPath` if the assembly is installed somewhere other than the default SSMS 22 location. This script never creates a SQL connection.

Live SQL execution, trigger behavior, locking under concurrent writers and end-to-end browser behavior remain unverified. Before production use, validate the release against a representative test database with disjoint history, shared surveys, lab mappings, stale previews, late writes, transaction interruption and repeated requests. No database test setup or data mutation was performed during implementation.
