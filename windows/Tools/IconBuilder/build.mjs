// IconPark → WinUI 3 Font Pipeline
// Phase 1: Stroke→Fill via oslllo-svg-fixer
// Phase 2: SVG font → TTF via svgicons2svgfont + svg2ttf
// Phase 3: XAML resource dictionary generation

import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const { fixString } = require('oslllo-svg-fixer');
const { SVGIcons2SVGFontStream } = require('svgicons2svgfont');
const svg2ttf = require('svg2ttf');

import { readdir, readFile, writeFile, rm, mkdir, copyFile } from 'node:fs/promises';
import { createReadStream, createWriteStream } from 'node:fs';
import { pipeline } from 'node:stream';
import { promisify } from 'node:util';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const pipelineAsync = promisify(pipeline);

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const SRC = path.resolve(__dirname, '../../Assets/Icons');
const FIXED = path.resolve(__dirname, '.fixed');
const DIST = path.resolve(__dirname, 'dist');
const OUT = path.resolve(__dirname, '../../Assets');

async function build() {
  // --- Phase 1: Stroke → Fill ---
  await rm(FIXED, { recursive: true, force: true });
  await mkdir(FIXED, { recursive: true });

  const files = (await readdir(SRC)).filter(f => f.endsWith('.svg'));
  console.log(`[Phase 1] Converting stroke → fill for ${files.length} icons...`);

  for (const file of files) {
    const raw = await readFile(path.join(SRC, file), 'utf8');
    let fixed = await fixString(raw);
    // Strip width/height attributes — svgicons2svgfont normalizes against
    // physical dimensions (24px) instead of viewBox (48×48), causing tiny glyphs.
    // Use \s* to also catch width/height when they're the first attribute.
    fixed = fixed.replace(/\s*(width|height)="[^"]*"/g, '');
    fixed = fixed.replace(/viewBox="0 0 48 48"/g, 'viewBox="0 0 24 24"');
    await writeFile(path.join(FIXED, file), fixed);
  }

  // --- Phase 2: Deterministic PUA codepoint mapping (U+E001 →) ---
  const names = files.map(f => path.basename(f, '.svg')).sort();
  const codepoints = {};
  names.forEach((name, i) => {
    codepoints[name] = 0xe001 + i;
  });

  // --- Phase 3: SVG Font → TTF ---
  await rm(DIST, { recursive: true, force: true });
  await mkdir(DIST, { recursive: true });

  console.log(`[Phase 2] Generating TTF for ${names.length} icons...`);

  const svgFontPath = path.join(DIST, 'IconPark.svg');
  const fontStream = new SVGIcons2SVGFontStream({
    fontName: 'IconPark',
    fontHeight: 1000,
    normalize: true,
    log: () => {},
  });

  // pipelineAsync must be set up before writing glyphs to avoid
  // double-piping that causes duplicate <font> output.
  const svgOut = createWriteStream(svgFontPath);
  const pipelinePromise = pipelineAsync(fontStream, svgOut);

  for (const [name, cp] of Object.entries(codepoints)) {
    const glyph = createReadStream(path.join(FIXED, `${name}.svg`));
    glyph.metadata = {
      name,
      unicode: [String.fromCodePoint(cp)],
    };
    fontStream.write(glyph);
  }
  fontStream.end();

  await pipelinePromise;

  const svgFont = await readFile(svgFontPath, 'utf8');
  // Strip internal <?xml?> and <!DOCTYPE> that svgicons2svgfont may embed.
  // Also strip any nested <svg>/<defs> wrappers inside the <font> element
  // that result from per-glyph embedding.
  let cleanSvg = svgFont
    .replace(/<\?xml[^?]*\?>\s*/g, '')
    .replace(/<!DOCTYPE[^>]*>\s*/g, '')
    // Remove nested <svg> open/close tags inside <font> (artifact of per-glyph embedding)
    .replace(/<svg[^>]*>/g, '')
    .replace(/<\/svg>/g, '')
    // Remove nested <defs> open/close tags
    .replace(/<defs>/g, '')
    .replace(/<\/defs>/g, '');
  const ttf = svg2ttf(cleanSvg, {});
  await writeFile(path.join(DIST, 'IconPark.ttf'), ttf.buffer);

  // --- Phase 4: XAML generation ---
  console.log('[Phase 3] Generating Icons.xaml...');
  const toPascal = (s) => s.split('-').map(w => w[0].toUpperCase() + w.slice(1)).join('');
  const xamlEntries = Object.entries(codepoints)
    .map(([name, cp]) => {
      const hex = cp.toString(16);
      return `    <x:String x:Key="Icon_${toPascal(name)}">&#x${hex};</x:String>`;
    })
    .join('\n');

  const xaml = `<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
${xamlEntries}
</ResourceDictionary>
`;

  await writeFile(path.join(DIST, 'Icons.xaml'), xaml);

  // --- Phase 5: Deploy to Assets/ ---
  await copyFile(path.join(DIST, 'IconPark.ttf'), path.join(OUT, 'IconPark.ttf'));
  await copyFile(path.join(DIST, 'Icons.xaml'), path.join(OUT, 'Icons.xaml'));

  // --- Cleanup ---
  await rm(FIXED, { recursive: true, force: true });
  await rm(DIST, { recursive: true, force: true });

  console.log('Done: IconPark.ttf + Icons.xaml → windows/Assets/');
}

build();
