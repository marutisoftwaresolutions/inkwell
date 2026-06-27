-- DBScripts/2026-05-22_add-country-region-to-pageviews.sql
-- Change: Add Country (ISO-3166-1 alpha-2) and Region (subdivision name) columns to PageViews.
-- Purpose: Store geo data resolved from the visitor IP via MaxMind GeoLite2 *before* the IP is hashed.
-- Schema impact: Two nullable NVARCHAR columns added to PageViews.
-- Action required: Run this script once. MigrationService also applies it on startup.

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PageViews') AND name = 'Country')
BEGIN
    ALTER TABLE PageViews ADD Country NVARCHAR(2) NULL;
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PageViews') AND name = 'Region')
BEGIN
    ALTER TABLE PageViews ADD Region NVARCHAR(200) NULL;
END

-- Index to support country breakdown queries
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID('PageViews') AND name = 'IX_PageViews_OwnerId_Country')
BEGIN
    CREATE INDEX IX_PageViews_OwnerId_Country ON PageViews (OwnerId, Country);
END
