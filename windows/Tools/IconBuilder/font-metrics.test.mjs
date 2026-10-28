import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import svg2ttf from 'svg2ttf';
import { removeIconFontLineGap } from './font-metrics.mjs';

const generated = Buffer.from(svg2ttf(`<svg xmlns="http://www.w3.org/2000/svg"><defs>
  <font id="TestIcons" horiz-adv-x="1000">
    <font-face font-family="TestIcons" units-per-em="1000" ascent="1000" descent="0" />
    <missing-glyph horiz-adv-x="1000" />
    <glyph glyph-name="test" unicode="&#xe001;" d="M200 200H800V800H200Z" />
  </font></defs></svg>`, {}).buffer);

function tables(font) {
  const result = new Map();
  for (let i = 0; i < font.readUInt16BE(4); i++) {
    const record = 12 + i * 16;
    const offset = font.readUInt32BE(record + 8);
    const length = font.readUInt32BE(record + 12);
    result.set(font.toString('ascii', record, record + 4), {
      record, bytes: font.subarray(offset, offset + length), offset,
    });
  }
  return result;
}

// An independent checksum implementation, including zero padding to 4 bytes.
function checksum(bytes) {
  const padded = Buffer.alloc(Math.ceil(bytes.length / 4) * 4);
  bytes.copy(padded);
  let result = 0;
  for (let i = 0; i < padded.length; i += 4) result = (result + padded.readUInt32BE(i)) >>> 0;
  return result;
}

test('removes the real svg2ttf default gap without changing ascent or descent', () => {
  const original = tables(generated);
  assert.equal(original.get('OS/2').bytes.readInt16BE(72), 90);
  assert.equal(original.get('OS/2').bytes.readUInt16BE(74), 1090);
  const normalized = tables(removeIconFontLineGap(generated));
  assert.equal(normalized.get('OS/2').bytes.readInt16BE(72), 0);
  assert.equal(normalized.get('OS/2').bytes.readUInt16BE(74), 1000);
  assert.equal(normalized.get('OS/2').bytes.readUInt16BE(76), 0);
  assert.equal(normalized.get('hhea').bytes.readInt16BE(8), 0);
  assert.deepEqual(normalized.get('hhea').bytes.subarray(4, 8), original.get('hhea').bytes.subarray(4, 8));
  assert.deepEqual(normalized.get('OS/2').bytes.subarray(68, 72), original.get('OS/2').bytes.subarray(68, 72));
});

test('preserves clipping space for glyphs extending outside the em square', () => {
  const overshoot = Buffer.from(generated);
  const head = tables(overshoot).get('head').offset;
  overshoot.writeInt16BE(-120, head + 38);
  overshoot.writeInt16BE(1100, head + 42);
  const normalized = tables(removeIconFontLineGap(overshoot)).get('OS/2').bytes;
  assert.equal(normalized.readUInt16BE(74), 1100);
  assert.equal(normalized.readUInt16BE(76), 120);
});

for (const [name, source] of [
  ['generated font', generated],
  ['shipped IconPark font', readFileSync(new URL('../../Assets/IconPark.ttf', import.meta.url))],
]) {
  test(`${name}: changes only line metrics and required checksums; leaves input untouched`, () => {
    const before = Buffer.from(source);
    const output = removeIconFontLineGap(source);
    assert.deepEqual(source, before);
    assert.equal(output.length, source.length);
    const originalTables = tables(source);
    for (const [tag, { bytes }] of tables(output)) {
      const original = originalTables.get(tag).bytes;
      if (tag === 'head') {
        assert.deepEqual(bytes.subarray(0, 8), original.subarray(0, 8));
        assert.deepEqual(bytes.subarray(12), original.subarray(12));
      } else if (tag === 'OS/2') {
        assert.deepEqual(bytes.subarray(0, 72), original.subarray(0, 72));
        assert.deepEqual(bytes.subarray(78), original.subarray(78));
      } else if (tag === 'hhea') {
        assert.deepEqual(bytes.subarray(0, 8), original.subarray(0, 8));
        assert.deepEqual(bytes.subarray(10), original.subarray(10));
      } else {
        assert.deepEqual(bytes, original, `${tag} must not change`);
      }
    }
    assert.deepEqual(removeIconFontLineGap(output), output, 'normalization must be idempotent');
  });

  test(`${name}: has valid table checksums and a valid whole-font checksum`, () => {
    const output = removeIconFontLineGap(source);
    assert.equal(checksum(output), 0xb1b0afba);
    for (const [tag, { record, bytes }] of tables(output)) {
      const data = Buffer.from(bytes);
      if (tag === 'head') data.writeUInt32BE(0, 8);
      assert.equal(output.readUInt32BE(record + 4), checksum(data), `${tag} checksum`);
    }
  });
}

test('rejects invalid or incomplete fonts before writing metadata', () => {
  assert.throws(() => removeIconFontLineGap(Buffer.alloc(8)), /TrueType/);
  assert.throws(() => removeIconFontLineGap(generated.subarray(0, 20)), /Truncated/);
  const invalidOffset = Buffer.from(generated);
  invalidOffset.writeUInt32BE(0xffffffff, 20);
  assert.throws(() => removeIconFontLineGap(invalidOffset), /Invalid TrueType table/);
  const missingOs2 = Buffer.from(generated);
  missingOs2.write('xxxx', tables(missingOs2).get('OS/2').record, 'ascii');
  assert.throws(() => removeIconFontLineGap(missingOs2), /Missing or truncated.*OS\/2/);
});
