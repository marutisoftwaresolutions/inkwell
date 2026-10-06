# ADR-0027: Every date follows the site's time zone and country format, from IANA and CLDR catalogs

- **Status:** Accepted (2026-10-05)
- **Date:** 2026-10-05
- **Supersedes:** the free-form time-zone list on Settings → Display (host zones, Windows ids on Windows)
- **Related:** owner request 2026-10-05; ADR-0025 (custom field dates); `TimeZoneHelper`

## Context

The owner asked for every date to follow the time zone and the country format in Settings, with time zones and
date formats generated as database scripts from international standards. An audit of the working tree found:

- About 90 places that display dates. **3** of them converted to the site's zone.
- **25 home layouts, 6 post cards and most Desk screens** showed raw UTC.
- **Footers and two mastheads** used the server's local clock.
- **Hard-coded patterns** mixed "October 5, 2026", "5 October 2026" and "05 Oct, 2026" across layouts.
- **No culture or country setting** existed. The time-zone list came from the host, so it held Windows ids on Windows
  and IANA ids on Linux.
- **Structured data** (`datePublished`, `article:published_time`) was written without a zone, because stored UTC
  values come back as an unspecified kind and `"O"` then omits the offset.

## Decision

1. **Reference catalogs as tables, generated from the standards.**
   - `Countries`: ISO 3166-1. Each row holds its most-used locale from CLDR `territoryInfo` and its main zone from CLDR
     `primaryZones`, else its dominant metazone. Where counting zones misleads (Canada, Russia, Micronesia), the
     capital's zone is used.
   - `TimeZones`: IANA `zone.tab`, with Windows id and standard offset.
   - `DateLocales`: every country locale the runtime's CLDR/ICU data has, on the Gregorian calendar, with its
     patterns and samples.
   - `tools/RefDataGenerator` writes `DBScripts/2026-10-05_create-and-seed-countries-timezones-date-locales.sql`. Its
     header cites IANA tzdata 2026e and CLDR 48. The script is idempotent: create if missing, then MERGE, never delete.
   - The same body is section 39 of `init.sql`; a test keeps them identical.
   - `MigrationService` runs the embedded script when a table is missing or short of the script's row counts.
   - The catalogs are excluded from backups, because every install seeds them.
2. **The tables are the catalog, not the engine.**
   - Formatting uses the runtime's CLDR data for the chosen locale.
   - Conversion uses the runtime's tz rules, including daylight saving.
   - Offsets in `TimeZones` are informative, for sorting and labels.
   - Renamed zones (Europe/Kyiv, Pacific/Kanton…) resolve through their old names on hosts with older tz data.
   - Windows ids saved earlier convert to their IANA name on the next save.
3. **Settings:**
   - `Country` (ISO alpha-2), `DateLocale` (BCP-47, default `en-US`), `DateFormat` (`locale` | `iso8601`) and
     `ClockFormat` (`locale` | `24h` | `12h`), all in the settings blob.
   - Picking a country narrows the zone and format lists and suggests the country's main zone and the site
     language's locale there.
   - A live preview shows the result.
4. **One formatter.**
   - `Blog.Core/Services/DateDisplay` is pure. Call sites name a *style*:
     - Long, Medium, Short, with weekday;
     - MonthYear, MonthDay, Day, Year;
     - DateTime, DateTimeSeconds, Time.
   - The locale supplies the pattern.
   - Long drops the weekday cleanly in every culture, including literals that only qualified it (Thai "ที่", Danish
     "den"). Medium abbreviates the month. The clock override never breaks quoted literals.
   - Non-Gregorian defaults (ar-SA, th-TH, fa-IR) keep their language on the Gregorian year.
   - ISO 8601 mode writes `2026-10-05` and `14:30` everywhere.
5. **Views use `@Dates`.** This is `SiteDates`, scoped and injected through `_ViewImports`. It reads the site's
   settings once per request.
   - **Instants** (published, created, logged) are converted to the site's zone.
   - **Calendar dates** stored as midnight (last verified, next review, Search Console's "data through", daily
     buckets) are *not* converted, so 5 October stays 5 October.
   - **Wall times** the controller already converted (the editor's schedule) are formatted only.
   - `datetime` attributes carry the local ISO date.
   - Structured data and Open Graph use `DateDisplay.IsoUtc`, with a `Z` suffix.
6. **What stays invariant:** feeds (RFC 1123), HTTP headers, sitemaps, exports, file names, query strings and
   `<input type=date>` values. The setup and restore screens, which run before any settings exist, stay labelled UTC.

## Consequences

- **What sites see:**
  - A site that changes nothing sees en-US formats in UTC: the month-first long dates most layouts already used.
  - Layouts that hard-coded day-first dates now follow the site's choice.
  - The footer year and mastheads use the site's date, not the server's.
- **Caching:** the post ETag includes the date settings, so changing them never serves a stale 304.
- **Enforcement:** a source scan (`No_view_formats_a_date_by_hand`) fails the build when a view formats a date
  itself. Only machine formats on the allow-list are exempt.
- **Cost:** about 1,200 reference rows per database (179 KB of seed), and a script to regenerate when tzdata or CLDR
  publishes.
- **Host differences:** formats can differ slightly between hosts whose ICU versions differ, because CLDR revises
  patterns. The live preview always shows what this host will print.

## Proof

- `DateDisplayTests`:
  - styles across eight locales;
  - weekday removal for every specific culture on the host;
  - the Gregorian year for ar-SA, th-TH and fa-IR;
  - ISO mode, clocks and quoted literals;
  - a New York DST change and Kolkata;
  - calendar dates that never move, and `IsoUtc`;
  - renamed zones and Windows ids;
  - the source scan, and the seed script identical in `init.sql` and embedded;
  - catalog integrity on dev (no dangling locale or zone, no mojibake);
  - a rendered post in de-DE/Europe/Berlin and in ISO mode, with `datePublished` still in UTC.
- `AdminPagesSmokeTests.Date_settings_narrow_to_a_country_and_preview_the_choice`.
- The Settings block driven in headless Edge (15 checks). That run caught a change-event loop that would have
  called the options endpoint endlessly; it is fixed and guarded.
- The empty-database `sys.columns` diff is clean, and a fresh install from `init.sql` alone holds 249 countries,
  419 zones and 548 locales.
