var SourceLyrics = (() => {
  var __defProp = Object.defineProperty;
  var __getOwnPropDesc = Object.getOwnPropertyDescriptor;
  var __getOwnPropNames = Object.getOwnPropertyNames;
  var __hasOwnProp = Object.prototype.hasOwnProperty;
  var __export = (target, all) => {
    for (var name in all)
      __defProp(target, name, { get: all[name], enumerable: true });
  };
  var __copyProps = (to, from, except, desc) => {
    if (from && typeof from === "object" || typeof from === "function") {
      for (let key of __getOwnPropNames(from))
        if (!__hasOwnProp.call(to, key) && key !== except)
          __defProp(to, key, { get: () => from[key], enumerable: !(desc = __getOwnPropDesc(from, key)) || desc.enumerable });
    }
    return to;
  };
  var __toCommonJS = (mod) => __copyProps(__defProp({}, "__esModule", { value: true }), mod);

  // tools/SourceLyrics/blossom-entry.ts
  var blossom_entry_exports = {};
  __export(blossom_entry_exports, {
    build: () => build,
    canvasId: () => canvasId,
    dispose: () => dispose,
    draw: () => draw,
    pointer: () => pointer
  });

  // ../../../Users/moekotori/echo/src/shared/constants/blossomPreferences.ts
  var defaultBlossomPreferences = {
    intensity: 1,
    palette: "theme",
    frameCap: 0,
    seed: 0,
    effects: {
      background: true,
      glow: true,
      trail: true,
      highlight: true,
      guide: true,
      icons: true,
      ghost: true,
      frame: true,
      hud: true,
      chrome: true,
      grain: true,
      parallax: true,
      hover: true
    }
  };

  // native:frame-clock
  var subscribeVisualFrame = () => () => {
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/blossom/blossomMath.ts
  var TAU = Math.PI * 2;
  var GLOW_SOFT = "rgba(255,255,255,.45)";
  var GLOW_FAINT = "rgba(255,255,255,.28)";
  var GLOW_MAX_BLUR = 30;
  var hash = (value) => {
    const text = String(value);
    let h = 2166136261;
    for (let i = 0; i < text.length; i++) {
      h ^= text.charCodeAt(i);
      h = Math.imul(h, 16777619);
    }
    return h >>> 0;
  };
  var rng = (seed) => {
    let a = hash(seed);
    return () => {
      a |= 0;
      a = a + 1831565813 | 0;
      let t = Math.imul(a ^ a >>> 15, 1 | a);
      t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t;
      return ((t ^ t >>> 14) >>> 0) / 4294967296;
    };
  };
  var clamp01 = (x) => Math.min(1, Math.max(0, x));
  var expoOut = (v) => v >= 1 ? 1 : 1 - Math.pow(2, -10 * clamp01(v));
  var bezier = (a, b, t) => {
    const i = 1 - t;
    return 3 * i * i * t * a + 3 * i * t * t * b + t * t * t;
  };
  var cubic = (x1, y1, x2, y2, value) => {
    const v = clamp01(value);
    if (v === 0 || v === 1) return v;
    let lo = 0, hi = 1, t = v;
    for (let k = 0; k < 12; k++) {
      if (bezier(x1, x2, t) < v) lo = t;
      else hi = t;
      t = (lo + hi) / 2;
    }
    return bezier(y1, y2, t);
  };
  var easeIO = (v) => cubic(0.65, 0, 0.35, 1, v);
  var softOut = (v) => {
    const u = 1 - clamp01(v);
    return 1 - u * u * u * u;
  };
  var smoothstep = (v) => {
    const u = clamp01(v);
    return u * u * (3 - 2 * u);
  };
  var unitCache = /* @__PURE__ */ new Map();
  var unit = (key) => {
    let v = unitCache.get(key);
    if (v === void 0) {
      if (unitCache.size > 6e3) unitCache.clear();
      v = hash(key) % 1e5 / 1e5;
      unitCache.set(key, v);
    }
    return v;
  };
  var hilbert = (n, px, py) => {
    let x = px, y = py, d = 0;
    for (let s = n >> 1; s > 0; s >>= 1) {
      const rx = (x & s) > 0 ? 1 : 0, ry = (y & s) > 0 ? 1 : 0;
      d += s * s * (3 * rx ^ ry);
      if (ry === 0) {
        if (rx === 1) {
          x = n - 1 - x;
          y = n - 1 - y;
        }
        const t = x;
        x = y;
        y = t;
      }
    }
    return d;
  };
  var mitchell = (n, bx, by, r) => {
    const pts = [];
    for (let i = 0; i < n; i++) {
      let best = [0, 0], bd = -1;
      const tries = i ? 12 : 1;
      for (let c = 0; c < tries; c++) {
        const p = [(r() - 0.5) * bx, (r() - 0.5) * by];
        let md = 1e18;
        for (const q of pts) md = Math.min(md, (p[0] - q[0]) ** 2 + (p[1] - q[1]) ** 2);
        if (md > bd) {
          bd = md;
          best = p;
        }
      }
      pts.push(best);
    }
    return pts;
  };
  var clipHalf = (poly, nx, ny, c) => {
    const out = [];
    for (let i = 0; i < poly.length; i++) {
      const a = poly[i], b = poly[(i + 1) % poly.length];
      const da = nx * a[0] + ny * a[1] - c, db = nx * b[0] + ny * b[1] - c;
      if (da <= 0) out.push(a);
      if (da < 0 && db > 0 || da > 0 && db < 0) {
        const u = da / (da - db);
        out.push([a[0] + (b[0] - a[0]) * u, a[1] + (b[1] - a[1]) * u]);
      }
    }
    return out;
  };
  var voronoi = (seeds, bx, by) => seeds.map((s, i) => {
    let poly = [[-bx / 2, -by / 2], [bx / 2, -by / 2], [bx / 2, by / 2], [-bx / 2, by / 2]];
    for (let j = 0; j < seeds.length && poly.length > 2; j++) {
      if (j === i) continue;
      const q = seeds[j], nx = q[0] - s[0], ny = q[1] - s[1];
      poly = clipHalf(poly, nx, ny, (nx * (s[0] + q[0]) + ny * (s[1] + q[1])) / 2);
    }
    return poly;
  });
  var polygonCentroid = (poly, fallback) => {
    let area = 0, cx = 0, cy = 0;
    for (let k = 0; k < poly.length; k++) {
      const p = poly[k], q = poly[(k + 1) % poly.length], cr = p[0] * q[1] - q[0] * p[1];
      area += cr;
      cx += (p[0] + q[0]) * cr;
      cy += (p[1] + q[1]) * cr;
    }
    if (Math.abs(area) < 1e-3) return fallback;
    return [cx / (3 * area), cy / (3 * area)];
  };
  var dampedPendulum = (theta0, tau, decay, omega) => theta0 * Math.exp(-decay * tau) * Math.cos(omega * tau);
  var hilbertXY = (n, d) => {
    let x = 0, y = 0, t = d;
    for (let s = 1; s < n; s *= 2) {
      const rx = 1 & t >> 1, ry = 1 & (t ^ rx);
      if (ry === 0) {
        if (rx === 1) {
          x = s - 1 - x;
          y = s - 1 - y;
        }
        const q = x;
        x = y;
        y = q;
      }
      x += s * rx;
      y += s * ry;
      t >>= 2;
    }
    return [x, y];
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/blossom/blossomPalette.ts
  var base = { bg: "#0b0f1b", bg2: "#111a30", ink: "#e8e3d9", sub: "#8fa3c8", grain: 0.05 };
  var fixed = {
    vermilion: ["#ff5a47", "#ffb13b", "#58b4ff", "#ff6fa5"],
    indigo: ["#6c8cff", "#58e0d0", "#ff7ab8", "#ffd36b"],
    moss: ["#6fe0a8", "#e6d36b", "#58b4ff", "#ff8f6b"],
    sakura: ["#ff7aa8", "#ffc3d8", "#9a8cff", "#ffd36b"],
    mono: ["#e8e4da", "#9aa3b8", "#ff5a47", "#c9b79a"]
  };
  var blossomPaletteSwatches = {
    theme: "",
    cover: "",
    vermilion: fixed.vermilion[0],
    indigo: fixed.indigo[0],
    moss: fixed.moss[0],
    sakura: fixed.sakura[0],
    mono: fixed.mono[0]
  };
  var hexToRgb = (hex) => {
    const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})/i.exec(hex);
    return m ? [parseInt(m[1], 16), parseInt(m[2], 16), parseInt(m[3], 16)] : [255, 255, 255];
  };
  var toHex = (r, g, b) => "#" + [r, g, b].map((v) => Math.round(Math.min(255, Math.max(0, v))).toString(16).padStart(2, "0")).join("");
  var rgbToHsl = (r, g, b) => {
    const R = r / 255, G = g / 255, B = b / 255, max = Math.max(R, G, B), min = Math.min(R, G, B), l = (max + min) / 2, d = max - min;
    if (d === 0) return [0, 0, l];
    const s = d / (1 - Math.abs(2 * l - 1));
    const h = max === R ? (G - B) / d % 6 : max === G ? (B - R) / d + 2 : (R - G) / d + 4;
    return [(h * 60 + 360) % 360, s, l];
  };
  var hslToHex = (h, s, l) => {
    const c = (1 - Math.abs(2 * l - 1)) * s, hp = (h % 360 + 360) % 360 / 60, x = c * (1 - Math.abs(hp % 2 - 1));
    const [r, g, b] = hp < 1 ? [c, x, 0] : hp < 2 ? [x, c, 0] : hp < 3 ? [0, c, x] : hp < 4 ? [0, x, c] : hp < 5 ? [x, 0, c] : [c, 0, x];
    const m = l - c / 2;
    return toHex((r + m) * 255, (g + m) * 255, (b + m) * 255);
  };
  var accentsFromColor = (hex) => {
    const [h, s, l] = rgbToHsl(...hexToRgb(hex));
    const sat = Math.min(0.9, Math.max(0.55, s)), lit = Math.min(0.72, Math.max(0.58, l));
    return [hslToHex(h, sat, lit), hslToHex(h + 42, sat, Math.min(0.76, lit + 0.06)), hslToHex(h + 185, sat * 0.9, lit), hslToHex(h - 48, sat, lit + 0.04)];
  };
  var cssColorToHex = (ctx, css) => {
    const value = css.trim();
    if (!value) return null;
    ctx.fillStyle = "#000000";
    ctx.fillStyle = value;
    const resolved = String(ctx.fillStyle);
    return /^#[0-9a-f]{6}$/i.test(resolved) ? resolved : null;
  };
  var readThemeAccent = (ctx, element) => {
    const style = element && typeof getComputedStyle === "function" ? getComputedStyle(element) : null;
    for (const name of ["--color-accent", "--theme-accent-text", "--theme-accent-solid-bg"]) {
      const hex = style ? cssColorToHex(ctx, style.getPropertyValue(name)) : null;
      if (hex) return hex;
    }
    return fixed.vermilion[0];
  };
  var resolveAccents = (id, themeAccent, coverAccents) => {
    if (id === "theme") return accentsFromColor(themeAccent);
    if (id === "cover") return coverAccents ? [...coverAccents] : accentsFromColor(themeAccent);
    return fixed[id];
  };
  var hexLerp = (a, b, k) => {
    const A = hexToRgb(a), B = hexToRgb(b);
    return toHex(A[0] + (B[0] - A[0]) * k, A[1] + (B[1] - A[1]) * k, A[2] + (B[2] - A[2]) * k);
  };
  var paletteFor = (accents, shotIndex, seed) => ({
    ...base,
    accents,
    acc: accents[(shotIndex + seed) % 4],
    acc2: accents[(shotIndex + seed + 1) % 4],
    acc3: accents[(shotIndex + seed + 2) % 4]
  });
  var hexMix = (a, b, k) => {
    const A = hexToRgb(a), B = hexToRgb(b);
    return `rgb(${A.map((v, i) => Math.round(v + (B[i] - v) * k)).join(",")})`;
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/blossom/blossomLight.ts
  var GAIN = 0.34;
  var cache = /* @__PURE__ */ new Map();
  var LIMIT = 72;
  var quant = /* @__PURE__ */ new Map();
  var clearLightCaches = () => {
    cache.forEach((c) => {
      c.width = 0;
      c.height = 0;
    });
    cache.clear();
    quant.clear();
  };
  var rgba = (col, a, white = 0) => {
    const [r, g, b] = hexToRgb(col), k = white;
    return `rgba(${Math.round(r + (255 - r) * k)},${Math.round(g + (255 - g) * k)},${Math.round(b + (255 - b) * k)},${Math.max(0, Math.min(1, a)).toFixed(3)})`;
  };
  var snap = (col) => {
    let q = quant.get(col);
    if (q === void 0) {
      if (quant.size > 96) quant.clear();
      const [r, g, b] = hexToRgb(col);
      q = "#" + [r, g, b].map((v) => Math.min(255, (v >> 3 << 3) + 4).toString(16).padStart(2, "0")).join("");
      quant.set(col, q);
    }
    return q;
  };
  var bake = (kind, col0, w, h, paint) => {
    const col = snap(col0), key = kind + col, hit = cache.get(key);
    if (hit) {
      cache.delete(key);
      cache.set(key, hit);
      return hit;
    }
    if (typeof document === "undefined") return null;
    if (cache.size >= LIMIT) {
      const old = cache.keys().next().value;
      if (old !== void 0) {
        const c2 = cache.get(old);
        if (c2) {
          c2.width = 0;
          c2.height = 0;
        }
        cache.delete(old);
      }
    }
    const c = document.createElement("canvas");
    c.width = w;
    c.height = h;
    const g = c.getContext("2d");
    if (!g) return null;
    paint(g, col);
    cache.set(key, c);
    return c;
  };
  var FALL = [[0, 1], [0.1, 0.78], [0.24, 0.46], [0.42, 0.21], [0.62, 0.08], [0.8, 0.022], [1, 0]];
  var radial = (g, cx, cy, r, col, hot) => {
    const grad = g.createRadialGradient(cx, cy, 0, cx, cy, r);
    FALL.forEach(([p, a]) => grad.addColorStop(p, rgba(col, a, hot * Math.max(0, 1 - p / 0.34))));
    return grad;
  };
  var glow = (g, x, y, rx, ry, col, a, hot = true) => {
    if (a <= 3e-3) return;
    const s = bake(hot ? "g" : "m", col, 128, 128, (p, k) => {
      p.fillStyle = radial(p, 64, 64, 64, k, hot ? 0.6 : 0);
      p.fillRect(0, 0, 128, 128);
    });
    if (!s) return;
    g.globalAlpha = Math.min(1, a * GAIN);
    g.drawImage(s, x - rx, y - ry, rx * 2, ry * 2);
  };
  var mist = (g, x, y, rx, ry, col, a) => glow(g, x, y, rx, ry, col, a, false);
  var beam = (g, x, y, ang, len, w, col, a) => {
    if (a <= 3e-3) return;
    const s = bake("b", col, 64, 256, (p, k) => {
      const grad = p.createLinearGradient(0, 0, 0, 256);
      [[0, 0], [0.05, 1], [0.28, 0.55], [0.6, 0.2], [1, 0]].forEach(([q, v]) => grad.addColorStop(q, rgba(k, v * 0.2, 0.12)));
      p.fillStyle = grad;
      p.globalCompositeOperation = "lighter";
      p.filter = "blur(2.5px)";
      for (const f of [1, 0.84, 0.68, 0.52, 0.36, 0.2]) {
        p.beginPath();
        p.moveTo(32 - 1.6 * f, 0);
        p.lineTo(32 + 1.6 * f, 0);
        p.lineTo(32 + 28 * f, 256);
        p.lineTo(32 - 28 * f, 256);
        p.closePath();
        p.fill();
      }
    });
    if (!s) return;
    g.save();
    g.translate(x, y);
    g.rotate(ang - Math.PI / 2);
    g.globalAlpha = Math.min(1, a * GAIN);
    g.drawImage(s, -w / 2, 0, w, len);
    g.restore();
  };
  var tail = (g, x, y, ang, len, th, col, a) => {
    if (a <= 3e-3) return;
    const s = bake("t", col, 256, 32, (p, k) => {
      const grad = p.createLinearGradient(0, 0, 256, 0);
      [[0, 0], [0.4, 0.1], [0.7, 0.4], [1, 1]].forEach(([q, v]) => grad.addColorStop(q, rgba(k, v * 0.2, 0.2 * q)));
      p.fillStyle = grad;
      p.globalCompositeOperation = "lighter";
      p.filter = "blur(1.6px)";
      for (const f of [1, 0.84, 0.68, 0.52, 0.36, 0.2]) {
        p.beginPath();
        p.moveTo(0, 16 - 1 * f);
        p.lineTo(0, 16 + 1 * f);
        p.lineTo(256, 16 + 13 * f);
        p.lineTo(256, 16 - 13 * f);
        p.closePath();
        p.fill();
      }
    });
    if (!s) return;
    g.save();
    g.translate(x, y);
    g.rotate(ang);
    g.globalAlpha = Math.min(1, a * GAIN);
    g.drawImage(s, -len, -th / 2, len, th);
    g.restore();
  };
  var streak = (g, x, y, len, th, col, a) => {
    if (a <= 3e-3) return;
    const s = bake("s", col, 256, 32, (p, k) => {
      p.translate(128, 16);
      p.scale(1, 16 / 128);
      const grad = p.createRadialGradient(0, 0, 0, 0, 0, 128);
      [[0, 1, 0.7], [0.05, 0.7, 0.4], [0.2, 0.26, 0.1], [0.45, 0.08, 0], [0.75, 0.018, 0], [1, 0, 0]].forEach(([q, al, wh]) => grad.addColorStop(q, rgba(k, al, wh)));
      p.fillStyle = grad;
      p.fillRect(-128, -128, 256, 256);
    });
    if (!s) return;
    g.globalAlpha = Math.min(1, a * GAIN);
    g.drawImage(s, x - len / 2, y - th / 2, len, th);
  };
  var glint = (g, x, y, r, col, a, rot = 0) => {
    if (a <= 3e-3) return;
    const s = bake("*", col, 128, 128, (p, k) => {
      const line = (ang, len, th, al) => {
        p.save();
        p.translate(64, 64);
        p.rotate(ang);
        p.scale(1, th);
        const grad = p.createRadialGradient(0, 0, 0, 0, 0, len);
        grad.addColorStop(0, rgba(k, al, 0.7));
        grad.addColorStop(0.18, rgba(k, al * 0.5, 0.3));
        grad.addColorStop(0.5, rgba(k, al * 0.12));
        grad.addColorStop(1, rgba(k, 0));
        p.fillStyle = grad;
        p.fillRect(-len, -len, len * 2, len * 2);
        p.restore();
      };
      line(0, 62, 0.045, 1);
      line(Math.PI / 2, 62, 0.045, 1);
      line(Math.PI / 4, 30, 0.05, 0.5);
      line(-Math.PI / 4, 30, 0.05, 0.5);
      p.fillStyle = radial(p, 64, 64, 14, k, 0.9);
      p.fillRect(50, 50, 28, 28);
    });
    if (!s) return;
    g.globalAlpha = Math.min(1, a * GAIN);
    if (rot) {
      g.save();
      g.translate(x, y);
      g.rotate(rot);
      g.drawImage(s, -r, -r, r * 2, r * 2);
      g.restore();
    } else g.drawImage(s, x - r, y - r, r * 2, r * 2);
  };
  var panel = (g, x, y, w, h, col, a, skew = 0) => {
    if (a <= 3e-3) return;
    const s = bake("p", col, 128, 64, (p, k) => {
      p.filter = "blur(6px)";
      p.fillStyle = rgba(k, 1, 0.35);
      p.fillRect(22, 14, 84, 36);
      p.filter = "none";
    });
    if (!s) return;
    g.save();
    g.translate(x, y);
    if (skew) g.transform(1, 0, skew, 1, 0, 0);
    g.globalAlpha = Math.min(1, a * GAIN);
    g.drawImage(s, -w * 0.5 * 1.52, -h * 0.5 * 1.78, w * 1.52, h * 1.78);
    g.restore();
  };
  var table = (tag, n, f) => Array.from({ length: n }, (_, i) => f(tag + i));

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/blossom/blossomTextMeasure.ts
  var caches = /* @__PURE__ */ new WeakMap();
  var blossomMeasureLimits = { entries: 512, keyCodeUnits: 32768, prefixGlyphs: 64 };
  var BlossomTextMeasure = class {
    constructor(context) {
      this.context = context;
    }
    widths = /* @__PURE__ */ new Map();
    keyCodeUnits = 0;
    measure(text, font) {
      const key = `${font}|${text}`, cached = this.widths.get(key);
      if (cached !== void 0) return cached;
      this.context.font = font;
      const width = this.context.measureText(text).width;
      if (key.length > blossomMeasureLimits.keyCodeUnits) return width;
      while (this.widths.size >= blossomMeasureLimits.entries || this.keyCodeUnits + key.length > blossomMeasureLimits.keyCodeUnits) {
        const oldest = this.widths.keys().next().value;
        this.keyCodeUnits -= oldest.length;
        this.widths.delete(oldest);
      }
      this.widths.set(key, width);
      this.keyCodeUnits += key.length;
      return width;
    }
  };
  var clearBlossomMeasureCache = (context) => {
    caches.delete(context);
  };
  var measureBlossomText = (context, text, font) => {
    let cache2 = caches.get(context);
    if (!cache2) {
      cache2 = new BlossomTextMeasure(context);
      caches.set(context, cache2);
    }
    return cache2.measure(text, font);
  };
  var measureBlossomRun = (context, glyphs, font, tracking) => {
    const advances = [], centers = [];
    const shapePrefixes = glyphs.length <= blossomMeasureLimits.prefixGlyphs;
    let prefix = "", previous = 0;
    for (let index = 0; index < glyphs.length; index += 1) {
      const glyphWidth = measureBlossomText(context, glyphs[index].ch, font);
      if (shapePrefixes) prefix += glyphs[index].ch;
      const end = shapePrefixes ? measureBlossomText(context, prefix, font) : previous + glyphWidth;
      centers.push(end - glyphWidth / 2 + tracking * index);
      advances.push(end - previous + (index + 1 < glyphs.length ? tracking : 0));
      previous = end;
    }
    return { advances, centers };
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/blossom/blossomBoxes.ts
  var CJK = /[぀-ヿ㐀-鿿가-힯]/;
  var GAP = (s) => s.BASE * 0.6;
  var SGAP = (s) => s.BASE * 0.5;
  var mkBox = (s, seg, role, scale, vertical, kind) => {
    const px = s.BASE * scale, cjk2 = CJK.test(seg.text);
    const serif = role === "semi" || (kind === "editorial" || kind === "tableau") && role === "support";
    const weight = role === "support" ? serif ? 600 : 700 : 900, fam = serif ? s.fonts.serif : s.fonts.sans;
    const font = `${weight} ${px}px ${fam}`;
    let vert = false, rot = 0;
    if (vertical) {
      if (cjk2) vert = true;
      else if (seg.vis > 1 && role !== "support") rot = Math.PI / 2;
    }
    const track = cjk2 ? 0 : px * 0.03;
    const run = vert ? null : measureBlossomRun(s.m, seg.chars, font, track);
    const advs = run?.advances ?? seg.chars.map(() => px * 0.9);
    let w, h;
    if (vert) {
      for (let i = 0; i < advs.length; i++) advs[i] = px * 0.9;
      w = Math.max(px * 0.9, ...seg.chars.map((c) => measureBlossomText(s.m, c.ch, font)));
      h = Math.max(1, advs.length) * px * 0.9;
    } else if (rot) {
      w = px * 1.2;
      h = advs.reduce((a, b) => a + b, 0);
    } else {
      w = advs.reduce((a, b) => a + b, 0);
      h = px * 1.2;
    }
    return { seg, role, px, weight, fam, font, vert, rot, advs, centers: run?.centers, w, h, x: 0, y: 0, ex: 0, ey: 0, isHero: role === "hero", scale, glyphs: [], hx: 0, hy: 0 };
  };
  var scaleBox = (b, f) => {
    b.px *= f;
    b.w *= f;
    b.h *= f;
    b.x *= f;
    b.y *= f;
    b.advs = b.advs.map((a) => a * f);
    if (b.centers) b.centers = b.centers.map((c) => c * f);
    b.font = `${b.weight} ${b.px}px ${b.fam}`;
    if (b.arc) b.arc.R *= f;
  };
  var ext = (b) => ({ w: b.rot ? b.h : b.w, h: b.rot ? b.w : b.h });
  var placeGlyphs = (s, b, shotDur) => {
    const motionDur = Math.min(Math.min(1.8, Math.max(0.65, shotDur * 0.42)), shotDur * 0.72);
    const total = b.advs.reduce((a, c) => a + c, 0);
    let cur = -total / 2;
    const lean = hash(b.seg.text + b.seg.idx) % 2 ? 1 : -1;
    b.glyphs = b.seg.chars.map((g, i) => {
      const a = b.advs[i], c = b.centers ? b.centers[i] - total / 2 : cur + a / 2;
      cur += a;
      return {
        ch: g.ch,
        c,
        ex: b.ex,
        ey: b.ey + (b.vert ? -b.px * 0.3 : b.px * 0.26),
        er: lean * (b.role !== "support" ? 0.05 : 0.03) * s.intensity,
        s: g.s,
        e: Math.max(g.s + 0.12, g.e),
        settle: Math.max(g.s, g.s + motionDur),
        i,
        ox: 0,
        oy: 0,
        vx: 0,
        vy: 0
      };
    });
  };
  var sung = (g, t) => clamp01((t - g.s) / Math.max(0.12, g.e - g.s));
  var pose = (s, b, g, t, sh) => {
    const p = softOut((t - g.s) / Math.max(g.settle - g.s, 0.08)), off = (1 - p) * s.intensity;
    let x, y, rot;
    if (b.arc) {
      const a = b.arc.a0 + 0.22 * Math.sin(t * 0.5 + b.arc.ring * 2), R = b.arc.R, th = b.arc.lower ? a - g.c / R : a + g.c / R;
      x = Math.cos(th) * R;
      y = Math.sin(th) * R;
      rot = b.arc.lower ? th - Math.PI / 2 : th + Math.PI / 2;
    } else {
      const dx = b.vert ? 0 : Math.cos(b.rot), dy = b.vert ? 1 : Math.sin(b.rot);
      x = b.x + dx * g.c;
      y = b.y + dy * g.c;
      rot = b.vert ? 0 : b.rot;
      if (b.wave) {
        const ph = x * b.wave.k - t * b.wave.w + b.wave.ph;
        y += Math.sin(ph) * b.wave.amp;
        rot += Math.cos(ph) * b.wave.amp * b.wave.k * 0.6;
      }
    }
    x += g.ex * off;
    y += g.ey * off;
    rot += g.er * off;
    const grows = b.role !== "support" && (sh.kind === "impact" || sh.kind === "slice");
    let sc = grows ? 0.52 + p * 0.48 : 0.86 + p * 0.14;
    if (!grows && !b.arc && b.role !== "support" && t >= g.s) sc *= 1 + (b.role === "hero" ? 0.26 : 0.1) * s.intensity * (1 - softOut((t - g.s) / 0.45));
    if (s.fx.highlight && t >= g.s && !b.arc) {
      const age = t - g.s, dur = Math.max(0.12, g.e - g.s), amp = (b.role === "hero" ? 0.1 : b.role === "semi" ? 0.07 : 0.045) * s.intensity;
      sc += amp * 0.9 * (age / 0.14) * Math.exp(1 - age / 0.14);
      if (age < dur + 0.25) {
        const u = clamp01(age / dur), hold = clamp01((dur - 0.35) / 0.6), env = 1 - clamp01((age - dur) / 0.25);
        y -= Math.sin(u * Math.PI) * b.px * (b.role === "support" ? 0.035 : 0.05) * s.intensity * (b.vert || b.rot ? 0 : 1);
        sc += hold * env * 0.012 * Math.sin(age * 6) * s.intensity;
      }
    }
    const out = exitProgress(sh, b, g, t);
    return { x, y, rot, sc, p, alpha: t < g.s ? 0 : smoothstep((t - g.s) / 0.22) * (0.78 + p * 0.22) * (1 - out) };
  };
  var exitProgress = (sh, b, g, t) => {
    if (sh.exitAt === void 0 || !sh.exitDur || t <= sh.exitAt) return 0;
    const lag = Math.min(0.45, b.seg.idx * 0.05 + g.i * 0.02) * sh.exitDur;
    return smoothstep((t - sh.exitAt - lag) / (sh.exitDur * 0.55));
  };
  var toLayer = (s, px, py) => {
    const { tx, ty, cr, cs } = s.cam;
    const dx = px - tx, dy = py - ty, c = Math.cos(-cr), sn = Math.sin(-cr);
    return { x: (dx * c - dy * sn) / cs, y: (dx * sn + dy * c) / cs };
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/blossom/blossomLayouts.ts
  var columnFlow = (s, list, limitH) => {
    const cols = [[]];
    let cur = 0;
    list.forEach((b) => {
      const c = cols[cols.length - 1];
      if (c.length && cur + b.h > limitH) {
        cols.push([]);
        cur = 0;
      }
      cols[cols.length - 1].push(b);
      cur += b.h + SGAP(s);
    });
    return cols;
  };
  var rowFlow = (s, list, maxW) => {
    const rows = [[]];
    let cur = 0;
    list.forEach((b) => {
      if (rows[rows.length - 1].length && cur + b.w > maxW) {
        rows.push([]);
        cur = 0;
      }
      rows[rows.length - 1].push(b);
      cur += b.w + GAP(s);
    });
    return rows.filter((r) => r.length);
  };
  var stackAroundHero = (s, boxes, hero, hi, maxW, left, right, up, down, tuck) => {
    const pre = rowFlow(s, boxes.slice(0, hi), maxW), post = rowFlow(s, boxes.slice(hi + 1), maxW);
    let y = hero.y - hero.h / 2 - SGAP(s) * tuck;
    for (let k = pre.length - 1; k >= 0; k--) {
      const r = pre[k], rh = Math.max(...r.map((b) => b.h));
      let x = left;
      r.forEach((b) => {
        b.x = x + b.w / 2;
        b.y = y - rh / 2;
        x += b.w + GAP(s);
        b.ey = up;
      });
      y -= rh + SGAP(s);
    }
    y = hero.y + hero.h / 2 + SGAP(s) * tuck;
    post.forEach((r) => {
      const rh = Math.max(...r.map((b) => b.h)), rw = r.reduce((a, b) => a + b.w + GAP(s), -GAP(s));
      let x = right - rw;
      r.forEach((b) => {
        b.x = x + b.w / 2;
        b.y = y + rh / 2;
        x += b.w + GAP(s);
        b.ey = down;
      });
      y += rh + SGAP(s);
    });
  };
  var centredRows = (s, rows, top, ey) => {
    let y = top;
    rows.forEach((r) => {
      const rh = Math.max(...r.map((b) => b.h)), rw = r.reduce((a, b) => a + b.w + GAP(s), -GAP(s));
      let x = -rw / 2;
      r.forEach((b) => {
        b.x = x + b.w / 2;
        b.y = y + rh / 2;
        x += b.w + GAP(s);
        b.ey = ey;
      });
      y += rh + SGAP(s);
    });
    return y - SGAP(s);
  };
  var park = (_s, boxes) => boxes.forEach((b) => {
    b.x = 0;
    b.y = 0;
    b.ex = b.ey = 0;
    b.skip = true;
  });
  var impact = (s, boxes, hero, hi) => {
    hero.x = 0;
    hero.y = s.H * 0.02;
    hero.ey = s.H * 0.15;
    const maxW = Math.max(hero.w, s.W * 0.6);
    stackAroundHero(s, boxes, hero, hi, maxW, -maxW / 2, maxW / 2, -24, 24, 1);
  };
  var layouts = {
    editorial(s, boxes, hero, hi, ctx) {
      const { W, H } = s;
      hero.x = -W * 0.12 * ctx.mir;
      hero.y = 0;
      hero.ey = H * 0.15;
      const limit = H * 0.74, cw = (c) => Math.max(...c.map((b) => b.w));
      const pc = columnFlow(s, boxes.slice(0, hi), limit);
      let x = hero.x + ctx.mir * (hero.w / 2 + GAP(s));
      for (let k = pc.length - 1; k >= 0; k--) {
        const c = pc[k], w = cw(c);
        let y = -limit / 2;
        c.forEach((b) => {
          b.x = x + ctx.mir * w / 2;
          b.y = y + b.h / 2;
          y += b.h + SGAP(s);
          b.ex = 30 * ctx.mir;
        });
        x += ctx.mir * (w + GAP(s));
      }
      const qc = columnFlow(s, boxes.slice(hi + 1), limit);
      x = hero.x - ctx.mir * (hero.w / 2 + GAP(s));
      qc.forEach((c) => {
        const w = cw(c);
        let y = -limit / 2;
        c.forEach((b) => {
          b.x = x - ctx.mir * w / 2;
          b.y = y + b.h / 2;
          y += b.h + SGAP(s);
          b.ex = -30 * ctx.mir;
        });
        x -= ctx.mir * (w + GAP(s));
      });
    },
    impact,
    ribbon(s, boxes, hero, hi) {
      hero.x = 0;
      hero.y = 0;
      hero.ey = s.H * 0.1;
      const g = s.BASE * 0.9;
      let x = hero.x - hero.w / 2 - g;
      for (let i = hi - 1; i >= 0; i--) {
        const b = boxes[i];
        b.x = x - b.w / 2;
        b.y = i % 2 ? 10 : -10;
        x -= b.w + g;
        b.ex = 30;
      }
      x = hero.x + hero.w / 2 + g;
      for (let i = hi + 1; i < boxes.length; i++) {
        const b = boxes[i];
        b.x = x + b.w / 2;
        b.y = i % 2 ? 10 : -10;
        x += b.w + g;
        b.ex = 30;
      }
    },
    tableau(s, boxes, hero, _hi, ctx) {
      const gap = s.BASE * 1.2, tot = boxes.reduce((a, b) => a + b.w + gap, -gap);
      let x = tot / 2 * ctx.mir;
      boxes.forEach((b, i) => {
        b.x = x - ctx.mir * b.w / 2;
        x -= ctx.mir * (b.w + gap);
        b.y = b === hero ? -s.H * 0.05 : (i % 2 ? 1 : -1) * s.H * 0.07;
        b.ey = 20;
      });
    },
    collage(s, boxes, hero, _hi, ctx) {
      const { W, H } = s;
      hero.x = 0;
      hero.y = 0;
      hero.ey = H * 0.12;
      const placed = [{ l: -hero.w / 2, r: hero.w / 2, t: -hero.h / 2, b: hero.h / 2 }], r = ctx.r;
      let ang = r() * 6.28;
      const rx = W * 0.36, ry = H * 0.34, pad = s.BASE * 0.5;
      boxes.forEach((b, i) => {
        if (b === hero) return;
        b.rot = b.rot || (i % 3 === 0 ? 0 : (r() - 0.5) * 0.24);
        for (let k = 0; k < 90; k++) {
          const a = ang + k * 0.22, grow = 1 + k * 4e-3, x = Math.cos(a) * rx * grow, y = Math.sin(a) * ry * grow;
          const rc = { l: x - b.w / 2 - pad, r: x + b.w / 2 + pad, t: y - b.h / 2 - pad, b: y + b.h / 2 + pad };
          if (!placed.some((p) => rc.l < p.r && rc.r > p.l && rc.t < p.b && rc.b > p.t) || k === 89) {
            b.x = x;
            b.y = y;
            placed.push(rc);
            ang = a + 0.9;
            break;
          }
        }
        b.ex = Math.cos(ang) * 26;
        b.ey = Math.sin(ang) * 26;
      });
    },
    poster(s, boxes, hero, _hi, ctx) {
      const { W, H } = s;
      hero.x = -W * 0.2 * ctx.mir;
      hero.y = 0;
      hero.ey = H * 0.15;
      hero.block = { x: hero.x, w: Math.max(W * 0.34, hero.w + s.BASE * 2), h: H * 0.86 };
      const sup = boxes.filter((b) => b !== hero), sh = sup.reduce((a, b) => a + b.h + SGAP(s) * 1.6, 0);
      let y = -sh / 2;
      sup.forEach((b, i) => {
        b.x = ctx.mir * (W * 0.2 + (i % 2 ? W * 0.04 : -W * 0.02));
        b.y = y + b.h / 2;
        y += b.h + SGAP(s) * 1.6;
        b.ex = 40 * ctx.mir;
        b.rule = true;
      });
    },
    orbit(s, boxes, hero, _hi, ctx) {
      hero.x = 0;
      hero.y = 0;
      hero.ey = s.H * 0.12;
      const sup = boxes.filter((b) => b !== hero), n = sup.length, a0 = ctx.r() * 6.28;
      sup.forEach((b, i) => {
        const ring = i % 2, R = s.H * (ring ? 0.42 : 0.31), a = a0 + i * 6.2832 / Math.max(1, n);
        b.arc = { R, a0: a, ring, lower: Math.sin(a) > 0.2 };
        b.x = Math.cos(a) * R;
        b.y = Math.sin(a) * R;
        b.rot = 0;
        b.ex = 0;
        b.ey = 0;
      });
    },
    slice(s, boxes, hero, hi) {
      hero.x = 0;
      hero.y = -s.H * 0.04;
      hero.ey = 0;
      hero.slice = 4;
      const maxW = Math.max(hero.w, s.W * 0.6);
      stackAroundHero(s, boxes, hero, hi, maxW, -hero.w / 2, hero.w / 2, -16, 16, 0.8);
    },
    aperture(s, boxes, hero, _hi, ctx) {
      hero.x = 0;
      hero.y = -s.H * 0.06;
      hero.ey = 0;
      hero.aperture = { ph: ctx.r() * 6.28 };
      const sup = boxes.filter((b) => b !== hero), tot = sup.reduce((a, b) => a + b.w + GAP(s) * 1.4, -GAP(s) * 1.4);
      let x = -tot / 2;
      sup.forEach((b) => {
        b.x = x + b.w / 2;
        b.y = s.H * 0.3;
        x += b.w + GAP(s) * 1.4;
        b.ey = 20;
        b.rule = true;
      });
    },
    shatter(s, boxes, hero, hi, ctx) {
      impact(s, boxes, hero, hi, ctx);
      hero.shatter = true;
    },
    tunnel: park,
    pendulum: park,
    swarm: park,
    /** A film title: the hero alone in the middle, the rest of the line set small and level above and below it, like a subtitle. */
    cinema(s, boxes, hero, hi) {
      const { W, H } = s, maxW = W * 0.72, pre = rowFlow(s, boxes.slice(0, hi), maxW), post = rowFlow(s, boxes.slice(hi + 1), maxW);
      hero.x = 0;
      hero.y = -H * 0.02;
      hero.ey = H * 0.05;
      const preH = pre.reduce((a, r) => a + Math.max(...r.map((b) => b.h)) + SGAP(s), 0);
      centredRows(s, pre, hero.y - hero.h / 2 - SGAP(s) * 1.4 - preH, -14);
      centredRows(s, post, hero.y + hero.h / 2 + SGAP(s) * 1.6, 14);
    },
    /** The line laid along a swelling wave: one or two centred rows whose baseline rises and falls, and keeps moving as it is sung. */
    wave(s, boxes, _hero, _hi, ctx) {
      const { W, H } = s, rows = rowFlow(s, boxes, W * 0.84), total = rows.reduce((a, r) => a + Math.max(...r.map((b) => b.h)) + SGAP(s) * 2.2, -SGAP(s) * 2.2);
      let y = -total / 2;
      const ph = ctx.r() * 6.28, k = 6.2832 / (W * 0.62);
      rows.forEach((r) => {
        const rh = Math.max(...r.map((b) => b.h)), rw = r.reduce((a, b) => a + b.w + GAP(s), -GAP(s));
        let x = -rw / 2;
        r.forEach((b) => {
          b.x = x + b.w / 2;
          b.y = y + rh / 2 + Math.sin(b.x * k + ph) * H * 0.045;
          x += b.w + GAP(s);
          b.ey = H * 0.04;
          b.wave = { amp: Math.min(H * 0.022, b.px * 0.16), k, w: 1.6, ph };
        });
        y += rh + SGAP(s) * 2.2;
      });
    },
    /**
     * A list poem: a word to a line (small particles ride on the line before them), flush to one side, the hero breaking out at full
     * size on its own line; each line slides in from the edge.
     */
    stack(s, boxes, hero, _hi, ctx) {
      const { H } = s, gap = SGAP(s) * 0.7, lines = [];
      boxes.forEach((b) => {
        const last = lines[lines.length - 1], small = b !== hero && (b.seg.minor || b.seg.vis <= 1);
        if (last && small && !last.includes(hero)) last.push(b);
        else lines.push([b]);
      });
      const lineW = (l) => l.reduce((a, b) => a + b.w + GAP(s) * 0.5, -GAP(s) * 0.5);
      const total = lines.reduce((a, l) => a + Math.max(...l.map((b) => b.h)) + gap, -gap), maxW = Math.max(...lines.map(lineW));
      let y = -total / 2;
      lines.forEach((l, i) => {
        const lh = Math.max(...l.map((b) => b.h)), indent = l.includes(hero) ? 0 : s.BASE * (0.6 + i % 3 * 0.5);
        let x = -maxW / 2 + indent;
        l.forEach((b) => {
          b.x = ctx.mir * (x + b.w / 2);
          b.y = y + lh / 2;
          x += b.w + GAP(s) * 0.5;
          b.ex = -ctx.mir * s.BASE * 1.6;
          b.ey = 0;
        });
        y += lh + gap;
      });
      hero.ex = 0;
      hero.ey = H * 0.06;
    },
    stair(s, boxes, hero, hi, ctx) {
      boxes.forEach((b, i) => {
        if (b === hero) return;
        const f = 0.34 + 0.66 * Math.exp(-Math.abs(i - hi) * 0.55) * 0.55;
        scaleBox(b, f * (b.role === "support" ? 1.45 : 1));
      });
      let x = 0, y = 0;
      const pts = boxes.map((b) => {
        const p = { b, x: x + b.w / 2, y: y + b.h / 2 };
        x += b.w * 0.62 + s.BASE * 0.2;
        y += b.h * 0.52;
        return p;
      });
      const hp = pts[hi], cx = hp.x, cy = hp.y;
      pts.forEach((p) => {
        p.b.x = (p.x - cx) * ctx.mir;
        p.b.y = p.y - cy;
        p.b.ex = -26 * ctx.mir;
        p.b.ey = -16;
      });
      hero.ey = s.H * 0.1;
    }
  };
  var fitAll = (s, boxes, kind) => {
    const { W, H } = s;
    if (kind === "swarm" || kind === "tunnel" || kind === "pendulum") return;
    if (kind === "ribbon") {
      const maxW = W * 0.82;
      boxes.forEach((b) => {
        if (b.w > maxW) scaleBox(b, maxW / b.w);
      });
      return;
    }
    boxes.forEach((b) => {
      if (b.arc) return;
      if (b.w > W * 0.82 || b.h > H * 0.82) {
        const f2 = Math.min(W * 0.82 / b.w, H * 0.82 / b.h), x = b.x, y = b.y;
        scaleBox(b, f2);
        b.x = x;
        b.y = y;
      }
    });
    if (kind === "orbit") return;
    let l = 1e9, r = -1e9, t = 1e9, bt = -1e9;
    boxes.forEach((b) => {
      l = Math.min(l, b.x - b.w / 2);
      r = Math.max(r, b.x + b.w / 2);
      t = Math.min(t, b.y - b.h / 2);
      bt = Math.max(bt, b.y + b.h / 2);
    });
    const f = Math.min(1, W * 0.92 / Math.max(1, r - l), H * 0.82 / Math.max(1, bt - t));
    if (f < 1) boxes.forEach((b) => scaleBox(b, f));
    t = 1e9;
    bt = -1e9;
    boxes.forEach((b) => {
      t = Math.min(t, b.y - b.h / 2);
      bt = Math.max(bt, b.y + b.h / 2);
    });
    const lim = H * 0.4, dy = t < -lim ? -lim - t : bt > lim ? lim - bt : 0;
    if (dy && bt - t <= lim * 2) boxes.forEach((b) => {
      b.y += dy;
    });
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/blossom/blossomIcons.ts
  var arcPts = (cx, cy, r, a0, a1, n = 48) => Array.from({ length: n + 1 }, (_, i) => {
    const a = a0 + (a1 - a0) * i / n;
    return [cx + Math.cos(a) * r, cy + Math.sin(a) * r];
  });
  var ICONS = {
    star: { name: "STAR \xB7 \u661F", strokes: [[[0, -1], [0.2, -0.2], [1, 0], [0.2, 0.2], [0, 1], [-0.2, 0.2], [-1, 0], [-0.2, -0.2], [0, -1]], arcPts(0, 0, 0.55, 0, TAU)], dots: [] },
    city: {
      name: "CITY \xB7 \u8857",
      strokes: [[[-1, 0.8], [-1, 0.1], [-0.7, 0.1], [-0.7, -0.3], [-0.4, -0.3], [-0.4, 0.3], [-0.2, 0.3], [-0.2, -0.7], [0.1, -0.7], [0.1, 0], [0.35, 0], [0.35, -0.45], [0.7, -0.45], [0.7, 0.4], [1, 0.4], [1, 0.8], [-1, 0.8]]],
      dots: [[-0.1, -0.4], [-0.1, -0.1], [0.5, -0.2], [0.5, 0.1], [-0.55, 0]]
    },
    wave: {
      name: "VOICE \xB7 \u58F0",
      strokes: Array.from({ length: 13 }, (_, i) => {
        const x = -1 + i / 6, h = 0.15 + 0.8 * Math.abs(Math.sin(i * 1.3 + 0.4)) * (1 - Math.abs(x) * 0.35);
        return [[x, -h], [x, h]];
      }),
      dots: []
    },
    wind: {
      name: "WIND \xB7 \u98CE",
      strokes: [[...arcPts(0.25, -0.5, 0.25, 1.5708, -3.4), [-1, -0.75]].reverse(), [[-1, -0.25], [0.55, -0.25], ...arcPts(0.55, -0.02, 0.23, -1.5708, 3, 24)], [[-0.7, 0.3], [0.2, 0.3], ...arcPts(0.2, 0.52, 0.22, -1.5708, 3.2, 24)]],
      dots: []
    },
    rain: {
      name: "RAIN \xB7 \u96E8",
      strokes: [[[-0.9, -0.3], [-0.9, -0.5], [-0.5, -0.8], [0, -0.7], [0.5, -0.85], [0.9, -0.5], [0.9, -0.3], [-0.9, -0.3]], ...[-0.7, -0.3, 0.1, 0.5, 0.85].map((x, i) => [[x, 0 + i * 0.05], [x - 0.18, 0.55 + i * 0.05]])],
      dots: []
    },
    light: {
      name: "LIGHT \xB7 \u5149",
      strokes: [arcPts(0, 0, 0.42, 0, TAU), ...Array.from({ length: 10 }, (_, i) => {
        const a = i * 0.6283;
        return [[Math.cos(a) * 0.65, Math.sin(a) * 0.65], [Math.cos(a) * (i % 2 ? 0.9 : 1.05), Math.sin(a) * (i % 2 ? 0.9 : 1.05)]];
      })],
      dots: []
    },
    home: { name: "HOME \xB7 \u5E30", strokes: [[[-0.9, 0.8], [-0.9, -0.1], [0, -0.9], [0.9, -0.1], [0.9, 0.8], [-0.9, 0.8]], [[-0.22, 0.8], [-0.22, 0.25], [0.22, 0.25], [0.22, 0.8]]], dots: [[0.55, 0.1]] }
  };
  var ICON_RULES = [
    ["rain", /rain|雨/i],
    ["star", /星|star/i],
    ["city", /街|city|灯/i],
    ["home", /home|帰|回家/i],
    ["light", /光|light|neon/i],
    ["wind", /风|風|wind|run/i],
    ["wave", /声|響|hums|silence|voice/i]
  ];
  var iconForText = (text) => {
    for (const [name, re] of ICON_RULES) if (re.test(text)) return name;
    return null;
  };
  var strokeProgress = (g, pts, p) => {
    if (p <= 0 || pts.length < 2) return;
    let total = 0;
    const seg = [];
    for (let i = 1; i < pts.length; i++) {
      const d = Math.hypot(pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1]);
      seg.push(d);
      total += d;
    }
    let rem = total * clamp01(p);
    g.beginPath();
    g.moveTo(pts[0][0], pts[0][1]);
    for (let i = 1; i < pts.length; i++) {
      if (rem >= seg[i - 1]) {
        g.lineTo(pts[i][0], pts[i][1]);
        rem -= seg[i - 1];
      } else {
        const u = rem / seg[i - 1];
        g.lineTo(pts[i - 1][0] + (pts[i][0] - pts[i - 1][0]) * u, pts[i - 1][1] + (pts[i][1] - pts[i - 1][1]) * u);
        break;
      }
    }
    g.stroke();
  };
  var drawIcon = (s, sh, P, acc) => {
    const a = sh.anchor, g = s.g, t = s.t;
    if (!a || !sh.icon) return;
    const ic = ICONS[sh.icon], hb = sh.boxes[sh.hero], s0 = hb.glyphs[0].s - 0.1, p = clamp01((t - s0) / 1.3);
    if (p <= 0) return;
    g.save();
    g.translate(s.W / 2 + a.x, s.H / 2 + a.y);
    g.scale(a.s * 0.8, a.s * 0.8);
    g.lineWidth = 1.1 / (a.s * 0.8);
    g.strokeStyle = acc;
    g.lineCap = "round";
    g.lineJoin = "round";
    g.globalAlpha = 0.72 * s.fade;
    ic.strokes.forEach((pts, i) => strokeProgress(g, pts, clamp01(p * 1.4 - i * 0.12)));
    g.fillStyle = acc;
    ic.dots.forEach(([x, y]) => {
      if (p > 0.7) {
        g.beginPath();
        g.arc(x, y, 0.045, 0, TAU);
        g.fill();
      }
    });
    g.restore();
    g.save();
    g.font = `500 ${Math.max(9, s.H * 0.0115)}px ${s.fonts.mono}`;
    g.textAlign = "center";
    g.fillStyle = P.sub;
    g.globalAlpha = 0.6 * clamp01((p - 0.4) * 2) * s.fade;
    g.fillText(ic.name, s.W / 2 + a.x, s.H / 2 + a.y + a.s * 1.05);
    g.restore();
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/blossom/blossomSpecial.ts
  var shatterCells = (st) => {
    st.cells = voronoi(st.seeds, st.bx, st.by).map((poly, i) => {
      const [cx, cy] = polygonCentroid(poly, st.seeds[i]);
      const dx = cx + (hash("d" + i) % 100 - 50) / 160, dy = cy + (hash("e" + i) % 100 - 50) / 160 - 1e-4, L = Math.hypot(dx, dy) || 1;
      return { poly, cx, cy, dx: dx / L, dy: dy / L, spin: hash("s" + i) % 1e3 / 1e3 - 0.5, delay: (cx / st.bx + 0.5) * 0.5 + hash("t" + i) % 100 / 1500 };
    });
  };
  var shatterBuild = (hb, r) => {
    const rw = (hb.rot ? hb.h : hb.w) + hb.px * 0.5, rh = (hb.rot ? hb.w : hb.h) + hb.px * 0.5;
    const st = { bx: rw, by: rh, seeds: mitchell(24, rw, rh, r), cells: [], ev: [] };
    shatterCells(st);
    return st;
  };
  var shatterHit = (s, sh, L) => {
    const b = sh.boxes[sh.hero], st = sh.shatter;
    if (!st) return;
    st.ev.push({ x: L.x, y: L.y, t0: s.now });
    const lx = L.x - b.x, ly = L.y - b.y;
    if (st.seeds.length < 56 && Math.abs(lx) < st.bx / 2 && Math.abs(ly) < st.by / 2) {
      for (let i = 0; i < 6; i++) {
        const a = Math.random() * 6.28, d = b.px * (0.12 + Math.random() * 0.35);
        st.seeds.push([Math.max(-st.bx / 2 + 2, Math.min(st.bx / 2 - 2, lx + Math.cos(a) * d)), Math.max(-st.by / 2 + 2, Math.min(st.by / 2 - 2, ly + Math.sin(a) * d))]);
      }
      shatterCells(st);
    }
  };
  var drawShatter = (s, sh, P, acc, bt) => {
    const g = s.g, st = sh.shatter, b = sh.boxes[sh.hero], t = s.t;
    if (!st || !b) return;
    const s0 = b.glyphs[0].s, I = s.intensity, fin = b.glyphs.map((gl) => pose(s, b, gl, gl.settle + 5, sh)), chorus = sh.def.chorus;
    st.ev = st.ev.filter((e) => s.now - e.t0 < 1800);
    st.cells.forEach((c) => {
      const a = clamp01((t - s0 - c.delay) / 0.2);
      if (a <= 0) return;
      const u = expoOut(clamp01((t - s0 - c.delay) / 0.75));
      let ox = (1 - u) * b.px * 2.4 * c.dx * I, oy = (1 - u) * b.px * 2.4 * c.dy * I, rot = (1 - u) * c.spin * 1.4;
      const pl = bt * 0.05 * b.px * (chorus ? 1 : 0.5) * I;
      ox += c.dx * pl;
      oy += c.dy * pl;
      st.ev.forEach((e) => {
        const wx = b.x + c.cx, wy = b.y + c.cy, ex = wx - e.x, ey = wy - e.y, d = Math.hypot(ex, ey) || 1;
        const w = Math.exp(-d * d / (2 * (b.px * 1.3) ** 2)), age = (s.now - e.t0) / 1e3, f = age / 0.18 * Math.exp(1 - age / 0.18);
        ox += ex / d * b.px * 1.3 * w * f * I;
        oy += ey / d * b.px * 1.3 * w * f * I;
        rot += c.spin * w * f * 1.5;
      });
      g.save();
      g.translate(s.W / 2 + b.x + ox, s.H / 2 + b.y + oy);
      g.translate(c.cx, c.cy);
      g.rotate(rot);
      g.translate(-c.cx, -c.cy);
      g.beginPath();
      c.poly.forEach((p, i) => i ? g.lineTo(p[0], p[1]) : g.moveTo(p[0], p[1]));
      g.closePath();
      g.save();
      g.clip();
      g.globalAlpha = a;
      g.fillStyle = P.ink;
      g.font = b.font;
      g.textAlign = "center";
      g.textBaseline = "middle";
      b.glyphs.forEach((gl, i) => {
        const f = fin[i];
        g.save();
        g.translate(f.x - b.x, f.y - b.y);
        g.rotate(f.rot);
        g.fillText(gl.ch, 0, 0);
        g.restore();
      });
      g.restore();
      g.strokeStyle = acc;
      g.lineWidth = 1;
      g.globalAlpha = a * (0.22 + 0.5 * bt + 0.6 * (1 - u));
      g.stroke();
      g.restore();
    });
    if (st.ev.length) {
      g.save();
      g.translate(s.W / 2 + b.x, s.H / 2 + b.y);
      g.fillStyle = acc;
      g.globalAlpha = 0.8;
      st.seeds.forEach((sd) => {
        g.beginPath();
        g.arc(sd[0], sd[1], 2.2, 0, TAU);
        g.fill();
      });
      g.restore();
    }
  };
  var SWARM_N = 1200;
  var swarmPositions = /* @__PURE__ */ new WeakMap();
  var positionsFor = (sw) => {
    let positions = swarmPositions.get(sw);
    if (!positions || positions.length !== sw.N * 2) {
      positions = new Float64Array(sw.N * 2);
      swarmPositions.set(sw, positions);
    }
    return positions;
  };
  var swarmTarget = (sw, t) => {
    let target = -1;
    for (let i = 0; i < sw.ts.length; i++) if (t >= sw.ts[i]) target = i;
    return target;
  };
  var swarmBuild = (s, boxes, r) => {
    const k = 0.5, cw = Math.max(8, Math.ceil(s.W * k)), ch = Math.max(8, Math.ceil(s.H * 0.8 * k));
    const cv = document.createElement("canvas");
    cv.width = cw;
    cv.height = ch;
    const c = cv.getContext("2d", { willReadFrequently: true });
    const targets = boxes.map((b) => {
      const out = new Float32Array(SWARM_N * 2);
      if (!c) return out;
      let px = s.H * 0.3 * k;
      c.font = `900 ${px}px ${s.fonts.sans}`;
      const mw = c.measureText(b.seg.text).width;
      if (mw > cw * 0.72) px *= cw * 0.72 / mw;
      c.clearRect(0, 0, cw, ch);
      c.font = `900 ${px}px ${s.fonts.sans}`;
      c.textAlign = "center";
      c.textBaseline = "middle";
      c.fillStyle = "#fff";
      c.fillText(b.seg.text, cw / 2, ch / 2);
      const hw = Math.min(cw / 2, Math.min(mw, cw * 0.72) / 2 + 4), hh = Math.min(ch / 2, px * 0.8 + 2);
      const rx = Math.max(0, Math.floor(cw / 2 - hw)), ry = Math.max(0, Math.floor(ch / 2 - hh)), rw = Math.min(cw - rx, Math.ceil(hw * 2)), rh = Math.min(ch - ry, Math.ceil(hh * 2));
      const d = c.getImageData(rx, ry, rw, rh).data;
      let pts = [];
      for (let y = 0; y < rh; y++) for (let x = 0; x < rw; x++) if (d[(y * rw + x) * 4 + 3] > 110) pts.push([(x + rx - cw / 2) / k, (y + ry - ch / 2) / k]);
      if (!pts.length) pts = [[0, 0]];
      let sel = [];
      if (pts.length > SWARM_N) {
        for (let i = 0; i < SWARM_N; i++) {
          const j = i + Math.floor(r() * (pts.length - i));
          const tmp = pts[i];
          pts[i] = pts[j];
          pts[j] = tmp;
        }
        sel = pts.slice(0, SWARM_N);
      } else {
        for (let i = 0; i < SWARM_N; i++) {
          const p = pts[i % pts.length];
          sel.push(i < pts.length ? p : [p[0] + (r() - 0.5) * 3, p[1] + (r() - 0.5) * 3]);
        }
      }
      const keyed = sel.map((p) => ({
        p,
        key: hilbert(256, Math.max(0, Math.min(255, Math.floor((p[0] / (s.W * 0.5) + 1) * 128))), Math.max(0, Math.min(255, Math.floor((p[1] / (s.H * 0.4) + 1) * 128))))
      })).sort((a, b2) => a.key - b2.key);
      keyed.forEach((o, i) => {
        out[2 * i] = o.p[0];
        out[2 * i + 1] = o.p[1];
      });
      return out;
    });
    cv.width = 0;
    cv.height = 0;
    const f = () => new Float32Array(SWARM_N);
    const sw = { N: SWARM_N, targets, ts: boxes.map((b) => b.seg.start - 0.2), ang: f(), rad: f(), spin: f(), dly: f(), arc: f(), ox: f(), oy: f(), vx: f(), vy: f() };
    for (let i = 0; i < SWARM_N; i++) {
      sw.ang[i] = r() * TAU;
      sw.rad[i] = s.H * (0.12 + 0.34 * Math.sqrt(r()));
      sw.spin[i] = (0.07 + r() * 0.12) * (i % 2 ? 1 : -1);
      sw.dly[i] = i / SWARM_N * 0.34 + r() * 0.04;
      sw.arc[i] = (r() - 0.5) * 2;
    }
    return sw;
  };
  var swarmBase = (sw, k, t, t1, j, out) => {
    const ws = sw.ts, offset = 2 * k;
    let x, y;
    if (j < 0) {
      const a = sw.ang[k] + t * sw.spin[k], R = sw.rad[k];
      x = Math.cos(a) * R * 1.55;
      y = Math.sin(a) * R * 0.92;
    } else {
      const tg = sw.targets[j], bx = tg[2 * k], by = tg[2 * k + 1];
      let ax, ay;
      if (j) {
        ax = sw.targets[j - 1][2 * k];
        ay = sw.targets[j - 1][2 * k + 1];
      } else {
        const a = sw.ang[k] + ws[0] * sw.spin[k], R = sw.rad[k];
        ax = Math.cos(a) * R * 1.55;
        ay = Math.sin(a) * R * 0.92;
      }
      const u = clamp01((t - ws[j] - sw.dly[k]) / 0.8), e = easeIO(u), dx = bx - ax, dy = by - ay, L = Math.hypot(dx, dy) || 1, arc = Math.sin(u * Math.PI) * L * 0.22 * sw.arc[k];
      x = ax + dx * e - dy / L * arc;
      y = ay + dy * e + dx / L * arc;
      if (u >= 1) {
        x += Math.sin(t * 2.1 + k) * 0.9;
        y += Math.cos(t * 1.7 + k * 1.3) * 0.9;
      }
    }
    const te = t1 - 0.55;
    if (t > te) {
      const u = easeIO(clamp01((t - te) / 0.5)), a = sw.ang[k] + t * sw.spin[k], R = sw.rad[k];
      x += (Math.cos(a) * R * 1.55 * 1.4 - x) * u;
      y += (Math.sin(a) * R * 0.92 * 1.4 - y) * u;
    }
    out[offset] = x;
    out[offset + 1] = y;
  };
  var drawSwarm = (s, sh, P, acc, dt, bt) => {
    const g = s.g, sw = sh.swarm, t = s.t;
    if (!sw) return;
    const N = sw.N, L = toLayer(s, s.ptr.hx, s.ptr.hy), use = s.fx.hover && s.ptr.over, R = 120, I = s.intensity;
    const h = Math.min(0.05, dt || 0.016), t1 = sh.def.t1, positions = positionsFor(sw), target = swarmTarget(sw, t);
    for (let k = 0; k < N; k++) {
      swarmBase(sw, k, t, t1, target, positions);
      const offset = 2 * k;
      let fx = 0, fy = 0;
      if (use) {
        const dx = positions[offset] + sw.ox[k] - L.x, dy = positions[offset + 1] + sw.oy[k] - L.y, d = Math.hypot(dx, dy) || 1;
        if (s.ptr.down) {
          const a = Math.min(1, d / 220);
          fx -= dx / d * 1700 * a;
          fy -= dy / d * 1700 * a;
          fx += dy / d * 900 * a;
          fy -= dx / d * 900 * a;
        } else if (d < R) {
          const f = Math.pow(1 - d / R, 2) * 7e3 * I;
          fx += dx / d * f;
          fy += dy / d * f;
        }
      }
      sw.vx[k] += (fx - sw.ox[k] * 16 - sw.vx[k] * 4.5) * h;
      sw.vy[k] += (fy - sw.oy[k] * 16 - sw.vy[k] * 4.5) * h;
      sw.ox[k] += sw.vx[k] * h;
      sw.oy[k] += sw.vy[k] * h;
      const m = 520;
      if (sw.ox[k] > m) sw.ox[k] = m;
      else if (sw.ox[k] < -m) sw.ox[k] = -m;
      if (sw.oy[k] > m) sw.oy[k] = m;
      else if (sw.oy[k] < -m) sw.oy[k] = -m;
      positions[offset] += sw.ox[k];
      positions[offset + 1] += sw.oy[k];
    }
    const size = 2.6 * (1 + 0.5 * bt * I);
    g.save();
    g.translate(s.W / 2, s.H / 2);
    for (let bi = 0; bi < 3; bi++) {
      g.beginPath();
      for (let k = bi; k < N; k += 3) g.rect(positions[2 * k] - size / 2, positions[2 * k + 1] - size / 2, size, size);
      const col = bi === 0 ? P.ink : bi === 1 ? acc : P.acc2;
      g.fillStyle = col;
      g.globalAlpha = bi ? 0.9 : 0.95;
      if (bi && s.fx.glow) {
        g.shadowColor = col;
        g.shadowBlur = 9;
      }
      g.fill();
      g.shadowBlur = 0;
    }
    g.restore();
  };
  var swarmBurst = (s, sh, L) => {
    const sw = sh.swarm;
    if (!sw) return;
    const positions = positionsFor(sw), target = swarmTarget(sw, s.t);
    for (let k = 0; k < sw.N; k++) {
      swarmBase(sw, k, s.t, sh.def.t1, target, positions);
      const ex = positions[2 * k] + sw.ox[k] - L.x, ey = positions[2 * k + 1] + sw.oy[k] - L.y, d = Math.hypot(ex, ey) || 1;
      if (d < 320) {
        const f = 1100 * Math.pow(1 - d / 320, 1.4);
        sw.vx[k] += ex / d * f + (Math.random() - 0.5) * 120;
        sw.vy[k] += ey / d * f + (Math.random() - 0.5) * 120;
      }
    }
  };
  var tunnelBuild = (boxes) => {
    const n = boxes.length, hi = boxes.findIndex((b) => b.isHero);
    return { n, z: boxes.map((_, i) => i), ts: boxes.map((b) => b.glyphs[0].s), ang: boxes.map((_, i) => i * 2.399 + 0.6), hi, rr: boxes.map((_, i) => i === hi ? 0 : 0.2 + 0.07 * (i % 3)) };
  };
  var FZ = 1.15;
  var tunnelZ = (tn, t) => {
    const { z, ts, n } = tn;
    if (t <= ts[0]) return z[0] - FZ - (ts[0] - t) * 0.9;
    if (t >= ts[n - 1]) return z[n - 1] - FZ + (t - ts[n - 1]) * 0.9;
    for (let i = 0; i < n - 1; i++) {
      if (t < ts[i + 1]) {
        const u = (t - ts[i]) / (ts[i + 1] - ts[i]);
        return z[i] - FZ + (z[i + 1] - z[i]) * (0.55 * u + 0.45 * easeIO(u));
      }
    }
    return z[n - 1] - FZ;
  };
  var drawTunnel = (s, sh, P, acc, bt) => {
    const g = s.g, tn = sh.tunnel, t = s.t;
    if (!tn) return;
    const { W, H } = s, zc = tunnelZ(tn, t);
    const par = s.fx.parallax ? [(s.ptr.sx - 0.5) * W * 0.14, (s.ptr.sy - 0.5) * H * 0.14] : [0, 0];
    const roll = 0.32 * Math.sin(t * 0.45), cr = Math.cos(roll), sr = Math.sin(roll);
    const proj = (x, y, d) => {
      const k = FZ / d, X = x * k, Y = y * k;
      return [X * cr - Y * sr + par[0] * (1 - 0.5 / d), X * sr + Y * cr + par[1] * (1 - 0.5 / d), k];
    };
    g.save();
    g.translate(W / 2, H / 2);
    g.lineCap = "round";
    const RING = 8.4;
    g.strokeStyle = P.sub;
    for (let j = 0; j < 16; j++) {
      const dw = ((j * 0.55 - zc) % RING + RING) % RING + 0.25, S = H * 0.62, a = Math.min(1, (RING - dw) / 3) * 0.32 * Math.min(1, dw / 0.6), rot = j * 0.14 + t * 0.12;
      g.globalAlpha = a;
      g.lineWidth = 1.2;
      g.beginPath();
      for (let k = 0; k < 4; k++) {
        const an = rot + k * 1.5708 + 0.7854, p = proj(Math.cos(an) * S * 0.74, Math.sin(an) * S * 0.74, dw);
        k ? g.lineTo(p[0], p[1]) : g.moveTo(p[0], p[1]);
      }
      g.closePath();
      g.stroke();
    }
    const sp = Math.abs(tunnelZ(tn, t + 0.05) - tunnelZ(tn, t - 0.05)) * 10;
    g.strokeStyle = acc;
    for (let i = 0; i < 70; i++) {
      const rr = rng("st" + i), an = rr() * TAU, R = H * (0.18 + rr() * 0.5), z0 = rr() * 8, d = ((z0 - zc) % 8 + 8) % 8 + 0.3;
      const p0 = proj(Math.cos(an) * R, Math.sin(an) * R, d), p1 = proj(Math.cos(an) * R, Math.sin(an) * R, d + 0.08 + sp * 0.12);
      g.globalAlpha = Math.min(1, (8 - d) / 4) * (0.15 + 0.35 * Math.min(1, sp)) * Math.min(1, d / 0.8);
      g.lineWidth = 1.1;
      g.beginPath();
      g.moveTo(p0[0], p0[1]);
      g.lineTo(p1[0], p1[1]);
      g.stroke();
    }
    const items = [];
    sh.boxes.forEach((b, i) => {
      [0, 1].forEach((cy) => {
        const d = tn.z[i] + cy * tn.n - zc;
        if (d > 0.18 && d < 7.5) items.push({ b, i, d, cy });
      });
    });
    items.sort((a, b) => b.d - a.d);
    items.forEach(({ b, i, d, cy }) => {
      const R = tn.rr[i] * H, an = tn.ang[i], p = proj(Math.cos(an) * R, Math.sin(an) * R, d), k = p[2], hero = b.isHero, base3 = b.px * (hero ? 1 : 0.8) * k;
      const al = Math.min(1, (6.8 - d) / 2.5) * Math.min(1, (d - 0.18) / 0.22) * (cy ? 0.28 : 1);
      if (al <= 0.01 || base3 < 4) return;
      g.save();
      g.translate(p[0], p[1]);
      g.rotate(roll * 0.6 + (hero ? 0 : Math.sin(an) * 0.18));
      g.font = `900 ${base3}px ${hero ? s.fonts.sans : b.fam}`;
      g.textAlign = "center";
      g.textBaseline = "middle";
      const adv = b.advs.map((a) => a / b.px * base3 * (hero ? 1 : 0.8)), tot = adv.reduce((x, y) => x + y, 0);
      let cur = -tot / 2;
      b.glyphs.forEach((gl, q2) => {
        const cx = cur + adv[q2] / 2;
        cur += adv[q2];
        const lit = t >= gl.s, near = Math.max(0, 1 - Math.abs(d - FZ) * 1.2), lw = Math.max(1, base3 * 0.012);
        if (!cy && lit) {
          g.globalAlpha = al;
          if (s.fx.glow) {
            g.shadowColor = hero ? GLOW_SOFT : GLOW_FAINT;
            g.shadowBlur = Math.min(GLOW_MAX_BLUR, base3 * (0.06 + 0.1 * near));
          }
          g.fillStyle = hero && near <= 0.3 ? acc : P.ink;
          g.fillText(gl.ch, cx, 0);
          g.shadowBlur = 0;
        } else {
          g.globalAlpha = al * (cy ? 1 : 0.8);
          g.strokeStyle = hero ? acc : P.sub;
          g.lineWidth = lw;
          g.strokeText(gl.ch, cx, 0);
        }
      });
      g.restore();
    });
    g.globalAlpha = 0.5;
    g.strokeStyle = acc;
    g.lineWidth = 1;
    const q = H * 0.012 * (1 + bt);
    g.beginPath();
    g.arc(par[0] * 0.5, par[1] * 0.5, H * 0.012 + q, 0, TAU);
    g.stroke();
    g.restore();
  };
  var pendBuild = (s, boxes, r) => {
    const gap = s.BASE * 0.9;
    let tot = boxes.reduce((a, b) => a + b.w + gap, -gap);
    const f = Math.min(1, s.W * 0.86 / tot);
    if (f < 1) boxes.forEach((b) => scaleBox(b, f));
    tot = boxes.reduce((a, b) => a + b.w + gap * f, -gap * f);
    let x = -tot / 2;
    const items = boxes.map((b, i) => {
      const ax = x + b.w / 2;
      x += b.w + gap * f;
      return { b, ax, L: s.H * (b.isHero ? 0.62 : i % 2 ? 0.3 : 0.47), th0: -(0.5 + r() * 0.22) * (b.isHero ? 0.8 : 1), ph: r() * 6.28, ex: 0, ev: 0 };
    });
    return { items, rail: -s.H * 0.4 };
  };
  var pendTheta = (it, t) => {
    const tau = t - it.b.glyphs[0].s;
    if (tau < 0) return null;
    return dampedPendulum(it.th0, tau, 1.15, 3.4 * (1 + 0.1 * Math.sin(it.ph))) + 0.035 * Math.sin(0.9 * t + it.ph) + it.ex;
  };
  var pendStep = (s, sh, dt) => {
    const pd = sh.pend;
    if (!pd) return;
    const L = toLayer(s, s.ptr.hx, s.ptr.hy);
    pd.items.forEach((it) => {
      const th = pendTheta(it, s.t);
      if (th === null) return;
      const om2 = 900 / it.L, px = it.ax + Math.sin(th) * it.L, py = pd.rail + Math.cos(th) * it.L;
      if (s.ptr.over && Math.abs(L.x - px) < it.b.w / 2 + 20 && Math.abs(L.y - py) < it.b.h / 2 + 20) {
        it.ev += Math.max(-4, Math.min(4, s.ptr.vx / (it.L * 1.2))) * 0.6 * s.intensity;
      }
      it.ev += (-om2 * it.ex - 2 * 0.25 * Math.sqrt(om2) * it.ev) * dt;
      it.ex += it.ev * dt;
      it.ex = Math.max(-1.2, Math.min(1.2, it.ex));
    });
  };
  var pendHit = (s, sh, L) => {
    let hit = null;
    const pd = sh.pend;
    if (!pd) return null;
    pd.items.forEach((it) => {
      const th = pendTheta(it, s.t);
      if (th === null) return;
      const px = it.ax + Math.sin(th) * it.L, py = pd.rail + Math.cos(th) * it.L;
      if (Math.abs(L.x - px) < it.b.w / 2 + 12 && Math.abs(L.y - py) < it.b.h / 2 + 12) hit = it;
    });
    return hit;
  };
  var drawPend = (s, sh, P, acc, bt) => {
    const g = s.g, pd = sh.pend, t = s.t;
    if (!pd) return;
    const { W, H } = s;
    g.save();
    g.translate(W / 2, H / 2);
    g.strokeStyle = P.sub;
    g.fillStyle = P.sub;
    g.globalAlpha = 0.45;
    g.lineWidth = 1;
    g.beginPath();
    g.moveTo(-W * 0.46, pd.rail);
    g.lineTo(W * 0.46, pd.rail);
    g.stroke();
    g.lineWidth = 1;
    g.globalAlpha = 0.22;
    g.beginPath();
    for (let x = -W * 0.46; x <= W * 0.46; x += 24) {
      g.moveTo(x, pd.rail - 4);
      g.lineTo(x, pd.rail);
    }
    g.stroke();
    pd.items.forEach((it) => {
      const b = it.b, th = pendTheta(it, t);
      if (th === null) return;
      const hero = b.isHero, ac = clamp01((t - b.glyphs[0].s) / 0.15), bx = it.ax + Math.sin(th) * it.L, by = pd.rail + Math.cos(th) * it.L;
      const hh = b.h / 2, tx = it.ax + Math.sin(th) * (it.L - hh), ty = pd.rail + Math.cos(th) * (it.L - hh);
      g.globalAlpha = 0.6 * ac;
      g.strokeStyle = hero ? acc : P.sub;
      g.lineWidth = hero ? 1.2 : 0.8;
      g.beginPath();
      g.moveTo(it.ax, pd.rail);
      g.quadraticCurveTo((it.ax + tx) / 2 + Math.cos(th) * 3, (pd.rail + ty) / 2, tx, ty);
      g.stroke();
      g.fillStyle = hero ? acc : P.sub;
      g.beginPath();
      g.arc(it.ax, pd.rail, 2.5, 0, TAU);
      g.fill();
      g.save();
      g.translate(bx, H * 0.46);
      g.globalAlpha = 0.14 * ac;
      g.fillStyle = P.sub;
      g.beginPath();
      g.ellipse(0, 0, b.w * 0.42, 6, 0, 0, TAU);
      g.fill();
      g.restore();
      g.save();
      g.translate(bx, by);
      g.rotate(th);
      const tot = b.advs.reduce((x, y) => x + y, 0);
      let cur = -tot / 2;
      g.font = b.font;
      g.textAlign = "center";
      g.textBaseline = "middle";
      g.globalAlpha = 0.6 * ac;
      g.strokeStyle = hero ? acc : P.sub;
      g.lineWidth = 1;
      g.strokeRect(-tot / 2 - b.px * 0.2, -hh - b.px * 0.04, tot + b.px * 0.4, b.h + b.px * 0.08);
      b.glyphs.forEach((gl, k) => {
        const cx = cur + b.advs[k] / 2;
        cur += b.advs[k];
        const lit = t >= gl.s, e = clamp01((t - gl.s) / 0.3);
        if (lit) {
          g.globalAlpha = ac;
          if (s.fx.glow) {
            g.shadowColor = hero ? GLOW_SOFT : GLOW_FAINT;
            g.shadowBlur = Math.min(GLOW_MAX_BLUR, b.px * 0.15 * (0.4 + 0.6 * (1 - e) + 0.3 * bt));
          }
          g.fillStyle = hero ? acc : P.ink;
          g.fillText(gl.ch, cx, 0);
          g.shadowBlur = 0;
        } else {
          g.globalAlpha = 0.7 * ac;
          g.strokeStyle = P.sub;
          g.lineWidth = Math.max(1, b.px * 0.01);
          g.strokeText(gl.ch, cx, 0);
        }
      });
      g.restore();
    });
    g.restore();
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/blossom/blossomTransition.ts
  var IDENTITY = { x: 0, y: 0, r: 0, z: 1 };
  var compose = (a, b) => {
    const c = Math.cos(a.r), s = Math.sin(a.r);
    return { x: a.x + a.z * (c * b.x - s * b.y), y: a.y + a.z * (s * b.x + c * b.y), r: a.r + b.r, z: a.z * b.z };
  };
  var invert = (v) => {
    const c = Math.cos(-v.r), s = Math.sin(-v.r), k = 1 / v.z;
    return { x: -k * (c * v.x - s * v.y), y: -k * (s * v.x + c * v.y), r: -v.r, z: k };
  };
  var applyView = (g, v, W, H) => {
    if (v.x === 0 && v.y === 0 && v.r === 0 && v.z === 1) return;
    g.translate(W / 2 + v.x, H / 2 + v.y);
    g.rotate(v.r);
    g.scale(v.z, v.z);
    g.translate(-W / 2, -H / 2);
  };
  var MOVES = {
    "pan-right": [1, 0.06, 0.025, 1],
    "pan-left": [-1, -0.05, -0.025, 1],
    "tilt-down": [0.08, 1, -0.02, 1],
    "tilt-up": [-0.06, -1, 0.02, 1],
    arc: [0.85, 0.55, 0.1, 1],
    dolly: [0.12, -0.04, 0.03, 0.3],
    rise: [0.04, -0.95, -0.05, 0.86]
  };
  var HALF = {
    "pan-right": 0.72,
    "pan-left": 0.72,
    "tilt-down": 0.72,
    "tilt-up": 0.72,
    arc: 0.8,
    dolly: 0.85,
    rise: 0.85
  };
  var WANDER = ["pan-right", "tilt-down", "arc", "pan-left", "tilt-up", "pan-right", "arc", "tilt-down"];
  var shotTransition = (plan2, b, seed) => {
    const cur = plan2.shots[b], prev = plan2.shots[b - 1];
    if (!cur || !prev) return "pan-right";
    if (cur.rest || prev.rest) return "rise";
    if (cur.chorus && !prev.chorus) return "dolly";
    return WANDER[(b + seed) % WANDER.length];
  };
  var travel = (p) => {
    const v = clamp01(p);
    return v * v * v * (v * (v * 6 - 15) + 10);
  };
  var crossHalf = (plan2, b, seed) => {
    const shots = plan2.shots;
    if (b < 1 || b >= shots.length) return 0;
    const h = Math.min(HALF[shotTransition(plan2, b, seed)], (shots[b - 1].t1 - shots[b - 1].t0) * 0.45, (shots[b].t1 - shots[b].t0) * 0.45);
    return h > 0.05 ? h : 0;
  };
  var crossingAt = (plan2, t, si, seed) => {
    for (const b of [si + 1, si]) {
      const h = crossHalf(plan2, b, seed);
      if (h <= 0) continue;
      const at = plan2.shots[b].t0;
      if (t > at - h && t < at + h) {
        const p = clamp01((t - (at - h)) / (2 * h));
        return { a: b - 1, b, kind: shotTransition(plan2, b, seed), e: travel(p), p, half: h };
      }
    }
    return null;
  };
  var movePlacement = (kind, W, H) => {
    const [mx, my, r, z] = MOVES[kind];
    return { x: mx * W, y: my * H, r, z };
  };
  var partway = (to, u) => ({ x: to.x * u, y: to.y * u, r: to.r * u, z: Math.pow(to.z, u) });
  var BOW = 0.08;
  var cameraAt = (to, u) => {
    const v = partway(to, u), b = BOW * Math.sin(Math.PI * u);
    return { ...v, x: v.x - to.y * b, y: v.y + to.x * b };
  };
  var moveViews = (to, u) => {
    const cam = invert(cameraAt(to, u));
    return { out: cam, in: compose(cam, to) };
  };
  var backgroundView = (v, depth, cover) => ({
    x: v.x * depth,
    y: v.y * depth,
    r: v.r * depth * 2,
    z: Math.pow(v.z, depth) * cover
  });

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/blossom/blossomShot.ts
  var KINDS = {
    editorial: { hs: 4, ss: 1.2 },
    impact: { hs: 5.5, ss: 1.5 },
    ribbon: { hs: 3.5, ss: 1.5 },
    tableau: { hs: 3, ss: 1.15 },
    collage: { hs: 3.2, ss: 1.35 },
    poster: { hs: 4.4, ss: 1.8 },
    orbit: { hs: 4.2, ss: 1.3 },
    slice: { hs: 5.2, ss: 1.4 },
    aperture: { hs: 5, ss: 1.3 },
    stair: { hs: 4, ss: 1.2 },
    shatter: { hs: 4.8, ss: 1.4 },
    swarm: { hs: 4, ss: 1.4 },
    tunnel: { hs: 4.6, ss: 2.8 },
    pendulum: { hs: 4.4, ss: 3 },
    cinema: { hs: 4.4, ss: 1.25 },
    wave: { hs: 3.4, ss: 1.7 },
    stack: { hs: 3.8, ss: 1.6 }
  };
  var emphasisChars = (seg) => seg.emphasis?.chars ?? seg.vis;
  var score = (seg) => Math.min(emphasisChars(seg), 8) * 14 + Math.min(2.5, Math.max(0, seg.emphasis?.duration ?? seg.end - seg.start)) * 18;
  var emptyShot = (plan2, si, theme, trans) => {
    const def = plan2.shots[si];
    return { def, si, kind: "editorial", boxes: [], deco: [], hero: -1, shotDur: Math.max(0.5, def.t1 - def.t0), reveal: 0, icon: null, anchor: null, mir: 1, trans, win: null, custom: false, rest: true, theme };
  };
  var buildShot = (s, plan2, si, kind, theme) => {
    const def = plan2.shots[si], shotDur = Math.max(0.5, def.t1 - def.t0), trans = shotTransition(plan2, si, s.seed);
    if (def.rest) return emptyShot(plan2, si, theme, trans);
    const K = KINDS[kind], r = rng(`shot${si}:${s.seed}`);
    const segs = [];
    def.lines.forEach((li) => plan2.lines[li].words.forEach((w) => segs.push({
      text: w.text,
      chars: w.chars,
      start: w.start,
      end: w.end,
      minor: w.minor,
      emphasis: w.emphasis,
      line: li,
      idx: segs.length,
      vis: w.chars.filter((c) => c.ch.trim()).length
    })));
    let hero = 0, best = -1;
    segs.forEach((seg, i) => {
      const sc = score(seg) + (s.seed ? r() * 10 : 0);
      if (seg.vis && sc > best) {
        best = sc;
        hero = i;
      }
    });
    const semi = /* @__PURE__ */ new Set();
    def.lines.forEach((li) => {
      const ls = segs.filter((seg) => seg.line === li);
      let lh = ls[0], b2 = -1;
      ls.forEach((seg) => {
        if (seg.vis && score(seg) > b2) {
          b2 = score(seg);
          lh = seg;
        }
      });
      if (lh.idx !== hero) semi.add(lh.idx);
      if (ls.length >= 4) {
        const c = ls.filter((seg) => seg.idx !== hero && !semi.has(seg.idx) && emphasisChars(seg) >= 2 && !seg.minor && Math.abs(seg.idx - hero) >= 2 && score(seg) >= score(segs[hero]) * 0.35);
        if (c.length) semi.add(c.sort((a, b) => score(b) - score(a))[0].idx);
      }
    });
    const mir = s.seed % 2 && ["editorial", "tableau", "poster", "stair", "stack"].includes(kind) ? -1 : 1;
    const boxes = segs.map((seg) => {
      const role = seg.idx === hero ? "hero" : semi.has(seg.idx) ? "semi" : "support";
      const baseScale = role === "hero" ? K.hs : role === "semi" ? Math.max(K.ss * 1.35, K.hs * 0.72) : K.ss;
      const scale = seg.emphasis && role !== "support" ? baseScale * Math.sqrt(Math.min(1, emphasisChars(seg) / Math.max(1, seg.vis))) : baseScale;
      const vert = kind === "editorial" || kind === "tableau" || kind === "poster" && role === "hero" || kind === "collage" && role !== "hero" && (role === "semi" || seg.idx % 4 === 0);
      return mkBox(s, seg, role, scale, vert, kind);
    });
    const hb = boxes[hero];
    layouts[kind](s, boxes, hb, hero, { mir, r });
    fitAll(s, boxes, kind);
    boxes.forEach((b) => placeGlyphs(s, b, shotDur));
    const deco = [];
    if (!["tableau", "poster", "aperture", "orbit", "swarm", "tunnel", "pendulum", "cinema", "stack"].includes(kind)) {
      const dpx = s.BASE * Math.min(5.5, Math.max(2.8, K.hs * 3.5)), dr = rng("deco" + si + s.seed);
      deco.push({ text: hb.seg.text, px: dpx, x: hb.x - s.W * 0.1, y: hb.y - s.H * 0.05, rot: -0.15, depth: (dr() > 0.5 ? 1 : -1) * (0.5 + dr() * 0.8), start: hb.glyphs[0].s });
      const o = boxes.length > 1 ? boxes[hero === boxes.length - 1 ? 0 : boxes.length - 1] : null;
      if (o) deco.push({ text: o.seg.text, px: s.BASE * Math.min(3.5, Math.max(1.8, K.hs * 2.2)), x: hb.x + s.W * 0.25, y: hb.y + s.H * 0.15, rot: 0.08, depth: (dr() > 0.5 ? 1 : -1) * (0.5 + dr() * 0.8), start: o.glyphs[0].s });
    }
    boxes.forEach((b) => {
      b.frame = kind === "tableau" ? true : hash(b.seg.text + b.seg.idx + s.seed) % 100 < 30;
      b.hx = b.x;
      b.hy = b.y;
    });
    const icon = iconForText(segs.map((seg) => seg.text).join(" "));
    const reveal = Math.max(...boxes.map((b) => b.glyphs[b.glyphs.length - 1].settle));
    let anchor = null;
    if (icon) {
      const cand = [[-0.36, -0.3], [0.36, -0.3], [-0.36, 0.3], [0.36, 0.3], [0, -0.36], [0, 0.36]], isz = s.H * 0.1;
      let bestA = 1e18;
      cand.forEach(([ax, ay]) => {
        const X = ax * s.W, Y = ay * s.H;
        let ov = 0;
        boxes.forEach((b) => {
          const ox = Math.max(0, Math.min(X + isz, b.x + b.w / 2) - Math.max(X - isz, b.x - b.w / 2)), oy = Math.max(0, Math.min(Y + isz, b.y + b.h / 2) - Math.max(Y - isz, b.y - b.h / 2));
          ov += ox * oy;
        });
        if (ov < bestA) {
          bestA = ov;
          anchor = { x: X, y: Y, s: isz };
        }
      });
    }
    const shot = {
      def,
      si,
      kind,
      boxes,
      deco,
      hero,
      shotDur,
      reveal,
      icon,
      anchor,
      mir,
      trans,
      theme,
      rest: false,
      custom: false,
      win: kind === "aperture" ? { w: hb.w * 0.3, h: hb.h * 1.5 } : null
    };
    if (kind === "shatter") shot.shatter = shatterBuild(hb, r);
    if (kind === "swarm") shot.swarm = swarmBuild(s, boxes, r);
    if (kind === "tunnel") shot.tunnel = tunnelBuild(boxes);
    if (kind === "pendulum") shot.pend = pendBuild(s, boxes, r);
    shot.custom = !!(shot.swarm || shot.tunnel || shot.pend);
    return shot;
  };
  var SLOW_PATH = /* @__PURE__ */ new Set(["ribbon", "collage", "tableau", "poster", "orbit", "aperture", "cinema", "wave", "stack"]);
  var pathProg = (kind, lin) => {
    if (SLOW_PATH.has(kind)) return lin * 0.55 + easeIO(Math.min(1, lin)) * 0.45;
    return 0.3 * (1 - Math.exp(-lin / 0.07)) + 0.7 * lin;
  };
  var camFrame = (kind, lin) => {
    const e = pathProg(kind, lin);
    switch (kind) {
      case "editorial":
        return { x: -0.055 + e * 0.095, y: 0.025 - e * 0.04, s: 0.98 + e * 0.07, r: -6e-3 + e * 0.01 };
      case "impact":
        return { x: -0.035 + e * 0.07, y: 0.018 - e * 0.028, s: 1 + (1 - expoOut(Math.min(lin / 0.18, 1))) * 0.22 + e * 0.08, r: -0.01 + e * 0.016 };
      case "collage":
        return { x: -0.045 + e * 0.085, y: 0.028 - Math.sin(e * Math.PI) * 0.055, s: 0.97 + e * 0.09, r: -0.014 + e * 0.028 };
      case "ribbon":
        return { x: 0, y: 0.05 - e * 0.085, s: 0.98 + e * 0.07, r: 8e-3 - e * 0.014 };
      case "tableau":
        return { x: -0.022 + e * 0.04, y: 0.014 - e * 0.025, s: 1 + e * 0.028, r: -2e-3 + e * 3e-3 };
      case "orbit":
        return { x: 0, y: 0, s: 0.96 + e * 0.1, r: -0.02 + e * 0.04 };
      case "slice":
        return { x: 0.03 - e * 0.05, y: 0.012 - e * 0.02, s: 1.02 + e * 0.05, r: 0.01 - e * 0.016 };
      case "aperture":
        return { x: -0.02 + e * 0.03, y: 0.02 - e * 0.03, s: 0.98 + e * 0.06, r: 0 };
      case "stair":
        return { x: 0.05 - e * 0.09, y: -0.04 + e * 0.06, s: 0.97 + e * 0.08, r: 0.012 - e * 0.02 };
      // A slow, level push in, the way a film title holds the frame.
      case "cinema":
        return { x: 0, y: 6e-3 - e * 0.012, s: 0.97 + e * 0.07, r: 0 };
      // A gentle sideways glide that rides along with the swell.
      case "wave":
        return { x: 0.04 - e * 0.08, y: 0.01 - e * 0.02, s: 1 + e * 0.04, r: -6e-3 + e * 0.012 };
      // A slow crane down the list as it is sung.
      case "stack":
        return { x: 0.01 - e * 0.02, y: 0.04 - e * 0.08, s: 0.99 + e * 0.05, r: 4e-3 - e * 8e-3 };
      default:
        return { x: -0.012 + e * 0.024, y: 8e-3 - e * 0.016, s: 0.99 + e * 0.025, r: -15e-4 + e * 3e-3 };
    }
  };
  var breath = (t, ph) => {
    const tau = t * 6.2832;
    return {
      x: (Math.sin(tau * 0.13 + ph) * 0.65 + Math.sin(tau * 0.31 + ph * 1.7) * 0.35) * 6e-3,
      y: (Math.cos(tau * 0.11 + ph * 2.3) * 0.65 + Math.sin(tau * 0.29 + ph * 0.9) * 0.35) * 6e-3,
      s: Math.sin(tau * 0.09 + ph * 1.3) * 2e-3,
      r: Math.sin(tau * 0.07 + ph * 2.9) * 15e-4
    };
  };
  var focusX = (sh, t) => {
    let sw = 0, sx = 0;
    sh.boxes.forEach((b) => b.glyphs.forEach((g) => {
      const d = t < g.s ? g.s - t : t > g.settle ? t - g.settle : 0, w = Math.exp(-(d * d) / (2 * 0.35 * 0.35));
      sw += w;
      sx += w * (b.x + g.c);
    }));
    return sw ? sx / sw : 0;
  };
  var shotProgress = (sh, t) => clamp01((t - sh.def.t0) / Math.max(1e-3, sh.def.t1 - sh.def.t0));
  var cameraProgress = (sh, t, lead) => Math.max(0, (t - (sh.def.t0 - lead)) / Math.max(1e-3, sh.def.t1 - sh.def.t0 + lead));
  var trackingSamples = /* @__PURE__ */ new WeakMap();
  var trackingSample = (sh, t) => {
    const cached = trackingSamples.get(sh);
    if (cached && cached.t === t) return cached;
    let sw = 0, sx = 0, sy = 0, mx = 0, my = 0, n = 0;
    for (const b of sh.boxes) {
      if (b.skip || b.arc) continue;
      const dx = b.vert ? 0 : Math.cos(b.rot), dy = b.vert ? 1 : Math.sin(b.rot);
      for (const g of b.glyphs) {
        const d = t < g.s ? g.s - t : t > g.e ? t - g.e : 0, w = Math.exp(-(d * d) / (2 * 0.45 * 0.45)), px = b.x + dx * g.c, py = b.y + dy * g.c;
        sw += w;
        sx += w * px;
        sy += w * py;
        mx += px;
        my += py;
        n++;
      }
    }
    const sample = cached ?? { t, sw, sx, sy, mx, my, n };
    sample.t = t;
    sample.sw = sw;
    sample.sx = sx;
    sample.sy = sy;
    sample.mx = mx;
    sample.my = my;
    sample.n = n;
    if (!cached) trackingSamples.set(sh, sample);
    return sample;
  };
  var trackPoint = (sh, t) => {
    if (sh.custom || sh.rest) return { x: 0, y: 0 };
    const { sw, sx, sy, mx, my, n } = trackingSample(sh, t);
    return sw > 1e-9 && n ? { x: sx / sw - mx / n, y: sy / sw - my / n } : { x: 0, y: 0 };
  };
  var singPoint = (sh, t) => {
    if (sh.custom || sh.rest) return null;
    const { sw, sx, sy } = trackingSample(sh, t);
    return sw > 0.05 ? { x: sx / sw, y: sy / sw, k: Math.min(1, sw * 0.8) } : null;
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/blossom/blossomClassic.ts
  var NEON_SIGNS = [[-0.4, -0.33, 0.07, "\u591C\u5149", 0], [0.41, -0.27, 0.054, "\u55AB\u8336", 1]];
  var CROSS_LEVELS = [0.05, 0.08, 0.12, 0.17, 0.23, 0.3, 0.37, 0.44];
  var CLASSIC_THEMES = /* @__PURE__ */ new Set([0, 1, 2, 3, 4, 5, 7]);
  var SILK_ANG = [-0.2, 0.1, -0.07, 0.24, -0.28, 0.06, 0.18, -0.13, 0.09, -0.22, 0.13, -0.04, 0.2, -0.1, 0.15];
  var LPJ = Array.from({ length: 97 }, (_, k) => [unit("lpa" + k), unit("lpb" + k), unit("lpc" + k)]);
  var CODE = "\u591C\u660E\u6B4C\u661F\u5149\u98A8\u58F0\u6708\u541B\u8857\u96E8\u5F71\u5922\u706F\u8DEF\u99C50123456789ABCDEF<>/+*=";
  var silkGradients = /* @__PURE__ */ new Map();
  var CODE_COL = /* @__PURE__ */ new Map();
  var CODE_CHR = /* @__PURE__ */ new Map();
  var clearClassicCaches = () => {
    silkGradients.clear();
    CODE_COL.clear();
    CODE_CHR.clear();
    threadGrads.clear();
    lpPts = new Float32Array(0);
  };
  var silkGrad = (g, c, L) => {
    const key = `${c}|${Math.round(L)}`;
    let grad = silkGradients.get(key);
    if (!grad) {
      if (silkGradients.size > 24) silkGradients.clear();
      grad = g.createLinearGradient(-L, 0, L, 0);
      grad.addColorStop(0, c + "00");
      grad.addColorStop(0.28, c + "ff");
      grad.addColorStop(0.72, c + "ff");
      grad.addColorStop(1, c + "00");
      silkGradients.set(key, grad);
    }
    return grad;
  };
  var drawSilk = (s, theme, t, c, bt, px, py, n) => {
    const g = s.g, { W, H } = s, ang = SILK_ANG[theme % SILK_ANG.length] + px * 0.05, Lh = Math.hypot(W, H) * 0.62, seg = 40, cols = [c.acc, c.a2, c.a3];
    g.save();
    g.globalCompositeOperation = "lighter";
    g.translate(W / 2, H / 2);
    g.rotate(ang);
    g.lineJoin = "round";
    for (let k = 0; k < n; k++) {
      const u = k / (n - 1), col = cols[k % 3], y0 = (u - 0.5) * H * 0.92 - py * H * 0.03 * (0.5 + u) + Math.sin(t * 0.06 + k * 0.7) * 6, env = Math.pow(Math.sin(u * Math.PI), 1.3);
      const amp = H * (0.035 + 0.07 * Math.pow(Math.sin(u * 2.6 + theme * 0.9), 2)) * (1 + 0.05 * bt), f1 = 1.3 + 0.45 * Math.sin(theme * 0.7 + k * 0.23), f2 = 2.6 + 0.8 * Math.cos(theme + k * 0.31);
      const ph = k * 0.41 + theme * 1.3, sp = 0.1 + 0.05 * u;
      g.strokeStyle = silkGrad(g, col, Lh);
      g.globalAlpha = 0.05 + 0.2 * env;
      g.lineWidth = k % 5 === 2 ? 1.8 : 0.9;
      g.beginPath();
      for (let i = 0; i <= seg; i++) {
        const x = -Lh + 2 * Lh * i / seg, q = x / Lh, y = y0 + amp * Math.sin(q * f1 * 3.1416 + ph + t * sp * 2) + amp * 0.38 * Math.sin(q * f2 * 3.1416 - t * 0.09 * 2 + ph * 1.3);
        i ? g.lineTo(x, y) : g.moveTo(x, y);
      }
      g.stroke();
    }
    for (let m = 0; m < 5; m++) {
      const per = 5 + m * 1.7, ph = (t / per + m * 0.37) % 1, x = -Lh + ph * 2 * Lh, y = (unit("sl" + m + theme) - 0.5) * H * 0.8, len = Lh * (0.16 + 0.1 * unit("sn" + m));
      const grad = g.createLinearGradient(x - len, 0, x, 0), col = cols[m % 3];
      grad.addColorStop(0, col + "00");
      grad.addColorStop(1, col + "ff");
      g.strokeStyle = grad;
      g.globalAlpha = 0.5 * Math.sin(ph * Math.PI);
      g.lineWidth = 1.4;
      g.beginPath();
      g.moveTo(x - len, y);
      g.lineTo(x, y);
      g.stroke();
    }
    g.restore();
  };
  var layerGrid = (s, t, c, px) => {
    const g = s.g, { W, H } = s, hy = H * 0.08;
    g.save();
    g.translate(W / 2, H / 2);
    g.beginPath();
    g.rect(-W / 2, hy, W, H / 2 - hy);
    g.clip();
    g.strokeStyle = c.acc;
    g.lineWidth = 1;
    const grad = g.createLinearGradient(0, hy, 0, H / 2);
    grad.addColorStop(0, c.a2 + "30");
    grad.addColorStop(0.35, c.a2 + "0c");
    grad.addColorStop(1, c.a2 + "00");
    g.fillStyle = grad;
    g.fillRect(-W / 2, hy, W, H / 2 - hy);
    for (let i = -18; i <= 18; i++) {
      g.globalAlpha = 0.3 * (1 - Math.abs(i) / 20);
      g.beginPath();
      g.moveTo(i * W * 0.012 - px * 30, hy);
      g.lineTo(i * W * 0.15 - px * 90, H / 2);
      g.stroke();
    }
    const sp = t * 0.3 % 1;
    for (let j = 0; j < 15; j++) {
      const u = (j + sp) / 15, y = hy + Math.pow(u, 2.1) * (H / 2 - hy);
      g.globalAlpha = 0.06 + 0.3 * u;
      g.beginPath();
      g.moveTo(-W / 2, y);
      g.lineTo(W / 2, y);
      g.stroke();
    }
    g.restore();
  };
  var lpPts = new Float32Array(0);
  var LP_LEVELS = 10;
  var layerLowpoly = (s, t, c, theme, px, py) => {
    const g = s.g, { W, H } = s, cs = H * 0.18, cols = Math.ceil(W / cs) + 2, rows = Math.ceil(H / cs / 0.87) + 2, ox = -W / 2 - cs, oy = -H / 2 - cs;
    const stride = cols + 1, need = (rows + 1) * stride * 2;
    if (lpPts.length < need) lpPts = new Float32Array(need);
    const pts = lpPts;
    for (let j = 0; j <= rows; j++) for (let i = 0; i <= cols; i++) {
      const q = LPJ[(i * 7 + j * 13) % 97], o = (j * stride + i) * 2;
      pts[o] = ox + i * cs + j % 2 * cs * 0.5 + (q[0] - 0.5) * cs * 0.55 + Math.sin(t * 0.25 + i * 1.7 + j * 2.3 + theme) * cs * 0.12 - px * W * 0.02;
      pts[o + 1] = oy + j * cs * 0.87 + (q[1] - 0.5) * cs * 0.55 + Math.cos(t * 0.21 + i * 2.1 + j * 1.3 + theme) * cs * 0.12 - py * H * 0.02;
    }
    const tints = [c.acc, c.a2, c.a3];
    const paths = Array.from({ length: LP_LEVELS * 3 }, () => new Path2D()), tri = (p, x0, y0, x1, y1, x2, y2) => {
      p.moveTo(x0, y0);
      p.lineTo(x1, y1);
      p.lineTo(x2, y2);
      p.closePath();
    };
    const level = (a, tint) => paths[tint * LP_LEVELS + Math.min(LP_LEVELS - 1, Math.round((a - 6e-3) / 0.04 * (LP_LEVELS - 1)))];
    g.save();
    g.translate(W / 2, H / 2);
    g.lineWidth = 0.8;
    for (let j = 0; j < rows; j++) for (let i = 0; i < cols; i++) {
      const ia = (j * stride + i) * 2, ib = ia + 2, ic = ia + stride * 2, id = ic + 2, q = LPJ[(i * 5 + j * 11) % 97];
      const ax = pts[ia], ay = pts[ia + 1], bx = pts[ib], by = pts[ib + 1], cx = pts[ic], cy = pts[ic + 1], dx = pts[id], dy = pts[id + 1];
      const k = Math.max(0, Math.sin(t * 0.45 + i * 0.6 + j * 0.9 + theme * 1.7 + q[2] * 6)), tint = (i + j + theme) % 3;
      tri(level(6e-3 + 0.04 * k * k, tint), ax, ay, bx, by, cx, cy);
      tri(level(6e-3 + 0.028 * Math.max(0, Math.sin(t * 0.37 + i * 0.8 - j * 0.5 + theme + q[0] * 6)) ** 2, tint), bx, by, dx, dy, cx, cy);
    }
    paths.forEach((p, n) => {
      const tint = Math.floor(n / LP_LEVELS), a = 6e-3 + 0.04 * (n % LP_LEVELS) / (LP_LEVELS - 1);
      g.fillStyle = tints[tint];
      g.globalAlpha = a;
      g.fill(p);
    });
    g.beginPath();
    for (let j = 0; j < rows; j++) for (let i = 0; i < cols; i++) {
      const ia = (j * stride + i) * 2, ic = ia + stride * 2;
      g.moveTo(pts[ia + 2], pts[ia + 3]);
      g.lineTo(pts[ia], pts[ia + 1]);
      g.lineTo(pts[ic], pts[ic + 1]);
    }
    g.globalAlpha = 0.022;
    g.strokeStyle = c.acc;
    g.stroke();
    g.restore();
  };
  var layerSlabs = (s, t, c, theme, px) => {
    const g = s.g, { W, H } = s;
    g.save();
    g.translate(W / 2, H / 2);
    const h = H * 1.4, sk = 0.32;
    for (let k = 0; k < 5; k++) {
      const w = W * (0.1 + 0.1 * unit("sbw" + k + theme)), x = ((unit("sbx" + k + theme) + t * 5e-3 * (k % 2 ? 1 : -1)) % 1 + 1) % 1 * W * 1.3 - W * 0.65 - px * W * 0.05 * (k + 1) * 0.5;
      const col = k % 2 ? c.a2 : c.acc, grad = g.createLinearGradient(x - w / 2, 0, x + w / 2, 0);
      grad.addColorStop(0, col + "00");
      grad.addColorStop(0.5, col + "1c");
      grad.addColorStop(1, col + "06");
      g.fillStyle = grad;
      g.globalAlpha = 1;
      g.beginPath();
      g.moveTo(x - w / 2 + sk * h / 2, -h / 2);
      g.lineTo(x + w / 2 + sk * h / 2, -h / 2);
      g.lineTo(x + w / 2 - sk * h / 2, h / 2);
      g.lineTo(x - w / 2 - sk * h / 2, h / 2);
      g.closePath();
      g.fill();
      g.strokeStyle = col;
      g.globalAlpha = 0.22;
      g.lineWidth = 1;
      g.beginPath();
      g.moveTo(x - w / 2 + sk * h / 2, -h / 2);
      g.lineTo(x - w / 2 - sk * h / 2, h / 2);
      g.stroke();
    }
    g.restore();
  };
  var layerCrosses = (s, t, c, px, py) => {
    const g = s.g, { W, H } = s, sp = H * 0.085, cols = Math.ceil(W / sp) + 1, rows = Math.ceil(H / sp) + 1;
    const x0 = -W / 2 + (W - (cols - 1) * sp) / 2, y0 = -H / 2 + (H - (rows - 1) * sp) / 2;
    const paths = CROSS_LEVELS.map(() => new Path2D()), pxw = (px + 0.5) * W - W / 2, pyw = (py + 0.5) * H - H / 2;
    for (let j = 0; j < rows; j++) for (let i = 0; i < cols; i++) {
      const x = x0 + i * sp, y = y0 + j * sp, d = Math.hypot(x, y), dp = Math.hypot(x - pxw, y - pyw);
      const v = Math.max(0, Math.sin(d * 0.011 - t * 1.1)) ** 3 + Math.max(0, 1 - dp / (H * 0.2)) * 0.9, k = Math.min(CROSS_LEVELS.length - 1, Math.floor(v * 6.4)), p = paths[k], r = 3 + k * 0.8;
      p.moveTo(x - r, y);
      p.lineTo(x + r, y);
      p.moveTo(x, y - r);
      p.lineTo(x, y + r);
    }
    g.save();
    g.translate(W / 2, H / 2);
    g.lineWidth = 1;
    CROSS_LEVELS.forEach((a, k) => {
      g.strokeStyle = k > 3 ? c.acc : "#9fb0d8";
      g.globalAlpha = a;
      g.stroke(paths[k]);
    });
    g.restore();
  };
  var layerCode = (s, t, c, theme) => {
    const g = s.g, { W, H } = s, sp = H * 0.1, n = Math.ceil(W / sp), lh = H * 0.032, rows = 15;
    g.save();
    g.translate(W / 2, H / 2);
    g.font = `500 ${Math.round(H * 0.02)}px ${s.fonts.mono}`;
    g.textAlign = "center";
    g.textBaseline = "middle";
    for (let col = 0; col < n; col++) {
      const ck = col * 64 + theme % 64;
      let cc = CODE_COL.get(ck);
      if (!cc) {
        cc = { on: unit("cc" + col + theme) >= 0.2, speed: 0.4 + unit("cs" + col) * 0.9, acc: unit("cq" + col) > 0.5 };
        CODE_COL.set(ck, cc);
      }
      if (!cc.on) continue;
      const x = -W / 2 + (col + 0.5) * sp, speed = cc.speed, off = t * speed * lh * 1.6 % (rows * lh), tint = cc.acc ? c.acc : c.a2;
      for (let r = 0; r < rows; r++) {
        const y = -H / 2 + (r * lh + off) % (rows * lh) * (H / (rows * lh)), head = r === Math.floor(off / lh) % rows ? 1 : 0;
        const zi = (r + Math.floor(t * speed * 1.6)) % 23, zk = col * 32 + zi;
        let chr = CODE_CHR.get(zk);
        if (chr === void 0) {
          chr = CODE[Math.floor(unit("cz" + col + "_" + zi) * CODE.length)];
          CODE_CHR.set(zk, chr);
        }
        const f = y / H + 0.5;
        g.fillStyle = head ? "#ffffff" : tint;
        g.globalAlpha = head ? 0.7 : 0.2 * (0.35 + f * 0.9);
        g.fillText(chr, x, y);
      }
    }
    g.restore();
  };
  var layerFlare = (s, t, c, theme, px) => {
    const g = s.g, { W, H } = s;
    g.save();
    g.translate(W / 2, H / 2);
    const y = H * (-0.16 + 0.12 * Math.sin(t * 0.17 + theme)), x0 = -W * 0.58, w = W * 1.16;
    let grad = g.createLinearGradient(x0, 0, x0 + w, 0);
    grad.addColorStop(0, c.acc + "00");
    grad.addColorStop(0.5, "#ffffff70");
    grad.addColorStop(1, c.acc + "00");
    g.fillStyle = grad;
    g.globalAlpha = 0.5;
    g.fillRect(x0, y - 0.8, w, 1.6);
    grad = g.createLinearGradient(x0, 0, x0 + w, 0);
    grad.addColorStop(0, c.a2 + "00");
    grad.addColorStop(0.5, c.acc + "70");
    grad.addColorStop(1, c.a2 + "00");
    g.fillStyle = grad;
    g.globalAlpha = 0.5;
    g.fillRect(x0, y - 4, w, 8);
    const v = g.createLinearGradient(0, y - H * 0.1, 0, y + H * 0.1);
    v.addColorStop(0, c.acc + "00");
    v.addColorStop(0.5, c.acc + "20");
    v.addColorStop(1, c.acc + "00");
    g.fillStyle = v;
    g.globalAlpha = 1;
    g.fillRect(x0, y - H * 0.1, w, H * 0.2);
    g.strokeStyle = "#ffffff";
    g.lineWidth = 1;
    for (let k = 0; k < 4; k++) {
      const sx = (unit("fx" + k + theme) - 0.5) * W * 0.8 - px * 40 * (k + 1) * 0.3, r = H * (0.012 + 0.012 * unit("fr" + k)) * (0.7 + 0.3 * Math.sin(t * 2 + k * 2));
      g.globalAlpha = 0.5;
      g.beginPath();
      g.moveTo(sx - r * 3, y);
      g.lineTo(sx + r * 3, y);
      g.moveTo(sx, y - r);
      g.lineTo(sx, y + r);
      g.stroke();
    }
    g.restore();
  };
  var layerChevrons = (s, t, c) => {
    const g = s.g, { W, H } = s;
    g.save();
    g.translate(W / 2, H / 2);
    g.lineWidth = 1.2;
    g.lineJoin = "miter";
    const sz = H * 0.07, gap = H * 0.2;
    [-0.3, 0.02, 0.34].forEach((yy, r) => {
      const dir = r % 2 ? -1 : 1, off = t * 26 * dir % gap;
      g.strokeStyle = r % 2 ? c.a2 : c.acc;
      for (let i = -1; i * gap < W + gap; i++) {
        const x = -W / 2 + i * gap + off, y = yy * H;
        g.globalAlpha = 0.03 + 0.05 * Math.sin(Math.PI * clamp01((x + W / 2) / W));
        g.beginPath();
        g.moveTo(x - dir * sz * 0.5, y - sz);
        g.lineTo(x + dir * sz * 0.5, y);
        g.lineTo(x - dir * sz * 0.5, y + sz);
        g.stroke();
      }
    });
    g.restore();
  };
  var RECIPE = { 0: ["lowpoly", "flare"], 1: ["crosses", "flare"], 2: ["crosses", "slabs"], 3: ["slabs", "code"], 4: ["lowpoly", "crosses"], 5: ["grid", "flare"], 7: ["lowpoly", "chevrons"] };
  var drawClassicLayers = (s, theme, t, c, bt, px, py) => {
    const g = s.g;
    g.save();
    g.globalCompositeOperation = "lighter";
    (RECIPE[theme] ?? []).forEach((name) => {
      g.save();
      if (name === "grid") layerGrid(s, t, c, px);
      else if (name === "lowpoly") layerLowpoly(s, t, c, theme, px, py);
      else if (name === "slabs") layerSlabs(s, t, c, theme, px);
      else if (name === "crosses") layerCrosses(s, t, c, px, py);
      else if (name === "code") layerCode(s, t, c, theme);
      else if (name === "flare") layerFlare(s, t, c, theme, px);
      else if (name === "chevrons") layerChevrons(s, t, c);
      g.restore();
    });
    g.restore();
    if (theme % 3 === 0) drawSilk(s, theme, t, c, bt, px, py, 6);
  };
  var glowAt = (g, x, y, R, col, alpha) => {
    const grad = g.createRadialGradient(x, y, 0, x, y, R);
    grad.addColorStop(0, col + alpha);
    grad.addColorStop(1, col + "00");
    g.fillStyle = grad;
    g.fillRect(x - R, y - R, R * 2, R * 2);
  };
  var ridgeFlat = (s, seed, base3, amp, col, a, sx) => {
    const g = s.g, { W, H } = s;
    g.beginPath();
    g.moveTo(-W * 0.7, H);
    for (let x = -W * 0.7; x <= W * 0.7; x += 22) {
      const u = x + sx;
      g.lineTo(x, base3 + amp * (0.5 * Math.sin(u * 42e-4 + seed) + 0.3 * Math.sin(u * 0.011 + seed * 2.1) + 0.2 * Math.sin(u * 0.027 + seed * 3.7)));
    }
    g.lineTo(W * 0.7, H);
    g.closePath();
    g.globalAlpha = a;
    g.fillStyle = col;
    g.fill();
    g.globalAlpha = 1;
  };
  var softBand = (g, x, y, rw, rh, col, alpha) => {
    g.save();
    g.translate(x, y);
    g.scale(1, rh / rw);
    const grad = g.createRadialGradient(0, 0, 0, 0, 0, rw);
    grad.addColorStop(0, col + alpha);
    grad.addColorStop(1, col + "00");
    g.fillStyle = grad;
    g.fillRect(-rw, -rw, rw * 2, rw * 2);
    g.restore();
  };
  var threadGrads = /* @__PURE__ */ new Map();
  var threadGrad = (g, col, q, H) => {
    const key = `${col}|${q}|${Math.round(H)}`;
    let grad = threadGrads.get(key);
    if (!grad) {
      if (threadGrads.size > 24) threadGrads.clear();
      grad = g.createLinearGradient(0, -H / 2, 0, -H / 2 + H * (0.25 + 0.15 * q));
      grad.addColorStop(0, col + "00");
      grad.addColorStop(0.25, col + "ff");
      grad.addColorStop(1, col + "00");
      threadGrads.set(key, grad);
    }
    return grad;
  };
  var SCENES = {
    0(s, _sh, t, c, _P, _bt, lin, lt) {
      const g = s.g, { W, H } = s, e = expoOut(clamp01(lt / 6)), sy = H * 0.33 - e * H * 0.22 - lin * H * 0.04, sx = W * 0.2;
      g.globalCompositeOperation = "lighter";
      glowAt(g, sx, sy, H * 0.75, c.acc, "22");
      glowAt(g, sx, sy, H * 0.28, c.a2, "2a");
      for (let i = 0; i < 7; i++) beam(g, sx, sy, -0.5 + i * 0.3 + Math.sin(t * 0.12 + i * 1.3) * 0.03, W * 1.3, W * (0.16 + 0.05 * (i % 3)), i % 2 ? c.a2 : c.acc, 0.08);
      g.globalCompositeOperation = "source-over";
      g.globalAlpha = 1;
      for (let i = 0; i < 4; i++) {
        const y = H * (-0.2 + i * 0.06) + Math.sin(t * 0.1 + i) * 6;
        g.fillStyle = c.acc;
        g.globalAlpha = 0.05;
        g.fillRect(-W * 0.7 + t * (8 + i * 4) % 200, y, W * 0.7, 3 + i);
      }
      g.globalAlpha = 1;
      ridgeFlat(s, 1.3, H * 0.3, H * 0.1, hexMix("#0b0f1c", c.acc, 0.22), 0.95, -lin * 30);
      ridgeFlat(s, 4.1, H * 0.36, H * 0.09, hexMix("#080b16", c.acc, 0.14), 0.97, -lin * 60);
      ridgeFlat(s, 2.2, H * 0.43, H * 0.07, hexMix("#05070f", c.a2, 0.08), 1, -lin * 100);
    },
    1(s, _sh, t, c, _P, bt) {
      const g = s.g, { W, H } = s, cols = [c.acc, c.a2, c.a3];
      g.globalCompositeOperation = "lighter";
      for (let k = 0; k < 5; k++) {
        const x = (t * (6 + k * 3.5) + k * 410) % (W * 1.9) - W * 0.95, y2 = H * (0.1 + 0.1 * k) + Math.sin(t * 0.2 + k) * H * 0.012;
        softBand(g, x, y2, W * (0.5 + 0.08 * k), H * (0.05 + 0.012 * k), cols[k % 3], k % 2 ? "1c" : "26");
      }
      const y = H * 0.04, line = g.createLinearGradient(-W * 0.5, 0, W * 0.5, 0);
      line.addColorStop(0, c.acc + "00");
      line.addColorStop(0.5, c.acc + "ff");
      line.addColorStop(1, c.acc + "00");
      g.fillStyle = line;
      g.globalAlpha = 0.4 * (1 + 0.3 * bt);
      g.fillRect(-W * 0.5, y - 0.75, W, 1.5);
      glowAt(g, 0, y, H * 0.32, c.acc, "1a");
      g.globalAlpha = 1;
      g.globalCompositeOperation = "source-over";
    },
    2(s, _sh, t, c, _P, _bt, _lin, _lt) {
      const g = s.g, { W, H } = s, hy = H * 0.12;
      g.globalCompositeOperation = "lighter";
      glowAt(g, 0, hy - H * 0.1, H * 0.5, c.acc, "40");
      const grad = g.createLinearGradient(0, hy - 2, 0, hy + 2);
      grad.addColorStop(0.5, c.acc + "70");
      grad.addColorStop(0, c.acc + "00");
      grad.addColorStop(1, c.acc + "00");
      g.fillStyle = grad;
      g.fillRect(-W, hy - 14, W * 2, 28);
      g.fillStyle = c.acc;
      for (let i = 0; i < 22; i++) {
        const w = H * 0.12 * (1 - i / 26) * (0.7 + 0.3 * Math.sin(t * 1.4 + i)), y = hy + 6 + i * 9;
        g.globalAlpha = 0.26 * (1 - i / 24);
        g.fillRect(-w / 2 + Math.sin(t * 0.8 + i * 0.6) * 5, y, w, 2);
      }
      g.globalCompositeOperation = "source-over";
      g.globalAlpha = 1;
    },
    3(s, _sh, t, c, P, _bt, lin, lt) {
      const g = s.g, { W, H } = s, gy = H * 0.3;
      g.strokeStyle = P.sub;
      g.globalAlpha = 0.4;
      g.lineWidth = 2;
      g.beginPath();
      g.moveTo(-W, gy);
      g.lineTo(W, gy);
      g.stroke();
      g.lineWidth = 1;
      for (let i = -3; i <= 3; i++) {
        const x = i * W * 0.2 - lin * 30;
        g.beginPath();
        g.moveTo(x, gy);
        g.lineTo(x, gy - H * 0.2);
        g.stroke();
        g.globalCompositeOperation = "lighter";
        glowAt(g, x, gy - H * 0.2, H * 0.14, c.a2, "55");
        g.globalCompositeOperation = "source-over";
      }
      const tt = (lt - 1) / 1.15;
      if (tt > 0 && tt < 1) {
        const x0 = W * 0.9 - tt * W * 2.9, y = gy + 14, h = H * 0.07;
        g.globalAlpha = 0.9;
        g.fillStyle = hexMix("#06080f", c.acc, 0.2);
        g.fillRect(x0, y, W * 1.9, h);
        for (let i = 0; i < 24; i++) {
          g.fillStyle = i % 5 === 3 ? c.a3 : c.a2;
          g.globalAlpha = 0.85;
          g.fillRect(x0 + 14 + i * 76, y + 8, 46, h * 0.4);
        }
        const gg = g.createLinearGradient(x0, 0, x0 + W * 0.6, 0);
        gg.addColorStop(0, c.acc + "00");
        gg.addColorStop(1, c.acc + "30");
        g.globalAlpha = 1;
        g.fillStyle = gg;
        g.fillRect(x0, y - 3, W * 0.6, h + 6);
      }
      g.strokeStyle = c.acc;
      g.lineWidth = 1;
      for (let i = 0; i < 12; i++) {
        const y = (unit("wy" + i) - 0.62) * H * 0.9, sp = 60 + unit("ws" + i) * 120, x = (t * sp + unit("wx" + i) * W * 1.6) % (W * 1.6) - W * 0.8, L = 80 + unit("wl" + i) * 160;
        g.globalAlpha = 0.12;
        g.beginPath();
        g.moveTo(x, y);
        g.bezierCurveTo(x + L * 0.4, y - 10, x + L * 0.7, y + 10, x + L, y);
        g.stroke();
      }
      g.globalAlpha = 1;
    },
    4(s, _sh, t, c) {
      const g = s.g, { W } = s;
      g.globalCompositeOperation = "lighter";
      g.lineCap = "round";
      g.save();
      g.rotate(-0.12);
      [[c.acc, 0.9, 0.44, 0], [c.a2, -1.2, 0.34, 2.2]].forEach(([col, sp, sz, off]) => {
        const A = W * sz, pt = (th) => {
          const sn = Math.sin(th), cs = Math.cos(th), d = 1 + sn * sn;
          return [A * cs / d, A * 0.95 * sn * cs / d];
        };
        g.strokeStyle = col;
        g.globalAlpha = 0.1;
        g.lineWidth = 1;
        g.beginPath();
        for (let i = 0; i <= 120; i++) {
          const p = pt(i / 120 * 6.2832 + off);
          i ? g.lineTo(p[0], p[1]) : g.moveTo(p[0], p[1]);
        }
        g.closePath();
        g.stroke();
        const a = t * sp + off, N = 44, tail2 = 2.6;
        for (let i = 0; i < N; i++) {
          const u = i / N, p0 = pt(a - Math.sign(sp) * u * tail2), p1 = pt(a - Math.sign(sp) * (u + 1.2 / N) * tail2);
          g.globalAlpha = (1 - u) * 0.7;
          g.lineWidth = 3.2 * (1 - u) + 0.5;
          g.beginPath();
          g.moveTo(p0[0], p0[1]);
          g.lineTo(p1[0], p1[1]);
          g.stroke();
        }
        const hp = pt(a);
        glowAt(g, hp[0], hp[1], 46, col, "70");
      });
      g.restore();
      g.globalAlpha = 1;
      g.globalCompositeOperation = "source-over";
    },
    5(s, _sh, _t, c, P, _bt, _lin, lt) {
      const g = s.g, { W, H } = s, N = 16;
      const pt = (u) => [-W * 0.48 + u * W * 0.96, H * 0.3 - u * H * 0.12 + Math.sin(u * 5.5) * H * 0.035];
      g.strokeStyle = P.sub;
      g.globalAlpha = 0.18;
      g.setLineDash([3, 9]);
      g.beginPath();
      for (let i = 0; i <= 60; i++) {
        const p = pt(i / 60);
        i ? g.lineTo(p[0], p[1]) : g.moveTo(p[0], p[1]);
      }
      g.stroke();
      g.setLineDash([]);
      for (let i = 0; i < N; i++) {
        const ts = 0.3 + i * 0.26, age = lt - ts;
        if (age < 0) continue;
        const u = i / (N - 1) * 0.94 + 0.03, p = pt(u), fade = Math.max(0.15, 1 - age * 0.16), sz = 6 + (i % 4 === 0 ? 3 : 0);
        g.save();
        g.translate(p[0], p[1]);
        g.rotate(0.7854);
        g.fillStyle = i % 4 === 0 ? c.acc : P.ink;
        g.globalAlpha = 0.5 * fade;
        g.fillRect(-sz / 2, -sz / 2, sz, sz);
        if (age < 0.7) {
          const k = age / 0.7;
          g.strokeStyle = c.acc;
          g.globalAlpha = (1 - k) * 0.6;
          g.lineWidth = 1.2;
          const r = sz + k * 34;
          g.strokeRect(-r / 2, -r / 2, r, r);
        }
        g.restore();
        g.globalCompositeOperation = "lighter";
        glowAt(g, p[0], p[1], 34, c.acc, age < 0.7 ? "40" : "14");
        g.globalCompositeOperation = "source-over";
      }
      g.globalAlpha = 1;
    },
    7(s, sh, t, c, _P, _bt, lin) {
      const g = s.g, { W, H } = s, gy = H * 0.4;
      const icon = sh.icon && sh.anchor && sh.anchor.y < 0 ? Math.sign(sh.anchor.x) : 0;
      if (sh.kind !== "pendulum") NEON_SIGNS.forEach(([x, y, sz, text, ci], i) => {
        if (icon && Math.sign(x) === icon) return;
        const col = ci ? c.a2 : c.acc, flick = lin < 0.1 ? 1 : 1 - 0.4 * smoothstep((Math.sin(t * 1.7 + i * 2.3) - 0.9) / 0.1) * (0.5 + 0.5 * Math.sin(t * 31 + i)), w = H * sz, h = w * (text.length + 0.7);
        const X = x * W, Y = y * H;
        g.save();
        g.globalCompositeOperation = "lighter";
        const board = g.createLinearGradient(X, Y, X, Y + h);
        board.addColorStop(0, col + "14");
        board.addColorStop(1, col + "06");
        g.fillStyle = board;
        g.beginPath();
        g.roundRect(X - w / 2, Y, w, h, w * 0.12);
        g.fill();
        g.shadowColor = col;
        g.shadowBlur = 18;
        g.strokeStyle = col;
        g.globalAlpha = 0.5 * flick;
        g.lineWidth = 1.6;
        g.stroke();
        g.font = `500 ${Math.round(w * 0.62)}px ${s.fonts.sans}`;
        g.textAlign = "center";
        g.textBaseline = "middle";
        g.lineWidth = Math.max(1.2, w * 0.03);
        g.lineJoin = "round";
        g.globalAlpha = 0.8 * flick;
        Array.from(text).forEach((ch, k) => g.strokeText(ch, X, Y + w * (0.85 + k)));
        g.shadowBlur = 0;
        g.globalAlpha = 0.5 * flick;
        g.strokeStyle = "#ffffff";
        g.lineWidth = Math.max(0.6, w * 8e-3);
        Array.from(text).forEach((ch, k) => g.strokeText(ch, X, Y + w * (0.85 + k)));
        g.restore();
      });
      const grad = g.createLinearGradient(0, gy - H * 0.05, 0, H * 0.5);
      grad.addColorStop(0, c.acc + "00");
      grad.addColorStop(0.18, c.acc + "1a");
      grad.addColorStop(0.45, c.acc + "0a");
      grad.addColorStop(1, c.acc + "00");
      g.fillStyle = grad;
      g.fillRect(-W, gy - H * 0.05, W * 2, H);
      g.strokeStyle = c.acc;
      g.lineWidth = 1.2;
      g.lineCap = "round";
      const drops = [new Path2D(), new Path2D(), new Path2D()];
      for (let i = 0; i < 130; i++) {
        const x0 = (unit("rx" + i) - 0.5) * W * 1.4, sp = 500 + unit("rs" + i) * 500, y = (t * sp + unit("ry" + i) * H * 1.6) % (H * 1.6) - H * 0.8, L = 18 + unit("rl" + i) * 26;
        const p = drops[Math.min(2, Math.floor(unit("ra" + i) * 3))];
        p.moveTo(x0 + y * 0.18, y);
        p.lineTo(x0 + (y + L) * 0.18, y + L);
      }
      drops.forEach((p, k) => {
        g.globalAlpha = 0.25 + k * 0.1;
        g.stroke(p);
      });
      for (let i = 0; i < 10; i++) {
        const ph = (t * 0.9 + unit("pp" + i)) % 1, y = gy + 14 + unit("py" + i) * (H - gy) * 0.55, x = (unit("px" + i) - 0.5) * W * 0.9, w = 30 + ph * 140;
        g.globalAlpha = Math.sin(ph * Math.PI) * 0.3;
        g.beginPath();
        g.moveTo(x - w / 2, y);
        g.lineTo(x + w / 2, y);
        g.stroke();
      }
      g.globalAlpha = 1;
    }
  };
  var SIGS = {
    0(s, t, c, _bt, px, _py, sh) {
      const g = s.g, { W, H } = s, lt = t - sh.def.t0, fade = 1 - clamp01(lt / 6);
      g.fillStyle = "#fff";
      for (let i = 0; i < 56; i++) {
        g.globalAlpha = fade * (0.15 + 0.55 * (0.5 + 0.5 * Math.sin(t * 1.3 + i)));
        g.fillRect((unit("0sx" + i) - 0.5) * W - px * 20, (unit("0sy" + i) - 0.5) * H * 0.8 - H * 0.1, 1.6, 1.6);
      }
      for (let k = 0; k < 4; k++) softBand(g, (t * (5 + k * 3) + k * 420) % (W * 1.7) - W * 0.85, H * (0.2 + 0.065 * k), W * 0.42, H * 0.05, k % 2 ? c.a2 : c.acc, "1c");
      g.strokeStyle = "#ffe9d0";
      g.lineWidth = 1.3;
      g.lineCap = "round";
      for (let b = 0; b < 8; b++) {
        const ph = (t * 0.035 + unit("0b" + b)) % 1, x = (-0.58 + ph * 1.2) * W, y = H * (-0.16 + 0.1 * unit("0y" + b)) + Math.sin(ph * 9 + b) * 8, w = Math.sin(t * 5.5 + b * 2) * 5, sz = 6 + unit("0z" + b) * 5;
        g.globalAlpha = 0.45 * Math.sin(ph * Math.PI);
        g.beginPath();
        g.moveTo(x - sz, y + w);
        g.quadraticCurveTo(x - sz * 0.4, y - sz * 0.2, x, y);
        g.quadraticCurveTo(x + sz * 0.4, y - sz * 0.2, x + sz, y + w);
        g.stroke();
      }
    },
    1(s, t, c, bt, px) {
      const g = s.g, { W, H } = s, n = 44, cols = [c.acc, c.a2, c.a3];
      g.lineCap = "round";
      g.lineWidth = 1;
      for (let i = 0; i < n; i++) {
        const u = unit("th" + i), q = Math.min(3, Math.floor(unit("tl" + i) * 4)), col = cols[i % 3];
        const x = (i / (n - 1) - 0.5) * W * 1.05 + (u - 0.5) * W * 0.02 - px * W * 0.02 * (0.4 + u), len = H * (0.25 + 0.15 * q);
        const sway = Math.sin(t * 0.5 + i * 0.7) * W * 4e-3 * (1 + bt);
        g.save();
        g.translate(x, 0);
        g.strokeStyle = threadGrad(g, col, q, H);
        g.globalAlpha = 0.16 + 0.22 * u;
        g.beginPath();
        g.moveTo(0, -H / 2);
        g.quadraticCurveTo(sway, -H / 2 + len * 0.5, sway * 1.6, -H / 2 + len);
        g.stroke();
        g.restore();
      }
      g.fillStyle = "#ffffff";
      for (let i = 0; i < 12; i++) {
        const u = unit("tb" + i), ph = (t * (0.03 + 0.03 * unit("tv" + i)) + u) % 1, x = (unit("tx" + i) - 0.5) * W * 0.95, y = -H / 2 + ph * H * 0.7;
        g.globalAlpha = 0.5 * Math.sin(ph * Math.PI);
        g.fillRect(x, y, 1.6, 1.6);
      }
    },
    2(s, t, c) {
      const g = s.g, { W, H } = s;
      for (let k = 0; k < 3; k++) softBand(g, (t * (3 + k * 2) + k * 520) % (W * 1.8) - W * 0.9, -H * (0.36 - 0.07 * k), W * 0.5, H * 0.035, "#9fb0d8", "14");
      const y = -H * 0.18;
      g.strokeStyle = c.acc;
      g.lineWidth = 1;
      g.globalAlpha = 0.16;
      g.setLineDash([2, 6]);
      g.beginPath();
      g.moveTo(-W / 2, y);
      g.lineTo(W / 2, y);
      g.stroke();
      g.setLineDash([]);
      g.fillStyle = "#ffffff";
      for (let i = 0; i < 36; i++) {
        const sp = 0.4 + unit("se" + i), x = (unit("sq" + i) - 0.5) * W, yy = ((unit("sw" + i) + t * 0.012 * sp) % 1 - 0.5) * H;
        g.globalAlpha = 0.12 + 0.22 * (0.5 + 0.5 * Math.sin(t * 1.1 + i * 2));
        g.fillRect(x, yy, 1.4, 1.4);
      }
    },
    3(s, t, c) {
      const g = s.g, { W, H } = s, bx = W * 0.27, by = -H * 0.36;
      g.strokeStyle = "#9fb0d8";
      g.globalAlpha = 0.4;
      g.lineWidth = 1;
      g.beginPath();
      g.moveTo(bx - 60, -H / 2);
      g.lineTo(bx - 60, by - 16);
      g.moveTo(bx + 60, -H / 2);
      g.lineTo(bx + 60, by - 16);
      g.stroke();
      g.strokeRect(bx - 80, by - 16, 160, 34);
      g.fillStyle = c.a2;
      g.globalAlpha = 0.14;
      g.fillRect(bx - 80, by - 16, 160, 34);
      g.fillStyle = "#e8ecff";
      g.globalAlpha = 0.65;
      g.font = `600 ${Math.round(H * 0.019)}px ${s.fonts.mono}`;
      g.textAlign = "center";
      g.textBaseline = "middle";
      g.fillText("03\u756A\u7DDA \xB7 PLATFORM 03", bx, by + 1);
      g.textBaseline = "alphabetic";
      const gy = H * 0.3 + 18;
      g.strokeStyle = c.acc;
      g.lineWidth = 3;
      g.globalAlpha = 0.5;
      g.setLineDash([16, 10]);
      g.beginPath();
      g.moveTo(-W / 2, gy);
      g.lineTo(W / 2, gy);
      g.stroke();
      g.setLineDash([]);
      g.lineWidth = 1;
      g.strokeStyle = "#9fb0d8";
      [-1, 1].forEach((sg) => {
        g.globalAlpha = 0.35;
        g.beginPath();
        g.moveTo(sg * W * 0.012, gy + 14);
        g.lineTo(sg * W * 0.58, H / 2);
        g.stroke();
      });
      for (let j = 0; j < 7; j++) {
        const u = (j + 0.5) / 7, y = gy + 14 + (H / 2 - gy - 14) * u * u, w = W * (0.012 + 0.58 * u * u);
        g.globalAlpha = 0.18;
        g.beginPath();
        g.moveTo(-w, y);
        g.lineTo(w, y);
        g.stroke();
      }
      g.fillStyle = "#ffe9c0";
      for (let i = 0; i < 9; i++) {
        const ph = (t * (0.05 + unit("3v" + i) * 0.06) + unit("3p" + i)) % 1, x = (-0.55 + ph * 1.2) * W, y = H * (0.02 + unit("3y" + i) * 0.28) + Math.sin(ph * 13 + i) * 18, r = t * 2 + i;
        g.save();
        g.translate(x, y);
        g.rotate(r);
        g.scale(1, Math.cos(r * 1.3));
        g.globalAlpha = 0.5 * Math.sin(ph * Math.PI);
        g.fillRect(-4, -3, 8, 6);
        g.restore();
      }
    },
    4(s, t, c) {
      const g = s.g, { W } = s;
      g.save();
      g.rotate(-0.12);
      g.lineWidth = 0.9;
      const tints = [c.acc, c.a2, c.a3];
      for (let k = 0; k < 16; k++) {
        const sz = 0.16 + k * 0.034 + 0.01 * Math.sin(t * 0.4 + k), A = W * sz, ph = t * 0.05 * (k % 2 ? 1 : -1) + k * 0.2;
        g.strokeStyle = tints[k % 3];
        g.globalAlpha = 0.07 + 0.04 * Math.sin(t * 0.7 + k);
        g.beginPath();
        for (let i = 0; i <= 100; i++) {
          const th = i / 100 * 6.2832 + ph, sn = Math.sin(th), cs = Math.cos(th), d = 1 + sn * sn, x = A * cs / d, y = A * 0.9 * sn * cs / d;
          i ? g.lineTo(x, y) : g.moveTo(x, y);
        }
        g.closePath();
        g.stroke();
      }
      g.restore();
    },
    5(s, t) {
      const g = s.g, { H } = s, hy = H * 0.08;
      g.fillStyle = "#ffffff";
      for (let j = 0; j < 8; j++) {
        const u = (j + t * 0.5 % 1) / 8, y = hy + 8 + Math.pow(u, 2.2) * (H / 2 - hy - 8), w = 1 + 22 * u * u, h = 2 + 40 * u * u;
        g.globalAlpha = 0.1 + 0.5 * u;
        g.fillRect(-w / 2, y, w, h);
      }
    },
    7(s, t, c) {
      const g = s.g, { W, H } = s, gy = H * 0.3;
      g.globalCompositeOperation = "lighter";
      const tints = [c.acc, c.a2, c.a3];
      for (let i = 0; i < 14; i++) {
        const x = (unit("7x" + i) - 0.5) * W, w = W * (3e-3 + unit("7w" + i) * 9e-3), col = tints[i % 3], grad = g.createLinearGradient(0, gy, 0, H / 2);
        grad.addColorStop(0, col + "40");
        grad.addColorStop(1, col + "00");
        g.fillStyle = grad;
        g.globalAlpha = 0.5 + 0.5 * Math.sin(t * 1.3 + i);
        g.fillRect(x - w / 2, gy, w, H / 2 - gy);
      }
      for (let k = 0; k < 6; k++) {
        const per = 1.1 + k * 0.45, ph = (t / per + k * 0.27) % 1, y = gy + H * (0.03 + k * 0.026), L = W * 0.3, dir = k % 2 ? -1 : 1, x = (-0.7 + ph * 1.6) * W * dir;
        const col = k % 2 ? "#ff4a5a" : "#fff2c8", grad = g.createLinearGradient(x - dir * L, 0, x, 0);
        grad.addColorStop(0, col + "00");
        grad.addColorStop(1, col + "ff");
        g.strokeStyle = grad;
        g.lineWidth = 1.6;
        g.globalAlpha = 0.65;
        g.beginPath();
        g.moveTo(x - dir * L, y);
        g.lineTo(x, y);
        g.stroke();
      }
    }
  };
  var drawClassicScene = (s, sh, P, bt, lin) => {
    const fn = SCENES[sh.theme];
    if (!fn) return false;
    const g = s.g;
    g.save();
    g.lineWidth = 1;
    fn(s, sh, s.t, { acc: P.acc, a2: P.acc2, a3: P.acc3 }, P, bt, lin, s.t - sh.def.t0);
    g.restore();
    return true;
  };
  var drawClassicSignature = (s, sh, c, bt, px, py) => {
    const fn = SIGS[sh.theme];
    if (!fn) return;
    const g = s.g;
    g.save();
    g.translate(s.W / 2, s.H / 2);
    g.globalCompositeOperation = "lighter";
    g.lineWidth = 1;
    g.lineCap = "butt";
    g.lineJoin = "miter";
    fn(s, s.t, c, bt, px, py, sh);
    g.restore();
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/blossom/blossomSeasonScenes.ts
  var T = (tag, n) => table(tag, n, unit);
  var TAU2 = Math.PI * 2;
  var STARS = { x: T("ssx", 90), y: T("ssy", 90), z: T("ssz", 90), p: T("ssp", 90) };
  var SEA = { x: T("sex", 70), y: T("sey", 70), w: T("sew", 70), p: T("sep", 70) };
  var PETAL = { x: T("pex", 96), y: T("pey", 96), z: T("pez", 96), p: T("pep", 96), s: T("pes", 96) };
  var SNOW = { x: T("snx", 160), y: T("sny", 160), z: T("snz", 160), p: T("snp", 160) };
  var LAMPS = { x: T("lmx", 9), h: T("lmh", 9) };
  var SKYLINE = { w: T("skw", 26), h: T("skh", 26) };
  var BURST = { x: T("fbx", 64), y: T("fby", 64), c: T("fbc", 64), n: T("fbn", 64) };
  var SPARK = { a: T("fsa", 56), v: T("fsv", 56) };
  var starField = (s, t, px, top, bottom, gain) => {
    const g = s.g, { W, H } = s;
    g.fillStyle = "#ffffff";
    for (let i = 0; i < 90; i++) {
      const z = STARS.z[i], y = top + (bottom - top) * STARS.y[i];
      const x = (STARS.x[i] - 0.5) * W * 1.1 - px * (6 + 18 * z), tw = 0.5 + 0.5 * Math.sin(t * (0.3 + 0.5 * z) + STARS.p[i] * TAU2);
      const a = (0.12 + 0.5 * tw) * (0.5 + 0.5 * z) * gain, r = 0.8 + 1.4 * z;
      g.globalAlpha = a;
      g.fillRect(x - r / 2, y * H - r / 2, r, r);
      if (z > 0.93) glint(g, x, y * H, 9 + 8 * z, "#ffffff", a * 0.8);
    }
  };
  var silhouette = (g, W, H, base3, amp, seed, top, bottom, sx, rim, rimA) => {
    const crest = new Path2D();
    for (let x = -W * 0.7, i = 0; x <= W * 0.7 + 1; x += 18, i++) {
      const u = x + sx, y = base3 + amp * (0.55 * Math.sin(u * 35e-4 + seed) + 0.3 * Math.sin(u * 9e-3 + seed * 1.9) + 0.15 * Math.sin(u * 0.023 + seed * 3.1));
      if (i) crest.lineTo(x, y);
      else crest.moveTo(x, y);
    }
    const body = new Path2D(crest);
    body.lineTo(W * 0.7, H);
    body.lineTo(-W * 0.7, H);
    body.closePath();
    const fill = g.createLinearGradient(0, base3 - amp, 0, base3 + amp * 3);
    fill.addColorStop(0, top);
    fill.addColorStop(1, bottom);
    g.globalCompositeOperation = "source-over";
    g.globalAlpha = 1;
    g.fillStyle = fill;
    g.fill(body);
    g.globalCompositeOperation = "lighter";
    g.globalAlpha = rimA;
    g.strokeStyle = rim;
    g.lineWidth = 1;
    g.stroke(crest);
    g.globalCompositeOperation = "source-over";
    g.globalAlpha = 1;
  };
  var petal = (p, x, y, a, L, flat) => {
    const c = Math.cos(a), s = Math.sin(a), wv = L * 0.38 * flat;
    const pt = (u, v) => [x + u * c - v * s, y + u * s + v * c];
    const [x0, y0] = pt(-L / 2, 0), [x1, y1] = pt(L / 2, 0), [c0x, c0y] = pt(0, -wv), [c1x, c1y] = pt(0, wv);
    p.moveTo(x0, y0);
    p.quadraticCurveTo(c0x, c0y, x1, y1);
    p.quadraticCurveTo(c1x, c1y, x0, y0);
    p.closePath();
  };
  var FW_EVERY = 1.1;
  var FW_LIFE = 2.4;
  var SEASON_SCENES = {
    11(s, _sh, t, c, _P, bt) {
      const g = s.g, { W, H } = s, hy = H * 0.1, mx = W * 0.2, my = -H * 0.24;
      g.globalCompositeOperation = "lighter";
      mist(g, mx, my, H * 0.75, H * 0.6, c.a3, 0.18);
      glow(g, mx, my, H * 0.2, H * 0.2, "#fff4dc", 0.3);
      glow(g, mx, my, H * 0.055, H * 0.055, "#fffaf0", 0.6 * (1 + 0.08 * bt));
      const hl = g.createLinearGradient(-W * 0.6, 0, W * 0.6, 0);
      hl.addColorStop(0, c.acc + "00");
      hl.addColorStop(0.5 + mx / W / 1.2, "#fff4dc66");
      hl.addColorStop(1, c.acc + "00");
      g.fillStyle = hl;
      g.globalAlpha = 1;
      g.fillRect(-W * 0.6, hy - 0.6, W * 1.2, 1.2);
      g.fillStyle = "#fff4dc";
      for (let i = 0; i < 70; i++) {
        const d = SEA.y[i], y = hy + 4 + d * d * (H * 0.5 - hy), spread = 10 + d * W * 0.16;
        const x = mx + (SEA.x[i] - 0.5) * spread * 2 + Math.sin(t * 0.7 + SEA.p[i] * TAU2) * 4, w = (6 + SEA.w[i] * 26) * (0.5 + d);
        const a = Math.max(0, Math.sin(t * (0.9 + SEA.w[i]) + SEA.p[i] * TAU2)) * (0.55 - 0.35 * d);
        if (a < 0.02) continue;
        g.globalAlpha = a;
        g.fillRect(x - w / 2, y, w, 1 + d * 1.6);
      }
      g.strokeStyle = c.a2;
      g.lineWidth = 1;
      for (let k = 0; k < 6; k++) {
        const u = (k + 1) / 7, y0 = hy + u * u * (H * 0.5 - hy);
        g.globalAlpha = 0.05 + 0.05 * u;
        g.beginPath();
        for (let x = -W * 0.6; x <= W * 0.6; x += 30) {
          const y = y0 + Math.sin(x * 6e-3 + t * 0.4 + k) * (2 + 6 * u);
          x === -W * 0.6 ? g.moveTo(x, y) : g.lineTo(x, y);
        }
        g.stroke();
      }
      g.globalCompositeOperation = "source-over";
      g.globalAlpha = 1;
    },
    12(s, _sh, t, c, _P, bt, lin) {
      const g = s.g, { W, H } = s, cols = [c.acc, c.a2, c.a3], N = 48, x0 = -W * 0.65, sw = W * 1.3 / N;
      g.globalCompositeOperation = "lighter";
      g.lineWidth = 1;
      for (let k = 0; k < 3; k++) {
        const col = cols[k], L = H * (0.34 + 0.08 * k), base3 = -H * (0.34 - 0.08 * k), tops = [], lit = [];
        for (let i = 0; i <= N; i++) {
          const u = (x0 + i * sw) / W;
          tops.push(base3 + (Math.sin(u * 5.2 + t * (0.16 + 0.05 * k) + k * 2.1) + 0.45 * Math.sin(u * 11 - t * 0.23 + k)) * H * 0.06);
          const v = 0.5 + 0.5 * Math.sin(u * 7 + t * (0.5 + 0.2 * k) + k * 1.3);
          lit.push(v * v * clamp01(1 - Math.abs(u) * 0.9));
        }
        const top = Math.min(...tops), grad = g.createLinearGradient(0, top, 0, top + L);
        grad.addColorStop(0, col + "00");
        grad.addColorStop(0.12, col + "66");
        grad.addColorStop(0.45, col + "22");
        grad.addColorStop(1, col + "00");
        const sheet = new Path2D();
        tops.forEach((y, i) => i ? sheet.lineTo(x0 + i * sw, y) : sheet.moveTo(x0, y));
        for (let i = N; i >= 0; i--) sheet.lineTo(x0 + i * sw, tops[i] + L);
        sheet.closePath();
        g.fillStyle = grad;
        g.globalAlpha = 0.22 * (1 + 0.15 * bt);
        g.fill(sheet);
        const rays = [new Path2D(), new Path2D(), new Path2D(), new Path2D()];
        for (let i = 0; i < N * 3; i++) {
          const f = i / 3, a = Math.floor(f), w = f - a, y = tops[a] * (1 - w) + tops[Math.min(N, a + 1)] * w, v = lit[a] * (1 - w) + lit[Math.min(N, a + 1)] * w;
          const step = Math.min(3, Math.floor(v * 4));
          if (v < 0.08) continue;
          const x = x0 + f * sw;
          rays[step].moveTo(x, y + 2);
          rays[step].lineTo(x, y + L * (0.35 + 0.4 * v));
        }
        g.strokeStyle = grad;
        rays.forEach((p, n) => {
          g.globalAlpha = (0.08 + 0.1 * n) * (1 + 0.15 * bt);
          g.stroke(p);
        });
      }
      g.globalCompositeOperation = "source-over";
      g.globalAlpha = 1;
      silhouette(g, W, H, H * 0.3, H * 0.05, 2.7, hexMix("#0c1120", c.acc, 0.16), "#04060c", -lin * 40, c.a2, 0.22);
      g.globalCompositeOperation = "lighter";
      mist(g, 0, H * 0.36, W * 0.8, H * 0.04, c.a3, 0.08);
      g.globalCompositeOperation = "source-over";
      silhouette(g, W, H, H * 0.38, H * 0.04, 5.1, "#070912", "#020308", -lin * 80, c.acc, 0.1);
    },
    13(s, _sh, t, c, _P, bt) {
      const g = s.g, { W, H } = s;
      g.globalCompositeOperation = "lighter";
      beam(g, -W * 0.3, -H * 0.62, 0.55, H * 1.6, W * 0.42, c.acc, 0.07);
      beam(g, W * 0.05, -H * 0.62, 0.62, H * 1.5, W * 0.28, c.a2, 0.05);
      mist(g, -W * 0.15, -H * 0.1, W * 0.6, H * 0.45, "#ffc2d6", 0.16);
      g.globalCompositeOperation = "source-over";
      const layers = [new Path2D(), new Path2D(), new Path2D()], light = new Path2D();
      for (let i = 0; i < 96; i++) {
        const z = PETAL.z[i], d = z < 0.55 ? 0 : z < 0.88 ? 1 : 2, fall = 0.02 + 0.035 * z;
        const y = (PETAL.y[i] + t * fall) % 1 * H * 1.2 - H * 0.6, drift = Math.sin(t * (0.4 + 0.3 * PETAL.s[i]) + PETAL.p[i] * TAU2);
        const x = ((PETAL.x[i] - t * 0.012 * (0.5 + z)) % 1 + 1) % 1 * W * 1.3 - W * 0.65 + drift * W * 0.025;
        const a = PETAL.p[i] * TAU2 + t * (0.6 + 1.4 * PETAL.s[i]) * (i % 2 ? 1 : -1), flat = 0.3 + 0.7 * Math.abs(Math.sin(t * (1.1 + PETAL.s[i]) + PETAL.p[i] * 9));
        const L = H * (0.012 + 0.03 * z * z);
        petal(flat > 0.85 && d > 0 ? light : layers[d], x, y, a, L, flat);
      }
      const pink = hexLerp("#ffd3e0", c.acc, 0.2);
      [[0.22, 0.55], [0.5, 0.75], [0.78, 0.9]].forEach(([alpha, mix], d) => {
        g.fillStyle = hexLerp("#2a1420", pink, mix);
        g.globalAlpha = alpha * (1 + 0.2 * bt);
        g.fill(layers[d]);
      });
      g.globalCompositeOperation = "lighter";
      g.fillStyle = "#ffe9f0";
      g.globalAlpha = 0.5;
      g.fill(light);
      g.globalCompositeOperation = "source-over";
      g.globalAlpha = 1;
    },
    14(s, _sh, t, c, _P, bt, lin) {
      const g = s.g, { W, H } = s, gy = H * 0.24;
      g.globalCompositeOperation = "lighter";
      const ground = g.createLinearGradient(0, gy - H * 0.04, 0, H * 0.5);
      ground.addColorStop(0, "#dfe8ff00");
      ground.addColorStop(0.15, "#dfe8ff22");
      ground.addColorStop(1, "#dfe8ff08");
      g.fillStyle = ground;
      g.fillRect(-W * 0.7, gy - H * 0.04, W * 1.4, H);
      for (let i = 0; i < 9; i++) {
        const x = (LAMPS.x[i] - 0.5) * W * 1.1 - lin * 30, y = gy - H * (0.015 + 0.03 * LAMPS.h[i]);
        glow(g, x, y, H * 0.09, H * 0.09, "#ffd9a0", 0.4 * (1 + 0.2 * bt));
        glow(g, x, y, H * 0.012, H * 0.012, "#fff2d0", 0.9);
        mist(g, x, gy + H * 0.03, H * 0.12, H * 0.025, "#ffd9a0", 0.12);
      }
      mist(g, 0, gy, W * 0.7, H * 0.08, c.a3, 0.1);
      g.fillStyle = "#f2f6ff";
      for (let i = 0; i < 160; i++) {
        const z = SNOW.z[i], sp = 0.025 + 0.06 * z, y = (SNOW.y[i] + t * sp) % 1 * H * 1.2 - H * 0.6;
        const x = (SNOW.x[i] + Math.sin(t * (0.3 + 0.4 * z) + SNOW.p[i] * TAU2) * 0.012) % 1 * W * 1.3 - W * 0.65;
        if (z > 0.9) {
          glow(g, x, y, H * 0.012 * z, H * 0.012 * z, "#f2f6ff", 0.55, false);
          continue;
        }
        const r = 0.8 + 2.2 * z;
        g.globalAlpha = 0.25 + 0.5 * z;
        g.fillRect(x - r / 2, y - r / 2, r, r);
      }
      g.globalCompositeOperation = "source-over";
      g.globalAlpha = 1;
    },
    15(s, _sh, t, c, _P, bt, lin) {
      const g = s.g, { W, H } = s, cols = [c.acc, c.a2, c.a3, "#fff2c8"], sky = H * 0.3;
      g.globalCompositeOperation = "lighter";
      g.lineCap = "round";
      const first = Math.floor((t - FW_LIFE) / FW_EVERY) + 1, last = Math.floor(t / FW_EVERY);
      for (let k = first; k <= last; k++) {
        const age = t - k * FW_EVERY, idx = (k % 64 + 64) % 64;
        if (age < 0 || age > FW_LIFE) continue;
        const bx = (BURST.x[idx] - 0.5) * W * 0.9, by = -H * (0.12 + 0.3 * BURST.y[idx]), col = cols[Math.floor(BURST.c[idx] * 4)], n = 36 + Math.floor(BURST.n[idx] * 20);
        const R = H * (0.16 + 0.12 * BURST.n[idx]), fade = Math.pow(1 - age / FW_LIFE, 1.6) * (1 + 0.15 * bt);
        if (age < 0.5) glow(g, bx, by, R * 0.5, R * 0.5, col, 0.28 * (1 - age / 0.5));
        mist(g, bx, by, R * 1.1, R * 0.9, col, 0.08 * fade);
        const path = new Path2D();
        for (let i = 0; i < n; i++) {
          const ang = i / n * TAU2 + SPARK.a[i % 56] * 0.2, v = R * (0.75 + 0.35 * SPARK.v[i % 56]);
          const reach = (u) => {
            const d = v * (1 - Math.exp(-u * 3.2));
            return [bx + Math.cos(ang) * d, by + Math.sin(ang) * d + H * 0.05 * u * u];
          };
          const [x0, y0] = reach(Math.max(0, age - 0.16)), [x1, y1] = reach(age);
          path.moveTo(x0, y0);
          path.lineTo(x1, y1);
        }
        g.strokeStyle = col;
        g.globalAlpha = 0.6 * fade;
        g.lineWidth = 1.6;
        g.stroke(path);
        g.strokeStyle = "#ffffff";
        g.globalAlpha = 0.22 * fade;
        g.lineWidth = 0.7;
        g.stroke(path);
      }
      g.globalCompositeOperation = "source-over";
      g.globalAlpha = 1;
      const towers = new Path2D(), masts = new Path2D(), windows = new Path2D(), unitW = W * 0.022;
      let x = -W * 0.65 - lin * 30 % unitW;
      for (let i = 0; x < W * 0.65; i++) {
        const k = (i % 26 + 26) % 26, w = unitW * (1 + Math.floor(SKYLINE.w[k] * 3)), h = H * (0.04 + 0.13 * SKYLINE.h[k]);
        towers.rect(x, sky - h, w + 0.5, H);
        if (SKYLINE.w[k] > 0.6) towers.rect(x + w * 0.25, sky - h - H * 0.02, w * 0.5, H * 0.02 + 1);
        if (SKYLINE.h[k] > 0.8) {
          masts.moveTo(x + w / 2, sky - h - H * 0.02);
          masts.lineTo(x + w / 2, sky - h - H * 0.05);
        }
        for (let r = 1; sky - h + r * 11 < sky - 6; r++) for (let col = 0; col * 8 + 6 < w; col++) {
          if (SKYLINE.h[(k * 5 + r * 3 + col * 7) % 26] > 0.84) windows.rect(x + 4 + col * 8, sky - h + r * 11, 3, 3.5);
        }
        x += w;
      }
      const face = g.createLinearGradient(0, sky - H * 0.18, 0, sky + H * 0.1);
      face.addColorStop(0, hexMix("#0b0d18", c.acc, 0.1));
      face.addColorStop(1, "#020308");
      g.fillStyle = face;
      g.fill(towers);
      g.strokeStyle = "#0b0d18";
      g.lineWidth = 1;
      g.stroke(masts);
      g.fillStyle = "#ffd9a0";
      g.globalAlpha = 0.28;
      g.fill(windows);
      g.globalAlpha = 1;
    }
  };
  var SEASON_SIGNATURES = {
    11(s, t, _c, _bt, px) {
      starField(s, t, px, -0.5, 0.05, 0.9);
    },
    12(s, t, _c, _bt, px) {
      starField(s, t, px, -0.5, 0.2, 1);
    },
    13(s, t, c) {
      const g = s.g, { W, H } = s;
      for (let i = 0; i < 18; i++) {
        const x = (PETAL.x[i] - 0.5) * W, y = ((PETAL.y[i] - t * 0.01) % 1 + 1) % 1 * H - H / 2, a = 0.5 + 0.5 * Math.sin(t * 0.8 + i);
        glow(g, x, y, 6, 6, i % 2 ? c.acc : "#fff0f4", 0.5 * a);
      }
    },
    14(s, t, _c, _bt, px) {
      starField(s, t, px, -0.5, -0.2, 0.45);
    },
    15(s, t, c, bt, px) {
      starField(s, t, px, -0.5, -0.1, 0.4);
      if (bt > 0.05) streak(s.g, 0, -s.H * 0.18, s.W * 1.4, s.H * 0.04, c.acc, 0.1 * bt);
    }
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/blossom/blossomSkyScenes.ts
  var T2 = (tag, n) => table(tag, n, unit);
  var TAU3 = Math.PI * 2;
  var wrap = (v) => (v % 1 + 1) % 1;
  var BOKEH = { x: T2("bkx", 28), y: T2("bky", 28), r: T2("bkr", 28), c: T2("bkc", 28), p: T2("bkp", 28) };
  var DROP = { x: T2("drx", 44), y: T2("dry", 44), s: T2("drs", 44), l: T2("drl", 44), p: T2("drp", 44) };
  var MOTE = { x: T2("mtx", 90), y: T2("mty", 90), z: T2("mtz", 90), p: T2("mtp", 90) };
  var BUBBLE = { x: T2("bbx", 22), s: T2("bbs", 22), p: T2("bbp", 22) };
  var SHORE = { x: T2("shx", 9), p: T2("shp", 9) };
  var DRIP = { x: T2("dpx", 40), y: T2("dpy", 40) };
  var DRIP_EVERY = 0.9;
  var LANTERN = { x: T2("lnx", 34), y: T2("lny", 34), z: T2("lnz", 34), p: T2("lnp", 34) };
  var CLOUD = { x: T2("clx", 48), y: T2("cly", 48), r: T2("clr", 48) };
  var SKY_SCENES = {
    16(s, _sh, t, c) {
      const g = s.g, { W, H } = s, cols = [c.acc, c.a2, c.a3, "#ffcf8a", "#8ab4ff"];
      g.globalCompositeOperation = "lighter";
      for (let i = 0; i < 28; i++) {
        const x = (BOKEH.x[i] - 0.5) * W * 1.15 + Math.sin(t * 0.05 + i) * 6, y = (BOKEH.y[i] - 0.45) * H * 0.9;
        const r = H * (0.03 + 0.07 * BOKEH.r[i]), tw = 0.75 + 0.25 * Math.sin(t * 0.4 + BOKEH.p[i] * TAU3);
        glow(g, x, y, r, r, cols[Math.floor(BOKEH.c[i] * 5)], (0.32 + 0.3 * (1 - BOKEH.r[i])) * tw, false);
      }
      g.globalCompositeOperation = "source-over";
    },
    17(s, _sh, t, c, _P, bt) {
      const g = s.g, { W, H } = s;
      g.globalCompositeOperation = "lighter";
      mist(g, 0, -H * 0.55, W * 0.8, H * 0.4, c.a3, 0.36);
      for (let i = 0; i < 7; i++) {
        const sway = Math.sin(t * 0.21 + i * 1.7) * 0.05, a = (0.11 + 0.05 * Math.sin(t * 0.5 + i * 2.3)) * (1 + 0.1 * bt);
        beam(g, (i - 3) * W * 0.11, -H * 0.62, Math.PI / 2 + 0.12 * (i - 3) * 0.3 + sway, H * 1.25, W * (0.08 + 0.03 * (i % 3)), i % 2 ? c.a2 : c.a3, a);
      }
      g.strokeStyle = "#cfe8ff";
      g.lineWidth = 1;
      for (let k = 0; k < 5; k++) {
        g.globalAlpha = 0.06 - k * 8e-3;
        g.beginPath();
        for (let x = -W * 0.6; x <= W * 0.6; x += 28) {
          const y = -H * 0.46 + k * 9 + Math.sin(x * 0.012 + t * 0.6 + k) * 4;
          x === -W * 0.6 ? g.moveTo(x, y) : g.lineTo(x, y);
        }
        g.stroke();
      }
      g.fillStyle = "#d8ecff";
      for (let i = 0; i < 90; i++) {
        const z = MOTE.z[i], y = wrap(MOTE.y[i] + t * 4e-3 * (0.3 + z)) * H * 1.2 - H * 0.6, x = (MOTE.x[i] - 0.5) * W * 1.2 + Math.sin(t * 0.2 + MOTE.p[i] * TAU3) * 10;
        g.globalAlpha = 0.08 + 0.2 * z;
        const r = 0.8 + 1.6 * z;
        g.fillRect(x, y, r, r);
      }
      for (let i = 0; i < 22; i++) {
        const sp = 0.03 + 0.04 * BUBBLE.s[i], y = H * 0.6 - wrap(BUBBLE.p[i] + t * sp) * H * 1.3, x = (BUBBLE.x[i] - 0.5) * W * 0.9 + Math.sin(t * 2 + i) * 5;
        const r = 2 + 4 * BUBBLE.s[i];
        glow(g, x, y, r * 2, r * 2, "#cfe8ff", 0.18, false);
        glint(g, x - r * 0.3, y - r * 0.3, r * 1.4, "#ffffff", 0.25);
      }
      g.globalCompositeOperation = "source-over";
      g.globalAlpha = 1;
    },
    18(s, sh, t, c) {
      const g = s.g, { W, H } = s, hy = -H * 0.04, deep = H * 0.5 - hy;
      g.globalCompositeOperation = "lighter";
      for (let i = 0; i < 9; i++) {
        const x = (SHORE.x[i] - 0.5) * W * 1.1, col = i % 3 ? c.a3 : c.acc, sh2 = 0.75 + 0.25 * Math.sin(t * 1.3 + SHORE.p[i] * TAU3);
        glow(g, x, hy - 2, H * 0.012, H * 8e-3, "#fff1d8", 0.6);
        panel(g, x + Math.sin(t * 1.7 + i) * 2, hy + H * 0.12, H * 0.01, H * 0.22, col, 0.3 * sh2);
      }
      const line = g.createLinearGradient(-W * 0.6, 0, W * 0.6, 0);
      line.addColorStop(0, c.a3 + "00");
      line.addColorStop(0.5, c.a3 + "55");
      line.addColorStop(1, c.a3 + "00");
      g.fillStyle = line;
      g.globalAlpha = 1;
      g.fillRect(-W * 0.6, hy, W * 1.2, 1);
      const ring = (x, y, age, life, size, col, gain) => {
        const depth = clamp01((y - hy) / deep), flat = 0.12 + 0.3 * depth, reach = size * (0.35 + 0.65 * depth);
        for (let k = 0; k < 3; k++) {
          const a = age - k * 0.22;
          if (a <= 0 || a > life) continue;
          const u = a / life, r = reach * (0.08 + 0.92 * (1 - Math.pow(1 - u, 2)));
          g.strokeStyle = col;
          g.globalAlpha = gain * Math.pow(1 - u, 1.6) * (1 - k * 0.25);
          g.lineWidth = 0.8 + depth;
          g.beginPath();
          g.ellipse(x, y, r, r * flat, 0, 0, TAU3);
          g.stroke();
        }
      };
      sh.boxes.forEach((b) => {
        const age = t - b.seg.start;
        if (age < 0 || age > 3.2) return;
        const k = (b.seg.idx * 7 + sh.si * 13) % 40;
        ring((DRIP.y[k] - 0.5) * W * 0.9, hy + deep * (0.25 + 0.6 * DRIP.x[k]), age, 3.2, W * 0.24, c.acc, 0.5);
      });
      const first = Math.floor((t - 3) / DRIP_EVERY) + 1, last = Math.floor(t / DRIP_EVERY);
      for (let k = first; k <= last; k++) {
        const idx = (k % 40 + 40) % 40, age = t - k * DRIP_EVERY;
        ring((DRIP.x[idx] - 0.5) * W * 1.1, hy + deep * (0.08 + 0.85 * DRIP.y[idx]), age, 3, W * 0.12, "#cfe0ff", 0.28);
      }
      g.globalCompositeOperation = "source-over";
      g.globalAlpha = 1;
    },
    19(s, _sh, t, c, _P, bt) {
      const g = s.g, { W, H } = s, wy = H * 0.28, warm2 = hexLerp("#ffc27a", c.acc, 0.2);
      g.globalCompositeOperation = "lighter";
      mist(g, 0, wy, W * 0.7, H * 0.06, warm2, 0.1);
      for (let i = 0; i < 34; i++) {
        const z = LANTERN.z[i], sp = 6e-3 + 0.012 * z, y = wy - wrap(LANTERN.y[i] + t * sp) * H * 1.05;
        const x = (LANTERN.x[i] - 0.5) * W * 1.1 + Math.sin(t * 0.3 + LANTERN.p[i] * TAU3) * W * 0.01 * (0.5 + z);
        const size = H * (0.014 + 0.035 * z * z), up = clamp01((wy - y) / (H * 0.08)), breathe = 0.85 + 0.15 * Math.sin(t * 1.3 + LANTERN.p[i] * TAU3);
        const a = up * breathe * (0.4 + 0.5 * z) * (1 + 0.1 * bt);
        glow(g, x, y, size * 3, size * 3, warm2, 0.4 * a, false);
        panel(g, x, y, size * 0.8, size, "#ffd9a0", 0.9 * a);
        glow(g, x, y + size * 0.22, size * 0.35, size * 0.3, "#fff2d6", 0.55 * a);
        const low = clamp01(1 - (wy - y) / (H * 0.5));
        if (low > 0.05) panel(g, x, wy + (wy - y) * 0.25 + H * 0.02, size * 0.6, size * 2.4 * low, warm2, 0.25 * a * low);
      }
      g.globalCompositeOperation = "source-over";
      g.globalAlpha = 1;
    },
    20(s, _sh, t, c, _P, bt, lin) {
      const g = s.g, { W, H } = s, sun = { x: W * 0.24, y: -H * 0.3 };
      g.globalCompositeOperation = "lighter";
      mist(g, sun.x, sun.y, W * 0.6, H * 0.5, c.a3, 0.3);
      glow(g, sun.x, sun.y, H * 0.09, H * 0.09, "#fff6e6", 0.6 * (1 + 0.06 * bt));
      for (let i = 0; i < 5; i++) beam(g, sun.x, sun.y, Math.PI / 2 + 0.35 + i * 0.16 + Math.sin(t * 0.08 + i) * 0.02, H * 1.3, W * 0.16, c.a3, 0.04);
      for (let d = 0; d < 3; d++) {
        const base3 = H * (0.06 + 0.13 * d), speed = 4 + 7 * d, body = hexLerp("#1a2032", d === 2 ? c.a2 : c.a3, 0.18 + 0.06 * d), lit = hexLerp("#c9d4f0", c.a3, 0.3);
        for (let i = d * 16; i < d * 16 + 16; i++) {
          const x = wrap(CLOUD.x[i] + (t * speed + lin * 40 * (d + 1)) / (W * 1.6)) * W * 1.6 - W * 0.8, y = base3 + (CLOUD.y[i] - 0.5) * H * 0.05;
          const r = H * (0.06 + 0.07 * CLOUD.r[i]) * (1 + 0.35 * d);
          g.globalCompositeOperation = "source-over";
          mist(g, x, y + r * 0.2, r * 2, r * 0.85, body, 1.6);
          g.globalCompositeOperation = "lighter";
          mist(g, x - r * 0.2, y - r * 0.25, r * 1.4, r * 0.4, lit, 0.16 + 0.05 * d);
        }
      }
      g.globalCompositeOperation = "source-over";
      g.globalAlpha = 1;
    }
  };
  var SKY_SIGNATURES = {
    16(s, t) {
      const g = s.g, { W, H } = s, heads = new Path2D(), trails = new Path2D();
      for (let i = 0; i < 44; i++) {
        const run = Math.max(0, Math.sin(t * (0.25 + 0.3 * DROP.s[i]) + DROP.p[i] * TAU3));
        const y = wrap(DROP.y[i] + t * 0.03 * DROP.s[i] + run * 0.02) * H * 1.1 - H * 0.55, x = (DROP.x[i] - 0.5) * W * 1.05;
        const r = 2.4 + 3.6 * DROP.l[i], len = H * (0.02 + 0.06 * DROP.l[i]) * run;
        heads.rect(x - r / 2, y - r, r, r * 1.4);
        if (len > 2) trails.rect(x - 0.6, y - len, 1.2, len);
      }
      g.fillStyle = "#e6f0ff";
      g.globalAlpha = 0.16;
      g.fill(trails);
      g.globalAlpha = 0.42;
      g.fill(heads);
    },
    17() {
    },
    18(s, t) {
      const g = s.g, { W, H } = s, hy = -H * 0.04;
      g.fillStyle = "#f2f4ff";
      for (let i = 0; i < 30; i++) {
        const x = (MOTE.x[i] - 0.5) * W, y = hy - MOTE.y[i] * H * 0.42, tw = 0.5 + 0.5 * Math.sin(t * 0.6 + i * 1.7);
        g.globalAlpha = 0.15 + 0.3 * tw;
        g.fillRect(x, y, 1.4, 1.4);
        g.globalAlpha = (0.05 + 0.1 * tw) * (0.6 + 0.4 * Math.sin(t * 2.2 + i));
        g.fillRect(x + Math.sin(t * 1.4 + i) * 1.5, 2 * hy - y, 1.4, 2.2);
      }
    },
    19(s, t) {
      const g = s.g, { W, H } = s;
      g.fillStyle = "#ffffff";
      for (let i = 0; i < 40; i++) {
        g.globalAlpha = 0.1 + 0.2 * (0.5 + 0.5 * Math.sin(t * 0.5 + i * 1.3));
        g.fillRect((MOTE.x[i] - 0.5) * W, -H * 0.5 + MOTE.y[i] * H * 0.5, 1.2, 1.2);
      }
    },
    20() {
    }
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/blossom/blossomSigns.ts
  var T3 = (tag, n) => table(tag, n, unit);
  var SKY = (() => {
    const pts = Array.from({ length: 56 }, (_, i) => [unit("kx" + i) - 0.5, unit("ky" + i) - 0.5, unit("kz" + i)]);
    const edges = [];
    pts.forEach((a, i) => {
      let best = -1, bd = 0.19;
      pts.forEach((b, j) => {
        if (j <= i) return;
        const d = Math.hypot(a[0] - b[0], a[1] - b[1]);
        if (d < bd) {
          bd = d;
          best = j;
        }
      });
      if (best >= 0) edges.push([i, best]);
    });
    return { pts, edges };
  })();
  var FLY = { x: T3("9x", 24), y: T3("9y", 24), t: T3("9t", 24) };
  var STAR = { x: T3("6x", 130), y: T3("6y", 130), z: T3("6z", 130), p: T3("6p", 130) };
  var DUSTBAND = { a: T3("mw", 150), d: T3("md", 150), u: T3("mu", 150), p: T3("mp", 150) };
  var BEAMS10 = { a: T3("10a", 80), s: T3("10s", 80), p: T3("10p", 80) };
  var SIGS2 = {
    6(s, t, c, _bt, px) {
      const g = s.g, { W, H } = s, sx = W * 1.25, sy = H * 1.25;
      g.save();
      g.rotate(t * 3e-3 - 0.1);
      g.save();
      g.rotate(-0.3);
      mist(g, 0, 0, W * 0.7, H * 0.12, "#8fa3ff", 0.16);
      mist(g, W * 0.1, 0, W * 0.5, H * 0.07, c.a3, 0.1);
      g.restore();
      for (let i = 0; i < 150; i++) {
        const d = (DUSTBAND.d[i] - 0.5) * 0.14, u = DUSTBAND.u[i] - 0.5, x = u * W * 1.2 - 0.48 * d * H, y = u * H * 0.8 + d * H;
        g.fillStyle = i % 3 ? "#cfd8ff" : c.acc;
        g.globalAlpha = 0.1 + 0.25 * (0.5 + 0.5 * Math.sin(t * 0.6 + DUSTBAND.a[i] * 6.28));
        g.fillRect(x - px * 12, y, 1.2, 1.2);
      }
      g.strokeStyle = "#cfd8ff";
      g.lineWidth = 0.8;
      g.globalAlpha = 0.1;
      g.beginPath();
      SKY.edges.forEach(([i, j]) => {
        const a = SKY.pts[i], b = SKY.pts[j];
        g.moveTo(a[0] * sx - px * 20 * a[2], a[1] * sy);
        g.lineTo(b[0] * sx - px * 20 * b[2], b[1] * sy);
      });
      g.stroke();
      g.fillStyle = "#fff";
      for (let i = 0; i < 130; i++) {
        const z = STAR.z[i], x = (STAR.x[i] - 0.5) * sx - px * (8 + 28 * z), y = (STAR.y[i] - 0.5) * sy, a = 0.22 + 0.6 * (0.5 + 0.5 * Math.sin(t * (0.25 + z * 0.45) + STAR.p[i] * 6.28)), r = 1.3 + z * 2;
        g.globalAlpha = a * (0.7 + 0.3 * z);
        g.fillRect(x - r / 2, y - r / 2, r, r);
        if (z > 0.9) glint(g, x, y, 11 + z * 8, i % 4 ? "#ffffff" : c.acc, a * 0.7);
      }
      g.restore();
    },
    8() {
    },
    9(s, t, c) {
      const g = s.g, { W, H } = s;
      for (let i = 0; i < 24; i++) {
        const x = (FLY.x[i] - 0.5) * W * 0.9 + Math.sin(t * 0.3 + i * 2.1) * 30, y = H * 0.1 + FLY.y[i] * H * 0.38 + Math.cos(t * 0.25 + i * 1.3) * 22;
        const tw = Math.max(0, Math.sin(t * (0.3 + FLY.t[i] * 0.35) + i * 3)), a = tw * tw;
        glow(g, x, y, 14, 14, "#e8ffb8", 0.75 * a);
        g.globalAlpha = 0.8 * a;
        g.fillStyle = "#f4ffd8";
        g.fillRect(x - 1.1, y - 1.1, 2.2, 2.2);
      }
      mist(g, W * 0.2, H * 0.4, W * 0.5, H * 0.07, c.a3, 0.05);
    },
    10(s, t, c) {
      const g = s.g, { W, H } = s;
      g.lineCap = "round";
      for (let i = 0; i < 80; i++) {
        const an = BEAMS10.a[i] * 6.2832, u = (t * (0.06 + BEAMS10.s[i] * 0.12) + BEAMS10.p[i]) % 1, r0 = H * 0.06 + Math.pow(u, 2.2) * W * 0.62, r1 = r0 + 5 + u * u * W * 0.15;
        g.strokeStyle = i % 5 === 0 ? "#ffffff" : i % 2 ? c.acc : c.a2;
        g.globalAlpha = Math.pow(Math.sin(u * Math.PI), 0.8) * 0.3;
        g.lineWidth = 0.6 + u * 1.5;
        g.beginPath();
        g.moveTo(Math.cos(an) * r0, Math.sin(an) * r0 * 0.75);
        g.lineTo(Math.cos(an) * r1, Math.sin(an) * r1 * 0.75);
        g.stroke();
      }
    }
  };
  var drawSignature = (s, sh, c, bt, px, py) => {
    const fn = SIGS2[sh.theme] ?? SEASON_SIGNATURES[sh.theme] ?? SKY_SIGNATURES[sh.theme];
    if (!fn) return;
    const g = s.g;
    g.save();
    g.translate(s.W / 2, s.H / 2);
    g.globalCompositeOperation = "lighter";
    g.lineWidth = 1;
    g.lineCap = "butt";
    g.lineJoin = "miter";
    fn(s, s.t, c, bt, px, py, sh);
    g.restore();
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/blossom/blossomBackdrop.ts
  var DUST = Array.from({ length: 84 }, (_, i) => ({ x: unit("dx" + i), y: unit("dy" + i), z: 0.2 + unit("dz" + i) * 0.8, ph: unit("dp" + i) * 6.28 }));
  var MOTES = Array.from({ length: 18 }, (_, i) => ({
    x: unit("mox" + i),
    y: unit("moy" + i),
    z: 0.25 + unit("moz" + i) * 0.75,
    ph: unit("mop" + i) * 6.28,
    c: i % 3
  }));
  var SOFT = 4;
  var softLayer = null;
  var vignette = null;
  var vignetteKey = "";
  var washKey = "";
  var wash = ["", "", ""];
  var makeSoft = (W, H) => {
    if (typeof document === "undefined") return null;
    const c = document.createElement("canvas"), x = c.getContext("2d");
    if (!x) return null;
    const w = Math.max(2, Math.ceil(W / SOFT)), h = Math.max(2, Math.ceil(H / SOFT));
    c.width = w;
    c.height = h;
    x.setTransform(w / W, 0, 0, h / H, 0, 0);
    return { c, x, w, h };
  };
  var freeSoft = (v) => {
    if (v) {
      v.c.width = 0;
      v.c.height = 0;
    }
  };
  var bloomA = null;
  var bloomB = null;
  var bloomC = null;
  var freeBloom = () => {
    for (const c of [bloomA, bloomB, bloomC]) if (c) {
      c.width = 0;
      c.height = 0;
    }
    bloomA = null;
    bloomB = null;
    bloomC = null;
  };
  var drawBloom = (s, strength = 1) => {
    if (typeof document === "undefined") return;
    const g = s.g, src = g.canvas, w = src.width, h = src.height;
    if (w < 16 || h < 16) return;
    const aw = Math.max(8, Math.round(w / 4)), ah = Math.max(8, Math.round(h / 4)), bw = Math.max(4, Math.round(w / 16)), bh = Math.max(4, Math.round(h / 16));
    if (!bloomA) bloomA = document.createElement("canvas");
    if (!bloomB) bloomB = document.createElement("canvas");
    if (!bloomC) bloomC = document.createElement("canvas");
    for (const [cv, cw, ch] of [[bloomA, aw, ah], [bloomB, bw, bh], [bloomC, aw, ah]]) if (cv.width !== cw || cv.height !== ch) {
      cv.width = cw;
      cv.height = ch;
    }
    const a = bloomA.getContext("2d"), b = bloomB.getContext("2d"), c = bloomC.getContext("2d");
    if (!a || !b || !c) return;
    a.globalCompositeOperation = "copy";
    a.globalAlpha = 1;
    a.filter = "none";
    a.imageSmoothingQuality = "low";
    a.drawImage(src, 0, 0, w, h, 0, 0, aw, ah);
    c.globalCompositeOperation = "copy";
    c.globalAlpha = 1;
    c.filter = "contrast(2.6) brightness(0.85)";
    c.drawImage(bloomA, 0, 0);
    c.filter = "none";
    b.globalCompositeOperation = "copy";
    b.imageSmoothingQuality = "low";
    b.drawImage(bloomC, 0, 0, aw, ah, 0, 0, bw, bh);
    a.globalCompositeOperation = "copy";
    a.globalAlpha = 0.34 / 0.76;
    a.drawImage(bloomC, 0, 0);
    a.globalCompositeOperation = "lighter";
    a.globalAlpha = 0.42 / 0.76;
    a.imageSmoothingQuality = "low";
    a.drawImage(bloomB, 0, 0, bw, bh, 0, 0, aw, ah);
    a.globalAlpha = 1;
    g.save();
    g.setTransform(1, 0, 0, 1, 0, 0);
    g.globalCompositeOperation = "lighter";
    g.imageSmoothingEnabled = true;
    g.imageSmoothingQuality = "low";
    g.globalAlpha = 0.6 * strength;
    g.drawImage(bloomA, 0, 0, aw, ah, 0, 0, w, h);
    g.restore();
  };
  var clearBackdropCaches = () => {
    freeBloom();
    freeSoft(softLayer);
    freeSoft(vignette);
    softLayer = null;
    vignette = null;
    vignetteKey = "";
    washKey = "";
    clearClassicCaches();
    clearLightCaches();
  };
  var LIGHTS = [
    [[0, 0.2, 0.62, 0.7, 0.2, 0.6], [1, 0.78, 0.15, 0.5, 0.1, 0.8]],
    [[0, 0.5, 0, 0.6, 0.16, 0.5], [1, 0.5, 1, 0.6, 0.14, 0.6]],
    [[0, 0.3, 0.62, 0.7, 0.14, 0.4], [1, 0.8, 0.2, 0.5, 0.08, 0.6]],
    [[1, 0.1, 0.55, 0.55, 0.14, 0.5], [0, 0.9, 0.85, 0.5, 0.1, 0.7]],
    [[0, 0.4, 0.5, 0.6, 0.1, 0.5], [1, 0.7, 0.45, 0.55, 0.1, 0.6]],
    [[0, 0.5, 0.55, 0.75, 0.16, 0.4], [1, 0.5, 1, 0.5, 0.06, 0.5]],
    [[2, 0.3, 0.35, 0.6, 0.14, 0.4], [0, 0.75, 0.65, 0.5, 0.11, 0.5]],
    [[0, 0.22, 0.78, 0.6, 0.15, 0.6], [1, 0.8, 0.72, 0.55, 0.13, 0.7], [2, 0.5, 0.2, 0.5, 0.05, 0.5]],
    [[0, 0.85, 0, 0.8, 0.14, 0.4], [1, 0.15, 0.9, 0.5, 0.1, 0.6]],
    [[2, 0.7, 0.6, 0.6, 0.16, 0.5], [0, 0.15, 0.2, 0.5, 0.1, 0.5]],
    [[0, 0.5, 0.5, 0.75, 0.16, 0.4], [1, 0.5, 0.5, 0.4, 0.08, 0.6]],
    [[2, 0.68, 0.2, 0.55, 0.16, 0.3], [0, 0.5, 0.95, 0.6, 0.1, 0.5]],
    [[0, 0.35, 0.15, 0.7, 0.16, 0.5], [1, 0.75, 0.25, 0.55, 0.13, 0.6], [2, 0.5, 0.9, 0.5, 0.05, 0.5]],
    [[0, 0.3, 0.3, 0.7, 0.16, 0.4], [1, 0.8, 0.7, 0.5, 0.1, 0.6]],
    [[2, 0.5, 0.75, 0.75, 0.12, 0.4], [0, 0.2, 0.2, 0.5, 0.08, 0.5]],
    [[0, 0.3, 0.25, 0.6, 0.12, 0.6], [1, 0.75, 0.3, 0.6, 0.12, 0.7], [2, 0.5, 1, 0.6, 0.06, 0.5]],
    [[0, 0.3, 0.45, 0.6, 0.12, 0.4], [1, 0.75, 0.55, 0.55, 0.1, 0.5]],
    [[2, 0.5, 0, 0.7, 0.16, 0.3], [1, 0.5, 1, 0.6, 0.06, 0.4]],
    [[2, 0.5, 0.4, 0.7, 0.12, 0.4], [0, 0.3, 0.85, 0.5, 0.08, 0.5]],
    [[0, 0.5, 0.75, 0.7, 0.14, 0.4], [2, 0.3, 0.2, 0.5, 0.06, 0.5]],
    [[2, 0.7, 0.15, 0.6, 0.14, 0.3], [1, 0.3, 0.7, 0.6, 0.1, 0.5]]
  ];
  var THEME_COUNT = LIGHTS.length;
  var washFor = (c) => {
    const key = c.acc + c.a2;
    if (key !== washKey) {
      washKey = key;
      wash = [hexMix("#06080f", c.acc, 0.1), hexMix("#04060c", c.a2, 0.05), hexMix("#030408", c.acc, 0.06)];
    }
    return wash;
  };
  var alphaHex = (a) => Math.round(clamp01(a) * 255).toString(16).padStart(2, "0");
  var drawSoft = (s, theme, c, t, bt, lin, px, py, lights, chorus) => {
    const { W, H } = s;
    if (!softLayer || Math.abs(softLayer.w * SOFT - W) > SOFT * 1.5 || Math.abs(softLayer.h * SOFT - H) > SOFT * 1.5) {
      freeSoft(softLayer);
      softLayer = makeSoft(W, H);
    }
    const L = softLayer;
    if (!L) return false;
    const x = L.x, wash2 = washFor(c), cols = [c.acc, c.a2, c.a3], amp = (chorus ? 0.8 : 0.7) * (1 + 0.12 * bt);
    x.globalCompositeOperation = "source-over";
    x.globalAlpha = 1;
    const base3 = x.createLinearGradient(0, 0, 0, H);
    base3.addColorStop(0, wash2[0]);
    base3.addColorStop(0.6, wash2[1]);
    base3.addColorStop(1, wash2[2]);
    x.fillStyle = base3;
    x.fillRect(0, 0, W, H);
    if (lights) {
      x.globalCompositeOperation = "lighter";
      if (CLASSIC_THEMES.has(theme)) [[c.acc, 0.18, 0.2, 0.5, 0.9], [c.a2, 0.82, 0.74, 0.46, 0.7], [c.a3, 0.5, 1, 0.5, 0.6]].forEach(([col, bx, by, r, sp], i) => {
        const lx = W * (bx + 0.13 * Math.sin(t * 0.17 * sp + i * 2) + lin * 0.05) - px * W * 0.03 * (i + 1), ly = H * (by + 0.1 * Math.cos(t * 0.13 * sp + i)) - py * H * 0.03 * (i + 1);
        const R = Math.max(W, H) * r * (1 + 0.04 * bt), gr = x.createRadialGradient(lx, ly, 0, lx, ly, R);
        gr.addColorStop(0, col + alphaHex(0.2 * amp));
        gr.addColorStop(0.55, col + alphaHex(0.063 * amp));
        gr.addColorStop(1, col + "00");
        x.fillStyle = gr;
        x.fillRect(0, 0, W, H);
      });
      else LIGHTS[theme % LIGHTS.length].forEach(([ci, bx, by, r, a, sp], i) => {
        const lx = W * (bx + 0.05 * Math.sin(t * 0.11 * sp + i * 2) + lin * 0.03) - px * W * 0.03 * (i + 1), ly = H * (by + 0.05 * Math.cos(t * 0.09 * sp + i)) - py * H * 0.03 * (i + 1);
        const R = Math.max(W, H) * r * (1 + 0.03 * bt), gr = x.createRadialGradient(lx, ly, 0, lx, ly, R), col = cols[ci], A = a * amp;
        gr.addColorStop(0, col + alphaHex(A));
        gr.addColorStop(0.35, col + alphaHex(A * 0.5));
        gr.addColorStop(0.7, col + alphaHex(A * 0.14));
        gr.addColorStop(1, col + "00");
        x.fillStyle = gr;
        x.fillRect(0, 0, W, H);
      });
      x.globalCompositeOperation = "source-over";
    }
    s.g.drawImage(L.c, 0, 0, W, H);
    return true;
  };
  var drawVignette = (s) => {
    const { W, H } = s, g = s.g, key = `${Math.round(W)}x${Math.round(H)}`;
    if (!vignette || vignetteKey !== key) {
      freeSoft(vignette);
      vignette = makeSoft(W, H);
      vignetteKey = key;
      if (vignette) {
        const v = vignette.x.createRadialGradient(W / 2, H * 0.45, H * 0.25, W / 2, H / 2, H * 0.98);
        v.addColorStop(0, "rgba(0,0,0,0)");
        v.addColorStop(0.55, "rgba(0,0,0,.24)");
        v.addColorStop(1, "rgba(0,0,0,.72)");
        vignette.x.fillStyle = v;
        vignette.x.fillRect(0, 0, W, H);
      }
    }
    if (vignette) g.drawImage(vignette.c, 0, 0, W, H);
  };
  var drawBackdrop = (s, sh, P, bt, lin) => {
    const g = s.g, { W, H } = s, t = s.t, c = { acc: P.acc, a2: P.acc2, a3: P.acc3 };
    const px = s.ptr.sx - 0.5, py = s.ptr.sy - 0.5;
    if (!drawSoft(s, sh.theme, c, t, bt, lin, px, py, s.fx.background, sh.def.chorus)) {
      const w = washFor(c), base3 = g.createLinearGradient(0, 0, 0, H);
      base3.addColorStop(0, w[0]);
      base3.addColorStop(0.6, w[1]);
      base3.addColorStop(1, w[2]);
      g.fillStyle = base3;
      g.fillRect(0, 0, W, H);
    }
    if (!s.fx.background) return;
    if (CLASSIC_THEMES.has(sh.theme)) {
      drawClassicLayers(s, sh.theme, t, c, bt, px, py);
      drawClassicSignature(s, sh, c, bt, px, py);
    } else drawSignature(s, sh, c, bt, px, py);
    g.save();
    g.globalCompositeOperation = "lighter";
    g.fillStyle = "#ffffff";
    DUST.forEach((d) => {
      const x = (d.x + Math.sin(t * 0.1 + d.ph) * 0.01) % 1 * W - px * W * 0.04 * d.z, y = ((d.y - t * 0.01 * d.z) % 1 + 1) % 1 * H, tw = 0.5 + 0.5 * Math.sin(t * 0.8 + d.ph * 3);
      g.globalAlpha = (0.08 + 0.3 * tw) * d.z;
      const size = 0.7 + d.z * 1.3;
      g.fillRect(x, y, size, size);
      if (d.z > 0.88) glint(g, x, y, 7 + d.z * 9, "#ffffff", Math.pow(tw, 6) * 0.55);
    });
    const cols = [c.acc, c.a2, c.a3];
    MOTES.forEach((m) => {
      const x = ((m.x + t * 4e-3 * m.z + Math.sin(t * 0.13 + m.ph) * 0.02) % 1 + 1) % 1 * W * 1.2 - W * 0.1 - px * W * 0.06 * m.z;
      const y = ((m.y - t * 0.012 * m.z) % 1 + 1) % 1 * H * 1.3 - H * 0.15 - py * H * 0.05 * m.z;
      const r = H * (0.025 + 0.06 * m.z * m.z), tw = 0.6 + 0.4 * Math.sin(t * 0.5 + m.ph * 2);
      mist(g, x, y, r, r, cols[m.c], (0.05 + 0.09 * m.z) * tw * (1 + 0.15 * bt));
    });
    if (sh.def.chorus) {
      const ph = (t * 0.04 + sh.si * 0.37) % 1;
      beam(g, W * (-0.3 + 1.6 * ph), -H * 0.15, 1.25, H * 1.5, W * 0.38, c.acc, 0.05 * Math.sin(ph * Math.PI));
    }
    g.restore();
    drawVignette(s);
  };
  var drawSpot = (s, sh, c, bt, hit = 0) => {
    const p = singPoint(sh, s.t);
    if (!p) return;
    const g = s.g, R = s.H * 0.3, pulse = 1 + 0.25 * bt + 0.6 * hit;
    g.save();
    g.globalCompositeOperation = "lighter";
    glow(g, p.x, p.y, R * 1.9, R, c.acc, 0.12 * p.k * pulse, false);
    glow(g, p.x, p.y, R * 0.7, R * 0.4, c.a2, 0.07 * p.k * pulse);
    streak(g, p.x, p.y, R * 3.4, R * 0.05, c.acc, 0.09 * p.k * pulse);
    if (sh.def.chorus) for (let i = 0; i < 6; i++) beam(g, p.x, p.y, -Math.PI / 2 + (i - 2.5) * 0.42 + Math.sin(s.t * 0.2 + i) * 0.04, R * 3, R * 0.9, i % 2 ? c.a2 : c.acc, 0.05 * p.k);
    g.restore();
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/blossom/blossomDecor.ts
  var windowRect = (sh, b, t) => {
    const A = b.aperture, wv = sh.win;
    if (!A || !wv) return { x: b.x, y: b.y, w: 0, h: 0 };
    return { x: b.x + Math.sin(t * 0.85 + A.ph) * b.w * 0.34, y: b.y, w: wv.w, h: wv.h };
  };
  var drawBlock = (s, b, P, acc) => {
    const g = s.g, bl = b.block;
    if (!bl) return;
    const a = expoOut(clamp01((s.t - (b.glyphs[0].s - 0.5)) / 0.7));
    const x0 = s.W / 2 + bl.x - bl.w / 2, y0 = s.H / 2 - bl.h / 2 * a, h = bl.h * a;
    const face = g.createLinearGradient(x0, y0, x0 + bl.w * 0.6, y0 + h);
    face.addColorStop(0, hexLerp(acc, "#ffffff", 0.16));
    face.addColorStop(0.55, acc);
    face.addColorStop(1, hexLerp(acc, "#000000", 0.18));
    g.save();
    g.fillStyle = face;
    g.globalAlpha = 0.97 * s.fade;
    g.fillRect(x0, y0, bl.w, h);
    g.globalAlpha = 0.35 * s.fade;
    g.fillStyle = "#ffffff";
    g.fillRect(x0, y0, bl.w, 1);
    g.globalAlpha = 0.45 * s.fade;
    g.strokeStyle = P.bg;
    g.lineWidth = 1;
    g.strokeRect(s.W / 2 + bl.x - bl.w / 2 + 10, s.H / 2 - bl.h / 2 * a + 10, bl.w - 20, Math.max(0, bl.h * a - 20));
    g.restore();
  };
  var drawWindow = (s, sh, b, P, acc) => {
    const g = s.g, s0 = b.glyphs[0].s - 0.2;
    if (s.t < s0) return;
    const a = easeIO(clamp01((s.t - s0) / 0.6)), w = windowRect(sh, b, s.t);
    g.save();
    g.fillStyle = acc;
    g.globalAlpha = 0.95 * a * s.fade;
    g.fillRect(s.W / 2 + w.x - w.w / 2, s.H / 2 + w.y - w.h / 2 * a, w.w, w.h * a);
    g.strokeStyle = P.sub;
    g.globalAlpha = 0.7 * a * s.fade;
    g.lineWidth = 1;
    g.setLineDash([6, 5]);
    g.strokeRect(s.W / 2 + w.x - w.w / 2 - 8, s.H / 2 + w.y - w.h / 2 - 8, w.w + 16, w.h + 16);
    g.restore();
  };
  var drawOrbitRings = (s, P) => {
    const g = s.g, t = s.t;
    g.save();
    g.translate(s.W / 2, s.H / 2);
    g.strokeStyle = P.sub;
    g.lineWidth = 1;
    g.setLineDash([]);
    g.globalAlpha = 0.5;
    g.beginPath();
    for (let i = 0; i < 72; i++) {
      const a = i * 0.0873 + t * 0.05, r0 = s.H * 0.465, r1 = r0 + (i % 6 === 0 ? 12 : 5);
      g.moveTo(Math.cos(a) * r0, Math.sin(a) * r0);
      g.lineTo(Math.cos(a) * r1, Math.sin(a) * r1);
    }
    g.stroke();
    g.restore();
  };
  var DECO_SPRITES = 8;
  var decoSprites = /* @__PURE__ */ new Map();
  var releaseDecoSprites = () => {
    decoSprites.forEach((sp) => {
      sp.c.width = 0;
      sp.c.height = 0;
    });
    decoSprites.clear();
  };
  var decoSprite = (s, d, acc, blur) => {
    const font = `900 ${d.px}px ${s.fonts.sans}`, hit = decoSprites.get(d);
    if (hit && hit.acc === acc && hit.font === font) {
      decoSprites.delete(d);
      decoSprites.set(d, hit);
      return hit;
    }
    if (typeof document === "undefined") return null;
    const c = hit?.c ?? document.createElement("canvas"), x = c.getContext("2d");
    if (!x) return null;
    x.font = font;
    const lw = Math.max(1, Math.min(8, d.px * 6e-3)) + 0.6, pad = Math.ceil(blur * 3 + lw + 2);
    const cw = x.measureText(d.text).width + pad * 2, ch = d.px * 1.4 + pad * 2;
    const r = Math.min(1, Math.max(0.25, 3 / blur), 2048 / Math.max(cw, ch));
    c.width = Math.max(2, Math.ceil(cw * r));
    c.height = Math.max(2, Math.ceil(ch * r));
    x.font = font;
    x.textAlign = "center";
    x.textBaseline = "middle";
    x.strokeStyle = acc;
    x.lineWidth = lw;
    x.lineJoin = "round";
    x.filter = `blur(${(blur * r).toFixed(1)}px)`;
    x.translate(c.width / 2, c.height / 2);
    x.scale(r, r);
    x.strokeText(d.text, 0, 0);
    const sp = { c, acc, r, pad, font };
    decoSprites.delete(d);
    if (decoSprites.size >= DECO_SPRITES) {
      const oldest = decoSprites.keys().next().value;
      if (oldest) {
        const o = decoSprites.get(oldest);
        if (o) {
          o.c.width = 0;
          o.c.height = 0;
        }
        decoSprites.delete(oldest);
      }
    }
    decoSprites.set(d, sp);
    return sp;
  };
  var bakeDeco = (s, sh, acc) => {
    sh.deco.forEach((d) => {
      const blur = Math.abs(d.depth) * d.px * 0.04;
      if (blur > 1.2) decoSprite(s, d, acc, blur);
    });
  };
  var drawDeco = (s, d, acc, cx, cy, panX) => {
    const g = s.g, a = easeIO(clamp01((s.t - d.start) / 0.9));
    if (a <= 2e-3) return;
    g.save();
    g.translate(s.W / 2 + d.x + (cx * s.W + panX) * d.depth * 0.9, s.H / 2 + d.y + cy * s.H * d.depth * 0.9);
    g.rotate(d.rot);
    const sc = 1 + d.depth * 0.45 * 0.5;
    g.scale(sc, sc);
    const blur = Math.abs(d.depth) * d.px * 0.04;
    if (blur > 1.2) {
      const sp = decoSprite(s, d, acc, blur);
      if (sp) {
        g.globalAlpha = a * 0.34 * s.fade;
        g.imageSmoothingEnabled = true;
        g.drawImage(sp.c, -sp.c.width / sp.r / 2, -sp.c.height / sp.r / 2, sp.c.width / sp.r, sp.c.height / sp.r);
        g.restore();
        return;
      }
      g.filter = `blur(${blur.toFixed(1)}px)`;
    }
    g.font = `900 ${d.px}px ${s.fonts.sans}`;
    g.textAlign = "center";
    g.textBaseline = "middle";
    g.strokeStyle = acc;
    g.globalAlpha = a * 0.34 * s.fade;
    g.lineWidth = Math.max(1, Math.min(8, d.px * 6e-3)) + 0.6;
    g.lineJoin = "round";
    g.strokeText(d.text, 0, 0);
    g.restore();
  };
  var drawGuide = (s, b, P, acc) => {
    if (b.skip || b.role === "support" && b.seg.minor) return;
    const g = s.g, t = s.t, s0 = b.glyphs[0].s, dur = b.seg.end - b.seg.start;
    const lead = Math.min(0.38, Math.max(0.2, 0.18 + dur * 0.1)), a0 = s0 - lead, a1 = s0 + 0.65;
    if (t < a0 || t > a1) return;
    const p = clamp01((t - a0) / (a1 - a0)), e = easeIO(p), env = Math.sin(e * Math.PI), hero = b.role !== "support";
    const dir = b.ex !== 0 ? Math.sign(b.ex) : b.x > 0 ? 1 : -1, sx = b.x + dir * b.px * 1.8, sy = b.y - b.px * 0.9, tx = b.x, ty = b.y;
    g.save();
    g.translate(s.W / 2, s.H / 2);
    g.strokeStyle = hero ? acc : P.sub;
    g.fillStyle = g.strokeStyle;
    g.globalAlpha = env * (hero ? 0.6 : 0.4) * s.fade;
    g.lineWidth = hero ? 1.2 : 0.8;
    const cxm = (sx + tx) / 2, cym = Math.min(sy, ty) - b.px * 0.6, N = 28, upto = Math.max(2, Math.floor(N * Math.min(1, p * 2.2)));
    g.beginPath();
    for (let i = 0; i <= upto; i++) {
      const u = i / N, x = (1 - u) * (1 - u) * sx + 2 * (1 - u) * u * cxm + u * u * tx, y = (1 - u) * (1 - u) * sy + 2 * (1 - u) * u * cym + u * u * ty;
      i ? g.lineTo(x, y) : g.moveTo(x, y);
    }
    g.stroke();
    const m = b.px * 0.1;
    g.beginPath();
    g.moveTo(sx - m, sy);
    g.lineTo(sx + m, sy);
    g.moveTo(sx, sy - m);
    g.lineTo(sx, sy + m);
    g.stroke();
    const rr = b.px * (0.3 + 0.9 * e);
    g.globalAlpha *= 0.4;
    g.strokeRect(tx - rr, ty - rr, rr * 2, rr * 2);
    g.restore();
  };
  var drawFrame = (s, sh, b, P, acc) => {
    if (!b.frame || b.arc || b.skip) return;
    const g = s.g, s0 = b.glyphs[0].s - 0.1;
    if (s.t < s0) return;
    const p = easeIO(clamp01((s.t - s0) / 0.5)), pd = b.px * 0.28, ww = b.w + pd * 2, hh = b.h + pd * 2, L = Math.min(ww, hh) * 0.28 * p + 4;
    g.save();
    g.translate(s.W / 2 + b.x, s.H / 2 + b.y);
    g.strokeStyle = b.role === "hero" ? acc : P.sub;
    g.globalAlpha = (b.role === "hero" ? 0.7 : 0.4) * p * s.fade;
    g.lineWidth = 1;
    const x0 = -ww / 2, x1 = ww / 2, y0 = -hh / 2, y1 = hh / 2;
    if (sh.kind === "tableau") g.strokeRect(x0, y0, ww, hh * p);
    else {
      g.beginPath();
      [[x0, y0, 1, 1], [x1, y0, -1, 1], [x0, y1, 1, -1], [x1, y1, -1, -1]].forEach(([x, y, sx, sy]) => {
        g.moveTo(x + sx * L, y);
        g.lineTo(x, y);
        g.lineTo(x, y + sy * L);
      });
      g.stroke();
    }
    if (b.rule) {
      g.globalAlpha = 0.4 * p * s.fade;
      g.beginPath();
      g.moveTo(x0, y1 + 4);
      g.lineTo(x0 + ww * p, y1 + 4);
      g.stroke();
    }
    g.restore();
  };
  var hilbertBackdrop = (s, P, acc) => {
    const g = s.g, n = 16, cw = s.W * 0.8, ch = s.H * 0.7, N = n * n, t = s.t;
    const at = (d) => {
      const [x, y] = hilbertXY(n, d);
      return [(x / (n - 1) - 0.5) * cw, (y / (n - 1) - 0.5) * ch];
    };
    g.save();
    g.strokeStyle = P.sub;
    g.lineWidth = 1;
    g.globalAlpha = 0.16;
    g.beginPath();
    for (let d = 0; d < N; d++) {
      const [px, py] = at(d);
      d ? g.lineTo(px, py) : g.moveTo(px, py);
    }
    g.stroke();
    const head = Math.floor(t * 48 % N), from = Math.max(0, head - 14);
    g.globalAlpha = 0.9;
    g.strokeStyle = acc;
    g.lineWidth = 1.6;
    g.beginPath();
    for (let d = from; d <= head; d++) {
      const [px, py] = at(d);
      d === from ? g.moveTo(px, py) : g.lineTo(px, py);
    }
    g.stroke();
    g.restore();
  };
  var drawMG = (s, sh, lin, acc, P) => {
    const g = s.g, { W, H } = s, k = sh.kind;
    g.save();
    g.strokeStyle = P.sub;
    g.lineWidth = 1;
    g.globalAlpha = 0.28;
    if (k === "editorial") {
      g.beginPath();
      [-0.34, -0.12, 0.1, 0.32].forEach((u) => {
        g.moveTo(u * W, -H * 0.5);
        g.lineTo(u * W, H * 0.5);
      });
      g.stroke();
    }
    if (k === "impact") {
      g.beginPath();
      g.moveTo(-W * 0.6, H * 0.19);
      g.lineTo(W * 0.6, H * 0.19);
      g.moveTo(-W * 0.6, -H * 0.15);
      g.lineTo(W * 0.6, -H * 0.15);
      g.stroke();
      g.fillStyle = acc;
      g.globalAlpha = 0.7;
      g.fillRect(-W * 0.5 + (1 - expoOut(clamp01(lin * 6))) * -W + W * 0.02, -H * 0.5, Math.max(2, W * 3e-3), H);
    }
    if (k === "ribbon") {
      g.beginPath();
      g.moveTo(-W * 3, 0);
      g.lineTo(W * 3, 0);
      g.stroke();
      g.globalAlpha = 0.4;
      g.beginPath();
      for (let x = -W * 3; x < W * 3; x += 24) {
        g.moveTo(x, 0);
        g.lineTo(x, Math.round(x / 24) % 5 === 0 ? 14 : 6);
      }
      g.stroke();
    }
    if (k === "tableau") {
      g.globalAlpha = 0.35;
      g.fillStyle = P.sub;
      for (let i = 0; i < 9; i++) for (let j = 0; j < 5; j++) g.fillRect(-W * 0.45 + i * W * 0.1125 - 1.4, -H * 0.4 + j * H * 0.2 - 1.4, 2.8, 2.8);
    }
    if (k === "collage") {
      g.beginPath();
      g.moveTo(-W * 0.5, H * 0.5);
      g.lineTo(W * 0.5, -H * 0.5);
      g.stroke();
    }
    if (k === "poster") {
      g.globalAlpha = 0.5;
      g.fillStyle = acc;
      for (let i = 0; i < 12; i++) for (let j = 0; j < 12; j++) {
        const r = 1 + 2.4 * (1 - Math.hypot(i - 11, j) / 16);
        if (r > 0.6) g.fillRect(W * 0.28 + i * 14 - r, -H * 0.46 + j * 14 - r, r * 2, r * 2);
      }
    }
    if (k === "orbit") {
      g.globalAlpha = 0.18;
      g.beginPath();
      g.moveTo(-W, 0);
      g.lineTo(W, 0);
      g.moveTo(0, -H);
      g.lineTo(0, H);
      g.stroke();
    }
    if (k === "slice") {
      g.globalAlpha = 0.5;
      g.beginPath();
      for (let i = 0; i < 14; i++) {
        g.moveTo(-W * 0.6, -H * 0.5 + i * H * 0.075);
        g.lineTo(W * 0.6, -H * 0.5 + i * H * 0.075);
      }
      g.stroke();
    }
    if (k === "aperture") {
      g.globalAlpha = 0.3;
      g.setLineDash([3, 7]);
      for (let i = 0; i < 5; i++) {
        g.beginPath();
        g.rect(-W * (0.2 + i * 0.07), -H * (0.2 + i * 0.06), W * (0.4 + i * 0.14), H * (0.4 + i * 0.12));
        g.stroke();
      }
    }
    if (k === "swarm") hilbertBackdrop(s, P, acc);
    if (k === "shatter") {
      g.globalAlpha = 0.22;
      g.beginPath();
      for (let i = 0; i < 22; i++) {
        const a = i * 0.2856 + 0.3, r0 = H * 0.18, r1 = H * (0.5 + 0.25 * (i * 7 % 5) / 5);
        g.moveTo(Math.cos(a) * r0, Math.sin(a) * r0);
        g.lineTo(Math.cos(a) * r1 * 1.6, Math.sin(a) * r1);
      }
      g.stroke();
    }
    if (k === "wave") {
      g.globalAlpha = 0.18;
      g.beginPath();
      for (let x = -W * 0.6; x <= W * 0.6; x += 24) {
        const y = H * 0.32 + Math.sin(x * 0.01 - s.t * 1.6) * H * 0.012;
        x === -W * 0.6 ? g.moveTo(x, y) : g.lineTo(x, y);
      }
      g.stroke();
    }
    if (k === "stack") {
      const x = -W * 0.36 * sh.mir;
      g.globalAlpha = 0.3;
      g.beginPath();
      g.moveTo(x, -H * 0.42);
      g.lineTo(x, H * 0.42);
      g.stroke();
    }
    if (k === "stair") {
      g.globalAlpha = 0.35;
      g.beginPath();
      for (let i = 0; i < 9; i++) {
        g.moveTo(-W * 0.6 + i * W * 0.15, -H * 0.5);
        g.lineTo(-W * 0.6 + i * W * 0.15 + H * 1, H * 0.5);
      }
      g.stroke();
    }
    g.restore();
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/blossom/blossomFurniture.ts
  var drawFurniture = (s, plan2, sh, lin, P, text) => {
    const g = s.g, { W, H } = s, t = s.t, acc = P.acc, chorus = sh.def.chorus, m = Math.min(W, H) * 0.035;
    g.save();
    g.strokeStyle = P.sub;
    g.fillStyle = P.sub;
    g.lineWidth = 1;
    const L = Math.min(W, H) * 0.022;
    g.globalAlpha = 0.42;
    g.beginPath();
    [[m, m, 1, 1], [W - m, m, -1, 1], [m, H - m, 1, -1], [W - m, H - m, -1, -1]].forEach(([x, y, sx, sy]) => {
      g.moveTo(x + sx * L, y);
      g.lineTo(x, y);
      g.lineTo(x, y + sy * L);
    });
    g.stroke();
    const fs = Math.max(10, H * 0.0135), top = m + fs + 6, inset = m + 12;
    g.font = `500 ${fs}px ${s.fonts.mono}`;
    g.textBaseline = "alphabetic";
    g.textAlign = "left";
    const index = text.getIndex(sh.si, plan2.shots.length, sh.rest ? "INTERLUDE" : sh.def.section, g);
    g.globalAlpha = 0.85;
    g.fillStyle = P.ink;
    g.fillText(index.num, inset, top);
    g.globalAlpha = 0.5;
    g.fillStyle = P.sub;
    g.fillText(index.rest, inset + index.numWidth, top);
    const credit = text.getCredit(plan2, g, W * 0.4);
    if (credit) {
      g.textAlign = "right";
      g.globalAlpha = 0.55;
      g.fillText(credit, W - inset, top);
    }
    if (!sh.rest) {
      g.save();
      g.beginPath();
      g.rect(inset, top + 6, W - 2 * inset, fs + 4);
      g.clip();
      g.font = `400 ${fs * 0.82}px ${s.fonts.mono}`;
      g.globalAlpha = 0.16;
      g.textAlign = "left";
      const marquee = text.getMarquee(plan2, sh, g);
      for (let x = -(t * 30 % marquee.width); x < W; x += marquee.width) g.fillText(marquee.text, x + inset, top + 6 + fs);
      g.restore();
    }
    const bx = inset, bw = W - 2 * inset, by = H - m - 10, px = bx + bw * lin;
    g.globalAlpha = 0.22;
    g.beginPath();
    g.moveTo(bx, by + 0.5);
    g.lineTo(bx + bw, by + 0.5);
    for (let i = 0; i <= 8; i++) {
      const x = Math.round(bx + bw * i / 8) + 0.5;
      g.moveTo(x, by + 0.5);
      g.lineTo(x, by - 4);
    }
    g.stroke();
    const run = g.createLinearGradient(bx, 0, Math.max(bx + 1, px), 0);
    run.addColorStop(0, hexAlpha(acc, 0));
    run.addColorStop(1, hexAlpha(acc, 1));
    g.globalAlpha = 0.9;
    g.fillStyle = run;
    g.fillRect(bx, by - 0.5, px - bx, 1.5);
    g.globalAlpha = 1;
    g.fillStyle = "#ffffff";
    g.fillRect(px - 1, by - 3, 2, 7);
    if (chorus && !sh.rest) {
      const sa = expoOut(clamp01((t - sh.def.t0) / 0.6)), y = top + fs * 2.6;
      g.globalAlpha = 0.85 * sa;
      g.fillStyle = acc;
      g.textAlign = "right";
      g.font = `600 ${fs * 0.9}px ${s.fonts.mono}`;
      g.fillText("C H O R U S", W - inset, y);
      g.fillRect(W - inset - fs * 7.4 - 22 * sa, y - fs * 0.32, 18 * sa, 1.5);
    }
    g.restore();
  };
  var hexAlpha = (hex, a) => {
    const v = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})/i.exec(hex);
    return v ? `rgba(${parseInt(v[1], 16)},${parseInt(v[2], 16)},${parseInt(v[3], 16)},${a})` : `rgba(255,255,255,${a})`;
  };
  var drawCaption = (s, plan2, P, text) => {
    const g = s.g, { W, H } = s, t = s.t, m = Math.min(W, H) * 0.035, by = H - m - 16;
    g.save();
    g.font = `500 ${Math.max(12, H * 0.022)}px ${s.fonts.sans}`;
    const caption = text.getCaption(plan2, t, g);
    if (caption) {
      const { line, width: tw } = caption;
      const ca = clamp01((t - line.start) / 0.35) * 0.95;
      g.globalAlpha = ca;
      g.textAlign = "center";
      g.textBaseline = "alphabetic";
      g.fillStyle = P.ink;
      g.fillText(line.translation, W / 2, by - 12 - H * 0.058);
      g.fillStyle = P.acc;
      g.fillRect(W / 2 - tw / 2, by - 12 - H * 0.05, tw * clamp01((t - line.start) / Math.max(0.2, line.end - line.start) * 1.15), 1);
    }
    g.restore();
  };
  var grain = null;
  var grainPatterns = /* @__PURE__ */ new WeakMap();
  var releaseGrain = () => {
    if (grain) {
      grain.width = 0;
      grain.height = 0;
      grain = null;
    }
    grainPatterns = /* @__PURE__ */ new WeakMap();
  };
  var drawGrain = (s, P) => {
    if (!grain) {
      grain = document.createElement("canvas");
      grain.width = grain.height = 160;
      const gg = grain.getContext("2d");
      if (!gg) return;
      const d = gg.createImageData(160, 160), r = rng("grain");
      for (let i = 0; i < d.data.length; i += 4) {
        const v = r() * 255;
        d.data[i] = d.data[i + 1] = d.data[i + 2] = v;
        d.data[i + 3] = 255;
      }
      gg.putImageData(d, 0, 0);
    }
    const pat = grainPatterns.get(s.g) ?? s.g.createPattern(grain, "repeat");
    if (!pat) return;
    grainPatterns.set(s.g, pat);
    const g = s.g, cv = g.canvas;
    g.save();
    g.setTransform(1, 0, 0, 1, 0, 0);
    g.globalAlpha = P.grain;
    g.globalCompositeOperation = "overlay";
    g.fillStyle = pat;
    g.fillRect(0, 0, cv.width, cv.height);
    g.restore();
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/blossom/blossomGlyphs.ts
  var heatCache = /* @__PURE__ */ new Map();
  var warm = (ink, acc, k) => {
    const q = Math.round(clamp01(k) * 32);
    if (q === 0) return ink;
    const key = `${ink}|${acc}|${q}`;
    let v = heatCache.get(key);
    if (!v) {
      if (heatCache.size > 480) heatCache.clear();
      v = hexLerp(ink, acc, q / 32);
      heatCache.set(key, v);
    }
    return v;
  };
  var clearGlyphCaches = () => {
    heatCache.clear();
    toneCache.clear();
  };
  var toneCache = /* @__PURE__ */ new Map();
  var shadowTone = (lift, k) => {
    const q = Math.round(clamp01(lift) * 12), key = q * 2 + (k < 1 ? 1 : 0);
    let v = toneCache.get(key);
    if (!v) {
      const u = q / 12, c = Math.round(255 * Math.pow(u, 0.6)), a = 0.5 * (1 - u) * (1 - u) + GLOW_HALO * u * u;
      v = `rgba(${c},${c},${c},${(Math.max(0.12, a) * k).toFixed(3)})`;
      toneCache.set(key, v);
    }
    return v;
  };
  var sparkNoise = /* @__PURE__ */ new WeakMap();
  var impactNoise = /* @__PURE__ */ new WeakMap();
  var sparkRandom = (s, b, gl, sh, count) => {
    const cached = sparkNoise.get(gl);
    if (cached && cached.seed === s.seed && cached.shot === sh.si && cached.word === b.seg.idx && cached.index === gl.i && cached.raw.length === count * 6) return cached.raw;
    const r = rng(`sp${sh.si}:${b.seg.idx}:${gl.i}:${s.seed}`), raw = new Float64Array(count * 6);
    for (let i = 0; i < raw.length; i++) raw[i] = r();
    sparkNoise.set(gl, { seed: s.seed, shot: sh.si, word: b.seg.idx, index: gl.i, raw });
    return raw;
  };
  var impactRandom = (s, gl, sh, index) => {
    const cached = impactNoise.get(gl);
    if (cached && cached.seed === s.seed && cached.shot === sh.si && cached.index === index) return cached.raw;
    const r = rng(`im${sh.si}:${index}:${s.seed}`), raw = new Float64Array(36);
    for (let i = 0; i < raw.length; i++) raw[i] = r();
    impactNoise.set(gl, { seed: s.seed, shot: sh.si, index, raw });
    return raw;
  };
  var coolTime = (b) => b.role === "hero" ? 1.7 : b.role === "semi" ? 1.2 : 0.9;
  var LEAD = 1.6;
  var GHOST = 0.2;
  var HANDOVER = 0.2;
  var FOCUS_S = 0.34;
  var OFFSTAGE = 8192;
  var blurredText = (g, ch, col, blur) => {
    const m = g.getTransform(), det = m.a * m.d - m.b * m.c;
    if (Math.abs(det) < 1e-9 || blur < 0.3) {
      g.fillStyle = col;
      g.fillText(ch, 0, 0);
      return;
    }
    const scale = Math.sqrt(Math.abs(det));
    g.shadowColor = col;
    g.shadowBlur = blur * scale;
    g.shadowOffsetX = OFFSTAGE;
    g.shadowOffsetY = 0;
    g.fillStyle = col;
    g.fillText(ch, -OFFSTAGE * m.d / det, OFFSTAGE * m.b / det);
  };
  var GLOW_HALO = 0.3;
  var FEATHER = 0.45;
  var sweepSpan = (b, gl) => Math.max(b.advs[gl.i] ?? b.px, b.px * 0.4) * 1.3;
  var drawGlyph = (s, b, gl, sh, P, acc, bt) => {
    const t = s.t, g = s.g;
    if (b.skip || b.shatter) return;
    const lit = s.fx.highlight && !b.aperture && !b.slice, early = t < gl.s;
    if (early && (!lit || gl.s - t > LEAD || /^\s*$/.test(gl.ch))) return;
    const ps = pose(s, b, gl, early ? gl.s : t, sh), hero = b.role !== "support", inBlock = !!b.block, base3 = inBlock ? P.bg : P.ink;
    const wp = lit ? sung(gl, t) : 1;
    const heat = lit && !inBlock ? (t <= gl.e ? 1 : 1 - easeIO(clamp01((t - gl.e) / coolTime(b)))) * 0.9 : 0;
    const ink = heat > 0.01 ? warm(base3, acc, heat) : base3;
    const X = s.W / 2 + ps.x + gl.ox, Y = s.H / 2 + ps.y + gl.oy, ch = gl.ch, alpha = ps.alpha;
    const span = sweepSpan(b, gl), feather = span * FEATHER, edge = -span / 2 + (span + feather) * wp;
    const sweep = () => {
      const w = Math.max(0, Math.min(span, edge - feather * 0.5 + span / 2));
      g.beginPath();
      if (b.vert) g.rect(-b.px, -span / 2, b.px * 2, w);
      else g.rect(-span / 2, -b.px * 1.1, w, b.px * 2.2);
      g.clip();
    };
    const wipe = (col) => {
      if (wp >= 1) return col;
      const grad = b.vert ? g.createLinearGradient(0, edge - feather, 0, edge) : g.createLinearGradient(edge - feather, 0, edge, 0);
      grad.addColorStop(0, col);
      grad.addColorStop(1, col + "00");
      return grad;
    };
    const paint = (col, dx, dy, a, comp, stroke, clipFn, soft) => {
      g.save();
      if (clipFn) clipFn();
      g.translate(X, Y);
      g.rotate(ps.rot);
      g.scale(ps.sc, ps.sc);
      if (wp < 1 && !soft) sweep();
      g.translate(dx, dy);
      g.globalAlpha = a;
      g.globalCompositeOperation = comp;
      g.font = b.font;
      g.textAlign = "center";
      g.textBaseline = "middle";
      if (stroke) {
        g.strokeStyle = col;
        g.lineWidth = Math.max(1, Math.min(8, b.px * 6e-3)) + 0.4;
        g.strokeText(ch, 0, 0);
      } else {
        g.fillStyle = soft ? wipe(col) : col;
        g.fillText(ch, 0, 0);
      }
      g.restore();
    };
    const age = t - gl.s, handover = early ? 1 : 1 - smoothstep(age / HANDOVER);
    if (lit && !inBlock && handover > 0) {
      const rest = pose(s, b, gl, gl.settle, sh);
      g.save();
      g.translate(s.W / 2 + rest.x + gl.ox, s.H / 2 + rest.y + gl.oy);
      g.rotate(rest.rot);
      g.scale(rest.sc, rest.sc);
      g.globalAlpha = GHOST * (early ? 1 - (gl.s - t) / LEAD : 1) * handover * (1 - exitProgress(sh, b, gl, t));
      g.fillStyle = base3;
      g.font = b.font;
      g.textAlign = "center";
      g.textBaseline = "middle";
      g.fillText(ch, 0, 0);
      g.restore();
    }
    if (early) return;
    if (lit && !inBlock && wp < 1) {
      g.save();
      g.translate(X, Y);
      g.rotate(ps.rot);
      g.scale(ps.sc, ps.sc);
      g.globalAlpha = GHOST * 1.3 * alpha * (1 - handover);
      g.fillStyle = base3;
      g.font = b.font;
      g.textAlign = "center";
      g.textBaseline = "middle";
      g.fillText(ch, 0, 0);
      g.restore();
    }
    if (s.fx.trail && ps.p < 0.85) {
      for (let j = 1; j <= 2; j++) {
        const tt = t - j * 0.024;
        if (tt < gl.s) break;
        const p2 = pose(s, b, gl, tt, sh);
        g.save();
        g.translate(s.W / 2 + p2.x + gl.ox, s.H / 2 + p2.y + gl.oy);
        g.rotate(p2.rot);
        g.scale(p2.sc, p2.sc);
        g.globalAlpha = p2.alpha * 0.22 / j * (1 - ps.p / 0.85);
        g.font = b.font;
        g.textAlign = "center";
        g.textBaseline = "middle";
        g.fillStyle = hero ? acc : P.sub;
        g.fillText(ch, 0, 0);
        g.restore();
      }
    }
    if (b.aperture) {
      paint(P.ink, 0, 0, alpha * 0.16, "source-over");
      paint(P.ink, 0, 0, alpha * 0.85, "source-over", true);
      const w = windowRect(sh, b, t), ww = Math.min(1, clamp01((t - (b.glyphs[0].s - 0.2)) / 0.6));
      paint(P.bg, 0, 0, alpha, "source-over", false, () => {
        g.beginPath();
        g.rect(s.W / 2 + w.x - w.w / 2, s.H / 2 + w.y - w.h / 2 * ww, w.w, w.h * ww);
        g.clip();
      });
      return;
    }
    if (b.slice) {
      const N = b.slice, bh = b.px * 1.3;
      for (let k = 0; k < N; k++) {
        const off = (k % 2 ? 1 : -1) * (k + 1) * b.px * 0.2 * Math.pow(1 - ps.p, 2) * s.intensity;
        paint(acc, off, 0, alpha, "source-over", false, () => {
          g.save();
          g.translate(X, Y);
          g.rotate(ps.rot);
          g.beginPath();
          g.rect(-b.w, -bh / 2 + bh * k / N, b.w * 2, bh / N);
          g.restore();
          g.clip();
        });
      }
      return;
    }
    const focus = smoothstep(age / FOCUS_S);
    if (focus < 1 && !inBlock && alpha > 0.01) {
      g.save();
      g.translate(X, Y);
      g.rotate(ps.rot);
      g.scale(ps.sc * (1 + 0.08 * (1 - focus)), ps.sc * (1 + 0.08 * (1 - focus)));
      g.globalAlpha = Math.min(1, alpha * 1.4) * (1 - focus);
      g.font = b.font;
      g.textAlign = "center";
      g.textBaseline = "middle";
      blurredText(g, ch, ink, b.px * 0.22 * (1 - focus));
      g.restore();
    }
    if (s.fx.glow && !inBlock) {
      g.save();
      g.translate(X, Y);
      g.rotate(ps.rot);
      g.scale(ps.sc, ps.sc);
      const lift = clamp01(heat / 0.9), k = hero ? 1 : 0.7;
      g.shadowColor = shadowTone(lift, k);
      g.shadowBlur = Math.min(GLOW_MAX_BLUR, Math.max(6, b.px * (0.07 + 0.07 * lift)) * (hero ? 1 + 0.25 * bt * s.intensity * lift : 1));
      g.shadowOffsetY = Math.max(1, b.px * 0.035) * (1 - lift);
      g.globalAlpha = alpha;
      g.fillStyle = wipe(ink);
      g.font = b.font;
      g.textAlign = "center";
      g.textBaseline = "middle";
      g.fillText(ch, 0, 0);
      g.restore();
    } else paint(ink, 0, 0, alpha, "source-over", false, void 0, true);
  };
  var star = (g, r) => {
    const k = r * 0.22;
    g.beginPath();
    g.moveTo(0, -r);
    g.lineTo(k, -k);
    g.lineTo(r, 0);
    g.lineTo(k, k);
    g.lineTo(0, r);
    g.lineTo(-k, k);
    g.lineTo(-r, 0);
    g.lineTo(-k, -k);
    g.closePath();
  };
  var drawSing = (s, b, sh, acc) => {
    const t = s.t, g = s.g, I = s.intensity;
    if (b.skip || b.shatter || b.aperture || !b.glyphs.length) return;
    const first = b.glyphs[0], last = b.glyphs[b.glyphs.length - 1];
    if (t < first.s || t > last.e + 1.4) return;
    const hero = b.role === "hero", big = b.role !== "support", cosr = Math.cos(b.arc ? 0 : b.rot), sinr = Math.sin(b.arc ? 0 : b.rot);
    const inBlock = !!b.block;
    for (const gl of b.glyphs) {
      const age = t - gl.s, dur = Math.max(0.12, gl.e - gl.s);
      if (age < 0 || age > dur + 0.9 || /^\s*$/.test(gl.ch)) continue;
      const ps = pose(s, b, gl, t, sh), X = s.W / 2 + ps.x + gl.ox, Y = s.H / 2 + ps.y + gl.oy, live = age <= dur, wp = clamp01(age / dur);
      const span = sweepSpan(b, gl), edge = -span / 2 + span * wp;
      const flare = Math.exp(-age * 8) * 0.6 + (live ? 0.4 : 0.4 * Math.exp(-(age - dur) * 5));
      if (!inBlock && flare > 0.02) {
        const r = b.px * (hero ? 1.15 : big ? 0.95 : 0.75), gr2 = g.createRadialGradient(X, Y, 0, X, Y, r);
        const a02 = Math.min(1, flare) * (hero ? 0.1 : 0.07) * I;
        gr2.addColorStop(0, `rgba(255,255,255,${a02.toFixed(3)})`);
        gr2.addColorStop(0.45, `rgba(255,255,255,${(a02 * 0.3).toFixed(3)})`);
        gr2.addColorStop(1, "rgba(255,255,255,0)");
        g.save();
        g.globalCompositeOperation = "lighter";
        g.fillStyle = gr2;
        g.fillRect(X - r, Y - r, r * 2, r * 2);
        g.restore();
      }
      if (live && !inBlock) {
        const fade = smoothstep(age / 0.08) * (0.55 + 0.45 * smoothstep((dur - age) / 0.12)), band = span * 0.6;
        g.save();
        g.translate(X, Y);
        g.rotate(ps.rot);
        g.scale(ps.sc, ps.sc);
        const sheen = b.vert ? g.createLinearGradient(0, edge - band, 0, edge + band * 0.35) : g.createLinearGradient(edge - band, 0, edge + band * 0.35, 0);
        sheen.addColorStop(0, "rgba(255,255,255,0)");
        sheen.addColorStop(0.62, `rgba(255,255,255,${(0.38 * fade).toFixed(3)})`);
        sheen.addColorStop(0.78, `rgba(255,255,255,${(0.48 * fade).toFixed(3)})`);
        sheen.addColorStop(1, "rgba(255,255,255,0)");
        g.globalCompositeOperation = "lighter";
        g.globalAlpha = Math.min(1, ps.alpha * 1.1);
        g.fillStyle = sheen;
        g.font = b.font;
        g.textAlign = "center";
        g.textBaseline = "middle";
        g.fillText(gl.ch, 0, 0);
        g.restore();
      }
      const pa = age - 0.14;
      if (big && pa > 0 && pa < 0.8) {
        const k = pa / 0.8, grow2 = 1 + 0.5 * (1 - Math.pow(1 - k, 3));
        g.save();
        g.translate(X, Y);
        g.rotate(ps.rot);
        g.scale(ps.sc * grow2, ps.sc * grow2);
        g.globalAlpha = smoothstep(pa / 0.08) * Math.pow(1 - k, 2) * (hero ? 0.3 : 0.2) * Math.min(1, I);
        g.strokeStyle = acc;
        g.lineWidth = Math.max(0.8, b.px * 8e-3) / grow2;
        g.font = b.font;
        g.textAlign = "center";
        g.textBaseline = "middle";
        g.strokeText(gl.ch, 0, 0);
        g.restore();
      }
      const n = hero ? 4 : big ? 3 : 1;
      let raw;
      for (let q = 0; q < n; q++) {
        const born = gl.s + (q + 0.5) / n * dur * 0.9, a = t - born, life = 0.8;
        if (a < 0 || a > life) continue;
        raw ??= sparkRandom(s, b, gl, sh, n);
        const ri = q * 6;
        const jx = raw[ri] - 0.5, jy = raw[ri + 1] - 0.5, vx = (raw[ri + 2] - 0.5) * b.px * 0.8, vy = (0.4 + raw[ri + 3] * 0.9) * b.px, sz = (0.5 + raw[ri + 4] * 0.6) * b.px * 0.032, tw = raw[ri + 5] * 6.28;
        const k = a / life, wpb = clamp01((born - gl.s) / dur), eb = -span / 2 + span * wpb;
        const lx = b.vert ? jx * b.px * 0.8 : eb, ly = b.vert ? eb : jy * b.px * 0.9;
        const ox = lx * ps.sc * cosr - ly * ps.sc * sinr, oy = lx * ps.sc * sinr + ly * ps.sc * cosr;
        const ease = 1 - Math.pow(1 - k, 2), alpha = (1 - k) * (0.55 + 0.45 * Math.sin(a * 30 + tw)) * (inBlock ? 0.6 : 1);
        g.save();
        g.translate(X + ox + vx * ease, Y + oy - vy * ease * 0.8);
        g.rotate(k * 1.6 + tw);
        g.globalAlpha = Math.max(0, alpha) * 0.8;
        g.fillStyle = "#ffffff";
        star(g, Math.max(1.4, sz * (1 - k * 0.6)));
        g.fill();
        g.restore();
      }
    }
    if (b.arc || inBlock) return;
    const ws = first.s, we = Math.max(last.e, ws + 0.2), grow = clamp01((t - ws) / (we - ws)), tail2 = clamp01((t - we) / 0.5);
    if (tail2 >= 1) return;
    const total = b.vert ? b.h : b.w, a0 = -total / 2 + easeIO(tail2) * total, a1 = -total / 2 + grow * total;
    if (a1 - a0 < 1) return;
    const th = Math.max(1.2, b.px * 0.014), off = b.px * (b.vert ? 0.64 : 0.68);
    g.save();
    g.translate(s.W / 2 + b.x, s.H / 2 + b.y);
    g.rotate(b.rot);
    const gr = b.vert ? g.createLinearGradient(0, a0, 0, a1) : g.createLinearGradient(a0, 0, a1, 0);
    gr.addColorStop(0, hexA(acc, 0));
    gr.addColorStop(0.85, hexA(acc, 0.9));
    gr.addColorStop(1, "rgba(255,255,255,0.95)");
    g.globalAlpha = (1 - tail2) * Math.min(1, I + 0.3);
    g.fillStyle = gr;
    if (b.vert) g.fillRect(off, a0, th, a1 - a0);
    else g.fillRect(a0, off, a1 - a0, th);
    g.restore();
  };
  var hexA = (hex, a) => {
    const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})/i.exec(hex);
    return m ? `rgba(${parseInt(m[1], 16)},${parseInt(m[2], 16)},${parseInt(m[3], 16)},${a})` : `rgba(255,255,255,${a})`;
  };
  var drawImpact = (s, b, sh, acc) => {
    const t = s.t, g = s.g;
    if (b.skip || b.shatter) return;
    b.glyphs.forEach((gl, gi) => {
      const age = t - gl.s;
      if (age < 0 || age > 0.9 || /^\s*$/.test(gl.ch)) return;
      const ps = pose(s, b, gl, t, sh), cx = s.W / 2 + ps.x + gl.ox, cy = s.H / 2 + ps.y + gl.oy, I = s.intensity;
      const k = clamp01(age / 0.55);
      g.save();
      g.strokeStyle = acc;
      g.lineWidth = 2 * (1 - k) + 0.5;
      g.globalAlpha = (1 - k) * 0.8;
      const rr = b.px * (0.2 + 1.3 * (1 - Math.pow(1 - k, 3)));
      g.translate(cx, cy);
      g.rotate(0.7854);
      g.strokeRect(-rr / 2, -rr / 2, rr, rr);
      g.rotate(-0.7854);
      g.translate(-cx, -cy);
      const raw = impactRandom(s, gl, sh, gi);
      g.fillStyle = acc;
      for (let q = 0; q < 9; q++) {
        const ri = q * 4;
        const ang = raw[ri] * TAU, spd = (80 + raw[ri + 1] * 260) * I, sz = 3 + raw[ri + 2] * 7, d = spd * (1 - Math.exp(-age * 5)) / 5, a = 1 - age / 0.9, rot = raw[ri + 3] * 6.28 + age * 8;
        g.save();
        g.translate(cx + Math.cos(ang) * d, cy + Math.sin(ang) * d + 30 * age * age);
        g.rotate(rot);
        g.globalAlpha = a * 0.9;
        g.beginPath();
        g.moveTo(-sz, sz * 0.6);
        g.lineTo(sz, 0);
        g.lineTo(-sz * 0.2, -sz);
        g.closePath();
        g.fill();
        g.restore();
      }
      g.restore();
    });
  };
  var drawHover = (s, b, acc) => {
    const g = s.g;
    const w = b.arc ? b.px * 2 : (b.rot ? b.h : b.w) + b.px * 0.6, h = b.arc ? b.px * 1.4 : (b.rot ? b.w : b.h) + b.px * 0.6;
    g.save();
    g.translate(s.W / 2 + b.x, s.H / 2 + b.y);
    g.strokeStyle = acc;
    g.lineWidth = 1.5;
    g.setLineDash([5, 4]);
    g.lineDashOffset = -s.now / 60;
    g.globalAlpha = 0.9;
    g.strokeRect(-w / 2, -h / 2, w, h);
    g.restore();
  };
  var drawBursts = (s) => {
    const g = s.g;
    for (let i = s.bursts.length - 1; i >= 0; i--) {
      const u = s.bursts[i], age = (s.now - u.t0) / 1e3;
      if (age > 1.1) {
        s.bursts.splice(i, 1);
        continue;
      }
      const r = rng(u.seed);
      g.save();
      g.translate(s.W / 2 + u.x, s.H / 2 + u.y);
      g.fillStyle = u.col;
      g.strokeStyle = u.col;
      g.globalAlpha = (1 - age / 1.1) * 0.9;
      g.lineWidth = 1.5;
      g.beginPath();
      g.arc(0, 0, 8 + age * 90, 0, TAU);
      g.stroke();
      g.globalAlpha *= 0.9;
      for (let q = 0; q < 16; q++) {
        const a = r() * TAU, sp = 70 + r() * 220, d = sp * (1 - Math.exp(-age * 3.5)) / 3.5, sz = 2 + r() * 5 * (1 - age / 1.1);
        g.beginPath();
        g.arc(Math.cos(a) * d, Math.sin(a) * d + 40 * age * age, sz, 0, TAU);
        g.fill();
      }
      g.restore();
    }
  };
  var drawHUD = (s, sh, acc, dt) => {
    if (sh.custom) return;
    const g = s.g, t = s.t;
    let tg = null, ta = 99;
    sh.boxes.forEach((b2) => b2.glyphs.forEach((gl2) => {
      const a = t - gl2.s;
      if (a >= 0 && a < ta) {
        ta = a;
        tg = { b: b2, gl: gl2 };
      }
    }));
    if (!tg || ta > 2.2) return;
    const { b, gl } = tg;
    const ps = pose(s, b, gl, t, sh), x = ps.x + gl.ox, y = ps.y + gl.oy;
    if (!s.ret.ready) {
      s.ret.x = x;
      s.ret.y = y;
      s.ret.ready = true;
    }
    const k = 1 - Math.exp(-(dt || 0.016) * 12);
    s.ret.x += (x - s.ret.x) * k;
    s.ret.y += (y - s.ret.y) * k;
    const sz = Math.max(22, b.px * 0.6), env = clamp01(1 - (ta - 1.4) / 0.8);
    g.save();
    g.translate(s.W / 2 + s.ret.x, s.H / 2 + s.ret.y);
    g.strokeStyle = acc;
    g.fillStyle = acc;
    g.lineWidth = 1;
    g.globalAlpha = 0.6 * env * s.fade;
    const c = sz * 0.22;
    g.beginPath();
    [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(([sx, sy]) => {
      g.moveTo(sx * sz, sy * sz - sy * c);
      g.lineTo(sx * sz, sy * sz);
      g.lineTo(sx * sz - sx * c, sy * sz);
    });
    g.stroke();
    g.globalAlpha = 0.22 * env * s.fade;
    g.beginPath();
    g.moveTo(-sz * 2.2, 0);
    g.lineTo(-sz * 1.25, 0);
    g.moveTo(sz * 1.25, 0);
    g.lineTo(sz * 2.2, 0);
    g.moveTo(0, -sz * 1.8);
    g.lineTo(0, -sz * 1.25);
    g.moveTo(0, sz * 1.25);
    g.lineTo(0, sz * 1.8);
    g.stroke();
    g.globalAlpha = 0.55 * env * s.fade;
    g.font = `500 ${Math.max(9, s.H * 0.011)}px ${s.fonts.mono}`;
    g.textAlign = "left";
    g.textBaseline = "alphabetic";
    g.fillRect(sz + 5, -sz - 7, 3, 3);
    g.fillText(hudTag(b.seg.line, b.seg.idx), sz + 12, -sz - 4);
    g.restore();
  };
  var hudTags = /* @__PURE__ */ new Map();
  var hudTag = (line, word) => {
    const key = line * 64 + (word & 63);
    let v = hudTags.get(key);
    if (!v) {
      if (hudTags.size > 256) hudTags.clear();
      v = `L${String(line + 1).padStart(2, "0")}  \xB7  ${String(word + 1).padStart(2, "0")}`;
      hudTags.set(key, v);
    }
    return v;
  };
  var drawPins = (s, sh, acc) => {
    const g = s.g;
    sh.boxes.forEach((b) => {
      if (!b.pinned) return;
      const e = ext(b);
      g.save();
      g.translate(s.W / 2 + b.x - e.w / 2, s.H / 2 + b.y - e.h / 2);
      g.strokeStyle = acc;
      g.fillStyle = acc;
      g.lineWidth = 1.4;
      g.beginPath();
      g.moveTo(0, 0);
      g.lineTo(-9, -12);
      g.stroke();
      g.beginPath();
      g.arc(-9, -13, 3.2, 0, TAU);
      g.fill();
      g.restore();
    });
  };
  var stepGlyphPhysics = (s, sh, dt) => {
    const spring = (gl, fx, fy) => {
      gl.vx += (fx - gl.ox * 40 - gl.vx * 8) * dt;
      gl.vy += (fy - gl.oy * 40 - gl.vy * 8) * dt;
      gl.ox += gl.vx * dt;
      gl.oy += gl.vy * dt;
      if (!fx && !fy && Math.abs(gl.ox) < 5e-3 && Math.abs(gl.vx) < 5e-3) {
        gl.ox = 0;
        gl.vx = 0;
      }
      if (!fx && !fy && Math.abs(gl.oy) < 5e-3 && Math.abs(gl.vy) < 5e-3) {
        gl.oy = 0;
        gl.vy = 0;
      }
    };
    if (!s.fx.hover || !s.ptr.over) {
      sh.boxes.forEach((b) => b.glyphs.forEach((gl) => {
        if (gl.ox || gl.oy || gl.vx || gl.vy) spring(gl, 0, 0);
      }));
      return;
    }
    const L = toLayer(s, s.ptr.hx, s.ptr.hy), R = s.BASE * 3.2, m = s.BASE * 2;
    sh.boxes.forEach((b) => b.glyphs.forEach((gl) => {
      const ps = pose(s, b, gl, s.t, sh), dx = ps.x - L.x, dy = ps.y - L.y, d = Math.hypot(dx, dy) || 1;
      let fx = 0, fy = 0;
      if (d < R && s.t >= gl.s) {
        const f = Math.pow(1 - d / R, 2) * 5200 * s.intensity;
        fx = dx / d * f;
        fy = dy / d * f;
      }
      spring(gl, fx, fy);
      gl.ox = Math.max(-m, Math.min(m, gl.ox));
      gl.oy = Math.max(-m, Math.min(m, gl.oy));
    }));
  };
  var hitBox = (s, sh, L) => {
    let hit = null;
    sh.boxes.forEach((b) => {
      if (b.skip || s.t < b.glyphs[0].s) return;
      const rw = b.arc ? b.px * 1.2 * b.glyphs.length : b.rot ? b.h : b.w, rh = b.arc ? b.px * 1.2 : b.rot ? b.w : b.h;
      if (Math.abs(L.x - b.x) < rw / 2 + 6 && Math.abs(L.y - b.y) < rh / 2 + 6) hit = b;
    });
    return hit;
  };

  // ../../../Users/moekotori/echo/src/shared/lyrics/lyricsDisplayTiming.ts
  var segmenter = new Intl.Segmenter(void 0, { granularity: "grapheme" });
  var singleCodePointGraphemes = /^[\u0020-\u02ff\u2000-\u200b\u2010-\u20cf\u2100-\u2fff\u3000-\u3098\u309b-\u30ff\u3100-\ua4cf\uac00-\ud7a3\uff00-\uff9d\uffa0-\uffef]*$/u;
  var splitLyricGraphemes = (text) => singleCodePointGraphemes.test(text) ? Array.from(text) : Array.from(segmenter.segment(text), ({ segment }) => segment);
  var wordSegmenter = new Intl.Segmenter(void 0, { granularity: "word" });

  // ../../../Users/moekotori/echo/src/shared/lyrics/kanaToRomaji.ts
  var base2 = {
    \u3042: "a",
    \u3044: "i",
    \u3046: "u",
    \u3048: "e",
    \u304A: "o",
    \u304B: "ka",
    \u304D: "ki",
    \u304F: "ku",
    \u3051: "ke",
    \u3053: "ko",
    \u3055: "sa",
    \u3057: "shi",
    \u3059: "su",
    \u305B: "se",
    \u305D: "so",
    \u305F: "ta",
    \u3061: "chi",
    \u3064: "tsu",
    \u3066: "te",
    \u3068: "to",
    \u306A: "na",
    \u306B: "ni",
    \u306C: "nu",
    \u306D: "ne",
    \u306E: "no",
    \u306F: "ha",
    \u3072: "hi",
    \u3075: "fu",
    \u3078: "he",
    \u307B: "ho",
    \u307E: "ma",
    \u307F: "mi",
    \u3080: "mu",
    \u3081: "me",
    \u3082: "mo",
    \u3084: "ya",
    \u3086: "yu",
    \u3088: "yo",
    \u3089: "ra",
    \u308A: "ri",
    \u308B: "ru",
    \u308C: "re",
    \u308D: "ro",
    \u308F: "wa",
    \u3090: "i",
    \u3091: "e",
    \u3092: "o",
    \u3093: "n",
    \u304C: "ga",
    \u304E: "gi",
    \u3050: "gu",
    \u3052: "ge",
    \u3054: "go",
    \u3056: "za",
    \u3058: "ji",
    \u305A: "zu",
    \u305C: "ze",
    \u305E: "zo",
    \u3060: "da",
    \u3062: "ji",
    \u3065: "zu",
    \u3067: "de",
    \u3069: "do",
    \u3070: "ba",
    \u3073: "bi",
    \u3076: "bu",
    \u3079: "be",
    \u307C: "bo",
    \u3071: "pa",
    \u3074: "pi",
    \u3077: "pu",
    \u307A: "pe",
    \u307D: "po",
    \u3094: "vu",
    \u3041: "a",
    \u3043: "i",
    \u3045: "u",
    \u3047: "e",
    \u3049: "o",
    \u3083: "ya",
    \u3085: "yu",
    \u3087: "yo",
    \u308E: "wa"
  };
  var digraphs = {
    \u304D\u3083: "kya",
    \u304D\u3085: "kyu",
    \u304D\u3087: "kyo",
    \u304D\u3047: "kye",
    \u304E\u3083: "gya",
    \u304E\u3085: "gyu",
    \u304E\u3087: "gyo",
    \u304E\u3047: "gye",
    \u3057\u3083: "sha",
    \u3057\u3085: "shu",
    \u3057\u3087: "sho",
    \u3057\u3047: "she",
    \u3058\u3083: "ja",
    \u3058\u3085: "ju",
    \u3058\u3087: "jo",
    \u3058\u3047: "je",
    \u3061\u3083: "cha",
    \u3061\u3085: "chu",
    \u3061\u3087: "cho",
    \u3061\u3047: "che",
    \u3062\u3083: "ja",
    \u3062\u3085: "ju",
    \u3062\u3087: "jo",
    \u306B\u3083: "nya",
    \u306B\u3085: "nyu",
    \u306B\u3087: "nyo",
    \u306B\u3047: "nye",
    \u3072\u3083: "hya",
    \u3072\u3085: "hyu",
    \u3072\u3087: "hyo",
    \u3072\u3047: "hye",
    \u3073\u3083: "bya",
    \u3073\u3085: "byu",
    \u3073\u3087: "byo",
    \u3073\u3047: "bye",
    \u3074\u3083: "pya",
    \u3074\u3085: "pyu",
    \u3074\u3087: "pyo",
    \u3074\u3047: "pye",
    \u307F\u3083: "mya",
    \u307F\u3085: "myu",
    \u307F\u3087: "myo",
    \u307F\u3047: "mye",
    \u308A\u3083: "rya",
    \u308A\u3085: "ryu",
    \u308A\u3087: "ryo",
    \u308A\u3047: "rye",
    \u3066\u3043: "ti",
    \u3067\u3043: "di",
    \u3068\u3045: "tu",
    \u3069\u3045: "du",
    \u3066\u3085: "tyu",
    \u3067\u3085: "dyu",
    \u3075\u3041: "fa",
    \u3075\u3043: "fi",
    \u3075\u3047: "fe",
    \u3075\u3049: "fo",
    \u3075\u3085: "fyu",
    \u3046\u3043: "wi",
    \u3046\u3047: "we",
    \u3046\u3049: "wo",
    \u3094\u3041: "va",
    \u3094\u3043: "vi",
    \u3094\u3047: "ve",
    \u3094\u3049: "vo",
    \u3064\u3041: "tsa",
    \u3064\u3043: "tsi",
    \u3064\u3047: "tse",
    \u3064\u3049: "tso",
    \u3057\u3043: "shi",
    \u3061\u3043: "chi",
    \u3058\u3043: "ji",
    \u3044\u3047: "ye"
  };
  var vowels = /* @__PURE__ */ new Set(["a", "i", "u", "e", "o"]);
  var katakanaStart = 12449;
  var katakanaEnd = 12534;
  var katakanaToHiraganaOffset = 96;
  var toHiragana = (text) => Array.from(text, (character) => {
    const code = character.codePointAt(0) ?? 0;
    return code >= katakanaStart && code <= katakanaEnd ? String.fromCodePoint(code - katakanaToHiraganaOffset) : character;
  }).join("");
  var isSmallKana = (character) => /[ぁぃぅぇぉゃゅょゎ]/u.test(character);
  var kanaToRomaji = (text) => {
    const characters = Array.from(toHiragana(text));
    let output = "";
    let pendingSokuon = false;
    for (let index = 0; index < characters.length; index += 1) {
      const character = characters[index];
      if (character === "\u3063") {
        pendingSokuon = true;
        continue;
      }
      if (character === "\u30FC" || character === "\u301C" || character === "\uFF5E" || character === "~") {
        const last = output[output.length - 1];
        if (last && vowels.has(last)) {
          output += last;
        }
        continue;
      }
      let romaji;
      const next = characters[index + 1];
      if (next && isSmallKana(next) && digraphs[character + next]) {
        romaji = digraphs[character + next];
        index += 1;
      } else {
        romaji = base2[character];
      }
      if (romaji === void 0) {
        pendingSokuon = false;
        continue;
      }
      if (pendingSokuon) {
        const consonant = romaji[0];
        if (consonant && !vowels.has(consonant)) {
          output += consonant === "c" ? "t" : consonant;
        }
        pendingSokuon = false;
      }
      if (romaji === "n") {
        const following = characters[index + 1];
        const followingRomaji = following ? base2[following] : void 0;
        if (followingRomaji && (vowels.has(followingRomaji[0]) || followingRomaji[0] === "y")) {
          output += "n'";
          continue;
        }
      }
      output += romaji;
    }
    return output;
  };

  // ../../../Users/moekotori/echo/src/shared/lyrics/lyricsReadingAlignment.ts
  var smallKana = /[ぁぃぅぇぉゃゅょゎ]/u;
  var romajiVowel = /[aeiou\u2070\u00b9]/u;
  var moraBoundaryBefore = /[aeiou\u2070\u00b9n']/u;
  var wildcardAfterO = "\u2070";
  var wildcardAfterE = "\xB9";
  var expandedVowels = {
    "\u0101": "aa",
    "\u012B": "ii",
    "\u016B": "uu",
    "\u0113": `e${wildcardAfterE}`,
    "\u014D": `o${wildcardAfterO}`
  };
  var normalizeKana = (text) => {
    let output = "";
    for (let index = 0; index < text.length; index += 1) {
      const code = text.charCodeAt(index);
      if (code >= 12353 && code <= 12438) {
        output += text[index];
      } else if (code >= 12449 && code <= 12534) {
        output += String.fromCharCode(code - 96);
      } else if (code === 12540) {
        output += "\u30FC";
      }
    }
    return output;
  };
  var cleanKanaReading = (reading) => normalizeKana(reading);
  var cleanRomajiReading = (reading) => reading.toLowerCase().normalize("NFC").replace(/[’ʼ]/gu, "'").replace(/[^a-zāīūēō']/gu, "").replace(/[āīūēō]/gu, (vowel) => expandedVowels[vowel]);
  var wildcardToVowel = (text) => text.replace(/\u2070/gu, "u").replace(/\u00b9/gu, "i");
  var matchesAt = (reading, index, anchor) => {
    if (index + anchor.length > reading.length) {
      return false;
    }
    for (let offset = 0; offset < anchor.length; offset += 1) {
      const have = reading[index + offset];
      const want = anchor[offset];
      if (have !== want && !(have === wildcardAfterO && (want === "u" || want === "o")) && !(have === wildcardAfterE && (want === "i" || want === "e"))) {
        return false;
      }
    }
    return true;
  };
  var countMoras = (chunk, kind) => kind === "kana" ? Array.from(chunk).filter((character) => !smallKana.test(character)).length : Array.from(chunk).filter((character) => romajiVowel.test(character)).length;
  var particleVariant = (kana2) => kana2.replace(/は/gu, "\u308F").replace(/へ/gu, "\u3048");
  var joinedRomaji = (kana2, joiner, separator) => kana2.split(joiner).map((part) => kanaToRomaji(part)).join(separator);
  var kanaConverters = {
    kana: [
      { convert: (kana2) => kana2, applies: () => true },
      { convert: particleVariant, applies: (kana2) => /[はへ]/u.test(kana2) }
    ],
    romaji: [
      { convert: (kana2) => kanaToRomaji(kana2), applies: () => true },
      { convert: (kana2) => kanaToRomaji(particleVariant(kana2)), applies: (kana2) => /[はへ]/u.test(kana2) },
      { convert: (kana2) => joinedRomaji(kana2, "\u3092", "wo"), applies: (kana2) => kana2.includes("\u3092") },
      { convert: (kana2) => joinedRomaji(kana2, "\u3063", "tsu"), applies: (kana2) => kana2.includes("\u3063") }
    ]
  };
  var anchorCandidates = (items, kind) => {
    if (kind === "kana" && items.some((item) => item.type === "latin")) {
      return null;
    }
    const parts = [];
    for (const item of items) {
      if (item.type === "kana") {
        const kana2 = normalizeKana(item.text);
        const last = parts[parts.length - 1];
        if (last && "kana" in last) {
          last.kana += kana2;
        } else {
          parts.push({ kana: kana2 });
        }
      } else if (item.type === "latin") {
        parts.push({ latin: item.text.toLowerCase().replace(/[^a-z]/gu, "") });
      }
    }
    const candidates = [];
    const allKana = parts.map((part) => "kana" in part ? part.kana : "").join("");
    for (const { convert, applies } of kanaConverters[kind]) {
      if (!applies(allKana)) {
        continue;
      }
      const candidate = parts.map((part) => "kana" in part ? convert(part.kana) : part.latin).join("");
      if (candidate && !candidates.includes(candidate)) {
        candidates.push(candidate);
      }
    }
    return candidates.length > 0 ? candidates : null;
  };
  var findAnchor = (reading, candidates, from, pendingKanji, kind) => {
    let best = null;
    for (const anchor of candidates) {
      const lastStart = Math.min(
        pendingKanji === 0 ? from : reading.length - anchor.length,
        best ? best.start : Number.POSITIVE_INFINITY
      );
      for (let index = from; index <= lastStart; index += 1) {
        if (reading[index] === wildcardAfterO || reading[index] === wildcardAfterE || !matchesAt(reading, index, anchor)) {
          continue;
        }
        const atBoundary = kind === "kana" || index === from || moraBoundaryBefore.test(reading[index - 1] ?? "");
        const syllabicNOk = kind === "kana" || !anchor.endsWith("n") || !/[aeiouy\u2070\u00b9]/u.test(reading[index + anchor.length] ?? "");
        if (atBoundary && syllabicNOk && countMoras(reading.slice(from, index), kind) >= pendingKanji) {
          best = { start: index, end: index + anchor.length };
          break;
        }
      }
    }
    return best;
  };
  var alignKanjiReadings = (items, rawReading, kind) => {
    const reading = kind === "kana" ? cleanKanaReading(rawReading) : cleanRomajiReading(rawReading);
    if (!reading) {
      return null;
    }
    const runs = [];
    let cursor = 0;
    let pendingKanji = 0;
    let group = [];
    const consumeGroup = () => {
      if (group.length === 0) {
        return true;
      }
      const candidates = anchorCandidates(group, kind);
      group = [];
      if (!candidates) {
        return false;
      }
      const match = findAnchor(reading, candidates, cursor, pendingKanji, kind);
      if (!match) {
        return false;
      }
      if (pendingKanji > 0) {
        runs.push({ kanjiCount: pendingKanji, reading: wildcardToVowel(reading.slice(cursor, match.start)) });
        pendingKanji = 0;
      }
      cursor = match.end;
      return true;
    };
    for (const item of items) {
      if (item.type === "kanji") {
        if (!consumeGroup()) {
          return null;
        }
        pendingKanji += 1;
      } else {
        group.push(item);
      }
    }
    if (!consumeGroup()) {
      return null;
    }
    const tail2 = reading.slice(cursor);
    if (pendingKanji > 0) {
      if (countMoras(tail2, kind) < pendingKanji) {
        return null;
      }
      runs.push({ kanjiCount: pendingKanji, reading: wildcardToVowel(tail2) });
    } else if (countMoras(tail2, kind) > 0) {
      return null;
    }
    return runs.length > 0 ? runs : null;
  };

  // ../../../Users/moekotori/echo/src/shared/lyrics/lyricsTimingLanguage.ts
  var kanaPattern = /[\p{Script=Hiragana}\p{Script=Katakana}]/u;
  var kanaReadingPattern = /^[\p{Script=Hiragana}\p{Script=Katakana}\p{M}\p{P}\sー]+$/u;
  var hanOnlyPattern = /^\p{Script=Han}+$/u;
  var pinyinSyllablePattern = /^(?:zh|ch|sh|[bpmfdtnlgkhjqxrzcswy])?(?:a|o|e|ai|ei|ao|ou|an|en|ang|eng|ong|i|ia|ie|iao|iu|ian|in|iang|ing|iong|u|ua|uo|uai|ui|uan|un|uang|ueng|ü|üe|üan|ün|er)$/u;
  var neighbourRadius = 3;
  var hasPinyinReading = (line) => {
    const text = line.text.replace(/[\s\p{P}\p{S}]/gu, "");
    if (!hanOnlyPattern.test(text) || !line.romanization?.trim()) return false;
    const reading = line.romanization.trim().toLowerCase().normalize("NFD");
    const tokens = reading.split(/[\s'’·-]+/u);
    if (tokens.length !== Array.from(text).length) return false;
    const numbered = tokens.every((token) => /^[a-züv]+[1-5]$/u.test(token.normalize("NFC")));
    if (!numbered && !/[\u0300\u0301\u030c]/u.test(reading)) return false;
    return tokens.every((token) => {
      const syllable = token.replace(/u\u0308/gu, "\xFC").replace(/[\u0300\u0301\u0304\u030c]/gu, "").replace(/v/gu, "\xFC");
      return pinyinSyllablePattern.test(numbered ? syllable.slice(0, -1) : syllable);
    });
  };
  var detectLyricsTimingLanguage = (line) => {
    if (kanaPattern.test(line.text)) return "japanese";
    if (line.kana && kanaPattern.test(line.kana) && kanaReadingPattern.test(line.kana)) return "japanese";
    return hasPinyinReading(line) ? "chinese" : null;
  };
  var createLyricsTimingLanguageContext = (lines) => {
    const evidence = lines.map(detectLyricsTimingLanguage);
    const japanese = evidence.includes("japanese");
    const lineJapanese = /* @__PURE__ */ new WeakMap();
    const nearest = (index, direction) => {
      for (let distance = 1; distance <= neighbourRadius; distance += 1) {
        const language = evidence[index + distance * direction];
        if (language) return language;
      }
      return null;
    };
    for (let index = 0; index < lines.length; index += 1) {
      let language = evidence[index];
      if (language === null) {
        const before = nearest(index, -1);
        const after = nearest(index, 1);
        if (before !== null && before === after) language = before;
      }
      lineJapanese.set(lines[index], language === null ? japanese : language === "japanese");
    }
    return { japanese, lineJapanese };
  };

  // ../../../Users/moekotori/echo/src/shared/lyrics/lyricsWordTimingEstimator.ts
  var maxRawWordHighlightSegments = 480;
  var maxRenderableWordHighlightSegments = 160;
  var minRenderableWordHighlightSegments = 1;
  var minLineTimingSpanMs = 220;
  var minVisualWordDurationMs = 72;
  var defaultMsPerBeatJapanese = 200;
  var defaultMsPerBeatLatin = 235;
  var defaultMsPerBeatHan = 250;
  var minMsPerBeat = 80;
  var maxMsPerBeat = 520;
  var breathAllowanceMs = 260;
  var rateQuantile = 0.35;
  var minCalibrationBeats = 3;
  var localTightRatio = 1.3;
  var globalTightCap = 2.5;
  var localPaceRadius = 4;
  var maxLocalPaceWeight = 0.8;
  var localPaceWeightPerSample = 0.2;
  var finalHoldBeats = 0.6;
  var lineLeadInMs = 60;
  var stableWindowFillRatio = 0.9;
  var longGapTailAllowanceMs = 1200;
  var singleSegmentHoldWindowMs = 4e3;
  var tailHoldSurplusThreshold = 1.1;
  var tailHoldSurplusShare = 0.6;
  var minBodyShareOfSungTime = 0.45;
  var weakFunctionWordBeats = 0.7;
  var weakFunctionWords = /* @__PURE__ */ new Set([
    "a",
    "an",
    "the",
    "to",
    "of",
    "in",
    "on",
    "at",
    "and",
    "or",
    "but",
    "for",
    "as",
    "is",
    "it",
    "my",
    "your",
    "our",
    "her",
    "his",
    "be"
  ]);
  var defaultKanjiBeats = 1.7;
  var chineseCharBeats = 1;
  var cjkWhitespacePauseBeats = 0.4;
  var kanaPattern2 = /[\p{Script=Hiragana}\p{Script=Katakana}]/u;
  var hanPattern = /\p{Script=Han}/u;
  var hangulPattern = /\p{Script=Hangul}/u;
  var smallKanaPattern = /[ぁぃぅぇぉゃゅょゎァィゥェォャュョヮヵヶ]/u;
  var sokuonPattern = /[っッ]/u;
  var choonPattern = /[ー〜～~]/u;
  var nasalKanaPattern = /[んン]/u;
  var wordLetterPattern = /[\p{L}\p{M}\p{N}'’]/u;
  var cjkLetterPattern = /[\p{Script=Han}\p{Script=Hiragana}\p{Script=Katakana}\p{Script=Hangul}]/u;
  var punctuationPattern = /[\p{P}\p{S}]/u;
  var hardPausePattern = /[.。!！?？…]/u;
  var softPausePattern = /[,，、;；:：—–-]/u;
  var whitespacePattern = /\s/u;
  var maxSegmentPauseBeats = 1.2;
  var latinWordOnsetBeats = 0.25;
  var normalizeCompact = (value) => value.replace(/\s+/gu, "");
  var countLatinSyllables = (rawWord) => {
    const word = rawWord.toLowerCase().normalize("NFD").replace(/[\u0300-\u036f]/gu, "").replace(/[^a-z]/gu, "");
    if (!word) {
      return 1;
    }
    if (word.length <= 2) {
      return 1;
    }
    const stretchedRuns = word.match(/([aeiou])\1{2,}/gu) ?? [];
    const stretchBeats = stretchedRuns.reduce((total, run) => total + (run.length >= 6 ? 2 : 1), 0);
    let stem = word.replace(/([aeiou])\1{2,}/gu, "$1");
    if (/[^aeiou]e$/u.test(stem) && !/[^aeiou]le$/u.test(stem)) {
      stem = stem.slice(0, -1);
    } else if (/[^aeiou](ed|es)$/u.test(stem) && !/[td]ed$/u.test(stem) && !/([sxz]|ch|sh)es$/u.test(stem)) {
      stem = stem.slice(0, -2);
    }
    const ingAfterVowel = stem.length >= 5 && /[aeiouy]ing$/u.test(stem);
    const body = ingAfterVowel ? stem.slice(0, -3) : stem;
    const groups = body.match(/[aeiouy]+/gu) ?? [];
    const hiatusCount = (body.match(/[^aeiouytcsgx]ia|[^aeiouytcsgx]io(?!n)|[^qg]ual/gu) ?? []).length;
    return Math.max(1, groups.length) + (ingAfterVowel ? 1 : 0) + hiatusCount + stretchBeats;
  };
  var getLatinWordBeats = (word) => {
    const compact = word.replace(/[\s\p{P}]+/gu, "");
    if (!compact) {
      return 0;
    }
    if (/^\p{N}+$/u.test(compact)) {
      return Math.min(6, Math.max(1, Array.from(compact).length * 1.4));
    }
    if (weakFunctionWords.has(compact.toLowerCase())) {
      return weakFunctionWordBeats;
    }
    return countLatinSyllables(compact) + latinWordOnsetBeats;
  };
  var getPauseWeight = (character) => {
    if (hardPausePattern.test(character)) {
      return 0.8;
    }
    if (softPausePattern.test(character)) {
      return 0.5;
    }
    return 0.1;
  };
  var getKanaBeats = (character) => {
    if (sokuonPattern.test(character)) {
      return 0.55;
    }
    if (nasalKanaPattern.test(character)) {
      return 0.85;
    }
    return 1;
  };
  var segmentText = (value, kanjiBeats) => {
    const segments = [];
    let latinWord = "";
    let pendingPrefix = "";
    const pushSegment = (segment) => {
      if (pendingPrefix) {
        segment.text = pendingPrefix + segment.text;
        pendingPrefix = "";
      }
      segments.push(segment);
    };
    const flushLatinWord = () => {
      if (latinWord) {
        pushSegment({ text: latinWord, kind: "latin", beats: getLatinWordBeats(latinWord), pause: 0 });
        latinWord = "";
      }
    };
    const appendToLast = (text, beats = 0, pause = 0) => {
      const last = segments[segments.length - 1];
      if (last) {
        last.text += text;
        last.beats += beats;
        last.pause = Math.min(maxSegmentPauseBeats, last.pause + pause);
      } else {
        pendingPrefix += text;
      }
    };
    for (const character of splitLyricGraphemes(value)) {
      if (character.length === 1) {
        const code = character.charCodeAt(0);
        if (code >= 12353 && code <= 12438 || code >= 12449 && code <= 12538) {
          flushLatinWord();
          if (smallKanaPattern.test(character) && segments.length > 0) {
            appendToLast(character);
          } else {
            pushSegment({ text: character, kind: "kana", beats: getKanaBeats(character), pause: 0 });
          }
          continue;
        }
        if (code >= 19968 && code <= 40869) {
          flushLatinWord();
          pushSegment({ text: character, kind: "han", beats: kanjiBeats, pause: 0 });
          continue;
        }
      }
      if (whitespacePattern.test(character)) {
        if (latinWord) {
          latinWord += character;
          flushLatinWord();
        } else if (segments.length > 0) {
          const last = segments[segments.length - 1];
          const alreadyPaused = whitespacePattern.test(last.text[last.text.length - 1] ?? "");
          appendToLast(character, 0, last.kind !== "latin" && !alreadyPaused ? cjkWhitespacePauseBeats : 0);
        }
        continue;
      }
      if (character !== "\u30FC" && wordLetterPattern.test(character) && !cjkLetterPattern.test(character)) {
        latinWord += character;
        continue;
      }
      flushLatinWord();
      if (kanaPattern2.test(character)) {
        if (smallKanaPattern.test(character) && segments.length > 0) {
          appendToLast(character);
        } else {
          pushSegment({ text: character, kind: "kana", beats: getKanaBeats(character), pause: 0 });
        }
        continue;
      }
      if (choonPattern.test(character)) {
        appendToLast(character, 0.9);
        continue;
      }
      if (hanPattern.test(character)) {
        pushSegment({ text: character, kind: "han", beats: kanjiBeats, pause: 0 });
        continue;
      }
      if (hangulPattern.test(character)) {
        pushSegment({ text: character, kind: "hangul", beats: 1, pause: 0 });
        continue;
      }
      if (punctuationPattern.test(character)) {
        appendToLast(character, 0, getPauseWeight(character));
        continue;
      }
      pushSegment({ text: character, kind: "other", beats: 1, pause: 0 });
    }
    flushLatinWord();
    if (pendingPrefix && segments.length > 0) {
      segments[segments.length - 1].text += pendingPrefix;
    }
    return segments.filter((segment) => segment.text.trim().length > 0);
  };
  var sumBeats = (segments) => segments.reduce((total, segment) => total + segment.beats + segment.pause, 0);
  var countRomajiBeats = (romaji) => {
    let beats = 0;
    for (const token of romaji.toLowerCase().split(/\s+/u)) {
      if (!token) {
        continue;
      }
      const normalized = token.normalize("NFC");
      beats += (normalized.match(/[aeiou]/gu) ?? []).length;
      beats += (normalized.match(/[āīūēō]/gu) ?? []).length * 2;
      beats += (normalized.match(/n(?=[^aeiouyāīūēō]|$)|n'/gu) ?? []).length * 0.85;
      beats += (normalized.match(/([bcdfghjklmpqrstvwz])\1|tch/gu) ?? []).length * 0.55;
    }
    return beats;
  };
  var resolveKanjiBeats = (line, segments) => {
    const kanjiCount = segments.filter((segment) => segment.kind === "han").length;
    if (kanjiCount === 0) {
      return defaultKanjiBeats;
    }
    let readingBeats = null;
    if (line.kana?.trim() && kanaPattern2.test(line.kana)) {
      readingBeats = sumBeats(segmentText(line.kana, defaultKanjiBeats).map((segment) => ({ ...segment, pause: 0 })));
    } else if (line.romanization?.trim()) {
      readingBeats = countRomajiBeats(line.romanization);
    }
    if (readingBeats === null || readingBeats <= 0) {
      return defaultKanjiBeats;
    }
    const knownBeats = segments.filter((segment) => segment.kind !== "han").reduce((total, segment) => total + segment.beats, 0);
    const perKanji = (readingBeats - knownBeats) / kanjiCount;
    return Number.isFinite(perKanji) ? Math.min(4, Math.max(0.8, perKanji)) : defaultKanjiBeats;
  };
  var resolveAlignedKanjiBeats = (line, segments) => {
    const items = [];
    for (const segment of segments) {
      if (segment.kind === "han") {
        items.push({ type: "kanji" });
      } else if (segment.kind === "kana") {
        items.push({ type: "kana", text: segment.text });
      } else if (segment.kind === "latin") {
        items.push({ type: "latin", text: segment.text });
      } else {
        return null;
      }
    }
    let runs = null;
    let measure = countRomajiBeats;
    if (line.kana?.trim() && kanaPattern2.test(line.kana)) {
      runs = alignKanjiReadings(items, line.kana, "kana");
      measure = (reading) => sumBeats(segmentText(reading, defaultKanjiBeats).map((segment) => ({ ...segment, pause: 0 })));
    } else if (line.romanization?.trim()) {
      runs = alignKanjiReadings(items, line.romanization, "romaji");
    }
    if (!runs) {
      return null;
    }
    const beats = [];
    for (const run of runs) {
      const perKanji = measure(run.reading) / run.kanjiCount;
      if (!Number.isFinite(perKanji) || perKanji <= 0) {
        return null;
      }
      const clamped = Math.min(4, Math.max(0.8, perKanji));
      for (let index = 0; index < run.kanjiCount; index += 1) {
        beats.push(clamped);
      }
    }
    return beats;
  };
  var buildLineBeatProfile = (line, japaneseContext) => {
    const language = detectLyricsTimingLanguage(line);
    const japanese = language === null ? japaneseContext : language === "japanese";
    let segments = segmentText(line.text, japanese ? defaultKanjiBeats : chineseCharBeats);
    if (segments.length === 0) {
      return null;
    }
    if (japanese && segments.some((segment) => segment.kind === "han")) {
      const alignedBeats = resolveAlignedKanjiBeats(line, segments);
      if (alignedBeats) {
        let kanjiIndex = 0;
        segments = segments.map((segment) => segment.kind === "han" ? { ...segment, beats: alignedBeats[kanjiIndex++] } : segment);
      } else {
        const kanjiBeats = resolveKanjiBeats(line, segments);
        if (kanjiBeats !== defaultKanjiBeats) {
          segments = segments.map((segment) => segment.kind === "han" ? { ...segment, beats: kanjiBeats } : segment);
        }
      }
    }
    return {
      segments,
      totalBeats: sumBeats(segments) + finalHoldBeats,
      finalHoldBeats,
      japanese
    };
  };
  var createCalibrationState = (base3) => ({
    ...base3,
    profiles: /* @__PURE__ */ new WeakMap(),
    estimates: /* @__PURE__ */ new WeakMap(),
    lineMsPerBeat: /* @__PURE__ */ new WeakMap(),
    lineSungDurationMs: /* @__PURE__ */ new WeakMap(),
    lineJapanese: /* @__PURE__ */ new WeakMap()
  });
  var defaultCalibration = createCalibrationState({ msPerBeat: null, sampleCount: 0, japanese: false });
  var getProfile = (state, line) => {
    if (state.profiles.has(line)) {
      return state.profiles.get(line) ?? null;
    }
    const profile = buildLineBeatProfile(line, state.lineJapanese.get(line) ?? state.japanese);
    state.profiles.set(line, profile);
    return profile;
  };
  var quantile = (sortedValues, fraction) => {
    const position = (sortedValues.length - 1) * fraction;
    const lower = Math.floor(position);
    const upper = Math.min(sortedValues.length - 1, lower + 1);
    const weight = position - lower;
    return sortedValues[lower] * (1 - weight) + sortedValues[upper] * weight;
  };
  var isValidTime = (value) => typeof value === "number" && Number.isFinite(value) && value >= 0;
  var median = (values) => {
    const sorted = [...values].sort((a, b) => a - b);
    return quantile(sorted, 0.5);
  };
  var clampRate = (rate) => Math.min(maxMsPerBeat, Math.max(minMsPerBeat, rate));
  var normalizeRepeatKey = (text) => text.replace(/[\s\p{P}\p{S}]+/gu, "").toLowerCase();
  var collectCalibrationSamples = (state, lines) => {
    const samples = [];
    for (let index = 0; index < lines.length; index += 1) {
      const line = lines[index];
      if (!isValidTime(line.timeMs)) {
        continue;
      }
      const profile = getProfile(state, line);
      if (!profile) {
        continue;
      }
      const next = lines[index + 1];
      const windowMs = next && isValidTime(next.timeMs) && next.timeMs > line.timeMs ? next.timeMs - line.timeMs : null;
      const explicitDurationMs = isValidTime(line.endMs) && line.endMs > line.timeMs + minLineTimingSpanMs ? line.endMs - line.timeMs : null;
      samples.push({ line, profile, windowMs, explicitDurationMs, tightRate: null });
    }
    return samples;
  };
  var createLyricsWordTimingCalibration = (lines) => {
    const { japanese, lineJapanese } = createLyricsTimingLanguageContext(lines);
    const state = createCalibrationState({ msPerBeat: null, sampleCount: 0, japanese });
    state.lineJapanese = lineJapanese;
    const samples = collectCalibrationSamples(state, lines);
    const observedRates = samples.map((sample) => {
      if (sample.profile.totalBeats < minCalibrationBeats) {
        return null;
      }
      if (sample.explicitDurationMs !== null) {
        return sample.explicitDurationMs / sample.profile.totalBeats;
      }
      if (sample.windowMs === null || sample.windowMs <= breathAllowanceMs) {
        return null;
      }
      return Math.max(0, sample.windowMs - breathAllowanceMs) / sample.profile.totalBeats;
    });
    const roughRates = observedRates.filter((rate) => rate !== null).sort((a, b) => a - b);
    if (roughRates.length === 0) {
      return state;
    }
    const roughRate = clampRate(quantile(roughRates, roughRates.length >= 4 ? rateQuantile : 0.5));
    const collectNearby = (index, values) => {
      const nearby = [];
      for (let offset = -localPaceRadius; offset <= localPaceRadius; offset += 1) {
        const value = values[index + offset];
        if (value !== null && value !== void 0) {
          nearby.push(value);
        }
      }
      return nearby;
    };
    for (let index = 0; index < samples.length; index += 1) {
      const sample = samples[index];
      const observed = observedRates[index];
      if (observed === null) {
        continue;
      }
      if (sample.explicitDurationMs !== null) {
        sample.tightRate = observed;
        continue;
      }
      const nearby = collectNearby(index, observedRates);
      const localRough = nearby.length >= 3 ? median(nearby) : roughRate;
      if (observed <= localRough * localTightRatio && observed <= roughRate * globalTightCap) {
        sample.tightRate = observed;
      }
    }
    const tightRates = samples.map((sample) => sample.tightRate).filter((rate) => rate !== null);
    const globalRate = tightRates.length >= 3 ? clampRate(median(tightRates)) : roughRate;
    state.msPerBeat = globalRate;
    state.sampleCount = tightRates.length > 0 ? tightRates.length : roughRates.length;
    const tightRateByIndex = samples.map((sample) => sample.tightRate);
    for (let index = 0; index < samples.length; index += 1) {
      const nearby = collectNearby(index, tightRateByIndex);
      if (nearby.length >= 2) {
        const localRate = clampRate(median(nearby));
        const localWeight = Math.min(maxLocalPaceWeight, nearby.length * localPaceWeightPerSample);
        state.lineMsPerBeat.set(samples[index].line, localRate * localWeight + globalRate * (1 - localWeight));
      }
    }
    const tightDurationsByText = /* @__PURE__ */ new Map();
    for (const sample of samples) {
      if (sample.tightRate === null) {
        continue;
      }
      const key = normalizeRepeatKey(sample.line.text);
      if (!key) {
        continue;
      }
      const sungMs = sample.explicitDurationMs ?? Math.max(0, (sample.windowMs ?? 0) - breathAllowanceMs);
      state.lineSungDurationMs.set(sample.line, sungMs);
      const bucket = tightDurationsByText.get(key) ?? [];
      bucket.push(sungMs);
      tightDurationsByText.set(key, bucket);
    }
    for (const sample of samples) {
      if (sample.tightRate !== null || sample.explicitDurationMs !== null) {
        continue;
      }
      const durations = tightDurationsByText.get(normalizeRepeatKey(sample.line.text));
      if (durations && durations.length > 0) {
        state.lineSungDurationMs.set(sample.line, median(durations));
      }
    }
    return state;
  };
  var asState = (calibration) => calibration && "profiles" in calibration ? calibration : defaultCalibration;
  var resolveMsPerBeat = (state, line, profile) => {
    const lineRate = state.lineMsPerBeat.get(line);
    if (lineRate !== void 0) {
      return lineRate;
    }
    if (state.msPerBeat !== null) {
      return state.msPerBeat;
    }
    if (profile.japanese) {
      return defaultMsPerBeatJapanese;
    }
    return profile.segments.some((segment) => segment.kind === "han") ? defaultMsPerBeatHan : defaultMsPerBeatLatin;
  };
  var coalesceSegments = (segments, maxSegments) => {
    if (segments.length <= maxSegments) {
      return [...segments];
    }
    const totalWeight = sumBeats(segments);
    const groupCount = Math.max(minRenderableWordHighlightSegments, maxSegments);
    const targetWeight = totalWeight / groupCount;
    const grouped = [];
    let current = null;
    for (let index = 0; index < segments.length; index += 1) {
      const segment = segments[index];
      const merged = current ? {
        kind: current.kind,
        text: current.text + segment.text,
        beats: current.beats + segment.beats,
        pause: current.pause + segment.pause
      } : { ...segment };
      current = merged;
      const remainingSegments = segments.length - index - 1;
      const remainingGroups = groupCount - grouped.length - 1;
      if (grouped.length < groupCount - 1 && remainingSegments >= remainingGroups && merged.beats + merged.pause >= targetWeight) {
        grouped.push(merged);
        current = null;
      }
    }
    if (current) {
      grouped.push(current);
    }
    return grouped;
  };
  var resolveSungEndMs = (line, lineEndMs, predictedDurationMs, learnedDurationMs, segmentCount) => {
    if (isValidTime(line.endMs) && line.endMs > line.timeMs + minLineTimingSpanMs && line.endMs <= lineEndMs) {
      return line.endMs;
    }
    const windowMs = lineEndMs - line.timeMs;
    if (learnedDurationMs !== void 0) {
      const breathMs = Math.min(breathAllowanceMs, windowMs * (1 - stableWindowFillRatio));
      return line.timeMs + Math.min(windowMs - breathMs, learnedDurationMs);
    }
    const durationCapMs = Math.max(
      predictedDurationMs + longGapTailAllowanceMs,
      segmentCount === 1 ? singleSegmentHoldWindowMs : 0
    );
    const cappedWindowMs = Math.min(windowMs, durationCapMs);
    return Math.min(lineEndMs - 40, line.timeMs + cappedWindowMs * stableWindowFillRatio);
  };
  var estimateLyricWordTimings = (line, lineEndMs, calibration) => {
    if (!isValidTime(line.timeMs) || typeof lineEndMs !== "number" || !Number.isFinite(lineEndMs) || lineEndMs <= line.timeMs + minLineTimingSpanMs) {
      return null;
    }
    const state = asState(calibration);
    const cacheKey = Math.round(lineEndMs);
    let lineCache = state.estimates.get(line);
    if (!lineCache) {
      lineCache = /* @__PURE__ */ new Map();
      state.estimates.set(line, lineCache);
    }
    if (lineCache.has(cacheKey)) {
      return lineCache.get(cacheKey) ?? null;
    }
    if (lineCache.size >= 4) lineCache.clear();
    const profile = getProfile(state, line);
    if (!profile || profile.segments.length < minRenderableWordHighlightSegments || profile.segments.length > maxRawWordHighlightSegments) {
      lineCache.set(cacheKey, null);
      return null;
    }
    const msPerBeat = resolveMsPerBeat(state, line, profile);
    const predictedDurationMs = profile.totalBeats * msPerBeat;
    const sungEndMs = resolveSungEndMs(
      line,
      cacheKey,
      predictedDurationMs,
      state.lineSungDurationMs.get(line),
      profile.segments.length
    );
    const windowMs = cacheKey - line.timeMs;
    const startMs = line.timeMs + Math.min(lineLeadInMs, windowMs * 0.04);
    const availableMs = sungEndMs - startMs;
    if (availableMs < minLineTimingSpanMs) {
      lineCache.set(cacheKey, null);
      return null;
    }
    const maxSegmentsForPace = Math.max(
      minRenderableWordHighlightSegments,
      Math.min(maxRenderableWordHighlightSegments, Math.floor(availableMs / minVisualWordDurationMs))
    );
    const segments = coalesceSegments(profile.segments, maxSegmentsForPace);
    const weights = segments.map((segment, index) => segment.beats + (index === segments.length - 1 ? profile.finalHoldBeats : segment.pause));
    const totalWeight = weights.reduce((total, weight) => total + weight, 0);
    if (totalWeight <= 0) {
      lineCache.set(cacheKey, null);
      return null;
    }
    const surplusMs = state.lineSungDurationMs.has(line) ? 0 : Math.max(0, availableMs - predictedDurationMs * tailHoldSurplusThreshold);
    const tailExtraMs = Math.min(surplusMs * tailHoldSurplusShare, availableMs * (1 - minBodyShareOfSungTime));
    const bodyMs = availableMs - tailExtraMs;
    let elapsedWeight = 0;
    const words = segments.map((segment, index) => {
      const wordStartMs = Math.round(startMs + elapsedWeight / totalWeight * bodyMs);
      const sungWeight = segment.beats + (index === segments.length - 1 ? profile.finalHoldBeats : 0);
      const wordEndMs = index === segments.length - 1 ? Math.round(sungEndMs) : Math.round(startMs + (elapsedWeight + sungWeight) / totalWeight * bodyMs);
      elapsedWeight += weights[index];
      return { text: segment.text, startMs: wordStartMs, endMs: Math.max(wordEndMs, wordStartMs + 1) };
    });
    const result = normalizeCompact(words.map((word) => word.text).join("")) === normalizeCompact(line.text) ? words : null;
    lineCache.set(cacheKey, result);
    return result;
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/jizura/jizuraWordTiming.ts
  var createJizuraWordIntervals = (text, lineStart, lineEnd, words, limit) => {
    const source = text.replace(/\s/gu, "");
    const result = [];
    let cursor = 0;
    for (let i = 0; i < Math.min(words?.length ?? 0, 512) && result.length < Math.min(limit, 256); i++) {
      const word = words[i];
      if (!word || typeof word.text !== "string" || !Number.isFinite(word.startMs)) continue;
      const value = word.text.slice(0, 512).replace(/\s/gu, "");
      if (!value) continue;
      const at = source.indexOf(value, cursor);
      if (at < 0) continue;
      const start = Math.max(lineStart, word.startMs / 1e3);
      if (start >= lineEnd) continue;
      const rawEnd = word.endMs ?? words?.[i + 1]?.startMs;
      const end = Math.min(lineEnd, Math.max(start + 5e-3, Number.isFinite(rawEnd) ? rawEnd / 1e3 : lineEnd));
      const from = [...source.slice(0, at)].length, to = from + [...value].length;
      result.push({ from, to, start, end });
      cursor = at + value.length;
    }
    return result;
  };
  var populateEstimatedJizuraWordIntervals = (lines, sources, limit) => {
    if (limit <= 0 || lines.every((line) => line.timings?.length)) return;
    const inputs = lines.map((line, index) => ({
      text: line.text,
      timeMs: line.start * 1e3,
      endMs: sources[index].endMs,
      kana: sources[index].kana?.slice(0, 512),
      romanization: sources[index].romanization?.slice(0, 512)
    }));
    const calibration = createLyricsWordTimingCalibration(inputs);
    for (let i = 0; i < lines.length && limit > 0; i++) {
      const line = lines[i];
      if (line.timings?.length) continue;
      const words = estimateLyricWordTimings(inputs[i], line.end * 1e3, calibration);
      const timings = createJizuraWordIntervals(line.text, line.start, line.end, words, limit);
      if (timings.length) {
        line.timings = timings;
        line.estimatedTimings = true;
        limit -= timings.length;
      }
    }
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/blossom/blossomTypes.ts
  var blossomKindIds = [
    "editorial",
    "impact",
    "ribbon",
    "tableau",
    "collage",
    "poster",
    "orbit",
    "slice",
    "aperture",
    "stair",
    "shatter",
    "swarm",
    "tunnel",
    "pendulum",
    "cinema",
    "wave",
    "stack"
  ];

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/blossom/blossomWordGroups.ts
  var cjk = /[\p{Script=Han}\p{Script=Hiragana}\p{Script=Katakana}\p{Script=Hangul}]/u;
  var kana = /[\p{Script=Hiragana}\p{Script=Katakana}]/u;
  var closing = /^[,.;:!?，。！？、：；）】》」』〉〕］)}\]’”]+$/u;
  var opening = /^[（【《「『〈〔［({[‘“]+$/u;
  var segmenters = typeof Intl.Segmenter === "function" ? { zh: new Intl.Segmenter("zh", { granularity: "word" }), ja: new Intl.Segmenter("ja", { granularity: "word" }) } : null;
  var groupBlossomWords = (text, words) => {
    if (words.length < 2 || !cjk.test(text) || !segmenters) return words;
    const spans = [];
    const gaps = /* @__PURE__ */ new Set();
    let compactOffset = 0;
    for (const part of (kana.test(text) ? segmenters.ja : segmenters.zh).segment(text)) {
      const start = compactOffset;
      for (const char of part.segment) {
        if (/\s/u.test(char)) gaps.add(compactOffset);
        else compactOffset += 1;
      }
      if (part.isWordLike && cjk.test(part.segment)) spans.push({ start, end: compactOffset });
    }
    if (words.map((word) => word.text).join("") !== text.replace(/\s/gu, "")) return words;
    const groups = [];
    let spanIndex = 0, offset = 0, first = 0;
    for (let index = 0; index < words.length; index += 1) {
      offset += Array.from(words[index].text).length;
      while (spans[spanIndex] && spans[spanIndex].end <= offset) spanIndex += 1;
      const span = spans[spanIndex], next = words[index + 1];
      const join = span && span.start < offset && offset < span.end;
      if (next && !gaps.has(offset) && (join || closing.test(next.text) || opening.test(words[index].text))) continue;
      if (first === index) groups.push(words[index]);
      else {
        const parts = words.slice(first, index + 1);
        groups.push({
          text: parts.map((word) => word.text).join(""),
          start: parts[0].start,
          end: Math.max(...parts.map((word) => word.end)),
          minor: parts.every((word) => word.minor),
          chars: parts.flatMap((word) => word.chars),
          emphasis: {
            chars: Math.max(...parts.map((word) => word.chars.filter((char) => char.ch.trim()).length)),
            duration: Math.max(...parts.map((word) => word.end - word.start))
          }
        });
      }
      first = index + 1;
    }
    return groups;
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/blossom/blossomPlan.ts
  var maxLines = 512;
  var maxTextLength = 32768;
  var maxWordsPerLine = 12;
  var MINOR = /* @__PURE__ */ new Set([
    "\u306E",
    "\u304C",
    "\u3092",
    "\u306B",
    "\u3082",
    "\u306F",
    "\u3067",
    "\u3068",
    "\u3066",
    "a",
    "an",
    "the",
    "of",
    "to",
    "in",
    "on",
    "at",
    "and",
    "is",
    "i",
    "it",
    "we",
    "are",
    "my",
    "me",
    "you",
    "your",
    "be",
    "or",
    "as",
    "so",
    "\u7684",
    "\u4E86",
    "\u5728",
    "\u628A",
    "\u662F",
    "\u307E\u3067",
    "\u3044\u304F",
    "\u3078",
    "\u3084",
    "\u304B\u3089",
    "\u3088\u308A"
  ]);
  var graphemes = splitLyricGraphemes;
  var wordSegmenter2 = typeof Intl !== "undefined" && "Segmenter" in Intl ? new Intl.Segmenter(void 0, { granularity: "word" }) : null;
  var splitUntimed = (text) => {
    const spaced = text.trim().split(/\s+/u).filter(Boolean);
    if (spaced.length > 1) return spaced;
    const only = spaced[0] ?? "";
    if (!only) return [];
    const g = graphemes(only);
    if (g.length <= 4) return [only];
    const parts = wordSegmenter2 ? Array.from(wordSegmenter2.segment(only), (part) => part.segment) : [];
    if (parts.length > 1) return parts;
    const chunks = [];
    for (let i = 0; i < g.length; i += 4) chunks.push(g.slice(i, i + 4).join(""));
    return chunks;
  };
  var toWord = (text, start, end) => {
    const g = graphemes(text), n = Math.max(1, g.length), span = Math.max(0.01, end - start);
    return {
      text,
      start,
      end,
      minor: MINOR.has(text.toLowerCase()),
      chars: g.map((ch, k) => ({ ch, s: start + span * k / n, e: start + span * (k + 1) / n }))
    };
  };
  var wordsFromTimings = (text, timings, lineStart, lineEnd) => {
    const compact = Array.from(text.replace(/\s/gu, ""));
    const sorted = [...timings].sort((a, b) => a.from - b.from);
    const words = [];
    let cursor = 0, cursorTime = lineStart;
    const push = (from, to, s, e) => {
      const piece = compact.slice(from, to).join("");
      if (piece) words.push(toWord(piece, s, Math.max(s + 0.02, e)));
    };
    for (const item of sorted) {
      if (item.from < cursor) continue;
      if (item.from > cursor) push(cursor, item.from, cursorTime, Math.max(cursorTime + 0.02, item.start));
      push(item.from, item.to, item.start, item.end);
      cursor = item.to;
      cursorTime = item.end;
    }
    if (cursor < compact.length) push(cursor, compact.length, cursorTime, Math.max(cursorTime + 0.05, lineEnd));
    return words;
  };
  var wordsFromEstimate = (text, lineStart, lineEnd) => {
    const parts = splitUntimed(text);
    const counts = parts.map((part) => Math.max(1, graphemes(part).length));
    const total = counts.reduce((sum, n) => sum + n + 0.8, 0) || 1;
    const unitTime = Math.max(0.02, (lineEnd - lineStart) * 0.86 / total);
    let cursor = lineStart + Math.min(0.1, (lineEnd - lineStart) * 0.05);
    return parts.map((part, i) => {
      const end = cursor + unitTime * counts[i], word = toWord(part, cursor, end);
      cursor = end + unitTime * 0.8;
      return word;
    });
  };
  var mergeToLimit = (words, limit) => {
    const list = [...words];
    while (list.length > limit) {
      let best = 0, bestLength = Infinity;
      for (let i = 0; i < list.length - 1; i++) {
        const length = list[i].chars.length + list[i + 1].chars.length;
        if (length < bestLength) {
          bestLength = length;
          best = i;
        }
      }
      const a = list[best], b = list[best + 1], latin = /[A-Za-z0-9]$/.test(a.text) && /^[A-Za-z0-9]/.test(b.text);
      const joined = latin ? " " : "";
      const chars = [...a.chars];
      if (joined) chars.push({ ch: " ", s: a.end, e: a.end });
      chars.push(...b.chars);
      const emphasis = a.emphasis || b.emphasis ? {
        chars: Math.max(a.emphasis?.chars ?? a.chars.length, b.emphasis?.chars ?? b.chars.length),
        duration: Math.max(a.emphasis?.duration ?? a.end - a.start, b.emphasis?.duration ?? b.end - b.start)
      } : void 0;
      list.splice(best, 2, { text: a.text + joined + b.text, start: a.start, end: b.end, minor: false, chars, emphasis });
    }
    return list;
  };
  var normalise = (text) => text.toLowerCase().replace(/[\s\p{P}\p{S}]+/gu, "");
  var collectLines = (lyrics, durationMs) => {
    const out = [];
    const intervals = [];
    let textLength = 0, budget = 8192;
    if (lyrics.kind !== "synced") return out;
    const meta = [];
    for (let i = 0; i < lyrics.lines.length && meta.length < maxLines; i++) {
      const source = lyrics.lines[i];
      if (!Number.isFinite(source.timeMs)) continue;
      const text = source.text.replace(/[\r\n]/g, " ").slice(0, Math.min(512, maxTextLength - textLength));
      if (!text.trim()) continue;
      textLength += text.length;
      const start = Math.max(0, source.timeMs / 1e3), next = lyrics.lines[i + 1]?.timeMs;
      const end = source.endMs ?? (Number.isFinite(next) ? next : durationMs);
      const lineEnd = Math.max(start + 0.05, Number.isFinite(end) ? end / 1e3 : start + 4);
      const timings = createJizuraWordIntervals(text, start, lineEnd, source.words, budget);
      budget -= timings.length;
      meta.push({ text, start, end: lineEnd, source });
      intervals.push(timings);
      if (textLength >= maxTextLength) break;
    }
    const shaped = meta.map((m, i) => ({ text: m.text, start: m.start, end: m.end, timings: intervals[i].length ? intervals[i] : void 0 }));
    populateEstimatedJizuraWordIntervals(shaped, meta.map((m) => m.source), budget);
    meta.forEach((m, i) => {
      const timings = shaped[i].timings ?? [];
      let words = timings.length ? wordsFromTimings(m.text, timings, m.start, m.end) : wordsFromEstimate(m.text, m.start, m.end);
      if (!words.length) return;
      const sourceWordCount = Math.min(words.length, maxWordsPerLine);
      if (m.source.wordTimingOrigin !== "segmented") words = groupBlossomWords(m.text, words);
      words = mergeToLimit(words, maxWordsPerLine);
      const translation = typeof m.source.translation === "string" && m.source.translation.trim() ? m.source.translation.trim().slice(0, 160) : null;
      out.push({ line: { start: m.start, end: m.end, words, translation, sourceWordCount }, source: m.source, text: m.text, songPart: m.source.songPart ?? null });
    });
    return out;
  };
  var themeRules = [
    [7, /rain|neon|雨|霓虹|city|街/i],
    [6, /star|星|流れ|galaxy|cosmos/i],
    [15, /firework|花火|烟花|煙火|祭り|festival/i],
    [13, /sakura|cherry|blossom|petal|桜|樱|櫻|花びら|花瓣|春/i],
    [14, /snow|winter|雪|冬/i],
    [16, /tear|\bcry|涙|泪|哭/i],
    [17, /\bdeep\b|\bdive|深海|水底|沈む|沉/i],
    [18, /ripple|\bdrops?\b|\blake|波紋|涟漪|水面|湖/i],
    [11, /\bsea\b|ocean|moon|海|波|月/i],
    [9, /home|家|帰|almost|もうすぐ|回家/i],
    [2, /silence|silent|quiet|静|沈黙|寂/i],
    [3, /wind|train|station|platform|風|风|駅|站|電車/i],
    [0, /dawn|sunrise|sunset|dusk|morning|明け|夜明|晨|黎明|夕|黄昏/i],
    [10, /tunnel|infinite|forever|限りない|トンネル|隧道|无尽/i],
    [19, /\bwish|lantern|祈|願|愿|灯籠|天灯|\bdream|夢|梦/i],
    [20, /cloud|\bwings?\b|\bfly|雲|云|翼|飛|飞/i],
    [12, /aurora|\bsky\b|极光|オーロラ|夜空|天空/i],
    [8, /light|sun|window|光|窓|窗|日差し/i],
    [4, /ending|begin|again|loop|終|始|结局|开始/i],
    [5, /walk|road|run|歩|道|走|路/i],
    [1, /voice|sing|song|sound|声|歌|響|音/i]
  ];
  var isChorusPart = (part) => !!part && /chorus|hook|refrain|サビ|副歌/i.test(part);
  var restShot = (t0, t1) => ({ lines: [], t0, t1, section: "", chorus: false, theme: -1, rest: true });
  var createBlossomPlan = (lyrics, durationMs, title, artist) => {
    const collected = collectLines(lyrics, durationMs);
    const duration = Number.isFinite(durationMs) && durationMs > 0 ? durationMs / 1e3 : (collected.at(-1)?.line.end ?? 0) + 4;
    const lines = collected.map((item) => item.line);
    const seen = /* @__PURE__ */ new Map();
    collected.forEach((item) => {
      const key = normalise(item.text);
      if (key.length >= 3) seen.set(key, (seen.get(key) ?? 0) + 1);
    });
    const chorusLine = collected.map((item) => isChorusPart(item.songPart) || (seen.get(normalise(item.text)) ?? 0) >= 2);
    const groups = [];
    for (let i = 0; i < lines.length; ) {
      const group = [i];
      let words = lines[i].sourceWordCount ?? lines[i].words.length;
      while (group.length < 2 && i + group.length < lines.length) {
        const next = i + group.length, last = lines[group[group.length - 1]];
        const nextWords = lines[next].sourceWordCount ?? lines[next].words.length;
        if (words + nextWords > 9 || lines[next].start - last.end > 1.2 || lines[next].end - lines[group[0]].start > 8) break;
        group.push(next);
        words += nextWords;
      }
      groups.push(group);
      i += group.length;
    }
    const shots = [];
    const hold = 3;
    groups.forEach((group, gi) => {
      const first = lines[group[0]], last = lines[group[group.length - 1]], nextFirst = groups[gi + 1] ? lines[groups[gi + 1][0]].start : null;
      let t0 = first.start;
      if (gi === 0) t0 = first.start <= 5 ? 0 : first.start;
      let t1;
      if (nextFirst !== null) t1 = nextFirst - last.end <= 6 ? nextFirst : last.end + hold;
      else t1 = Math.max(last.end + 2.5, Math.min(duration, last.end + 8));
      t1 = Math.max(t1, t0 + 0.8);
      if (gi === 0 && first.start > 5) shots.push(restShot(0, t0));
      const text = group.map((index) => collected[index].text).join(" ");
      const rule = themeRules.find(([, re]) => re.test(text));
      shots.push({
        lines: group,
        t0,
        t1,
        theme: rule ? rule[0] : -1,
        rest: false,
        chorus: group.some((index) => chorusLine[index]),
        section: (collected[group[0]].songPart ?? "").trim().toUpperCase().slice(0, 14) || (group.some((index) => chorusLine[index]) ? "CHORUS" : "")
      });
      if (nextFirst !== null && nextFirst - t1 > 0.5) shots.push(restShot(t1, nextFirst));
      else if (nextFirst === null && duration - t1 > 8) shots.push(restShot(t1, duration));
    });
    for (let i = 1; i < shots.length; i++) {
      if (shots[i].theme >= 0 && shots[i].theme === shots[i - 1].theme) shots[i] = { ...shots[i], theme: -1 };
    }
    return { lines, shots, duration, title, artist };
  };
  var shotIndexAt = (plan2, t) => {
    let lo = 0, hi = plan2.shots.length - 1, found = 0;
    while (lo <= hi) {
      const mid = lo + hi >> 1;
      if (plan2.shots[mid].t0 <= t) {
        found = mid;
        lo = mid + 1;
      } else hi = mid - 1;
    }
    return found;
  };
  var kindMinWords = {
    editorial: 1,
    impact: 1,
    ribbon: 3,
    tableau: 1,
    collage: 3,
    poster: 2,
    orbit: 3,
    slice: 1,
    aperture: 1,
    stair: 3,
    shatter: 1,
    swarm: 1,
    tunnel: 2,
    pendulum: 2,
    cinema: 1,
    wave: 3,
    stack: 2
  };
  var energetic = ["impact", "slice", "shatter", "swarm", "tunnel", "poster", "stair", "stack"];
  var calm = ["editorial", "tableau", "ribbon", "orbit", "collage", "aperture", "pendulum", "cinema", "wave"];
  var resolveBlossomKinds = (plan2, seed) => {
    const r = rng(`kinds:${seed}`);
    const kinds = [];
    const recent = [];
    plan2.shots.forEach((shot, index) => {
      if (shot.rest) {
        kinds.push("editorial");
        return;
      }
      const words = shot.lines.reduce((sum, li) => sum + plan2.lines[li].words.length, 0);
      const fits = blossomKindIds.filter((kind2) => words >= kindMinWords[kind2]);
      const pool = (shot.chorus ? energetic : calm).filter((kind2) => fits.includes(kind2));
      const other = (shot.chorus ? calm : energetic).filter((kind2) => fits.includes(kind2));
      const preferred = (r() < 0.72 ? pool : other).filter((kind2) => !recent.includes(kind2));
      const any = fits.filter((kind2) => !recent.includes(kind2));
      const candidates = preferred.length ? preferred : any.length ? any : fits;
      const kind = index === 0 && !seed && fits.includes("editorial") ? "editorial" : candidates[Math.floor(r() * candidates.length)];
      kinds.push(kind);
      recent.push(kind);
      if (recent.length > 4) recent.shift();
    });
    return kinds;
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/blossom/blossomOverlayText.ts
  var BlossomOverlayText = class {
    plan = null;
    shot = null;
    marquee = { text: "", font: "", width: 0 };
    caption = null;
    captionStart = Infinity;
    captionEnd = -Infinity;
    index = { key: "", font: "", num: "", rest: "", numWidth: 0 };
    credit = { key: "", text: "" };
    clear() {
      this.plan = null;
      this.shot = null;
      this.marquee = { text: "", font: "", width: 0 };
      this.caption = null;
      this.captionStart = Infinity;
      this.captionEnd = -Infinity;
      this.index = { key: "", font: "", num: "", rest: "", numWidth: 0 };
      this.credit = { key: "", text: "" };
    }
    usePlan(plan2) {
      if (this.plan === plan2) return;
      this.clear();
      this.plan = plan2;
    }
    getMarquee(plan2, shot, context) {
      this.usePlan(plan2);
      if (this.shot !== shot) {
        this.shot = shot;
        this.marquee.text = `${shot.def.lines.map((li) => plan2.lines[li].words.map((w) => w.text).join(" ")).join("   \u2726   ")}   \u2726   `;
        this.marquee.font = "";
      }
      if (this.marquee.font !== context.font) {
        this.marquee.font = context.font;
        this.marquee.width = context.measureText(this.marquee.text).width || 1;
      }
      return this.marquee;
    }
    getCaption(plan2, time, context) {
      this.usePlan(plan2);
      if (!(time >= this.captionStart && time < this.captionEnd)) {
        let index = -1;
        for (let i = 0; i < plan2.lines.length; i++) {
          if (plan2.lines[i].start <= time) index = i;
          else break;
        }
        const line = index >= 0 ? plan2.lines[index] : null;
        this.caption = line?.translation ? { line, font: "", width: 0 } : null;
        this.captionStart = line?.start ?? -Infinity;
        this.captionEnd = plan2.lines[index + 1]?.start ?? Infinity;
      }
      if (this.caption && this.caption.font !== context.font) {
        this.caption.font = context.font;
        this.caption.width = context.measureText(this.caption.line.translation).width;
      }
      return this.caption;
    }
    /** "03" and " / 12 · CHORUS" as two runs, so the number can be set brighter than the rest. */
    getIndex(shot, count, section, context) {
      const key = `${shot}|${count}|${section}`;
      if (this.index.key !== key || this.index.font !== context.font) {
        const num = String(shot + 1).padStart(2, "0");
        this.index = { key, font: context.font, num, rest: ` / ${String(count).padStart(2, "0")}${section ? `   ${section}` : ""}`, numWidth: context.measureText(num).width };
      }
      return this.index;
    }
    /** "TITLE — ARTIST", shortened with an ellipsis to fit `maxWidth` in the current font; empty when the song has neither. */
    getCredit(plan2, context, maxWidth) {
      const key = `${plan2.title}|${plan2.artist}|${context.font}|${Math.round(maxWidth)}`;
      if (this.credit.key === key) return this.credit.text;
      const parts = [plan2.title.trim(), plan2.artist.trim()].filter(Boolean);
      let text = parts.join("  \u2014  ");
      if (text && context.measureText(text).width > maxWidth) {
        const chars = Array.from(text);
        while (chars.length > 1 && context.measureText(chars.join("") + "\u2026").width > maxWidth) chars.pop();
        text = chars.join("").trimEnd() + "\u2026";
      }
      this.credit = { key, text };
      return text;
    }
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/blossom/blossomBackdropLayer.ts
  var BACKDROP_DENSITY = 0.75;
  var BlossomBackdropLayers = class {
    slots = [null, null];
    /** The slot's canvas sized for a `cssW` × `cssH` stage drawn at `stageDpr`; null when no 2D context is available. */
    acquire(slot, cssW, cssH, stageDpr) {
      const dpr = Math.min(stageDpr, BACKDROP_DENSITY), w = Math.max(2, Math.round(cssW * dpr)), h = Math.max(2, Math.round(cssH * dpr));
      let layer = this.slots[slot];
      if (!layer) {
        const c = document.createElement("canvas"), x = c.getContext("2d", { alpha: false });
        if (!x) return null;
        layer = { c, x, dpr, si: -1, frame: -1 };
        this.slots[slot] = layer;
      }
      if (layer.c.width !== w || layer.c.height !== h) {
        layer.c.width = w;
        layer.c.height = h;
        layer.si = -1;
      }
      layer.dpr = dpr;
      return layer;
    }
    /** Let go of the second slot (only needed around a cut). */
    releaseSecond() {
      this.free(1);
    }
    release() {
      this.free(0);
      this.free(1);
    }
    free(slot) {
      const layer = this.slots[slot];
      if (layer) {
        layer.c.width = 0;
        layer.c.height = 0;
      }
      this.slots[slot] = null;
    }
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/blossom/blossomTypeWall.ts
  var SPRITE_DENSITY = 0.5;
  var KEEP = 3;
  var MAX_SPRITE_W = 4096;
  var ROWS = [
    [-0.36, 0.8, 0.09, 0.032],
    [-0.13, 1.25, -0.06, 0.042],
    [0.12, 0.62, 0.12, 0.028],
    [0.36, 1.05, -0.075, 0.038]
  ];
  var LATIN_END = /[A-Za-z0-9'’,.!?]$/;
  var LATIN_START = /^[A-Za-z0-9]/;
  var sprites = /* @__PURE__ */ new Map();
  var releaseTypeWall = () => {
    sprites.forEach((sp) => {
      sp.c.width = 0;
      sp.c.height = 0;
    });
    sprites.clear();
  };
  var wallText = (plan2, sh) => {
    if (sh.rest) return [plan2.title, plan2.artist].filter(Boolean).join("  \xB7  ");
    return sh.def.lines.map((li) => (plan2.lines[li]?.words ?? []).reduce((line, w) => line + (line && LATIN_END.test(line) && LATIN_START.test(w.text) ? " " : "") + w.text, "")).filter(Boolean).join("   ");
  };
  var spriteFor = (s, plan2, sh) => {
    const text = wallText(plan2, sh).trim();
    if (!text || typeof document === "undefined") return null;
    const px = Math.round(s.H * 0.15), font = `900 ${px}px ${s.fonts.sans}`, key = `${font}|${text}`, hit = sprites.get(sh);
    if (hit && hit.key === key) {
      sprites.delete(sh);
      sprites.set(sh, hit);
      return hit;
    }
    const c = hit?.c ?? document.createElement("canvas"), x = c.getContext("2d");
    if (!x) return null;
    x.font = font;
    const gap = px * 1.2, w = x.measureText(text).width + gap, h = px * 1.35, r = Math.min(SPRITE_DENSITY, MAX_SPRITE_W / w);
    c.width = Math.max(2, Math.ceil(w * r));
    c.height = Math.max(2, Math.ceil(h * r));
    x.setTransform(r, 0, 0, r, 0, 0);
    x.font = font;
    x.textAlign = "left";
    x.textBaseline = "middle";
    x.lineJoin = "round";
    x.lineWidth = Math.max(1.5, px * 0.012) / Math.min(1, r * 2);
    x.strokeStyle = "#ffffff";
    x.strokeText(text, gap / 2, h / 2);
    const sp = { key, c, w, h };
    sprites.delete(sh);
    sprites.set(sh, sp);
    while (sprites.size > KEEP) {
      const first = sprites.keys().next().value;
      if (!first) break;
      const old = sprites.get(first);
      if (old) {
        old.c.width = 0;
        old.c.height = 0;
      }
      sprites.delete(first);
    }
    return sp;
  };
  var drawTypeWall = (s, plan2, sh, kick, ox, oy) => {
    const sp = spriteFor(s, plan2, sh);
    if (!sp) return;
    const g = s.g, { W, H } = s, t = s.t;
    const fade = easeIO(clamp01((t - sh.def.t0) / 0.9)) * (sh.def.chorus ? 1.25 : 1) * (sh.rest ? 0.8 : sh.custom ? 0.6 : 1);
    if (fade <= 0.01) return;
    const tilt = (hash(sh.si * 31 + s.seed) % 2 ? 1 : -1) * 0.045, unitPx = H * 0.15;
    g.save();
    g.translate(W / 2 + ox * 0.5, H / 2 + oy * 0.5);
    g.rotate(tilt);
    g.globalCompositeOperation = "lighter";
    ROWS.forEach(([y, size, speed, a], i) => {
      const w = sp.w * size, h = sp.h * size;
      const travel2 = t * speed * unitPx * size + i * w * 0.37 + kick * Math.sign(speed) * unitPx * 0.06;
      const x0 = -W * 0.62 - (travel2 % w + w) % w;
      g.globalAlpha = Math.min(1, a * fade * (1 + 0.2 * kick));
      for (let x = x0; x < W * 0.62; x += w) g.drawImage(sp.c, x, y * H - h / 2, w, h);
    });
    g.restore();
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/blossom/blossomScenes.ts
  var ADD = "lighter";
  var OVER = "source-over";
  var ridge = (s, seed, base3, amp, top, bot, sx, rim, rimX, rimA) => {
    const g = s.g, { W, H } = s, crest = new Path2D();
    for (let x = -W * 0.7, i = 0; x <= W * 0.7 + 1; x += 20, i++) {
      const u = x + sx, y = base3 + amp * (0.5 * Math.sin(u * 42e-4 + seed) + 0.3 * Math.sin(u * 0.011 + seed * 2.1) + 0.2 * Math.sin(u * 0.027 + seed * 3.7));
      if (i) crest.lineTo(x, y);
      else crest.moveTo(x, y);
    }
    const body = new Path2D(crest);
    body.lineTo(W * 0.7, H);
    body.lineTo(-W * 0.7, H);
    body.closePath();
    const fill = g.createLinearGradient(0, base3 - amp, 0, base3 + amp * 2.4);
    fill.addColorStop(0, top);
    fill.addColorStop(1, bot);
    g.globalAlpha = 1;
    g.fillStyle = fill;
    g.fill(body);
    if (rimA > 0) {
      const rg = g.createLinearGradient(rimX - W * 0.42, 0, rimX + W * 0.42, 0);
      rg.addColorStop(0, rim + "00");
      rg.addColorStop(0.5, rim + "ff");
      rg.addColorStop(1, rim + "00");
      g.save();
      g.globalCompositeOperation = ADD;
      g.globalAlpha = rimA;
      g.strokeStyle = rg;
      g.lineWidth = 1.4;
      g.stroke(crest);
      g.restore();
    }
  };
  var haze = (g, W, y, ry, col, a) => {
    g.globalCompositeOperation = ADD;
    mist(g, 0, y, W * 0.85, ry, col, a);
    g.globalCompositeOperation = OVER;
  };
  var BLIND_N = 7;
  var blind = (i, t, W) => ({
    c: (i - (BLIND_N - 1) / 2) * 0.13 * W + Math.sin(t * 0.06 + i * 1.3) * W * 0.017,
    w: W * (0.03 + 0.05 * unit("bw" + i)),
    a: (0.15 + 0.1 * unit("ba" + i)) * (0.82 + 0.18 * Math.sin(t * 0.35 + i * 1.9))
  });
  var SCENES2 = {
    6(s, _sh, t, c) {
      const g = s.g, { W, H } = s;
      g.globalCompositeOperation = ADD;
      mist(g, -W * 0.22, -H * 0.08, W * 0.55, H * 0.42, c.a3, 0.26);
      mist(g, W * 0.26, H * 0.18, W * 0.5, H * 0.34, c.acc, 0.18);
      mist(g, W * 0.05, -H * 0.2, W * 0.4, H * 0.18, c.a2, 0.12);
      for (let m = 0; m < 2; m++) {
        const per = m ? 13.5 : 9.5, dur = 2.6, e = (t + m * 4.7) % per / dur;
        if (e >= 1) continue;
        const sx = W * (0.55 - 0.15 * m), sy = -H * 0.5 - m * H * 0.05, dx = -W * 1, dy = H * 0.85, x = sx + dx * e, y = sy + dy * e, ang = Math.atan2(dy, dx), a = Math.pow(Math.sin(Math.PI * e), 1.2);
        tail(g, x, y, ang, W * (m ? 0.2 : 0.3), m ? 4 : 6, m ? c.a2 : c.acc, 0.9 * a);
        glow(g, x, y, 30, 30, m ? c.a2 : c.acc, 0.85 * a);
        glint(g, x, y, 26, "#ffffff", 0.7 * a, ang);
      }
      g.globalCompositeOperation = OVER;
    },
    8(s, _sh, t, c) {
      const g = s.g, { W, H } = s, lean = 0.4, n = BLIND_N, steps = 10;
      const bands = Array.from({ length: n }, (_, i) => blind(i, t, W));
      g.save();
      g.globalCompositeOperation = "lighter";
      const wide = g.createLinearGradient(-W * 0.35, 0, W * 0.35, 0);
      wide.addColorStop(0, c.acc + "00");
      wide.addColorStop(0.5, c.acc + "22");
      wide.addColorStop(1, c.acc + "00");
      g.save();
      g.transform(1, 0, lean, 1, 0, 0);
      g.fillStyle = wide;
      for (let j = 0; j < steps; j++) {
        g.globalAlpha = Math.pow(1 - j / steps, 1.3);
        g.fillRect(-W * 0.35, -H * 0.7 + j * H * 0.14, W * 0.7, H * 0.14 + 1);
      }
      g.restore();
      for (let i = 0; i < n; i++) {
        const b = bands[i], grad = g.createLinearGradient(b.c - b.w / 2, 0, b.c + b.w / 2, 0), col = i % 3 === 1 ? c.a2 : c.acc;
        grad.addColorStop(0, col + "00");
        grad.addColorStop(0.5, col + "ff");
        grad.addColorStop(1, col + "00");
        g.save();
        g.transform(1, 0, lean, 1, 0, 0);
        g.fillStyle = grad;
        for (let j = 0; j < steps; j++) {
          g.globalAlpha = b.a * Math.pow(1 - j / steps, 1.3);
          g.fillRect(b.c - b.w / 2, -H * 0.7 + j * H * 0.14, b.w, H * 0.14 + 1);
        }
        g.restore();
      }
      g.fillStyle = "#fff";
      for (let i = 0; i < 64; i++) {
        const z = 0.4 + unit("mz" + i), x = (unit("mx" + i) - 0.5) * W * 1.1 + Math.sin(t * 0.25 * z + i) * 18, y = ((unit("my" + i) - t * 0.012 * z) % 1 + 1) % 1 * H * 1.2 - H * 0.6;
        const u = x - lean * y;
        let lit = 0;
        for (let k = 0; k < n; k++) {
          const b = bands[k], d = Math.abs(u - b.c) / (b.w * 0.5);
          if (d < 1) lit = Math.max(lit, (1 - d) * (1 - d) * b.a / 0.2);
        }
        const fade = Math.pow(Math.max(0, 1 - (y + H * 0.6) / (H * 1.2)), 1.1);
        g.globalAlpha = Math.min(0.9, (0.05 + 0.55 * Math.min(1, lit)) * fade * (0.6 + 0.4 * Math.sin(t * 1.4 + i)));
        if (g.globalAlpha > 0.01) g.fillRect(x, y, 1.5 + z, 1.5 + z);
      }
      g.restore();
      g.globalAlpha = 1;
    },
    9(s, _sh, t, c, _P, bt, lin, lt) {
      const g = s.g, { W, H } = s, hx = W * 0.3, hy = H * 0.31, on = easeIO(clamp01((lt - 1.4) / 1.6));
      g.globalCompositeOperation = ADD;
      mist(g, hx, hy + H * 0.04, W * 0.5, H * 0.2, c.a3, 0.12 * on);
      g.globalCompositeOperation = OVER;
      ridge(s, 0.7, H * 0.34, H * 0.08, hexMix("#121a2e", c.acc, 0.22), "#070a14", -lin * 40, c.a3, hx, 0.4 * on);
      haze(g, W, H * 0.42, H * 0.06, c.acc, 0.14);
      ridge(s, 3.3, H * 0.44, H * 0.06, hexMix("#070a14", c.a2, 0.1), "#03050a", -lin * 80, c.a2, hx, 0.16);
      g.globalCompositeOperation = ADD;
      beam(g, hx, hy, 2.35, H * 0.6, W * 0.22, c.a3, 0.16 * on);
      glow(g, hx, hy, H * 0.5, H * 0.4, c.a3, 0.45 * on);
      panel(g, hx, hy, W * 0.02, H * 0.034, "#ffd9a0", 0.9 * on);
      glow(g, hx, hy, H * 0.06, H * 0.06, "#fff2d0", 0.6 * on * (1 + 0.4 * bt));
      glint(g, hx, hy, H * 0.09, "#ffffff", 0.25 * on);
      for (let i = 0; i < 16; i++) {
        const u = i / 15, x = -W * 0.4 + (hx + W * 0.4) * u, y = H * 0.47 - (H * 0.47 - hy) * Math.pow(u, 1.4), w = clamp01((lt - 0.2 * i) / 0.8) * (0.55 + 0.45 * Math.sin(t * 0.9 - i * 0.6));
        glow(g, x, y, 15, 15, c.acc, 0.5 * w);
      }
      g.globalCompositeOperation = OVER;
    },
    10(s, _sh, t, c, _P, bt) {
      const g = s.g, { W, H } = s;
      g.globalCompositeOperation = ADD;
      glow(g, 0, 0, H * 0.7, H * 0.55, c.acc, 0.2);
      for (let i = 0; i < 16; i++) beam(g, 0, 0, i * 0.3927 + t * 0.016, W * 0.78, W * 0.17, i % 2 ? c.a2 : c.acc, 0.05 + 0.02 * Math.sin(t * 0.25 + i * 1.7));
      glow(g, 0, 0, H * 0.1, H * 0.08, "#ffffff", 0.14 * (1 + 0.15 * bt));
      g.globalCompositeOperation = OVER;
    }
  };
  var drawScene = (s, sh, P, bt, lin) => {
    if (CLASSIC_THEMES.has(sh.theme)) return drawClassicScene(s, sh, P, bt, lin);
    const fn = SCENES2[sh.theme] ?? SEASON_SCENES[sh.theme] ?? SKY_SCENES[sh.theme];
    if (!fn) return false;
    const g = s.g;
    g.save();
    g.lineWidth = 1;
    fn(s, sh, s.t, { acc: P.acc, a2: P.acc2, a3: P.acc3 }, P, bt, lin, s.t - sh.def.t0);
    g.restore();
    return true;
  };
  var reflection = null;
  var releaseReflection = () => {
    if (reflection) {
      reflection.width = 0;
      reflection.height = 0;
      reflection = null;
    }
  };
  var drawReflection = (s) => {
    const g = s.g, cv = g.canvas, hy = Math.round(s.H * 0.62 * s.DPR), t = s.t;
    if (hy < 8 || hy >= cv.height) return;
    if (!reflection) reflection = document.createElement("canvas");
    if (reflection.width !== cv.width || reflection.height !== hy) {
      reflection.width = cv.width;
      reflection.height = hy;
    }
    const rg = reflection.getContext("2d");
    if (!rg) return;
    rg.setTransform(1, 0, 0, 1, 0, 0);
    rg.clearRect(0, 0, reflection.width, reflection.height);
    rg.drawImage(cv, 0, 0, cv.width, hy, 0, 0, cv.width, hy);
    g.save();
    g.setTransform(1, 0, 0, 1, 0, 0);
    g.beginPath();
    g.rect(0, hy, cv.width, cv.height - hy);
    g.clip();
    const step = Math.max(2, Math.round(3 * s.DPR));
    for (let y = hy; y < cv.height; y += step) {
      const d = y - hy, srcY = hy - d - step, wob = Math.sin(d * 0.06 / s.DPR + t * 2.2) * (2 + d * 0.012) * s.DPR;
      g.globalAlpha = 0.34 * (1 - d / (cv.height - hy) * 0.85);
      if (srcY >= 0) g.drawImage(reflection, 0, srcY, cv.width, step, wob, y, cv.width, step);
    }
    g.globalAlpha = 1;
    const grad = g.createLinearGradient(0, hy, 0, cv.height);
    grad.addColorStop(0, "rgba(4,6,12,0)");
    grad.addColorStop(1, "rgba(4,6,12,.55)");
    g.fillStyle = grad;
    g.fillRect(0, hy, cv.width, cv.height - hy);
    g.restore();
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/blossom/blossomEngine.ts
  var SANS = '"Noto Sans CJK JP","Noto Sans CJK SC","Hiragino Sans","Yu Gothic","PingFang SC","Microsoft YaHei",system-ui,sans-serif';
  var SERIF = '"Noto Serif CJK JP","Noto Serif JP","Yu Mincho","Hiragino Mincho ProN","Songti SC","SimSun",Georgia,serif';
  var MONO = 'ui-monospace,"SFMono-Regular",Menlo,Consolas,monospace';
  var ACCENT_FADE_MS = 700;
  var SHOT_CACHE = 4;
  var MAX_PIXELS = 5e6;
  var SPARE_IDLE_MS = 12e3;
  var BG_DEPTH = 0.07;
  var LETTERBOX = 0.1;
  var LETTERBOX_INK = "#020308";
  var letterbox = (sh, t) => sh.kind === "cinema" && !sh.rest ? smoothstep((t - sh.def.t0 + 0.5) / 1.1) : 0;
  var SMEAR_TAPS = [[0.2, 0.5], [0.4, 0.42], [0.6, 0.34], [0.8, 0.26], [1, 0.18]];
  var WARM_AHEAD_S = 1.1;
  var TRACK_GAIN = 0.2;
  var TRACK_LIMIT = 0.07;
  var CLOCK_SLEW_S = 0.2;
  var CLOCK_SNAP_S = 0.35;
  var ACTIVE_AFTER_POINTER_MS = 1500;
  var SCALE_MIN = 0.5;
  var SCALE_STEP_DOWN = 0.15;
  var SCALE_STEP_UP = 0.1;
  var SCALE_SLOW_AFTER_MS = 1200;
  var SCALE_FAST_AFTER_MS = 6e3;
  var BlossomEngine = class {
    host;
    main;
    s;
    plan = null;
    kinds = [];
    overrides = /* @__PURE__ */ new Map();
    cache = /* @__PURE__ */ new Map();
    overlayText = new BlossomOverlayText();
    backdrops = new BlossomBackdropLayers();
    clock = { positionMs: 0, updatedAtMs: 0, playbackRate: 1, playing: false, offsetMs: 0 };
    prefs = defaultBlossomPreferences;
    accents = [];
    coverAccents = null;
    fade = null;
    pals = /* @__PURE__ */ new Map();
    themeAccent = "";
    accentReadAt = -1e9;
    motion = true;
    maxDpr = 2;
    cssW = 0;
    cssH = 0;
    lastShot = -1;
    frameNo = 0;
    lastInfoKey = "";
    lastMoveAt = 0;
    /** Half-size snapshot the motion blur draws from; only exists around moves. */
    blur = null;
    scale = 1;
    targetMs = 1e3 / 30;
    lateMs = 0;
    onTimeMs = 0;
    lastScaleChangeAt = 0;
    tc = 0;
    tcAt = 0;
    tcValid = false;
    warmed = -1;
    warmStep = 0;
    warmTask = null;
    activeUntil = 0;
    stop = null;
    interval = null;
    seekEnabled = true;
    disposed = false;
    cleanup = [];
    constructor(host) {
      this.host = host;
      const ctx = host.canvas.getContext("2d", { alpha: host.transparent === true });
      const measureCanvas = document.createElement("canvas");
      const m = measureCanvas.getContext("2d");
      if (!ctx || !m) throw new Error("Canvas 2D is unavailable");
      this.main = ctx;
      this.s = {
        g: ctx,
        m,
        W: 1,
        H: 1,
        DPR: 1,
        BASE: 40,
        intensity: 1,
        seed: 0,
        fx: { ...defaultBlossomPreferences.effects },
        ptr: { vx: 0, vy: 0, pt: 0, down: false, x: 0.5, y: 0.5, sx: 0.5, sy: 0.5, over: false, hx: 0, hy: 0 },
        cam: { tx: 0, ty: 0, cr: 0, cs: 1 },
        fonts: { sans: SANS, serif: SERIF, mono: MONO },
        t: 0,
        fade: 1,
        now: 0,
        bursts: [],
        ret: { x: 0, y: 0, ready: false },
        hover: null,
        r: rng("stage")
      };
      this.accents = resolveAccents("theme", readThemeAccent(m, host.canvas), null);
      const c = host.canvas;
      const local = (e) => {
        const r = c.getBoundingClientRect();
        return { x: e.clientX - r.left, y: e.clientY - r.top };
      };
      const move = (e) => {
        const p = local(e), q = this.s.ptr;
        q.hx = p.x;
        q.hy = p.y;
        q.x = p.x / this.s.W;
        q.y = p.y / this.s.H;
        q.over = true;
        this.wakeFor(ACTIVE_AFTER_POINTER_MS);
        this.updateHover();
      };
      const leave = () => {
        const q = this.s.ptr;
        q.over = false;
        q.down = false;
        q.x = q.y = 0.5;
        this.s.hover = null;
        c.style.cursor = "";
        this.wakeFor(ACTIVE_AFTER_POINTER_MS);
      };
      const down = (e) => {
        if (e.button === 0) this.s.ptr.down = true;
      };
      const up = (e) => {
        if (e.button !== 0) return;
        this.s.ptr.down = false;
        const p = local(e);
        this.click(p.x, p.y);
      };
      c.addEventListener("pointermove", move);
      c.addEventListener("pointerleave", leave);
      c.addEventListener("pointerdown", down);
      c.addEventListener("pointerup", up);
      this.cleanup.push(() => {
        c.removeEventListener("pointermove", move);
        c.removeEventListener("pointerleave", leave);
        c.removeEventListener("pointerdown", down);
        c.removeEventListener("pointerup", up);
      });
      const fontsReady = () => {
        clearBlossomMeasureCache(this.s.m);
        this.dropShots();
        this.lastShot = -1;
        this.lastInfoKey = "";
        this.requestFrame();
      };
      document.fonts?.addEventListener("loadingdone", fontsReady);
      this.cleanup.push(() => document.fonts?.removeEventListener("loadingdone", fontsReady));
    }
    /* ---------- configuration ---------- */
    setPlan(plan2) {
      if (this.disposed) return;
      clearBlossomMeasureCache(this.s.m);
      this.plan = plan2;
      this.overrides.clear();
      this.dropShots();
      this.lastShot = -1;
      this.lastInfoKey = "";
      this.pals.clear();
      this.s.bursts.length = 0;
      this.s.ret.ready = false;
      this.tcValid = false;
      this.kinds = plan2 ? resolveBlossomKinds(plan2, this.s.seed) : [];
      this.requestFrame();
    }
    setPreferences(prefs) {
      const s = this.s, old = this.prefs;
      this.prefs = prefs;
      s.fx = { ...prefs.effects };
      const rebuild = prefs.intensity !== old.intensity || prefs.seed !== old.seed;
      s.intensity = prefs.intensity;
      s.seed = prefs.seed;
      if (prefs.seed !== old.seed && this.plan) {
        this.overrides.clear();
        this.kinds = resolveBlossomKinds(this.plan, prefs.seed);
      }
      if (prefs.palette !== old.palette) this.accentReadAt = -1e9;
      if (rebuild) {
        this.dropShots();
        this.lastShot = -1;
        this.lastInfoKey = "";
      }
      if (!this.themeAccent) this.themeAccent = readThemeAccent(s.m, this.host.canvas);
      this.setAccents(this.pickAccents());
      this.requestFrame();
    }
    /** Accents sampled from the current cover (null when there is none); only used while the palette is set to follow the cover. */
    setCoverAccents(accents) {
      const next = accents && accents.length >= 4 ? accents.slice(0, 4) : null;
      if ((next?.join() ?? "") === (this.coverAccents?.join() ?? "")) return;
      this.coverAccents = next;
      if (this.prefs.palette === "cover") this.setAccents(this.pickAccents());
    }
    pickAccents() {
      return resolveAccents(this.prefs.palette, this.themeAccent || readThemeAccent(this.s.m, this.host.canvas), this.coverAccents);
    }
    /** Swap the accent set; with motion on the colours glide over to it instead of snapping. */
    setAccents(next) {
      if (next.join() === this.accents.join()) return;
      const now = performance.now();
      this.fade = this.accents.length && this.motion ? { from: this.shownAccents(now), t0: now } : null;
      this.accents = next;
      this.wakeFor(ACCENT_FADE_MS + 120);
    }
    shownAccents(now) {
      const f = this.fade;
      if (!f) return this.accents;
      const k = (now - f.t0) / ACCENT_FADE_MS;
      if (k >= 1) {
        this.fade = null;
        return this.accents;
      }
      const e = easeIO(k);
      return this.accents.map((c, i) => hexLerp(f.from[i] ?? c, c, e));
    }
    setClock(clock) {
      if (clock.playing !== this.clock.playing) this.tcValid = false;
      this.clock = clock;
      this.requestFrame();
    }
    setMotion(motion) {
      this.motion = motion;
      this.requestFrame();
    }
    setSeekEnabled(enabled) {
      this.seekEnabled = enabled;
    }
    setMaxDpr(dpr) {
      if (dpr !== this.maxDpr) {
        this.maxDpr = dpr;
        if (this.cssW) this.resize(this.cssW, this.cssH);
      }
    }
    /** Choose the composition of the shot on screen; null returns to the planned one. */
    setKind(kind) {
      if (!this.plan || this.lastShot < 0) return;
      if (kind === null) this.overrides.delete(this.lastShot);
      else this.overrides.set(this.lastShot, kind);
      this.dropShots();
      this.lastShot = -1;
      this.lastInfoKey = "";
      this.requestFrame();
    }
    /** Set the backing-store size and DPR for the current CSS size. Shots are in CSS units, so they survive this. */
    applyBacking(cssW, cssH) {
      const dpr = Math.max(0.5, Math.min(this.maxDpr, window.devicePixelRatio || 1, Math.sqrt(MAX_PIXELS / (cssW * cssH))) * this.scale);
      const c = this.host.canvas, w = Math.round(cssW * dpr), h = Math.round(cssH * dpr);
      if (c.width !== w || c.height !== h) {
        c.width = w;
        c.height = h;
      }
      this.s.DPR = dpr;
      releaseReflection();
      releaseGrain();
    }
    resize(cssW, cssH) {
      if (cssW < 2 || cssH < 2) return;
      this.cssW = cssW;
      this.cssH = cssH;
      this.applyBacking(cssW, cssH);
      const s = this.s;
      s.W = cssW;
      s.H = cssH;
      s.BASE = cssH * 0.05;
      clearBlossomMeasureCache(s.m);
      clearBackdropCaches();
      this.dropShots();
      this.lastShot = -1;
      this.lastInfoKey = "";
      this.requestFrame();
    }
    /* ---------- loop ---------- */
    /** Start (or retime) the frame subscription; null stops it. */
    setFrameInterval(ms) {
      this.stop?.();
      this.stop = null;
      this.interval = ms;
      if (ms === null || this.disposed) {
        this.cancelWarmTask();
        if (ms === null) {
          this.backdrops.release();
          this.releaseBlur();
          releaseDecoSprites();
          releaseTypeWall();
        }
        return;
      }
      this.targetMs = ms > 0 ? ms : 1e3 / 60;
      this.lateMs = 0;
      this.onTimeMs = 0;
      this.requestFrame();
      this.subscribe();
    }
    requestFrame() {
      this.wakeFor(300);
    }
    /** Keep drawing for `ms` more. */
    wakeFor(ms) {
      this.activeUntil = Math.max(this.activeUntil, performance.now() + ms);
    }
    subscribe() {
      if (this.stop || this.interval === null || this.disposed) return;
      this.stop = subscribeVisualFrame((ts, elapsed) => this.frame(ts, elapsed), this.interval);
    }
    dispose() {
      this.disposed = true;
      this.cancelWarmTask();
      this.stop?.();
      this.stop = null;
      this.cleanup.forEach((fn) => fn());
      this.cleanup.length = 0;
      this.dropShots();
      this.s.bursts.length = 0;
      this.s.hover = null;
      this.plan = null;
      this.kinds = [];
      this.overrides.clear();
      this.pals.clear();
      this.coverAccents = null;
      this.fade = null;
      this.backdrops.release();
      this.releaseBlur();
      clearBlossomMeasureCache(this.s.m);
      clearBackdropCaches();
      clearGlyphCaches();
      releaseReflection();
      releaseGrain();
      this.host.canvas.width = 0;
      this.host.canvas.height = 0;
    }
    releaseBlur() {
      if (this.blur) {
        this.blur.width = 0;
        this.blur.height = 0;
      }
      this.blur = null;
    }
    cancelWarmTask() {
      const task = this.warmTask;
      this.warmTask = null;
      if (!task) return;
      if (task.idle) window.cancelIdleCallback(task.id);
      else window.clearTimeout(task.id);
    }
    dropShots() {
      this.cancelWarmTask();
      this.cache.clear();
      this.overlayText.clear();
      releaseDecoSprites();
      releaseTypeWall();
      this.s.hover = null;
      this.warmed = -1;
    }
    /* ---------- shots ---------- */
    kindFor(si) {
      return this.overrides.get(si) ?? this.kinds[si] ?? "editorial";
    }
    themeFor(si) {
      const planned = this.plan?.shots[si].theme ?? -1;
      return planned >= 0 ? planned : (si * 5 + this.s.seed * 3 + 1) % THEME_COUNT;
    }
    shot(si) {
      const plan2 = this.plan, kind = this.kindFor(si), key = `${si}:${kind}`;
      const hit = this.cache.get(key);
      if (hit) {
        this.cache.delete(key);
        this.cache.set(key, hit);
        return hit;
      }
      const theme = this.themeFor(si);
      let sh;
      try {
        sh = buildShot(this.s, plan2, si, kind, theme);
      } catch {
        try {
          sh = buildShot(this.s, plan2, si, "editorial", theme);
        } catch {
          sh = emptyShot(plan2, si, theme, shotTransition(plan2, si, this.s.seed));
        }
      }
      const half = crossHalf(plan2, si + 1, this.s.seed);
      if (half > 0 && shotTransition(plan2, si + 1, this.s.seed) === "dolly") {
        sh.exitAt = plan2.shots[si + 1].t0 - half;
        sh.exitDur = half * 2;
      }
      this.cache.set(key, sh);
      while (this.cache.size > SHOT_CACHE) {
        const first = this.cache.keys().next().value;
        if (first === void 0) break;
        this.cache.delete(first);
      }
      return sh;
    }
    palette(si, now = performance.now()) {
      const accents = this.shownAccents(now), key = `${this.s.seed}|${accents.join()}`, hit = this.pals.get(si);
      if (hit && hit.key === key) return hit.pal;
      const pal = paletteFor(accents, si, this.s.seed);
      if (this.pals.size > 8) this.pals.clear();
      this.pals.set(si, { key, pal });
      return pal;
    }
    time(now) {
      const c = this.clock;
      const raw = Math.max(0, (c.positionMs + (c.playing ? Math.max(0, now - c.updatedAtMs) * c.playbackRate : 0) + c.offsetMs) / 1e3);
      if (!c.playing || !this.motion || !this.tcValid) {
        this.tc = raw;
        this.tcAt = now;
        this.tcValid = true;
        return raw;
      }
      const dt = Math.min(0.1, Math.max(0, (now - this.tcAt) / 1e3));
      this.tcAt = now;
      const predicted = this.tc + dt * c.playbackRate, err = raw - predicted;
      this.tc = Math.abs(err) > CLOCK_SNAP_S ? raw : predicted + err * (1 - Math.exp(-dt / CLOCK_SLEW_S));
      return this.tc;
    }
    /* ---------- pointer ---------- */
    current() {
      if (!this.plan || this.lastShot < 0) return null;
      return this.cache.get(`${this.lastShot}:${this.kindFor(this.lastShot)}`) ?? null;
    }
    updateHover() {
      const s = this.s, sh = this.current();
      if (!sh || !s.fx.hover || sh.rest) {
        s.hover = null;
        this.host.canvas.style.cursor = "";
        return;
      }
      const b = hitBox(s, sh, toLayer(s, s.ptr.hx, s.ptr.hy));
      s.hover = b ? { b, sh } : null;
      this.host.canvas.style.cursor = b && this.seekEnabled ? "pointer" : "";
    }
    click(x, y) {
      const s = this.s, sh = this.current();
      if (!sh || sh.rest || !s.fx.hover) return;
      const P = this.palette(sh.si), L = toLayer(s, x, y), b = hitBox(s, sh, L), now = performance.now();
      this.wakeFor(ACTIVE_AFTER_POINTER_MS);
      const burst = (px, py, col) => {
        if (s.bursts.length < 6) s.bursts.push({ x: px, y: py, t0: now, seed: `b${Math.floor(now)}`, col });
      };
      if (sh.pend) {
        const it = pendHit(s, sh, L);
        if (it) it.ev += (L.x > it.ax ? -1 : 1) * 7;
        burst(L.x, L.y, P.acc2);
        return;
      }
      const hero = sh.boxes[sh.hero];
      if (sh.shatter && hero && Math.abs(L.x - hero.x) < sh.shatter.bx * 0.75 && Math.abs(L.y - hero.y) < sh.shatter.by * 0.75) {
        shatterHit(s, sh, L);
        return;
      }
      if (sh.swarm) {
        swarmBurst(s, sh, L);
        burst(L.x, L.y, P.acc2);
        return;
      }
      if (b) {
        if (this.seekEnabled) this.host.onSeek(Math.max(sh.def.t0, b.seg.start - 0.05) * 1e3);
        burst(b.x, b.y, P.acc);
      } else burst(L.x, L.y, P.acc2);
    }
    /* ---------- drawing ---------- */
    frame(ts, elapsedMs) {
      const plan2 = this.plan, s = this.s;
      if (this.disposed || !plan2 || !plan2.shots.length || s.W < 2) return;
      const now = performance.now();
      if (!this.clock.playing && now > this.activeUntil) return;
      const dt = Math.min(0.1, Math.max(1e-3, elapsedMs / 1e3));
      const started = performance.now();
      const frameAt = ts > 0 && Math.abs(now - ts) < 50 ? ts : now;
      try {
        this.render(plan2, dt, frameAt);
      } catch (error) {
        if (typeof console !== "undefined") console.warn("Blossom frame failed", error);
      }
      if (this.clock.playing) this.pace(elapsedMs, performance.now() - started, now);
    }
    /**
     * Watch whether frames arrive on time. Late frames (the display cadence or the draw itself) step the render scale down;
     * a long run of on-time frames steps it back up. Idle or throttled periods are not counted either way.
     */
    pace(elapsedMs, drawMs, now) {
      if (!this.motion || elapsedMs > 400) {
        this.lateMs = 0;
        this.onTimeMs = 0;
        return;
      }
      const late = elapsedMs > this.targetMs * 1.45 || drawMs > this.targetMs * 0.85;
      const early = elapsedMs < this.targetMs * 1.15 && drawMs < this.targetMs * 0.5;
      this.lateMs = late ? this.lateMs + elapsedMs : Math.max(0, this.lateMs - elapsedMs);
      this.onTimeMs = early ? this.onTimeMs + elapsedMs : 0;
      if (now - this.lastScaleChangeAt < 2e3) return;
      if (this.lateMs > SCALE_SLOW_AFTER_MS && this.scale > SCALE_MIN) this.setScale(Math.max(SCALE_MIN, this.scale - SCALE_STEP_DOWN), now);
      else if (this.onTimeMs > SCALE_FAST_AFTER_MS && this.scale < 1) this.setScale(Math.min(1, this.scale + SCALE_STEP_UP), now);
    }
    setScale(scale, now) {
      this.scale = Math.round(scale * 100) / 100;
      this.lastScaleChangeAt = now;
      this.lateMs = 0;
      this.onTimeMs = 0;
      if (this.cssW) {
        this.applyBacking(this.cssW, this.cssH);
        this.requestFrame();
      }
    }
    render(plan2, dt, now) {
      const s = this.s, t = this.time(now), si = shotIndexAt(plan2, t);
      this.frameNo++;
      s.t = t;
      s.now = now;
      s.ptr.sx += (s.ptr.x - s.ptr.sx) * Math.min(1, dt * 5);
      s.ptr.sy += (s.ptr.y - s.ptr.sy) * Math.min(1, dt * 5);
      if ((this.prefs.palette === "theme" || this.prefs.palette === "cover" && !this.coverAccents) && now - this.accentReadAt > 2e3) {
        this.accentReadAt = now;
        const hex = readThemeAccent(s.m, this.host.canvas);
        if (hex !== this.themeAccent) {
          this.themeAccent = hex;
          this.setAccents(this.pickAccents());
        }
      }
      const cross = this.motion ? crossingAt(plan2, t, si, s.seed) : null;
      const primary = cross ? cross.b : si, shB = this.shot(primary), g = this.main;
      if (primary !== this.lastShot) this.enterShot(shB);
      g.setTransform(1, 0, 0, 1, 0, 0);
      g.globalAlpha = 1;
      g.filter = "none";
      g.globalCompositeOperation = "source-over";
      g.clearRect(0, 0, g.canvas.width, g.canvas.height);
      if (!cross) {
        if (now - this.lastMoveAt > SPARE_IDLE_MS) {
          this.backdrops.releaseSecond();
          this.releaseBlur();
        }
        this.warmNext(plan2, primary, now);
        const info = this.drawShot(plan2, shB, dt, now, 0, "all", IDENTITY, IDENTITY, 1);
        this.drawOverlay(plan2, shB, info, letterbox(shB, t));
        return;
      }
      this.lastMoveAt = now;
      const shA = this.shot(cross.a), to = movePlacement(cross.kind, s.W, s.H), views = moveViews(to, cross.e);
      const cover = 1 + 0.2 * Math.sin(Math.PI * cross.p);
      const odd = this.frameNo % 2 === 1;
      this.drawShot(plan2, shA, dt, now, 0, "bg", views.out, backgroundView(views.out, BG_DEPTH, cover), 1, odd);
      this.drawShot(plan2, shB, dt, now, 1, "bg", views.in, backgroundView(views.in, BG_DEPTH, cover), travel((cross.p - 0.08) / 0.84), !odd);
      const infoA = this.drawShot(plan2, shA, dt, now, 0, "type", views.out, IDENTITY, 1);
      const infoB = this.drawShot(plan2, shB, dt, now, 1, "type", views.in, IDENTITY, 1);
      this.smear(to, cross, dt);
      const lead = cross.e < 0.5;
      this.drawOverlay(plan2, lead ? shA : shB, lead ? infoA : infoB, letterbox(shA, t) * (1 - cross.e) + letterbox(shB, t) * cross.e);
    }
    /**
     * Motion blur for the fast middle of a move: the finished frame is laid back over itself, faintly, at five points along the way
     * it travelled during the last frame, so the streak is continuous rather than a row of copies. It only happens while the camera
     * is really travelling.
     */
    smear(to, cross, dt) {
      const s = this.s, g = this.main, step = dt / (2 * cross.half);
      const now = moveViews(to, cross.e).out, before = moveViews(to, travel(clamp01(cross.p - step))).out;
      const back = compose(before, invert(now)), speed = Math.hypot(back.x, back.y) + Math.abs(back.r) * s.W * 0.5 + Math.abs(Math.log(back.z)) * s.W * 0.5;
      const a = clamp01((speed - 5) / 45) * 0.42;
      if (a < 0.02) return;
      const src = g.canvas, w = Math.max(2, Math.round(src.width / 2)), h = Math.max(2, Math.round(src.height / 2));
      if (!this.blur) {
        this.blur = document.createElement("canvas");
      }
      if (this.blur.width !== w || this.blur.height !== h) {
        this.blur.width = w;
        this.blur.height = h;
      }
      const b = this.blur.getContext("2d");
      if (!b) return;
      b.globalCompositeOperation = "copy";
      b.drawImage(src, 0, 0, w, h);
      g.save();
      g.imageSmoothingEnabled = true;
      for (const [f, weight] of SMEAR_TAPS) {
        g.setTransform(s.DPR, 0, 0, s.DPR, 0, 0);
        applyView(g, partway(back, f), s.W, s.H);
        g.globalAlpha = a * weight;
        g.drawImage(this.blur, 0, 0, s.W, s.H);
      }
      g.restore();
    }
    /**
     * A little before a move, build the next shot, bake its ghost words and paint its backdrop once. Those first-use costs are
     * paid here, in a frame of their own, instead of in the middle of the move.
     */
    warmNext(plan2, si, now) {
      const next = si + 1;
      if (!this.motion || !this.clock.playing || next >= plan2.shots.length) return;
      if (this.warmed !== next) {
        const ahead = plan2.shots[next].t0 - crossHalf(plan2, next, this.s.seed) - this.s.t;
        if (ahead > WARM_AHEAD_S || ahead < -0.05) return;
        this.warmed = next;
        this.warmStep = 0;
      }
      if (this.warmStep > 2) return;
      const shN = this.shot(next);
      if (this.warmStep === 1) bakeDeco(this.s, shN, this.palette(next, now).acc);
      if (this.warmStep === 2 && !this.host.transparent) this.paintBackdrop(plan2, shN, this.palette(next, now), 0, 0, 1, 0, 0, 1);
      this.warmStep++;
    }
    /**
     * One shot onto the stage. `part` picks its backdrop ('bg'), its type ('type') or both ('all'); `view` places the type and
     * `bgView` the backdrop where the camera currently sees them, and the backdrop is laid down at `bgAlpha`. `slot` picks the
     * backdrop canvas: 0 for the shot being shown (and, during a move, the outgoing one), 1 for the incoming or pre-warmed one;
     * `reuseBg` lets this frame show that canvas's last picture instead of repainting it.
     */
    drawShot(plan2, sh, dt, now, slot, part, view, bgView, bgAlpha, reuseBg = false) {
      const s = this.s, g = this.main, t = s.t, { W, H } = s, P = this.palette(sh.si, now), acc = P.acc, lin = shotProgress(sh, t), motion = this.motion, chorus = sh.def.chorus;
      let onset = -1e9;
      for (const b of sh.boxes) if (b.seg.start <= t && b.seg.start > onset) onset = b.seg.start;
      const age = t - onset, bt = motion && age >= 0 ? Math.min(1, 1.65 * (1 - Math.exp(-age / 0.04)) * Math.exp(-age * 4.5)) : 0;
      const heroBox = sh.boxes[sh.hero], ha = heroBox ? t - heroBox.seg.start : -1;
      const hit = motion && ha >= 0 ? 1.4 * (1 - Math.exp(-ha / 0.035)) * Math.exp(-ha * 3.2) : 0;
      s.g = g;
      const k = motion ? s.intensity : 0, c0 = camFrame(sh.kind, cameraProgress(sh, t, crossHalf(plan2, sh.si, s.seed))), cf = { x: c0.x * k, y: c0.y * k, s: 1 + (c0.s - 1) * k, r: c0.r * k };
      const bw = clamp01(easeIO((t - sh.reveal) / 1.2)), br = breath(t, sh.si * 1.7);
      const punch = ((chorus ? 0.012 : 6e-3) * bt + 0.024 * hit) * s.intensity;
      const par = s.fx.parallax && motion ? { x: -(s.ptr.sx - 0.5) * W * 0.03, y: -(s.ptr.sy - 0.5) * H * 0.03, r: (s.ptr.sx - 0.5) * 0.012 } : { x: 0, y: 0, r: 0 };
      const trk = motion && !sh.rest ? trackPoint(sh, t) : { x: 0, y: 0 };
      const lean = TRACK_GAIN * s.intensity, limX = TRACK_LIMIT * W, limY = TRACK_LIMIT * H;
      const trkX = Math.max(-limX, Math.min(limX, -trk.x * lean)), trkY = Math.max(-limY, Math.min(limY, -trk.y * lean));
      const cx = cf.x + br.x * bw * (motion ? 1 : 0), cy = cf.y + br.y * bw * (motion ? 1 : 0) + trkY / H;
      const cs = cf.s + br.s * bw * (motion ? 1 : 0) + punch, cr = cf.r + br.r * bw * (motion ? 1 : 0) + par.r;
      const panX = sh.kind === "ribbon" && !sh.rest ? -focusX(sh, t) : trkX;
      s.cam = { tx: W / 2 + cx * W + panX + par.x, ty: H / 2 + cy * H + par.y, cr, cs };
      const info = { bt, lin, P };
      if (part !== "type" && !this.host.transparent && bgAlpha > 3e-3) {
        g.setTransform(s.DPR, 0, 0, s.DPR, 0, 0);
        g.globalAlpha = 1;
        g.globalCompositeOperation = "source-over";
        applyView(g, bgView, W, H);
        this.paintBackdrop(plan2, sh, P, Math.max(bt, hit) * 0.5, lin, slot, (cx * W + panX + par.x) * 0.3, (cy * H + par.y) * 0.3, 1 + (cs - 1) * 0.3, bgAlpha, reuseBg);
      }
      if (part === "bg") return info;
      if (sh.pend) pendStep(s, sh, dt);
      if (motion) stepGlyphPhysics(s, sh, dt);
      g.setTransform(s.DPR, 0, 0, s.DPR, 0, 0);
      g.globalAlpha = 1;
      g.globalCompositeOperation = "source-over";
      applyView(g, view, W, H);
      g.save();
      g.translate(s.cam.tx, s.cam.ty);
      g.rotate(cr);
      g.scale(cs, cs);
      g.translate(-W / 2, -H / 2);
      s.fade = sh.exitAt !== void 0 && sh.exitDur ? 1 - clamp01((t - sh.exitAt) / (sh.exitDur * 0.6)) : 1;
      if (!sh.rest) {
        if (!this.host.transparent && s.fx.background && motion) drawSpot(s, sh, { acc, a2: P.acc2, a3: P.acc3 }, bt, hit);
        if (s.fx.ghost) sh.deco.forEach((d) => drawDeco(s, d, acc, cx, cy, panX + par.x));
        if (!this.host.transparent && s.fx.background && sh.kind === "orbit") drawOrbitRings(s, P);
        if (s.fx.icons && sh.icon) drawIcon(s, sh, P, acc);
        sh.boxes.forEach((b) => {
          if (b.block) drawBlock(s, b, P, acc);
          if (b.aperture) drawWindow(s, sh, b, P, acc);
        });
        if (s.fx.guide) sh.boxes.forEach((b) => drawGuide(s, b, P, acc));
        if (s.fx.frame) sh.boxes.forEach((b) => drawFrame(s, sh, b, P, acc));
        sh.boxes.forEach((b) => b.glyphs.forEach((gl) => drawGlyph(s, b, gl, sh, P, acc, bt)));
        if (s.fx.highlight && motion) sh.boxes.forEach((b) => drawSing(s, b, sh, acc));
        if (sh.shatter) drawShatter(s, sh, P, acc, bt);
        if (sh.swarm) drawSwarm(s, sh, P, acc, dt, bt);
        if (sh.tunnel) drawTunnel(s, sh, P, acc, bt);
        if (sh.pend) drawPend(s, sh, P, acc, bt);
        drawPins(s, sh, acc);
        if (!s.fx.highlight) sh.boxes.forEach((b) => {
          if (b.isHero) drawImpact(s, b, sh, acc);
        });
        if (s.hover && s.hover.sh === sh && s.fx.hover) drawHover(s, s.hover.b, acc);
        drawBursts(s);
        if (s.fx.hud) drawHUD(s, sh, acc, dt);
      }
      g.restore();
      s.fade = 1;
      if (part === "all" && !this.host.transparent && s.fx.background && sh.theme === 2) drawReflection(s);
      return info;
    }
    /**
     * Backdrop, scenery, mid-ground line-work, bloom and grain on the reduced backdrop canvas, then stretched onto the stage at
     * `alpha` under whatever transform the stage context carries. `ox`/`oy`/`ms` are the scenery's share of the shot's camera.
     * Called with `alpha` 0 it only paints the backdrop canvas (to warm its caches before the shot is needed).
     */
    paintBackdrop(plan2, sh, P, bt, lin, slot, ox, oy, ms, alpha = 0, mayReuse = false) {
      const s = this.s, g = this.main, { W, H } = s, layer = this.backdrops.acquire(slot, W, H, s.DPR);
      if (!layer) return;
      const reuse = mayReuse && layer.si === sh.si && this.frameNo - layer.frame <= 2;
      if (!reuse) {
        layer.si = sh.si;
        layer.frame = this.frameNo;
        const x = layer.x;
        s.g = x;
        x.setTransform(layer.dpr, 0, 0, layer.dpr, 0, 0);
        x.globalAlpha = 1;
        x.filter = "none";
        x.globalCompositeOperation = "source-over";
        drawBackdrop(s, sh, P, bt, lin);
        if (s.fx.background) {
          drawTypeWall(s, plan2, sh, bt, ox, oy);
          x.save();
          x.translate(W / 2 + ox, H / 2 + oy);
          x.scale(ms, ms);
          drawScene(s, sh, P, bt, lin);
          drawMG(s, sh, lin, P.acc, P);
          x.restore();
          drawBloom(s, CLASSIC_THEMES.has(sh.theme) ? 0.15 : 0.28);
        }
        if (s.fx.grain) drawGrain(s, P);
        s.g = g;
      }
      if (alpha <= 0) return;
      g.globalAlpha = Math.min(1, alpha);
      g.imageSmoothingEnabled = true;
      g.drawImage(layer.c, 0, 0, W, H);
      g.globalAlpha = 1;
    }
    /** What sits on top of the finished picture: letterbox bars (`bars`, 0–1), chrome and caption, always on the visible canvas. */
    drawOverlay(plan2, sh, info, bars) {
      const s = this.s, g = this.main;
      s.g = g;
      g.setTransform(s.DPR, 0, 0, s.DPR, 0, 0);
      g.globalAlpha = 1;
      g.filter = "none";
      g.globalCompositeOperation = "source-over";
      if (!this.host.transparent && bars > 2e-3) {
        const h = s.H * LETTERBOX * bars;
        g.fillStyle = LETTERBOX_INK;
        g.globalAlpha = 0.94;
        g.fillRect(0, 0, s.W, h);
        g.fillRect(0, s.H - h, s.W, h);
        g.globalAlpha = 1;
      }
      if (!this.host.transparent && s.fx.chrome) drawFurniture(s, plan2, sh, info.lin, info.P, this.overlayText);
      drawCaption(s, plan2, info.P, this.overlayText);
    }
    enterShot(sh) {
      this.cancelWarmTask();
      const s = this.s, plan2 = this.plan;
      this.lastShot = sh.si;
      s.ret.ready = false;
      s.hover = null;
      s.bursts.length = 0;
      this.host.canvas.style.cursor = "";
      const sw = sh.swarm;
      if (sw) {
        sw.ox.fill(0);
        sw.oy.fill(0);
        sw.vx.fill(0);
        sw.vy.fill(0);
      }
      if (sh.shatter) sh.shatter.ev = [];
      const key = `${sh.si}|${sh.kind}|${this.overrides.has(sh.si)}`;
      if (key !== this.lastInfoKey) {
        this.lastInfoKey = key;
        this.host.onShot({ index: sh.si, count: plan2.shots.length, kind: sh.kind, rest: sh.rest, overridden: this.overrides.has(sh.si) });
      }
      const next = sh.si + 1;
      if (next < plan2.shots.length && !this.cache.has(`${next}:${this.kindFor(next)}`)) {
        const task = { id: 0, idle: typeof window.requestIdleCallback === "function" && typeof window.cancelIdleCallback === "function" };
        this.warmTask = task;
        const warm2 = () => {
          if (this.warmTask !== task) return;
          this.warmTask = null;
          if (!this.disposed && this.plan === plan2 && this.lastShot === sh.si && this.interval !== null) this.shot(next);
        };
        task.id = task.idle ? window.requestIdleCallback(warm2, { timeout: 1200 }) : window.setTimeout(warm2, 400);
      }
    }
  };

  // tools/SourceLyrics/blossom-entry.ts
  var stage;
  var canvas;
  var plan;
  function build(input, width, height) {
    canvas = document.createElement("canvas");
    stage = new BlossomEngine({ canvas, onSeek: (ms) => {
      globalThis.__seek = ms;
    }, onShot: () => {
    } });
    stage.resize(width, height);
    const lyrics = { kind: "synced", source: "local", offsetMs: 0, lines: input.lines };
    plan = createBlossomPlan(lyrics, input.durationMs, input.title, input.artist);
    stage.setPlan(plan);
    stage.setFrameInterval(null);
    return { shots: plan.shots.length, kind: "blossom" };
  }
  function draw(ms, motion, elapsed, playing) {
    const now = globalThis.__now;
    stage.setMotion(motion);
    stage.setClock({ positionMs: ms, updatedAtMs: now, playbackRate: 1, playing, offsetMs: 0 });
    stage.render(plan, Math.min(0.1, Math.max(1e-3, elapsed / 1e3)), now);
    return canvas.nativeId;
  }
  function canvasId() {
    return canvas.nativeId;
  }
  function dispose() {
    stage?.dispose();
    stage = null;
    canvas?.dispose();
    canvas = null;
  }
  function pointer(x, y, press) {
    const p = stage.s.ptr;
    p.hx = x;
    p.hy = y;
    p.x = x / stage.s.W;
    p.y = y / stage.s.H;
    p.over = true;
    if (press) stage.click(x, y);
    else stage.updateHover();
  }
  return __toCommonJS(blossom_entry_exports);
})();
