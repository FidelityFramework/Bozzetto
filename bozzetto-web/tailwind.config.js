/** @type {import('tailwindcss').Config} */
// All browser colors and font families belong to theme.js. Components and
// reusable treatments in styles.css consume its DaisyUI/semantic tokens.
import daisyui from 'daisyui';
import { fontFamilies, logoColors, themes } from './theme.js';

export default {
  content: [
    './src/Frontend/**/*.fs',
    './index.html',
  ],
  theme: {
    extend: {
      fontFamily: fontFamilies,
      colors: { bp: logoColors },
    },
  },
  plugins: [daisyui],
  daisyui: {
    themes: [themes],
    // data-theme on <html> is the authority; the app restores the saved choice.
    darkTheme: false,
    logs: false,
  },
};
