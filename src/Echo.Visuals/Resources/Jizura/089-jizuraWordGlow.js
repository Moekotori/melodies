/* First-party lyric illumination over the existing glyph pipeline.
   One active-cut cache (eight texts, <=512 glyphs each), four light overlays per frame,
   no extra canvas, particle history, timer, PCM or alternate playback clock. */
(() => {
  'use strict';
  const compact = text => String(text).replace(/\s/gu, '');
  let currentCut = null, lightTime = -Infinity, lightCount = 0;
  const cache = new Map();
  // Alternate quiet whole-line / word treatments with an occasional detailed accent.
  const highlightModes = ['phrase', 'line', 'word', 'phrase', 'glyph'];
  J.afterglowWordGroups = (intervals, text, mode) => {
    if (mode !== 'phrase' && mode !== 'word') return intervals;
    const chars = [...compact(text)], grouped = [];
    const cjk = word => /^[\p{Script=Han}\p{Script=Hiragana}\p{Script=Katakana}\p{Script=Hangul}]+$/u.test(chars.slice(word.from, word.to).join(''));
    for (const word of intervals) {
      const prior = grouped[grouped.length - 1];
      if (prior && word.from === prior.to && word.to - prior.from <= 4 && word.start - prior.end <= 0.14 && cjk(prior) && cjk(word)) {
        prior.to = word.to; prior.end = word.end;
      } else grouped.push({ ...word });
    }
    return grouped;
  };
  J.clearAfterglowWordEffects = () => { currentCut = null; cache.clear(); lightTime = -Infinity; lightCount = 0; };
  J.bindAfterglowWordEffects = (plan, lines) => {
    J.clearAfterglowWordEffects();
    const cursors = new Map();
    const groupedByLine = new Map();
    for (const cut of plan.cuts) {
      const line = lines[cut.line];
      if (line && Number.isFinite(line.start) && Number.isFinite(line.end) && line.end > line.start) {
        cut.echoLyric = line;
        cut.echoLyricHighlight = highlightModes[J.h(cut.seed, 5, 829) % highlightModes.length];
        const intervals = line.timings || [];
        if (cut.echoLyricHighlight === 'phrase' || cut.echoLyricHighlight === 'word') {
          if (!groupedByLine.has(line)) groupedByLine.set(line, J.afterglowWordGroups(intervals, line.text, 'phrase'));
          cut.echoLyricIntervals = groupedByLine.get(line);
        } else cut.echoLyricIntervals = intervals;
        const source = compact(line.text), chunk = compact(cut.text || ''), cursor = cursors.get(cut.line) || 0;
        let at = chunk ? source.indexOf(chunk, cursor) : -1;
        if (at < 0) at = source.indexOf(chunk);
        cut.echoLyricOffset = [...source.slice(0, Math.max(0, at))].length;
        if (at >= 0 && !cut.recap) cursors.set(cut.line, at + chunk.length);
        if (cut.companion && typeof cut.companion === 'object') {
          cut.companion.echoLyric = line;
          cut.companion.echoLyricHighlight = cut.echoLyricHighlight;
          cut.companion.echoLyricIntervals = cut.echoLyricIntervals;
          const companionAt = source.indexOf(compact(cut.companion.text || ''), Math.max(0, at));
          cut.companion.echoLyricOffset = [...source.slice(0, Math.max(0, companionAt))].length;
        }
      }
    }
  };
  J.afterglowWordState = (start, end, time, motion = true) => {
    const progress = J.clamp((time - start) / Math.max(0.005, end - start));
    const active = time >= start && time < end;
    const pulse = active && motion ? Math.sin(progress * Math.PI) : 0;
    return { progress, active, pulse, sung: time >= end };
  };
  const mapping = (cut, item) => {
    if (currentCut !== cut) { currentCut = cut; cache.clear(); }
    if (String(item.text).length > 1024) return null;
    const key = `${item.mi || 0}|${item.text}`;
    if (cache.has(key)) return cache.get(key);
    const line = cut.echoLyric, source = compact(line.text), chunk = compact(cut.text), target = compact(item.text);
    if (!target || target.length > 512) return null;
    const chunkAt = source.indexOf(chunk), near = [...target].length === 1 ? Math.max(0, Math.floor(item.mi || 0)) : 0;
    let at = chunk.indexOf(target, near); if (at < 0) at = chunk.indexOf(target);
    if (at < 0 || chunkAt < 0) return null; // Captions and unrelated decorative text keep their own treatment.
    const origin = (cut.echoLyricOffset ?? [...source.slice(0, chunkAt)].length) + [...chunk.slice(0, at)].length, total = [...source].length;
    const chars = [...String(item.text).replace(/\n/g, '')]; if (chars.length > 512) return null;
    const times = new Float64Array(chars.length * 2);
    let offset = 0;
    const intervals = cut.echoLyricIntervals || (Array.isArray(line.timings) ? line.timings : []);
    chars.forEach((ch, i) => {
      if (/\s/u.test(ch)) { times[i * 2] = times[i * 2 + 1] = NaN; return; }
      const position = origin + offset++;
      let start = line.start + (line.end - line.start) * position / total;
      let end = line.start + (line.end - line.start) * (position + 1) / total;
      let lo = 0, hi = Math.min(intervals.length, 256);
      while (lo < hi) { const mid = (lo + hi) >>> 1; if (intervals[mid].from <= position) lo = mid + 1; else hi = mid; }
      const word = intervals[lo - 1];
      if (word && position < word.to && Number.isFinite(word.start) && Number.isFinite(word.end) && word.end > word.start && word.to > word.from) {
        if (cut.echoLyricHighlight === 'word') { start = word.start; end = word.end; }
        else {
          const duration = (word.end - word.start) / (word.to - word.from);
          start = word.start + duration * (position - word.from); end = start + duration;
        }
      }
      times[i * 2] = start; times[i * 2 + 1] = end;
    });
    cache.set(key, times); while (cache.size > 8) cache.delete(cache.keys().next().value);
    return times;
  };
  const baseDraw = J.drawFx;
  J.drawFx = (env, item) => {
    if (env.pass !== 'main' || env.bgOnly || env.glyphLog || env.hideText || !env.cut?.echoLyric || !item.text || item.plain) return baseDraw(env, item);
    const time = Number.isFinite(J.echoPresentationTime) ? J.echoPresentationTime : env.t;
    const mode = env.cut.echoLyricHighlight || 'phrase';
    if (mode === 'line' || !env.cut.echoLyric.timings?.length) {
      const start = env.cut.echoLyric.timings?.[0]?.start ?? env.cut.echoLyric.start;
      item.alpha = (item.alpha ?? 1) * (0.84 + 0.16 * J.smooth(start, start + 0.35, time));
      return baseDraw(env, item);
    }
    const times = mapping(env.cut, item); if (!times) return baseDraw(env, item);
    const motion = J.echoMotion !== false, tier = J.echoTier || 0, dark = J.lum(env.sc.bg) < 0.45;
    const accent = [env.sc.accent, env.sc.accent2, env.sc.fg].find(c => J.contrast(c, env.sc.bg) >= 3) || env.sc.fg;
    const bright = dark ? J.mix(accent, '#FFFFFF', 0.8) : J.mix(accent, env.sc.fg, 0.3);
    const original = item.charFn, profile = J.h(env.cut.seed, 9, 817) % 3;
    const stateAt = i => Number.isFinite(times[i * 2]) ? J.afterglowWordState(times[i * 2], times[i * 2 + 1], time, motion && tier < 2 && mode === 'glyph') : null;
    let active = -1;
    item.charFn = (i, g, n) => {
      const prior = original?.(i, g, n), state = stateAt(i); if (!state) return prior;
      if (state.active && !prior?.hide && (prior?.a ?? 1) > 0.03) active = i;
      const lift = state.pulse * (profile === 2 ? 0.018 : 0.03) * item.size;
      const gain = mode === 'phrase' ? 0.94 : state.active ? 0.82 + 0.18 * J.E.outCubic(Math.min(1, state.progress * 5)) : state.sung ? 0.94 : 0.82;
      return { ...prior, ...(state.active && mode !== 'phrase' ? { color: accent } : {}),
        a: (prior?.a ?? 1) * gain,
        s: (prior?.s ?? 1) * (1 + state.pulse * 0.035), dy: (prior?.dy || 0) - lift };
    };
    const box = baseDraw(env, item);
    if (time !== lightTime) { lightTime = time; lightCount = 0; }
    if (active < 0 || mode === 'word' || tier >= 2 || item.pieceFn || times.length > 256 || lightCount >= (mode === 'glyph' ? 4 : 1) || (item.alpha ?? 1) < 0.15) return box;
    lightCount++;
    const lay = item._lay || J.layoutText(item), charFn = item.charFn, g = lay.find(glyph => glyph.i === active);
    const sheen = profile === 1 ? [[0, bright], [0.38, accent], [0.52, bright], [0.76, env.sc.accent2], [1, bright]] : null;
    const overlay = { ...item, gradient: sheen, shadow: null, pre: null, post: null, echo: null, streak: null, extrude: null, pieceFn: null,
      blur: 0, pattern: null, patternBg: null, _lay: lay, fill: true, stroke: 0,
      charFn(i, glyph, count) {
        const prior = charFn(i, glyph, count), state = stateAt(i);
        if (!state?.active || prior?.hide) return { hide: true };
        const sweep = state.progress * 1.3;
        const nativeClip = prior?.clipX || [-0.7, 0.7];
        return { ...prior, color: bright, a: (prior?.a ?? 1) * (mode === 'phrase' ? 0.4 : 0.75),
          clipX: [Math.max(-0.65, nativeClip[0]), Math.min(-0.65 + sweep, nativeClip[1])] };
      } };
    J.drawItem(env, overlay);
    if (!motion || mode !== 'glyph' || !g || !Number.isFinite(g.x)) return box;
    const state = stateAt(active), local = charFn(active, g, lay.N), size = item.size;
    const x = (g.x + g.vx) * (item.sx || 1) + (local?.dx || 0), y = (g.y + g.vy) * (item.sy || 1) + (local?.dy || 0);
    const radius = Math.min(64, size * (profile === 1 ? 0.09 : 0.14));
    const ctx = env.ctx; ctx.save();
    try {
      ctx.translate(item.x, item.y); if (item.rot) ctx.rotate(item.rot * J.DEG); if (item.skew) ctx.transform(1, 0, Math.tan(item.skew * J.DEG), 1, 0, 0);
      ctx.globalAlpha = (item.alpha ?? 1) * state.pulse * (dark ? 0.75 : 0.45);
      const beam = ctx.createRadialGradient(x, y + size * 0.4, 0, x, y + size * 0.4, radius * 2);
      beam.addColorStop(0, J.rgba(accent, 0.5)); beam.addColorStop(1, J.rgba(accent, 0));
      ctx.fillStyle = beam; ctx.beginPath(); ctx.arc(x, y + size * 0.4, radius * 2, 0, Math.PI * 2); ctx.fill();
      ctx.strokeStyle = bright; ctx.lineWidth = Math.max(1, size * 0.012); ctx.lineCap = 'round';
      if (profile === 0) {
        const ribbon = ctx.createLinearGradient(x - size * 1.2, 0, x + size * 0.35, 0);
        ribbon.addColorStop(0, J.rgba(accent, 0)); ribbon.addColorStop(0.75, J.rgba(accent, 0.65)); ribbon.addColorStop(1, bright);
        ctx.strokeStyle = ribbon;
        for (let k = 0; k < 3; k++) { const yy = y + size * (0.53 + k * 0.035);
          ctx.beginPath(); ctx.moveTo(x - size * (1.2 - k * 0.1), yy);
          ctx.bezierCurveTo(x - size * 0.8, yy + size * 0.06, x - size * 0.15, yy - size * 0.035, x + size * 0.35, yy); ctx.stroke(); }
      } else if (profile === 1) {
        const cx = x + size * 0.24, cy = y + size * 0.34, r = size * (0.1 + state.pulse * 0.025);
        ctx.beginPath(); ctx.moveTo(cx - r, cy); ctx.lineTo(cx, cy - r * 1.7); ctx.lineTo(cx + r, cy); ctx.lineTo(cx, cy + r * 1.7); ctx.closePath(); ctx.stroke();
        ctx.strokeStyle = env.sc.accent2; ctx.beginPath(); ctx.moveTo(cx - r * 2.5, cy - r * 0.3); ctx.lineTo(cx + r * 2.8, cy + r * 0.3); ctx.stroke();
      } else {
        const tail = ctx.createLinearGradient(x - size * 0.85, y + size * 0.55, x + size * 0.2, y + size * 0.18);
        tail.addColorStop(0, J.rgba(accent, 0)); tail.addColorStop(1, bright); ctx.strokeStyle = tail;
        for (let k = 0; k < 2; k++) { ctx.beginPath(); ctx.moveTo(x - size * (0.85 - k * 0.1), y + size * (0.55 + k * 0.04));
          ctx.quadraticCurveTo(x - size * 0.25, y + size * 0.5, x + size * 0.2, y + size * 0.18); ctx.stroke(); }
      }
      if (tier === 0) for (let k = 0; k < 3; k++) {
        const age = (state.progress + k / 3) % 1, angle = k * 2.4 + (profile === 2 ? -0.7 : env.cut.seed % 3);
        const sx = x + Math.cos(angle) * size * age * 0.42, sy = y - size * age * 0.34;
        ctx.globalAlpha = (item.alpha ?? 1) * state.pulse * (1 - age) * 0.6;
        ctx.beginPath(); ctx.moveTo(sx - 2, sy); ctx.lineTo(sx + 2, sy); ctx.moveTo(sx, sy - 2); ctx.lineTo(sx, sy + 2); ctx.stroke();
      }
    } finally { ctx.restore(); }
    return box;
  };
})();
