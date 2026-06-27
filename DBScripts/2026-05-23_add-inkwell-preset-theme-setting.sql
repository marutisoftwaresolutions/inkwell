-- DBScripts/2026-05-23_add-inkwell-preset-theme-setting.sql
-- Change: Added 'inkwell-preset' key to the CustomThemeSetting defaults.
-- Schema impact: NONE — CustomThemeSetting table schema unchanged.
--   The new row is seeded automatically the next time an admin visits
--   Admin → Theme (ThemeSettingsController.Index calls SeedDefaultsAsync).
-- Action required: None. Default value is 'cream'.
-- Valid values: cream | linen | manuscript | folio | press | letterpress |
--               foxglove | cobalt | ink | onyx | slate | sand | plum | forest | mono
PRINT 'No schema migration required — inkwell-preset is seeded at runtime by SeedDefaultsAsync.';
