/* Bounded caches replace the engine's cache maps; there is no second cache layer. */
class JizuraLruMap extends Map {
  constructor(maxEntries, maxBytes, weight) {
    super(); this.maxEntries = maxEntries; this.maxBytes = maxBytes; this.weight = weight; this.bytes = 0;
  }
  get(key) {
    const entry = super.get(key);
    if (!entry) return undefined;
    super.delete(key); super.set(key, entry);
    return entry.value;
  }
  set(key, value) {
    this.delete(key);
    const bytes = Math.max(0, this.weight(value));
    if (bytes > this.maxBytes) return this;
    super.set(key, { value, bytes }); this.bytes += bytes; this.trim(); return this;
  }
  delete(key) {
    const entry = super.get(key);
    if (!entry) return false;
    this.bytes -= entry.bytes; return super.delete(key);
  }
  clear() { super.clear(); this.bytes = 0; }
  trim() { while (this.size > this.maxEntries || this.bytes > this.maxBytes) this.delete(this.keys().next().value); }
  limit(entries, bytes) { this.maxEntries = entries; this.maxBytes = bytes; this.trim(); }
  *values() { for (const entry of super.values()) yield entry.value; }
  *entries() { for (const [key, entry] of super.entries()) yield [key, entry.value]; }
  [Symbol.iterator]() { return this.entries(); }
  forEach(callback, owner) { for (const [key, value] of this.entries()) callback.call(owner, value, key, this); }
}

const installJizuraFrameCaches = renderer => {
  const megabyte = 1024 * 1024;
  const canvasBytes = canvas => canvas.width * canvas.height * 4;
  const glyphs = new JizuraLruMap(256, 6 * megabyte, glyph =>
    256 + glyph.pieces.reduce((bytes, piece) => bytes + canvasBytes(piece.cv), 0));
  const colors = new JizuraLruMap(1024, 6 * megabyte, canvasBytes);
  const paper = new JizuraLruMap(2, 4 * megabyte, canvasBytes);
  J.glyphs.map = glyphs;
  J.glyphs.tint = colors;
  J.metrics.m = new JizuraLruMap(4096, 256 * 1024, () => 64);
  renderer.paperCache = paper;
  // Same tint operation as the upstream engine, with one bounded LRU owner.
  J.glyphs.sprite = (piece, color) => {
    if (color === '#ffffff' || color === '#fff') return piece.cv;
    const key = `${piece.id}|${color}`;
    let canvas = colors.get(key);
    if (!canvas) {
      canvas = document.createElement('canvas'); canvas.width = piece.cv.width; canvas.height = piece.cv.height;
      const ctx = canvas.getContext('2d'); ctx.drawImage(piece.cv, 0, 0);
      ctx.globalCompositeOperation = 'source-in'; ctx.fillStyle = color; ctx.fillRect(0, 0, canvas.width, canvas.height);
      colors.set(key, canvas);
    }
    return canvas;
  };
  const sidSet = _sidCache.set.bind(_sidCache);
  _sidCache.set = (key, value) => {
    if (!_sidCache.has(key) && _sidCache.size >= 4096) _sidCache.delete(_sidCache.keys().next().value);
    return sidSet(key, value);
  };
  return {
    pressure: reduced => {
      glyphs.limit(reduced ? 96 : 256, (reduced ? 2 : 6) * megabyte);
      colors.limit(reduced ? 256 : 1024, (reduced ? 2 : 6) * megabyte);
      paper.limit(reduced ? 1 : 2, (reduced ? 1 : 4) * megabyte);
    },
    clearGlyphs: () => { J.glyphs.clear(); J.metrics.clear(); },
    clear: () => { J.glyphs.clear(); J.metrics.clear(); paper.clear(); _sidCache.clear(); },
    dispose: () => {
      const release = canvas => { canvas.width = canvas.height = 1; };
      glyphs.forEach(glyph => glyph.pieces.forEach(piece => release(piece.cv)));
      colors.forEach(release); paper.forEach(release);
      J.glyphs.clear(); J.metrics.clear(); paper.clear(); _sidCache.clear();
    },
  };
};
