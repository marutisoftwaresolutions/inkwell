-- DBScripts/2026-11-12_theme-colors-on-site-setting.sql
-- Change: new theme setting "colors-on-site" (CustomThemeSettings row, key/value), which lets a site
--         apply the colours saved under Theme -> Colours to the public reader pages.
-- Schema impact: NONE. CustomThemeSettings is a key/value table; the row is created with its default
--         ("false") the next time Admin -> Theme is opened, and a missing row reads as "false".
-- Action required: None. Existing sites keep their current reader colours until an admin switches it on.
PRINT 'No schema migration required for this change.';
