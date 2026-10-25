# Changelog

All notable changes to Inkwell are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [Unreleased]

## [1.0.9] — 2026-10-25

### Added

- **Choose how long page-view details are kept.** Settings → Notifications & housekeeping → **Keep page-view details for (days)**. Each recorded view holds an address hash, the browser and the exact time; with a number set (31–3650), an hourly job removes views older than that, spending at most two minutes per run so a large first clean-up never holds up other scheduled work. Admin → Analytics keeps every figure, because it reads a daily summary. Only days already in that summary are removed, and each removal is a small batch. The default, 0, keeps every view, as before.
- **Push straight to WordPress or Ghost.** Admin → Export can now send the content to a running WordPress or Ghost site through its API, as well as download a file.
  - **Signing in:** a WordPress user name and application password, or a Ghost Admin API key. The address and credential are checked before anything is sent, and the screen says which permission is missing if the account cannot write posts, pages or images.
  - **Existing posts:** a post or page whose address is already on the site is left as it is by default, so running a push again sends only what is missing. You can choose to replace it or add a second copy instead.
  - **In the background:** a batch every minute (`Exports:PushBatchSize`, default 25), with a progress page listing each item's outcome and a link to it on the other site. Images are uploaded once each and their links rewritten; an image file missing here keeps pointing at this site. A failed item is retried after 1 and 5 minutes; a rate limit pauses the push until the site allows more; a cancelled or interrupted push leaves what it already sent in place.
  - **The credential is never stored.** It is kept in the server's memory only while the push runs and never written to the database, a log or the audit trail. After a restart the push pauses and asks for it again, then resumes from the first item not yet sent.
  - **What does not go:** comments, authors (everything is credited to the account whose credential is used) and, on WordPress, Yoast SEO, Rank Math and custom fields, which the WordPress API does not accept. The screen lists this before you start; the file export still carries all of them.
  - **Access:** Admin-only, audited (started, finished, paused, resumed, cancelled, with counts but never the credential), and not available in cloud mode. Only public `https://` sites are accepted; `Exports:PushAllowPrivateTargets` allows local test sites.
  - **Upgrade:** adds the `ExportPushes` and `ExportPushItems` tables automatically.
- **Contact forms.** Admin → Forms builds forms for your pages, and the new **Form** block in the page editor places one.
  - **Fields:** name, email, short text, long text, choice list, checkbox and consent (always required), up to 20, in any order, with hints, your own button text and thank-you message. A new form starts as a ready-made contact form.
  - **For readers:** a plain form in your theme, in both colour schemes and on phones. It works without JavaScript; with it, it sends without leaving the page and shows each error at its field.
  - **Spam:** the same checks as comments (a hidden honeypot, a signed timing token, no markup, at most two links, no repeats within 30 days, at most five per address an hour). Spam gets the same "sent" reply, is not stored, and is reported to the IP firewall.
  - **Submissions:** listed per form with search and paging, opened one at a time (with a "Reply by email" button when the form asks for an email), deleted singly or in bulk, and downloadable as CSV. They can be emailed to an address you choose, with Reply-To set to the visitor, and are sent to webhooks as the new `form.submitted` event.
  - **Personal data:** each form sets how long submissions are kept (default 365 days; older ones are deleted every night). The sender's IP address is stored only as a keyed hash, for the rate limit. Admin-only, audited, and included in backups. Export to WordPress or Ghost lists forms as not carried.
  - **Upgrade:** adds the `Forms` and `FormSubmissions` tables automatically.

- **Responsive images.** Every JPEG, PNG and WebP upload now also has 480, 800 and 1200 px WebP copies, and pages offer them to the browser (`srcset` and `sizes`), so a phone downloads a small copy instead of the full image. Measured on the development site, a phone loads about 77 KB of images for the home page instead of about 1 MB. Each copy is named after its original (`photo.jpg.w480.webp`), so two images with the same name and different types never share a copy.
  - **How:** copies are made when an image is uploaded; images already on the site get theirs from a background job, a batch every 10 minutes. Deleting an image deletes its copies. Your posts and pages are not changed; the attributes are added as pages are shown.
  - **Also:** every upload image now carries its width and height, so text no longer jumps as images load, and images in posts and pages load lazily.
  - **Setting:** `ResponsiveImages:Backfill` (default `true`) can stop the background job.
- **Pages can be written in Markdown,** like posts. Choose **Write in Markdown** on a new page, or switch an existing page from its editor. The switch keeps the page looking exactly as before and is refused if it would not. The page uses the same self-hosted editor, toolbar and server-rendered preview as posts, and its revisions keep the Markdown. Existing pages are unchanged and stay in the visual editor. **Upgrade:** adds the `Pages.Markdown` and `Pages.EditorMode` columns automatically.

- **Custom fields over the API.** Post responses carry `customFields` with typed values (numbers, true/false, text, or null when unset), and post create and update accept them. Only the fields sent change, `null` clears one, and values are checked exactly as in the editor; an unknown field or a wrong type is refused by name and nothing is saved. Anonymous callers see only the fields shown to readers.
- **`GET /api/v1/pages/{slug}`** returns a published page with its content, search fields and custom fields, for headless front ends.

- **Contact details for search and answer engines.** Settings → Identity has a contact email, a contact page (a full address or a path such as /contact) and a contact type. With an email or a page set, the publisher's structured data carries a `contactPoint`, and `llms.txt` lists the contact email; left empty, nothing changes.

- **Accessibility checks.** The post and page editors show what a screen-reader or keyboard user would trip over in the saved text: images without alt text, alt text that is a file name, skipped heading levels, link text such as "click here", tables without a header row and paragraphs over 150 words, each with what to do. Admin → Content Health has an Accessibility view for all published posts. The checks are advisory and never block a publish.

- **Content calendar.** Admin → Calendar shows the month's published and scheduled posts, and drafts that carry a date, each on its day in the site's time zone. Every entry opens its editor; on a phone the month is a list of days. Read-only.

- **Right-to-left sites.** A site whose language is Arabic, Hebrew, Persian, Urdu or another right-to-left language now reads right to left: every layout and preset mirrors. The direction follows the site language in Settings. Left-to-right sites look exactly as before.

### Changed

- **The Desk has a new shell.** The admin sidebar is grouped into Content, Audience, Growth, System and Settings, each group collapsible, with a count badge on Comments (awaiting moderation) and Posts (waiting for review); the sidebar collapses to an icon rail; a breadcrumb and the page title sit in the same place on every screen with the primary action beside them; the account menu holds Profile, two-factor sign-in, colour scheme and sign-out; and **Ctrl/Cmd K** opens a command palette that jumps to any screen or action and searches posts. The dashboard is now a working surface: linked tiles with the 7-day change in page views, a "Needs attention" list (comments to moderate, posts to review, posts past their review date, scheduled posts, failed jobs, server errors, firewall blocks in the last 24 hours) that says "Nothing needs you today" when the site is calm, a 30-day traffic sparkline with no chart library, the review queue and recent posts; the scheduled-jobs table folds away unless a job has failed.
- **Every Desk screen uses one set of components,** buttons, inputs, cards, tables, status pills, tabs and overflow menus, in both colour schemes. Status chips, moderation buttons, diff colours, notices and the shared confirmation dialog, toasts and the "Unsaved changes" pill no longer use fixed colours that ignored the dark scheme. Lists that had their own page links (Posts, Security, Subscribers) use the shared search and pager and return to the same filtered page after a delete or bulk action; empty lists say what to do next. Icon-only buttons carry a spoken name. Surfaces lift off the page (8 px corners, a white card on the cream background, one shadow) and content is capped at a readable width on wide screens.
- **The Desk stylesheet is compiled at build time.** Admin and sign-in pages no longer load the 407 KB in-browser Tailwind compiler on every page (which flashed an unstyled shell on each navigation); a precompiled `desk.css` (80 KB) replaces it, together with the 1,300 lines of dead pre-Tailwind CSS that shipped with every page. Opacity variants of the Desk colours now work. The admin layout no longer loads the icon and syntax-highlighting libraries only the public site uses. Maintainers who add a new utility class rebuild with `npx tailwindcss -c tailwind.desk.config.js -i wwwroot/css/desk.src.css -o wwwroot/css/desk.css --minify` or `dotnet build -p:BuildTailwindCss=true`. ADR-0036.
- **Editors fit a phone.** The post and page editors use their natural height under 768 px (the canvas was a few lines tall with the keyboard open) with the Save/Publish toolbar kept in reach, and the publish, schedule and submit-for-review confirmations use the shared dialog (focus trap, Escape, both schemes) instead of a hand-rolled overlay.
- **The Desk shows which version is running,** under the sign-out button and on the dashboard, so you can confirm a deploy took effect. Readers never see it: public pages carry no version number.
- **Post pages can be cached by a CDN, like the home page.** An anonymous visit to a post sets no cookie and is not marked `no-store`. The comment and reply forms fetch their security tokens when a reader reaches the comments, and a comment is sent without leaving the page: the outcome appears next to the form and is read out by screen readers. Without JavaScript, a comment opens a short page that keeps what the reader typed and sends it on the next press, so nothing is lost. Spam protection is unchanged.

### Removed

- **`POST /api/v1/comments`.** It accepted comments without the spam checks, the comments-off setting or the firewall reporting of the comment form. Readers comment through the post page.
- **`POST /api/v1/media`.** It only redirected to the Desk's upload, which needs a signed-in browser, so it never worked from the API.

### Fixed

- **The Docker deployment did not work.** The image failed to build, for two reasons: it added its own user with the UID the .NET 10 runtime image already gives its built-in `app` user, and it left out a SQL file the build embeds from `DBScripts/`. And on first boot nothing created the `blog` database in the new SQL Server container, so the app could not connect and no schema was applied. The image now runs as the base image's `app` user and includes `DBScripts/`. `docker compose up` now runs a one-off `db-init` service that creates the database and applies `DBScripts/init.sql` before the app starts; re-running it on an existing database changes nothing. A GitHub Actions workflow now builds the image and boots the full stack on an empty database on every push. Thanks to the community member who reported it and shared a working fork.
  - **Upgrade:** if you run a single container with `docker run` against your own SQL Server, create the database and run `DBScripts/init.sql` against it first.
- **Link Audit reported links to scheduled posts as broken after they had gone live.** A scheduled post goes live at its time without changing status, and the audit counted only published posts, so every link to it was listed as a dead end. It now treats a post as live exactly as the site does.
- **Analytics and AI Crawlers could take minutes to open, or fail with a server error, on a busy site.** Each screen ran one query per section (eight and four), each reading every row in the period; on a site with millions of recorded views or crawler visits single sections ran past the database time limit. Each screen now reads its whole report in one database call, through an index that covers it, and keeps the result for five minutes, so switching periods and exporting do not read the table again. Both screens also read a daily summary instead of every view and visit: two new hourly jobs, `page-view-rollup` and `crawler-visit-rollup`, summarise each day once it has ended (on an existing site they work back through the stored history a month per run), and the screens read only today's rows directly. The AI Crawlers period now counts whole days, matching its chart. If the database still does not answer in time, the screen shows a message and empty figures instead of an error page.
  - **Upgrade:** the new report procedures and the `PageViewDaily` and `CrawlerVisitDaily` summary tables are added automatically. If you still have to run `DBScripts/2026-09-27_archive-flood-traffic-rows.sql`, it now also clears the summaries from the flood onward so the jobs rebuild them without the archived rows. On a site where `PageViews` or `CrawlerVisits` already holds more than 200,000 rows, the matching index is not built at startup, because building it would hold up the site; Admin → Dashboard shows a "Needs attention" line until you run `DBScripts/2026-10-22_add-covering-indexes-for-report-procedures.sql` in a quiet period. The screens work without it, only slower.
