# ADR-0025: Custom fields are defined in Settings and stored per post and page as one JSON column

- **Status:** Accepted (2026-10-05)
- **Date:** 2026-10-05
- **Supersedes:** —
- **Related:** `.gsd/ROADMAP.md` v1.0.7/v1.0.8 stretch "Custom fields"; ADR-0020 (exporter); ADR-0022 (revisions bag precedent)

## Context

WordPress, Drupal, Joomla and Hugo let a site record its own facts on each item, such as a price, a version or an
event date. Inkwell had one JSON column per built-in block (FAQ, key facts, how-to), and nothing a site could
define for itself. The roadmap asks for typed, per-site field definitions on posts and pages, shown to readers and
emitted as `additionalProperty` in structured data.

## Options considered

1. **A field table and a value table (EAV).** Two new tables, joins on every post read, and a repository for each.
   That is a lot of machinery for a few short strings per item.
2. **A JSON column for the values, with definitions in the settings blob.** This follows the pattern the codebase
   already uses (block columns for values, `UserSettings` for site configuration).
3. **Definitions as their own table.** Nothing else in the site configuration lives in a table. It would add a
   repository for a list rarely longer than ten.

## Decision

Option 2.

- **Definitions.** `UserSettings.CustomFields` holds `CustomFieldDefinition { Key, Label, Type, AppliesTo, ShowOnPage }`.
  - Types: text, number, date, url, yesno.
  - Applies to: posts, pages or both.
  - They are edited on the Settings → Custom fields tab.
  - `CustomFields.NormalizeDefinitions` keeps an existing field's key whatever its label becomes.
  - A new field gets a unique key from its label, with accents folded. A client cannot invent a key.
  - Maximum 30 fields.
- **Values.** `Posts.CustomFieldsJson` and `Pages.CustomFieldsJson` (NVARCHAR(MAX) NULL) hold a flat `{ key: value }`
  object of invariant strings.
  - `CustomFields.Clean` validates each type: numbers invariant, dates `yyyy-MM-dd`, absolute http(s) URLs, text up
    to 500 characters, yes/no stored as `true` or `false` (three states with "not set").
  - Values for fields that are not in the list are kept untouched: a deleted definition, or the other content type.
    So removing a field never deletes data, and re-adding a field with the same label brings its values back.
  - Changing a field's type never clears values either. `CustomFields.Fits` detects a stored value that no longer
    fits, and the editor shows it as text with a prompt to correct it, instead of a typed input that would show it
    blank and drop it on the next save. Settings warns when the type of an existing field changes.
- **Editors.** `_CustomFieldsPanel` sits in the post and page editors.
  - Inputs post as `cf[key]`.
  - An invalid value re-renders the editor with the message at the field and saves nothing.
- **Readers.** `_CustomFieldDetails` is a "Details" definition list in all five post and page layouts, styled in
  `inkwell.css` with Inkwell tokens. It shows only fields marked "Show to readers" that have a value.
  - Posts also emit `additionalProperty` (`PropertyValue`, numbers as numbers) on `BlogPosting`.
  - Pages have no JSON-LD, so they show the list only.
  - The post ETag includes the definitions.
- **Revisions** carry the values: posts in the existing structured bag, pages in a new `PageBag`. Both are written
  only when set, so older revisions compare equal. Restoring a revision that predates custom fields keeps the
  current values.
- **Export.** WordPress gets each value as a native custom field (post meta) under its key. Ghost has no custom
  fields, so the fidelity summary declares the loss.
- **Schema trio:** `DBScripts/2026-10-05_add-custom-fields-to-posts-and-pages.sql` (+ `MigrationService`, `init.sql`),
  and a no-op script for the settings field.

## Consequences

- A site with no fields sees no change: no panel, no list, no structured data, no extra query.
- Values are not queryable in SQL without `JSON_VALUE`. Nothing filters or sorts by them yet. A future "filter
  posts by field" feature would need a computed column or an index.
- The API returns the stored JSON as part of a post but does not accept values. That stays out of scope until
  asked for.

## Proof

- `CustomFieldsTests`: key rules, validation per type, values kept for other fields, reader formatting, schema
  values, revisions, layout wiring, and an end-to-end render on dev of "Details" and `additionalProperty`.
- `AdminPagesSmokeTests.An_invalid_custom_field_value_comes_back_to_the_field_and_saves_nothing`: a real editor
  round trip.
- `ContentExportTests.Custom_fields_become_wordpress_custom_fields_and_ghost_declares_the_loss`.
- The Settings tab was driven in headless Edge: add, type, move, remove, focus, unsaved-changes pill, 400 px,
  dark tokens.
