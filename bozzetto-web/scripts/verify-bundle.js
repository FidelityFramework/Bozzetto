// Verify the welded page is self-contained: exactly one file in dist/, no
// http(s) URL anywhere in it (no CDN, no font or stylesheet fetch, no
// external script), and report its size. The one reference allowed is a
// relative link to a tab icon the daemon serves beside the page.
import { readdirSync, readFileSync } from 'node:fs';
import { gzipSync } from 'node:zlib';
import postcss from 'postcss';
import { prepaintStyle, themes } from '../theme.js';

const files = readdirSync('dist').filter((name) => name !== 'EmbeddedAssets.fs');
if (files.length !== 1 || files[0] !== 'index.html') {
  console.error(`verify: dist/ must hold only index.html, found: ${files.join(', ')}`);
  process.exit(1);
}

const html = readFileSync('dist/index.html', 'utf8');
const failures = [];

// Verify the generated page, not a hand-maintained copy of the palette.
// Its first-paint surfaces and logo variables must agree with the same theme
// source Tailwind/DaisyUI consumed for both saved choices.
const compact = (value) => value.replace(/\s+/g, '');
const styles = [...html.matchAll(/<style\b([^>]*)>([\s\S]*?)<\/style>/gi)];
const prepaint = styles.filter(([, attributes]) => /\bdata-bozzetto-theme=["']prepaint["']/.test(attributes));
if (prepaint.length !== 1 || compact(prepaint[0][2]) !== compact(prepaintStyle)) {
  failures.push('first-paint style does not derive from theme.js light/dark surfaces');
}
for (const [name, theme] of Object.entries(themes)) {
  const declarations = new Map();
  const selector = new RegExp(`\\[data-theme=["']?${name}["']?\\]`);
  for (const [, , css] of styles) {
    postcss.parse(css).walkRules((rule) => {
      if (!selector.test(rule.selector)) return;
      for (const node of rule.nodes) {
        if (node.type === 'decl') declarations.set(node.prop, node.value);
      }
    });
  }
  for (const [property, expected] of Object.entries(theme)) {
    if (property !== 'color-scheme' && !property.startsWith('--bp-')) continue;
    if (compact(declarations.get(property) ?? '') !== compact(expected)) {
      failures.push(`${name} ${property} does not derive from theme.js`);
    }
  }
}
const paletteSources = [
  'index.html', 'src/Frontend/styles.css', 'tailwind.config.js',
  ...readdirSync('src/Frontend', { recursive: true })
    .filter((name) => name.endsWith('.fs'))
    .map((name) => `src/Frontend/${name}`),
];
for (const source of paletteSources) {
  const content = readFileSync(source, 'utf8');
  const literalColor = /#[0-9a-f]{3,8}\b|\b(?:rgb|rgba|hsl|hsla|hwb|lab|lch|oklab|oklch)\(\s*[\d.-]/i;
  const independentPalette = /\b(?:bg|text|border|ring|outline|fill|stroke|decoration|shadow)-(?:slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose)-\d{2,3}\b/;
  if (literalColor.test(content) || independentPalette.test(content)) {
    failures.push(`${source} duplicates a palette; use theme.js semantic tokens`);
  }
}

for (const match of html.matchAll(/https?:\/\/[^\s"'`)<>]*/gi)) {
  const at = match.index;
  failures.push(`URL at ${at}: ${match[0]}  …${html.slice(Math.max(0, at - 60), at + 60).replace(/\s+/g, ' ')}…`);
}
const ownIcons = new Set(['favicon.ico', 'favicon.svg', 'favicon-dark.svg']);
for (const match of html.matchAll(/<(script|link|img|iframe)\b[^>]*>/gi)) {
  const tag = match[0];
  const reference = /\b(?:src|href)\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))/i.exec(tag);
  if (!reference) continue;
  const target = reference[1] ?? reference[2] ?? reference[3];
  const icon = /^<link\b/i.test(tag) && /\brel\s*=\s*["']?[^"'>]*\bicon\b/i.test(tag) && ownIcons.has(target);
  if (!icon) failures.push(`external reference: ${tag}`);
}

if (failures.length > 0) {
  console.error(`verify: dist/index.html is not self-contained (${failures.length}):`);
  for (const failure of failures) console.error('  ' + failure);
  process.exit(1);
}

const bytes = Buffer.byteLength(html);
const gzip = gzipSync(html).length;
console.log(`verify: dist/index.html is self-contained, ${(bytes / 1024).toFixed(1)} KiB (${(gzip / 1024).toFixed(1)} KiB gzip), no http(s) URLs; light/dark theme and first paint agree with theme.js`);
