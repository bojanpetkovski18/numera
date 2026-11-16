# M15: Import column mapping

## Goal
A user can import a sales CSV whose headers and formats do not match Numera's, by mapping the columns once and reusing that mapping later.

## Context
Read `docs/SPEC.md` sections 4 and 12 and the import pipeline in `docs/ARCHITECTURE.md`. The import (M04, M05) expects Numera's own headers and `yyyy-MM-dd` dates.

## Scope
1. `POST /api/imports/preview` (multipart): reads at most the header and the first 20 rows. Returns the source headers, the sample rows, and a suggested source column for each Numera field. Suggestions come from exact names, known synonyms (for example "Order Date", "date", "Qty", "Price") and case and spacing differences. It reports which required fields are still unmapped. The file is not stored.
2. Mapping options: source column per Numera field; date format (`yyyy-MM-dd`, `dd/MM/yyyy`, `MM/dd/yyyy`, `dd.MM.yyyy`); discount as a fraction (0.10) or a percentage (10).
3. `POST /api/imports` accepts an optional mapping. Without one it behaves exactly as in v1. With one, rows are validated with the v1 rules after mapping, and error reports name the source column and the row number in the source file.
4. Saved mappings per user: name, the mapping and the source headers. When an uploaded file's headers match a saved mapping, it is suggested first. List, rename and delete. New table and migration.
5. Import screen: after choosing a file, a mapping step with a preview table, one select per Numera field, the options above, unmapped required fields highlighted, and "Save this mapping". Then the v1 upload, progress and result flow.
6. A second sample CSV with different headers and `dd/MM/yyyy` dates, served from the web app, to demonstrate the feature.

## Out of scope
Excel files, mapping status values, transformations beyond the listed options, mappings shared between users.

## Security
Preview and mapping input are untrusted: the same size limit, content-type checks and header length limits as the import. The preview never returns more than 20 rows.

## Tests required
- Unit tests: suggestions (exact, synonym, case), each date format, percentage discounts, mappings with missing required fields.
- Integration tests: preview responses; an import with a mapping gives the same orders as the same data in Numera's format; error report positions; saved mapping CRUD and per-user isolation; v1 imports unchanged.
- Component tests: the mapping step, highlighting, saving and reusing a mapping.

## Acceptance criteria
- The second sample file imports with the suggested mapping plus the date format choice, and produces the same rows as the v1 sample (automated test).
- A file with Numera's own headers imports without showing the mapping step.
- 10 MB and 100,000 rows are still enforced with a mapping.

## Verification required
Run all tests; import both sample files in a browser; measure a 100,000-row mapped import and record it in `PROGRESS.md`.
