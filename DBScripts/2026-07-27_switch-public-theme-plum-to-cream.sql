-- DBScripts/2026-07-27_switch-public-theme-plum-to-cream.sql
-- Change: Switch the public theme OFF the custom "plum" purple theme and back to the documented
--         Inkwell "cream" editorial brand (warm cream palette + Source Serif 4 / Geist).
-- Why: the live plum theme used a body font (Nunito) that is NOT self-hosted (silent system-font
--      fallback) and its fonts did not match the preloaded Geist/Source Serif 4 (wasted preloads,
--      minor FOUT). Cream uses the self-hosted + preloaded brand fonts and is coherent with the design.
-- Scope: updates the CustomThemeSettings COLORS + TYPOGRAPHY + border-radius + inkwell-preset only.
--        Layout keys (layout-index / -post / -page / -navbar / -footer) are intentionally NOT touched,
--        so the current layout selection is preserved.
-- Schema impact: NONE (data update on existing rows). Settings/config operation — not a product change,
--        so no README/CHANGELOG entry (per Documentation Maintenance Rule).
-- Idempotent: re-running sets the same values. ENCODING: run with  sqlcmd -f 65001  (values are ASCII).

SET NOCOUNT ON;

-- Colors group -> documented Inkwell cream palette (Tailwind-namespace tokens used by header/footer/cards)
UPDATE CustomThemeSettings SET SettingValue = '#FAF7F2' WHERE SettingKey = 'background';
UPDATE CustomThemeSettings SET SettingValue = '#1A1A1A' WHERE SettingKey = 'foreground';
UPDATE CustomThemeSettings SET SettingValue = '#1A1A1A' WHERE SettingKey = 'primary';
UPDATE CustomThemeSettings SET SettingValue = '#FAF7F2' WHERE SettingKey = 'primary-foreground';
UPDATE CustomThemeSettings SET SettingValue = '#F4EFE6' WHERE SettingKey = 'secondary';
UPDATE CustomThemeSettings SET SettingValue = '#1A1A1A' WHERE SettingKey = 'secondary-foreground';
UPDATE CustomThemeSettings SET SettingValue = '#F4EFE6' WHERE SettingKey = 'muted';
UPDATE CustomThemeSettings SET SettingValue = '#8B7355' WHERE SettingKey = 'muted-foreground';
UPDATE CustomThemeSettings SET SettingValue = '#EDE4D3' WHERE SettingKey = 'accent';
UPDATE CustomThemeSettings SET SettingValue = '#1A1A1A' WHERE SettingKey = 'accent-foreground';
UPDATE CustomThemeSettings SET SettingValue = '#F4EFE6' WHERE SettingKey = 'card';
UPDATE CustomThemeSettings SET SettingValue = '#1A1A1A' WHERE SettingKey = 'card-foreground';
UPDATE CustomThemeSettings SET SettingValue = '#E7DCC9' WHERE SettingKey = 'border';
UPDATE CustomThemeSettings SET SettingValue = '#8C2A1F' WHERE SettingKey = 'destructive';

-- Typography -> documented Inkwell fonts (both self-hosted in fonts.css AND preloaded by _PublicLayout)
UPDATE CustomThemeSettings SET SettingValue = 'Geist' WHERE SettingKey = 'font-heading';
UPDATE CustomThemeSettings SET SettingValue = 'Source Serif 4' WHERE SettingKey = 'font-body';

-- Corner rounding -> Inkwell sharp editorial corners
UPDATE CustomThemeSettings SET SettingValue = '2px' WHERE SettingKey = 'border-radius';

-- Inkwell preset -> cream (drives the public Inkwell token palette: --bg/--fg/--link/--serif/--sans)
UPDATE CustomThemeSettings SET SettingValue = 'cream' WHERE SettingKey = 'inkwell-preset';

-- Verify
SELECT SettingGroup, SettingKey, SettingValue
FROM   CustomThemeSettings
WHERE  SettingKey IN ('background','foreground','primary','primary-foreground','secondary','secondary-foreground',
                      'muted','muted-foreground','accent','accent-foreground','card','card-foreground','border',
                      'destructive','font-heading','font-body','border-radius','inkwell-preset')
ORDER BY SettingGroup, SettingKey;
