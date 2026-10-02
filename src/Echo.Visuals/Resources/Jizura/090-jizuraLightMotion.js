/* Cheap, continuous line entrances/exits for Afterglow. No text snapshots, blur filters or extra layers:
   everything below is position / opacity / scale, or a per-glyph stagger of the same, on the glyph pipeline that already draws the line.
   The family is deliberately small and soft; a line picks one by its seed, and never the same one twice in a row. */
(() => {
  'use strict';
  const still = () => J.echoMotion === false;
  const clamp = v => (v < 0 ? 0 : v > 1 ? 1 : v);
  const quint = v => 1 - Math.pow(1 - clamp(v), 5);          // long, silky settle
  const sine = v => 0.5 - 0.5 * Math.cos(Math.PI * clamp(v)); // slow start, slow end
  const smooth = v => { v = clamp(v); return v * v * (3 - 2 * v); };
  const HIDE = Object.freeze({ hide: true });
  const stagger = (p, k, spread) => clamp((p - spread * k) / (1 - spread));
  const order = (i, n) => (n > 1 ? i / (n - 1) : 0);
  const side = item => ((item.seed | 0) % 2 ? 1 : -1);

  /* ---- entrances ---- */
  J.register('enter', 'echoLuminousRise', {
    name: 'Luminous rise', tags: ['calm', 'emotional'], w: 1,
    apply(env, item, p) {
      if (still()) return;
      const e = quint(p); item.alpha = (item.alpha ?? 1) * smooth(p * 1.3);
      item.y += (1 - e) * item.size * 0.16; item.size *= 0.985 + 0.015 * e;
    },
  }, 'echo');
  J.register('enter', 'echoPrismUnfold', {
    name: 'Prism unfold', tags: ['graphic', 'editorial'], w: 1,
    apply(env, item, p) {
      if (still()) return;
      const e = quint(p); item.alpha = (item.alpha ?? 1) * smooth(p * 1.25);
      item.sx = (item.sx || 1) * (0.94 + e * 0.06);
      item.y += (1 - e) * item.size * 0.07;
    },
  }, 'echo');
  J.register('enter', 'echoCometArrival', {
    name: 'Comet arrival', tags: ['pop', 'emotional'], w: 1,
    apply(env, item, p) {
      if (still()) return;
      const e = quint(p); item.alpha = (item.alpha ?? 1) * smooth(p * 1.3);
      item.x -= (1 - e) * item.size * 0.2; item.y += (1 - e) * item.size * 0.09;
    },
  }, 'echo');
  J.register('enter', 'echoSoftSlide', {
    name: 'Soft slide', tags: ['calm', 'editorial'], w: 1,
    apply(env, item, p) {
      if (still()) return;
      const e = quint(p); item.alpha = (item.alpha ?? 1) * smooth(p * 1.2);
      item.x += side(item) * (1 - e) * item.size * 0.34;
    },
  }, 'echo');
  /* the line is written in light, glyph after glyph, each settling from slightly below */
  J.register('enter', 'echoGlyphBloom', {
    name: 'Glyph bloom', tags: ['calm', 'emotional', 'pop'], w: 1,
    apply(env, item, p) {
      if (still()) return;
      const size = item.size;
      item.charFns.push((i, g, n) => {
        const q = stagger(p, order(i, n), 0.62);
        if (q <= 0) return HIDE; if (q >= 1) return null;
        const e = quint(q);
        return { a: sine(Math.min(1, q * 1.35)), dy: (1 - e) * size * 0.14, s: 0.955 + 0.045 * e };
      });
    },
  }, 'echo');

  /* ---- exits ---- */
  J.register('exit', 'echoLightRelease', {
    name: 'Light release', tags: ['calm', 'emotional'], w: 1,
    apply(env, item, p) {
      if (still()) return;
      const e = sine(p);
      item.alpha = (item.alpha ?? 1) * Math.pow(1 - e, 1.15); item.y -= e * item.size * 0.07;
    },
  }, 'echo');
  J.register('exit', 'echoSinkOut', {
    name: 'Sink out', tags: ['calm', 'emotional'], w: 1,
    apply(env, item, p) {
      if (still()) return;
      const e = sine(p);
      item.alpha = (item.alpha ?? 1) * (1 - e); item.y += e * item.size * 0.05; item.size *= 1 - 0.015 * e;
    },
  }, 'echo');
  J.register('exit', 'echoGlyphEvaporate', {
    name: 'Glyph evaporate', tags: ['calm', 'emotional', 'pop'], w: 1,
    apply(env, item, p) {
      if (still()) return;
      const size = item.size;
      item.charFns.push((i, g, n) => {
        const q = stagger(p, order(i, n), 0.55);
        if (q <= 0) return null; if (q >= 1) return HIDE;
        const e = smooth(q);
        return { a: 1 - e, dy: -e * size * 0.1, s: 1 + 0.02 * e };
      });
    },
  }, 'echo');

  /* ---- picking ---- */
  const glyphs = text => [...String(text || '').replace(/\s/gu, '')].length;
  const SOFT_IN = ['echoLuminousRise', 'echoLuminousRise', 'echoSoftSlide', 'echoPrismUnfold', 'echoCometArrival'];
  const SOFT_OUT = ['echoLightRelease', 'echoLightRelease', 'echoSinkOut'];
  J.bindAfterglowLightMotion = plan => {
    let lastIn = '', lastOut = '';
    for (const cut of plan.cuts) {
      /* the title card (track and artist before the first line) opens in light and leaves slowly */
      if (cut.layout === 'title' && cut.dur >= 1.2 && cut.text?.trim()) {
        cut.enter = 'echoGlyphBloom'; cut.inDur = Math.min(cut.dur * 0.4, 1.1);
        cut.exit = 'echoLightRelease'; cut.outDur = Math.min(cut.dur * 0.3, 0.8);
        continue;
      }
      if (!cut.echoLyric || !cut.text?.trim() || cut.layout === 'interlude' || cut.dur < 0.3) continue;
      const n = glyphs(cut.text), roll = (salt, count) => J.h(cut.seed, 9, 817 + salt) % count;
      if (cut.enter === 'cut' || cut.enter === 'blur') {
        const pool = (n >= 4 && cut.dur >= 0.9 ? ['echoGlyphBloom', 'echoGlyphBloom', ...SOFT_IN] : SOFT_IN).filter(name => name !== lastIn);
        cut.enter = pool[roll(0, pool.length)]; lastIn = cut.enter;
        const span = cut.enter === 'echoGlyphBloom' ? Math.min(0.78, 0.4 + n * 0.035) : Math.min(0.5, Math.max(0.3, cut.dur * 0.22));
        cut.inDur = Math.min(cut.dur * 0.42, span);
      }
      if (['cut', 'blur', 'dissolve'].includes(cut.exit)) {
        const pool = (n >= 4 && cut.dur >= 1.2 ? ['echoGlyphEvaporate', ...SOFT_OUT] : SOFT_OUT).filter(name => name !== lastOut);
        cut.exit = pool[roll(1, pool.length)]; lastOut = cut.exit;
        cut.outDur = Math.min(cut.dur * 0.24, cut.exit === 'echoGlyphEvaporate' ? 0.5 : Math.max(0.24, Math.min(0.4, cut.dur * 0.16)));
      }
    }
  };
})();
