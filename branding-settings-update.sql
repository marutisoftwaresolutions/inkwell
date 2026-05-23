-- =================================================================
-- branding-settings-update.sql
-- Updates site name, tagline, logo, and favicon in Settings JSON.
-- Targets the single Settings row for the self-hosted owner.
-- =================================================================

UPDATE Settings
SET JsonPayload = JSON_MODIFY(
                  JSON_MODIFY(
                   JSON_MODIFY(
                    JSON_MODIFY(
                     JSON_MODIFY(JsonPayload,
                       '$.SiteName',        N'Optical Software'),
                       '$.SiteDescription', N'Independent Reviews & Guides for Optometry Software'),
                       '$.SiteLogoUrl',     N'/logo.png'),
                       '$.SiteFaviconUrl',  N'/favicon.ico'),
                       '$.SiteCoverUrl',    N'/uploads/images/site-cover.jpg'),
    UpdatedAt = GETUTCDATE()
WHERE UserId IS NOT NULL;  -- applies to whichever row holds site-wide settings

-- Verify
SELECT JSON_VALUE(JsonPayload,'$.SiteName')        AS SiteName,
       JSON_VALUE(JsonPayload,'$.SiteDescription') AS Tagline,
       JSON_VALUE(JsonPayload,'$.SiteLogoUrl')     AS Logo,
       JSON_VALUE(JsonPayload,'$.SiteFaviconUrl')  AS Favicon,
       JSON_VALUE(JsonPayload,'$.SiteCoverUrl')    AS Cover
FROM   Settings;
