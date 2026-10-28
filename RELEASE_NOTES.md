# Inkwell v1.0.10

**Release date:** 2026-10-28
**Type:** Fix release (backward-compatible: no schema change; existing tenants keep their layout, theme, content and settings; filter URLs and `robots.txt` change, see Upgrade notes)

v1.0.10 fixes the Desk confirmations that v1.0.9 broke, and stops crawlers from looping through
endless filter combinations. **If you run v1.0.9, upgrade:** in that build, Publish, Save, Delete and
every other Desk confirmation do nothing.

## Highlights

### ✅ The Desk works again

- **Every Desk confirmation responds again.** In v1.0.9 the dialog script failed to load, so Publish
  and Save on posts and pages, deletes, the editor switch and the typed confirmations silently did
  nothing. A test now checks that every Desk script loads cleanly.
- **Saving can no longer be cancelled by the browser's "Leave site?" prompt** during Publish or Save.
  Leaving an editor with unsaved changes through a Desk link or the command palette now asks in the
  Desk's own dialog.
- **Publish publishes.** The Publish button and the server now judge the schedule in the site's time
  zone, so a post is no longer scheduled quietly when your computer's clock and the site disagree.
  The schedule field names the zone, and the posts list shows "Goes live" for scheduled posts.
- Desk dialogs and the command palette open in the centre of the screen again.

### 🕸️ Crawlers read your pages, not endless filter URLs

- **Category and tag chips open the archive they name** (`/category/{slug}`, `/tag/{slug}`), and the
  active chip clears the filter. Before, each listing linked dozens of `?category=…&tags=…`
  combinations that each linked dozens more, an endless set of URLs for crawlers to walk.
- **Older filter-combination URLs answer 301** with the single archive they narrow to.
- **`robots.txt` puts every allowed search and AI crawler in one group.** Each AI crawler used to have
  its own `Allow: /` group, which made it ignore every other rule. Private areas, search results and
  filter parameters are now out of reach for all of them, and every public page stays allowed, which
  a new test checks against the sitemap. Anthropic's `Claude-User` and `Claude-SearchBot` are named.

### ⚖️ Roundups that disclose the publisher's own products

- **A roundup entry can be marked `"firstParty": true`.** It then shows one short line, "Made by this
  site's publisher.", is listed without a rank number, and is left out of rating structured data. The
  pre-publish linter blocks a post that scores such a product, ranks it first or gives it a pick badge.
- Roundup `ItemList` positions now follow the order the entries appear in, so an unranked entry is
  never numbered 0.

## Upgrade notes

- **No schema change.** No new tables or columns, and no dated scripts to run. `DBScripts/init.sql`
  is unchanged and still builds the complete schema on an empty database (verified before this
  release by a `sys.columns` diff in both directions).
- **Filtering by several categories or tags at once is no longer offered.** Existing links to such
  URLs keep working: they redirect (301) to one archive.
- **`robots.txt` changes on its own** after the upgrade. If you edited crawler rules by hand somewhere
  in front of Inkwell, check they still say what you intend. Re-submitting your sitemap in Search
  Console is optional; nothing indexable is newly blocked.
- **Roundups that feature your own products:** add `"firstParty": true` to those entries. Earlier
  roundups that link the product's site are recognised without the flag, but a post that ranks such a
  product first, scores it or badges it as the pick is now held back at publish until that is removed.
- **Backward-compatible otherwise.** Layouts, presets and settings are unchanged.

See [`CHANGELOG.md`](CHANGELOG.md) for the complete list.
