/* ECHO first-party toolkit for the Afterglow looks: shared canvas helpers, a small cache for static art layers,
   and two text treatments (gold foil, neon tube). Loaded after the JIZURA engine, before the look backdrops. */
(() => {
  'use strict';
  const E = J.E, TAU = Math.PI * 2, clamp = J.clamp, mix = J.mix, rgba = J.rgba;

  const kit = {
    E, TAU, clamp, mix, rgba,
    U: env => Math.min(env.W, env.H),
    isDark: colour => J.lum(colour) < 0.45,
    wrap: (value, span) => ((value % span) + span) % span,
  };
  /* the brightest scheme colour that stands apart from the ground: used to tint glows */
  kit.glowOf = sc => {
    const base = J.lum(sc.bg);
    for (const colour of [sc.accent, sc.accent2, sc.ghostA, sc.ghostB, sc.fg]) if (colour && J.lum(colour) > base + 0.25) return colour;
    return sc.fg;
  };
  /* scheme colours that read against the ground */
  kit.hues = sc => {
    const out = [];
    for (const colour of [sc.accent, sc.accent2, sc.ghostA, sc.ghostB, sc.fg]) {
      if (!colour || J.contrast(colour, sc.bg) < 1.25) continue;
      if (!out.some(known => known.toLowerCase() === colour.toLowerCase())) out.push(colour);
    }
    return out.length ? out : [sc.fg];
  };

  /* seconds since this backdrop started: consecutive cuts of one line share the same backdrop and seed */
  const runStart = new WeakMap();
  kit.sinceStart = env => {
    const cut = env.cut;
    if (!cut) return env.t;
    let start = runStart.get(cut);
    if (start == null) {
      start = cut.start;
      const cuts = (env.plan && env.plan.cuts) || [], seed = (cut.bgP || {}).seed;
      for (let i = (cut.index | 0) - 1; i >= 0 && i < cuts.length; i--) {
        const prev = cuts[i];
        if (prev.bg === cut.bg && (prev.bgP || {}).seed === seed && Math.abs(prev.end - start) < 0.06) start = prev.start; else break;
      }
      runStart.set(cut, start);
    }
    return env.t - start;
  };
  kit.fade = (env, seconds = 0.7) => E.outCubic(clamp(kit.sinceStart(env) / seconds));

  kit.vertical = (ctx, y0, y1, stops) => {
    const gradient = ctx.createLinearGradient(0, y0, 0, y1);
    for (const [at, colour] of stops) gradient.addColorStop(at, colour);
    return gradient;
  };
  kit.glowDisc = (ctx, x, y, radius, colour, alpha) => {
    const gradient = ctx.createRadialGradient(x, y, 0, x, y, radius);
    gradient.addColorStop(0, rgba(colour, alpha)); gradient.addColorStop(0.45, rgba(colour, alpha * 0.32)); gradient.addColorStop(1, rgba(colour, 0));
    ctx.fillStyle = gradient; ctx.fillRect(x - radius, y - radius, radius * 2, radius * 2);
  };
  kit.star = (ctx, x, y, r, arms = 4, pinch = 0.16) => {
    ctx.beginPath();
    for (let i = 0; i < arms * 2; i++) {
      const angle = (i / (arms * 2)) * TAU - Math.PI / 2, radius = i % 2 ? r * pinch : r;
      if (i) ctx.lineTo(x + Math.cos(angle) * radius, y + Math.sin(angle) * radius); else ctx.moveTo(x + Math.cos(angle) * radius, y + Math.sin(angle) * radius);
    }
    ctx.closePath();
  };
  kit.polygon = (ctx, x, y, r, sides, rotation = 0) => {
    ctx.beginPath();
    for (let i = 0; i < sides; i++) {
      const angle = rotation + (i / sides) * TAU;
      if (i) ctx.lineTo(x + Math.cos(angle) * r, y + Math.sin(angle) * r); else ctx.moveTo(x + Math.cos(angle) * r, y + Math.sin(angle) * r);
    }
    ctx.closePath();
  };

  /* Static art (skylines, ridges, cloud banks…) is painted once into an offscreen layer and only moved afterwards.
     The key must name everything the picture depends on (seed, colours); a handful of layers stay resident. */
  const LAYERS = new Map();
  kit.layer = (env, key, resolution, draw) => {
    if (J.echoMinRes) resolution = Math.max(resolution, J.echoMinRes);
    const q = (env.scale || 1) * resolution, w = Math.max(8, Math.ceil(env.W * q - (resolution >= 1 ? 1e-3 : 0))), h = Math.max(8, Math.ceil(env.H * q - (resolution >= 1 ? 1e-3 : 0))), id = `${key}|${w}x${h}`;
    let canvas = LAYERS.get(id);
    if (canvas) { LAYERS.delete(id); LAYERS.set(id, canvas); return canvas; }
    canvas = document.createElement('canvas'); canvas.width = w; canvas.height = h;
    const x = canvas.getContext('2d');
    x.setTransform(w / env.W, 0, 0, h / env.H, 0, 0);
    draw(x, env.W, env.H);
    LAYERS.set(id, canvas);
    while (LAYERS.size > 20) { const oldest = LAYERS.keys().next().value, old = LAYERS.get(oldest); old.width = old.height = 0; LAYERS.delete(oldest); }
    return canvas;
  };
  /* a tiling strip: `across` × the frame width, drawn scrolled by `offset` design units so it loops seamlessly */
  kit.scroll = (ctx, canvas, env, offset, across = 1, y = 0, alpha = 1) => {
    const span = env.W * across, o = kit.wrap(offset, span);
    ctx.globalAlpha *= alpha;
    for (let x = -o; x < env.W; x += span) ctx.drawImage(canvas, x, y, span, env.H);
    ctx.globalAlpha /= alpha || 1;
  };

  /* ---- clouds: one shared painter so every scene's sky has the same clean, hand-painted look ----
     A flat-bottomed cumulus is one solid silhouette (a run of big round bumps on a rounded base) shaded as a whole:
     a vertical body gradient, a bright crescent along the edge facing the light, a soft cool crescent along the underside.
     tone = { light, body, shade, belly } ; side = +1 when the light comes from the right ; flat = a far, quiet cloud (no crescents). */
  const CLOUD_CANVAS = [];
  const cloudCanvas = (i, w, h) => {
    const c = CLOUD_CANVAS[i] || (CLOUD_CANVAS[i] = document.createElement('canvas'));
    if (c.width < w || c.height < h) { c.width = Math.max(c.width, w); c.height = Math.max(c.height, h); }
    return c;
  };
  kit.cloud = (ctx, cx, cy, w, h, seed, tone, side = 1, flat = false) => {
    const q = ctx.getTransform().a || 1, pad = h * 0.3, tw = Math.ceil((w + pad * 2) * q), th = Math.ceil((h + pad * 2) * q);
    const A = cloudCanvas(0, tw, th), B = cloudCanvas(1, tw, th), C = cloudCanvas(2, tw, th);
    const a = A.getContext('2d'), b = B.getContext('2d'), c = C.getContext('2d');
    for (const g of [a, b, c]) { g.setTransform(1, 0, 0, 1, 0, 0); g.globalCompositeOperation = 'source-over'; g.globalAlpha = 1; g.filter = 'none'; g.clearRect(0, 0, A.width, A.height); g.setTransform(q, 0, 0, q, 0, 0); }
    const base = pad + h * 0.92, lobes = [], n = 5 + (J.h(seed, 3) % 3), lit = side < 0 ? -1 : 1;
    for (let i = 0; i < n; i++) {
      const u = (i + 0.5 + (J.r(seed, i, 5) - 0.5) * 0.55) / n, dome = Math.pow(Math.sin(Math.PI * Math.min(0.98, Math.max(0.02, u))), 0.75);
      const r = h * (0.13 + 0.36 * Math.pow(dome, 1.3) * (0.6 + 0.4 * J.r(seed, i, 6)));
      lobes.push({ x: pad + w * (0.09 + 0.82 * u), y: base - r * 0.86, r });
    }
    /* silhouette on A */
    a.fillStyle = '#FFFFFF';
    a.beginPath(); a.moveTo(pad + w * 0.05 + h * 0.11, base); a.arc(pad + w * 0.05 + h * 0.11, base - h * 0.11, h * 0.11, Math.PI / 2, Math.PI * 1.5); a.lineTo(pad + w * 0.95 - h * 0.11, base - h * 0.22);
    a.arc(pad + w * 0.95 - h * 0.11, base - h * 0.11, h * 0.11, -Math.PI / 2, Math.PI / 2); a.closePath(); a.fill();
    for (const l of lobes) { a.beginPath(); a.arc(l.x, l.y, l.r, 0, TAU); a.fill(); }
    a.globalCompositeOperation = 'destination-in'; a.fillRect(0, 0, tw / q, base); a.globalCompositeOperation = 'source-over';
    /* body on B */
    b.drawImage(A, 0, 0, tw, th, 0, 0, tw / q, th / q);
    b.globalCompositeOperation = 'source-in';
    const top = base - h * 0.98, body = b.createLinearGradient(0, top, 0, base);
    if (flat) { body.addColorStop(0, tone.body); body.addColorStop(1, kit.mix(tone.body, tone.shade, 0.4)); }
    else { body.addColorStop(0, kit.mix(tone.light, tone.body, 0.45)); body.addColorStop(0.5, tone.body); body.addColorStop(1, kit.mix(tone.body, tone.shade, 0.55)); }
    b.fillStyle = body; b.fillRect(0, 0, tw / q, th / q);
    b.globalCompositeOperation = 'source-over';
    if (!flat) {
      const s = h * 0.075;
      /* bright crescent on the lit edge */
      c.drawImage(A, 0, 0, tw, th, 0, 0, tw / q, th / q); c.globalCompositeOperation = 'source-in'; c.fillStyle = tone.light; c.fillRect(0, 0, tw / q, th / q);
      c.globalCompositeOperation = 'destination-out'; c.drawImage(A, 0, 0, tw, th, -lit * s * 1.2, s * 1.0, tw / q, th / q); c.globalCompositeOperation = 'source-over';
      if ('filter' in b) b.filter = `blur(${Math.max(0.4, h * 0.008 * q).toFixed(2)}px)`;
      b.globalAlpha = 0.85; b.drawImage(C, 0, 0, tw, th, 0, 0, tw / q, th / q); b.globalAlpha = 1; b.filter = 'none';
      /* cool crescent on the underside and the side away from the light */
      c.setTransform(1, 0, 0, 1, 0, 0); c.clearRect(0, 0, C.width, C.height); c.setTransform(q, 0, 0, q, 0, 0);
      c.drawImage(A, 0, 0, tw, th, 0, 0, tw / q, th / q); c.globalCompositeOperation = 'source-in'; c.fillStyle = tone.shade; c.fillRect(0, 0, tw / q, th / q);
      c.globalCompositeOperation = 'destination-out'; c.drawImage(A, 0, 0, tw, th, lit * s * 0.9, -s * 2.6, tw / q, th / q); c.globalCompositeOperation = 'source-over';
      if ('filter' in b) b.filter = `blur(${Math.max(0.8, h * 0.035 * q).toFixed(2)}px)`;
      b.globalCompositeOperation = 'source-atop'; b.globalAlpha = (tone.belly == null ? 0.6 : tone.belly) * 0.8; b.drawImage(C, 0, 0, tw, th, 0, 0, tw / q, th / q); b.globalAlpha = 1; b.filter = 'none'; b.globalCompositeOperation = 'source-over';
      /* faint fold under each bump so the bumps read as separate lobes without turning into bubbles */
      b.globalCompositeOperation = 'source-atop';
      for (const l of lobes) {
        if (l.r < h * 0.2) continue;
        b.strokeStyle = kit.rgba(tone.shade, 0.12); b.lineWidth = Math.max(0.6, h * 0.012); b.lineCap = 'round';
        b.beginPath(); b.arc(l.x - lit * l.r * 0.06, l.y + l.r * 0.06, l.r * 0.92, Math.PI * (lit > 0 ? 0.62 : 0.02), Math.PI * (lit > 0 ? 0.98 : 0.38)); b.stroke();
      }
      b.globalCompositeOperation = 'source-over';
    }
    if ('filter' in ctx) ctx.filter = `blur(${Math.max(0.25, h * 0.0035 * q).toFixed(2)}px)`;
    ctx.drawImage(B, 0, 0, tw, th, cx - pad, cy - pad, w + pad * 2, h + pad * 2);
    if ('filter' in ctx) ctx.filter = 'none';
  };
  /* a cloud from a single colour (the cute scenes), baked once per size/colour into a small tile and then only blitted.
     (x, y) is the middle of the flat base, like shapes.puff. */
  const PUFFS = new Map();
  kit.puffCloud = (ctx, x, y, w, h, colour, alpha = 1, seed = 1, side = 1, dark = false) => {
    const q = Math.max(0.5, ctx.getTransform().a || 1), pad = h * 0.3;
    const key = [colour, dark, Math.round(w * q / 6), Math.round(h * q / 6), seed % 97, side].join('|');
    let c = PUFFS.get(key);
    if (c) { PUFFS.delete(key); PUFFS.set(key, c); }
    else {
      c = document.createElement('canvas'); c.width = Math.ceil((w + pad * 2) * q); c.height = Math.ceil((h + pad * 2) * q);
      const g = c.getContext('2d'); g.setTransform(q, 0, 0, q, 0, 0);
      const tone = dark
        ? { light: kit.mix(colour, '#FFFFFF', 0.4), body: colour, shade: kit.mix(colour, '#0B0B30', 0.5), belly: 0.6 }
        : { light: kit.mix(colour, '#FFFFFF', 0.7), body: colour, shade: kit.mix(colour, '#7A84C8', 0.42), belly: 0.62 };
      kit.cloud(g, pad, pad, w, h, seed, tone, side, false);
      PUFFS.set(key, c);
      while (PUFFS.size > 60) { const k = PUFFS.keys().next().value, old = PUFFS.get(k); old.width = old.height = 0; PUFFS.delete(k); }
    }
    const a = ctx.globalAlpha; ctx.globalAlpha = a * alpha;
    ctx.drawImage(c, x - w / 2 - pad, y - h * 0.92 - pad, w + pad * 2, h + pad * 2);
    ctx.globalAlpha = a;
  };

  /* a thin drifting veil of cloud: a few long, soft ellipses */
  kit.wisp = (ctx, cx, cy, w, h, colour, alpha, seed = 1) => {
    for (let k = 0; k < 4; k++) {
      const ex = cx + (J.r(seed, k, 1) - 0.5) * w * 0.5, ey = cy + (J.r(seed, k, 2) - 0.5) * h, ew = w * (0.35 + 0.35 * J.r(seed, k, 3)), eh = h * (0.4 + 0.4 * J.r(seed, k, 4));
      ctx.save(); ctx.translate(ex, ey); ctx.scale(1, eh / ew);
      const g = ctx.createRadialGradient(0, 0, 0, 0, 0, ew / 2); g.addColorStop(0, kit.rgba(colour, alpha)); g.addColorStop(0.6, kit.rgba(colour, alpha * 0.45)); g.addColorStop(1, kit.rgba(colour, 0));
      ctx.fillStyle = g; ctx.beginPath(); ctx.arc(0, 0, ew / 2, 0, TAU); ctx.fill(); ctx.restore();
    }
  };

  /* the photographic finish every painted scene gets: a soft tinted vignette, a gentle warm-top / cool-bottom grade (both cached in one small layer)
     and a fine live grain. Pixel scenes and the kabuki screen keep their own look. */
  let grainTile = null;
  const grain = () => {
    if (grainTile) return grainTile;
    grainTile = document.createElement('canvas'); grainTile.width = grainTile.height = 192;
    const g = grainTile.getContext('2d'), id = g.createImageData(192, 192); let seed = 20240929;
    const rnd = () => { seed = (seed * 1664525 + 1013904223) >>> 0; return seed / 4294967296; };
    for (let i = 0; i < 192 * 192; i++) { const v = rnd() > 0.5 ? 255 : 0; id.data[i * 4] = id.data[i * 4 + 1] = id.data[i * 4 + 2] = v; id.data[i * 4 + 3] = Math.floor(rnd() * 255); }
    g.putImageData(id, 0, 0); return grainTile;
  };
  const NO_FINISH = /^(echoPixel|echoGoldWa)/;
  kit.finish = (env, ctx, key) => {
    if (NO_FINISH.test(key)) return;
    const { W, H, sc } = env, dark = kit.isDark(sc.bg), e = kit.fade(env, 0.8), s = env.scale || 1;
    const frame = Math.floor(env.t * 10) % 3;                       /* three baked veils (vignette + grade + a different grain each) played in turn */
    const veil = kit.layer(env, `finish|${sc.bg}|${sc.fg}|${dark}|${frame}`, 0.5, (x, w, h) => {
      const edge = dark ? kit.mix(sc.bg, '#000000', 0.75) : kit.mix(sc.bg, sc.fg, 0.4);
      const vg = x.createRadialGradient(w * 0.5, h * 0.48, Math.min(w, h) * 0.32, w * 0.5, h * 0.5, Math.max(w, h) * 0.78);
      vg.addColorStop(0, kit.rgba(edge, 0)); vg.addColorStop(1, kit.rgba(edge, dark ? 0.36 : 0.16)); x.fillStyle = vg; x.fillRect(0, 0, w, h);
      const lg = x.createLinearGradient(0, 0, 0, h);
      lg.addColorStop(0, kit.rgba(dark ? '#4A5AB0' : '#FFE6C2', dark ? 0.05 : 0.07)); lg.addColorStop(0.5, kit.rgba('#FFFFFF', 0)); lg.addColorStop(1, kit.rgba(dark ? '#000000' : '#5A6AA0', dark ? 0.12 : 0.06));
      x.fillStyle = lg; x.fillRect(0, 0, w, h);
      const cw = x.canvas.width, ch = x.canvas.height; x.save(); x.setTransform(1, 0, 0, 1, 0, 0); x.globalAlpha = dark ? 0.05 : 0.038; x.translate(-frame * 61, -frame * 97);
      x.fillStyle = x.createPattern(grain(), 'repeat'); x.fillRect(0, 0, cw + 192, ch + 192); x.restore();
    });
    ctx.globalAlpha = e; ctx.drawImage(veil, 0, 0, W, H);
  };
  kit.registerBg = (key, name, tags, draw, finish = true) => J.register('bg', key, {
    name, tags, w: 1,
    plan: rng => ({ seed: rng.int(1, 1e9) }),
    draw(env, P) { const ctx = env.ctx; ctx.save(); try { draw(env, P || {}, ctx); ctx.globalCompositeOperation = 'source-over'; if (finish) kit.finish(env, ctx, key); } finally { ctx.restore(); } },
  }, 'echo');
  /* a foreground layer in front of the lyric: kept light so the text always reads */
  kit.registerFront = (key, name, draw) => J.register('decor', key, {
    name, layer: 'front', w: 0.5, tags: ['emotional'],
    draw(env, bb, P) {
      if (env.pass && env.pass !== 'main') return;
      const ctx = env.ctx; ctx.save(); try { draw(env, P || {}, ctx, bb); } finally { ctx.restore(); }
    },
  }, 'echo');
  kit.frontSeed = (cut, project) => J.h((project && project.seed) || 1, (cut.line | 0) + 11, 406);
  J.echo = kit;

  /* ---------- text treatments (mirroring the extra pack's item helpers, which are private to it) ---------- */
  const ctr = (a, b) => J.contrast(a, b);
  const firstOK = (list, test, fb) => { for (const c of list) if (c && test(c)) return c; return fb; };
  const alive = (it, amin = 0.9) => it.fill !== false && (it.alpha ?? 1) >= amin && !!it.text && it.size > 1;
  const addPre = (it, f) => { const p = it.pre; it.pre = p ? (e, i) => { p(e, i); f(e, i); } : f; };
  const sized = (it, env, f) => { f(it, env); addPre(it, (e, i) => f(i, e)); };
  const inPieces = (env, it) => {
    const c = env.cut, en = J.ENTER[it.enter || c.enter], ex = J.EXIT[it.exit || c.exit];
    if (en && en.pieces && env.lt - (it.delay || 0) < c.inDur * 1.3 + 0.05) return true;
    if (ex && ex.pieces && c.outDur > 0 && env.lt > c.dur - c.outDur - 0.3) return true;
    return false;
  };
  const withFx = (env, it, fn) => {
    const ctx = env.ctx, W = env.W, H = env.H;
    ctx.save();
    if (it.blur > 0.4 && env.allowFilter) ctx.filter = `blur(${(it.blur * env.scale).toFixed(1)}px)`;
    if (it.clip) { ctx.beginPath(); ctx.rect(it.clip[0], -H, it.clip[1] - it.clip[0], H * 3); ctx.clip(); }
    if (it.clipY) { ctx.beginPath(); ctx.rect(-W, it.clipY[0], W * 3, it.clipY[1] - it.clipY[0]); ctx.clip(); }
    if (it.clipFn) { ctx.beginPath(); it.clipFn(ctx, env, it); ctx.clip(); }
    try { fn(); } finally { ctx.restore(); }
  };
  const bare = (i, extra) => Object.assign({}, i, { pre: null, post: null, echo: null, streak: null, shadow: null, extrude: null, pattern: null, patternBg: null, gradient: null,
    pieceFn: null, strokeDash: null, strokeUnder: false, wipeBar: null, cursorAt: null, bands: null, vbands: null, clip: null, clipY: null, clipFn: null, blend: null, _lay: null }, extra || {});
  const tracing = i => i.dash != null && i.dash < 1;

  /* 金箔: beaten gold with a bright crest and a dark reflected band, keyed in deep umber. Only on dark grounds. */
  const GOLD = [[0.06, '#FFF4C9'], [0.3, '#F1CB68'], [0.5, '#A9741F'], [0.52, '#FBE7A0'], [0.78, '#D6A445'], [0.96, '#FFF0B5']];
  J.register('treat', 'echoGoldFoil', {
    name: '金箔', tags: ['emotional', 'editorial', 'graphic'], w: 1,
    plan: rng => ({ shimmer: rng.chance(0.7), k: rng.range(0.012, 0.018) }),
    apply(env, it, P) {
      if (!alive(it) || inPieces(env, it)) return;
      const sc = env.sc;
      if (J.lum(sc.bg) > 0.45) return;           // on paper grounds the ink colour stays
      it.gradient = GOLD;
      it.strokeColor = '#3B2409';
      sized(it, env, i => {
        i.stroke = Math.max(1.2, i.size * P.k);
        i.shadow = { color: 'rgba(255,196,80,0.55)', blur: i.size * 0.08, dx: 0, dy: 0 };
      });
      if (P.shimmer) {                            // a slow glint travelling across the glyphs
        const s0 = J.h(env.cut.seed, (it.mi | 0) + 5, 43);
        it.charFns.push((gi, g, n) => {
          const sweep = ((env.ltb * 0.55 + J.r(s0, 1)) % 2) * (n + 3) - 1.5, d = Math.abs(gi - sweep);
          return d < 1.1 ? { color: J.mix('#F1CB68', '#FFFFFF', 0.85 * (1 - d / 1.1)) } : null;
        });
      }
    },
  }, 'echo');

  /* ネオン管: a thin bright tube with a coloured bloom; a rare stutter. The extra pack's version needs its toggle. */
  J.register('treat', 'echoNeonTube', {
    name: 'ネオン管', tags: ['glitch', 'emotional', 'pop'], w: 1,
    plan: rng => ({ k: rng.range(0.024, 0.032), fl: rng.chance(0.75) }),
    apply(env, it, P) {
      if (!alive(it, 0.5) || inPieces(env, it)) return;
      const sc = env.sc, col = it.color || sc.fg, dk = J.lum(sc.bg) < 0.45;
      const gc = dk ? firstOK([sc.accent, sc.accent2, sc.ghostA, sc.ghostB], c => J.lum(c) > J.lum(sc.bg) + 0.15 && ctr(c, col) >= 1.2, col)
        : firstOK([sc.accent, sc.accent2, sc.ghostA, sc.ghostB], c => ctr(c, sc.bg) >= 1.8, col);
      const tube = dk ? J.mix(col, '#FFFFFF', 0.4) : col;
      it.fill = false; it.strokeColor = tube;
      sized(it, env, i => {
        i.stroke = Math.max(1.4, i.size * P.k);
        i.shadow = { color: J.rgba(gc, dk ? 1 : 0.7), blur: i.size * 0.07, dx: 0, dy: 0 };
      });
      addPre(it, (e, i) => {
        if (e.pass !== 'main' || tracing(i)) return;
        const s = i.size;
        const c = bare(i, { fill: false, stroke: s * P.k * 4.5, strokeColor: gc, alpha: (i.alpha ?? 1) * (dk ? 0.22 : 0.16), shadow: { color: J.rgba(gc, 0.9), blur: s * 0.16, dx: 0, dy: 0 } });
        withFx(e, i, () => J.drawItem(e, c));
      });
      if (P.fl) {
        const s0 = J.h(env.cut.seed, (it.mi | 0) + 3, 41);
        it.charFns.push(gi => { const r = J.r(s0, gi, env.step); return r < 0.03 ? { a: 0.22 } : r < 0.05 ? { a: 0.6 } : null; });
      }
    },
  }, 'echo');
})();

