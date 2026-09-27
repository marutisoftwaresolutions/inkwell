# Inkwell v1.0.7

**Release date:** 2026-09-27
**Type:** Feature and security release (backward-compatible: existing tenants keep their layout, theme, content and settings)

v1.0.7: **"Trust your numbers."** A self-hosted blog has to be able to tell a reader from a
scraper, and a real crawler from someone using its name. This release makes Inkwell's own
analytics count on the same basis as Google Analytics. It proves which crawlers really are who they
say they are. It makes a traffic flood cost almost nothing, and it stops stored reader IP hashes from
being reversible. It also adds a supported Docker deployment.

## Highlights

### 📈 Page views that match what your readers actually do

A page view is now counted when the page's own small, self-hosted script reports back with a signed
token for that page, not when the server merely sees a request. Clients that never run JavaScript,
such as scrapers and request floods, are no longer counted. Admin → Analytics now counts on the same
basis as Google Analytics.

- No cookies are set and nothing is stored in the browser.
- Signed-in users and bots are ignored, a token only counts for its own page, and each address is
  capped at 30 views a minute.
- "Most read" no longer rises when a search engine or AI crawler fetches a post.
- Trade-off, stated plainly: readers with JavaScript disabled are no longer counted. Your trend will
  show a step down at the upgrade, because earlier figures were counted on the server.

### 🔎 Crawler identity, checked rather than assumed

A User-Agent is self-declared. Admin → AI Crawlers now checks every visit that claims to be
Googlebot, Bingbot, Applebot, GPTBot, OAI-SearchBot, ChatGPT-User, PerplexityBot or Perplexity-User
against the IP list or reverse-DNS name its operator publishes.

- A new **Identity check** column shows verified, failed and unchecked visits.
- Failed visits (someone else using the name) are left out of the totals, the trend and the
  most-crawled pages.
- Operators that publish no method are shown as unchecked, never guessed. So are private or proxy
  addresses and checks that could not run.
- The IP firewall keeps protecting search and AI crawlers from being blocked over missing pages. It
  withdraws that protection only from an address proven, by two independent checks, not to be the
  crawler it names.

### 🛡️ Floods cost almost nothing

Page views and crawler visits are held in memory and written in batches every 30 seconds, instead of
one database write per request. Geo-IP lookups happen at write time within the free service's rate
limit. Each buffer is capped, so a flood can neither grow memory nor turn into a flood of database
writes. Up to 30 seconds of analytics can be lost if the process stops abruptly.

### 🐳 Docker deployment

A `Dockerfile` and `docker-compose.yml` (the app, SQL Server, and Caddy for automatic HTTPS) are now
at the repo root. `docker compose up -d` starts a complete stack. A single container against an
external SQL Server also works, configured entirely through environment variables.

## Security

- **Reader IP hashes are no longer reversible.** They were SHA-256 with a salt that was the same in
  every copy of the source. They are now HMAC-SHA256 under a random secret generated for each
  installation, stored Data-Protection-encrypted beside the key ring.
- **Crawler impersonators lose firewall protection.** This applies only to Google, Bing and Apple,
  where the address and its reverse DNS can both be checked.
- **Behind a trusted proxy, traffic recording uses the real client address.** This covers page views,
  crawler visits and the identity check.

## Fixed

- Crawler requests that hit a missing page or a server error were recorded twice, or under the
  internal error page. Each is now recorded once, under the URL that was requested.
- GoogleOther, Google-InspectionTool, Google-Read-Aloud and similar Google agents were counted as
  readers. They no longer are.
- A fresh checkout could fail to build because the project listed specific uploaded files.

## Upgrade notes

- **Schema is automatic.** The one change, `CrawlerVisits.Verified`, is applied on startup by
  `MigrationService`. A fresh install gets the same schema from `DBScripts/init.sql` alone, verified
  by diffing `sys.columns` against a live database in both directions before this release was cut.
- **Back up `ip-hash.secret`** together with your Data Protection keys (`DataProtection:KeysPath`,
  default `App_Data/keys`). If it is lost, reader hashes simply start a new series. Nothing in Inkwell
  depends on them.
- **Optional: clear the old reversible hashes.** Rows written before the upgrade keep them. Nothing in
  Inkwell reads the column, so you can remove them; this cannot be undone, which is the point. Put the
  UTC time you upgraded in place of the date below:
  ```sql
  UPDATE PageViews SET IpHash = NULL WHERE IpHash IS NOT NULL AND CreatedAt < '2026-09-27T00:00:00';
  ```
  On a large table, run it in batches (`UPDATE TOP (50000) …` until no rows are affected).
- **Outbound requests.** To verify crawlers, the server now fetches the public IP lists that Google,
  Bing, Apple, OpenAI and Perplexity publish (at most once a day each), and does some reverse-DNS
  lookups. Nothing about readers is sent.
- **Behind Cloudflare or another proxy,** restrict the server to the proxy's addresses first, then
  turn on **Trust proxy headers** in Admin → Security. Until then the header can be forged.
- **Content Security Policy.** If you run one, allow the self-hosted `/js/inkwell-views.js` and
  `POST /pv`, or page views will not be recorded.
- **Backward-compatible.** No setting changes meaning, and no layout, theme or content changes.

See [`CHANGELOG.md`](CHANGELOG.md) for the complete list.
