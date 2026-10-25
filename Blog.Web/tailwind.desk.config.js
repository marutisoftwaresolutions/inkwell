/**
 * Tailwind config for the DESK (admin) and the auth pages — compiled to wwwroot/css/desk.css.
 *
 * Until v1.0.9 the Desk compiled its utility classes in the browser with the 407 KB Play-CDN
 * runtime (lib/tailwindcss/tailwind.js), which flashed an unstyled shell on every navigation.
 * This config moves that work to build time, the same way tailwind.config.js already does for
 * the public frontend. Build:
 *
 *   npx tailwindcss@3 -c tailwind.desk.config.js -i wwwroot/css/desk.src.css -o wwwroot/css/desk.css --minify
 *
 * or `dotnet build -p:BuildTailwindCss=true` (both stylesheets). The compiled desk.css is
 * committed and is the source of truth; add a class, rebuild, ship.
 *
 * Colours are CSS variables from desk-tokens.css (the shadcn/Tailwind namespace: --background,
 * --primary, --card …) so the light/dark scheme keeps working at runtime. The radius scale is
 * derived from one --radius token (8px): rounded-sm 4px · rounded / rounded-md 6px · rounded-lg
 * 8px · rounded-xl 12px · rounded-2xl 16px.
 */
// A token colour that also supports Tailwind opacity modifiers (bg-primary/90, border-foreground/30):
// the tokens are hex values in CSS variables, so the alpha is applied with color-mix (supported in every
// evergreen browser since 2023; admin.css already relies on it).
const token = (name) => ({ opacityValue }) =>
  opacityValue === undefined || opacityValue === 1 || opacityValue === '1'
    ? `var(--${name})`
    : `color-mix(in srgb, var(--${name}) calc(${opacityValue} * 100%), transparent)`;

module.exports = {
  content: [
    './Views/**/*.cshtml',
    './wwwroot/js/**/*.js',
  ],
  theme: {
    extend: {
      colors: {
        background: token('background'),
        foreground: token('foreground'),
        card: token('card'),
        'card-foreground': token('card-foreground'),
        popover: token('popover'),
        'popover-foreground': token('popover-foreground'),
        primary: token('primary'),
        'primary-foreground': token('primary-foreground'),
        secondary: token('secondary'),
        'secondary-foreground': token('secondary-foreground'),
        muted: token('muted'),
        'muted-foreground': token('muted-foreground'),
        accent: token('accent'),
        'accent-foreground': token('accent-foreground'),
        destructive: token('destructive'),
        'destructive-foreground': token('destructive-foreground'),
        border: token('border'),
        input: token('input'),
        ring: token('ring'),
        sidebar: token('sidebar'),
        'sidebar-foreground': token('sidebar-foreground'),
        'sidebar-primary': token('sidebar-primary'),
        'sidebar-primary-foreground': token('sidebar-primary-foreground'),
        'sidebar-accent': token('sidebar-accent'),
        'sidebar-accent-foreground': token('sidebar-accent-foreground'),
        'sidebar-border': token('sidebar-border'),
        // Status tokens (admin.css defines --success/--warning/--info for both schemes)
        success: token('success'),
        warning: token('warning'),
        info: token('info'),
        chart: {
          1: token('chart-1'), 2: token('chart-2'), 3: token('chart-3'), 4: token('chart-4'),
          5: token('chart-5'), 6: token('chart-6'), 7: token('chart-7'), 8: token('chart-8'),
        },
      },
      fontFamily: {
        sans: ["'Geist'", 'Inter', 'system-ui', 'sans-serif'],
        serif: ["'Source Serif 4'", 'Georgia', 'serif'],
        mono: ["'Geist Mono'", 'ui-monospace', 'monospace'],
      },
      borderRadius: {
        none: '0',
        sm: 'calc(var(--radius) - 4px)',
        DEFAULT: 'calc(var(--radius) - 2px)',
        md: 'calc(var(--radius) - 2px)',
        lg: 'var(--radius)',
        xl: 'calc(var(--radius) + 4px)',
        '2xl': 'calc(var(--radius) + 8px)',
        '3xl': 'calc(var(--radius) + 16px)',
        full: '9999px',
      },
      boxShadow: {
        card: 'var(--shadow-card)',
        pop: 'var(--shadow-pop)',
      },
    },
  },
  plugins: [],
};