/* ECHO: a quieter interlude. Upstream shows a big seconds countdown in the middle of the frame during instrumental gaps;
   ECHO replaces it with slow expanding rings and a thin progress line (no digits). */
(() => {
  const L = J.LAYOUTS && J.LAYOUTS.interlude; if (!L || L.__echo) return; L.__echo = true;
  const base = L.render, clamp = J.clamp;
  L.plan = () => ({ variant: 'rings' });
  L.render = function (env) {
    const P = env.cut.params || {}; if (P.variant === 'quiet') return base.call(this, env);
    const { W, H, sc } = env, lb = env.ltb, lt = env.lt, dur = Math.max(0.1, env.cut.dur), sm = v => { v = clamp(v); return v * v * (3 - 2 * v); };
    const a = sm(lt / 0.9) * sm((dur - lt) / 0.9), U = Math.min(W, H), cx = W / 2, cy = H * 0.47, R = U * 0.095, prog = clamp(lt / dur);
    /* a calm veil so a busy scene steps back while the music plays on its own */
    env.rect(0, 0, W, H, sc.bg, 0.34 * a, false);
    /* slow ripples leaving a still centre */
    for (let k = 0; k < 3; k++) { const p = ((lb * 0.16 + k / 3) % 1 + 1) % 1, e = 1 - Math.pow(1 - p, 2); env.circle(cx, cy, R * (1.25 + 3.4 * e), null, k % 2 ? sc.accent2 || sc.sub : sc.sub, 1.2, (1 - p) * 0.4 * a, false); }
    /* the ring that fills as the gap passes, and the breath inside it */
    env.circle(cx, cy, R, null, sc.sub, 1.3, 0.28 * a, false);
    const n = Math.max(2, Math.round(80 * prog)), pts = []; for (let i = 0; i <= n; i++) { const an = -Math.PI / 2 + prog * Math.PI * 2 * i / n; pts.push([cx + Math.cos(an) * R, cy + Math.sin(an) * R]); }
    if (n >= 2) env.line(pts, sc.accent, 2.6, 0.9 * a, false);
    const breath = 0.5 + 0.5 * Math.sin(lb * 1.4);
    env.circle(cx, cy, R * (0.22 + 0.1 * breath), sc.accent, null, 0, (0.55 + 0.35 * breath) * a, true);
    env.circle(cx, cy, R * (0.55 + 0.08 * breath), null, sc.accent, 1, 0.22 * a, false);
    if (env.cut.text) env.draw({ text: env.cut.text, font: env.st.fonts.body[0], size: clamp(H * 0.021, 12, 22), x: cx, y: cy + R * 2.9, track: 0.5, color: sc.sub, alpha: a * 0.85, ghost: false });
    return { x0: W * 0.35, x1: W * 0.65, y0: H * 0.3, y1: H * 0.7, cx, cy, boxes: [] };
  };
})();

