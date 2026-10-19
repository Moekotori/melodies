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

  // tools/SourceLyrics/cassette-entry.ts
  var cassette_entry_exports = {};
  __export(cassette_entry_exports, {
    build: () => build,
    canvasId: () => canvasId,
    cover: () => cover,
    dispose: () => dispose,
    draw: () => draw,
    pointer: () => pointer
  });

  // ../../../Users/moekotori/echo/src/shared/constants/cassettePreferences.ts
  var defaultCassettePreferences = { timeOfDay: "auto", retro: true, frameCap: 0, seed: 0 };

  // native:frame-clock
  var subscribeVisualFrame = () => () => {
  };

  // native:resource-owner
  var registerLyricsResourceOwner = () => () => {
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/lyricsPlaybackClock.ts
  var wordClockSnapThresholdMs = 320;
  var maxWordClockCorrectionRate = 0.1;
  var stabilizeWordPlaybackPosition = ({
    previous,
    rawPositionMs,
    sampledAtMs,
    playbackRate,
    smoothingEnabled
  }) => {
    if (!previous || !smoothingEnabled) {
      return { positionMs: rawPositionMs, sampledAtMs };
    }
    const elapsedMs = Math.max(0, sampledAtMs - previous.sampledAtMs);
    const mediaElapsedMs = elapsedMs * Math.max(0, playbackRate);
    const expectedPositionMs = previous.positionMs + mediaElapsedMs;
    const correctionMs = rawPositionMs - expectedPositionMs;
    if (Math.abs(correctionMs) >= wordClockSnapThresholdMs) {
      return { positionMs: rawPositionMs, sampledAtMs };
    }
    const correctionLimitMs = mediaElapsedMs * maxWordClockCorrectionRate;
    const boundedCorrectionMs = Math.max(-correctionLimitMs, Math.min(correctionLimitMs, correctionMs));
    return { positionMs: expectedPositionMs + boundedCorrectionMs, sampledAtMs };
  };

  // image:../../../assets/lyrics-cassette/sky-day.webp?no-inline
  var sky_day_default = "sky-day.webp";

  // image:../../../assets/lyrics-cassette/sky-sunset.webp?no-inline
  var sky_sunset_default = "sky-sunset.webp";

  // image:../../../assets/lyrics-cassette/sky-night.webp?no-inline
  var sky_night_default = "sky-night.webp";

  // image:../../../assets/lyrics-cassette/city-day.webp?no-inline
  var city_day_default = "city-day.webp";

  // image:../../../assets/lyrics-cassette/city-sunset.webp?no-inline
  var city_sunset_default = "city-sunset.webp";

  // image:../../../assets/lyrics-cassette/city-night.webp?no-inline
  var city_night_default = "city-night.webp";

  // image:../../../assets/lyrics-cassette/tape-day.webp?no-inline
  var tape_day_default = "tape-day.webp";

  // image:../../../assets/lyrics-cassette/tape-sunset.webp?no-inline
  var tape_sunset_default = "tape-sunset.webp";

  // image:../../../assets/lyrics-cassette/tape-night.webp?no-inline
  var tape_night_default = "tape-night.webp";

  // image:../../../assets/lyrics-cassette/label-day.webp?no-inline
  var label_day_default = "label-day.webp";

  // image:../../../assets/lyrics-cassette/label-sunset.webp?no-inline
  var label_sunset_default = "label-sunset.webp";

  // image:../../../assets/lyrics-cassette/label-night.webp?no-inline
  var label_night_default = "label-night.webp";

  // image:../../../assets/lyrics-cassette/reel-left-day.webp?no-inline
  var reel_left_day_default = "reel-left-day.webp";

  // image:../../../assets/lyrics-cassette/reel-right-day.webp?no-inline
  var reel_right_day_default = "reel-right-day.webp";

  // image:../../../assets/lyrics-cassette/reel-left-sunset.webp?no-inline
  var reel_left_sunset_default = "reel-left-sunset.webp";

  // image:../../../assets/lyrics-cassette/reel-right-sunset.webp?no-inline
  var reel_right_sunset_default = "reel-right-sunset.webp";

  // image:../../../assets/lyrics-cassette/reel-left-night.webp?no-inline
  var reel_left_night_default = "reel-left-night.webp";

  // image:../../../assets/lyrics-cassette/reel-right-night.webp?no-inline
  var reel_right_night_default = "reel-right-night.webp";

  // image:../../../assets/lyrics-cassette/clouds.webp?no-inline
  var clouds_default = "clouds.webp";

  // image:../../../assets/lyrics-cassette/aurora.webp?no-inline
  var aurora_default = "aurora.webp";

  // image:../../../assets/lyrics-cassette/moon.webp?no-inline
  var moon_default = "moon.webp";

  // image:../../../assets/lyrics-cassette/paper-plane.webp?no-inline
  var paper_plane_default = "paper-plane.webp";

  // image:../../../assets/lyrics-cassette/birds.webp?no-inline
  var birds_default = "birds.webp";

  // image:../../../assets/lyrics-cassette/star-cross.webp?no-inline
  var star_cross_default = "star-cross.webp";

  // image:../../../assets/lyrics-cassette/star-glow.webp?no-inline
  var star_glow_default = "star-glow.webp";

  // image:../../../assets/lyrics-cassette/flare-ring.webp?no-inline
  var flare_ring_default = "flare-ring.webp";

  // image:../../../assets/lyrics-cassette/flare-halo.webp?no-inline
  var flare_halo_default = "flare-halo.webp";

  // image:../../../assets/lyrics-cassette/flare-dot.webp?no-inline
  var flare_dot_default = "flare-dot.webp";

  // image:../../../assets/lyrics-cassette/street-light.webp?no-inline
  var street_light_default = "street-light.webp";

  // image:../../../assets/lyrics-cassette/water-normal.webp?no-inline
  var water_normal_default = "water-normal.webp";

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/cassette/cassetteAssets.ts
  var cassetteAssetUrls = {
    skyDay: sky_day_default,
    skySunset: sky_sunset_default,
    skyNight: sky_night_default,
    cityDay: city_day_default,
    citySunset: city_sunset_default,
    cityNight: city_night_default,
    tapeDay: tape_day_default,
    tapeSunset: tape_sunset_default,
    tapeNight: tape_night_default,
    labelDay: label_day_default,
    labelSunset: label_sunset_default,
    labelNight: label_night_default,
    reelLeftDay: reel_left_day_default,
    reelRightDay: reel_right_day_default,
    reelLeftSunset: reel_left_sunset_default,
    reelRightSunset: reel_right_sunset_default,
    reelLeftNight: reel_left_night_default,
    reelRightNight: reel_right_night_default,
    clouds: clouds_default,
    aurora: aurora_default,
    moon: moon_default,
    paperPlane: paper_plane_default,
    birds: birds_default,
    starCross: star_cross_default,
    starGlow: star_glow_default,
    flareRing: flare_ring_default,
    flareHalo: flare_halo_default,
    flareDot: flare_dot_default,
    streetLight: street_light_default,
    waterNormal: water_normal_default
  };
  var cassetteRepeatingAssets = /* @__PURE__ */ new Set(["skyDay", "skySunset", "skyNight", "clouds", "waterNormal"]);
  var cassettePhaseAssets = {
    day: {
      essential: ["skyDay", "cityDay", "tapeDay", "labelDay", "reelLeftDay", "reelRightDay"],
      extra: ["paperPlane", "birds", "flareRing", "flareHalo", "flareDot"]
    },
    sunset: {
      essential: ["skySunset", "citySunset", "tapeSunset", "labelSunset", "reelLeftSunset", "reelRightSunset"],
      extra: ["clouds", "moon", "birds", "streetLight"]
    },
    night: {
      essential: ["skyNight", "cityNight", "tapeNight", "labelNight", "reelLeftNight", "reelRightNight"],
      extra: ["clouds", "aurora", "starCross", "starGlow"]
    }
  };
  var cassetteSharedAssets = ["waterNormal"];
  var loadCassetteImage = (url, signal) => new Promise((resolve, reject) => {
    if (signal.aborted) {
      reject(new DOMException("aborted", "AbortError"));
      return;
    }
    const image = new Image();
    image.decoding = "async";
    const abort = () => {
      image.src = "";
      reject(new DOMException("aborted", "AbortError"));
    };
    signal.addEventListener("abort", abort, { once: true });
    image.onload = () => {
      void image.decode().catch(() => void 0).then(() => {
        signal.removeEventListener("abort", abort);
        if (signal.aborted) return;
        resolve(image);
      });
    };
    image.onerror = () => {
      signal.removeEventListener("abort", abort);
      reject(new Error("cassette asset failed to load"));
    };
    image.src = url;
  });

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/cassette/cassetteGl.ts
  var cassetteInstanceFloats = 17;
  var writeCassetteInstance = (data, index, cx, cy, w, h, rotation, u0, v0, u1, v1, r, g, b, a, p0, p1, p2, p3) => {
    const o = index * cassetteInstanceFloats;
    data[o] = cx;
    data[o + 1] = cy;
    data[o + 2] = w;
    data[o + 3] = h;
    data[o + 4] = rotation;
    data[o + 5] = u0;
    data[o + 6] = v0;
    data[o + 7] = u1;
    data[o + 8] = v1;
    data[o + 9] = r;
    data[o + 10] = g;
    data[o + 11] = b;
    data[o + 12] = a;
    data[o + 13] = p0;
    data[o + 14] = p1;
    data[o + 15] = p2;
    data[o + 16] = p3;
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/cassette/cassetteLyricsTimeline.ts
  var cassetteEnterLeadMs = 280;
  var cassetteEnterMs = 760;
  var cassetteScatterMs = 2100;
  var maxHoldMs = 12e3;
  var restHoldMs = 900;
  var cassetteMaxVisibleLines = 4;
  var createCassetteTimeline = (lines, durationMs) => {
    const timed = lines.map((line, index) => ({ line, index })).filter(({ line }) => Number.isFinite(line.timeMs) && /\S/.test(line.text)).sort((a, b) => a.line.timeMs - b.line.timeMs);
    const songEnd = Number.isFinite(durationMs) && durationMs > 0 ? durationMs : Number.POSITIVE_INFINITY;
    return timed.map(({ line, index }, order) => {
      const startMs = line.timeMs;
      const nextStart = timed[order + 1]?.line.timeMs ?? Number.POSITIVE_INFINITY;
      const wordEnd = (line.words ?? []).reduce((end, word) => typeof word.endMs === "number" && Number.isFinite(word.endMs) ? Math.max(end, word.endMs) : end, startMs);
      const endMs = typeof line.endMs === "number" && Number.isFinite(line.endMs) && line.endMs > startMs ? line.endMs : wordEnd;
      const ownEnd = endMs > startMs ? endMs + restHoldMs : Number.POSITIVE_INFINITY;
      const timedEnd = Math.min(nextStart, ownEnd, Math.max(startMs + 1200, songEnd));
      const scatterAtMs = Number.isFinite(timedEnd) ? timedEnd : startMs + maxHoldMs;
      return { index, line, startMs, scatterAtMs: Math.max(startMs + 400, scatterAtMs) };
    });
  };
  var lastStartedAt = (timeline, timeMs) => {
    let low = 0;
    let high = timeline.length - 1;
    let found = -1;
    while (low <= high) {
      const mid = low + high >> 1;
      if (timeline[mid].startMs <= timeMs) {
        found = mid;
        low = mid + 1;
      } else high = mid - 1;
    }
    return found;
  };
  var visibleCassetteLines = (timeline, timeMs) => {
    if (timeline.length === 0 || !Number.isFinite(timeMs)) return [];
    const last = lastStartedAt(timeline, timeMs + cassetteEnterLeadMs);
    const out = [];
    for (let order = last; order >= 0 && out.length < cassetteMaxVisibleLines; order--) {
      const entry = timeline[order];
      const scatterAgeMs = timeMs - entry.scatterAtMs;
      if (scatterAgeMs >= cassetteScatterMs) break;
      out.push({ ...entry, ageMs: timeMs - entry.startMs, scatterAgeMs });
    }
    return out.reverse();
  };
  var cassetteCharProgress = (line, text, timeMs, out) => {
    const length = text.length;
    const words = line.words?.filter((word) => Number.isFinite(word.startMs) && word.text.length > 0) ?? [];
    if (words.length === 0) {
      const sweep = (timeMs - line.timeMs) / 380 * Math.max(1, length);
      for (let i = 0; i < length; i++) out[i] = Math.min(1, Math.max(0, sweep - i));
      return out;
    }
    let cursor = 0;
    for (const word of words) {
      let at = text.indexOf(word.text, cursor);
      if (at < 0) at = Math.min(length, cursor);
      const end = typeof word.endMs === "number" && word.endMs > word.startMs ? word.endMs : word.startMs + 320;
      const progress = Math.min(1, Math.max(0, (timeMs - word.startMs) / (end - word.startMs)));
      const span = Math.max(1, Math.min(word.text.length, length - at));
      for (let i = cursor; i < at; i++) out[i] = timeMs >= word.startMs ? 1 : 0;
      for (let i = 0; i < span && at + i < length; i++) out[at + i] = Math.min(1, Math.max(0, progress * span - i));
      cursor = Math.min(length, at + span);
    }
    const lastWord = words[words.length - 1];
    for (let i = cursor; i < length; i++) out[i] = timeMs >= (lastWord.endMs ?? lastWord.startMs) ? 1 : 0;
    return out;
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/cassette/cassetteStageLayout.ts
  var cassetteScene = { width: 3840, height: 2160 };
  var cassetteTape = { width: 2128, height: 1495 };
  var tapeOrigin = { x: 1175, y: 665 };
  var leftCropShare = 0.2;
  var clamp = (value, min, max) => Math.min(max, Math.max(min, value));
  var resolveCassetteStageLayout = (width, height, topInset = 48) => {
    const w = Math.max(1, width);
    const h = Math.max(1, height);
    const sceneScale = Math.max(w / cassetteScene.width, h / cassetteScene.height);
    const sceneX = (w - cassetteScene.width * sceneScale) * leftCropShare;
    const sceneY = h - cassetteScene.height * sceneScale;
    const tapeScale = sceneScale;
    const tapeHeight = cassetteTape.height * tapeScale;
    const tapeWidth = cassetteTape.width * tapeScale;
    const tapeX = sceneX + tapeOrigin.x * sceneScale;
    const tapeY = sceneY + tapeOrigin.y * sceneScale;
    const margin = clamp(w * 0.05, 18, 72);
    const top = topInset + h * 0.05;
    const zones = [];
    const skyBottom = tapeY + tapeHeight * 0.06;
    const hasSky = skyBottom - top > h * 0.14;
    const leftEdge = tapeX + tapeWidth * 0.03 - w * 0.015;
    const leftTop = hasSky ? skyBottom : top;
    if (leftEdge - margin > w * 0.22 && h * 0.86 - leftTop > h * 0.16) zones.push({ x0: margin, y0: leftTop, x1: leftEdge, y1: h * 0.86 });
    if (hasSky) zones.push({ x0: margin, y0: top, x1: w - margin, y1: skyBottom });
    if (zones.length === 0) zones.push({ x0: margin, y0: top, x1: w - margin, y1: h * 0.5 });
    return {
      width: w,
      height: h,
      scene: { x: sceneX, y: sceneY, scale: sceneScale },
      tape: { x: tapeX, y: tapeY, scale: tapeScale },
      zones,
      fontPx: Math.round(clamp(Math.min(w, h) * 0.052, 22, 60))
    };
  };
  var cassetteHash = (seed, index, salt) => {
    let x = seed * 374761393 + index * 668265263 + salt * 2246822519 | 0;
    x = Math.imul(x ^ x >>> 13, 1274126177);
    x ^= x >>> 16;
    return (x >>> 0) / 4294967296;
  };
  var cassetteAnchorCells = (layout) => {
    const cells = [];
    for (const zone of layout.zones) {
      const zw = zone.x1 - zone.x0;
      const zh = zone.y1 - zone.y0;
      const cols = clamp(Math.round(zw / Math.max(260, layout.fontPx * 7)), 1, 3);
      const rows = clamp(Math.round(zh / Math.max(120, layout.fontPx * 3.2)), 1, 4);
      for (let row = 0; row < rows; row++) {
        for (let col = 0; col < cols; col++) {
          cells.push({ x0: zone.x0 + zw * col / cols, y0: zone.y0 + zh * row / rows, x1: zone.x0 + zw * (col + 1) / cols, y1: zone.y0 + zh * (row + 1) / rows });
        }
      }
    }
    return cells;
  };
  var pickCassetteCell = (cellCount, seed, lineIndex, avoid) => {
    if (cellCount <= 1) return 0;
    for (let attempt = 0; attempt < 8; attempt++) {
      const cell = Math.floor(cassetteHash(seed, lineIndex, 11 + attempt) * cellCount);
      if (!avoid.includes(cell)) return cell;
    }
    for (let cell = 0; cell < cellCount; cell++) if (!avoid.includes(cell)) return cell;
    return 0;
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/cassette/cassetteGlyphMotion.ts
  var easeOutCubic = (t) => 1 - (1 - t) ** 3;
  var easeOutBack = (t) => {
    const c = 1.35;
    return 1 + (c + 1) * (t - 1) ** 3 + c * (t - 1) ** 2;
  };
  var clamp01 = (t) => Math.min(1, Math.max(0, t));
  var cassetteGlyphPose = (glyph, glyphCount, lineSeed, ageMs, scatterAgeMs, fontPx, timeSec, motion, out) => {
    const h1 = cassetteHash(lineSeed, glyph, 1);
    const h2 = cassetteHash(lineSeed, glyph, 2);
    const h3 = cassetteHash(lineSeed, glyph, 3);
    const h4 = cassetteHash(lineSeed, glyph, 4);
    const stagger = Math.min(48, 520 / Math.max(1, glyphCount));
    const enter = clamp01((ageMs + cassetteEnterLeadMs - glyph * stagger) / cassetteEnterMs);
    const rise = easeOutBack(enter);
    out.alpha = clamp01(enter * 1.8);
    out.dx = 0;
    out.dy = (1 - rise) * fontPx * 0.95;
    out.scale = 0.8 + 0.2 * easeOutCubic(enter);
    out.rotation = (1 - easeOutCubic(enter)) * (h1 - 0.5) * 0.6;
    out.wobble = 0.18 + (1 - enter) * 1.2;
    out.dissolve = 0;
    if (!motion) {
      out.dy = 0;
      out.rotation = 0;
      out.scale = 1;
      out.wobble = 0.08;
      out.alpha = clamp01((ageMs + cassetteEnterLeadMs) / 240) * (scatterAgeMs > 0 ? clamp01(1 - scatterAgeMs / 420) : 1);
      return out;
    }
    out.dx += Math.cos(timeSec * 0.9 + glyph * 0.7 + h2 * 3) * fontPx * 0.022;
    out.dy += Math.sin(timeSec * 1.25 - glyph * 0.55) * fontPx * 0.05;
    out.rotation += Math.sin(timeSec * 0.8 + glyph * 0.9) * 0.025;
    if (scatterAgeMs > 0) {
      const order = h4 < 0.5 ? glyph : glyphCount - 1 - glyph;
      const q = clamp01((scatterAgeMs - order * Math.min(36, 420 / Math.max(1, glyphCount))) / (cassetteScatterMs * 0.82));
      const heading = -Math.PI / 2 + 0.4 + (h2 - 0.5) * 1.4;
      const travel = fontPx * (2.2 + h3 * 3.4) * easeOutCubic(q);
      out.dx += Math.cos(heading) * travel + q * q * fontPx * 1.4;
      out.dy += Math.sin(heading) * travel;
      out.rotation += (h1 - 0.5) * 1.4 * q;
      out.scale *= 1 + 0.22 * q;
      out.alpha *= (1 - q) ** 1.3;
      out.wobble += q * 1.3;
      out.dissolve = clamp01(q * 1.15 - 0.1);
    }
    return out;
  };

  // native:font-list
  var serializeFontList = (s) => s;

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/cassette/cassetteLineRaster.ts
  var cassetteLyricsFont = '"Yu Gothic UI", "Microsoft YaHei UI", "PingFang SC", "Hiragino Sans", "Noto Sans CJK SC", "Malgun Gothic", system-ui, sans-serif';
  var atlasMaxWidth = 2048;
  var maxGlyphs = 160;
  var createSegmenter = () => {
    const Ctor = Intl.Segmenter;
    return Ctor ? new Ctor(void 0, { granularity: "grapheme" }) : null;
  };
  var segmenter;
  var graphemes = (text) => {
    if (segmenter === void 0) segmenter = createSegmenter();
    if (segmenter) return [...segmenter.segment(text)].map((part) => ({ text: part.segment, index: part.index }));
    return Array.from(text).reduce((parts, ch) => {
      const index = parts.length ? parts[parts.length - 1].index + parts[parts.length - 1].text.length : 0;
      parts.push({ text: ch, index });
      return parts;
    }, []);
  };
  var wrapRows = (ctx, text, maxWidth, maxRows) => {
    if (ctx.measureText(text).width <= maxWidth) return [text];
    const spaced = /\s/.test(text.trim()) && !/[぀-ヿ㐀-鿿가-힯]/.test(text);
    const units = spaced ? text.split(/(\s+)/).filter(Boolean) : graphemes(text).map((part) => part.text);
    const rows = [];
    let row = "";
    const target = Math.min(maxWidth, ctx.measureText(text).width / Math.min(maxRows, Math.ceil(ctx.measureText(text).width / maxWidth)) * 1.08);
    for (const unit of units) {
      const next = row + unit;
      if (row.trim() && ctx.measureText(next).width > target && rows.length < maxRows - 1) {
        rows.push(row.trimEnd());
        row = unit.trimStart();
      } else row = next;
    }
    if (row.trim()) rows.push(row.trim());
    return rows;
  };
  var rasterCassetteLine = (text, translation, fontPx, pixelRatio, maxWidth, fontStack = cassetteLyricsFont, subScale = 1) => {
    const canvas2 = document.createElement("canvas");
    const ctx = canvas2.getContext("2d", { willReadFrequently: false });
    if (!ctx) return null;
    const mainText = text.trim().slice(0, 240);
    const subText = translation?.trim().slice(0, 240) || null;
    let size = fontPx;
    const fontFor = (px, weight) => `${weight} ${px}px ${fontStack}`;
    ctx.font = fontFor(size, 700);
    while (size > 16 && wrapRows(ctx, mainText, maxWidth, 3).some((row) => ctx.measureText(row).width > maxWidth)) {
      size = Math.round(size * 0.9);
      ctx.font = fontFor(size, 700);
    }
    const mainRows = wrapRows(ctx, mainText, maxWidth, 3);
    const subSize = Math.max(12, Math.round(size * 0.5 * subScale));
    ctx.font = fontFor(subSize, 600);
    const subRows = subText ? wrapRows(ctx, subText, maxWidth, 2) : [];
    const pending = [];
    let lineWidth = 0;
    const lineGap = size * 1.24;
    const subGap = subSize * 1.35;
    const totalHeight = mainRows.length * lineGap + (subRows.length ? subRows.length * subGap + size * 0.2 : 0);
    let cursorY = -totalHeight / 2;
    let charBase = 0;
    const layoutRows = (rows, sub, px, gap) => {
      ctx.font = fontFor(px, sub ? 600 : 700);
      for (const row of rows) {
        const rowWidth = ctx.measureText(row).width;
        lineWidth = Math.max(lineWidth, rowWidth);
        const parts = graphemes(row);
        for (const part of parts) {
          if (!/\S/.test(part.text)) continue;
          const before = ctx.measureText(row.slice(0, part.index)).width;
          const w = ctx.measureText(part.text).width;
          const sourceIndex = sub ? -1 : mainText.indexOf(row, charBase) + part.index;
          pending.push({ ch: part.text, charIndex: sub ? -1 : Math.max(0, sourceIndex), sub, px, x: -rowWidth / 2 + before + w / 2, y: cursorY + gap / 2, w });
          if (pending.length >= maxGlyphs) return;
        }
        if (!sub) charBase = Math.max(charBase, mainText.indexOf(row, charBase) + row.length);
        cursorY += gap;
      }
    };
    layoutRows(mainRows, false, size, lineGap);
    if (subRows.length) {
      cursorY += size * 0.2;
      layoutRows(subRows, true, subSize, subGap);
    }
    if (pending.length === 0) return null;
    const scale = pixelRatio;
    const pad = Math.ceil(size * 0.34 * scale);
    const cellHeight = Math.ceil(size * 1.3 * scale) + pad * 2;
    let x = 0;
    let y = 0;
    let rowHeight = 0;
    let atlasWidth = 0;
    const cells = pending.map((glyph) => {
      const cw = Math.ceil(Math.max(glyph.w, glyph.px * 0.3) * scale) + pad * 2;
      const ch = glyph.sub ? Math.ceil(glyph.px * 1.3 * scale) + pad * 2 : cellHeight;
      if (x + cw > atlasMaxWidth) {
        x = 0;
        y += rowHeight;
        rowHeight = 0;
      }
      const cell = { x, y, w: cw, h: ch };
      x += cw;
      rowHeight = Math.max(rowHeight, ch);
      atlasWidth = Math.max(atlasWidth, x);
      return cell;
    });
    const atlasHeight = y + rowHeight;
    canvas2.width = Math.max(1, atlasWidth);
    canvas2.height = Math.max(1, atlasHeight);
    ctx.fillStyle = "#000";
    ctx.fillRect(0, 0, canvas2.width, canvas2.height);
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";
    ctx.globalCompositeOperation = "lighter";
    const offscreen = canvas2.width + canvas2.height + 64;
    pending.forEach((glyph, index) => {
      const cell = cells[index];
      ctx.font = fontFor(glyph.px * scale, glyph.sub ? 600 : 700);
      ctx.shadowColor = "rgb(0,255,0)";
      ctx.shadowBlur = glyph.px * scale * 0.3;
      ctx.shadowOffsetX = offscreen;
      ctx.fillStyle = "rgb(0,255,0)";
      ctx.save();
      ctx.beginPath();
      ctx.rect(cell.x, cell.y, cell.w, cell.h);
      ctx.clip();
      ctx.fillText(glyph.ch, cell.x + cell.w / 2 - offscreen, cell.y + cell.h / 2);
      ctx.fillText(glyph.ch, cell.x + cell.w / 2 - offscreen, cell.y + cell.h / 2);
      ctx.restore();
    });
    ctx.shadowColor = "transparent";
    ctx.shadowBlur = 0;
    ctx.shadowOffsetX = 0;
    ctx.fillStyle = "rgb(255,0,0)";
    pending.forEach((glyph, index) => {
      const cell = cells[index];
      ctx.font = fontFor(glyph.px * scale, glyph.sub ? 600 : 700);
      ctx.fillText(glyph.ch, cell.x + cell.w / 2, cell.y + cell.h / 2);
    });
    const glyphs = pending.map((glyph, index) => {
      const cell = cells[index];
      return {
        charIndex: glyph.charIndex,
        sub: glyph.sub,
        u0: cell.x / canvas2.width,
        v0: cell.y / canvas2.height,
        u1: (cell.x + cell.w) / canvas2.width,
        v1: (cell.y + cell.h) / canvas2.height,
        restX: glyph.x,
        restY: glyph.y,
        width: cell.w / scale,
        height: cell.h / scale
      };
    });
    return { canvas: canvas2, glyphs, width: lineWidth, height: totalHeight, text: mainText };
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/cassette/cassetteParticles.ts
  var random = (min, max) => min + Math.random() * (max - min);
  var pool = (count, make) => Array.from({ length: count }, make);
  var blank = () => ({ x: 0, y: 0, vx: 0, vy: 0, age: 0, life: 1, size: 1, seed: 0, alive: false });
  var lifeFade = (p) => {
    const t = p.age / p.life;
    return Math.min(1, t / 0.1) * Math.min(1, (1 - t) / 0.1);
  };
  var CassetteParticles = class {
    crossStars = pool(18, blank);
    glowStars = pool(6, blank);
    dust = pool(46, blank);
    meteors = pool(6, blank);
    firefliesFar = pool(10, blank);
    firefliesNear = pool(10, blank);
    birds = pool(4, () => ({ ...blank(), frameOffset: 0, dy: 0 }));
    lists = [this.crossStars, this.glowStars, this.dust, this.meteors, this.firefliesFar, this.firefliesNear, this.birds];
    fireflies = [this.firefliesFar, this.firefliesNear];
    meteorTimer = 1.2;
    flockTimer = 6;
    flockPhase = "day";
    time = 0;
    reset() {
      for (const list of this.lists) for (const p of list) p.alive = false;
      this.meteorTimer = 1.2;
      this.flockTimer = 6;
    }
    step(dt, weights) {
      const step = Math.min(0.1, Math.max(0, dt));
      this.time += step;
      const night = weights.night > 0.01;
      const sunset = weights.sunset > 0.01;
      const day = weights.day > 0.01;
      this.spawnStars(night);
      if (night) {
        this.meteorTimer -= step;
        if (this.meteorTimer <= 0) {
          this.spawnMeteor();
          this.meteorTimer = random(0.9, 2.6);
        }
      }
      if (sunset) this.spawnFireflies();
      this.flockTimer -= step;
      if (this.flockTimer <= 0 && (day || sunset)) {
        this.spawnFlock(weights.day >= weights.sunset ? "day" : "sunset");
        this.flockTimer = random(48, 72);
      }
      for (const list of this.lists) {
        for (const p of list) {
          if (!p.alive) continue;
          p.age += step;
          if (p.age >= p.life) {
            p.alive = false;
            continue;
          }
          p.x += p.vx * step;
          p.y += p.vy * step;
        }
      }
      for (const p of this.dust) {
        if (!p.alive) continue;
        const heading = p.seed * 6.283 + this.time * 0.35 + Math.sin(this.time * 0.7 + p.seed * 9) * 1.4;
        const speed = 100 + p.seed * 70;
        p.vx += (Math.cos(heading) * speed - p.vx) * step;
        p.vy += (Math.sin(heading) * speed - p.vy) * step;
      }
      for (const p of this.meteors) if (p.alive) {
        p.vx -= 50 * 9 * step * 0.2;
        p.vy += 50 * 9 * step * 0.2;
      }
    }
    spawn(list, init, max = 1) {
      let made = 0;
      for (const p of list) {
        if (made >= max) return;
        if (!p.alive) {
          p.alive = true;
          p.age = 0;
          p.vx = 0;
          p.vy = 0;
          p.seed = Math.random();
          init(p);
          made++;
        }
      }
    }
    spawnStars(night) {
      if (!night) return;
      this.spawn(this.crossStars, (p) => {
        p.x = random(0, 3840);
        p.y = random(0, 1500);
        p.life = random(4, 8);
        p.size = random(150, 180) * 1.5;
      });
      this.spawn(this.glowStars, (p) => {
        p.x = random(0, 3840);
        p.y = random(0, 1400);
        p.life = random(4, 8);
        p.size = random(80, 150) * 1.5;
      });
      this.spawn(this.dust, (p) => {
        p.x = random(0, 3840);
        p.y = random(0, 1700);
        p.life = random(5, 7);
        p.size = random(14, 18) * 1.5;
      }, 2);
    }
    spawnMeteor() {
      this.spawn(this.meteors, (p) => {
        p.x = random(1100, 4600);
        p.y = random(-300, 700);
        const speed = random(100, 250) * 9;
        p.vx = -speed * 0.7071;
        p.vy = speed * 0.7071;
        p.life = random(0.9, 1.6);
        p.size = random(5, 10) * 3;
      });
    }
    spawnFireflies() {
      this.spawn(this.firefliesFar, (p) => {
        p.x = 3249 + random(-205, 205);
        p.y = 1092 + random(-110, 110);
        p.life = random(3, 20);
        p.size = random(70, 90) * 0.4;
      });
      this.spawn(this.firefliesNear, (p) => {
        p.x = 540 + random(-102, 102);
        p.y = 1594 + random(-60, 60);
        p.life = random(3, 20);
        p.size = random(70, 90) * 0.2;
      });
    }
    spawnFlock(phase) {
      this.flockPhase = phase;
      const leader = { x: phase === "day" ? 4100 : -260, y: phase === "day" ? 408 : 686 };
      const direction = phase === "day" ? -1 : 1;
      const count = 1 + [0, 1, 2].filter(() => Math.random() < 0.5).length;
      let index = 0;
      for (const bird of this.birds) {
        if (index >= count) bird.alive = false;
        else {
          Object.assign(bird, {
            alive: true,
            age: 0,
            life: 60,
            seed: Math.random(),
            frameOffset: Math.floor(Math.random() * 16),
            x: leader.x - direction * index * random(90, 180),
            y: leader.y + random(-120, 120),
            dy: 0,
            vx: direction * random(100, 120),
            vy: random(-10, 10),
            size: random(100, 150)
          });
        }
        index++;
      }
    }
    /** Emits instances per texture group, already weighted by phase. `toPx` maps scene units to framebuffer pixels. */
    emitStars(sink, weights, toPx, scale, group) {
      const list = group === "cross" ? this.crossStars : group === "glow" ? this.glowStars : this.dust;
      for (const p of list) {
        if (!p.alive) continue;
        const twinkle = 0.2 + 0.8 * (0.5 + 0.5 * Math.sin(this.time * (1 + p.seed * 2) * 6.283 + p.seed * 40));
        const alpha = lifeFade(p) * twinkle * weights.night * (group === "dust" ? 0.9 : 0.8);
        if (alpha <= 3e-3) continue;
        const [x, y] = toPx(p.x, p.y);
        const s = p.size * scale;
        sink(x, y, s, s, 0, 0, 0, 1, 1, 1, 1, 1, alpha, true);
      }
    }
    emitMeteors(sink, weights, toPx, scale, heads) {
      for (const p of this.meteors) {
        if (!p.alive) continue;
        const t = p.age / p.life;
        const alpha = Math.min(1, t / 0.08) * (1 - t) * weights.night;
        const [x, y] = toPx(p.x, p.y);
        const speed = Math.hypot(p.vx, p.vy);
        const angle = Math.atan2(p.vy, p.vx);
        if (heads) {
          const s = p.size * 6 * scale;
          sink(x, y, s, s, 0, 0, 0, 1, 1, 0.92, 0.96, 1, alpha, true);
          continue;
        }
        const length = Math.max(40, speed * 0.4) * scale;
        sink(x - Math.cos(angle) * length / 2, y - Math.sin(angle) * length / 2, length, p.size * 1.6 * scale, angle, 0, 0, 1, 1, 0.9, 0.95, 1, alpha, true);
      }
    }
    emitFireflies(sink, weights, toPx, scale) {
      for (const list of this.fireflies) for (const p of list) {
        if (!p.alive) continue;
        const wander = 15 * Math.sin(this.time * (0.3 + p.seed * 0.7) + p.seed * 20);
        const [x, y] = toPx(p.x + wander + Math.sin(this.time * 0.5 + p.seed * 7) * 30, p.y + Math.cos(this.time * 0.43 + p.seed * 5) * 22);
        const flicker = 0.7 + 0.3 * Math.sin(this.time * (10 + p.seed * 10) * 6.283 * 0.1 + p.seed * 9);
        const alpha = lifeFade(p) * flicker * weights.sunset;
        const s = p.size * scale * 2.2;
        sink(x, y, s, s, 0, 0, 0, 1, 1, (136 + p.seed * 38) / 255, 1, (80 + p.seed * 26) / 255, alpha, true);
      }
    }
    emitBirds(sink, weights, toPx, scale) {
      const weight = this.flockPhase === "day" ? weights.day : weights.sunset;
      for (const bird of this.birds) {
        if (!bird.alive) continue;
        const frame = (Math.floor(this.time * 12) + bird.frameOffset) % 16;
        const u0 = frame % 4 / 4;
        const v0 = Math.floor(frame / 4) / 4;
        const flip = this.flockPhase === "sunset";
        const bob = Math.sin(this.time * (2 + bird.seed * 2) + bird.seed * 10) * 3 * (2 + bird.seed * 2);
        const [x, y] = toPx(bird.x, bird.y + bob);
        const s = bird.size * scale;
        sink(x, y, s, s, 0, flip ? u0 + 0.25 : u0, v0, flip ? u0 : u0 + 0.25, v0 + 0.25, 1, 1, 1, Math.min(1, bird.age / 1.5) * weight, false);
      }
    }
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/cassette/cassetteTimeOfDay.ts
  var cassettePhases = ["day", "sunset", "night"];
  var cassettePhaseAt = (hours) => hours >= 7 && hours < 18 ? "day" : hours >= 18 && hours < 22 ? "sunset" : "night";
  var resolveCassettePhase = (choice, now) => choice === "auto" ? cassettePhaseAt(now.getHours() + now.getMinutes() / 60) : choice;
  var cassetteWeightsFor = (phase) => ({ day: phase === "day" ? 1 : 0, sunset: phase === "sunset" ? 1 : 0, night: phase === "night" ? 1 : 0 });
  var stepCassetteWeights = (current, target, elapsedMs, fadeMs) => {
    const step = fadeMs <= 0 ? 1 : Math.min(1, Math.max(0, elapsedMs) / fadeMs);
    const next = { ...current };
    for (const phase of cassettePhases) {
      next[phase] = phase === target ? Math.min(1, current[phase] + step) : Math.max(0, current[phase] - step);
    }
    const sum = next.day + next.sunset + next.night;
    if (sum <= 0) return cassetteWeightsFor(target);
    for (const phase of cassettePhases) next[phase] /= sum;
    return next;
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/cassette/cassetteScenePass.ts
  var phaseAsset = (phase, day, sunset, night) => phase === "day" ? day : phase === "sunset" ? sunset : night;
  var stackedPhaseAlphas = (weights) => {
    const out = { day: 0, sunset: 0, night: 0 };
    let total = 0;
    for (const phase of cassettePhases) {
      const weight = weights[phase];
      if (weight <= 1e-4) continue;
      total += weight;
      out[phase] = total === weight ? 1 : weight / total;
    }
    return out;
  };
  var fract = (value) => value - Math.floor(value);
  var drawCassetteScene = (draw2, input) => {
    const { layout, pixelRatio: pr, weights, time, motion } = input;
    const ss = layout.scene.scale * pr;
    const sx = (x) => (layout.scene.x + x * layout.scene.scale) * pr;
    const sy = (y) => (layout.scene.y + y * layout.scene.scale) * pr;
    const toPx = (x, y) => [sx(x), sy(y)];
    const t = motion ? time : 0;
    const stacked = stackedPhaseAlphas(weights);
    for (const phase of cassettePhases) {
      if (stacked[phase] <= 0) continue;
      const id = phaseAsset(phase, "skyDay", "skySunset", "skyNight");
      if (!draw2.has(id)) continue;
      const scroll = fract(t * (phase === "night" ? -0.01 : 64e-4));
      draw2.layer(id, sx(1920), sy(1080), 5760 * ss, 2160 * ss, { alpha: stacked[phase], scroll: [scroll, 0], ripple: motion ? [0.05, 0.07, 1] : void 0 });
    }
    if (weights.day > 0) {
      if (motion && draw2.has("paperPlane")) {
        const cycle = t % 46 / 18;
        if (cycle <= 1) {
          const fade = Math.min(1, cycle / 0.12, (1 - cycle) / 0.12);
          draw2.layer("paperPlane", sx(-1040 + cycle * 5400), sy(1741 - cycle * 2500), 893 * ss, 890 * ss, { alpha: 0.9 * weights.day * fade });
        }
      }
      if (motion && draw2.has("birds") && input.particles) draw2.particles("birds", (sink) => input.particles.emitBirds(sink, weights, toPx, ss));
      if (draw2.has("cityDay")) draw2.layer("cityDay", sx(1920), sy(1605), 3840 * ss, 1110 * ss, { alpha: weights.day });
    }
    if (weights.sunset > 0) {
      if (draw2.has("clouds")) {
        draw2.layer("clouds", sx(1920), sy(2244.17), 5760 * ss, 2160 * ss, {
          alpha: 0.4 * weights.sunset,
          tint: [0.906, 0.71, 0.831],
          scroll: [fract(t * 0.01), 0],
          ripple: motion ? [0.1, 0.1, 1] : void 0
        });
      }
      if (draw2.has("citySunset")) draw2.layer("citySunset", sx(1920), sy(1080), 3840 * ss, 2160 * ss, { alpha: weights.sunset, waves: motion ? [0.03, 3, 5, 0] : void 0 });
      if (draw2.has("moon")) draw2.layer("moon", sx(1904.5), sy(509.5), 157 * ss, 157 * ss, { alpha: weights.sunset });
      if (motion && draw2.has("birds")) draw2.particles("birds", (sink) => input.particles.emitBirds(sink, weights, toPx, ss));
      if (motion) draw2.particles("glow", (sink) => input.particles.emitFireflies(sink, weights, toPx, ss));
      if (draw2.has("streetLight")) {
        const flicker = (i) => motion ? 0.86 + 0.14 * Math.sin(t * 3 + i * 2.1) * Math.sin(t * 0.7 + i) : 1;
        draw2.layer("streetLight", sx(3282.7), sy(1743), 1e3 * ss, 1504.5 * ss, { alpha: 0.8 * weights.sunset * flicker(0), additive: true });
        draw2.layer("streetLight", sx(536.5), sy(2051), 550 * ss, 1003 * ss, { alpha: 0.8 * weights.sunset * flicker(1), additive: true });
      }
    }
    if (weights.night > 0) {
      if (draw2.has("aurora")) {
        const pulse = motion ? 0.42 + 0.16 * Math.sin(t * 0.5) : 0.5;
        draw2.layer("aurora", sx(1920), sy(747), 8192 * ss, 4096 * ss, { alpha: weights.night * pulse, waves: motion ? [0.05, 0.6, 3, 1.57] : void 0 });
      }
      if (motion) {
        if (draw2.has("starGlow")) draw2.particles("starGlow", (sink) => input.particles.emitStars(sink, weights, toPx, ss, "dust"));
        draw2.particles("streak", (sink) => input.particles.emitMeteors(sink, weights, toPx, ss, false));
        draw2.particles("glow", (sink) => input.particles.emitMeteors(sink, weights, toPx, ss, true));
        if (draw2.has("starCross")) draw2.particles("starCross", (sink) => input.particles.emitStars(sink, weights, toPx, ss, "cross"));
        if (draw2.has("starGlow")) draw2.particles("starGlow", (sink) => input.particles.emitStars(sink, weights, toPx, ss, "glow"));
      }
      if (draw2.has("clouds")) {
        draw2.layer("clouds", sx(1920), sy(2244.17), 5760 * ss, 2160 * ss, {
          alpha: 0.6 * weights.night,
          tint: [0.906, 0.71, 0.831],
          scroll: [fract(t * 0.01), 0],
          ripple: motion ? [0.1, 0.1, 1] : void 0
        });
      }
      if (draw2.has("cityNight")) draw2.layer("cityNight", sx(1920), sy(1603.5), 3840 * ss, 1113 * ss, { alpha: weights.night, sway: motion ? [0.4, 1.4] : void 0 });
    }
    drawCassetteTape(draw2, input, stacked);
    if (weights.day > 0) {
      const sway = (i) => motion ? Math.sin(t * 0.45 + i * 1.7) * 10 : 0;
      const flares = [
        ["flareRing", 683.5, 1514.2, 1807, 1788, 0, 1],
        ["flareHalo", 1168.8, 1336.7, 1915, 1842, 0, 1],
        ["flareDot", 1954.9, 489.3, 275, 261, 0, 1],
        ["flareHalo", 2375.9, 148.9, 967, 930, 2.04, 0.8],
        ["flareRing", 2860.8, -330.3, 1701, 1683, 0, 0.3]
      ];
      flares.forEach(([id, x, y, w, h, rotation, alpha], i) => {
        if (draw2.has(id)) draw2.layer(id, sx(x + sway(i)), sy(y - sway(i + 3)), w * ss, h * ss, { rotation, alpha: alpha * 0.45 * weights.day, screen: true });
      });
    }
  };
  var drawCassetteTape = (draw2, input, stacked) => {
    const { layout, pixelRatio: pr } = input;
    const ts = layout.tape.scale * pr;
    const tx = (x) => (layout.tape.x + x * layout.tape.scale) * pr;
    const ty = (y) => (layout.tape.y + y * layout.tape.scale) * pr;
    const labelAngle = 0.46775;
    for (const phase of cassettePhases) {
      if (stacked[phase] <= 0) continue;
      const tape = phaseAsset(phase, "tapeDay", "tapeSunset", "tapeNight");
      if (draw2.has(tape)) draw2.layer(tape, tx(1064), ty(747.5), 2128 * ts, 1495 * ts, { alpha: stacked[phase] });
      if (!input.title) {
        const label = phaseAsset(phase, "labelDay", "labelSunset", "labelNight");
        if (draw2.has(label)) draw2.layer(label, tx(614.5), ty(686.5), 941 * ts, 569 * ts, { alpha: stacked[phase] });
      }
    }
    if (input.title && input.title.reveal > 0) {
      const { width, height, reveal } = input.title;
      const shown = width * reveal;
      const startX = 606 - width / 2;
      const cx = startX + shown / 2;
      const cy = 694;
      const dx = cx - 606;
      const rx = 606 + dx * Math.cos(labelAngle);
      const ry = cy + dx * Math.sin(labelAngle);
      const options = { rotation: labelAngle, uvRect: [0, 0, reveal, 1] };
      if (input.title.ink > 0) draw2.layer("title", tx(rx), ty(ry), shown * ts, height * ts, { ...options, alpha: 0.9 * input.title.ink });
      if (input.title.neon > 0) draw2.layer("titleNeon", tx(rx), ty(ry), shown * ts, height * ts, { ...options, alpha: 0.9 * input.title.neon });
    }
    const reels = [
      ["reelLeftDay", "reelLeftSunset", "reelLeftNight", 490, 456.5, 261],
      ["reelRightDay", "reelRightSunset", "reelRightNight", 897.5, 656.5, 266]
    ];
    for (const phase of cassettePhases) {
      if (stacked[phase] <= 0) continue;
      for (const [day, sunset, night, x, y, size] of reels) {
        const id = phaseAsset(phase, day, sunset, night);
        if (draw2.has(id)) draw2.layer(id, tx(x), ty(y), size * ts, size * ts, { rotation: input.spinAngle, alpha: stacked[phase] });
      }
    }
    if (input.hasCover) {
      const waves = input.motion && input.spinning > 0.01 ? [0.06 * input.spinning, 15, 25, 1.93] : void 0;
      draw2.layer("cover", tx(489.7), ty(464.4), 361.2 * ts, 361.2 * ts, { rotation: input.spinAngle, alpha: 0.26, disc: 0.2, waves });
      draw2.layer("cover", tx(904.5), ty(667), 496.3 * ts, 496.3 * ts, { rotation: input.spinAngle + 1.22, alpha: 0.26, disc: 0.16, waves });
    }
  };

  // tools/SourceLyrics/cassette-renderer.ts
  var native = (...args) => globalThis.__cassette(...args);
  var CassetteRenderer = class {
    constructor(canvas2) {
      this.canvas = canvas2;
      this.generated = { glow: this.generatedSprite(false), streak: this.generatedSprite(true) };
    }
    ratio = 1;
    w = 1;
    h = 1;
    assets = /* @__PURE__ */ new Map();
    cover = null;
    titles = null;
    generated;
    gl = { isContextLost: () => false };
    get pixelRatio() {
      return this.ratio;
    }
    isContextLost() {
      return false;
    }
    resize(w, h, dpr, pressure) {
      this.ratio = Math.min(Math.max(0.5, dpr), pressure ? 1.25 : 2, Math.sqrt((pressure ? 1280 * 720 : 1920 * 1080) / (w * h)));
      this.w = Math.max(1, Math.floor(w * this.ratio));
      this.h = Math.max(1, Math.floor(h * this.ratio));
      this.canvas.width = this.w;
      this.canvas.height = this.h;
    }
    setPressure() {
    }
    backingBytes() {
      return native("bytes");
    }
    hasAsset(id) {
      return this.assets.has(id);
    }
    uploadAsset(id, image) {
      if (!this.assets.has(id)) this.assets.set(id, this.upload(image, cassetteRepeatingAssets.has(id)));
    }
    releaseAsset(id) {
      this.deleteTexture(this.assets.get(id));
      this.assets.delete(id);
    }
    assetIds() {
      return [...this.assets.keys()];
    }
    uploadAtlas(canvas2) {
      return this.upload(canvas2);
    }
    deleteTexture(texture) {
      if (texture) native("delete", texture.texture);
    }
    upload(canvas2, repeat = false) {
      const id = native("upload", canvas2.nativeId, repeat);
      canvas2.__consumed = true;
      return { texture: id, width: canvas2.width, height: canvas2.height, bytes: canvas2.width * canvas2.height * 4 };
    }
    setCover(canvas2) {
      this.deleteTexture(this.cover);
      this.cover = canvas2 ? this.upload(canvas2) : null;
    }
    hasCover() {
      return !!this.cover;
    }
    setTitles(ink, neon) {
      if (this.titles) {
        this.deleteTexture(this.titles.ink);
        this.deleteTexture(this.titles.neon);
      }
      this.titles = ink && neon ? { ink: this.upload(ink), neon: this.upload(neon) } : null;
    }
    render(frame) {
      const pr = this.ratio;
      native("begin", this.w, this.h, frame.time, frame.fallback[0], frame.fallback[1], frame.fallback[2]);
      const noise = this.assets.get("waterNormal") ?? this.generated.glow;
      drawCassetteScene({
        has: (id) => this.assets.has(id),
        layer: (id, cx, cy, w, h, options = {}) => {
          const texture = id === "cover" ? this.cover : id === "title" ? this.titles?.ink : id === "titleNeon" ? this.titles?.neon : this.assets.get(id);
          if (texture) native("layer", texture.texture, noise.texture, cx, cy, w, h, JSON.stringify(options));
        },
        particles: (id, emit) => {
          const texture = id === "glow" ? this.generated.glow : id === "streak" ? this.generated.streak : this.assets.get(id);
          if (texture) emit((...v) => native("particle", texture.texture, ...v));
        }
      }, { ...frame, pixelRatio: pr });
      native("composite", JSON.stringify({ ripples: Array.from(frame.ripples), pixelRatio: pr, retro: frame.retro, reveal: frame.reveal, fallback: frame.fallback }));
      for (const batch of frame.glyphs) native("glyphs", batch.texture, noise.texture, JSON.stringify(Array.from(batch.data.subarray(0, batch.count * 17))), batch.count, JSON.stringify(frame.palette));
      native("finish");
    }
    park() {
    }
    // The worker releases the whole scene when hidden instead of retaining a parked renderer.
    releaseArt() {
      for (const id of this.assetIds()) this.releaseAsset(id);
      this.setCover(null);
      this.setTitles(null, null);
    }
    dispose() {
      this.releaseArt();
      this.deleteTexture(this.generated.glow);
      this.deleteTexture(this.generated.streak);
    }
    generatedSprite(streak) {
      const c = document.createElement("canvas");
      c.width = streak ? 128 : 64;
      c.height = streak ? 16 : 64;
      const x = c.getContext("2d");
      if (streak) {
        const a = x.createLinearGradient(0, 0, 128, 0);
        a.addColorStop(0, "rgba(255,255,255,0)");
        a.addColorStop(0.85, "rgba(255,255,255,.7)");
        a.addColorStop(1, "white");
        x.fillStyle = a;
        x.fillRect(0, 0, 128, 16);
        x.globalCompositeOperation = "destination-in";
        const b = x.createLinearGradient(0, 0, 0, 16);
        b.addColorStop(0, "transparent");
        b.addColorStop(0.5, "black");
        b.addColorStop(1, "transparent");
        x.fillStyle = b;
        x.fillRect(0, 0, 128, 16);
      } else {
        const a = x.createRadialGradient(32, 32, 0, 32, 32, 32);
        a.addColorStop(0, "white");
        a.addColorStop(0.18, "rgba(255,255,255,.75)");
        a.addColorStop(0.5, "rgba(255,255,255,.18)");
        a.addColorStop(1, "transparent");
        x.fillStyle = a;
        x.fillRect(0, 0, 64, 64);
      }
      const texture = this.upload(c);
      c.width = 0;
      return texture;
    }
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/cassette/cassetteShaders.ts
  var cassetteMaxRipples = 8;
  var rippleChunk = (
    /* glsl */
    `
uniform vec4 u_ripples[${cassetteMaxRipples}];
uniform vec3 u_rippleShape; // speed px/s, band width px, amplitude px
vec2 rippleOffset(vec2 p) {
  vec2 o = vec2(0.0);
  for (int i = 0; i < ${cassetteMaxRipples}; i++) {
    vec4 r = u_ripples[i];
    if (r.w <= 0.0) continue;
    vec2 d = p - r.xy;
    float dist = length(d);
    float radius = r.z * u_rippleShape.x;
    float band = exp(-pow((dist - radius) / u_rippleShape.y, 2.0));
    float fade = r.w * pow(max(0.0, 1.0 - r.z / 2.8), 2.0);
    o += (d / max(dist, 1.0)) * sin((dist - radius) * 0.085) * band * fade * u_rippleShape.z;
  }
  return o;
}
`
  );
  var cassetteGlyphFragment = (
    /* glsl */
    `#version 300 es
precision highp float;
in vec2 v_local;
in vec4 v_rect;
in vec4 v_color;
in vec4 v_params;
in vec2 v_screen;
in vec2 v_sizePx;
uniform sampler2D u_tex;
uniform sampler2D u_noise;
uniform float u_time;
uniform vec3 u_base;
uniform vec3 u_sub;
uniform vec3 u_hi;
uniform vec3 u_halo;
uniform float u_haloAlpha;
${rippleChunk}
out vec4 outColor;
void main() {
  float fill = v_params.x;
  float wobble = v_params.y;
  float dissolve = v_params.z;
  float seed = v_params.w;
  vec2 local = v_local;
  vec2 w = vec2(sin(local.y * 17.0 + u_time * 4.1 + seed * 6.2831), cos(local.x * 13.0 + u_time * 3.3 + seed * 3.1)) * 0.016 * wobble;
  vec2 shifted = clamp(local + w + rippleOffset(v_screen) / v_sizePx, 0.0, 1.0);
  vec2 t = texture(u_tex, mix(v_rect.xy, v_rect.zw, shifted)).rg;
  float fillA = t.r;
  float glowA = t.g * u_haloAlpha;
  if (dissolve > 0.0) {
    float n = texture(u_noise, local * 0.45 + vec2(seed, seed * 1.7)).b;
    float keep = smoothstep(dissolve - 0.12, dissolve + 0.12, n * 1.1);
    fillA *= keep;
    glowA *= mix(keep, 1.0, 0.35) * (1.0 - dissolve * 0.6);
  }
  float hi = clamp((fill * 1.24 - 0.12 - local.x) / 0.24 + 0.5, 0.0, 1.0);
  vec3 body = mix(mix(u_sub, u_base, v_color.r), u_hi, hi * v_color.r);
  vec3 halo = mix(u_halo, u_hi, hi * v_color.r * 0.4);
  vec3 rgb = body * fillA + halo * glowA * (1.0 - fillA);
  float a = fillA + glowA * (1.0 - fillA);
  outColor = vec4(rgb, a) * v_color.a;
}
`
  );
  var cassetteCompositeFragment = (
    /* glsl */
    `#version 300 es
precision highp float;
uniform sampler2D u_scene;
uniform vec2 u_view;
uniform float u_time;
uniform float u_retro;
uniform float u_pxScale;
uniform float u_reveal;
uniform vec3 u_fallback;
${rippleChunk}
out vec4 outColor;
float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }
void main() {
  vec2 frag = gl_FragCoord.xy;
  vec2 p = vec2(frag.x, u_view.y - frag.y);
  vec2 offset = rippleOffset(p);
  vec2 uv = (frag + vec2(offset.x, -offset.y)) / u_view;
  vec3 color;
  if (u_retro > 0.0) {
    float split = 0.9 * u_pxScale / u_view.x * u_retro;
    color = vec3(texture(u_scene, uv + vec2(split, 0.0)).r, texture(u_scene, uv).g, texture(u_scene, uv - vec2(split, 0.0)).b);
    float line = 0.5 + 0.5 * sin(p.y / u_pxScale * 2.0944);
    color *= 1.0 - 0.045 * u_retro * line;
    color += (hash(floor(p / u_pxScale) + floor(u_time * 24.0)) - 0.5) * 0.035 * u_retro;
    vec2 q = uv - 0.5;
    color *= 1.0 - dot(q, q) * 0.42 * u_retro;
  } else {
    color = texture(u_scene, uv).rgb;
  }
  float crest = length(offset) / max(1.0, u_rippleShape.z);
  color += crest * 0.07;
  outColor = vec4(mix(u_fallback, color, u_reveal), 1.0);
}
`
  );

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/cassette/cassetteTitleRaster.ts
  var titleUnits = 30;
  var maxWidthUnits = 640;
  var titleFont = '"MS Gothic", "Osaka-Mono", "Noto Sans Mono CJK SC", "Courier New", monospace';
  var rasterCassetteTitle = (title, rasterPx) => {
    const clean = title.replace(/\s+/g, " ").trim();
    if (!clean) return null;
    const px = Math.max(16, Math.min(72, Math.round(rasterPx)));
    const font = `700 ${px}px ${titleFont}`;
    const probe = document.createElement("canvas").getContext("2d");
    if (!probe) return null;
    probe.font = font;
    const maxPx = maxWidthUnits / titleUnits * px;
    let text = clean;
    if (probe.measureText(text).width > maxPx) {
      const chars = Array.from(clean);
      while (chars.length > 1 && probe.measureText(`${chars.join("")}\u2026`).width > maxPx) chars.pop();
      text = `${chars.join("")}\u2026`;
    }
    const pad = Math.ceil(px * 0.18);
    const width = Math.ceil(probe.measureText(text).width) + pad * 2;
    const height = Math.ceil(px * 1.3) + pad * 2;
    const draw2 = (neon) => {
      const canvas2 = document.createElement("canvas");
      canvas2.width = width;
      canvas2.height = height;
      const ctx = canvas2.getContext("2d");
      if (!ctx) return canvas2;
      ctx.font = font;
      ctx.textAlign = "left";
      ctx.textBaseline = "middle";
      if (neon) {
        ctx.lineJoin = "round";
        ctx.lineWidth = Math.max(2, px * 0.14);
        ctx.strokeStyle = "#162d8d";
        ctx.strokeText(text, pad, height / 2);
        ctx.fillStyle = "#f77dff";
      } else {
        ctx.fillStyle = "#2e113f";
      }
      ctx.fillText(text, pad, height / 2);
      return canvas2;
    };
    const scale = titleUnits / px;
    return { ink: draw2(false), neon: draw2(true), width: width * scale, height: height * scale, length: Array.from(text).length };
  };

  // ../../../Users/moekotori/echo/src/renderer/components/lyrics/cassette/cassetteEngine.ts
  var palettes = {
    day: { base: [1, 1, 1], sub: [0.93, 0.95, 1], hi: [1, 0.56, 0.83], halo: [0.23, 0.11, 0.39], haloAlpha: 0.78 },
    sunset: { base: [1, 1, 1], sub: [1, 0.94, 0.98], hi: [1, 0.88, 0.56], halo: [0.29, 0.13, 0.44], haloAlpha: 0.72 },
    night: { base: [0.96, 0.95, 1], sub: [0.85, 0.9, 1], hi: [0.97, 0.49, 1], halo: [0.09, 0.18, 0.55], haloAlpha: 0.8 }
  };
  var fallbackSky = {
    day: [0.55, 0.8, 0.9],
    sunset: [0.78, 0.6, 0.85],
    night: [0.12, 0.18, 0.45]
  };
  var maxCachedLines = 6;
  var phaseFadeMs = 2600;
  var phaseCheckMs = 2e4;
  var rippleLifeSec = 2.8;
  var titleCharSec = 0.035;
  var CassetteEngine = class {
    constructor(host) {
      this.host = host;
      this.renderer = new CassetteRenderer(host.canvas);
      const lost = () => {
        this.stopLoop();
        host.onFailure();
      };
      host.canvas.addEventListener("webglcontextlost", lost);
      this.cleanup.push(() => host.canvas.removeEventListener("webglcontextlost", lost));
      this.cleanup.push(registerLyricsResourceOwner({
        id: "lyrics-cassette-stage",
        snapshot: () => ({ kind: "webgl", state: this.stop ? "active" : "parked", backingBytes: this.renderer.backingBytes() }),
        // Parked under memory pressure: drop the decoded art; it is reloaded when the page shows again.
        releaseUnused: () => {
          this.coverNeedsReload = this.coverNeedsReload || this.renderer.hasCover();
          this.renderer.releaseArt();
          this.clearLines();
          this.titleInfo = null;
          this.reveal = 0;
        },
        releaseOnPressure: true
      }));
      this.target = resolveCassettePhase(this.preferences.timeOfDay, /* @__PURE__ */ new Date());
      const fonts = typeof document !== "undefined" ? document.fonts : void 0;
      if (fonts && typeof fonts.addEventListener === "function") {
        const loaded = () => {
          if (this.font.stack !== cassetteLyricsFont) this.clearLines();
        };
        fonts.addEventListener("loadingdone", loaded);
        this.cleanup.push(() => fonts.removeEventListener("loadingdone", loaded));
      }
    }
    renderer;
    particles = new CassetteParticles();
    cleanup = [];
    layout = resolveCassetteStageLayout(1, 1);
    cssWidth = 0;
    cssHeight = 0;
    preferences = defaultCassettePreferences;
    clock = { positionMs: 0, updatedAtMs: 0, playbackRate: 1, playing: false, offsetMs: 0 };
    wordClock = null;
    motion = true;
    pressure = false;
    seekEnabled = true;
    showTranslation = true;
    coverNeedsReload = false;
    stop = null;
    disposed = false;
    timeline = [];
    songKey = 0;
    cells = [];
    cellOfLine = new Int16Array(0);
    lines = /* @__PURE__ */ new Map();
    progress = new Float32Array(256);
    pose = { dx: 0, dy: 0, rotation: 0, scale: 1, alpha: 1, wobble: 0, dissolve: 0 };
    ripples = new Float32Array(cassetteMaxRipples * 4);
    pointerRipples = [];
    hitBoxes = [];
    title = "";
    /** From the lyrics font settings: CSS family list, and sizes relative to their defaults (40px / 22px). */
    font = { stack: cassetteLyricsFont, scale: 1, subScale: 1 };
    titleInfo = null;
    titleTyped = 0;
    weights = { day: 0, sunset: 0, night: 0 };
    target = "day";
    reveal = 0;
    sinceCheck = 0;
    loading = /* @__PURE__ */ new Set();
    wanted = /* @__PURE__ */ new Set();
    loadAbort = new AbortController();
    time = 0;
    spinAngle = 0;
    spinSpeed = 0;
    frameNumber = 0;
    resize(cssWidth, cssHeight) {
      if (this.disposed || cssWidth <= 0 || cssHeight <= 0) return;
      this.cssWidth = cssWidth;
      this.cssHeight = cssHeight;
      this.renderer.resize(cssWidth, cssHeight, window.devicePixelRatio || 1, this.pressure);
      this.layout = resolveCassetteStageLayout(cssWidth, cssHeight);
      this.cells = [];
      for (const cell of cassetteAnchorCells(this.layout)) {
        const zone = this.layout.zones.find((z) => cell.x0 >= z.x0 - 0.5 && cell.x1 <= z.x1 + 0.5 && cell.y0 >= z.y0 - 0.5 && cell.y1 <= z.y1 + 0.5) ?? this.layout.zones[0];
        this.cells.push({ ...cell, zone });
      }
      this.assignCells();
      this.clearLines();
      this.rasterTitle();
    }
    setSong(lyrics, durationMs, title) {
      this.timeline = createCassetteTimeline(lyrics.kind === "synced" ? lyrics.lines : [], durationMs);
      this.songKey++;
      this.assignCells();
      this.clearLines();
      if (title !== this.title) {
        this.title = title;
        this.titleTyped = 0;
        this.rasterTitle();
      }
    }
    setClock(clock) {
      this.clock = clock;
    }
    setMotion(motion) {
      if (motion !== this.motion) {
        this.motion = motion;
        if (!motion) this.particles.reset();
      }
    }
    setSeekEnabled(enabled) {
      this.seekEnabled = enabled;
    }
    setShowTranslation(enabled) {
      if (enabled === this.showTranslation) return;
      this.showTranslation = enabled;
      this.clearLines();
    }
    /** The lyrics font settings. A change re-rasters the lines; an imported font re-rasters again once it has loaded. */
    setFont(family, sizePx, secondarySizePx) {
      const custom = family?.trim();
      const next = {
        stack: custom ? `${serializeFontList(custom)}, ${cassetteLyricsFont}` : cassetteLyricsFont,
        scale: Math.min(1.5, Math.max(0.5, (sizePx ?? 40) / 40)),
        subScale: Math.min(1.6, Math.max(0.5, (secondarySizePx ?? 22) / 22))
      };
      if (next.stack === this.font.stack && next.scale === this.font.scale && next.subScale === this.font.subScale) return;
      this.font = next;
      this.clearLines();
      if (custom && typeof document !== "undefined" && document.fonts) {
        const stack = next.stack;
        void document.fonts.load(`700 40px ${stack}`).then(() => {
          if (!this.disposed && this.font.stack === stack) this.clearLines();
        }).catch(() => void 0);
      }
    }
    setPressure(pressure) {
      if (pressure === this.pressure) return;
      this.pressure = pressure;
      if (this.cssWidth > 0) this.resize(this.cssWidth, this.cssHeight);
    }
    setPreferences(preferences) {
      const reseed = preferences.seed !== this.preferences.seed;
      this.preferences = preferences;
      this.target = resolveCassettePhase(preferences.timeOfDay, /* @__PURE__ */ new Date());
      if (reseed) {
        this.assignCells();
        this.clearLines();
      }
      this.ensureAssets();
    }
    setCover(source) {
      if (this.disposed) return;
      this.coverNeedsReload = false;
      this.renderer.setCover(source, source?.width ?? 0, source?.height ?? 0);
    }
    /** A click on a live line seeks to it; anywhere else drops a ripple on the water. */
    press(cssX, cssY) {
      if (this.seekEnabled) {
        const hit = this.hitBoxes.find((box) => cssX >= box.x0 && cssX <= box.x1 && cssY >= box.y0 && cssY <= box.y1);
        if (hit) {
          this.host.onSeek(hit.timeMs);
          this.addPointerRipple(cssX, cssY);
          return;
        }
      }
      this.addPointerRipple(cssX, cssY);
    }
    addPointerRipple(x, y) {
      this.pointerRipples.push({ x, y, at: this.time });
      if (this.pointerRipples.length > 3) this.pointerRipples.shift();
    }
    /** null stops drawing (hidden); the full-size buffers are parked but the art stays for a quick return. */
    setFrameInterval(ms) {
      this.stopLoop();
      if (this.disposed) return;
      if (ms === null) {
        this.renderer.park();
        this.clearLines();
        return;
      }
      if (this.cssWidth > 0) this.renderer.resize(this.cssWidth, this.cssHeight, window.devicePixelRatio || 1, this.pressure);
      this.ensureAssets();
      if (this.title && !this.titleInfo) this.rasterTitle();
      if (this.coverNeedsReload) {
        this.coverNeedsReload = false;
        this.host.onCoverReload();
      }
      this.stop = subscribeVisualFrame((_timestamp, elapsed) => this.frame(elapsed), ms);
    }
    stopLoop() {
      this.stop?.();
      this.stop = null;
    }
    dispose() {
      if (this.disposed) return;
      this.disposed = true;
      this.stopLoop();
      this.loadAbort.abort();
      this.cleanup.forEach((fn) => fn());
      this.cleanup.length = 0;
      this.clearLines();
      this.renderer.dispose();
      this.timeline = [];
    }
    assignCells() {
      const count = this.cells.length;
      this.cellOfLine = new Int16Array(this.timeline.length);
      const seed = this.preferences.seed * 131 + this.timeline.length * 7;
      for (let i = 0; i < this.timeline.length; i++) {
        const avoid = [i > 0 ? this.cellOfLine[i - 1] : -1, i > 1 ? this.cellOfLine[i - 2] : -1];
        this.cellOfLine[i] = pickCassetteCell(count, seed, i, avoid);
      }
    }
    clearLines() {
      for (const entry of this.lines.values()) this.renderer.deleteTexture(entry.texture);
      this.lines.clear();
    }
    rasterTitle() {
      if (this.disposed) return;
      const raster = this.title ? rasterCassetteTitle(this.title, 30 * this.layout.tape.scale * this.renderer.pixelRatio * 2) : null;
      this.renderer.setTitles(raster?.ink ?? null, raster?.neon ?? null);
      this.titleInfo = raster ? { width: raster.width, height: raster.height, length: raster.length } : null;
      if (raster) {
        for (const canvas2 of [raster.ink, raster.neon]) {
          canvas2.width = 1;
          canvas2.height = 1;
        }
      }
    }
    /** Decodes what the shown and target phases need, and frees phases that have fully faded out. */
    ensureAssets() {
      if (this.disposed) return;
      const wanted = new Set(cassetteSharedAssets);
      for (const phase of cassettePhases) {
        if (phase !== this.target && this.weights[phase] <= 0) continue;
        for (const id of cassettePhaseAssets[phase].essential) wanted.add(id);
        for (const id of cassettePhaseAssets[phase].extra) wanted.add(id);
      }
      this.wanted = wanted;
      for (const id of this.renderer.assetIds()) if (!wanted.has(id)) this.renderer.releaseAsset(id);
      const missing = [...wanted].filter((id) => !this.renderer.hasAsset(id) && !this.loading.has(id));
      missing.sort((a, b) => Number(!cassettePhaseAssets[this.target].essential.includes(a)) - Number(!cassettePhaseAssets[this.target].essential.includes(b)));
      const signal = this.loadAbort.signal;
      for (const id of missing) this.loading.add(id);
      const next = () => {
        const id = missing.shift();
        if (!id) return;
        if (signal.aborted) {
          this.loading.delete(id);
          return;
        }
        void loadCassetteImage(cassetteAssetUrls[id], signal).then((image) => {
          if (!this.disposed && this.wanted.has(id)) this.renderer.uploadAsset(id, image);
          image.src = "";
        }).catch(() => void 0).finally(() => {
          this.loading.delete(id);
          next();
        });
      };
      next();
      next();
    }
    phaseReady(phase) {
      return cassettePhaseAssets[phase].essential.every((id) => this.renderer.hasAsset(id));
    }
    songTimeMs(now) {
      const c = this.clock;
      const raw = c.positionMs + (c.playing ? Math.max(0, now - c.updatedAtMs) * c.playbackRate : 0) + c.offsetMs;
      this.wordClock = stabilizeWordPlaybackPosition({
        previous: this.wordClock,
        rawPositionMs: raw,
        sampledAtMs: now,
        playbackRate: c.playing ? c.playbackRate : 0,
        smoothingEnabled: c.playing
      });
      return this.wordClock.positionMs;
    }
    frame(elapsedMs) {
      if (this.disposed || this.renderer.isContextLost()) return;
      this.frameNumber++;
      const dt = Math.min(0.1, Math.max(0, elapsedMs / 1e3));
      const now = performance.now();
      if (this.motion) this.time += dt;
      this.sinceCheck += elapsedMs;
      if (this.sinceCheck >= phaseCheckMs) {
        this.sinceCheck = 0;
        const target = resolveCassettePhase(this.preferences.timeOfDay, /* @__PURE__ */ new Date());
        if (target !== this.target) {
          this.target = target;
          this.ensureAssets();
        }
      }
      if (this.phaseReady(this.target)) {
        const before = this.weights;
        const sum = before.day + before.sunset + before.night;
        this.weights = sum <= 0 ? { day: 0, sunset: 0, night: 0, [this.target]: 1 } : stepCassetteWeights(before, this.target, elapsedMs, this.motion ? phaseFadeMs : 0);
        this.reveal = Math.min(1, this.reveal + (this.motion ? elapsedMs / 900 : 1));
        if (cassettePhases.some((p) => before[p] > 0 && this.weights[p] <= 0)) this.ensureAssets();
      }
      if (this.motion) this.particles.step(dt, this.weights);
      const playing = this.clock.playing;
      const desired = playing ? 1 : 0;
      this.spinSpeed = Math.abs(this.spinSpeed - desired) < 0.05 ? desired : this.spinSpeed + (desired - this.spinSpeed) * Math.min(1, dt * 0.7 * 1.5);
      if (this.motion) this.spinAngle = (this.spinAngle + dt * this.spinSpeed * 100 * Math.PI / 180) % (Math.PI * 2);
      if (this.titleInfo) this.titleTyped = this.motion ? Math.min(this.titleInfo.length, this.titleTyped + dt / titleCharSec) : this.titleInfo.length;
      const songMs = this.songTimeMs(now);
      const visible = visibleCassetteLines(this.timeline, songMs);
      const glyphs = this.buildGlyphs(visible, songMs);
      this.buildRipples(visible);
      const ink = this.weights.day + this.weights.sunset;
      this.renderer.render({
        layout: this.layout,
        pixelRatio: this.renderer.pixelRatio,
        weights: this.weights,
        time: this.time,
        motion: this.motion,
        spinAngle: this.spinAngle,
        spinning: this.spinSpeed,
        hasCover: this.renderer.hasCover(),
        title: this.titleInfo ? { width: this.titleInfo.width, height: this.titleInfo.height, reveal: Math.min(1, Math.ceil(this.titleTyped) / Math.max(1, this.titleInfo.length)), ink, neon: this.weights.night } : null,
        particles: this.particles,
        retro: this.preferences.retro && !this.pressure,
        ripples: this.ripples,
        glyphs,
        palette: this.blendPalette(),
        fallback: this.blendFallback(),
        reveal: this.reveal
      });
    }
    blendPalette() {
      const w = this.weights;
      const sum = w.day + w.sunset + w.night;
      if (sum <= 0) return palettes[this.target];
      const mix = (key) => [0, 1, 2].map((i) => cassettePhases.reduce((acc, p) => acc + palettes[p][key][i] * w[p] / sum, 0));
      return { base: mix("base"), sub: mix("sub"), hi: mix("hi"), halo: mix("halo"), haloAlpha: cassettePhases.reduce((acc, p) => acc + palettes[p].haloAlpha * w[p] / sum, 0) };
    }
    blendFallback() {
      const w = this.weights;
      const sum = w.day + w.sunset + w.night;
      if (sum <= 0) return fallbackSky[this.target];
      return [0, 1, 2].map((i) => cassettePhases.reduce((acc, p) => acc + fallbackSky[p][i] * w[p] / sum, 0));
    }
    /** Raster on first sight (and one line ahead), keep a few, place each in its anchor cell. */
    lineEntry(order) {
      const cached = this.lines.get(order);
      if (cached) return cached;
      const timed = this.timeline[order];
      if (!timed) return null;
      const layout = this.layout;
      const cell = this.cells[this.cellOfLine[order]] ?? { ...layout.zones[0], zone: layout.zones[0] };
      const seed = this.songKey * 977 + order * 31 + this.preferences.seed;
      const sizeJitter = 0.92 + cassetteHash(seed, order, 21) * 0.2;
      const fontPx = Math.round(layout.fontPx * sizeJitter * this.font.scale);
      const zoneWidth = cell.zone.x1 - cell.zone.x0;
      const raster = rasterCassetteLine(
        timed.line.text,
        this.showTranslation ? timed.line.translation ?? null : null,
        fontPx,
        Math.min(2, this.renderer.pixelRatio),
        Math.min(zoneWidth * 0.92, layout.width * 0.62),
        this.font.stack,
        this.font.subScale
      );
      if (!raster) return null;
      const texture = this.renderer.uploadAtlas(raster.canvas);
      raster.canvas.width = 1;
      raster.canvas.height = 1;
      const halfW = raster.width / 2 + fontPx * 0.3;
      const halfH = raster.height / 2 + fontPx * 0.3;
      const jitterX = (cassetteHash(seed, order, 22) - 0.5) * (cell.x1 - cell.x0) * 0.35;
      const jitterY = (cassetteHash(seed, order, 23) - 0.5) * (cell.y1 - cell.y0) * 0.35;
      const clampTo = (value, min, max) => min > max ? (min + max) / 2 : Math.min(max, Math.max(min, value));
      const heading = cassetteHash(seed, order, 24) * Math.PI * 2;
      const entry = {
        order,
        texture,
        glyphs: raster.glyphs,
        width: raster.width,
        height: raster.height,
        textLength: raster.text.length,
        centerX: clampTo((cell.x0 + cell.x1) / 2 + jitterX, cell.zone.x0 + halfW, cell.zone.x1 - halfW),
        centerY: clampTo((cell.y0 + cell.y1) / 2 + jitterY, cell.zone.y0 + halfH, cell.zone.y1 - halfH),
        rotation: (cassetteHash(seed, order, 25) - 0.5) * 0.14,
        driftX: Math.cos(heading) * fontPx * 0.09,
        driftY: Math.sin(heading) * fontPx * 0.06,
        seed,
        instances: new Float32Array(raster.glyphs.length * cassetteInstanceFloats),
        usedAt: this.frameNumber
      };
      this.lines.set(order, entry);
      if (this.lines.size > maxCachedLines) {
        const stale = [...this.lines.values()].filter((line) => line.usedAt < this.frameNumber).sort((a, b) => a.usedAt - b.usedAt);
        for (const line of stale.slice(0, this.lines.size - maxCachedLines)) {
          this.renderer.deleteTexture(line.texture);
          this.lines.delete(line.order);
        }
      }
      return entry;
    }
    buildGlyphs(visible, songMs) {
      const batches = [];
      this.hitBoxes.length = 0;
      let rastered = 0;
      for (const line of visible) {
        const order = this.timelineOrder(line);
        if (order < 0) continue;
        if (!this.lines.has(order) && rastered >= 2) continue;
        if (!this.lines.has(order)) rastered++;
        const entry = this.lineEntry(order);
        if (!entry) continue;
        const pr = this.renderer.pixelRatio;
        const fontPx = this.layout.fontPx * this.font.scale;
        if (this.progress.length < entry.textLength) this.progress = new Float32Array(entry.textLength * 2);
        cassetteCharProgress(line.line, line.line.text.trim().slice(0, 240), songMs, this.progress);
        entry.usedAt = this.frameNumber;
        const age = Math.max(0, line.ageMs + cassetteEnterLeadMs) / 1e3;
        const driftTime = Math.min(age, 10);
        const cx = entry.centerX + entry.driftX * driftTime;
        const cy = entry.centerY + entry.driftY * driftTime;
        const cos = Math.cos(entry.rotation);
        const sin = Math.sin(entry.rotation);
        let count = 0;
        entry.glyphs.forEach((glyph, index) => {
          const pose = cassetteGlyphPose(index, entry.glyphs.length, entry.seed, line.ageMs, line.scatterAgeMs, fontPx, this.time, this.motion, this.pose);
          if (pose.alpha <= 3e-3) return;
          const x = cx + glyph.restX * cos - glyph.restY * sin + pose.dx;
          const y = cy + glyph.restX * sin + glyph.restY * cos + pose.dy;
          const fill = glyph.sub ? 0 : this.progress[glyph.charIndex] ?? 0;
          writeCassetteInstance(
            entry.instances,
            count++,
            x * pr,
            y * pr,
            glyph.width * pose.scale * pr,
            glyph.height * pose.scale * pr,
            entry.rotation + pose.rotation,
            glyph.u0,
            glyph.v0,
            glyph.u1,
            glyph.v1,
            glyph.sub ? 0 : 1,
            0,
            0,
            pose.alpha * (glyph.sub ? 0.85 : 1),
            fill,
            pose.wobble,
            pose.dissolve,
            cassetteHash(entry.seed, index, 9)
          );
        });
        if (count > 0) batches.push({ texture: entry.texture.texture, data: entry.instances, count });
        if (line.scatterAgeMs < 0) {
          this.hitBoxes.push({ x0: cx - entry.width / 2, x1: cx + entry.width / 2, y0: cy - entry.height / 2, y1: cy + entry.height / 2, timeMs: line.line.timeMs });
        }
      }
      const last = visible[visible.length - 1];
      const nextOrder = last ? this.timelineOrder(last) + 1 : -1;
      if (rastered === 0 && nextOrder > 0 && nextOrder < this.timeline.length && songMs > this.timeline[nextOrder].startMs - 1600) this.lineEntry(nextOrder);
      return batches;
    }
    timelineOrder(line) {
      let low = 0;
      let high = this.timeline.length - 1;
      while (low <= high) {
        const mid = low + high >> 1;
        const entry = this.timeline[mid];
        if (entry.startMs === line.startMs && entry.index === line.index) return mid;
        if (entry.startMs < line.startMs || entry.startMs === line.startMs && entry.index < line.index) low = mid + 1;
        else high = mid - 1;
      }
      return this.timeline.findIndex((entry) => entry.index === line.index);
    }
    buildRipples(visible) {
      this.ripples.fill(0);
      if (!this.motion) return;
      let slot = 0;
      const put = (x, y, ageSec, strength) => {
        if (slot >= cassetteMaxRipples || ageSec < 0 || ageSec >= rippleLifeSec) return;
        const o = slot++ * 4;
        this.ripples[o] = x;
        this.ripples[o + 1] = y;
        this.ripples[o + 2] = ageSec;
        this.ripples[o + 3] = strength;
      };
      for (const line of visible) {
        const entry = this.lines.get(this.timelineOrder(line));
        if (!entry) continue;
        put(entry.centerX, entry.centerY, (line.ageMs + cassetteEnterLeadMs * 0.4) / 1e3, 1);
        put(entry.centerX, entry.centerY, line.scatterAgeMs / 1e3, 0.55);
      }
      this.pointerRipples = this.pointerRipples.filter((r) => this.time - r.at < rippleLifeSec);
      for (const ripple of this.pointerRipples) put(ripple.x, ripple.y, this.time - ripple.at, 0.8);
    }
  };

  // tools/SourceLyrics/cassette-entry.ts
  var stage;
  var canvas;
  function build(input, width, height) {
    canvas = document.createElement("canvas");
    stage = new CassetteEngine({ canvas, onSeek: (ms) => {
      globalThis.__seek = ms;
    }, onFailure: () => {
      throw Error("Cassette scene failed");
    }, onCoverReload: () => {
    } });
    stage.ensureAssets = () => {
      const wanted = new Set(cassetteSharedAssets);
      for (const phase of ["day", "sunset", "night"]) {
        if (phase !== stage.target && stage.weights[phase] <= 0) continue;
        for (const id of [...cassettePhaseAssets[phase].essential, ...cassettePhaseAssets[phase].extra]) wanted.add(id);
      }
      for (const id of stage.renderer.assetIds()) if (!wanted.has(id)) stage.renderer.releaseAsset(id);
      for (const id of wanted) if (!stage.renderer.hasAsset(id)) {
        const c = globalThis.__assetCanvas(id);
        stage.renderer.uploadAsset(id, c);
        c.dispose();
      }
    };
    stage.resize(width, height);
    stage.setSong({ kind: "synced", source: "local", offsetMs: 0, lines: input.lines }, input.durationMs, input.title);
    stage.setShowTranslation(input.translation !== false);
    stage.setFont(input.font || null, input.fontSize || 40, input.secondarySize || 22);
    stage.setPreferences({ timeOfDay: input.phase || "auto", retro: input.retro !== false, frameCap: 0, seed: input.seed || 0 });
    if (input.coverCanvas) stage.setCover(input.coverCanvas);
    return { lines: stage.timeline.length, kind: "cassette" };
  }
  function draw(ms, motion, elapsed, playing) {
    stage.setMotion(motion);
    stage.setClock({ positionMs: ms, updatedAtMs: globalThis.__now, playbackRate: 1, playing, offsetMs: 0 });
    stage.frame(elapsed);
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
    if (press) stage.press(x, y);
  }
  function cover(canvas2) {
    stage.setCover(canvas2);
  }
  return __toCommonJS(cassette_entry_exports);
})();
