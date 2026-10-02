/* ECHO first-party backdrops for ドット: a pixel meadow, a pixel sky of stars and planets, and a pixel town at dusk.
   Every scene is painted on a coarse grid (about 64 cells across the short side) and scaled up without smoothing, so the
   cells stay square at any window size. Sprites are original, drawn from small character maps. */
(() => {
  'use strict';
  const K = J.echo, E = K.E, TAU = K.TAU, clamp = K.clamp, mix = K.mix, rgba = K.rgba;
  const { U: Umin, isDark, wrap, fade, layer, scroll } = K;
  const colourKey = sc => [sc.bg, sc.fg, sc.accent, sc.accent2].join('');

  const grid = env => { const cols = Math.max(32, Math.round(env.W / (Umin(env) / 64))), c = env.W / cols; return { cols, rows: Math.ceil(env.H / c), c, res: cols / (env.W * env.scale) }; };
  const cells = (env, key, g, draw) => layer(env, key, g.res, x => { x.setTransform(1, 0, 0, 1, 0, 0); x.imageSmoothingEnabled = false; draw(x, x.canvas.width, x.canvas.height); });
  const blit = (ctx, canvas, env) => { ctx.imageSmoothingEnabled = false; ctx.drawImage(canvas, 0, 0, env.W, env.H); };
  const pr = (ctx, g, ix, iy, w, h) => ctx.fillRect(Math.round(ix) * g.c, Math.round(iy) * g.c, w * g.c + 0.5, h * g.c + 0.5);
  const disc = (x, cx, cy, r, col) => { x.fillStyle = col; for (let dy = -r; dy <= r; dy++) { const span = Math.floor(Math.sqrt(r * r - dy * dy + r * 0.6)); x.fillRect(cx - span, cy + dy, span * 2 + 1, 1); } };
  const bands = (x, w, h, y1, colours) => {                       /* banded sky with checker dithering at every seam */
    const n = colours.length, hh = y1 / n;
    for (let y = 0; y < y1; y++) {
      const b = Math.min(n - 1, Math.floor(y / hh)), edge = y - b * hh;
      x.fillStyle = colours[b]; x.fillRect(0, y, w, 1);
      if (b < n - 1 && edge > hh - 3) { x.fillStyle = colours[b + 1]; for (let xx = (y % 2); xx < w; xx += 2) x.fillRect(xx, y, 1, 1); }
    }
  };
  const cloud = (x, cx, cy, s, top, bottom) => {
    const blobs = [[0, 0, 3], [4, -2, 4], [9, -1, 3], [5, 1, 3]];
    for (const [dx, dy, r] of blobs) disc(x, cx + Math.round(dx * s), cy + Math.round(dy * s), Math.max(1, Math.round(r * s)), top);
    x.fillStyle = bottom; x.fillRect(cx - Math.round(3 * s), cy + Math.round(2 * s), Math.round(15 * s), Math.max(1, Math.round(s)));
  };
  const GLYPH = { 0: '111101101101111', 1: '010110010010111', 2: '111001111100111', 3: '111001111001111', 4: '101101111001001', 5: '111100111001111', 6: '111100111101111', 7: '111001001001001', 8: '111101111101111', 9: '111101111001111',
    S: '111100111001111', C: '111100100100111', O: '111101101101111', R: '110101110101101', E: '111100111100111', L: '100100100100111', V: '101101101101010', H: '101101111101101', I: '111010010010111', P: '111101111100100' };
  const text = (ctx, g, str, ix, iy, col) => { ctx.fillStyle = col; let x = ix; for (const ch of str) { const rows = GLYPH[ch]; if (rows) for (let i = 0; i < 15; i++) if (rows[i] === '1') pr(ctx, g, x + (i % 3), iy + Math.floor(i / 3), 1, 1); x += 4; } };
  const sprite = (ctx, g, rows, pal, ix, iy, flip) => { for (let r = 0; r < rows.length; r++) for (let c = 0; c < rows[r].length; c++) { const ch = rows[r][c], col = pal[ch]; if (!col) continue; ctx.fillStyle = col; pr(ctx, g, ix + (flip ? rows[r].length - 1 - c : c), iy + r, 1, 1); } };
  const HEART = ['.11.11.', '1221221', '1222221', '.12221.', '..121..', '...1...'];
  const STAR = ['...1...', '...1...', '1111111', '.11111.', '..111..', '.11.11.', '.1...1.'];
  const COIN = [['..111..', '.12221.', '1223221', '1223221', '1223221', '.12221.', '..111..'], ['..11...', '.1221..', '.1231..', '.1231..', '.1231..', '.1221..', '..11...'], ['...1...', '...1...', '...1...', '...1...', '...1...', '...1...', '...1...'], ['..11...', '.1221..', '.1231..', '.1231..', '.1231..', '.1221..', '..11...']];
  const CATA = ['.1...1..', '.11111..', '1211121.', '1111111.', '.13331..', '.11111.1', '.1.1.11.'];
  const CATB = ['.1...1..', '.11111..', '1211121.', '1111111.', '.13331..', '.11111.1', '..1.1.1.'];
  const BIRD = [['1...1', '.111.', '..1..'], ['..1..', '.111.', '1...1']];
  const shades = (sc, dark) => {
    const sky = dark ? [mix(sc.bg, '#000000', 0.5), mix(sc.bg, '#000000', 0.32), mix(sc.bg, '#000000', 0.12), sc.bg, mix(sc.bg, sc.accent, 0.25), mix(sc.bg, sc.accent2, 0.3)]
      : [mix(sc.bg, '#7EC8FF', 0.55), mix(sc.bg, '#7EC8FF', 0.32), mix(sc.bg, '#7EC8FF', 0.12), sc.bg, mix(sc.bg, '#FFFFFF', 0.45), mix(sc.bg, sc.accent2, 0.22)];
    const grass = mix('#6FD070', sc.bg, dark ? 0.45 : 0.06);
    return { sky, grass, grassDark: mix(grass, '#000000', 0.25), grassLight: mix(grass, '#FFFFFF', 0.25), dirt: mix('#C79A68', sc.bg, dark ? 0.5 : 0.2), dirtDark: mix('#C79A68', '#000000', dark ? 0.55 : 0.3), hill: mix(grass, sky[3], 0.22), hillFar: mix(grass, sky[3], 0.5) };
  };
  const hud = (ctx, g, env, sc, t, dark) => {
    const ink = dark ? sc.fg : sc.ink || sc.fg, sh = rgba(dark ? '#000000' : '#FFFFFF', dark ? 0.35 : 0.5);
    for (let i = 0; i < 3; i++) sprite(ctx, g, HEART, { 1: sc.accent, 2: mix(sc.accent, '#FFFFFF', 0.55) }, 2 + i * 8, 2);
    ctx.fillStyle = sh; const score = String(Math.floor(t * 42 + 1280) % 1000000).padStart(6, '0');
    text(ctx, g, 'SCORE', g.cols - 46, 2, ink); text(ctx, g, score, g.cols - 25, 2, sc.accent2);
    ctx.fillStyle = rgba(ink, 0.35); pr(ctx, g, g.cols - 46, 8, 44, 1);
  };

  /* ================= ドット草原 ================= */
  K.registerBg('echoPixel', 'ドット草原', ['pop', 'calm', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, e = fade(env, 0.6), dark = isDark(sc.bg), g = grid(env), col = shades(sc, dark);
    const key = `pixel|${seed}|${colourKey(sc)}`, ground = Math.round(g.rows * 0.84);
    ctx.globalAlpha = e; ctx.imageSmoothingEnabled = false;
    const sky = cells(env, `${key}|sky`, g, (x, w, h) => {
      bands(x, w, h, ground, col.sky);
      if (dark) { for (let i = 0; i < 70; i++) { x.fillStyle = rgba(sc.fg, 0.4 + 0.5 * J.r(seed, i, 3)); x.fillRect(Math.floor(J.r(seed, i, 1) * w), Math.floor(J.r(seed, i, 2) * ground * 0.7), 1, 1); } disc(x, Math.round(w * 0.78), Math.round(h * 0.2), 6, '#FFF6D8'); disc(x, Math.round(w * 0.78) + 3, Math.round(h * 0.2) - 2, 5, col.sky[0]); }
      else { const sx = Math.round(w * 0.78), sy = Math.round(h * 0.2); disc(x, sx, sy, 9, mix(sc.accent2, '#FFFFFF', 0.55)); disc(x, sx, sy, 7, sc.accent2); x.fillStyle = mix(sc.accent2, '#FFFFFF', 0.6); x.fillRect(sx - 3, sy - 4, 3, 1); x.fillRect(sx - 4, sy - 3, 1, 2); }
    });
    blit(ctx, sky, env);
    const far = cells(env, `${key}|far`, g, (x, w, h) => { for (let i = 0; i < 6; i++) { disc(x, Math.round(w * (i + 0.5) / 6), ground - 2 + Math.round(2 * J.r(seed, i, 5)), Math.round(9 + 8 * J.r(seed, i, 4)), col.hillFar); } x.fillStyle = col.hillFar; x.fillRect(0, ground - 3, w, 4); });
    const near = cells(env, `${key}|near`, g, (x, w, h) => { for (let i = 0; i < 5; i++) { const cx = Math.round(w * (i + 0.5) / 5), r = Math.round(7 + 6 * J.r(seed, i, 7)); disc(x, cx, ground + 1, r, col.hill); x.fillStyle = mix(col.hill, '#FFFFFF', 0.18); for (let k = 0; k < 3; k++) x.fillRect(cx - r + 3 + k * 4, ground - r + 3 + k, 2, 1); }
      for (let i = 0; i < 8; i++) { const cx = Math.floor(w * J.r(seed, i, 8)), base = ground; x.fillStyle = col.dirtDark; x.fillRect(cx, base - 5, 1, 5); disc(x, cx, base - 8, 3, col.grassDark); disc(x, cx - 1, base - 9, 2, col.grass); x.fillStyle = mix(col.grass, '#FFFFFF', 0.3); x.fillRect(cx - 2, base - 10, 1, 1); } });
    scroll(ctx, far, env, Math.round(t * 1.2) * g.c * 0.5, 1, 0, 1); scroll(ctx, near, env, Math.round(t * 3) * g.c * 0.5, 1, 0, 1);
    const cloudLayer = cells(env, `${key}|clouds`, g, (x, w, h) => { for (let i = 0; i < 5; i++) cloud(x, Math.round(w * (i + 0.3) / 5), Math.round(h * (0.1 + 0.28 * J.r(seed, i, 12))), 0.65 + J.r(seed, i, 13) * 0.45, dark ? mix(col.sky[2], '#FFFFFF', 0.25) : '#FFFFFF', dark ? mix(col.sky[1], '#FFFFFF', 0.1) : mix('#FFFFFF', '#9CD3FF', 0.5)); });
    scroll(ctx, cloudLayer, env, Math.round(t * 2) * g.c * 0.5, 1, 0, dark ? 0.7 : 1);
    const floor = cells(env, `${key}|floor`, g, (x, w, h) => {
      x.fillStyle = col.dirt; x.fillRect(0, ground, w, h - ground);
      for (let yy = ground + 2; yy < h; yy += 3) for (let xx = (yy % 2) * 2; xx < w; xx += 5) { x.fillStyle = col.dirtDark; x.fillRect(xx, yy, 2, 1); }
      x.fillStyle = col.grass; x.fillRect(0, ground, w, 3); x.fillStyle = col.grassDark; x.fillRect(0, ground + 3, w, 1);
      for (let xx = 0; xx < w; xx += 2) { x.fillStyle = col.grassLight; if (J.r(seed, xx, 21) > 0.5) x.fillRect(xx, ground - 1, 1, 1); }
      for (let i = 0; i < 14; i++) { const fx = Math.floor(J.r(seed, i, 22) * w), c = [sc.accent, sc.accent2, '#FFFFFF'][i % 3]; x.fillStyle = col.grassDark; x.fillRect(fx, ground - 2, 1, 2); x.fillStyle = c; x.fillRect(fx - 1, ground - 3, 3, 1); x.fillRect(fx, ground - 4, 1, 3); x.fillStyle = sc.accent2; x.fillRect(fx, ground - 3, 1, 1); }
    });
    scroll(ctx, floor, env, Math.round(t * 6) * g.c * 0.5, 1, 0, 1);
    /* coins in an arc and a little block with a sparkle above the cat's path */
    const coinsX = ((t * 6) % g.cols);
    for (let i = 0; i < 7; i++) {
      const cx = wrap(g.cols * 0.3 + i * 5 - t * 6, g.cols + 20) - 10, cy = ground - 14 - Math.round(Math.sin(i / 6 * Math.PI) * 8) + Math.round(Math.sin(t * 3 + i) * 1.2), f = Math.floor(t * 8 + i) % 4;
      sprite(ctx, g, COIN[f], { 1: mix(sc.accent2, '#000000', 0.35), 2: sc.accent2, 3: mix(sc.accent2, '#FFFFFF', 0.6) }, cx, cy);
    }
    /* the cat runs along the grass */
    const catX = wrap(t * 9, g.cols + 24) - 12, bounce = Math.abs(Math.sin(t * 9)) > 0.7 ? 1 : 0;
    sprite(ctx, g, Math.floor(t * 9) % 2 ? CATA : CATB, { 1: dark ? '#FFF7EC' : '#FFFFFF', 2: '#3A2E44', 3: '#FF9DB0' }, catX, ground - 8 - bounce);
    for (let i = 0; i < 2; i++) { const b = BIRD[Math.floor(t * 4 + i) % 2], bx = wrap(g.cols * (0.2 + i * 0.35) + t * 4, g.cols + 20) - 10, by = Math.round(g.rows * (0.25 + 0.06 * i) + Math.sin(t * 1.4 + i) * 2); sprite(ctx, g, b, { 1: dark ? sc.fg : '#3A4A6B' }, bx, by); }
    hud(ctx, g, env, sc, t, dark);
  });

  /* ================= ドット宇宙 ================= */
  K.registerBg('echoPixelSpace', 'ドット宇宙', ['pop', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, e = fade(env, 0.6), dark = isDark(sc.bg), g = grid(env);
    const key = `pspace|${seed}|${colourKey(sc)}`, ink = dark ? sc.fg : mix(sc.fg, sc.bg, 0.2);
    ctx.globalAlpha = e; ctx.imageSmoothingEnabled = false;
    const back = cells(env, `${key}|back`, g, (x, w, h) => {
      bands(x, w, h, h, dark ? [mix(sc.bg, '#000000', 0.5), mix(sc.bg, '#000000', 0.35), mix(sc.bg, '#000000', 0.18), sc.bg, mix(sc.bg, sc.accent, 0.2)] : [mix(sc.bg, '#FFFFFF', 0.45), mix(sc.bg, '#FFFFFF', 0.25), sc.bg, mix(sc.bg, sc.accent, 0.2), mix(sc.bg, sc.accent2, 0.25)]);
      for (let i = 0; i < 8; i++) { const cx = Math.floor(J.r(seed, i, 1) * w), cy = Math.floor(J.r(seed, i, 2) * h), r = Math.round(6 + 10 * J.r(seed, i, 3)), c = [sc.accent, sc.accent2, sc.ghostB][i % 3]; x.fillStyle = rgba(c, dark ? 0.16 : 0.2); for (let yy = -r; yy <= r; yy++) for (let xx = -r; xx <= r; xx++) if (xx * xx + yy * yy < r * r && (xx + yy + cx + cy) % 2 === 0) x.fillRect(cx + xx, cy + yy, 1, 1); }
    });
    blit(ctx, back, env);
    for (let l = 0; l < 3; l++) {
      const strip = cells(env, `${key}|stars${l}`, g, (x, w, h) => { for (let i = 0; i < 30 + l * 10; i++) { x.fillStyle = rgba(l === 2 ? sc.accent2 : ink, 0.4 + 0.2 * l); const sx = Math.floor(J.r(seed, l * 100 + i, 1) * w), sy = Math.floor(J.r(seed, l * 100 + i, 2) * h); x.fillRect(sx, sy, 1, 1); if (l === 2 && i % 3 === 0) { x.fillRect(sx - 1, sy, 3, 1); x.fillRect(sx, sy - 1, 1, 3); } } });
      scroll(ctx, strip, env, Math.round(t * (1.5 + l * 3)) * g.c * 0.5, 1, 0, 1);
    }
    for (let i = 0; i < 12; i++) { const b = Math.floor(t * 2 + i * 1.7) % 4; if (b !== 0) continue; ctx.fillStyle = sc.accent2; const sx = Math.floor(J.r(seed, i, 41) * g.cols), sy = Math.floor(J.r(seed, i, 42) * g.rows * 0.8); pr(ctx, g, sx - 1, sy, 3, 1); pr(ctx, g, sx, sy - 1, 1, 3); }
    /* a banded planet with a ring, drifting slowly, and a small moon */
    const px = Math.round(g.cols * 0.78 - Math.sin(t * 0.15) * 2), py = Math.round(g.rows * 0.3), pR = Math.round(g.rows * 0.2);
    const planet = cells(env, `${key}|planet`, g, (x, w, h) => {
      const cx = Math.round(w * 0.78), cy = Math.round(h * 0.3);
      disc(x, cx, cy, pR, sc.accent); x.save(); x.beginPath(); for (let dy = -pR; dy <= pR; dy++) { const span = Math.floor(Math.sqrt(pR * pR - dy * dy + pR * 0.6)); x.rect(cx - span, cy + dy, span * 2 + 1, 1); } x.clip();
      for (let b = 0; b < 5; b++) { x.fillStyle = b % 2 ? mix(sc.accent, '#FFFFFF', 0.3) : mix(sc.accent, sc.accent2, 0.35); x.fillRect(cx - pR, cy - pR + Math.round(pR * 0.4 * b + 2), pR * 2 + 1, Math.max(1, Math.round(pR * 0.16))); }
      x.fillStyle = mix(sc.accent, '#000000', dark ? 0.45 : 0.25); for (let dy = -pR; dy <= pR; dy++) for (let dx = -pR; dx <= pR; dx++) if (dx * dx + dy * dy <= pR * pR && dx * 0.6 + dy * 0.8 > pR * 0.55 && (dx + dy) % 2 === 0) x.fillRect(cx + dx, cy + dy, 1, 1);
      x.restore();
      x.fillStyle = mix(sc.accent2, '#FFFFFF', 0.3); for (let a = 0; a < 360; a += 3) { const rad = a * Math.PI / 180, rx = Math.round(Math.cos(rad) * pR * 1.7), ry = Math.round(Math.sin(rad) * pR * 0.35); if (Math.sin(rad) > 0 || Math.abs(rx) > pR) x.fillRect(cx + rx, cy + ry - Math.round(rx * 0.12), 1, 1); }
    });
    blit(ctx, planet, env);
    /* a friendly rocket cruising with a flickering flame, and heart pickups */
    const rx = wrap(t * 7, g.cols + 30) - 15, ry = Math.round(g.rows * 0.62 + Math.sin(t * 1.6) * 3);
    const ROCKET = ['....11....', '...1221...', '..122221..', '..123321..', '..122221..', '.11222211.', '1.1222212.', '..1.11.1..'];
    sprite(ctx, g, ROCKET, { 1: dark ? sc.fg : '#3A3F6A', 2: '#FFFFFF', 3: sc.accent2 }, rx, ry);
    ctx.fillStyle = sc.accent2; const fl = Math.floor(t * 12) % 2; pr(ctx, g, rx + 4, ry + 8, 2, 2 + fl); ctx.fillStyle = sc.accent; pr(ctx, g, rx + 4, ry + 8, 2, 1 + fl);
    for (let i = 0; i < 4; i++) sprite(ctx, g, HEART, { 1: sc.accent, 2: mix(sc.accent, '#FFFFFF', 0.55) }, wrap(g.cols * 0.9 - t * 5 + i * 14, g.cols + 20) - 10, Math.round(g.rows * 0.25 + i * 4 + Math.sin(t * 2 + i) * 2));
    hud(ctx, g, env, sc, t, dark);
  });

  /* ================= ドット街 ================= */
  K.registerBg('echoPixelTown', 'ドット街', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, e = fade(env, 0.6), dark = isDark(sc.bg), g = grid(env);
    const key = `ptown|${seed}|${colourKey(sc)}`, ground = Math.round(g.rows * 0.8), ink = dark ? sc.fg : sc.fg;
    const skyBands = dark ? [mix(sc.bg, '#000000', 0.45), mix(sc.bg, '#000000', 0.25), sc.bg, mix(sc.bg, sc.accent, 0.3), mix(sc.accent, sc.accent2, 0.5)] : [mix(sc.bg, '#9CD3FF', 0.4), sc.bg, mix(sc.bg, sc.accent2, 0.3), mix(sc.bg, sc.accent, 0.35), mix(sc.accent2, '#FFFFFF', 0.3)];
    const bld = [mix(skyBands[3], dark ? '#000000' : sc.fg, dark ? 0.2 : 0.16), mix(skyBands[2], dark ? '#000000' : sc.fg, dark ? 0.4 : 0.32), mix(skyBands[1], dark ? '#000000' : sc.fg, dark ? 0.6 : 0.5)];
    ctx.globalAlpha = e; ctx.imageSmoothingEnabled = false;
    const sky = cells(env, `${key}|sky`, g, (x, w, h) => {
      bands(x, w, h, ground, skyBands);
      const sx = Math.round(w * 0.3), sy = ground - 10; disc(x, sx, sy, 9, mix(sc.accent2, '#FFFFFF', 0.4)); disc(x, sx, sy, 7, dark ? '#FFF6D8' : mix(sc.accent2, '#FFFFFF', 0.15));
      if (dark) for (let i = 0; i < 50; i++) { x.fillStyle = rgba(sc.fg, 0.4 + 0.5 * J.r(seed, i, 3)); x.fillRect(Math.floor(J.r(seed, i, 1) * w), Math.floor(J.r(seed, i, 2) * ground * 0.5), 1, 1); }
    });
    blit(ctx, sky, env);
    for (let l = 0; l < 3; l++) {
      const rows = [ground - 1, ground, ground + 1][l], strip = cells(env, `${key}|bld${l}`, g, (x, w, h) => {
        let bx = 0, i = 0; while (bx < w) { const bw = Math.round(6 + 8 * J.r(seed, l * 50 + i, 1)), bh = Math.round((5 + (3 - l) * 6 + 12 * J.r(seed, l * 50 + i, 2)) * (l === 0 ? 1.2 : 1));
          x.fillStyle = bld[l]; x.fillRect(bx, rows - bh, bw, bh + 2); if (J.r(seed, l * 50 + i, 3) > 0.6) x.fillRect(bx + 2, rows - bh - 3, 1, 3);
          for (let wy = rows - bh + 2; wy < rows - 1; wy += 3) for (let wx = bx + 1; wx < bx + bw - 1; wx += 3) if (J.r(seed, l * 50 + i, wy, wx) > (dark ? 0.35 : 0.55)) { x.fillStyle = dark ? (J.r(seed, wx, wy) > 0.5 ? '#FFE9A6' : '#FFC8D8') : mix(bld[l], '#FFFFFF', 0.55); x.fillRect(wx, wy, 1, 2); }
          bx += bw + (l === 0 ? 1 : 0); i++; }
      });
      scroll(ctx, strip, env, Math.round(t * (0.6 + l * 1.4)) * g.c * 0.5, 1, 0, 1);
    }
    /* a little train crossing on the elevated line, with lit windows */
    const trainY = Math.round(g.rows * 0.5), cyc = 14, ph = wrap(t + J.r(seed, 3) * cyc, cyc) / cyc, tx = -60 + (g.cols + 120) * ph;
    ctx.fillStyle = mix(bld[1], '#000000', 0.15); pr(ctx, g, 0, trainY + 5, g.cols, 1);
    for (let i = 0; i < 4; i++) { const cx = tx + i * 15; ctx.fillStyle = i % 2 ? sc.accent : mix(sc.accent, '#FFFFFF', 0.2); pr(ctx, g, cx, trainY, 14, 5); ctx.fillStyle = dark ? '#FFE9A6' : '#FFFFFF'; for (let w2 = 0; w2 < 4; w2++) pr(ctx, g, cx + 1 + w2 * 3, trainY + 1, 2, 2); ctx.fillStyle = mix(sc.accent, '#000000', 0.4); pr(ctx, g, cx, trainY + 4, 14, 1); }
    const street = cells(env, `${key}|street`, g, (x, w, h) => {
      x.fillStyle = dark ? mix(sc.bg, '#000000', 0.5) : mix(sc.bg, sc.fg, 0.28); x.fillRect(0, ground, w, h - ground);
      x.fillStyle = dark ? mix(sc.bg, '#000000', 0.3) : mix(sc.bg, sc.fg, 0.18); x.fillRect(0, ground, w, 2);
      x.fillStyle = rgba('#FFFFFF', 0.5); for (let xx = 0; xx < w; xx += 8) x.fillRect(xx, ground + Math.round((h - ground) * 0.55), 4, 1);
      for (let i = 0; i < 5; i++) { const lx = Math.round(w * (i + 0.5) / 5); x.fillStyle = mix(sc.fg, sc.bg, 0.4); x.fillRect(lx, ground - 12, 1, 12); x.fillRect(lx - 2, ground - 13, 5, 1); x.fillStyle = dark ? '#FFE9A6' : '#FFFFFF'; x.fillRect(lx - 2, ground - 12, 5, 1); }
    });
    scroll(ctx, street, env, Math.round(t * 4) * g.c * 0.5, 1, 0, 1);
    if (dark) { ctx.globalAlpha = e * 0.25; ctx.fillStyle = '#FFE9A6'; for (let i = 0; i < 5; i++) { const lx = ((g.cols * (i + 0.5) / 5 - t * 2) % g.cols + g.cols) % g.cols; for (let k = 0; k < 6; k++) pr(ctx, g, lx - k, ground - 11 + k * 2, 2 * k + 1, 2); } ctx.globalAlpha = e; }
    for (let i = 0; i < 4; i++) { const b = BIRD[Math.floor(t * 4 + i) % 2]; sprite(ctx, g, b, { 1: dark ? sc.fg : '#3A4A6B' }, wrap(g.cols * (0.1 + i * 0.22) + t * 5, g.cols + 20) - 10, Math.round(g.rows * (0.16 + 0.04 * (i % 2)) + Math.sin(t * 1.2 + i) * 2)); }
    const catX = wrap(t * 6, g.cols + 24) - 12; sprite(ctx, g, Math.floor(t * 8) % 2 ? CATA : CATB, { 1: dark ? '#FFF7EC' : '#FFFFFF', 2: '#3A2E44', 3: '#FF9DB0' }, catX, ground + 3);
    hud(ctx, g, env, sc, t, dark);
  });

  K.pixel = { grid, cells, blit, pr, disc, bands, cloud, text, sprite, hud, colourKey, shades, HEART, STAR, COIN, CATA, CATB, BIRD };

  /* in front: pixel sparkles, hearts and blossom squares drifting, kept sparse so the lyric stays readable */
  K.registerFront('echoPixelSparks', 'ドットきらきら', (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, e = fade(env, 1), dark = isDark(sc.bg), g = grid(env), pal = [sc.accent, sc.accent2, dark ? '#FFFFFF' : sc.fg];
    ctx.globalAlpha = e;
    for (let i = 0; i < 16; i++) {
      const sx = Math.floor(J.r(seed, i, 1) * g.cols), sy = wrap(Math.floor(J.r(seed, i, 2) * g.rows * 1.2) + t * (2 + 3 * J.r(seed, i, 3)), g.rows * 1.2) - g.rows * 0.1, on = Math.floor(t * 3 + i * 1.3) % 3 !== 0;
      if (!on) continue; ctx.fillStyle = pal[i % 3]; const y = Math.floor(sy);
      if (i % 4 === 0) { pr(ctx, g, sx - 1, y, 3, 1); pr(ctx, g, sx, y - 1, 1, 3); } else if (i % 4 === 1) { pr(ctx, g, sx, y, 2, 2); } else pr(ctx, g, sx, y, 1, 1);
    }
    for (let i = 0; i < 3; i++) { const sx = Math.floor(J.r(seed, i, 21) * g.cols), sy = Math.floor(wrap(J.r(seed, i, 22) * g.rows + t * (1.5 + i), g.rows * 1.1)); ctx.globalAlpha = e * 0.85; sprite(ctx, g, HEART, { 1: sc.accent, 2: mix(sc.accent, '#FFFFFF', 0.55) }, sx, sy - 3); }
    /* finishing: faint scanlines and a slow diagonal shine that steps across the frame */
    ctx.globalAlpha = e * (dark ? 0.07 : 0.04); ctx.fillStyle = dark ? '#000000' : sc.fg; for (let y = 0; y < g.rows; y += 2) pr(ctx, g, 0, y, g.cols, 1);
    const cyc = 12, ph = wrap(t + J.r(seed, 9) * cyc, cyc) / cyc, sx = -g.rows + (g.cols + g.rows * 2) * ph;
    ctx.globalAlpha = e * (dark ? 0.05 : 0.08); ctx.fillStyle = '#FFFFFF'; ctx.beginPath(); for (let y = 0; y < g.rows; y++) { const x0 = Math.round(sx + y * 0.8); ctx.rect(x0 * g.c, y * g.c, 3 * g.c, g.c + 0.5); } ctx.fill();
    ctx.globalAlpha = e;
  });
})();