/* ECHO: smoother hand-overs between lines and backdrops. The upstream engine switches a backdrop (or a hard-cut line) in a single
   frame; here the previous cut's resting frame is kept and dissolved away over the first moments of the next one, and when a
   backdrop is left for a gap it fades out instead of vanishing. The engine itself is untouched: this wraps Renderer.frame. */
(() => {
  const R = J.Renderer && J.Renderer.prototype; if (!R || R.__echoDissolve) return; R.__echoDissolve = true;
  const base = R.frame, ease = v => { v = Math.min(1, Math.max(0, v)); return v * v * (3 - 2 * v); };
  const mk = (w, h) => { const c = document.createElement('canvas'); c.width = w; c.height = h; return c; };
  const store = new WeakMap();
  const lastEnded = (cuts, t) => { let lo = 0, hi = cuts.length - 1, ans = -1; while (lo <= hi) { const m = (lo + hi) >> 1; if (cuts[m].end <= t) { ans = m; lo = m + 1; } else hi = m - 1; } return ans < 0 ? null : cuts[ans]; };
  const scOf = (plan, c) => plan.style.schemes[c.scheme % plan.style.schemes.length];
  /* How one picture gives way to the next. A backdrop change picks, per boundary, one of four soft hand-overs:
     the old picture simply dissolving while it drifts a little further (the default), a slow band of light sweeping across it,
     a dip through the page colour like a breath, or a gentle slide. Deeper quality tiers always fall back to the plain dissolve. */
  const SWEEP = { c: null };
  const scratch = (w, h) => { const c = SWEEP.c || (SWEEP.c = document.createElement('canvas')); if (c.width !== w || c.height !== h) { c.width = w; c.height = h; } return c; };
  const pickHandOver = (plan, prev) => {
    if ((J.echoTier || 0) >= 2 || J.echoMotion === false) return 'dissolve';
    if (J.echoHandOver) return J.echoHandOver;
    const k = J.h((prev.seed | 0) + 17, prev.index | 0, 421) % 8;
    return k < 3 ? 'dissolve' : k < 5 ? 'sweep' : k < 6 ? 'veil' : 'slide';
  };
  const handOver = (ctx, A, ph, kind, sc, cw, ch) => {
    const e = ease(ph);
    ctx.save(); ctx.setTransform(1, 0, 0, 1, 0, 0); ctx.globalCompositeOperation = 'source-over'; ctx.filter = 'none';
    if (kind === 'sweep') {
      const S = scratch(cw, ch), g = S.getContext('2d'); g.setTransform(1, 0, 0, 1, 0, 0); g.globalCompositeOperation = 'source-over'; g.globalAlpha = 1; g.clearRect(0, 0, cw, ch); g.drawImage(A, 0, 0);
      const ang = -0.38, dx = Math.cos(ang), dy = Math.sin(ang), span = Math.abs(cw * dx) + Math.abs(ch * dy), cx = cw / 2, cy = ch / 2, pos = -0.2 + 1.4 * ph, soft = 0.22;
      const ax = cx - dx * span / 2, ay = cy - dy * span / 2, bx = cx + dx * span / 2, by = cy + dy * span / 2;
      const gr = g.createLinearGradient(ax, ay, bx, by); gr.addColorStop(0, 'rgba(0,0,0,1)'); gr.addColorStop(Math.max(0, Math.min(1, pos - soft)), 'rgba(0,0,0,1)'); gr.addColorStop(Math.max(0, Math.min(1, pos + soft)), 'rgba(0,0,0,0)'); gr.addColorStop(1, 'rgba(0,0,0,0)');
      g.globalCompositeOperation = 'destination-out'; g.fillStyle = gr; g.fillRect(0, 0, cw, ch); g.globalCompositeOperation = 'source-over';
      ctx.globalAlpha = 1; ctx.drawImage(S, 0, 0);
      const hl = ctx.createLinearGradient(ax, ay, bx, by), a0 = Math.max(0, Math.min(1, pos - soft * 0.9)), a1 = Math.max(0, Math.min(1, pos)), a2 = Math.max(0, Math.min(1, pos + soft * 0.9));
      hl.addColorStop(0, 'rgba(255,255,255,0)'); hl.addColorStop(a0, 'rgba(255,255,255,0)'); hl.addColorStop(a1, `rgba(255,255,255,${0.13 * Math.sin(Math.PI * ph)})`); hl.addColorStop(a2, 'rgba(255,255,255,0)'); hl.addColorStop(1, 'rgba(255,255,255,0)');
      ctx.globalCompositeOperation = 'lighter'; ctx.fillStyle = hl; ctx.fillRect(0, 0, cw, ch);
    } else if (kind === 'veil') {
      ctx.globalAlpha = 1 - ease(Math.min(1, ph * 1.5)); ctx.drawImage(A, 0, 0);
      ctx.globalAlpha = 0.5 * Math.pow(Math.sin(Math.PI * ph), 1.6); ctx.fillStyle = sc.bg; ctx.fillRect(0, 0, cw, ch);
    } else if (kind === 'slide') {
      ctx.globalAlpha = 1 - e; ctx.drawImage(A, -e * cw * 0.04, 0);
    } else {
      ctx.globalAlpha = 1 - e; ctx.drawImage(A, 0, 0);
    }
    ctx.restore();
  };
  R.frame = function (ctx, plan, t, opt = {}) {
    base.call(this, ctx, plan, t, opt);
    if (J.echoDissolve === false || opt.noTrans || opt.transparent || opt.glyphLog || plan.keyBg || !plan.cuts || !plan.cuts.length) return;
    let prev = null, D = 0, ph = 0, bgOnly = true;
    const cur = J.cutAt(plan, t), p = lastEnded(plan.cuts, t);
    if (!p || p.layout === 'interlude' || (!p.text && p.bg === 'none')) return;
    if (cur) {
      if (cur.layout === 'interlude' || cur.trans || cur.morph || cur.start < p.end - 0.06 || cur.start - p.end > 0.5) return;
      const gap = cur.start - p.end, n = plan.style.schemes.length;
      const bgChanged = p.bg !== cur.bg || (p.bgP || {}).seed !== (cur.bgP || {}).seed || p.scheme % n !== cur.scheme % n;
      const hard = gap < 0.3 && (!p.outDur || p.exit === 'cut');
      if (!bgChanged && !hard) return;
      D = bgChanged ? 0.8 : 0.26; bgOnly = bgChanged;
    } else { const hard = !p.outDur || p.exit === 'cut'; D = hard ? 0.4 : 0.6; bgOnly = !hard; }
    ph = (t - p.end) / D; prev = p;
    if (ph <= 0 || ph >= 1 || !prev) return;
    const cw = ctx.canvas.width, ch = ctx.canvas.height, scale = opt.scale || 1, key = `${prev.index}|${bgOnly ? 'b' : 'f'}|${cw}x${ch}|${scale.toFixed(3)}`;
    let m = store.get(plan); if (!m) store.set(plan, m = new Map());
    let A = m.get(key);
    if (!A) {
      A = mk(cw, ch);
      const wasD = J.echoDissolve, ax = A.getContext('2d'); J.echoDissolve = false;
      try {
        const o2 = Object.assign({}, opt, { noTrans: true, noPost: true, noHud: true, noGhost: true }), tp = Math.max(prev.start, prev.end - 1e-3);
        if (bgOnly) { ax.fillStyle = scOf(plan, prev).bg; ax.fillRect(0, 0, cw, ch); base.call(this, ax, plan, tp, Object.assign(o2, { transparent: true, layer: 'back' })); }
        else base.call(this, ax, plan, tp, o2);
      } finally { J.echoDissolve = wasD; }
      m.set(key, A); while (m.size > 3) { const k0 = m.keys().next().value, o = m.get(k0); o.width = o.height = 0; m.delete(k0); }
    }
    handOver(ctx, A, ph, bgOnly && cur ? pickHandOver(plan, prev) : 'dissolve', scOf(plan, prev), cw, ch);
  };
})();


