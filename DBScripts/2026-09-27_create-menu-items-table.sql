-- DBScripts/2026-09-27_create-menu-items-table.sql
-- Change: creates MenuItems — the header and footer navigation menus built in Admin → Menus
--         (v1.0.8 core item; see ADR-0017). Items point at a page, category, tag or series by id
--         (so a slug change never breaks a menu), at the home feed, or at a checked custom URL, with
--         one level of nesting. A site with no header items keeps the nav-page behaviour unchanged.
-- Idempotent; safe to run more than once.

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'MenuItems')
BEGIN
    CREATE TABLE MenuItems (
        Id           UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        OwnerId      UNIQUEIDENTIFIER NOT NULL,
        Location     NVARCHAR(20)     NOT NULL,
        ParentId     UNIQUEIDENTIFIER NULL,
        SortOrder    INT              NOT NULL CONSTRAINT DF_MenuItems_SortOrder DEFAULT 0,
        Label        NVARCHAR(100)    NULL,
        LinkType     NVARCHAR(20)     NOT NULL,
        TargetId     UNIQUEIDENTIFIER NULL,
        Url          NVARCHAR(2048)   NULL,
        OpenInNewTab BIT              NOT NULL CONSTRAINT DF_MenuItems_OpenInNewTab DEFAULT 0,
        CreatedAt    DATETIME2        NOT NULL CONSTRAINT DF_MenuItems_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt    DATETIME2        NOT NULL CONSTRAINT DF_MenuItems_UpdatedAt DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_MenuItems_Owner_Location ON MenuItems (OwnerId, Location, SortOrder);
    PRINT 'MenuItems created.';
END
ELSE
    PRINT 'MenuItems already exists — skipped.';
