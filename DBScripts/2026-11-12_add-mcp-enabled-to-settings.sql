-- DBScripts/2026-11-12_add-mcp-enabled-to-settings.sql
-- Change: Added McpEnabled to the UserSettings JSON blob (Settings -> AI assistants).
-- Schema impact: NONE. Stored in Settings.JsonPayload, no column change.
-- Default: false. The /mcp endpoint answers 404 until an Admin switches it on.
-- Action required: None. The field is written on the next settings save.
PRINT 'No schema migration required for this change.';
