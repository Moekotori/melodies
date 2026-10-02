/* ECHO first-party backdrops, more ドット scenes: a torch-lit dungeon (ドットダンジョン) and an underwater world (ドット海).
   Same rules as the other pixel scenes: a coarse cell grid scaled up without smoothing, sprites drawn from small maps. */
(() => {
  'use strict';
  const K = J.echo, TAU = K.TAU, clamp = K.clamp, mix = K.mix, rgba = K.rgba;
  const { isDark, wrap, fade, scroll } = K;
  const { grid, cells, blit, pr, disc, bands, sprite, hud, colourKey, HEART, STAR, CATA, CATB } = K.pixel;

  const CHEST = ['.11111111.', '1222222221', '1233333321', '1111441111', '1233443321', '1233333321', '1111111111'];
  const SLIME = [['..11111..', '.1222221.', '122232221', '122222221', '111111111'], ['.........', '..11111..', '.1222221.', '122232221', '111111111']];
  const BAT = [['1.11...11.1', '.111111111.', '..1.1.1.1..'], ['...1...1...', '11111111111', '..1.1.1.1..']];
  const CRYS = ['..1..', '.121.', '.121.', '12321', '12321'];
  const FISH = ['..111...', '.1111.1.', '13111111', '.1111.1.', '..111...'];
  const JELLY = [['..1111..', '.122221.', '12222221', '12222221'], ['..1111..', '.122221.', '12222221', '12222221']];
  const dither = (ctx, g, cx, cy, r, col, a, sx) => {            /* a light pool drawn as checker-dithered cells, denser near the centre */
    ctx.globalAlpha *= a; ctx.fillStyle = col; ctx.beginPath();
    for (let dy = -r; dy <= r; dy++) for (let dx = -r; dx <= r; dx++) { const d = Math.hypot(dx, dy * 1.15); if (d > r) continue; if (((dx + dy) & 1) === 0 || d < r * 0.4) ctx.rect(Math.round(cx + dx) * g.c, Math.round(cy + dy) * g.c, g.c + 0.5, g.c + 0.5); }
    ctx.fill(); ctx.globalAlpha /= a;
  };

  /* stepped, dithered darkening at the frame edges: the pixel take on a vignette */
  const pixVignette = (ctx, g, dark, e) => {
    ctx.save(); ctx.fillStyle = dark ? '#000000' : '#20304A'; ctx.beginPath();
    for (let d = 0; d < 4; d++) for (let i = 0; i < g.cols; i++) for (const y of [d, g.rows - 1 - d]) if (d === 0 || ((i + y + d) & 1) === (d === 3 ? 0 : 1) || d === 1) ctx.rect(i * g.c, y * g.c, g.c + 0.5, g.c + 0.5);
    for (let d = 0; d < 4; d++) for (let j = 4; j < g.rows - 4; j++) for (const x of [d, g.cols - 1 - d]) if (d === 0 || ((j + x + d) & 1) === (d === 3 ? 0 : 1) || d === 1) ctx.rect(x * g.c, j * g.c, g.c + 0.5, g.c + 0.5);
    ctx.globalAlpha = e * (dark ? 0.28 : 0.14); ctx.fill(); ctx.restore();
  };

  /* ================= ドットダンジョン ================= */
  K.registerBg('echoPixelDungeon', 'ドットダンジョン', ['pop', 'calm', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, e = fade(env, 0.6), dark = isDark(sc.bg), g = grid(env);
    const key = `dungeon|${seed}|${colourKey(sc)}`, ground = Math.round(g.rows * 0.84);
    const stone = mix(sc.bg, dark ? '#2C2646' : '#BDAA90', 0.55), mortar = mix(stone, '#000000', dark ? 0.4 : 0.22), hi = mix(stone, '#FFFFFF', dark ? 0.14 : 0.3);
    ctx.globalAlpha = e; ctx.imageSmoothingEnabled = false;
    const wallOff = () => Math.round(t * 3) * 0.5;
    const torchX = [], bannerX = [];
    for (let i = 0; i < 3; i++) { bannerX.push(g.cols * (i + 0.5) / 3); torchX.push(g.cols * (i + 0.5) / 3 - g.cols * 0.13, g.cols * (i + 0.5) / 3 + g.cols * 0.13); }
    const wall = cells(env, `${key}|wall`, g, (x, w, h) => {
      x.fillStyle = mortar; x.fillRect(0, 0, w, ground);
      for (let r = 0, y = 0; y < ground; r++, y += 4) for (let xx = -4 * (r % 2); xx < w; xx += 8) {
        const v = J.r(seed, r, Math.floor(xx / 8) + 40, 1); x.fillStyle = mix(stone, v > 0.5 ? '#FFFFFF' : '#000000', 0.05 + 0.08 * Math.abs(v - 0.5) * 2); x.fillRect(xx + 1, y + 1, 7, 3);
        x.fillStyle = hi; x.fillRect(xx + 1, y + 1, 7, 1); if (v > 0.86) { x.fillStyle = rgba('#7FD07A', dark ? 0.5 : 0.7); x.fillRect(xx + 2, y + 3, 3, 1); x.fillRect(xx + 3, y + 4, 1, 1); }
      }
      x.fillStyle = rgba('#000000', 0.22); for (let y = 0; y < ground; y++) if (y % 2 === 0) x.fillRect(0, y, w, 1);   /* not too flat: faint rows */
      for (let i = 0; i < 3; i++) {                                            /* pillars with capitals, banners between */
        const px = Math.round(w * i / 3); x.fillStyle = mix(stone, '#000000', 0.18); x.fillRect(px - 3, 0, 7, ground); x.fillStyle = hi; x.fillRect(px - 3, 0, 1, ground); x.fillStyle = mix(stone, '#000000', 0.35); x.fillRect(px + 3, 0, 1, ground);
        x.fillStyle = mix(stone, '#FFFFFF', 0.1); x.fillRect(px - 4, 0, 9, 3); x.fillRect(px - 4, ground - 4, 9, 4);
        const bx = Math.round(w * (i + 0.5) / 3); x.fillStyle = i % 2 ? sc.accent2 : sc.accent; x.fillRect(bx - 5, 6, 11, 22); x.fillStyle = mix(i % 2 ? sc.accent2 : sc.accent, '#000000', 0.3); x.fillRect(bx - 5, 6, 1, 22); x.fillRect(bx - 5, 27, 11, 1);
        x.fillStyle = '#4A3A2A'; x.fillRect(bx - 6, 5, 13, 1); for (let k = 0; k < 3; k++) { x.fillStyle = i % 2 ? sc.accent2 : sc.accent; x.fillRect(bx - 4 + k * 4, 28, 2, 2); }
        x.fillStyle = 'rgba(0,0,0,0)';
      }
      for (const tx of torchX) { const cx = Math.round(tx); x.fillStyle = '#3A2A20'; x.fillRect(cx - 1, ground * 0.42 + 2, 3, 6); x.fillRect(cx - 2, ground * 0.42 + 1, 5, 1); }
    });
    scroll(ctx, wall, env, wallOff() * g.c, 1, 0, 1);
    /* banner emblems (heart / star) drawn crisply on top of the scrolling strip */
    bannerX.forEach((bx, i) => { const x0 = wrap(bx - wallOff(), g.cols); sprite(ctx, g, i % 2 ? STAR : HEART, i % 2 ? { 1: '#FFF3B0' } : { 1: '#FFFFFF', 2: '#FFD0DC' }, x0 - 3, 11); });
    /* torches: dithered light pools, flames that flicker in three frames */
    const ty = Math.round(ground * 0.42);
    torchX.forEach((tx, i) => {
      const cx = wrap(tx - wallOff(), g.cols), fl = Math.floor(t * 9 + i * 2.3) % 3;
      dither(ctx, g, cx, ty - 3, 16 + (fl === 1 ? 1 : 0), dark ? '#FFB050' : '#FFF0C0', dark ? 0.22 : 0.3);
      dither(ctx, g, cx, ty - 3, 8, dark ? '#FFE9A6' : '#FFFFFF', dark ? 0.3 : 0.38);
      ctx.fillStyle = '#FF6A30'; pr(ctx, g, cx - 1, ty - 3 - (fl === 2 ? 1 : 0), 3, 3 + (fl === 2 ? 1 : 0)); ctx.fillStyle = '#FFB050'; pr(ctx, g, cx - (fl === 0 ? 0 : 0), ty - 2 - (fl === 1 ? 1 : 0), 1 + (fl === 1 ? 0 : 1), 2); ctx.fillStyle = '#FFF0B0'; pr(ctx, g, cx, ty - 1, 1, 1);
      if (fl === 2) { ctx.fillStyle = '#FFB050'; pr(ctx, g, cx + 2, ty - 6 - Math.floor(t * 3) % 3, 1, 1); }
    });
    /* floor: flagstones with a shine, then crystals, the chest and a slime along the way */
    const floor = cells(env, `${key}|floor`, g, (x, w, h) => {
      x.fillStyle = mix(stone, '#000000', dark ? 0.35 : 0.14); x.fillRect(0, ground, w, h - ground); x.fillStyle = hi; x.fillRect(0, ground, w, 1); x.fillStyle = mix(stone, '#000000', 0.45); x.fillRect(0, ground + 1, w, 1);
      for (let r = 0, y = ground + 2; y < h; r++, y += 5) { for (let xx = -8 * (r % 2); xx < w; xx += 16) { x.fillStyle = mortar; x.fillRect(xx, y, 1, 5); } x.fillRect(0, y, w, 1); for (let xx = 0; xx < w; xx += 2) if (J.r(seed, xx, r, 61) > 0.93) { x.fillStyle = hi; x.fillRect(xx, y + 2, 1, 1); } }
      for (let i = 0; i < 6; i++) { const cx = Math.floor(J.r(seed, i, 71) * w), pal = [sc.accent, sc.accent2, '#7FE3FF'][i % 3]; sprite(x, { c: 1 }, CRYS, { 1: mix(pal, '#000000', 0.35), 2: pal, 3: mix(pal, '#FFFFFF', 0.6) }, cx, ground - 5 + (i % 2)); }
    });
    scroll(ctx, floor, env, Math.round(t * 6) * g.c * 0.5, 1, 0, 1);
    for (let i = 0; i < 6; i++) { const cx = wrap(J.r(seed, i, 71) * g.cols - Math.round(t * 6) * 0.5, g.cols), b = Math.floor(t * 3 + i * 2) % 4 === 0; if (b) { ctx.fillStyle = '#FFFFFF'; pr(ctx, g, cx + 2, ground - 8, 1, 3); pr(ctx, g, cx + 1, ground - 7, 3, 1); } }
    const chX = wrap(g.cols * 0.7 - Math.round(t * 6) * 0.5, g.cols + 20) - 8, open = Math.floor(t * 1.2) % 4 === 0;
    sprite(ctx, g, open ? ['.11111111.', '1222222221', '1111111111', '1333333331', '1333443331', '1333333331', '1111111111'] : CHEST, { 1: '#3A2418', 2: '#C58A4E', 3: '#9A6238', 4: '#FFD86B' }, chX, ground - 6);
    if (open) { ctx.globalAlpha = e * 0.9; sprite(ctx, g, ['.1.', '111', '.1.'], { 1: '#FFF3B0' }, chX + 4, ground - 9 - Math.floor(t * 4) % 3); ctx.globalAlpha = e; }
    const slX = wrap(g.cols * 0.3 - Math.round(t * 6) * 0.5 + Math.sin(t * 0.5) * 6, g.cols + 20) - 8, jump = Math.abs(Math.sin(t * 3));
    sprite(ctx, g, SLIME[jump < 0.25 ? 1 : 0], { 1: '#2A4A3A', 2: mix('#7AE08A', sc.accent2, 0.15), 3: '#FFFFFF' }, slX, ground - 5 - Math.round(jump * 4) + (jump < 0.25 ? 1 : 0));
    ctx.fillStyle = '#20302A'; pr(ctx, g, slX + 2, ground - 3 - Math.round(jump * 4), 1, 1); pr(ctx, g, slX + 5, ground - 3 - Math.round(jump * 4), 1, 1);
    for (let i = 0; i < 3; i++) sprite(ctx, g, BAT[Math.floor(t * 6 + i) % 2], { 1: dark ? '#C8B8F0' : '#4A3A6A' }, wrap(g.cols * (0.15 + i * 0.33) + t * (5 + i * 2), g.cols + 24) - 12, Math.round(g.rows * (0.12 + 0.05 * i) + Math.sin(t * 1.7 + i * 2) * 3));
    const catX = wrap(t * 6, g.cols + 24) - 12; sprite(ctx, g, Math.floor(t * 8) % 2 ? CATA : CATB, { 1: dark ? '#FFF7EC' : '#FFFFFF', 2: '#3A2E44', 3: '#FF9DB0' }, catX, ground + 3);
    ctx.fillStyle = '#C8D4E8'; pr(ctx, g, catX + 8, ground + 4, 1, 3); ctx.fillStyle = '#7A5A3A'; pr(ctx, g, catX + 8, ground + 7, 1, 1);          /* a tiny sword */
    pixVignette(ctx, g, dark, e);
    hud(ctx, g, env, sc, t, dark);
  });

  /* ================= ドット海 ================= */
  K.registerBg('echoPixelSea', 'ドット海', ['pop', 'calm', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, e = fade(env, 0.6), dark = isDark(sc.bg), g = grid(env);
    const key = `sea|${seed}|${colourKey(sc)}`, ground = Math.round(g.rows * 0.86), sand = mix('#F2DDA2', sc.bg, dark ? 0.5 : 0.1);
    const water = dark ? ['#1C6E92', '#155C80', '#104A6C', '#0C3B58', '#082C46', '#061F36'].map(c => mix(c, sc.bg, 0.3)) : ['#D6F7FF', '#B4EBFA', '#93DDF3', '#78CDEA', '#63BDE0', '#57ADD3'].map(c => mix(c, sc.bg, 0.25));
    ctx.globalAlpha = e; ctx.imageSmoothingEnabled = false;
    const back = cells(env, `${key}|water`, g, (x, w, h) => {
      bands(x, w, h, ground, water);
      x.fillStyle = mix(water[0], '#FFFFFF', 0.5); for (let xx = 0; xx < w; xx++) { const y = 1 + Math.round(Math.sin(xx * 0.5) * 0.8); x.fillRect(xx, y, 1, 1); }     /* the surface glimmer overhead */
      for (let i = 0; i < 5; i++) { const cx = Math.round(w * (i + 0.5) / 5 + (J.r(seed, i, 5) - 0.5) * 6), hh = 12 + Math.round(J.r(seed, i, 6) * 14), c = mix(water[3], water[5], 0.5); x.fillStyle = c; for (let y = ground - hh; y < ground; y++) { const half = Math.round((y - (ground - hh)) * 0.25 + 1); x.fillRect(cx - half, y, half * 2 + 1, 1); } }       /* distant rocks */
    });
    blit(ctx, back, env);
    ctx.globalAlpha = e * (dark ? 0.12 : 0.2); ctx.fillStyle = '#FFFFFF'; ctx.beginPath();          /* light rays sliding through the water */
    for (let r = 0; r < 4; r++) { const x0 = g.cols * (0.08 + 0.26 * r) + Math.sin(t * 0.25 + r * 2) * 5, wd = 4 + (r % 2) * 3; for (let y = 0; y < ground * 0.86; y++) { const cx = Math.round(x0 + y * 0.32); for (let xx = 0; xx < wd; xx++) if (((cx + xx + y) & 1) === 0) ctx.rect((cx + xx) * g.c, y * g.c, g.c + 0.5, g.c + 0.5); } }
    ctx.fill(); ctx.globalAlpha = e;
    /* fish schools swim through in two directions */
    [[sc.accent, 0.33, 7, false, 0], [sc.accent2, 0.5, 9, true, 1], ['#FFE58A', 0.2, 5, false, 2]].forEach(([c, yy, sp, right, id]) => {
      const lead = wrap((right ? 1 : -1) * t * sp + g.cols * J.r(seed, id, 3), g.cols + 40) - 20;
      for (let k = 0; k < 6; k++) { const fx = lead + (right ? -1 : 1) * (k % 3) * 9 - (right ? 0 : 0), fy = Math.round(g.rows * yy + Math.sin(t * 1.3 + k * 1.7 + id) * 3 + (k % 2) * 4 + Math.floor(k / 3) * 5); sprite(ctx, g, FISH, { 1: k % 2 ? c : mix(c, '#FFFFFF', 0.25), 3: '#FFFFFF' }, wrap(fx + 20, g.cols + 40) - 20, fy, right); }
    });
    /* jellyfish drifting up and down with waving tentacles */
    for (let i = 0; i < 3; i++) {
      const jx = Math.round(g.cols * (0.18 + 0.32 * i) + Math.sin(t * 0.4 + i * 2) * 4), jy = Math.round(g.rows * (0.25 + 0.15 * i) + Math.sin(t * 0.9 + i * 1.7) * 5), col = [sc.accent, sc.accent2, '#FFB6E8'][i];
      ctx.globalAlpha = e * 0.85; sprite(ctx, g, JELLY[0], { 1: mix(col, '#FFFFFF', 0.45), 2: col }, jx, jy);
      ctx.fillStyle = mix(col, '#FFFFFF', 0.3); for (let k = 0; k < 4; k++) for (let y = 0; y < 5; y++) pr(ctx, g, jx + 1 + k * 2 + Math.round(Math.sin(t * 3 + y * 0.8 + k) * 0.9), jy + 4 + y, 1, 1);
      ctx.globalAlpha = e;
    }
    /* the sea floor: sand, a coral garden, then swaying weed on top */
    const floor = cells(env, `${key}|floor`, g, (x, w, h) => {
      x.fillStyle = sand; x.fillRect(0, ground, w, h - ground); x.fillStyle = mix(sand, '#FFFFFF', 0.3); x.fillRect(0, ground, w, 1);
      for (let yy = ground + 2; yy < h; yy += 3) for (let xx = (yy % 2) * 2; xx < w; xx += 5) { x.fillStyle = mix(sand, '#8A6A3A', 0.25); x.fillRect(xx, yy, 2, 1); }
      for (let i = 0; i < 9; i++) {
        const cx = Math.floor(w * (i + 0.5) / 9 + (J.r(seed, i, 81) - 0.5) * 4), c = [sc.accent, sc.accent2, '#FF9A7A'][i % 3], tall = 5 + Math.round(J.r(seed, i, 82) * 8), dk = mix(c, '#000000', 0.3);
        if (i % 3 === 0) { for (let k = -2; k <= 2; k++) { const hh = tall - Math.abs(k) * 2; x.fillStyle = k % 2 ? c : mix(c, '#FFFFFF', 0.2); x.fillRect(cx + k * 2, ground - hh, 2, hh + 1); x.fillStyle = dk; x.fillRect(cx + k * 2, ground - hh, 1, 1); } }      /* branch coral */
        else if (i % 3 === 1) { disc(x, cx, ground - 3, 4, c); x.fillStyle = dk; for (let k = 0; k < 4; k++) x.fillRect(cx - 2 + k, ground - 5 + (k % 2) * 2, 1, 1); x.fillStyle = mix(c, '#FFFFFF', 0.45); x.fillRect(cx - 2, ground - 5, 2, 1); }                  /* brain coral */
        else { x.fillStyle = c; x.fillRect(cx, ground - tall, 3, tall + 1); x.fillRect(cx - 3, ground - tall + 3, 3, 2); x.fillRect(cx - 3, ground - tall + 3, 1, 4); x.fillRect(cx + 3, ground - tall + 5, 3, 2); x.fillStyle = dk; x.fillRect(cx, ground - tall, 1, tall + 1); }    /* fork coral */
      }
      for (let i = 0; i < 4; i++) { const sx = Math.floor(J.r(seed, i, 91) * w), sy = ground + 3 + Math.floor(J.r(seed, i, 92) * (h - ground - 5)); x.fillStyle = '#FFF3E0'; x.fillRect(sx, sy, 3, 2); x.fillStyle = '#FF9DB0'; x.fillRect(sx, sy, 1, 2); }                 /* shells */
    });
    scroll(ctx, floor, env, Math.round(t * 4) * g.c * 0.5, 1, 0, 1);
    for (let i = 0; i < 12; i++) {
      const bx = wrap(J.r(seed, i, 101) * g.cols - Math.round(t * 4) * 0.5, g.cols), h0 = 6 + Math.floor(J.r(seed, i, 102) * 10), c = i % 2 ? '#3FBF70' : '#2E9A5A';
      for (let y = 0; y < h0; y++) { const sway = Math.round(Math.sin(t * 1.4 + y * 0.4 + i) * (0.5 + y * 0.1)); ctx.fillStyle = y % 3 === 0 ? mix(c, '#FFFFFF', 0.25) : c; pr(ctx, g, bx + sway, ground - y, 1, 1); if (y % 4 === 2) pr(ctx, g, bx + sway + (i % 2 ? 1 : -1), ground - y, 1, 1); }
    }
    /* a slow turtle, and the cat diver in a bubble helmet */
    const tx = wrap(g.cols * 0.9 - t * 3 + J.r(seed, 7) * g.cols, g.cols + 40) - 20, ty = Math.round(g.rows * 0.55 + Math.sin(t * 0.7) * 3), shell = mix('#6FC77A', sc.accent2, 0.15), fl = Math.floor(t * 2) % 2;
    for (let dy = -3; dy <= 3; dy++) { const span = Math.floor(6 * Math.sqrt(Math.max(0, 1 - (dy / 3.8) ** 2))); ctx.fillStyle = dy < -1 ? mix(shell, '#FFFFFF', 0.25) : shell; pr(ctx, g, tx - span, ty + dy, span * 2 + 1, 1); }
    ctx.fillStyle = mix(shell, '#000000', 0.3); for (let k = -1; k <= 1; k++) pr(ctx, g, tx + k * 3, ty - 1 + (k === 0 ? 0 : 1), 1, 2); ctx.fillStyle = mix(shell, '#FFE58A', 0.5); pr(ctx, g, tx - 8, ty - 1, 3, 3); pr(ctx, g, tx + 4 + fl, ty + 3, 3, 1); pr(ctx, g, tx - 5, ty + 3 - fl, 3, 1); pr(ctx, g, tx + 6, ty - 3, 2, 1); ctx.fillStyle = '#20302A'; pr(ctx, g, tx - 8, ty - 1, 1, 1);
    const dx = wrap(t * 4 + g.cols * 0.2, g.cols + 30) - 15, dy2 = Math.round(g.rows * 0.42 + Math.sin(t * 1.2) * 4);
    sprite(ctx, g, Math.floor(t * 6) % 2 ? CATA : CATB, { 1: dark ? '#FFF7EC' : '#FFFFFF', 2: '#3A2E44', 3: '#FF9DB0' }, dx, dy2);
    ctx.strokeStyle = rgba('#FFFFFF', 0.7); ctx.lineWidth = g.c * 0.9; ctx.beginPath(); ctx.arc((dx + 4) * g.c, (dy2 + 3) * g.c, 6.2 * g.c, 0, TAU); ctx.stroke(); ctx.fillStyle = rgba('#BFEFFF', 0.25); ctx.fill(); ctx.fillStyle = '#FFFFFF'; pr(ctx, g, dx - 1, dy2 - 1, 2, 1); pr(ctx, g, dx - 2, dy2, 1, 1);
    /* bubbles */
    for (let i = 0; i < 16; i++) { const spd = 4 + 5 * J.r(seed, i, 111), by = g.rows - wrap(J.r(seed, i, 112) * g.rows * 1.2 + t * spd, g.rows * 1.2) + g.rows * 0.1 - 0, bx = J.r(seed, i, 113) * g.cols + Math.round(Math.sin(t * 1.5 + i) * 1.4), big = i % 3 === 0; ctx.fillStyle = rgba('#FFFFFF', 0.6); pr(ctx, g, bx, by, big ? 3 : 2, big ? 3 : 2); ctx.fillStyle = rgba(water[1], 0.75); pr(ctx, g, bx + 1, by + 1, big ? 1 : 0, big ? 1 : 0); }
    pixVignette(ctx, g, dark, e);
    hud(ctx, g, env, sc, t, dark);
  });
})();