/* ECHO: the engine's full-frame texture passes (film grain, scanlines, vignette) are the largest fixed cost of every frame on
   software or weak GPUs: a scaled pattern fill is resampled per pixel, and the radial gradient is rebuilt each time. They are
   redrawn here from cached layers (one cropped noise canvas, scanline rows, a pre-rendered vignette) with the same strengths
   and order; bloom and the event effects still run in the engine's own post(). J.echoLeanPost = false restores the original. */
(() => {
  const R = J.Renderer && J.Renderer.prototype; if (!R || R.__echoLeanPost) return; R.__echoLeanPost = true;
  const base = R.post, mk = (w, h) => { const c = document.createElement('canvas'); c.width = w; c.height = h; return c; };
  const drop = c => { if (c) c.width = c.height = 0; };
  let noise = null, noiseKey = '';
  const vignettes = new Map();
  const noiseFor = (renderer, cw, ch, k) => {
    const w = Math.ceil(cw + 256 * k), h = Math.ceil(ch + 256 * k), key = `${w}x${h}`;
    if (noise && noiseKey === key) return noise;
    drop(noise); noise = mk(w, h); noiseKey = key;
    const x = noise.getContext('2d'), ts = Math.max(8, Math.ceil(256 * k));
    for (let y = 0; y < h; y += ts) for (let xx = 0; xx < w; xx += ts) x.drawImage(renderer.grain[(Math.random() * 4) | 0], xx, y, ts, ts);
    return noise;
  };
  const vignetteFor = (cw, ch, alpha) => {
    const key = `${cw}x${ch}|${alpha.toFixed(3)}`;
    let v = vignettes.get(key);
    if (!v) {
      v = mk(cw, ch);
      const x = v.getContext('2d'), g = x.createRadialGradient(cw / 2, ch / 2, Math.min(cw, ch) * 0.35, cw / 2, ch / 2, Math.hypot(cw, ch) * 0.62);
      g.addColorStop(0, 'rgba(0,0,0,0)'); g.addColorStop(1, `rgba(0,0,0,${alpha})`);
      x.fillStyle = g; x.fillRect(0, 0, cw, ch);
      vignettes.set(key, v);
      while (vignettes.size > 3) { const k0 = vignettes.keys().next().value; drop(vignettes.get(k0)); vignettes.delete(k0); }
    }
    return v;
  };
  R.post = function (ctx, plan, t, tq, step, sc, scale, opt, allowFilter) {
    if (J.echoLeanPost === false || opt.transparent || plan.keyBg) return base.apply(this, arguments);
    // a view of the plan that reports a key background makes the engine skip exactly its three texture passes
    base.call(this, ctx, Object.create(plan, { keyBg: { value: 'lean' } }), t, tq, step, sc, scale, opt, allowFilter);
    const st = plan.style, tex = plan.fx.texture ?? 0.6, cw = ctx.canvas.width, ch = ctx.canvas.height;
    ctx.save(); ctx.setTransform(1, 0, 0, 1, 0, 0); ctx.filter = 'none';
    try {
      const scan = ((st.texture && st.texture.scan) || 0) * tex;
      if (scan > 0.03) {
        const k = Math.max(1, Math.round(ch / 540));
        ctx.globalAlpha = scan * 0.28; ctx.fillStyle = '#000'; ctx.beginPath();
        for (let y = 3 * k; y < ch; y += 4 * k) ctx.rect(0, y, cw, k);
        ctx.fill(); ctx.globalAlpha = 1;
      }
      const gr = ((st.texture && st.texture.grain) || 0) * tex;
      if (gr > 0.02) {
        const k = Math.max(1, ch / 1080), src = noiseFor(this, cw, ch, k);
        const sx = Math.floor(J.r(step, 1) * 256 * k), sy = Math.floor(J.r(step, 2) * 256 * k);
        ctx.globalCompositeOperation = 'overlay'; ctx.globalAlpha = gr * 0.16; ctx.drawImage(src, sx, sy, cw, ch, 0, 0, cw, ch);
        ctx.globalCompositeOperation = 'source-over'; ctx.globalAlpha = gr * 0.035; ctx.drawImage(src, sx, sy, cw, ch, 0, 0, cw, ch);
        ctx.globalAlpha = 1;
      }
      ctx.globalCompositeOperation = 'source-over'; ctx.globalAlpha = 1;
      ctx.drawImage(vignetteFor(cw, ch, 0.28 * tex), 0, 0);
    } finally { ctx.restore(); }
  };
})();

