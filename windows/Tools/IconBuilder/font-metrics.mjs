// svg2ttf adds a 9% OS/2 line gap and includes it in usWinAscent. It does not
// expose a public option to disable this. Icons use an em square, not text
// paragraphs: remove that extra leading without moving any glyph contours.
export function removeIconFontLineGap(input) {
  const font = Buffer.from(input);
  if (font.length < 12 || font.readUInt32BE(0) !== 0x00010000) {
    throw new Error('Expected a TrueType font.');
  }
  const tableCount = font.readUInt16BE(4);
  const directoryEnd = 12 + tableCount * 16;
  if (directoryEnd > font.length) throw new Error('Truncated TrueType table directory.');

  const tables = new Map();
  for (let record = 12; record < directoryEnd; record += 16) {
    const tag = font.toString('ascii', record, record + 4);
    const offset = font.readUInt32BE(record + 8);
    const length = font.readUInt32BE(record + 12);
    if (tables.has(tag) || offset < directoryEnd || offset % 4 !== 0 || offset + length > font.length) {
      throw new Error(`Invalid TrueType table: ${tag}`);
    }
    tables.set(tag, { record, offset, length });
  }
  function requiredTable(tag, minimumLength) {
    const table = tables.get(tag);
    if (!table || table.length < minimumLength) throw new Error(`Missing or truncated TrueType table: ${tag}`);
    return table;
  }
  const head = requiredTable('head', 54);
  const hhea = requiredTable('hhea', 36);
  const os2 = requiredTable('OS/2', 78);
  if (font.readUInt32BE(head.offset + 12) !== 0x5f0f3cf5) throw new Error('Invalid TrueType head magic.');

  font.writeInt16BE(0, hhea.offset + 8); // hhea.lineGap
  font.writeInt16BE(0, os2.offset + 72); // OS/2.sTypoLineGap
  // Keep actual outline overshoots within the Windows clipping bounds.
  font.writeUInt16BE(Math.max(0, font.readInt16BE(head.offset + 42),
    font.readInt16BE(hhea.offset + 4), font.readInt16BE(os2.offset + 68)), os2.offset + 74);
  font.writeUInt16BE(Math.max(0, -font.readInt16BE(head.offset + 38),
    -font.readInt16BE(hhea.offset + 6), -font.readInt16BE(os2.offset + 70)), os2.offset + 76);

  // OpenType checksums treat head.checkSumAdjustment as zero, including the
  // head table's directory checksum. Calculate the final adjustment last.
  font.writeUInt32BE(0, head.offset + 8);
  for (const { record, offset, length } of tables.values()) {
    font.writeUInt32BE(checksum(font.subarray(offset, offset + length)), record + 4);
  }
  font.writeUInt32BE((0xb1b0afba - checksum(font)) >>> 0, head.offset + 8);
  return font;
}

function checksum(bytes) {
  let sum = 0;
  for (let offset = 0; offset < bytes.length; offset += 4) {
    let word = 0;
    for (let byte = 0; byte < 4; byte++) word = (word << 8) | (bytes[offset + byte] ?? 0);
    sum = (sum + (word >>> 0)) >>> 0;
  }
  return sum;
}
