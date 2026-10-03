// Verify the welded page is self-contained: exactly one file in dist/, no
// http(s) URL anywhere in it (no CDN, no font or stylesheet fetch, no
// external script), and report its size. The one reference allowed is a
// relative link to a tab icon the daemon serves beside the page.
import { readdirSync, readFileSync } from 'node:fs';
import { gzipSync } from 'node:zlib';

const files = readdirSync('dist').filter((name) => name !== 'EmbeddedAssets.fs');
if (files.length !== 1 || files[0] !== 'index.html') {
  console.error(`verify: dist/ must hold only index.html, found: ${files.join(', ')}`);
  process.exit(1);
}

const html = readFileSync('dist/index.html', 'utf8');
const failures = [];

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
console.log(`verify: dist/index.html is self-contained, ${(bytes / 1024).toFixed(1)} KiB (${(gzip / 1024).toFixed(1)} KiB gzip), no http(s) URLs`);
