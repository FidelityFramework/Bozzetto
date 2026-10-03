/** @type {import('tailwindcss').Config} */
// The Braidpoint look (braidpoint-site/hugo): its DaisyUI `dark` and `light`
// themes exactly as that site defines them, its type families, and the
// Braidpoint logo palette as extra accent colors. Dark first: index.html
// starts dark and the app applies the saved choice. Build-time only: the
// bundle carries the generated CSS, never a stylesheet or font URL.
//
// The logo colors are theme-aware tokens (src/Frontend/styles.css): `fill` is
// the logo's exact hex, `ink` the shade that reads as text on that theme's
// background. bozzetto-web/README.md records which UI role each accent plays.
import daisyui from 'daisyui';

const token = (name) => `rgb(var(--bp-${name}) / <alpha-value>)`;

export default {
  content: [
    './src/Frontend/**/*.fs',
    './index.html',
  ],
  theme: {
    extend: {
      // Local families first; nothing is fetched. Montserrat is commonly
      // installed; Nunito and Fira Code fall back to the system's own faces.
      fontFamily: {
        sans: ['Montserrat', 'system-ui', 'sans-serif'],
        heading: ['Nunito', 'Montserrat', 'system-ui', 'sans-serif'],
        mono: ['"Fira Code"', '"JetBrains Mono"', '"Cascadia Code"', 'ui-monospace', 'monospace'],
      },
      colors: {
        bp: {
          ring: token('ring'),
          orange: token('orange'),
          rust: token('rust'),
          'rust-ink': token('rust-ink'),
          teal: token('teal'),
          'teal-ink': token('teal-ink'),
          blue: token('blue'),
          'blue-ink': token('blue-ink'),
          plum: token('plum'),
          'plum-ink': token('plum-ink'),
        },
      },
    },
  },
  plugins: [daisyui],
  daisyui: {
    // braidpoint-site/hugo/tailwind.config.js, colors unchanged; only
    // `color-scheme` is added, so native controls and scrollbars follow.
    themes: [
      {
        light: {
          'color-scheme': 'light',
          'primary': '#469c95ff',           // Brand teal
          'primary-content': '#ffffff',
          'secondary': '#0065b2',         // Brand blue
          'secondary-content': '#ffffff',
          'accent': '#f58220',            // Brand orange
          'accent-content': '#ffffff',
          'neutral': '#323232',           // Brand dark gray
          'neutral-content': '#eaeaea',
          'base-100': '#ffffff',
          'base-200': '#f5f5f5',
          'base-300': '#e5e5e5',
          'base-content': '#323232',
          'info': '#009bab',              // Brand teal variant
          'info-content': '#ffffff',
          'success': '#22c55e',
          'success-content': '#ffffff',
          'warning': '#f58220',           // Brand orange
          'warning-content': '#ffffff',
          'error': '#ef4444',
          'error-content': '#ffffff',
        },
        dark: {
          'color-scheme': 'dark',
          'primary': '#007067ff',           // Lighter teal for dark mode
          'primary-content': '#1a1a1a',
          'secondary': '#3b8ed0',         // Lighter blue for dark mode
          'secondary-content': '#1a1a1a',
          'accent': '#f58220',            // Brand orange (same)
          'accent-content': '#1a1a1a',
          'neutral': '#2a2a2a',           // Neutral dark gray
          'neutral-content': '#eaeaea',
          'base-100': '#1a1a1a',          // Neutral dark background
          'base-200': '#242424',          // Slightly lighter
          'base-300': '#2e2e2e',          // Card backgrounds
          'base-content': '#eaeaea',      // Brand light gray text
          'info': '#00b4a6',              // Lighter teal
          'info-content': '#1a1a1a',
          'success': '#4ade80',
          'success-content': '#1a1a1a',
          'warning': '#f58220',           // Brand orange
          'warning-content': '#1a1a1a',
          'error': '#f87171',
          'error-content': '#1a1a1a',
        },
      },
    ],
    // data-theme on <html> is the single authority: no media-query theming.
    darkTheme: false,
    logs: false,
  },
};