- **A crawler request with a very long address could lose a whole batch of crawler visits.** An index on the address column could not hold addresses longer than 842 characters, and the batch they were written in was rejected with them. That index, and another nothing used, are removed, which also makes each recorded crawler visit cheaper to write.
- **Switching editors with unsaved changes could unpublish a live page or post.** The dialog offered "Save draft", which on a published item is Unpublish. It now offers to save a draft only when that keeps the item's status; otherwise you keep editing and save with the usual button, or discard the changes and switch.
- **A slow site could hold up other background work during a push to WordPress or Ghost.** Each item now has 50 seconds (`Exports:PushItemSeconds`); one that runs out of time is retried later and, if it keeps timing out, marked as failed, and scheduled publishing, newsletters and webhooks carry on. An image the other site refuses (WordPress refuses SVG) is now linked on your site instead of failing the whole post.
- **Editors could not see the forms list in the page builder's Form block.** It showed "There are no forms yet" to anyone but an Admin; anyone who may edit pages now sees the forms (names only).
- **A slow mail server could keep a visitor waiting after sending a contact form.** The notification email is now given 15 seconds; the submission is saved first either way.
- **Comments:** a comment sent again within seconds after an "expired form" message could be discarded as spam; the button now waits long enough. Without JavaScript, a comment resent after fixing a missing field, or after a failed reCAPTCHA check, is no longer discarded as too fast either. If the page cannot fetch its comment tokens, the comment is sent the way it works without JavaScript instead of failing.
- **Push progress no longer moves keyboard focus** every few seconds; the counts update in place and status messages are announced. The "no matches" message has a Clear link.
- **Forms:** deleting a form returns to the same search and page; bulk deleting submissions has a select-all box and stays disabled until something is selected.
- **Image copies** made in the background no longer re-read every image every 10 minutes, and an image that cannot be decoded is not retried each time. Images whose file names contain spaces now get copies too.
- **Errors are shown where they happen.** On the comment page, an empty name, email or comment is marked at that field with its own message; in the form editor, a field without a label or a choice list with one choice is marked on its own row.
- **Recent pushes** show "Finished with failures" when any item failed, with how many items were created, updated, skipped and failed. The push page lists its items a page at a time from the database, and finished pushes are removed after 90 days. Expired form submissions are deleted in batches.
- **Smaller fixes:** Content Health's accessibility view no longer reloads every post on each page of the list; validation messages close only when you type in the form that has the errors; the Register link stays visible after a failed sign-in on cloud sites; the contact type field shows its length limit; commenter avatars are no longer read aloud twice.
- **Posts written in the Desk never counted views, so they never appeared in "Most read".** A new post started with no view count, and adding to nothing left nothing. Views now add up for every post. **Upgrade:** existing posts without a count are set to 0 automatically.
- **Four layouts scrolled sideways.** The Magazine header ran off the page when a site had many categories; on phones, a long tagline widened the Minimal, Reader and Newsletter headers, and the Journal sidebar and Broadsheet columns did not stack. All four now fit the screen.
- **After a validation error, the error message could cover the Save button.** The messages stay at the fields; the copies in the bottom-right corner now close as soon as you start correcting the form.
- **Theme settings could fail with a server error** for a session whose user was no longer in the database (for example after restoring a backup or switching databases). Such a session is now signed out instead.
- **Publishing through the API now follows the Desk:** structured data with errors keeps the post unpublished (the API answers 422 with the problems), each save is kept in revision history, the social preview image is refreshed, and IndexNow is notified.
- **API errors are JSON.** A missing, expired or refused token used to return the site's HTML error page.
- **An expired API token no longer counts against the caller in the IP firewall,** so an integration retrying with a lapsed token cannot block itself. Wrong passwords and two-factor codes at the API sign-in still count.
- **The API's write endpoints never worked.** Creating, updating, publishing and deleting posts, moderating comments and `users/me` answered with a server error, because API sign-in was never switched on. They now accept the token from `POST /api/v1/auth/login`.
- **The Markdown editor's Preview button could not turn the preview off.** Once in preview, the toolbar ignored clicks, including on Preview itself, so only Ctrl+P returned to editing. Preview and Side by side now stay clickable.
- **The page editor's top bar was wider than a phone screen,** so the page scrolled sideways at 400 px. The title field now shrinks to fit.
- **Comments could still be posted with comments switched off.** Turning comments off in Settings hid the form but did not stop a direct submission; a submission is now refused.
- **Every reply form repeated the same hidden field id**, so a page with replies had duplicate ids; each form now has none.
- **The post and page block builders behaved slightly differently.** They were three separate copies of the same editor and had drifted apart; they are now one shared builder, so every block works the same in both. Three visible differences are gone: a duplicated block in the post editor could not be selected, the page editor wrapped a plain horizontal line from an imported or Markdown page in builder markup on the next save, and editing an existing page offered no heading or paragraph text fields in the settings panel, which the post and new-page editors had.
- **Header menus were see-through on sites with saved theme colours.** Theme colours were written out in a form browsers ignore, so in light mode the header dropdowns had no background and their links disappeared into the page behind them. Saved theme colours now apply, and the built-in defaults are always set first, so a partial colour set can no longer leave a menu transparent.
- **The home-page hero card cut off long titles' summaries and hid the Read More button** on the Classic and Neutral layouts. The card now grows to fit its text, the summary is shortened to three lines, and the feature image extends behind a taller card.
- **The Classic header's page menu lost its outline,** because its border colour was set twice and the browser ignored the second setting.
- **The Feed header had no menu on phones, and its Categories menu could only be opened by hovering.** The menu button now opens a drawer with the pages and categories. Categories opens on click or tap, works from the keyboard, closes with Escape, and appears above the page content.

### Security

- **Sign-up is closed once a site has its first account.** Registration was meant for creating the admin account on a new site, but it stayed open afterwards and gave every new account the Author role, which can publish. Anyone could create an account and publish on a live site. It now answers "not found" once any account exists, and the sign-in page no longer links to it. Invite people from Users → Invite. If your site has been public, review Users for accounts you did not create.
- **The API no longer returns commenters' email or IP addresses.** `GET /api/v1/comments` returns what readers see on the page.
- **Public post reads return an explicit shape.** `GET /api/v1/posts/{slug}` used to return the whole stored post, including review notes, schedules, the Markdown source and internal fields. It now returns what a reader sees on the page.
- **API writes follow the Desk's permissions.** Before, any token holder could have changed any post, published without the publish permission and moderated comments. Now posts can be changed or deleted only by their author, an Admin or an Editor; publishing needs the publish permission; a published or scheduled post can be changed only by someone who may publish; moderation needs the comments permission. Every API write is in the audit trail.
- **API tokens are checked against the account on every request,** so disabling an account, changing its role or removing a permission takes effect at once.
- **The example `Jwt:Key` no longer works.** While the key is empty, shorter than 32 bytes or still `REPLACE_WITH_A_RANDOM_64_CHAR_SECRET`, the API issues and accepts no tokens and the dashboard warns.
- **Disabling or deleting a user now ends their Desk session.** A sign-in lasts up to seven days, and the account behind it was never checked again, so a disabled or deleted user kept the Desk until it expired. The account is now re-checked at least once a minute, and at once when an admin disables or deletes it; a missing or disabled account is signed out.
- **Internal custom fields are readable through the API only by people who could edit that post or page.**

## [1.0.8] — 2026-10-06

### Added

- **Newsletter sending.** Admin → Newsletter writes, previews and sends newsletters to your confirmed subscribers.
  - **Writing:** each issue has a subject, preview text, an introduction in Markdown and up to six posts shown as cards (image, summary, date, link).
  - **Before sending:** preview the email, then send a test to yourself. Sending asks for confirmation with the number of recipients, and the typed word SEND at five or more.
  - **How it goes out:** in the background, in batches (`Newsletter:BatchSize` per minute, default 50), through your SMTP server. A failed copy is retried after 5 and 30 minutes and the reason is shown in the send log. Someone who unsubscribes meanwhile is skipped, and **Stop sending** cancels whatever has not gone out.
  - **Unsubscribe:** every copy carries the subscriber's own unsubscribe link and one-click unsubscribe headers (RFC 8058), so inbox providers can offer an Unsubscribe button. There is a plain-text version for mail apps that need one.
  - **Privacy and access:** no tracking pixels or rewritten links. Admin-only, audited (drafted, updated, queued, test sent, cancelled, sent), and single-site installs only.
  - **Upgrade:** adds the `NewsletterIssues` and `NewsletterDeliveries` tables automatically.
- **Dates in your time zone and country's format.** Settings → Display → **Date and time** sets four things:
  - **Country** narrows the next two lists, and suggests the country's main zone and its usual format.
  - **Time zone,** from the IANA Time Zone Database.
  - **Date format,** from each country's Unicode CLDR conventions, or ISO 8601.
  - **Clock:** the format's own, 24-hour or 12-hour. A live preview shows today in the chosen settings.
  - **Where it applies:** every date on the public site (all home layouts, post cards, post pages, comments, footers, the masthead date) and in the Desk now follows these settings.
  - **Calendar dates** such as “last verified” and “next review” are never shifted by the time zone.
  - **Gregorian year everywhere:** sites using Arabic, Thai or Persian formats still show it.
  - **Reference data:** 249 countries, 419 time zones and 548 country date formats are stored in three new tables (`Countries`, `TimeZones`, `DateLocales`), seeded by a dated, idempotent script generated from IANA tzdata 2026e and CLDR 48. `tools/RefDataGenerator` rebuilds it when either is updated.
  - **Upgrade:** the tables are created and seeded automatically. A site that changes nothing shows US-English dates in UTC; a Windows time zone saved earlier keeps working and is stored under its IANA name on the next save.
- **Custom fields.** Settings → Custom fields defines your own fields for posts, pages or both.
  - **Types:** text, number, date, web address or yes/no.
  - **Editing:** a “Custom fields” panel in the post and page editors. A wrong value, such as a number with words in it, is shown again at the field with what to fix, and nothing is saved.
  - **Readers:** fields marked “Show to readers” appear as a “Details” list on the page. Posts also carry them as `additionalProperty` in their structured data. Other fields stay internal.
  - **Safe to change:** renaming a field keeps its values. Removing one hides it everywhere but keeps what was entered. Changing a field's type never clears a value: one that no longer fits is shown in the editor as it was, flagged for you to correct. Revisions record the values.
  - **Yes / no** fields have three states: not set, Yes or No. Readers see the answer only when one is chosen.
  - **Export:** WordPress exports carry each value as a native custom field. Ghost has no custom fields, and the export summary says so before you download.
  - **Upgrade:** adds `CustomFieldsJson` to Posts and Pages automatically. Nothing changes on a site until a field is defined.
- **Gallery block.** Several images in a grid of two, three or four columns:
  - **In every editor:** the post and page builders have a Gallery block whose settings edit columns, order, alt text and captions. The rich-text editor has `/gal`, and the Markdown editor has a toolbar button.
  - **Choosing images:** the media picker can now select several images, in the order you pick them.
  - **Readers:** each image links to its full-size file, so a gallery works without JavaScript. With it, a self-hosted lightbox opens the image, with arrow keys between images and Escape to close, and focus returns where it was. The lightbox script loads only on pages with a gallery. Phones show two images per row.
- **Alt text and captions for images in the media library.** Each image has a **Details** page, shown as **Add alt** when it has none. Its alt text and caption are copied into galleries when you place them. Changes are audited.
- **Markdown authoring.** Posts can be written in Markdown as well as in the visual editor.
  - **Starting:** choose **Write in Markdown** on a new post, or switch an existing post from its editor. Its current HTML becomes the Markdown source, and the switch is refused if that would change how the post looks. Switching back keeps the post as it renders.
  - **The editor:** self-hosted, with a formatting toolbar, keyboard shortcuts, side-by-side view and a preview rendered by the server. It supports CommonMark plus tables, strikethrough, task lists and footnotes. HTML is allowed.
  - **Saving:** the post's HTML is rendered from the Markdown on every save, so pages, feeds, search, `llms.txt`, webhooks and exports are unchanged.
  - **Revisions** keep the Markdown source.
  - **API:** the posts API accepts `markdown`. Sending `content` (HTML) makes HTML the post's source again.
  - **Ghost import:** a new option keeps Ghost posts written in Markdown as Markdown posts. Ghost exports without rendered HTML now render their Markdown properly instead of flattening it.
  - **Upgrade:** adds `Markdown` and `EditorMode` to `Posts` automatically. Existing posts are unchanged.
- **Two-factor sign-in.** Users can add a code from an authenticator app to their sign-in, under Profile → Two-factor sign-in.
  - **Setting up:** scan a QR code (or type the setup key), confirm one code, then save ten single-use recovery codes. They are shown once.
  - **Signing in:** after the password, the Desk asks for the 6-digit code. A recovery code works in its place. Five wrong codes end the attempt, and each one counts toward the IP firewall's sign-in limits.
  - **API:** `POST /api/v1/auth/login` accepts the code as `code` and refuses a token without it.
  - **Requiring it:** optional for everyone by default. An Admin can require it for chosen roles in Settings → Notifications & housekeeping. People in those roles, including anyone already signed in, are asked to set it up before they reach the Desk.
  - **Recovery:** users can create new recovery codes or turn two-factor off (with their password). An Admin can turn it off for someone who lost their phone (Users → Reset 2FA).
  - **Audit:** every change is audited.
  - **Upgrade:** adds four columns to `Users` automatically.
