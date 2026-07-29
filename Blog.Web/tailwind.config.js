/**
 * Tailwind config for the PUBLIC frontend (compiled to wwwroot/css/tailwind.css).
 *
 * This replaces the render-blocking runtime Tailwind JIT compiler that was previously
 * loaded via <script src="~/lib/tailwindcss/tailwind.js"> in _PublicLayout.cshtml.
 * The admin panel (_AdminLayout.cshtml) still uses its own runtime compiler — it is
 * noindex/auth-gated and not part of the public Core-Web-Vitals surface.
 *
 * Color values are CSS variables, so runtime theming (Inkwell presets / custom theme
 * settings) keeps working exactly as before — only class→CSS generation moves to build time.
 *
 * NOTE (CLAUDE.md CSS-namespace rule): the public frontend primarily uses the Inkwell
 * token namespace (--bg, --fg, --surface, --border …). The shadcn names below are included
 * because a few public views/navbars reference them; they resolve against the vars the
 * public layout defines. `primary` is intentionally mapped to var(--ink-black) to preserve
 * the exact prior public behavior (do NOT change to var(--primary) here).
 */
module.exports = {
  content: [
    './Views/**/*.cshtml',
    './wwwroot/js/**/*.js',
  ],
  theme: {
    extend: {
      fontFamily: {
        sans: ["'Geist'", 'Inter', 'system-ui', 'sans-serif'],
        serif: ["'Source Serif 4'", 'Georgia', 'serif'],
        body: ["'Source Serif 4'", 'Georgia', 'serif'],
        mono: ["'Geist Mono'", 'monospace'],
      },
      colors: {
        // ── Public Inkwell mapping (matches the prior _PublicLayout inline config) ──
        neutral: {
          50: 'var(--bg)',
          100: 'var(--surface)',
          200: 'var(--border)',
          500: 'var(--fg-meta)',
          700: 'var(--fg-body)',
          900: 'var(--fg)',
        },
        primary: 'var(--ink-black)',
        'primary-foreground': 'var(--ink-cream)',
        // ── shadcn names used by a handful of public views/navbars ──
        background: 'var(--background)',
        foreground: 'var(--foreground)',
        card: 'var(--card)',
        'card-foreground': 'var(--card-foreground)',
        popover: 'var(--popover)',
        'popover-foreground': 'var(--popover-foreground)',
        secondary: 'var(--secondary)',
        'secondary-foreground': 'var(--secondary-foreground)',
        muted: 'var(--muted)',
        'muted-foreground': 'var(--muted-foreground)',
        accent: 'var(--accent)',
        'accent-foreground': 'var(--accent-foreground)',
        destructive: 'var(--destructive)',
        'destructive-foreground': 'var(--destructive-foreground)',
        border: 'var(--border)',
        input: 'var(--input)',
        ring: 'var(--ring)',
      },
      borderRadius: {
        DEFAULT: 'var(--radius)',
      },
    },
  },
  plugins: [],
};
