/* ECHO first-party backdrops for the default look 薄雾: 霧の稜線 (ridges dissolving into morning fog), 藤の花 (a wisteria arbour),
   湖畔の朝もや (a still lake with its mirror image). Also the small painter helpers (K.paint, K.sprite) the other default looks share. */
(() => {
  'use strict';
  const K = J.echo, TAU = K.TAU, clamp = K.clamp, mix = K.mix, rgba = K.rgba;
  const { U: Umin, isDark, wrap, glowDisc, star, vertical, fade, layer, scroll } = K;
  const r = J.r, ck = sc => [sc.bg, sc.fg, sc.accent, sc.accent2].join('');

  /* ---- shared painter helpers ---- */
  const SPRITES = new Map();
  /* a small cached picture (one flower cluster, one koi…): drawn once at the target's pixel density, then blitted */
  K.sprite = (ctx, key, w, h, draw) => {
    const q = Math.max(0.5, ctx.getTransform().a || 1), cw = Math.max(4, Math.ceil(w * q)), ch = Math.max(4, Math.ceil(h * q)), id = `${key}|${cw}x${ch}`;
    let c = SPRITES.get(id);
    if (c) { SPRITES.delete(id); SPRITES.set(id, c); return c; }
    c = document.createElement('canvas'); c.width = cw; c.height = ch;
    const g = c.getContext('2d'); g.setTransform(cw / w, 0, 0, ch / h, 0, 0); draw(g, w, h);
    SPRITES.set(id, c);
    while (SPRITES.size > 90) { const k = SPRITES.keys().next().value, old = SPRITES.get(k); old.width = old.height = 0; SPRITES.delete(k); }
    return c;
  };
  /* scroll a layer but blit only the horizontal band that actually holds paint (software canvases pay per pixel drawn) */
  const bandScroll = K.bandScroll = (ctx, cv, env, offset, y0, y1) => {
    const W = env.W, H = env.H, o = K.wrap(offset, W), a = Math.max(0, Math.floor(y0)), b = Math.min(H, Math.ceil(y1)), k = cv.height / H;
    if (b <= a) return;
    for (let x = -o; x < W; x += W) ctx.drawImage(cv, 0, a * k, cv.width, (b - a) * k, x, a, W, b - a);
  };
  const paint = K.paint = {
    /* a tiling height profile in 0..1: a few sine octaves with whole cycles, so a layer scrolled by its own width never shows a seam */
    profile(seed, salt, octaves = [[1, 0.5], [2, 0.28], [4, 0.14], [9, 0.08]]) {
      const parts = octaves.map(([c, a], i) => [c, a, r(seed, salt, i, 5) * TAU]);
      const sum = parts.reduce((s, p) => s + p[1], 0);
      return u => clamp(0.5 + 0.5 * parts.reduce((s, [c, a, ph]) => s + a * Math.sin(TAU * c * u + ph), 0) / sum);
    },
    /* fill everything under a profile; fill may be a colour or a function of the ridge's highest point */
    ridge(x, w, h, base, amp, fn, fill, step = 6) {
      let top = h; const pts = [];
      for (let px = -step; px <= w + step; px += step) { const y = base - amp * fn(px / w); pts.push([px, y]); top = Math.min(top, y); }
      x.fillStyle = typeof fill === 'function' ? fill(top) : fill;
      x.beginPath(); x.moveTo(-step, h + 4); for (const [px, y] of pts) x.lineTo(px, y); x.lineTo(w + step, h + 4); x.closePath(); x.fill();
      return pts;
    },
    /* soft long bands of mist, wrapped so the layer tiles */
    fog(x, w, h, y, band, colour, alpha, seed, count = 7) {
      for (let i = 0; i < count; i++) {
        const cx = w * (i + r(seed, i, 1) * 0.8) / count, cy = y + (r(seed, i, 2) - 0.5) * band, ew = w * (0.22 + 0.26 * r(seed, i, 3)), eh = band * (0.35 + 0.5 * r(seed, i, 4));
        for (const shift of [-w, 0, w]) {
          x.save(); x.translate(cx + shift, cy); x.scale(1, eh / ew);
          const g = x.createRadialGradient(0, 0, 0, 0, 0, ew / 2);
          g.addColorStop(0, rgba(colour, alpha)); g.addColorStop(0.55, rgba(colour, alpha * 0.5)); g.addColorStop(1, rgba(colour, 0));
          x.fillStyle = g; x.beginPath(); x.arc(0, 0, ew / 2, 0, TAU); x.fill(); x.restore();
        }
      }
    },
    birds(ctx, t, W, H, U, seed, colour, count = 5, y0 = 0.22) {
      ctx.strokeStyle = colour; ctx.lineWidth = Math.max(1.1, U * 0.002); ctx.lineCap = 'round';
      for (let b = 0; b < count; b++) {
        const bx = wrap(r(seed, b, 51) * W * 0.5 + t * U * (0.028 + 0.004 * b) - W * 0.1, W * 1.25) - W * 0.12 + b * U * 0.035, by = H * (y0 + 0.05 * r(seed, b, 52)) + Math.sin(t * 0.8 + b) * U * 0.01 + b * U * 0.011;
        const s = U * (0.012 + 0.005 * r(seed, b, 53)), flap = Math.sin(t * 5.5 + b * 2.1) * s * 0.6;
        ctx.beginPath(); ctx.moveTo(bx - s, by - flap); ctx.quadraticCurveTo(bx - s * 0.4, by - s * 0.55 - flap * 0.3, bx, by); ctx.quadraticCurveTo(bx + s * 0.4, by - s * 0.55 - flap * 0.3, bx + s, by - flap); ctx.stroke();
      }
    },
    stars(x, w, h, seed, U, count, limit = 0.6, alpha = 1) {
      for (let i = 0; i < count; i++) {
        const sy = r(seed, i, 2) * h * limit, fadeOut = 1 - sy / (h * (limit + 0.15));
        x.fillStyle = rgba('#FFFFFF', (0.25 + 0.7 * Math.pow(r(seed, i, 3), 2)) * fadeOut * alpha);
        x.beginPath(); x.arc(r(seed, i, 1) * w, sy, U * (0.0008 + 0.0016 * Math.pow(r(seed, i, 4), 2)), 0, TAU); x.fill();
      }
    },
    /* a fir: tiers of tapering boughs */
    fir(x, px, base, height, colour, tiers = 6) {
      x.fillStyle = colour;
      for (let i = 0; i < tiers; i++) {
        const v0 = i / tiers, y0 = base - height * (1 - v0 * 0.94), half = height * 0.17 * (0.25 + 0.75 * (i + 1) / tiers);
        x.beginPath(); x.moveTo(px, y0 - height * 0.05); x.quadraticCurveTo(px + half * 0.55, y0 + height * 0.02, px + half, y0 + height * 0.16 / tiers * 6 * 0.5);
        x.lineTo(px - half, y0 + height * 0.16 / tiers * 6 * 0.5); x.quadraticCurveTo(px - half * 0.55, y0 + height * 0.02, px, y0 - height * 0.05); x.fill();
      }
      x.fillRect(px - height * 0.012, base - height * 0.06, height * 0.024, height * 0.08);
    },
  };

  /* ================= 霧の稜線 ================= */
  const mistTone = sc => {
    const dark = isDark(sc.bg);
    return dark
      ? { dark, sky0: mix(sc.bg, '#04061C', 0.55), sky1: mix(sc.bg, sc.accent, 0.22), sky2: mix(sc.bg, sc.accent2, 0.3), fog: mix(sc.bg, sc.accent, 0.5), far: mix(sc.bg, sc.accent, 0.3), near: mix(sc.bg, '#03040F', 0.7), glow: '#DCE4FF' }
      : { dark, sky0: mix(sc.bg, sc.accent, 0.2), sky1: mix(sc.bg, '#FFFFFF', 0.5), sky2: mix(sc.bg, sc.accent2, 0.36), fog: mix(sc.bg, '#FFFFFF', 0.7), far: mix(sc.bg, sc.accent, 0.3), near: mix(sc.fg, sc.bg, 0.3), glow: '#FFF3DC' };
  };
  K.registerBg('echoMistRidge', '霧の稜線', ['calm', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), tone = mistTone(sc), key = `mr|${seed}|${ck(sc)}`;
    const portrait = H > W, sunX = W * (portrait ? 0.66 : 0.3 + 0.42 * r(seed, 1)), sunY = H * (portrait ? 0.24 : 0.3);
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|sky`, 0.25, (x, w, h) => {
      x.fillStyle = vertical(x, 0, h, [[0, tone.sky0], [0.5, tone.sky1], [0.86, tone.sky2], [1, tone.fog]]); x.fillRect(0, 0, w, h);
      glowDisc(x, sunX, sunY, U * 1.1, tone.glow, tone.dark ? 0.34 : 0.7);
      if (tone.dark) paint.stars(x, w, h, seed, U, 120, 0.5);
    }), 0, 0, W, H);
    /* the sun (or moon) in the haze */
    const breathe = 1 + 0.02 * Math.sin(t * 0.6);
    ctx.fillStyle = rgba(tone.glow, 0.28); ctx.beginPath(); ctx.arc(sunX, sunY, U * 0.105 * breathe, 0, TAU); ctx.fill();
    ctx.fillStyle = rgba(tone.dark ? '#F4F7FF' : '#FFFFFF', 0.95); ctx.beginPath(); ctx.arc(sunX, sunY, U * 0.058, 0, TAU); ctx.fill();
    if (tone.dark) { ctx.fillStyle = rgba(tone.sky1, 0.5); [[-0.02, -0.01, 0.012], [0.015, 0.02, 0.009], [0.02, -0.02, 0.006]].forEach(([dx, dy, s]) => { ctx.beginPath(); ctx.arc(sunX + dx * U, sunY + dy * U, U * s, 0, TAU); ctx.fill(); }); }
    /* six ridges, each paler and bluer the farther away, each veiled at its foot by fog */
    const rows = [[0.5, 0.16, 0.0], [0.58, 0.15, 0.2], [0.66, 0.16, 0.42], [0.75, 0.15, 0.64], [0.84, 0.13, 0.82], [0.95, 0.1, 1]];
    rows.forEach(([level, amp, depth], i) => {
      const colour = mix(tone.far, tone.near, Math.pow(depth, 1.15)), fogAt = mix(tone.fog, colour, 0.25 + 0.5 * depth);
      const ridge = layer(env, `${key}|r${i}`, i < 3 ? 0.4 : 0.6, (x, w, h) => {
        const fn = paint.profile(seed, i * 7 + 1, [[1 + (i % 2), 0.5], [2 + i, 0.3], [5 + i * 2, 0.14], [12 + i * 3, 0.06]]);
        const pts = paint.ridge(x, w, h, h * level, h * amp, fn, top => vertical(x, top, top + h * (1 - level + amp) * 0.95, [[0, colour], [0.55, mix(colour, fogAt, 0.5)], [1, fogAt]]));
        if (i >= 4) for (let k = 0; k < 20; k++) {                    /* firs standing along the near ridges */
          const px = w * (k + r(seed, i, k, 8) * 0.85) / 20, [, y] = pts[Math.min(pts.length - 1, Math.max(0, Math.round(px / 6) + 1))];
          paint.fir(x, px, y + U * 0.004, U * (0.045 + 0.09 * Math.pow(r(seed, i, k, 9), 1.5)) * (i === 5 ? 1.6 : 1), mix(colour, tone.near, 0.25), 6);
        }
      });
      bandScroll(ctx, ridge, env, t * U * (0.0015 + 0.004 * depth) * (i % 2 ? 1 : -1), H * (level - amp) - U * (i >= 4 ? 0.26 : 0.03), H);
      if (i < 5) {
        const fogLayer = layer(env, `${key}|f${i}`, 0.35, (x, w, h) => paint.fog(x, w, h, h * (level + 0.02), h * 0.11, tone.fog, tone.dark ? 0.32 : 0.62, seed + i * 13));
        bandScroll(ctx, fogLayer, env, t * U * (0.006 + 0.008 * depth) * (i % 2 ? -1 : 1) + i * 137, H * (level + 0.02 - 0.115), H * (level + 0.02 + 0.115));
      }
    });
    /* light through the haze */
    ctx.globalCompositeOperation = 'screen';
    for (let k = 0; k < 5; k++) {
      const ang = Math.PI * (0.32 + 0.09 * k) + Math.sin(t * 0.13 + k) * 0.04, reach = Math.hypot(W, H);
      const g = ctx.createLinearGradient(sunX, sunY, sunX + Math.cos(ang) * reach, sunY + Math.sin(ang) * reach);
      g.addColorStop(0, rgba(tone.glow, (tone.dark ? 0.1 : 0.2) * (0.7 + 0.3 * Math.sin(t * 0.4 + k * 2)))); g.addColorStop(0.7, rgba(tone.glow, 0));
      ctx.fillStyle = g; ctx.beginPath(); ctx.moveTo(sunX, sunY); ctx.lineTo(sunX + Math.cos(ang - 0.035) * reach, sunY + Math.sin(ang - 0.035) * reach); ctx.lineTo(sunX + Math.cos(ang + 0.035) * reach, sunY + Math.sin(ang + 0.035) * reach); ctx.fill();
    }
    ctx.globalCompositeOperation = 'source-over';
    paint.birds(ctx, t, W, H, U, seed, rgba(tone.dark ? '#DCE4FF' : mix(sc.fg, sc.bg, 0.3), 0.6), 5, 0.2);
    for (let i = 0; i < 30; i++) {                                     /* motes rising through the fog */
      const y = H - wrap(r(seed, i, 71) * H * 1.2 + t * H * (0.012 + 0.024 * r(seed, i, 72)), H * 1.2) + H * 0.1, x = r(seed, i, 73) * W + Math.sin(t * 0.5 + i) * U * 0.03;
      ctx.fillStyle = rgba(tone.dark ? '#FFFFFF' : sc.accent2, 0.15 + 0.3 * (0.5 + 0.5 * Math.sin(t * 1.2 + i * 1.7))); ctx.beginPath(); ctx.arc(x, y, U * (0.0018 + 0.003 * r(seed, i, 74)), 0, TAU); ctx.fill();
    }
  });

  /* ================= 藤の花 ================= */
  const wisteriaShades = sc => isDark(sc.bg)
    ? { top: mix('#6A44C8', sc.accent, 0.3), tip: mix('#C4A4FF', sc.accent, 0.3), hi: '#F2E8FF' }
    : { top: mix(sc.accent, '#5B3AA8', 0.4), tip: mix(sc.accent, '#FFFFFF', 0.72), hi: '#FFFFFF' };
  const raceme = (ctx, seed, L, Wd, sh, blur) => K.sprite(ctx, `wist|${seed}|${sh.top}|${sh.tip}|${blur}`, Wd * 1.6, L * 1.1, (g, w, h) => {
    const rows = 26, cx = w / 2;
    g.lineCap = 'round';
    g.strokeStyle = rgba(mix(sh.top, '#2E5A34', 0.55), 0.9); g.lineWidth = Wd * 0.03; g.beginPath(); g.moveTo(cx, 0); g.quadraticCurveTo(cx + Wd * 0.05, L * 0.4, cx, L * 0.92); g.stroke();
    for (let j = 0; j < rows; j++) {
      const v = j / rows, y = L * (0.02 + 0.92 * v), half = Wd * 0.52 * Math.pow(1 - v * 0.95, 0.6) * (0.6 + 0.4 * Math.sin(Math.PI * Math.min(1, v * 2.4 + 0.25))), n = 4 + Math.round(5 * (1 - v));
      for (let k = 0; k < n; k++) {
        const u = (k + 0.5 + (r(seed, j, k, 1) - 0.5) * 0.7) / n * 2 - 1, px = cx + u * half, py = y + (r(seed, j, k, 2) - 0.5) * L * 0.03;
        const s = Wd * (0.085 + 0.06 * (1 - v)) * (0.8 + 0.5 * r(seed, j, k, 3)), c = mix(sh.top, sh.tip, Math.pow(v, 0.8) * 0.9 + 0.1 * r(seed, j, k, 4));
        g.save(); g.translate(px, py); g.rotate((r(seed, j, k, 5) - 0.5) * 1.2 + u * 0.4);
        const grad = g.createRadialGradient(-s * 0.2, -s * 0.3, 0, 0, 0, s * 1.2); grad.addColorStop(0, mix(c, sh.hi, 0.5)); grad.addColorStop(1, c);
        g.fillStyle = grad; g.beginPath(); g.ellipse(0, 0, s * 0.62, s, 0, 0, TAU); g.fill();
        g.fillStyle = rgba(mix(c, '#2A1A5A', 0.4), 0.28); g.beginPath(); g.ellipse(s * 0.1, s * 0.35, s * 0.4, s * 0.45, 0, 0, TAU); g.fill();
        g.restore();
      }
    }
  });
  K.registerBg('echoWisteria', '藤の花', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.9), dark = isDark(sc.bg), key = `wi|${seed}|${ck(sc)}`, sh = wisteriaShades(sc);
    ctx.globalAlpha = e;
    const back = layer(env, `${key}|back`, 0.5, (x, w, h) => {
      x.fillStyle = vertical(x, 0, h, [[0, dark ? mix(sc.bg, '#05061F', 0.4) : mix(sc.bg, sc.accent, 0.12)], [0.6, dark ? mix(sc.bg, sc.accent, 0.18) : mix(sc.bg, '#FFFFFF', 0.55)], [1, dark ? mix(sc.bg, '#0A0A28', 0.3) : mix(sc.bg, sc.accent2, 0.25)]]); x.fillRect(0, 0, w, h);
      if (dark) glowDisc(x, w * 0.78, h * 0.2, U * 0.7, '#DCE4FF', 0.32);
      x.globalCompositeOperation = dark ? 'screen' : 'source-over';
      for (let i = 0; i < 26; i++) { const c = [sc.accent, sc.accent2, '#FFFFFF'][i % 3], px = r(seed, i, 1) * w, py = h * (0.25 + 0.6 * r(seed, i, 2)), rr = U * (0.02 + 0.06 * r(seed, i, 3)); const g = x.createRadialGradient(px, py, 0, px, py, rr); g.addColorStop(0, rgba(c, dark ? 0.2 : 0.3)); g.addColorStop(0.55, rgba(c, dark ? 0.1 : 0.16)); g.addColorStop(1, rgba(c, 0)); x.fillStyle = g; x.beginPath(); x.arc(px, py, rr, 0, TAU); x.fill(); }
      x.globalCompositeOperation = 'source-over';
      /* the stone path and dappled light on it */
      const gy = h * 0.86; x.fillStyle = vertical(x, gy, h, [[0, dark ? mix(sc.bg, '#000000', 0.25) : mix(sc.bg, sc.fg, 0.1)], [1, dark ? mix(sc.bg, '#000000', 0.5) : mix(sc.bg, sc.fg, 0.24)]]); x.beginPath(); x.moveTo(0, gy + U * 0.02); x.quadraticCurveTo(w * 0.5, gy - U * 0.03, w, gy + U * 0.01); x.lineTo(w, h); x.lineTo(0, h); x.fill();
      for (let i = 0; i < 46; i++) { x.fillStyle = rgba(i % 3 ? '#FFFFFF' : sc.accent2, (dark ? 0.06 : 0.16) + 0.1 * r(seed, i, 6)); x.beginPath(); x.ellipse(r(seed, i, 4) * w, gy + (h - gy) * (0.15 + 0.85 * r(seed, i, 5)), U * (0.01 + 0.03 * r(seed, i, 7)), U * (0.004 + 0.008 * r(seed, i, 8)), 0, 0, TAU); x.fill(); }
      for (let i = 0; i < 70; i++) { x.fillStyle = rgba(mix(sh.tip, sh.top, r(seed, i, 12)), 0.85); x.save(); x.translate(r(seed, i, 9) * w, gy + (h - gy) * (0.1 + 0.9 * r(seed, i, 10))); x.rotate(r(seed, i, 11) * TAU); x.beginPath(); x.ellipse(0, 0, U * 0.005, U * 0.0028, 0, 0, TAU); x.fill(); x.restore(); }
    });
    ctx.drawImage(back, 0, 0, W, H);
    /* the arbour: two beams and cross rafters, with vines wound along them */
    const beam = layer(env, `${key}|beam`, 0.5, (x, w, h) => {
      const wood = dark ? mix(sc.bg, '#000000', 0.55) : mix(sc.fg, sc.bg, 0.3), by = h * 0.05, bh = U * 0.05;
      x.fillStyle = wood; x.fillRect(0, by, w, bh); x.fillStyle = rgba('#FFFFFF', dark ? 0.06 : 0.12); x.fillRect(0, by, w, bh * 0.16);
      for (let i = 0; i < 9; i++) { x.fillStyle = wood; x.fillRect(w * (i + 0.5) / 9 - U * 0.012, 0, U * 0.024, by + bh * 1.3); }
      x.strokeStyle = rgba(mix('#3E7A48', sc.bg, dark ? 0.6 : 0.3), 0.95); x.lineWidth = U * 0.006; x.lineCap = 'round';
      for (let i = 0; i < 6; i++) { x.beginPath(); for (let px = -10; px <= w + 10; px += 12) { const y = by + bh * 0.9 + Math.sin(px / (U * 0.09) + i * 1.7) * U * 0.012 + i * U * 0.004; px === -10 ? x.moveTo(px, y) : x.lineTo(px, y); } x.stroke(); }
      for (let i = 0; i < 90; i++) { x.fillStyle = rgba(mix('#4E9A5A', sc.bg, dark ? 0.55 : 0.2), 0.95); x.save(); x.translate(r(seed, i, 21) * w, by + bh * (0.6 + r(seed, i, 22) * 1.4)); x.rotate(r(seed, i, 23) * TAU); x.beginPath(); x.ellipse(0, 0, U * 0.014, U * 0.007, 0, 0, TAU); x.fill(); x.restore(); }
    });
    /* racemes at three depths: far small and hazy, near large and swaying */
    const RACEMES = [[16, 0.19, 0.06, 0.55, 0.3, 0.6], [11, 0.3, 0.085, 0.85, 0.45, 1], [7, 0.44, 0.12, 1, 0.7, 1.4]];
    RACEMES.forEach(([n, len, wid, alpha, yoff, sway], depth) => {
      for (let i = 0; i < n; i++) {
        const px = W * (i + 0.5 + (r(seed, depth, i, 31) - 0.5) * 0.7) / n, L = U * len * (0.6 + 0.75 * r(seed, depth, i, 32)), Wd = U * wid * (0.9 + 0.45 * r(seed, depth, i, 33));
        const spr = raceme(ctx, J.h(seed, depth, i % 5), L, Wd, sh, depth);
        const ang = Math.sin(t * (0.5 + 0.2 * depth) + i * 1.3 + depth) * 0.045 * sway + Math.sin(t * 0.23 + i) * 0.02 * sway;
        ctx.save(); ctx.globalAlpha = e * alpha; ctx.translate(px, H * 0.09); ctx.rotate(ang);
        ctx.drawImage(spr, -Wd * 0.8, 0, Wd * 1.6, L * 1.1); ctx.restore();
      }
      if (depth === 0) ctx.drawImage(beam, 0, 0, W, H);
    });
    /* petals let go and drifting down */
    for (let i = 0; i < 40; i++) {
      const life = wrap(t * (0.035 + 0.03 * r(seed, i, 41)) + r(seed, i, 42), 1), px = r(seed, i, 43) * W + Math.sin(t * 0.7 + i * 2) * U * 0.05 + life * U * 0.08, py = -U * 0.02 + life * H * 1.05;
      ctx.save(); ctx.translate(px, py); ctx.rotate(t * (0.5 + r(seed, i, 44)) + i); ctx.fillStyle = rgba(mix(sh.tip, sh.top, r(seed, i, 45)), 0.9 * Math.sin(Math.PI * life) ** 0.5);
      ctx.beginPath(); ctx.ellipse(0, 0, U * (0.005 + 0.004 * r(seed, i, 46)), U * (0.0028 + 0.002 * r(seed, i, 46)) * (0.6 + 0.4 * Math.cos(t * 2 + i)), 0, 0, TAU); ctx.fill(); ctx.restore();
    }
    /* sun through the leaves: slow shafts */
    ctx.globalCompositeOperation = 'screen';
    for (let k = 0; k < 4; k++) {
      const x0 = W * (0.15 + 0.22 * k) + Math.sin(t * 0.15 + k) * U * 0.03, g = ctx.createLinearGradient(x0, 0, x0 + U * 0.25, H);
      g.addColorStop(0, rgba(dark ? sc.accent : '#FFFFFF', dark ? 0.1 : 0.2)); g.addColorStop(1, rgba('#FFFFFF', 0));
      ctx.fillStyle = g; ctx.beginPath(); ctx.moveTo(x0, 0); ctx.lineTo(x0 + U * 0.06, 0); ctx.lineTo(x0 + U * 0.36, H); ctx.lineTo(x0 + U * 0.16, H); ctx.fill();
    }
    ctx.globalCompositeOperation = 'source-over';
  });

  /* ================= 湖畔の朝もや ================= */
  K.registerBg('echoLakeMist', '湖畔の朝もや', ['calm', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), tone = mistTone(sc), key = `lk|${seed}|${ck(sc)}`, hz = H * 0.6, portrait = H > W;
    const sunX = W * (portrait ? 0.62 : 0.32 + 0.4 * r(seed, 2)), sunY = hz - H * 0.17;
    ctx.globalAlpha = e;
    /* the world above the water, painted once; the water shows it upside-down */
    const world = layer(env, `${key}|world`, 0.5, (x, w, h) => {
      x.fillStyle = vertical(x, 0, hz, [[0, tone.sky0], [0.6, tone.sky1], [1, tone.sky2]]); x.fillRect(0, 0, w, hz);
      glowDisc(x, sunX, sunY, U * 0.9, tone.glow, tone.dark ? 0.35 : 0.75);
      if (tone.dark) paint.stars(x, w, hz, seed, U, 110, 0.85);
      x.fillStyle = rgba(tone.dark ? '#F4F7FF' : '#FFFFFF', 0.96); x.beginPath(); x.arc(sunX, sunY, U * 0.052, 0, TAU); x.fill();
      [[0.7, 0.4, 0.3, 0], [0.66, 0.3, 0.55, 3], [0.62, 0.2, 0.8, 6]].forEach(([lvl, amp, depth, salt], i) => {
        const colour = mix(tone.far, tone.near, depth), fn = paint.profile(seed, salt + 20, [[1, 0.45], [2, 0.3], [5, 0.16], [11, 0.09]]);
        paint.ridge(x, w, hz + 2, hz - U * 0.004 * i, hz * amp, fn, top => vertical(x, top, hz, [[0, colour], [0.75, mix(colour, tone.fog, 0.55)], [1, tone.fog]]));
      });
      x.fillStyle = vertical(x, hz - U * 0.05, hz, [[0, rgba(tone.fog, 0)], [1, rgba(tone.fog, 0.55)]]); x.fillRect(0, hz - U * 0.05, w, U * 0.05);
    });
    ctx.drawImage(world, 0, 0, W, H);
    /* mirror image: the same picture flipped, dimmed, wobbled by ripples */
    ctx.save(); ctx.beginPath(); ctx.rect(0, hz, W, H - hz); ctx.clip();
    ctx.translate(0, hz * 2); ctx.scale(1, -1); ctx.globalAlpha = e * (tone.dark ? 0.7 : 0.85);
    for (let strip = 0; strip < 14; strip++) {
      const y0 = hz * strip / 14, sy = y0 / H * world.height, sh = hz / 14 / H * world.height, wob = Math.sin(t * 1.3 + strip * 1.7) * U * 0.004 * (1 + (14 - strip) * 0.12);
      ctx.drawImage(world, 0, sy, world.width, sh + 1, wob, y0 - 0.5, W, hz / 14 + 1);
    }
    ctx.restore();
    ctx.globalAlpha = e;
    ctx.fillStyle = vertical(ctx, hz, H, [[0, rgba(mix(tone.fog, sc.accent, 0.15), 0.12)], [1, rgba(tone.dark ? '#02030C' : mix(sc.accent, sc.fg, 0.5), tone.dark ? 0.6 : 0.34)]]); ctx.fillRect(0, hz, W, H - hz);
    /* ripples and the path of light */
    ctx.lineCap = 'round';
    for (let i = 0; i < 46; i++) {
      const v = r(seed, i, 21), y = hz + (H - hz) * (0.03 + 0.95 * v * v), l = U * (0.03 + 0.14 * v), x0 = wrap(r(seed, i, 22) * W * 1.3 + t * U * (0.006 + 0.02 * v), W * 1.3) - W * 0.15;
      ctx.strokeStyle = rgba(i % 4 ? '#FFFFFF' : tone.glow, (tone.dark ? 0.16 : 0.34) * (0.4 + v)); ctx.lineWidth = Math.max(1, U * (0.0014 + 0.0026 * v));
      ctx.beginPath(); ctx.moveTo(x0, y); ctx.quadraticCurveTo(x0 + l / 2, y - U * 0.004 * (1 + v), x0 + l, y); ctx.stroke();
    }
    ctx.globalCompositeOperation = 'screen';
    for (let i = 0; i < 34; i++) { const v = i / 34, y = hz + (H - hz) * (0.02 + 0.86 * v), hw = U * (0.008 + 0.09 * v) * (0.5 + r(seed, i, 8)), b = 0.5 + 0.5 * Math.sin(t * (0.8 + r(seed, i, 9)) + i * 2.1); ctx.fillStyle = rgba(tone.glow, (tone.dark ? 0.18 : 0.3) * b * (1 - v * 0.5)); ctx.fillRect(sunX - hw + (r(seed, i, 10) - 0.5) * U * 0.03, y, hw * 2, Math.max(1, U * 0.0032)); }
    ctx.globalCompositeOperation = 'source-over';
    /* a boat, a lantern at its bow */
    const bx = W * (portrait ? 0.4 : 0.55) + Math.sin(t * 0.12) * U * 0.04, by = hz + (H - hz) * 0.2 + Math.sin(t * 1.1) * U * 0.002, bs = U * 0.11;
    ctx.fillStyle = tone.dark ? '#02030C' : mix(sc.fg, sc.bg, 0.15); ctx.beginPath(); ctx.moveTo(bx - bs, by - bs * 0.08); ctx.quadraticCurveTo(bx, by + bs * 0.24, bx + bs, by - bs * 0.14); ctx.lineTo(bx + bs * 0.86, by - bs * 0.02); ctx.quadraticCurveTo(bx, by + bs * 0.16, bx - bs * 0.9, by + bs * 0.0); ctx.fill();
    ctx.fillRect(bx + bs * 0.02, by - bs * 0.46, bs * 0.04, bs * 0.4); ctx.beginPath(); ctx.moveTo(bx + bs * 0.06, by - bs * 0.44); ctx.lineTo(bx + bs * 0.5, by - bs * 0.12); ctx.lineTo(bx + bs * 0.06, by - bs * 0.12); ctx.fill();
    ctx.globalCompositeOperation = 'screen'; glowDisc(ctx, bx - bs * 0.9, by - bs * 0.2, U * 0.05, '#FFCE7A', tone.dark ? 0.8 : 0.35); ctx.globalCompositeOperation = 'source-over';
    ctx.fillStyle = '#FFE2A0'; ctx.beginPath(); ctx.arc(bx - bs * 0.9, by - bs * 0.2, U * 0.005, 0, TAU); ctx.fill();
    ctx.strokeStyle = rgba('#FFFFFF', 0.3); ctx.lineWidth = Math.max(1, U * 0.0018); ctx.beginPath(); ctx.moveTo(bx - bs * 1.05, by + bs * 0.06); ctx.quadraticCurveTo(bx - bs * 1.9, by + bs * 0.12, bx - bs * 3, by + bs * 0.08); ctx.moveTo(bx + bs * 1.05, by + bs * 0.04); ctx.quadraticCurveTo(bx + bs * 1.8, by + bs * 0.1, bx + bs * 2.7, by + bs * 0.08); ctx.stroke();
    /* mist lying on the water */
    const mistLayer = layer(env, `${key}|mist`, 0.35, (x, w, h) => { paint.fog(x, w, h, hz + U * 0.01, U * 0.12, tone.fog, tone.dark ? 0.26 : 0.42, seed + 5, 8); paint.fog(x, w, h, hz + (H - hz) * 0.55, U * 0.1, tone.fog, tone.dark ? 0.12 : 0.2, seed + 9, 6); });
    scroll(ctx, mistLayer, env, t * U * 0.008); scroll(ctx, mistLayer, env, -t * U * 0.005 + 400, 1, U * 0.02, 0.6);
    /* reeds at both banks, bending in the breeze */
    ctx.strokeStyle = tone.dark ? '#02030C' : mix(sc.fg, sc.bg, 0.2); ctx.lineWidth = Math.max(1.5, U * 0.004); ctx.lineCap = 'round';
    for (let i = 0; i < 44; i++) {
      const left = i < 24, px = left ? W * 0.13 * r(seed, i, 61) : W * (0.86 + 0.14 * r(seed, i, 61)), h0 = U * (0.09 + 0.16 * r(seed, i, 62)), base = H + U * 0.01, lean = (left ? 1 : -1) * U * (0.02 + 0.03 * r(seed, i, 63)), sw = Math.sin(t * 0.9 + i * 1.3) * U * 0.012;
      ctx.beginPath(); ctx.moveTo(px, base); ctx.quadraticCurveTo(px + lean * 0.3, base - h0 * 0.6, px + lean + sw, base - h0); ctx.stroke();
      if (i % 3 === 0) { ctx.fillStyle = ctx.strokeStyle; ctx.beginPath(); ctx.ellipse(px + lean * 0.96 + sw, base - h0 * 0.94, U * 0.004, U * 0.016, Math.atan2(-lean, h0) * -1, 0, TAU); ctx.fill(); }
    }
    /* dragonfly light: fireflies at dusk, motes by day */
    for (let i = 0; i < 22; i++) {
      const px = r(seed, i, 71) * W + Math.sin(t * 0.4 + i) * U * 0.05, py = hz + (H - hz) * (0.1 + 0.8 * r(seed, i, 72)) + Math.cos(t * 0.5 + i * 2) * U * 0.03, b = Math.pow(0.5 + 0.5 * Math.sin(t * (0.6 + r(seed, i, 73)) + i * 3), 2);
      ctx.fillStyle = rgba(tone.dark ? '#FFF0A8' : '#FFFFFF', (tone.dark ? 0.9 : 0.6) * b); ctx.beginPath(); ctx.arc(px, py, U * (0.002 + 0.002 * r(seed, i, 74)), 0, TAU); ctx.fill();
    }
  });

  /* in front of the lyric: two drifting veils of fog and a few slow motes; light enough that text always reads */
  K.registerFront('echoMistDrift', '霧の流れ', (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, U = Umin(env), seed = P.seed || 1, e = fade(env, 1.2), tone = mistTone(sc);
    ctx.globalAlpha = e;
    const veil = layer(env, `mf|${ck(sc)}`, 0.3, (x, w, h) => { paint.fog(x, w, h, h * 0.92, h * 0.14, tone.fog, tone.dark ? 0.32 : 0.5, 3, 6); paint.fog(x, w, h, h * 0.04, h * 0.1, tone.fog, tone.dark ? 0.22 : 0.4, 8, 5); });
    scroll(ctx, veil, env, t * U * 0.012);
    ctx.globalCompositeOperation = 'screen';
    for (let i = 0; i < 12; i++) { const px = r(seed, i, 1) * W, py = H - wrap(r(seed, i, 2) * H + t * H * (0.01 + 0.02 * r(seed, i, 3)), H), b = 0.5 + 0.5 * Math.sin(t * 0.9 + i * 2); ctx.fillStyle = rgba(tone.dark ? sc.accent : '#FFFFFF', 0.35 * b); ctx.beginPath(); ctx.arc(px + Math.sin(t * 0.4 + i) * U * 0.03, py, U * (0.002 + 0.003 * r(seed, i, 4)), 0, TAU); ctx.fill(); }
    if (K.luxe) K.luxe(ctx, env, { seed: (P && P.seed) || 1, tint: ['#FFFFFF', sc.accent, sc.accent2], strength: 0.55, bokeh: 3, glitter: 6 });
  });
})();