- **Export to WordPress and Ghost.** Admin → Export downloads the site's content in each platform's own import format:
  - **WordPress:** a WXR 1.2 file for **Tools → Import → WordPress**.
  - **Ghost:** a ZIP of import JSON plus `content/images` for **Settings → Import**.
  - **Before you download,** a summary built from your own content shows what carries over, what changes and what is not exported:
    - **WordPress keeps** posts, pages and their hierarchy, categories, tags, authors, comments with replies, and feature and in-body images, which WordPress downloads during import.
    - **Search and social fields** are written for both Yoast SEO and Rank Math.
    - **Ghost:** categories become tags, and series become internal tags. Images are packed in the ZIP. Comments are not exported, because Ghost cannot import them.
    - **Inkwell's own blocks** (answer capsule, key facts, roundups, how-to steps, FAQs and series) are written into the post body as ordinary HTML, so no section is left empty. WordPress also keeps the original data in post meta.
  - **Options:** unpublished posts, pages, comments and email addresses can each be left out. Email addresses are off by default; Ghost needs them to keep authors.
  - **Files and access:** files are built outside the web root, streamed to you and deleted when the download finishes. Admin-only, every export is audited, and the screen is not available in cloud mode.
  - **Tested by real imports** into WordPress 7.1.2 (with the WordPress Importer 0.9.6) and Ghost 6.67.0. The import instructions on the screen now say that WordPress downloads images only from ports 80, 443 and 8080, that re-importing into WordPress skips posts it already has, and that Ghost should be imported into once, because a second import copies every post.
- **Cookie consent.** The public site now asks readers before any optional cookie is set. It is on by default, including on existing sites after the upgrade, and can be turned off in Admin → Settings → Search & analytics (**Ask readers for cookie consent**).
  - **With a Google Analytics Measurement ID:** the Google tag is no longer in the page. A banner offers **Accept analytics cookies** and **Reject analytics cookies** as equal buttons, and Analytics loads only after Accept.
  - **Without one:** the banner is a short notice that only essential cookies are used.
  - **Changing a choice:** the choice is remembered for 180 days in a first-party cookie and can be changed from **Cookie settings** at the foot of every page. Withdrawing consent stops the tag and removes its cookies.
  - **Rendering:** the banner is self-hosted, works in every layout, preset and colour scheme, and leaves page caching unchanged.
  - **Turning it off** restores the earlier behaviour (Analytics on every page).
  - **Measurement ID:** the Measurement ID field now accepts only a Google tag id, such as `G-ABC123XYZ9`.
  - **Built-in Analytics:** it uses no cookies and is unaffected.
- **Backup and restore.** Admin → Backups makes a whole-site backup: one ZIP file with posts, pages, taxonomy, series, users, roles, settings, theme, menus, redirects, snippets, webhooks, firewall rules, comments, subscribers, newsletters, revisions, the audit trail and every uploaded file.
  - **Making one:** backups run in the background through the scheduler, and the list updates by itself when one is done. Page-view analytics are included only when asked for. Backups are stored outside the web root (`Backups:Path`, default `App_Data/backups`), and the newest 10 are kept (`Backups:Keep`).
  - **Restoring:** only into a fresh install. Copy the ZIP into the new server's backups folder and choose **Restore from a backup** on the first-run screen. The restore runs as one transaction, so a failure leaves the install empty. Primary keys are kept, so every link between posts, categories, users and media survives.
  - **Signing in afterwards:** users sign in with their old passwords.
  - **Not included:**
    - the encryption key ring, so the Search Console connection and webhook signing secrets must be entered again after a restore;
    - crawler, error and job logs;
    - search-performance data;
    - the webhook delivery log;
    - import staging;
    - password-reset links.
  - **Access:** Admin-only; making, downloading, deleting and restoring a backup are all audited. The screen is not available in cloud (multi-tenant) mode.
- **Editorial review.** Authors without the publish permission now submit posts for review; before, pressing Publish silently saved a draft.
  - **Two new states:** *In review* and *Needs changes*.
  - **Reviewing:** an Editor or Admin approves the post, which publishes it (or schedules it, if its time is ahead), or sends it back with a note the author sees above the editor. Saving a returned post as a draft keeps the note, and an editor saving edits to a post in review keeps it in review.
  - **An author's changes to a live post** go through review too, which takes the post off the site until approved; the confirmation says so first.
  - **Finding work:** the Posts list filters by both states, and the dashboard shows a "Waiting for review" queue to editors and "Sent back to you" to authors.
  - **Readers never see** a post in review or one that needs changes: not on any page, feed, sitemap, search or `llms.txt`.
  - **Audit:** submissions, approvals and returns are audited.
  - **Upgrade:** adds `ReviewNote` and `ReviewRequestedAt` to `Posts` automatically.
- **Navigation menus.** Admin → Menus builds the header and footer menus from pages, categories, tags, series, custom links and, in the footer, the home feed:
  - **Items:** ordered and nested one level, with an optional new-tab setting. They point at content by id, so renaming a slug never breaks a menu, and deleted or unpublished content drops out on its own.
  - **Layouts:** all eight navbar and six footer layouts render them. Top-level items appear in the header bar; nested items appear in the theme's menu and on phones.
  - **Existing sites are unchanged:** until a header menu is saved, the header shows the pages marked "Show in navigation" exactly as before, and a site with no footer menu shows no extra footer row. Saving a menu empty returns to that default, after a confirmation.
  - **Custom links:** only a site path or an http(s) address is accepted.
  - **Access:** uses the Theme permission; changes are audited.
  - **Upgrade:** adds the `MenuItems` table automatically.