/* ECHO: the engine's bokeh discs are flat, low-alpha fills in the palette colours, which turn into grey smudges on the dark painted scenes.
   Here they are lit like out-of-focus lamps: soft-edged discs with a brighter rim, added over dark scenes and veiled over light ones. */
(() => {
  const D = J.DECOR && J.DECOR.bokeh; if (!D || D.__echo) return; D.__echo = true;
  const base = D.draw, K = J.echo, clamp = v => Math.min(1, Math.max(0, v));
  D.draw = function (env, bb, P) {
    if (J.echoPlainBokeh === true || !K || env.pass !== 'main') return base.call(this, env, bb, P);
    const { W, H, sc } = env, ctx = env.ctx, M = Math.min(W, H), u = M / 1080;
    const out = 1 - (J.ANIM ? 0 : 0) - (env.pOut > 0 ? env.pOut * env.pOut * env.pOut : 0);
    if (out <= 0.003 || env.lt < 0) return;
    const dk = J.lum(sc.bg) < 0.5, N = Math.min(18, 7 + (P.n | 0) * 2), hex = (P.v | 0) % 2 === 1;
    const tints = [sc.accent, sc.accent2 || sc.fg, sc.ghostA || sc.accent, sc.fg];
    ctx.save();
    ctx.globalCompositeOperation = dk ? 'screen' : 'source-over';
    for (let i = 0; i < N; i++) {
      const r = k => J.r(P.seed, i, k);
      const f = clamp((env.lt - r(1) * 0.5) / 0.7), fe = f * f * (3 - 2 * f);
      if (fe <= 0) continue;
      const R0 = M * (0.026 + r(2) * r(2) * 0.085), R = R0 * (1.35 - 0.35 * fe);
      const span = H + R0 * 3, yy = ((r(5) * H - env.ltb * (5 + r(6) * 9) * u) % span + span) % span - R0 * 1.5;
      const x = r(3) * W + Math.sin(env.ltb * 0.45 + r(4) * 6.28) * 16 * u, tw = 0.82 + 0.18 * Math.sin(env.ltb * (0.6 + r(8)) + r(9) * 6.28);
      const c = dk ? K.mix(tints[i % 4], '#FFFFFF', 0.1) : K.mix(tints[i % 4], '#FFFFFF', 0.42);
      const a = (dk ? 0.17 + r(7) * 0.2 : 0.24 + r(7) * 0.22) * fe * out * tw;
      if (hex) {
        ctx.beginPath(); for (let k = 0; k < 6; k++) { const an = (k * 60 + 15) * Math.PI / 180; ctx.lineTo(x + Math.cos(an) * R, yy + Math.sin(an) * R); } ctx.closePath();
        ctx.fillStyle = K.rgba(c, a * 0.55); ctx.fill(); ctx.strokeStyle = K.rgba(c, a * 1.1); ctx.lineWidth = Math.max(1, 1.6 * u); ctx.stroke();
      } else {
        const g = ctx.createRadialGradient(x, yy, 0, x, yy, R);
        g.addColorStop(0, K.rgba(c, a * 0.42)); g.addColorStop(0.72, K.rgba(c, a * 0.62)); g.addColorStop(0.9, K.rgba(c, a * 1.3)); g.addColorStop(1, K.rgba(c, 0));
        ctx.fillStyle = g; ctx.fillRect(x - R, yy - R, R * 2, R * 2);
      }
    }
    ctx.restore();
  };
})();
