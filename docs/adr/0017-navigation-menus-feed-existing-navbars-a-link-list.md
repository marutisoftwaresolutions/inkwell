# ADR-0017: Navigation menus feed the existing navbars a link list; no menu means exactly the old links

- **Status:** Accepted (2026-09-27)
- **Date:** 2026-09-27
- **Supersedes:** —
- **Related:** `.gsd/ROADMAP.md` v1.0.8 core "Navigation menu manager"; `.claude/rules/theme-backward-compat.md`; CHANGELOG `[Unreleased]`

## Context

Header links came only from pages marked "Show in navigation". A site could not link a category,
tag, series or outside address from its header, and it had no footer links at all. Menus are core
in WordPress, Joomla, Drupal, Dotclear and Serendipity. Two things constrained the design:

- **The theme backward-compatibility rule.** Every existing site must render unchanged until its
  owner builds a menu.
- **Eight navbar variants and six footer variants,** each with its own markup.

## Options considered

1. **Rewrite each navbar to render a menu model.** This means 14 bespoke rewrites, each a chance to
   change a site's header without anyone asking.
2. **Render the menu from a shared partial, replacing each variant's link markup.** It loses every
   variant's styling.
3. **Give the existing views a link list.** Each navbar keeps its markup and swaps only its link
   source and `href`: `Model.Links` instead of sorted pages, `@link.Href` instead of `/@page.Slug`.
   With no menu, the list is built from nav pages exactly as the views built it
   (`NavigationMenu.FromPages`), so output is unchanged. Footers get one shared, optional row.

## Decision

Option 3.

**Storage.** A `MenuItems` table: location (`header` or `footer`), order, an optional parent (one
level), a label, and a link type:
- `page`, `category`, `tag`, `series`: by **id**, so renaming a slug never breaks a menu;
- `home`: footer only, because every navbar theme already has its own Home link;
- `custom`: a site path or an http(s) URL. `javascript:`, `data:`, `//host` and backslashes are
  refused.

Items are stored under the site's branding owner, the same owner the public layout passes to the
navbar and footer and uses for theme settings: the tenant in cloud mode, otherwise the first admin.
This differs from analytics and webhooks, which use `Guid.Empty` for a self-hosted site, because
menus are read where the theme is read.

**Rendering.**
- `MenuService` resolves a menu in one query (left joins to the target, skipping gone or
  unpublished content and the children of a skipped parent). It caches the result for 60 seconds per
  site, cleared on save, and fails open: an error returns "no menu", and the navbar shows its
  pre-menu links.
- **Navbar:** a header menu with at least one resolvable item replaces the nav-page links. Bars that
  show top-level links only (Neutral, Default, Feed) filter on depth 0. Dropdowns and phone menus mark
  children with a "— " that is hidden from screen readers; link titles themselves stay clean.
- **Footer:** the `_FooterMenu` partial renders a `<nav aria-label="Footer">` row above the copyright
  line only when a footer menu exists, with nested items as a list under their parent. The Magazine
  footer, drawn on the brand colour, passes an "inverse" tone so the links keep their contrast.

**Desk.** Admin → Menus (`CanManageThemes`) has one editor per menu, posted as a whole, in display
order, with a per-row child flag. Labels are at most 40 characters, so a long label cannot overflow
a header bar. A posted target must be one of this site's own pages, categories, tags or series
(`MenusController.MarkForeignTargets`); the resolving read joins by id alone, so this check is what keeps
another tenant's titles and slugs out of a navbar. Saving an empty menu returns the site to its default, after a
confirmation. The audit actions are `Menu.Created`, `Menu.Updated` and `Menu.Deleted` (saved empty).

## Consequences

- Easier: header and footer links for any content type, with no theme change, and no effect on any
  site that doesn't build a menu.
- Proven, not assumed: on dev, the rendered header of the home page and a page was byte-identical
  before and after the view changes, and the footer was identical apart from whitespace.
- One exception, intended: the Feed navbar linked pages to `/pages/{slug}`, a route that has never
  existed, so its page links were 404s. It now links `/{slug}` like every other variant.
- Harder: every navbar variant must keep using `Model.Links` and `Href`; a new variant has to follow
  the same pattern. Nesting is one level.
- Menus change within 60 seconds of a content change (the cache), and immediately on a menu save.
- Now maintained: `NavigationMenuTests`:
  - no-menu links equal the old page links;
  - resolution, ordering, nesting and skipping gone content;
  - custom-link safety;
  - validation;
  - row mapping;
  - end to end: a menu replaces the header, and removing it restores the page.

  Also the `AdminPagesSmokeTests` row for `/admin/menus`.

## Proof

- Code: `Blog.Core/Domain/MenuItem.cs`, `Blog.Core/Services/NavigationMenu.cs`,
  `Blog.Infrastructure/Data/Repositories/MenuRepository.cs`, `Blog.Web/Services/MenuService.cs`,
  `Blog.Web/ViewComponents/{Navbar,Footer}ViewComponent.cs`,
  `Blog.Web/Views/Shared/Components/Navbar/*.cshtml`, `Blog.Web/Views/Shared/_FooterMenu.cshtml`,
  `Blog.Web/Controllers/MenusController.cs`, `Blog.Web/Views/Menus/Index.cshtml`
- Schema: `DBScripts/2026-09-27_create-menu-items-table.sql`, `MigrationService.cs`, `init.sql` §36b
  (empty-database diff clean, 369 columns)
- Tests: `Blog.Tests/NavigationMenuTests.cs`, `Blog.Tests/AdminPagesSmokeTests.cs`