- **Webhooks.** Admin → Webhooks sends a signed JSON request to another service when something changes, to rebuild a static site, post to social media or purge a cache:
  - **Events:** `post.published` (including a scheduled post reaching its time), `post.updated`, `post.unpublished`, `page.published` and `comment.created` (a reader's comment, sent while it awaits moderation).
  - **Delivery:** queued and sent by the scheduler within a minute, so a slow or dead endpoint never holds up publishing. A failed delivery is retried after 1 minute, 5 minutes, 30 minutes and 2 hours, then marked failed.
  - **Delivery log:** kept for 30 days per endpoint, with the response code and the start of the response, **Redeliver**, and **Send test**.
  - **Headers:** every request carries `X-Inkwell-Event`, an `X-Inkwell-Delivery` id that a redelivery keeps (so receivers can ignore duplicates), and `X-Inkwell-Signature: t=<unix>,v1=<HMAC-SHA256 of "t.body">`.
  - **Secret:** the signing secret is generated for you, stored Data-Protection-encrypted and never shown again.
  - **Private networks refused:** addresses on the server itself or a private network are refused, including host names that resolve to one, and redirects are not followed.
  - **Privacy:** payloads never include a commenter's email address or IP.
  - **Access:** Admin-only; creating, changing, deleting, testing and redelivering are audited.
  - **Upgrade:** adds the `Webhooks` and `WebhookDeliveries` tables automatically.
- **What crawlers actually request, in AI Crawlers.** Crawler visits now record the query string as well as the path, and the report gains a **Requests with a query string** table: the path, the query string, visits and how many crawlers asked for it, leaving out visits that failed the identity check. Before this, `/?page=500`, `/?p=123` and `/?anything` were all recorded as `/`. Query strings are shown as text, never as links. Visits recorded before the upgrade have none. Upgrading adds a `Query` column to `CrawlerVisits` automatically.

### Changed

- **LICENSE names Maruti Software Solutions as the copyright holder,** in place of the product's former name.
- **The home page can be cached by a CDN.** It no longer sets a cookie or sends `Cache-Control: no-store`. The newsletter form fetches its anti-forgery token when the reader presses Subscribe, instead of carrying one in the page, so an edge cache rule on `/` no longer breaks the form. The check itself is unchanged: nothing is written without a valid token. Without JavaScript, the form leads to a short page that confirms the sign-up with one more press. Post pages still carry the comment form's token and are not cacheable yet.
- **"Most read" counts the same readers as Analytics.** A post's view count, which orders the popular-posts lists on the home page and the 404 page, now goes up from the page-view beacon, like Analytics, instead of on every page load. Request floods, scrapers, cache revalidations and API reads no longer raise it, and opening a post no longer writes to the database. Existing counts are kept; new views add to them.
- **The Desk takes you to the first error after a failed save.** On any editor, the first field with an error is opened, scrolled into view and focused, even inside a collapsed section.

### Removed

- **`DBScripts/migration-all-changes.sql`.** An early one-off migration that predated `init.sql`, stopped with errors when run against a current database, still carried the product's old codename, and seeded one site's category and tag list. Every table and column it created is in `DBScripts/init.sql`, which, together with the automatic startup migration, is the supported way to create or upgrade a database. The `init.sql` header and start-up banner now name only Inkwell.

### Fixed

- **Opening an unsubscribe link unsubscribed immediately.** Mail systems open links to scan them, which could unsubscribe readers who never clicked. The link now shows a page with an Unsubscribe button; one-click unsubscribe from inbox providers still works.
- **The welcome email promised automatic emails for every new article,** which the site never sent. It now says readers will get the newsletter when one is sent.
- **Deleting a subscriber left no audit entry.** It is now audited, by id only, so the address stays out of the permanent record.
- **The Settings page logged a script error on every visit.** It loaded a form-validation library that needs jQuery, which the Desk does not load, so it never ran. It is removed; fields are still checked by the browser and the server.
- **Most dates ignored the site's time zone.** Only the post page converted. Home layouts, post cards, comments and most Desk screens showed UTC, and footers and mastheads used the server's own clock, so a post published late in the evening in India could show the next day's date, or the previous one. All of them now use the site's zone.
- **Published and modified times in structured data and Open Graph had no time zone.** They now end in `Z` (UTC), so search engines no longer have to guess the zone.
- **Editing a page did not save its search and social fields.** Meta title, description, canonical address, Open Graph and Twitter fields were shown on the page editor but only saved when the page was first created. They are now saved on every edit.
- **Newsletter sign-up without JavaScript.** Errors such as a mistyped address were lost after the redirect; they now appear next to the address field. The sign-up box no longer names a topic in its copy; it uses the site's name.
- **Listing pages answered any page number and any query string.** The home feed, search, category, tag, author and series pages returned 200 for a page past the last one (an empty listing) and for any unknown parameter (`/?p=123`, `/?anything`), so every variant was a separate URL crawlers could keep fetching. A page past the last one now returns 404, and unknown or invalid parameters are dropped with a 301 to the clean URL. Paging, search, filters, `utm_*` campaign tags and ad click ids are always kept, and no link the site itself produces is ever redirected.
- **Cancelling a confirmation turned off the unsaved-changes warning.** On any Desk form that asks for confirmation before saving, choosing Cancel quietly stopped the "you have unsaved changes" prompt for the rest of the visit, so leaving the page afterwards could lose work. The warning now stays on until a save actually goes through.
- **Page links in the Feed header layout led to "not found".** The Feed navbar linked navigation pages to `/pages/{slug}`, an address that never existed; it now links `/{slug}` like every other layout.
- **Three Settings fields had labels that pointed at nothing.** The site logo, favicon and cover image address fields carry their own ids for the media picker, which disconnected their labels: screen readers announced them as unnamed fields and clicking a label did not focus its field. The labels now point at the right fields.
- **Comments and social-card images were available for unpublished posts.** A comment could be posted to a draft, and `/og-image/{slug}.png` generated an image showing a draft's title, for anyone who knew the address. Both now answer "not found" unless the post is live.
- **Search results lost the query from page 2 onwards.** The pager on `/search?q=…` pages linked with the home feed's `search=` parameter, which the search page ignored, so page 2 and later listed every post. Search now reads either, so paging through results keeps the query.

### Security

- **The subscription confirmation email inserted the subscriber's name into its HTML unescaped.** A name containing HTML could alter the email sent to that address. It is now encoded.
- **An unused autosave endpoint let other websites overwrite posts.** `POST /admin/posts/autosave/{id}` was called by nothing in the Desk, yet it replaced any post's title and body for whoever was signed in, with no anti-forgery check, no ownership check and no audit entry. A page on another site could have used a signed-in editor's browser to rewrite a post. It has been removed, and the README no longer claims draft auto-save, which never worked.
- **Failed API sign-ins were invisible to the IP firewall.** A wrong password at `POST /api/v1/auth/login` returned 401 without being reported, so password-guessing against the API never led to a block. Failed API sign-ins (and failed two-factor codes) now count toward the same limits as the Desk sign-in page.
- **The setup wizard stayed open after the first admin registered.** The wizard at `/setup` treated a site as set up only once its settings had been saved. The first admin can also be created at `/account/register`, which saves no settings, so until someone saved Settings, anyone could complete the wizard and create another Admin account. The wizard now closes as soon as any user or settings exist.
- **A user without the publish permission could publish a post by crafting the save request.** The post editor took the status from the button only for Publish and Save Draft; any other request kept a submitted Status field, so `Status=Published` went live. The server now decides every status from the button pressed and the user's permission, and never from a submitted value.

## [1.0.7] — 2026-09-27

### Added

- **Docker deployment** — a `Dockerfile` (multi-stage, non-root, health-checked) and `docker-compose.yml` (the app, SQL Server, and Caddy for automatic HTTPS) are now at the repo root. `docker compose up -d` after copying `.env.example` to `.env` starts a complete stack on the schema's normal auto-apply path; a single container against an external SQL Server also works, configured entirely through environment variables (`ConnectionStrings__DefaultConnection` and friends) with no `appsettings.json` baked into the image. Adds `ReverseProxy:TrustForwardedHeaders` (default `false`), which the compose deployment enables because only Caddy's ports are published to the host — a bare IIS/Kestrel install is unaffected.
- **Crawler identity check in AI Crawlers** — each visit claiming to be Googlebot, Bingbot, Applebot, GPTBot, OAI-SearchBot, ChatGPT-User, PerplexityBot or Perplexity-User is checked against the IP list or reverse-DNS name its operator publishes. The report gains an **Identity check** column (verified, failed, unchecked), and visits that fail — someone else using the crawler's name — are left out of the headline totals, the daily trend and the most-crawled pages. Operators that publish no method stay unchecked. Private or proxy addresses and checks that cannot run are recorded as unchecked, never as failed. The server fetches each operator's public list at most once a day; nothing about readers is sent. Existing visits stay unchecked. Upgrading adds a `Verified` column to `CrawlerVisits` automatically.

### Changed

- **Page views are counted when the page's script reports back.** A public page now carries a small self-hosted script that sends a signed, path-bound token to `/pv` once the page loads; only that beacon records a view. Clients that never run JavaScript — scrapers and request floods, which sent the server's counts far above Google Analytics — are no longer counted, so Analytics now counts on the same basis as GA. Readers with JavaScript disabled are not counted either, and figures before the upgrade were counted on the server, so expect a step down in the trend. No cookies are set and nothing is stored in the browser. Signed-in users, bots, beacons for a different page and more than 30 beacons a minute from one address are ignored.
- **"Most read" ignores crawlers.** A post's view counter no longer rises when a search engine, AI crawler or script fetches it.
- **Page view and crawler tracking is buffered.** A counted request no longer writes to the database or calls the geo-IP service itself: rows are held in memory and written in batches every 30 seconds or 500 rows, with a final flush when the application stops. Geo-IP lookups happen at flush time for at most 20 new addresses per batch, keeping the free service under its rate limit; views beyond that budget are stored without a country. Each buffer is capped at 50,000 rows and drops (and logs) anything past that, so a traffic flood cannot grow memory or become a flood of database writes. Up to 30 seconds of analytics can be lost if the process ends abruptly.

### Fixed

- **Crawler errors were recorded twice or under the wrong address.** When a crawler hit a missing page, AI Crawlers logged both the requested URL and the internal `/error/404` page; a crawler that caused a server error was logged only as `/error/500`, hiding which URL failed. Each request is now recorded once, under the URL that was requested, with its real status.
- **Some Google fetchers were counted as readers.** GoogleOther, Google-InspectionTool, Google-Read-Aloud and similar Google agents carry no "bot" token in their user-agent and were counted as page views. They are now filtered out.
- **A fresh checkout could fail to build.** `Blog.Web.csproj` listed close to twenty specific uploaded media files as individual build items; each one is an error waiting for any checkout other than the machine that happened to have that exact file — a new clone, CI, or a Docker build, which must exclude live uploads from the image in the first place. Removed; uploads are excluded from the project's default items by design and were never meant to be enumerated there.

### Security

- **Reader IP hashes are no longer reversible.** Page view IPs were stored as SHA-256 of the address plus a salt that was the same in every copy of the source, so anyone with a copy of the table could recover the addresses; the README had also called it a per-install salt. They are now stored as HMAC-SHA256 under a random secret generated for each installation and kept, Data-Protection-encrypted, as `ip-hash.secret` beside the key ring — back it up with the keys. Rows written before the upgrade keep the old values until they are cleared (`UPDATE PageViews SET IpHash = NULL WHERE CreatedAt < <upgrade time>`, shown in the release notes); nothing in Inkwell reads the column, so clearing it loses no feature.
- **Crawler impersonators lose the firewall's crawler protection.** Search and AI crawlers are still never scored for missing pages, except from an address already proven not to be the crawler it names — outside the operator's published IP list and with reverse DNS that is not the operator's (Google, Bing, Apple). Unchecked addresses, and operators that publish only an IP list, keep the protection so a real crawler is never blocked on a stale list.
- **Traffic recording behind a proxy uses the real client address.** With **Trust proxy headers** enabled, page views, crawler visits and the crawler identity check use the address the firewall resolves from `CF-Connecting-IP` / `X-Forwarded-For`, not the proxy's.

## [1.0.6] — 2026-09-24

### Added

- **Bulk moderation in Comments** — select comments and approve, move to pending, delete, or delete as spam in one action, with a confirmation dialog for the destructive choices and a typed guard at five or more; spam reports each posting address to the IP Firewall once per batch. The queue also gains search across author, email, text and post title, an All tab beside Pending and Approved, the shared pager, and honest empty states.
- **Generated invite passwords** — the Users screen offers Generate and show/hide on the invite password; leave it blank and a 16-character password without ambiguous characters is created. After the invite the password is shown once with a Copy button and never emailed.
- **Reader accessibility** — a "Skip to content" link opens every public page, every keyboard-focused control shows a visible focus ring, and a rejected comment returns the reader to the comments block with the message beside the form and their text still filled in.
- **Search and paging on Link Audit and Content Health**, keeping the active filter tile while searching.
- **Confirmation before blocking an address** — the manual block form and the watchlist Block button in Admin → Security, and the 410 "retire" action in Admin → Redirects, now open the Desk confirmation dialog with consequence copy instead of acting on a single click.
- **Table of contents and reading progress** — the Magazine and Neutral post layouts now show an "On this page" list when a post has three or more section headings, built server-side from the body's h2 and h3 elements, which also gain stable, unique ids so sections can be linked to directly (hand-authored ids are kept). A thin progress bar across the top of the viewport fills as the reader moves through the article on all three post layouts. Both are self-hosted and degrade cleanly without JavaScript.
- **404 page that helps** — the not-found page names the path that was requested, offers a search box seeded from it, lists the five most-read posts, and links home and to the feed. It is `noindex,follow`.
- **Dark colour scheme** — Admin → Settings gains **Colour scheme**: *System* follows each reader's device setting, *Light* and *Dark* force one. It applies to the public site and the Desk. The dark palette is the Ink preset's, so it reads as a designed theme rather than an inverted one; presets that are already dark (Ink, Onyx) keep their own colours. Both token namespaces are covered, so the Magazine, Verdict and legacy layouts all respond.
- **Site search in every header** — a search icon in all eight navbar variants opens a search box (no JavaScript required; Escape closes it), and `/search?q=…` now renders a proper results page: a heading with the result count and the query, the page position, a refine box and a clear link, with a matching page title. Results pages remain `noindex,follow`.
- **Bulk actions in the Posts list** — tick any rows (or the header checkbox) and a toolbar appears: Publish, Move to draft, Add tag, or Delete for every selected post. Each post is checked and audited individually with the same ownership and permission rules as the single actions; a post that is not yours is skipped and counted, never silently changed. Deleting five or more asks you to type DELETE.
- **Spam and reply in comment moderation** — **Spam** deletes a comment and reports the address that posted it to the IP Firewall with the same weight as the automatic gate, so an address that keeps posting spam blocks itself. **Reply** posts an approved reply as the signed-in user, threaded under the original, and approves the original if it was still pending. Both are audited.
- **Draft preview links** — the post editor sidebar gains **Share a preview**: one click issues a signed link, valid for seven days, that shows the post as last saved to anyone who holds it, with no account. The token carries the post id and expiry under Data Protection, so nothing is stored and nothing can be forged; a rotated key ring simply retires old links. Preview pages are `noindex,nofollow` (header and meta), `Cache-Control: no-store`, close comments, show a banner naming the expiry, and are never counted as views.
- **Conditional GET on post pages** — public post pages now send a weak `ETag` and a `Last-Modified` computed from the post, its approved comments, the theme settings, the site settings and the deployed build version, with `Cache-Control: no-cache`. A revalidating request whose `If-None-Match` (or, absent that, `If-Modified-Since`) matches receives `304 Not Modified` before any rendering; the view is still counted, so returning readers keep contributing to "most read". Signed-in staff and preview links bypass validators entirely. Archive and home pages are unchanged.
- **Revision history for posts and pages** — each save with changed content (title, body, meta description or title, FAQ / Key Facts / How-To / roundup / answer capsule) writes a revision to a new `Revisions` table, tagged with why (Created, Saved, Published, Scheduled, Restored) and by whom. The editor sidebar lists them; **Compare** shows the revision's title, meta and a line-level diff of the body against the current one; **Restore** puts the content back, recording the current content as a revision first so the restore is itself undoable. Slug and publish state are never touched by a restore. Revisions are pruned to the new **Revisions kept per post or page** setting (default 25). Restores are audited. The dead `Revision` types that had been registered with no caller since v1.0.1 are now the real implementation.
- **Display time zone** — Admin → Settings gains a time-zone picker. Dates on the public post templates are shown in that zone; storage stays UTC.
- **Search Console integration** — Admin → Settings gains a Search Console section: enter the property (a bare domain becomes a domain property, a URL a URL-prefix property) and paste a Google service-account key file, then add that account as a user on the property in Search Console. The key is stored Data-Protection-encrypted, never echoed back, logged, audited or exported; the audit trail records only that a property was connected or disconnected and by which service account. A nightly `search-console-sync` job pulls every date × page × query row into a new `SearchPerformance` table — the first run backfills the configured retention (default and maximum 16 months), later runs re-pull the last three days because Google revises them, and rows past retention are pruned. The job is fail-open per property and reports its result on the dashboard.
- **Striking-distance panel** — the post editor sidebar gains **Search performance**: clicks, impressions, CTR and average position over the last 28 days with their change against the 28 before, the top queries that brought the post impressions, and a plain-language flag when the post is in striking distance (position 8–20 with real demand), losing impressions (a hard fall from a meaningful base), or ranking on page one with almost no clicks (a title and snippet problem, not a ranking one). Admin → Content Health gains the same three flags as filter tiles and a Search column; the dashboard shows 28-day clicks, impressions and the striking-distance count. Every empty state says why it is empty. Loaded on demand; never blocks a publish; fails open.
- **Self-service password reset** — the sign-in page now offers "Forgot your password?". A single-use link, valid for 30 minutes, is emailed to the account's address; only the hash of the token is stored, so a leaked table cannot be replayed. The response is the same whether or not the address belongs to an account, requests are capped at three per account per hour, a new link supersedes any outstanding one, and both the request and the completed reset are recorded in the audit trail. On a site without SMTP configured the page says that reset by email is unavailable rather than pretending to send. Adds the `PasswordResetTokens` table.
- **Scheduled jobs** — the platform gains an in-process scheduler: one hosted service that wakes every minute, runs any job whose interval has elapsed inside a fresh scope, and records the outcome in a new `Jobs` table. Admin → Dashboard shows each job's last run, result and report. Jobs fail open (a failure is recorded and the next tick carries on), never delay startup, and catch up after an app-pool recycle without re-running work that already ran. No external cron or task scheduler is required. First two jobs: nightly pruning of AI-crawler visits to the configured retention window, and nightly removal of expired password-reset tokens.
- **AI-crawler visit retention** — Admin → Settings gains "Keep AI-crawler visits for (days)" (default 90, range 7–3650). The `CrawlerVisits` table previously grew without bound; it is now pruned nightly per owner, and Admin → AI Crawlers states the retention in effect.
- **CMS pages in `llms.txt` and `llms-full.txt`** — both AI-facing files now carry a `## Pages` section listing every published page (About, Contact, Editorial Policy and the like), which is what an answer engine reads to establish who a publisher is. `llms-full.txt` adds each page's meta description or opening text. The section is omitted for tenants with no published pages; drafts never appear.
- **Reading time on every post layout** — the Verdict layout now shows "N min read" alongside the date, and all three post layouts compute it through one shared helper (238 words per minute, HTML ignored, never zero) instead of three separate formulas.
- **Startup findings on the dashboard** — Admin → Dashboard shows a "Needs attention on this server" panel when startup finds a file in `wwwroot` that shadows a mapped route (the file is served and the route never runs) or when Data Protection keys could not be persisted to disk.

### Changed

- **Media Library** — Copy URL and Delete are reachable by keyboard and always visible on phones; uploads show the busy bar, block a second drop until finished, report the server's reason when a file is rejected, and return to the current folder; the folder tabs filter in SQL and live in the URL; the delete dialog names the posts and pages that still embed the file and requires typing DELETE for a referenced one; the upload copy lists exactly the accepted types and the 10 MB limit.
- **Generated Open Graph cards are pruned after 30 days** by the nightly `og-cache-prune` job, and a post's card is discarded when the post is saved or deleted so the next request rebuilds it.
- **Users** — disabling an account asks for confirmation; changing a role requires an explicit Change click and a confirmation naming the old and new role and what the new role can do.
- **Theme** — the editor panel stacks above the live preview on phones; layout thumbnails and the selected preset ring follow the colour scheme; switching a preset while Fine-tune has unsaved edits asks first; font pickers are real buttons with a keyboard-operable list; colour inputs are labelled; the preset sections say which layouts each governs; the font search no longer mentions Google Fonts.
- **Desk notifications** — error and warning notifications are announced to assistive technology and stay until dismissed; success notifications still fade. On a failed save the page scrolls to the first field with an error and focuses it. The active sidebar item, analytics period, crawler window and redirects tab are marked with `aria-current`.
- **Desk colours** — status pills, notice boxes and stat colours on the Dashboard, Audit Trail, Error Monitor, Analytics, Security, AI Crawlers, Pages and Redirects screens use the design tokens and render correctly in the dark scheme.
- **Accessible names everywhere** — icon-only actions (delete, edit, view, move, remove, close, menu) and every filter input carry a name that includes the item they act on; the Audit Trail's before/after details open from a keyboard-reachable button; the AI Crawlers chart is described for screen readers.
- **Post and page editors** — on phones the settings panel starts hidden and opens as a full-screen sheet with a Close button and Escape; the sidebar is grouped into collapsible Publishing, SEO and social, and Answer blocks sections that remember their state; the title, roundup, FAQ, key-fact and how-to fields have accessible names.
- **Import** — "Run the import" is a confirmed step that names what will be created, renamed, overwritten and skipped, and requires typing OVERWRITE when existing content would be replaced; progress and problems are announced to screen readers.
- **Every header layout carries a single site search** (the shared control posting to `/search`) instead of a second overlay posting to the home page; sites without a logo show their name as text instead of requesting a placeholder image from an external host; menu toggles have accessible names.
- **Neutral post layout** caps the article body at a comfortable reading width while the header keeps the full column.
- **Setup wizard** — calmer copy ("Ready to launch", "Launch blog", "Setup complete"), a "Step N of 4" indicator, validation at the field with the typed values kept, the launch button disabled while the blog is created, and the dark colour scheme.
- **Search performance summaries** on the Dashboard, Content Health and the editor are cached for five minutes per site and refreshed after each sync instead of re-aggregating eight weeks of rows on every load.
- **Success messages** across the Desk drop the exclamation marks and emoji.
- **Account pages follow the Desk colour scheme** — Sign in, Register, Forgot password and Reset password used their own light-only palette and ignored Settings → Colour scheme. The Desk tokens now live in one stylesheet (`css/desk-tokens.css`) shared by the admin layout and the account pages, so all of them honour the light, dark and system settings; the first-run setup screen drops its hard-coded navy card for the same tokens, and the Profile form shows the "Unsaved changes" pill and asks before navigating away like every other editor.
- **Calmer account-page copy** — "Invalid email or password." reads "That email and password don't match."; "Invalid email format." reads "Enter a valid email address."; a disabled account says what to do about it; "Sign In" is "Sign in"; the setup screen is titled "Complete your setup" with a "Save and continue" button.
- **Settings redesigned as tabs** — Admin → Settings is now seven tabs (General, Display, Comments, Search & analytics, Notifications & housekeeping, Social, Identity) with a purpose line per tab and a helper sentence under every field. All tabs post together; a sticky bar keeps **Save changes** and the unsaved-changes pill visible at every scroll position; a validation error opens the tab that holds it and the tab shows an error count; the tab is remembered in the URL hash; arrow keys move between tabs.
- **Landing page fonts are self-hosted** — the product landing page loaded Source Serif 4, Geist and Geist Mono from Google Fonts; it now uses the same self-hosted `fonts.css` as every other page, so no page makes a third-party request.
- **Setup wizard and first sign-in say Inkwell** — the last "BLGFRNT" codename strings in the setup wizard, its layout title, the admin layout fallback and the welcome message are gone.
- **Desk dialog default title** — a confirmation opened without a title now reads "Confirm this action?" rather than "Are you sure?".
- **`robots.txt` disallows `/search` and `/preview/`** — site search results and tokenised draft previews were already `noindex`; crawlers are now told not to fetch them at all. Admin pages additionally carry `noindex,nofollow`.
- **Icon-only controls have accessible names.** The eight dismiss buttons on notifications carry an `aria-label` and a tooltip, so screen readers no longer announce a bare "button". A test now fails the build if an icon-only button or link without a name is added anywhere in the views.
- **Editors warn before unsaved work is lost.** The post and page editors and the Settings form now track changes; once anything differs from what was loaded, an "Unsaved changes" pill appears next to the save buttons and leaving the page (closing the tab, Back, a sidebar link) asks for confirmation. Saving clears it. Autosave stores the body but does not clear the pill, because the title, meta and settings still need a real save.
- **Long actions show progress.** A thin progress bar at the top of the Desk runs for every form submission, htmx request and upload, and the button that was clicked disables with a spinner until the response arrives, so a slow import, sync or upload no longer looks like a dead click.
- **Admin tables scroll sideways on small screens** instead of clipping the Actions column; on phones the first column stays fixed so the row identity is visible while scrolling.
- **Search and paging on every list.** Pages, Categories, Tags, Series, Users, Import history and Redirects gain a search box and a pager (25 per page) through one shared component; the Media library gains a filename search that pages in SQL alongside its existing 48-per-page grid. Search terms are treated as text, never as patterns.
- **Desk confirmations, alerts and prompts no longer use the browser's native popups.** Sixteen destructive actions (delete post, page, category, tag, series, comment, media file, subscriber, user; remove redirect; unblock or un-allowlist an address; block an address; delete an import record; undo an import; reset theme; restore a revision) now open one styled dialog that names the item, states the consequence in a sentence, and offers the safer alternative where one exists — "add a redirect first", "return the comment to Pending instead". The two actions that undo many records at once, undo import and reset theme, require typing UNDO or RESET before the button enables. Upload failures and "URL copied" use the existing toast area instead of `alert()`, and the video-embed block in both editors asks for the URL in a dialog that validates it as you type instead of `prompt()`. The dialog is the native `<dialog>` element, so focus trapping, Escape and screen-reader semantics come from the browser; no library was added. A test scans the admin views so a native popup cannot return.
- **Data Protection keys are persisted to disk** — keys are now stored under `App_Data/keys` (or the folder named by `DataProtection:KeysPath` in `appsettings.json`), outside the web root, so signed comment-form tokens and any protected secret survive a redeploy or app-pool recycle instead of being silently invalidated.
- **Example configuration no longer suggests SQLite** — `appsettings.example.json` offered a SQLite connection string for a platform that only opens SQL Server connections. It now shows the SQL Server form.

### Fixed

- **An administrator can no longer disable their own account** — the Disable button is hidden on your own row and the server refuses the request.
- **Role changes no longer submit on every keyboard step** through the dropdown.
- **Typing a reply in Comments and then clicking another action** no longer silently discards the draft; the reply box is labelled and tracked.
- **The repeat-comment spam check is scoped to the post's owner**, so an identical comment on one tenant's site cannot block a genuine comment on another's.
- **Revision numbers are unique per post or page** — the number is assigned inside the insert under a range lock and backed by a unique index (`2026-09-19_add-unique-index-revisions-number.sql`), so two simultaneous saves cannot take the same number.
- **Desk replies record their author** — the reply's user id was set but never stored.
- **Disconnecting Search Console removes the stored performance rows** for that site instead of leaving them behind.
- **Comment spam checks use an index on the comment timestamp** (`2026-09-19_add-index-comments-createdat.sql`).
- **Dashboard Edit button** no longer renders white-on-dark in the dark scheme; **AI Crawlers** and the post editor's quote template no longer show mis-encoded dashes; the Analytics Excel link no longer carries two `href` attributes and "Save as image" respects the colour scheme.
- **Import preview and summary tables** scroll horizontally on narrow screens instead of overflowing the page.
- **The 404 page** no longer steals focus from its announcement or relies on a `javascript:` link; the post preview banner uses theme tokens.
- **Password fields on the sign-in and registration pages are real password fields** — both pages had rendered a plain text box driven by a script that masked characters itself and copied them into a hidden field, so password managers could not fill or save the password and the sign-in page asked the browser for a *new* password. Sign in, Register, Reset password and the first-run setup now use a native password input with the correct `autocomplete` (`current-password` / `new-password`) and an optional Show/Hide toggle.
- **Sign-in errors stay on the page and are announced** — "wrong password" and similar errors on the account pages appeared as a corner toast that removed itself after five seconds, was not announced to screen readers and was not tied to the field. They now render inline above the form as an alert that stays until the next submit and is linked to the first field; every label on the account pages is bound to its input. The Profile page, which never displayed its validation errors at all, now does.
- **Schedule times are read in your display zone** — the editor bound a typed schedule time straight to UTC, so an operator in a non-UTC zone got a post published hours late. Typed times are now converted from Admin → Settings → Display time zone to UTC on save and shown back in that zone in the editor.
- **The Desk has navigation on phones again** — the sidebar was hidden outright below 768 px, so the hamburger opened nothing. It now slides in with a named, keyboard-reachable toggle (Escape closes it).
- **One table of contents per post** — the older script-built "Contents" list and its progress bar still rendered beside the server-rendered "On this page"; the script version is removed.
- **Revisions are tenant-scoped** — listing, comparing and restoring a revision now always loads the post or page first, and in cloud mode refuses an entity that belongs to another tenant.
- **Returning readers count as views** — a conditional request answered with 304 no longer skips the view counter.
- **Comment-form tokens are bound to their post** — a token rendered on one post no longer validates on any other post.
- **Bulk publish pings IndexNow once** for the whole batch instead of once per post inside the request.
- **Data Protection key folder is checked for writability at startup**; a folder that exists but cannot be written now falls back with a dashboard warning instead of failing the first page that needs a token. A broken key ring also no longer takes a post page down; comments are rejected until it is fixed.
- **UTC conversion covers Redirects and Settings timestamps** — a follow-up script (`2026-09-17_convert-remaining-local-timestamps-to-utc.sql`) shifts the two columns the 2026-09-15 script left behind. README gains the 1.0.6 upgrade note and the `DataProtection:KeysPath` key.
- **Save buttons rendered as text on Settings and the Pages and Posts editors** — the 2026-09-16 build shipped four views whose Save / Save Draft / Publish button tag had been replaced by a literal `$1` while the "Unsaved changes" pill was added, so the button showed as raw class text and Settings could not be saved. Restored; a source-scanning test now fails on any `$1` left in a view.
- **Series editor guards unsaved changes** — both forms on Admin → Series → Edit (details and post order) now show the "Unsaved changes" pill and ask before navigating away, like every other editor.
- **Preview images have alt text** — the Open Graph, Twitter, logo, favicon, cover and avatar previews in the editors, Settings and Profile screens carry accessible names.
- **Status pills and the meta-description counter use theme tokens** instead of hard-coded colours, so they render correctly in the dark scheme.
- **README no longer claims newsletter sending or CSV import/export** — those were never built; the feature bullet now describes the subscribe flow and subscriber list that exist.
- **Publishing and visibility now run in UTC end to end.** Publish stamps, scheduled-post checks, "is it live yet" filters in the post, redirect and API paths, and comment, page, media and user timestamps were written in the host's local time while everything else used UTC. On a host behind UTC a freshly published post answered 200 at its URL yet was missing from the homepage, feed and sitemap until the local clock caught up, and the defect could not be reproduced on a machine whose offset matched. Every such call now uses UTC, a test fails the build if a local-time call returns to those paths, and `DBScripts/2026-09-15_convert-local-timestamps-to-utc.sql` shifts existing rows once the operator supplies the host's offset (a no-op at offset 0).
- **Scheduled jobs cannot run twice across processes** — the scheduler decided "due" by reading the `Jobs` ledger and then running, so two application instances sharing one database (an IIS web garden, two servers behind a balancer, or an admin-triggered tick overlapping the timer) would each run the same nightly job, including several simultaneous 16-month Search Console backfills. A job is now claimed with one atomic update on its ledger row and only the winner runs. When the ledger itself is unreachable the scheduler still skips rather than runs blind, but now writes a warning once per job (previously only a debug line), so a scheduler that is silently never running shows up in the log.
- **Search Console backfill stores each day in one bulk write** — the sync inserted one row per round trip, which turned the first 16-month pull into hundreds of thousands of statements, and it opened the connection a second time on a connection that was already open, which failed before any row was written. Each day is now bulk-copied inside the same transaction that clears it.
- **Password-reset links use the canonical host and the right site name** — the email built its link from the request's own scheme and host, which behind a TLS-terminating proxy produced an `http://` link to an internal host name, and read the site name from an owner key that holds nothing on a self-hosted install, so the subject and body fell back to the host name. The link is now built on the configured `CanonicalHost` (always `https`) when one is set outside Development, and the site name is read from the same owner Admin → Settings writes under.
- **CMS pages carry their own SEO title, description and canonical again** — every published Page (About, Contact, Editorial Policy and the like) was rendering a hard-coded empty meta description, ignoring its `MetaTitle` and `CanonicalUrl` fields entirely, so the Open Graph and Twitter cards were also blank. Discovered while publishing a new About page (`2026-09-24_publish-about-page.sql`, a content operation). A regression test picking a sample post from the sitemap by URL-path exclusion was also fixed to select by the sitemap's post-only `priority 0.8`, since it had started matching the new page instead of a post.
- **Admin → AI Crawlers loads without a full table scan** — `CrawlerVisitRepository` filtered every query with `(@OwnerId IS NULL OR OwnerId = @OwnerId)`, and the self-hosted deployment this site runs as always calls it with a null owner, so every load scanned the entire `CrawlerVisits` table instead of seeking the `(OwnerId, VisitedAt)` index; the three queries also ran one after another. The owner predicate is now only present in the SQL when an owner id is actually supplied, and the three loads run concurrently.
- **Publishing or updating a CMS Page now pings IndexNow** — `PostsController` has always notified IndexNow of a fresh URL on publish/update; `PagesController` never did, so every About/Contact/Editorial-Policy-style page was invisible to that pipeline. It now mirrors the Posts behavior.

### Security

- **Media upload validates the anti-forgery token again** — the attribute had been commented out; the library page renders the token and sends it with every upload, and upload failures no longer return the exception stack trace to the browser.
- **Self-disable guard and confirmed privilege changes** on the Users screen.
- **Comment spam protection** — public comment and reply submissions now pass a spam gate before anything is stored. The gate is structural rather than a word list: a hidden honeypot field that only form-filling software completes; a signed form-timing token proving the form was actually fetched and not submitted within seconds of rendering; rejection of HTML, BBCode and Markdown links and of comments carrying more than two links or nothing but a link; repeat detection, so an identical body posted again within 30 days under a different name is dropped; and a per-address limit of three comments an hour. Discarded submissions are never written to the database and receive the same "awaiting moderation" response a genuine comment would, so a sender cannot learn which rule fired. Each discard is reported to the IP Firewall with the same weight as a failed sign-in, so an address that keeps posting spam blocks itself. Signed-in editors are never filtered, a form left open for more than a day asks the reader to reload rather than treating them as spam, and the filter fails open: no rule can turn a comment into a server error. Requires no configuration and works alongside the optional reCAPTCHA check.

---

## [1.0.5] — 2026-09-05

### Added

- **Answer-engine readiness score** — every post now carries a 0–100 score for how ready it is to be *quoted* by an answer engine, shown in the editor sidebar with a per-signal breakdown and in Admin → Content Health as a column, a tile and a filter. The structured-data linter says what is *broken*; this says what is *absent*: the answer capsule, Key Facts, FAQ, meta description, section headings, internal links, freshness dates, depth and feature image, each weighted by how much it contributes to being cited and each carrying concrete advice rather than a bare number. The answer capsule carries the most weight because it is the passage an engine actually lifts. Advisory only — it never blocks a publish, because a short note that scores low may be exactly right and forcing every post into one shape would produce filler. Nothing unobservable is scored: there is no accuracy or authority judgement and no invented ranking prediction. The dashboard counts headings and links in SQL rather than loading article bodies, and the editor counts the same tokens, so the two screens cannot disagree about the same post.
- **Publisher identity settings for the knowledge graph** — Admin → Settings now carries the entity fields search and answer engines use to work out *who* a publication is: publisher type (Organization or Person), legal name, founder, founding date, authority profiles (Wikipedia, Wikidata, a company register, an ORCID), and an identity statement written for answer engines. Profiles join the existing social links in `sameAs`, the facts become `legalName` / `founder` / `foundingDate` on the publisher node, and the statement is published verbatim in the Identity section of `llms.txt`. This matters most on a domain that previously published something else, which otherwise keeps answering to its former identity. Every field is optional and nothing is inferred: an unsupplied founder or an unparseable date is omitted from structured data rather than guessed at or emitted blank, an unsupported publisher type falls back to Organization, and a profile that is not an absolute http(s) URL is dropped rather than allowed to invalidate the node. Blogs that never open Settings are unchanged and still emit a generated identity statement.
- **Redirects manager with a triaged 404 worklist** — a new Admin → Redirects screen manages the redirect table that until now could only be edited in SQL. Rules can be listed, created, changed and removed, with 301, 302 or **410 Gone**; rules written automatically by post renames and imports are finally visible. Alongside it, the 404 log becomes a worklist. Most 404s on a public site are vulnerability scanners, and tools that list all of them make the operator do the sorting by hand — so this asks instead for evidence a 404 is reader-facing: either another page links to it, or it is close enough to a real published slug to be a rename, a typo, or a stale external link. Those are ranked first with a one-click **301 to the suggested post** or **410 to retire it**; everything else is collapsed out of the way, since repeated probing is the IP firewall's business. Rules that would loop, point at a URL that redirects again, or target a URL already retired with 410 are refused with the real destination named, so the manager cannot manufacture the chains Link Audit exists to report. Admin-only, anti-forgery protected, and every change is audited.
- **AI-crawler analytics** — a new Admin → AI Crawlers screen reports which AI answer engines and search crawlers actually fetch the site, how often, and which pages they read. Search Console's generative-AI report gives impressions only, never names the engine, and is still rolling out; this traffic meanwhile already passes through the site every day and was discarded, because bot hits must not pollute human analytics. Recorded to its own table so existing visitor numbers are untouched, and every engine Inkwell can name is listed **including those with no visits** — "no AI engine has read this site" is the single most useful finding the page can report, and it is only visible if absent crawlers still appear. Shows a daily trend, the pages drawing the most AI attention, GPTBot, ClaudeBot, PerplexityBot, Google-Extended, Applebot-Extended, CCBot, Bytespider and the rest with the operator's published purpose for each, and separates AI engines from ordinary search crawlers. A user-agent is self-asserted, so the report states that it shows claimed identity; it is never used to allow or deny a request.
- **Import dry-run** — the import wizard now offers **Preview first** alongside Start import, showing exactly what a WordPress or Ghost file would do before anything is written: how many posts and pages would be created, how many would be given a suffixed slug because of a clash, how many would **overwrite** existing content, what would be skipped and why, and how many images would be fetched. It also reports **links that would not resolve** — internal links in the imported content pointing at posts the file does not bring and that do not already exist here. Overwrites are called out prominently, because that is the one outcome that destroys existing work. The preview is read-only by construction: it reads the staged file through a separate analyzer rather than the importer, so a preview has no code path that could persist anything. It predicts rather than simulates, and says so — images are counted, not downloaded.
- **Answer capsules** — a post can now carry a short direct answer, written to be quoted, shown above the article body and emitted as schema.org `abstract` on the BlogPosting. Answer engines and featured snippets lift passages rather than pages, so a post that reaches its answer three paragraphs down gives them nothing clean to take; the capsule is the sentence that does. It is visible copy, never hidden text, so the same words serve the reader. Written in the editor sidebar with a 40-60 word target, and the structured-data linter warns when a capsule is too short to be an answer or too long to be quoted whole. Posts without one are unaffected.
- **Internal-link suggester in the editor** — the post editor sidebar now proposes links in both directions while you write: published posts this one should link to, and posts that should link back to it. It also shows how the post stands against the two-in / two-out minimum, and flags it as an orphan when nothing links to it. Two kinds of suggestion, each with its reason shown: a post this one already names but has not linked (the link a reader expects), and posts sharing topics. Suggestions are ranked, never applied — a mention outranks a topic match, and nothing already linked is proposed again. Loaded on demand, so opening the editor costs nothing extra.
- **Link Audit** — a new Admin → Link Audit screen scans every published post and page for internal links that dead-end, point at a URL retired with 410, or take an avoidable redirect hop. Broken links and links to retired URLs are separated from redirect hops, because they cost different things: a dead end wastes the reader and the crawl budget spent following it, while a hop only spends link equity. Multi-hop chains are called out separately so the final destination can be linked directly. Generated routes (tag, category, author, uploads) and external links are out of scope, so the report contains faults rather than false positives. Read-only; Editor and Admin roles.
- **Pre-publish structured-data linter** — every post is validated before it goes live, and a post whose structured data would not validate is saved as a draft instead of published. It catches the failure that was previously invisible: a malformed FAQ, Key Facts, How-To or roundup block is swallowed by the renderer and emits nothing, so a broken block looked identical to a missing one. It also enforces the rules that matter for search: every roundup entry must link the vendor real site with an absolute URL, the internal review path must stay in `ctaUrl` rather than the vendor URL, scores must sit on the 0-10 scale, and a first-party product may be featured but never carries a self-authored score. Advisory issues (a thin FAQ answer, a duplicate label, a single-step How-To, duplicate ranks) surface as warnings and never block publishing.
- **Topic index in `llms-full.txt`** — the AI content index now carries a `## Topics` section alongside the existing category grouping. A post sits in one primary category but carries several tags, so the category view alone hides the site's secondary taxonomy; the new section lists each topic with its archive URL, its article count, and the titles it covers, giving answer engines the cross-cutting view of which articles address the same subject. Only topics deep enough to have an indexable archive are listed, so the file never points a crawler at a page that answers `noindex`. Generated per tenant from that blog's own published posts; blogs with no tags are unaffected.

### Changed

- **Category and tag links go straight to the canonical archive** — selecting a single category or tag from the home-feed filters now links directly to `/category/{slug}` or `/tag/{slug}` instead of the query-string form that 301-redirects there, so an in-page link no longer spends a redirect hop. Multi-value filter combinations, filters combined with a search term, and the toggle-off behaviour are unchanged.
- **Page titles no longer overflow the search-result budget** — the `" | Site Name"` brand suffix is now appended only when the finished title still fits inside roughly 60 characters, the width Google renders before truncating. Previously the suffix was added unconditionally, so a 50-character title on a blog called "The Independent Optometry Technology Review" rendered at 96 characters and search engines cut the end off — losing the part that distinguishes the page ("2026", "Compared", "FDA-Cleared") while keeping boilerplate that the result already shows as the site name. Titles that already contain the site name are unchanged, as are category, tag and series archives.
- **Multi-tag filter links from an archive no longer create duplicate URLs** — selecting a second tag while on `/tag/{slug}` (or a second category on `/category/{slug}`) built a relative `?tags=a,b` link, which resolved against the archive route as `/tag/{slug}?tags=a,b`. The archive ignores that query string, so the second selection was silently dropped and every chip minted a crawlable near-duplicate of the archive. Those links are now rooted at the home feed (`/?tags=a,b`), where multi-value filtering actually runs. The home feed, single-value links, toggle-off and archive pagination are unchanged.

### Fixed

- **Unknown category and tag URLs return 404** — `/category/{slug}` and `/tag/{slug}` previously answered 200 for any slug at all, rendering an empty archive titled with the raw slug. Every invented URL was therefore a soft-404 that search engines had to crawl and discard. Both routes now check the taxonomy exists first, matching how `/author/{slug}` already behaved. A category or tag that exists but holds no published posts still renders (and stays `noindex`).
- **Empty categories and tags are no longer offered as filters** — the public filter chips listed every category and tag in the database, including those with no posts and those whose only posts were drafts, so readers and crawlers were sent to archives that render empty. The reader-facing lists now count published posts only. Admin screens are unchanged and still show drafts and empty taxonomies.
- **Tag archives held up only by drafts no longer reach the sitemap** — the sitemap's "enough posts to be indexable" test counted unpublished posts, so a tag with three drafts and nothing published could be advertised while its archive answered `noindex`. The count is now published-only, matching what the archive itself does.
- **Archive pagination no longer repeats the facet in the query string** — page 2 of `/tag/{slug}` linked to `?page=2&tags={slug}`, restating in the query a filter the route already carries and creating a second URL for the same listing. Archive pagers now emit `?page=2` alone; home-feed pagination still carries its active filters.
- **A fresh install from `DBScripts/init.sql` now produces a complete schema.** The file describes itself as the complete schema and the README offers it as the manual setup path, but it was stamped `v1.0.1 (2026-06-27)` and was missing three tables (`ErrorLogs`, `IpFirewallRules`, `CrawlerVisits`) and four columns (`Posts.KeyFactsJson`, `Posts.HowToJson`, `Posts.AnswerCapsule`, `Redirects.StatusCode`) — so anyone installing from it got a database the application fails against. It went unnoticed because `MigrationService` repairs the schema on startup and therefore hides the drift on every existing installation; only a first-time install was affected. Verified by building a database from `init.sql` alone against an empty server and diffing `sys.columns` against a working installation in both directions: zero differences. New columns are applied through a top-up section at the end of the file, so databases created by an older copy are upgraded rather than left behind.
- **README no longer claims revision history.** The Features list advertised "Page Revisions — revision history for posts and pages". No such capability exists: `Revision.cs`, `IRevisionRepository` and `RevisionRepository` are present and registered in DI, but nothing ever calls them, `MigrationService` never creates the `Revisions` table, and the table is absent from the database. There is no UI. Per the docs-follow-code rule the claim has been removed rather than code written to make it true; real revision history is a separate, explicitly-scoped feature on the roadmap.
---

## [1.0.4] — 2026-08-15

### Added

- **Content Health dashboard** — a new Admin → Content Health screen turns the maintenance fields that already exist on every post into a worklist: reviews that are overdue or due within 30 days, posts never verified, posts missing a Key Facts or FAQ block, meta descriptions that are absent or long enough to be truncated in search results, and titles still promising a year that has passed. Tiles double as filters, each row links straight to the editor, and the default view shows only what needs attention. Read-only — it surfaces work, it never edits. Available to Editors and Admins.
- **Filter-URL canonicalization** — a single-value home-feed filter (`/?tags=cloud-based-ehr`, `/?category=practice-management`) now issues a 301 to the matching `/tag/{slug}` or `/category/{slug}` archive, so filter permutations stop competing with the canonical archive as separate indexed URLs. Multi-value combinations have no canonical equivalent and are unchanged (`noindex,follow`), search results are untouched, and pagination is preserved through the redirect. Only well-formed single slugs redirect, so a crafted query string cannot be turned into a redirect target.
- **Tag archives are indexable once they have depth** — a tag archive holding at least three posts is now a real topic page instead of being unconditionally `noindex`; thinner ones stay out of the index as before. `sitemap.xml` lists only the archives that are actually indexable, so it no longer advertises `noindex` URLs.
- **Retire a URL with 410 Gone** — redirect rules now carry a status code, so a URL can be moved (301, the default), temporarily moved (302), or **intentionally retired (410 Gone)**. A 410 serves a dedicated "no longer available" page that points readers at current content, and tells search engines the URL was removed on purpose — which drops it from the index far faster than a 404, whose repeated re-crawling keeps stale titles and snippets in search results. Existing redirect rules are unaffected and continue to serve 301.
- **IP Firewall — automatic blocking of hostile addresses** — every failing request is now weighed by a threat scorer, and addresses that cross the threshold block themselves. Exploit probes (`.php`, `wp-admin`, `.env`, `.git`, path traversal, SQL-injection payloads, app-server consoles) score 5 points, a rejected sign-in scores 3, and an ordinary 404 scores 1; at 10 points within 10 minutes (both configurable) the address is blocked — 24 hours on the first offense, a week on the second, permanently on the third — and every subsequent request is refused with a bare 403 before routing, views, or logging run. A new **Admin → Security** screen shows active blocks with their reason and denied-request count, a live watchlist of addresses currently building a score, and the thresholds, allowlist, and notification settings; addresses can also be blocked or allowlisted by hand, and **Admin → Error Monitor** now records each signature's last client IP with a one-click Block button. Blocks are stored in a new `IpFirewallRules` table so they survive a restart, while enforcement reads an in-memory snapshot so an ongoing attack costs no per-request database work. Safe by default: loopback and private ranges are never blocked, signed-in staff are never scored, search and AI crawlers (Googlebot, Bingbot, GPTBot, ClaudeBot, …) are never blocked for 404s, proxy headers are ignored unless a trusted proxy is declared, and the firewall fails open if the database is unreachable. Optional email alerts reuse the error-notification recipients. Every block, unblock, allowlist entry, and settings change is written to the audit trail.
- **WordPress & Ghost content importer** — migrate an existing blog from Admin → Import. Upload a WordPress WXR (`.xml`) export or a Ghost JSON (`.json`/`.zip`) export, preview what it contains, choose what to bring in (posts, pages, categories/tags, images, comments; published-only filter; slug-conflict policy), then run the import with a live progress bar and a per-item exception log. Posts, pages, categories and tags are created; referenced images are downloaded, converted to WebP and re-linked in the content; old permalinks become redirects to preserve SEO; imported HTML is sanitized. Ghost bodies use the rendered HTML when present, with a best-effort Lexical/Mobiledoc conversion otherwise; for a Ghost `.zip` that bundles the `content/images` folder, images are read straight from the archive (no live source site required). The import is batched and resumable, finishes with a summary and a downloadable `.xlsx` report, and can be **undone** (removes the posts, pages, images and comments it created; categories and tags are kept). Step-by-step export instructions for both platforms are shown in the importer. Admin-only; new tables only (backward-compatible).
- **Series & collections** — group posts into an ordered reading sequence (e.g. a multi-part guide). A new Admin → Series area creates series and manages their posts and order (add, reorder, remove). Each series gets a public page at `/series/{slug}` listing its posts in order, plus a `/series` directory of all series. Posts that belong to a series show an in-article "Part N of M · Previous / Next" navigation banner. Series pages emit `CollectionPage` + ordered `ItemList` + `BreadcrumbList` structured data and are included in `sitemap.xml`. Backward-compatible: new tables only, and blogs that never create a series are unaffected.

### Changed

- **Shared image pipeline** — the media library's automatic WebP conversion is now a shared service reused by the content importer, so both direct uploads and downloaded/imported images are optimized through one code path.

### Fixed

- **HEAD requests no longer return 405 Method Not Allowed** — routing matches HTTP methods exactly and every page route is declared as GET-only, so every HEAD request was refused: `HEAD /`, `HEAD /robots.txt`, even HEAD for a URL that does not exist (405 instead of 404). Uptime monitors, link checkers and some crawlers use HEAD precisely because it is cheap, and the refusals also filled the Error Monitor with 405 rows that resembled attack traffic. HEAD now returns exactly what GET would — same status, same headers, no body. Static files were already correct and are untouched, HEAD requests are excluded from page-view analytics, and the Error Monitor records them as HEAD rather than GET.
- **Category and tag archives render the full listing chrome again** — they share the home-feed view but never populated its filter chips, popular-posts sidebar, or the tenant's configured layout, so those pages silently fell back to the default layout with no filters. All three listing routes now populate the same view data, and an archive seeds its own filter selection so adding a second filter from an archive page keeps the first.
- **Post pages no longer 500 when a post has no categories or tags** — the related-posts query built an invalid `ORDER BY (0)` (a bare integer that SQL Server treats as a column ordinal) for posts with no topic overlap, throwing a `SqlException` and returning HTTP 500 on the article page. It now falls back to recency ordering. Affected any category-less/tag-less published post (e.g. freshly imported or bulk-loaded content).

---

## [1.0.3] — 2026-07-31

### Added

- **Automatic WebP conversion on upload** — raster images (JPEG/PNG/GIF) uploaded to the media library are re-encoded to WebP for smaller, faster-loading assets, and their dimensions are captured in the same pass. SVG and images already in WebP pass through unchanged; if an image can't be decoded, the original bytes are stored as a fallback. Completes the media-library image-management capability.

---

## [1.0.2] — 2026-07-29

Public release: a full SEO / AEO (AI-search) overhaul, the **Verdict** scored-roundup content system, new blog **layouts**, **IndexNow** instant indexing, author **E-E-A-T** pages, and an **Error Monitor** subsystem. Backward-compatible — existing tenants keep their layout, theme, and content unchanged.

### Added

- **Verdict roundup theme** — a scored "Top N Best … Software" comparison format for buyer's-guide posts. Adds the `Verdict` layout (index, post card, single-post template) and a `clinic` Inkwell preset (white / near-black / blue CTA, all-sans headings). A post renders as a Verdict roundup whenever it carries roundup data, independent of the site's `layout-post` setting.
- **`Posts.RoundupJson`** column (nullable, JSON blob mirroring the `FaqJson` pattern) storing per-entry rank, score, weighted sub-scores, best-for, standout, pros/cons, CTA, and logo. Authored via a repeater UI in the post editor (shared by Create and Edit).
- **Roundup structured data** — `ItemList` → `SoftwareApplication` per entry with `Review`, `reviewRating`, `positiveNotes`/`negativeNotes`, emitted alongside the existing Article/Breadcrumb/FAQ JSON-LD. Makes "best-of" pages eligible for rich results and AI-answer citation.
- **Internal-aware roundup CTAs** — entry links that point at an internal review (e.g. `/eyefinity-ehr-review`) render as in-tab dofollow links to build topic clusters; external vendor links stay `nofollow noopener sponsored`.
- **Roundup entry `website`** — each roundup entry carries the product/company's real URL, rendered as an outbound "Visit website" reference (`rel="nofollow noopener"`) and used as the `SoftwareApplication.url` in the `ItemList`/`Review` JSON-LD. Governed by the **Content External-Link Rule** in `CLAUDE.md`.
- **First-party roundup disclosure** — a roundup entry whose website is the operator's own domain (e.g. `opto-soft.com`) is flagged as first-party: it shows an "Our platform" tag and an on-page disclosure, and is deliberately omitted from the `Review`/`AggregateRating` structured data (still listed in the `ItemList` as a `SoftwareApplication`), preventing self-serving ratings that violate search-engine rich-result policy.
- **Catalog layout** — a single-column, boxed-card blog index with left-aligned thumbnails, a horizontal category filter bar ("All Posts" + per-category tabs), and numbered pagination, matching a modern SaaS/company-blog style. Selectable from Admin → Theme (Modern family); navbar/footer map to the Neutral variants, and unknown/legacy layout values fall back to Neutral.
- **Error Monitor** — a global exception handler + status-code logging records every 5xx and 4xx, grouped by signature (status + normalized path + exception type) so repeated/similar errors collapse into a single counted row. A new Admin → Error Monitor page (Admin-only, read-only) lists groups with occurrence counts, first/last-seen, path, exception type/message, and summary tiles, with status/keyword filters. Backed by a new `ErrorLogs` table.
- **Error alert emails** — optionally email one or more recipients (Admin → Settings) the first time a new server-error (5xx) signature is detected; repeats and 404 noise never email. Uses the existing SMTP settings; a no-op when disabled or unset.
- **IndexNow instant indexing** — Admin → Settings can enable IndexNow and set (or auto-generate) an API key. When enabled, publishing/updating a post pings `api.indexnow.org` so Bing, Yandex, Seznam, and Naver re-crawl within minutes. The ownership key is served automatically at the site root `/{key}.txt` (root placement authorizes all URLs per the protocol). Per-tenant, disabled by default, best-effort — never blocks a publish.
- **Author pages with E-E-A-T structured data** — each author gets a public page at `/author/{slug}` listing their published articles and emitting `Person` JSON-LD (name, job title, credentials, bio, linked profiles) plus an `og:profile` card. Post bylines link to it; author pages are in the sitemap.
- **Automatic author slugs** — every user is assigned a unique URL slug (new users on creation, existing users backfilled once on startup), with an editable "Author Page URL" field in the profile editor.
- **`llms-full.txt`** — an expanded companion to `llms.txt` at `/llms-full.txt` indexing every published article (title, URL, one-line summary) grouped by category, so AI answer engines can map the full breadth of a site's content.
- **CollectionPage structured data** on listing pages (home, category, tag), tied to the site's `WebSite` entity.
- **Key Facts / "At a glance" block** — a post-editor field (label/value repeater) stores quotable facts (`Posts.KeyFactsJson`), rendered as a semantic definition list near the top of the article and highly extractable by AI answer engines.
- **How-To step-by-step guides** — a post-editor field (`Posts.HowToJson`) captures ordered steps, rendered as a visible numbered list and emitted as `HowTo` structured data.
- **Configurable content language** — a `Content Language` setting (BCP-47, e.g. `en`, `es`, `hi`) drives the page `lang` attribute, `og:locale`, `hreflang` (self + `x-default`), and schema `inLanguage`.
- **Entity/knowledge-graph signals** — `Organization` JSON-LD advertises topics of expertise (`knowsAbout`, from categories), and `llms.txt` includes an explicit identity section (exact name, official URL, citation guidance, disambiguation note).
- **Search-engine site verification** — Admin → Settings accepts Google Search Console and Bing Webmaster verification codes, rendered as `<meta name="google-site-verification">` / `<meta name="msvalidate.01">`.
- **Sitelinks search box** — the `WebSite` structured data includes a `SearchAction`, backed by a dedicated `/search?q=` route.
- **Image sitemap** — the sitemap lists each post's feature image (`<image:image>`).
- **Meta-description length helper in the editor** — a live character count with green/amber/red guidance for the 120–158 character range.
- **`WebSite` structured data** and enriched **`Organization`** JSON-LD (stable `@id` plus a full `sameAs` list from all configured social links) on every public page.
- **Universal page metadata** — canonical URL, `author`, `theme-color`, and default Open Graph / Twitter Card tags render on all public pages (home, index, category, tag, CMS pages), not just posts.
- **AI-crawler discovery hint** — `<link rel="alternate" type="text/plain" href="/llms.txt">` in the page head.
- **Richer article meta** on post pages: `og:url`, `og:image:alt`, `og:locale`, `article:author`, `article:section`, `article:tag`, `article:modified_time`, `twitter:site`, `twitter:creator`, `twitter:image:alt`.
- **`docs/plans/SEO-AEO-PLAN.md`** — a living SEO/AEO audit, prioritized action plan, and progress tracker.

### Changed

- **Precompiled Tailwind CSS on the public site** — the public frontend serves a small static stylesheet instead of compiling Tailwind in the browser on every page load, eliminating the largest render-blocking script and the flash of unstyled content. Runtime theming (presets/custom colors) is unchanged; regenerate with `npx tailwindcss` or `-p:BuildTailwindCss=true`.
- **Faster first paint** — Highlight.js and Lucide now load only on pages that need them (code blocks / `data-lucide`), removing render-blocking scripts from the common text-only article path.
- **Critical fonts preloaded** — the two brand fonts (Geist, Source Serif 4) are preloaded, with a `preconnect` to the analytics origin when GA is configured.
- **Optimized article images** — feature images get `fetchpriority=high` for LCP while related-post thumbnails lazy-load.
- **Per-tenant `llms.txt`** — the `/llms.txt` summary is generated from each blog's own name, description, and categories.
- **Smarter related posts / stronger internal linking** — "See Also" links and related-post cards are ranked by topical relevance (a shared category outweighs a shared tag) instead of pure recency, and the module always fills (recency backfill) so every article carries internal links.
- **Richer home-page meta description** — the home listing emits a fuller, keyword-relevant description built from the site's own tagline and top categories (truncated to ~158 chars); tenant-neutral.
- **Consistent page titles** — every public page ends with a single ` | {Site Name}` brand suffix (added when missing, never doubled).
- **Cleaner auto-generated descriptions** — a missing summary is cut on a word boundary with an ellipsis instead of mid-word.
- **Thin/duplicate pages kept out of the index** — tag archives, on-site search, and paginated/query-filtered listings emit `noindex,follow`.
- **Sitemap no longer capped at 1000 posts** — split across a sitemap index and chunked child sitemaps.
- **Roundup ratings made policy-safe** — entries carry only their genuine editorial `Review`, not a self-serving per-entry `AggregateRating`.
- **Sitemap** now includes category, tag, and published CMS page URLs, and stamps the homepage `lastmod` from the most recent post update.
- **`robots.txt`** AI-crawler allow-list expanded (OAI-SearchBot, ChatGPT-User, Claude-Web, Perplexity-User, Bingbot, DuckDuckBot, Amazonbot, cohere-ai, Meta-ExternalAgent); `Bytespider` added to the blocklist.

### Fixed

- **`llms.txt` / `llms-full.txt` / `sitemap.xml` / `robots.txt` are fully dynamic** — a stale static `wwwroot/llms.txt` could shadow the per-tenant dynamic route (serving one hardcoded, inaccurate AI summary to every tenant). The app now deletes any such stale static SEO file on startup so the correct dynamic route always serves.
- **Absolute `og:image` / `twitter:image`** — relative `/uploads/...` image paths are promoted to fully-qualified URLs on every public page, so social/AI scrapers that reject relative paths work.
- **Inbound mixed-case URLs now 301 to lowercase** — `/Best-Optometry-EHR-Software-2026` permanently redirects to its lowercase canonical instead of serving a duplicate 200 (static assets and non-GET requests untouched).
- **Sensible default database connection** — running without a configured `DefaultConnection` falls back to SQL Server LocalDB (the data layer is SQL Server only); the unused SQLite driver dependency was removed.
- **Build/publish no longer breaks on missing feature images** — the `Blog.Web.csproj` feature-image list is now a self-healing glob instead of ~34 pinned paths.
- **Category and tag pages had blank titles/descriptions** — they now render a proper `<title>` and meta description derived from the category/tag name.
- **`robots.txt`** used the invalid token `Googlebot-Extended`; corrected to Google's actual AI-grounding token **`Google-Extended`** so Google AI/Gemini permissions are honored.
- Post pages no longer emit a duplicate `<link rel="canonical">`; the canonical is emitted exactly once by the shared layout.

---

## [1.0.1] — 2026-06-27

### Added

#### Analytics Dashboard
- `PageViewMiddleware` — fire-and-forget page view tracking using root `IServiceProvider` scope (never blocked by request lifetime)
- `PageViews` table with UTM parameters (`source`, `medium`, `campaign`, `term`, `content`), IP hash, user agent, country, and region columns
- `VisitSourceClassifier` — classifies traffic into Organic, Direct, Social, Email, Paid, and Referral buckets
- `AnalyticsController` — admin-only dashboard at `/admin/analytics` with daily/weekly trends, top pages, top referrers, and traffic source breakdown
- `AnalyticsDashboardViewModel` — structured view model for the analytics page
- Geo-location resolution (country + region) from IP address

#### Audit Trail
- `AuditLogs` table with full schema: action, entity type/id/name, before/after JSON snapshots, user info, IP address
- `IAuditRepository` + `AuditRepository` — Dapper implementation with paginated, filterable queries
- `AuditService` — thin service that resolves request context (owner, user, IP, UA) and logs asynchronously; swallows all exceptions so audit never crashes the caller
- `AuditActions` static constants class — 33 `EntityType.Verb` string constants (e.g. `Post.Published`, `Comment.Deleted`, `Auth.LoggedIn`)
- `AuditController` — admin-only, read-only at `/admin/audit`; supports filter by entity type, action, user, date range; CSV export up to 10,000 rows
- Audit Trail view — color-coded action badges, expandable before/after JSON diff rows, pagination
- Audit nav link in `_AdminLayout.cshtml` (Admin-role gated)
- **All 12 admin controllers wired**: PostsController, CommentsController, CategoriesController, TagsController, MediaController, PagesController, UsersController, AccountController, SettingsController, ThemeSettingsController, NewsletterController, SubscribersController

#### Members & Newsletter
- `Members` table and `MemberRepository` — member directory with label-based segmentation
- `MemberLabels` and `Labels` tables for subscriber segmentation
- `Newsletters`, `Emails`, `MemberNewsletters` tables for newsletter campaign tracking
- `NewsletterController` and `SubscribersController` — compose/send newsletters, manage subscribers, CSV import/export
- `SmtpEmailService` — built-in SMTP transactional email

#### Redirects
- `Redirects` table with source path, destination URL, HTTP status code, and hit count
- `RedirectRepository` and `RedirectsController` — full CRUD with rule-based redirect middleware

#### SEO & Content
- `OgImageController` — programmatic OG image generation at `/og-image/{slug}`
- `FaqJson` column on `Posts` — JSON-LD FAQ schema stored per post
- `LastVerifiedAt` / `NextReviewAt` columns on `Posts` — clinical review workflow for medical content
- `OptometryTaxonomySeeder` — seeds 200+ optometry-specific categories and tags
- `LandingController` — dedicated landing page route

#### User Profiles
- `Credentials`, `Specialty`, `LicenseNumber` columns added to `Users` — professional author credentials
- `AvatarController` — dedicated avatar upload/management endpoint
- Profile update now refreshes cookie claims immediately

#### Media
- `OriginalFileName` column on `Media` table — tracks the uploaded filename before randomization

#### Theme System (Inkwell)
- 10 curated Inkwell color presets seeded via `SeedDefaultsAsync`
- Magazine, Grid, Minimal, Neutral, Classic, and Modern layout variants
- `CustomThemeSettings` table — per-owner key/value CSS variable overrides
- `ThemeSettingsController` — preset application, layout presets, reset; all changes audited
- Theme backward-compatibility guards in all ViewComponents (unknown values fall back to safe defaults)

#### Security
- `ReCaptchaService` — Google reCAPTCHA v2 validation on public forms
- `IEmailService` / `IRedirectRepository` interfaces added to `Blog.Core`

### Fixed

- **PageView fire-and-forget scope bug** — `TrackAsync` previously used `context.RequestServices.CreateScope()` which was disposed before the async task ran, silently swallowing all view recordings. Fixed by pre-capturing all HTTP context values synchronously and switching to `_rootServices.CreateScope()`.
- **Admin page views counted** — Admin requests were being tracked; `PageViewMiddleware` now checks `context.User.Identity.IsAuthenticated` and skips authenticated users entirely.
- `BlogController` operator precedence — `!User.Identity?.IsAuthenticated == true` replaced with the clearer `User.Identity?.IsAuthenticated != true`.

### Changed

- Upgraded target framework from `.NET 8` to `.NET 10`
- `MigrationService` expanded to auto-create: `CustomThemeSettings`, `Members`, `Labels`, `MemberLabels`, `Newsletters`, `Emails`, `MemberNewsletters`, `Redirects`, `Snippets`, `PageViews`, `AuditLogs`, and all associated indexes and column additions
- `InfrastructureServiceExtensions` registers all new repositories (`IAuditRepository`, `IMemberRepository`, `IPageViewRepository`, `IRedirectRepository`)
- `Program.cs` registers `AuditService`, `SmtpEmailService`, `ReCaptchaService`, and `IHttpContextAccessor`
- README rewritten to reflect current stack (.NET 10, Dapper, no EF Core) and all new features

### Database Scripts (DBScripts/)

| Script | Change |
|---|---|
| `2026-05-17_create-pageviews-table.sql` | PageViews table |
| `2026-05-22_add-country-region-to-pageviews.sql` | Country + Region columns + index |
| `2026-05-22_add-verified-review-dates-to-posts.sql` | LastVerifiedAt + NextReviewAt columns |
| `2026-05-23_add-inkwell-preset-theme-setting.sql` | No schema — seeded at runtime |
| `2026-05-24_create-missing-tables-live.sql` | Backfill for live environments missing tables |
| `2026-05-24_reset-pageviews-fresh-start.sql` | One-time data reset (manual run only) |
| `2026-05-29_create-audit-logs-table.sql` | AuditLogs table + 3 indexes |

---

## [1.0.0] — 2026-05-15

### Added

- Initial release of the Blogfront platform
- ASP.NET Core MVC blog engine with Dapper data access
- Cookie-based authentication with PBKDF2-SHA256 password hashing
- Role-Based Access Control (Admin, Editor, Author)
- Post creation, editing, scheduling, and publishing
- Media library with secure upload and organized storage
- Categories and Tags management
- Comments with moderation
- Basic theme customization
- Multi-tenant `ITenantContext` support (cloud mode)
- SQL Server schema auto-migration on startup
