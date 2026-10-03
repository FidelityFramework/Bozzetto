// The canonical browser theme: the existing Braidpoint colors, logo accents
// and font families. Tailwind/DaisyUI and Vite's first-paint style consume
// this file; components use semantic classes, never their own palette.
const logoTokens = {
  ring: '186 83 13',           // #ba530d
  orange: '236 105 17',        // #ec6911
  rust: '190 53 14',           // #be350e
  'rust-ink': '190 53 14',
  teal: '70 143 153',          // #468f99
  'teal-ink': '47 111 120',    // #2f6f78
  blue: '49 81 130',           // #315182
  'blue-ink': '49 81 130',
  plum: '107 20 76',          // #6b144c
  'plum-ink': '107 20 76',
  'on-fill': '255 255 255',
};

const logoProperties = (tokens) => Object.fromEntries(
  Object.entries(tokens).map(([name, value]) => [`--bp-${name}`, value]),
);

export const fontFamilies = {
  sans: ['Montserrat', 'system-ui', 'sans-serif'],
  heading: ['Nunito', 'Montserrat', 'system-ui', 'sans-serif'],
  mono: ['"Fira Code"', '"JetBrains Mono"', '"Cascadia Code"', 'ui-monospace', 'monospace'],
};

export const logoColors = Object.fromEntries(
  Object.keys(logoTokens).map((name) => [name, `rgb(var(--bp-${name}) / <alpha-value>)`]),
);

// braidpoint-site/hugo/tailwind.config.js, colors unchanged. Logo fills stay
// exact; the ink shades retain their existing contrast on each background.
export const themes = {
  light: {
    'color-scheme': 'light',
    'primary': '#469c95ff',
    'primary-content': '#ffffff',
    'secondary': '#0065b2',
    'secondary-content': '#ffffff',
    'accent': '#f58220',
    'accent-content': '#ffffff',
    'neutral': '#323232',
    'neutral-content': '#eaeaea',
    'base-100': '#ffffff',
    'base-200': '#f5f5f5',
    'base-300': '#e5e5e5',
    'base-content': '#323232',
    'info': '#009bab',
    'info-content': '#ffffff',
    'success': '#22c55e',
    'success-content': '#ffffff',
    'warning': '#f58220',
    'warning-content': '#ffffff',
    'error': '#ef4444',
    'error-content': '#ffffff',
    ...logoProperties(logoTokens),
  },
  dark: {
    'color-scheme': 'dark',
    'primary': '#007067ff',
    'primary-content': '#1a1a1a',
    'secondary': '#3b8ed0',
    'secondary-content': '#1a1a1a',
    'accent': '#f58220',
    'accent-content': '#1a1a1a',
    'neutral': '#2a2a2a',
    'neutral-content': '#eaeaea',
    'base-100': '#1a1a1a',
    'base-200': '#242424',
    'base-300': '#2e2e2e',
    'base-content': '#eaeaea',
    'info': '#00b4a6',
    'info-content': '#1a1a1a',
    'success': '#4ade80',
    'success-content': '#1a1a1a',
    'warning': '#f58220',
    'warning-content': '#1a1a1a',
    'error': '#f87171',
    'error-content': '#1a1a1a',
    ...logoProperties({
      ...logoTokens,
      'rust-ink': '232 113 79',   // #e8714f
      'teal-ink': '95 179 189',   // #5fb3bd
      'blue-ink': '123 159 212',  // #7b9fd4
      'plum-ink': '210 127 178',  // #d27fb2
    }),
  },
};

// The same surfaces before the bundle initializes. Dark is the initial
// page choice; the app still restores the saved light/dark selection.
export const prepaintStyle = `
html { background-color: ${themes.dark['base-100']}; }
html[data-theme="light"] { background-color: ${themes.light['base-100']}; }
body { background-color: inherit; }
`;
