-- DBScripts/2026-08-15_add-firewall-settings-to-usersettings.sql
-- Change: Added the IP Firewall configuration fields to the UserSettings model -
--         FirewallEnabled, FirewallThresholdScore, FirewallWindowMinutes,
--         FirewallBlockHours, FirewallEscalateRepeatOffenders, FirewallProtectSearchCrawlers,
--         FirewallTrustProxyHeaders, FirewallIpAllowlist, FirewallNotifyOnBlock.
-- Schema impact: NONE - these are stored in Settings.JsonPayload (the UserSettings JSON blob),
--                so no column or table change is needed.
-- Defaults: The firewall ships enabled with conservative defaults (score threshold 10 over a
--           10-minute window, 24-hour first block, search-crawler protection on, proxy headers
--           NOT trusted). Tenants that never open Admin -> Security get the defaults, and any
--           setting absent from an existing JSON blob deserializes to those same defaults.
-- Action required: None. Fields are written on the next Security settings save.

PRINT 'No schema migration required for this change (UserSettings JSON blob only).';
GO
