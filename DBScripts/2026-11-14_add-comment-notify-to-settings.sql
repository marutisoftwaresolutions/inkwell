-- DBScripts/2026-11-14_add-comment-notify-to-settings.sql
-- Change: Added CommentNotifyEnabled and CommentNotifyEmails to the UserSettings JSON blob
--         (Settings -> Comments). When on, a reader's comment is emailed to the listed
--         addresses, or to the post's author when the list is empty.
-- Schema impact: NONE. Stored in Settings.JsonPayload, no column change.
-- Default: off, empty list.
-- Action required: None. The fields are written on the next settings save.
PRINT 'No schema migration required for this change.';
