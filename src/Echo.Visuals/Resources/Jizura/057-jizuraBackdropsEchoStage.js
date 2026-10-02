/* ECHO first-party backdrops, general-purpose stage scenes: ネオン地平線, プリズム, リキッド, 稜線ライン, ワープ, コックピット, ライブステージ.
   Unlike the per-look scenes these carry no story of their own: every colour is taken from the current scheme, so they suit any look
   (dark schemes glow with additive light; light schemes switch to inked lines and soft washes). Static art lives in cached layers. */
(() => {
  'use strict';
  const K = J.echo, TAU = K.TAU, clamp = K.clamp, mix = K.mix, rgba = K.rgba, r = J.r;
  const { U: Umin, isDark, wrap, fade, vertical, glowDisc, star, layer } = K;
  const ck = sc => [sc.bg, sc.fg, sc.accent, sc.accent2, sc.ghostA, sc.ghostB].join('');
  const frac = v => v - Math.floor(v);
  /* the colours every scene draws from */
  const tones = sc => {
    const dark = isDark(sc.bg);
    return dark
      ? { dark, bg0: mix(sc.bg, '#000000', 0.6), bg1: mix(sc.bg, '#000000', 0.32), c1: mix(sc.accent, '#FFFFFF', 0.12), c2: mix(sc.accent2, '#FFFFFF', 0.1), c3: mix(sc.ghostA || sc.accent, '#FFFFFF', 0.25), line: mix(sc.fg, sc.bg, 0.2), hi: mix(sc.accent, '#FFFFFF', 0.6), ink: '#000000' }
      : { dark, bg0: mix(sc.bg, '#FFFFFF', 0.55), bg1: sc.bg, c1: mix(sc.accent, '#000000', 0.06), c2: mix(sc.accent2, '#000000', 0.04), c3: sc.ghostA || sc.accent, line: mix(sc.fg, sc.bg, 0.2), hi: mix(sc.accent, '#FFFFFF', 0.7), ink: mix(sc.fg, '#000000', 0.2) };
  };
  const glow = (ctx, T, x, y, rad, c, a) => { ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over'; glowDisc(ctx, x, y, rad, c, T.dark ? a : a * 0.7); ctx.restore(); };

  /* ================= ネオン地平線 ================= */
  K.registerBg('echoGridHorizon', 'ネオン地平線', ['pop', 'glitch', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), T = tones(sc), portrait = H > W;
    const hz = H * (portrait ? 0.56 : 0.6), cx = W * 0.5, sunR = U * (portrait ? 0.3 : 0.27), sunY = hz - sunR * 0.5, key = `gh|${seed}|${ck(sc)}`;
    const skyStops = [[0, T.dark ? mix(sc.bg, '#000000', 0.66) : mix(sc.bg, sc.accent, 0.3)], [0.62, T.dark ? mix(sc.bg, sc.accent, 0.3) : mix(sc.bg, '#FFFFFF', 0.5)], [1, T.dark ? mix(sc.accent2, sc.bg, 0.3) : mix(sc.accent2, '#FFFFFF', 0.55)]];
    const skyAt = y => { const u = clamp(y / hz), i = u < 0.62 ? 0 : 1, a = skyStops[i], b = skyStops[i + 1], k = i ? (u - 0.62) / 0.38 : u / 0.62; return mix(a[1], b[1], clamp(k)); };
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|sky`, 0.5, (x, w, h) => {
      x.fillStyle = vertical(x, 0, hz, skyStops); x.fillRect(0, 0, w, hz + 2);
      if (T.dark) for (let i = 0; i < 110; i++) { x.fillStyle = rgba('#FFFFFF', (0.25 + 0.6 * r(seed, i, 3)) * (1 - r(seed, i, 2) * 0.7)); x.beginPath(); x.arc(r(seed, i, 1) * w, r(seed, i, 2) * hz * 0.85, U * (0.0012 + 0.002 * r(seed, i, 4)), 0, TAU); x.fill(); }
    }), 0, 0, W, H);
    /* the sun, sliced by bands that crawl downward */
    glow(ctx, T, cx, sunY, sunR * 2.6, T.c2, 0.5);
    ctx.save(); ctx.beginPath(); ctx.rect(0, 0, W, hz); ctx.clip(); ctx.beginPath(); ctx.arc(cx, sunY, sunR, 0, TAU); ctx.clip();
    const sg = ctx.createLinearGradient(0, sunY - sunR, 0, sunY + sunR); sg.addColorStop(0, T.dark ? mix(sc.accent2, '#FFFFFF', 0.55) : mix(sc.accent2, '#FFFFFF', 0.4)); sg.addColorStop(0.55, sc.accent2); sg.addColorStop(1, sc.accent); ctx.fillStyle = sg; ctx.fillRect(cx - sunR, sunY - sunR, sunR * 2, sunR * 2);
    const ph = frac(t * 0.12);
    for (let k = 0; k < 10; k++) { const q = k + ph, yy = sunY + sunR * 0.02 + sunR * 0.11 * q, th = sunR * (0.01 + 0.012 * q); ctx.fillStyle = skyAt(Math.min(yy, hz)); ctx.fillRect(cx - sunR, yy, sunR * 2, th); }
    ctx.restore();
    ctx.drawImage(layer(env, `${key}|ridge`, 0.5, (x, w, h) => {
      const ridge = (base, amp, col, edge, id) => { x.fillStyle = col; x.beginPath(); x.moveTo(0, hz + 2); const pts = []; for (let i = 0; i <= 22; i++) { const px = w * i / 22, dx = Math.abs(px - cx) / (w * 0.5), hh = amp * (0.25 + 0.75 * Math.pow(dx, 0.8)) * (0.45 + 0.55 * r(seed, id, i, 1)); pts.push([px, base - hh]); x.lineTo(px, base - hh); } x.lineTo(w, hz + 2); x.fill(); x.strokeStyle = edge; x.lineWidth = Math.max(1.2, U * 0.0028); x.beginPath(); pts.forEach(([px, py], i) => (i ? x.lineTo(px, py) : x.moveTo(px, py))); x.stroke(); };
      ridge(hz + 2, U * 0.24, T.dark ? mix(sc.bg, '#000000', 0.78) : mix(sc.bg, sc.accent, 0.42), rgba(T.c1, T.dark ? 0.85 : 0.6), 1);
      ridge(hz + 2, U * 0.13, T.dark ? mix(sc.bg, '#000000', 0.88) : mix(sc.bg, sc.accent, 0.55), rgba(T.c2, T.dark ? 0.7 : 0.5), 2);
    }), 0, 0, W, H);
    /* the floor and its moving grid */
    ctx.fillStyle = vertical(ctx, hz, H, [[0, T.dark ? mix(sc.bg, sc.accent, 0.2) : mix(sc.bg, sc.accent2, 0.3)], [0.25, T.dark ? mix(sc.bg, '#000000', 0.55) : mix(sc.bg, '#FFFFFF', 0.25)], [1, T.dark ? mix(sc.bg, '#000000', 0.78) : mix(sc.bg, '#FFFFFF', 0.4)]]); ctx.fillRect(0, hz, W, H - hz);
    glow(ctx, T, cx, hz, U * 0.9, T.c1, 0.28);
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over'; ctx.lineCap = 'butt';
    const N = 22, f = frac(t * 0.42), lineCol = T.dark ? T.c1 : T.c1, span = Math.max(W, H) * 1.4;
    for (const [lw, la] of T.dark ? [[Math.max(3, U * 0.008), 0.16], [Math.max(1.2, U * 0.0026), 0.9]] : [[Math.max(1.2, U * 0.003), 0.55]]) {
      ctx.lineWidth = lw; ctx.strokeStyle = rgba(lineCol, la);
      for (let i = 0; i < N; i++) { const q = (i + f) / N, y = hz + (H - hz) * Math.pow(q, 2.3); ctx.globalAlpha = e * clamp(0.1 + 1.1 * q); ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(W, y); ctx.stroke(); }
      ctx.globalAlpha = e; const vg = ctx.createLinearGradient(0, hz, 0, H); vg.addColorStop(0, rgba(lineCol, 0)); vg.addColorStop(0.35, rgba(lineCol, la * 0.7)); vg.addColorStop(1, rgba(lineCol, la));
      ctx.strokeStyle = vg; ctx.beginPath(); const step = U * 0.24; for (let j = -Math.ceil(span / step); j <= Math.ceil(span / step); j++) { ctx.moveTo(cx + j * U * 0.012, hz); ctx.lineTo(cx + j * step, H); } ctx.stroke();
    }
    /* a bright streak of the sun on the floor, and dust lifting off the grid */
    for (let k = 0; k < 14; k++) { const q = (k + frac(t * 0.3)) / 14, y = hz + (H - hz) * Math.pow(q, 1.6) * 0.8, w2 = sunR * (1.0 - 0.55 * q) * (0.75 + 0.25 * Math.sin(t * 2 + k * 2.1)); ctx.fillStyle = rgba(sc.accent2, 0.5 * (1 - q)); ctx.fillRect(cx - w2, y, w2 * 2, Math.max(1.5, (H - hz) * 0.012 * (1 + q * 2))); }
    for (let i = 0; i < 26; i++) { const age = frac(t * (0.05 + 0.05 * r(seed, i, 61)) + r(seed, i, 62)), px = W * r(seed, i, 63), py = hz + (H - hz) * (0.1 + 0.8 * r(seed, i, 64)) - age * U * 0.3; ctx.fillStyle = rgba(T.hi, 0.7 * Math.sin(age * Math.PI)); ctx.beginPath(); ctx.arc(px, py, U * (0.002 + 0.003 * r(seed, i, 65)), 0, TAU); ctx.fill(); }
    ctx.restore();
  });

  /* ================= プリズム ================= */
  /* A slowly turning crystal (a triangular glass bar seen end-on) that scatters soft spectral light across the room. No beam in, no fan out. */
  const SPECTRUM = ['#FF4D6D', '#FF9E4D', '#FFE04D', '#5BE38B', '#4DC8FF', '#5B6BFF', '#B04DFF'];
  K.registerBg('echoPrism', 'プリズム', ['pop', 'emotional', 'calm'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.9), T = tones(sc), portrait = H > W, key = `pr|${seed}|${ck(sc)}`;
    const px = W * (portrait ? 0.5 : 0.5), py = H * 0.5, s = U * (portrait ? 0.24 : 0.27);
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.4, (x, w, h) => {
      x.fillStyle = vertical(x, 0, h, [[0, T.bg0], [1, T.bg1]]); x.fillRect(0, 0, w, h);
      glowDisc(x, px, py, U * 0.9, T.c1, T.dark ? 0.16 : 0.26);
      x.fillStyle = rgba(T.line, T.dark ? 0.14 : 0.18); const st = U * 0.06; for (let gx = st / 2; gx < w; gx += st) for (let gy = st / 2; gy < h; gy += st) { x.beginPath(); x.arc(gx, gy, U * 0.0017, 0, TAU); x.fill(); }
    }), 0, 0, W, H);
    const blend = T.dark ? 'screen' : 'source-over', rot = t * 0.12, R = Math.hypot(W, H);
    /* soft spectral washes thrown around the crystal: wide petals of colour, turning with it */
    ctx.save(); ctx.globalCompositeOperation = blend;
    SPECTRUM.forEach((c0, k) => {
      const c = mix(c0, k % 2 ? sc.accent2 : sc.accent, 0.3), a0 = rot * 1.3 + k * TAU / 7 + 0.4 * Math.sin(t * 0.2 + k), wd = 0.2 + 0.05 * Math.sin(t * 0.3 + k * 2);
      const g = ctx.createRadialGradient(px, py, s * 0.6, px, py, R * 0.62); g.addColorStop(0, rgba(c, T.dark ? 0.34 : 0.3)); g.addColorStop(0.6, rgba(c, T.dark ? 0.1 : 0.1)); g.addColorStop(1, rgba(c, 0));
      ctx.fillStyle = g; ctx.beginPath(); ctx.moveTo(px, py); ctx.arc(px, py, R * 0.62, a0 - wd, a0 + wd); ctx.closePath(); ctx.fill();
    });
    ctx.restore();
    /* the crystal: three faces, each a slightly different pane, edges split into red and cyan */
    const ang = rot * 0.7, verts = [0, 1, 2].map(i => [px + Math.cos(ang + i * TAU / 3 - Math.PI / 2) * s, py + Math.sin(ang + i * TAU / 3 - Math.PI / 2) * s]);
    ctx.save(); ctx.lineJoin = 'round';
    [0, 1, 2].forEach(i => { const a = verts[i], b = verts[(i + 1) % 3], mx = (a[0] + b[0]) / 2, my = (a[1] + b[1]) / 2, c = SPECTRUM[(i * 2 + 1) % 7];
      const g = ctx.createLinearGradient(px, py, mx, my); g.addColorStop(0, rgba(mix(c, sc.accent, 0.4), T.dark ? 0.06 : 0.18)); g.addColorStop(1, rgba(mix(c, sc.accent2, 0.4), T.dark ? 0.32 : 0.34));
      ctx.fillStyle = g; ctx.beginPath(); ctx.moveTo(px, py); ctx.lineTo(a[0], a[1]); ctx.lineTo(b[0], b[1]); ctx.closePath(); ctx.fill(); });
    ctx.lineWidth = Math.max(1.5, U * 0.004);
    for (const [dx, col] of [[-U * 0.003, '#FF4D6D'], [U * 0.003, '#4DC8FF']]) { ctx.save(); ctx.translate(dx, 0); ctx.globalCompositeOperation = blend; ctx.strokeStyle = rgba(col, 0.7); ctx.beginPath(); verts.forEach((v, i) => (i ? ctx.lineTo(v[0], v[1]) : ctx.moveTo(v[0], v[1]))); ctx.closePath(); ctx.stroke(); ctx.restore(); }
    ctx.strokeStyle = rgba(T.dark ? '#FFFFFF' : T.ink, T.dark ? 0.85 : 0.55); ctx.lineWidth = Math.max(1, U * 0.002); ctx.beginPath(); verts.forEach((v, i) => (i ? ctx.lineTo(v[0], v[1]) : ctx.moveTo(v[0], v[1]))); ctx.closePath(); ctx.stroke();
    ctx.strokeStyle = rgba(T.dark ? '#FFFFFF' : T.ink, 0.25); ctx.beginPath(); verts.forEach(v => { ctx.moveTo(px, py); ctx.lineTo(v[0], v[1]); }); ctx.stroke(); ctx.restore();
    glow(ctx, T, px, py, U * 0.16, T.hi, 0.55);
    /* drifting glass shards and glints */
    for (let i = 0; i < 12; i++) {
      const sz = U * (0.03 + 0.07 * r(seed, i, 71)), sx = wrap(r(seed, i, 72) * W + t * U * (0.004 + 0.01 * r(seed, i, 73)) * (i % 2 ? 1 : -1), W + sz * 4) - sz * 2, sy = H * (0.08 + 0.84 * r(seed, i, 74)) + Math.sin(t * 0.4 + i) * U * 0.02, ro = t * (0.06 + 0.1 * r(seed, i, 75)) * (i % 2 ? 1 : -1) + i, sides = 3 + (i % 2);
      ctx.save(); ctx.translate(sx, sy); ctx.rotate(ro); ctx.beginPath(); for (let k = 0; k < sides; k++) { const a = k / sides * TAU, rr = sz * (0.7 + 0.5 * r(seed, i, k, 76)); ctx.lineTo(Math.cos(a) * rr, Math.sin(a) * rr); } ctx.closePath();
      ctx.fillStyle = rgba(mix(SPECTRUM[i % 7], sc.accent, 0.4), T.dark ? 0.12 : 0.16); ctx.fill(); ctx.strokeStyle = rgba(T.dark ? '#FFFFFF' : T.ink, T.dark ? 0.5 : 0.35); ctx.lineWidth = Math.max(1, U * 0.0018); ctx.stroke(); ctx.restore();
    }
    ctx.save(); ctx.globalCompositeOperation = blend;
    for (let i = 0; i < 30; i++) { const age = frac(t * (0.05 + 0.05 * r(seed, i, 81)) + r(seed, i, 82)), a = r(seed, i, 83) * TAU, d = s * 0.8 + age * R * 0.4; ctx.fillStyle = rgba(mix(SPECTRUM[i % 7], '#FFFFFF', 0.3), 0.85 * Math.sin(age * Math.PI)); ctx.beginPath(); ctx.arc(px + Math.cos(a + rot) * d, py + Math.sin(a + rot) * d, U * (0.002 + 0.003 * r(seed, i, 84)), 0, TAU); ctx.fill(); }
    ctx.restore();
  });

  /* ================= リキッド ================= */
  K.registerBg('echoLiquid', 'リキッド', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W;
    ctx.globalAlpha = e; ctx.fillStyle = vertical(ctx, 0, H, [[0, T.bg0], [1, T.bg1]]); ctx.fillRect(0, 0, W, H);
    const cols = [T.c1, T.c2, T.c3, mix(sc.accent, sc.accent2, 0.5), sc.ghostB ? mix(sc.ghostB, sc.accent, 0.4) : T.c1, mix(T.c2, T.hi, 0.4)];
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    for (let i = 0; i < 7; i++) {
      const sp = 0.09 + 0.05 * r(seed, i, 1), ax = W * (0.22 + 0.18 * r(seed, i, 2)), ay = H * (0.2 + 0.16 * r(seed, i, 3)), bx = W * (0.3 + 0.4 * r(seed, i, 4)), by = H * (0.3 + 0.4 * r(seed, i, 5));
      const x = bx + Math.cos(t * sp * 2 + i * 1.7) * ax * 0.9, y = by + Math.sin(t * sp * 2.6 + i * 2.3) * ay * 0.9, rad = U * (0.42 + 0.3 * r(seed, i, 6)) * (1 + 0.08 * Math.sin(t * 0.7 + i)), c = cols[i % cols.length];
      const g = ctx.createRadialGradient(x, y, 0, x, y, rad); g.addColorStop(0, rgba(c, T.dark ? 0.62 : 0.7)); g.addColorStop(0.55, rgba(c, T.dark ? 0.26 : 0.32)); g.addColorStop(1, rgba(c, 0)); ctx.fillStyle = g; ctx.fillRect(x - rad, y - rad, rad * 2, rad * 2);
    }
    ctx.restore();
    /* a glassy sheen, thin rings and a slowly turning tick circle over the colour */
    const cx = W * (portrait ? 0.5 : 0.62), cy = H * 0.5, R0 = U * (portrait ? 0.4 : 0.46);
    const sh = ctx.createLinearGradient(0, 0, W, H); sh.addColorStop(0, rgba('#FFFFFF', T.dark ? 0.1 : 0.32)); sh.addColorStop(0.45, rgba('#FFFFFF', 0)); sh.addColorStop(1, rgba('#FFFFFF', T.dark ? 0.05 : 0.18)); ctx.fillStyle = sh; ctx.fillRect(0, 0, W, H);
    ctx.strokeStyle = rgba(T.dark ? '#FFFFFF' : T.ink, T.dark ? 0.2 : 0.18); ctx.lineWidth = Math.max(1, U * 0.0018);
    [1, 1.38, 1.85].forEach((k, i) => { ctx.beginPath(); ctx.arc(cx + Math.sin(t * 0.2 + i) * U * 0.02, cy + Math.cos(t * 0.17 + i) * U * 0.02, R0 * k, 0, TAU); ctx.stroke(); });
    ctx.save(); ctx.translate(cx, cy); ctx.rotate(t * 0.05); ctx.strokeStyle = rgba(T.dark ? '#FFFFFF' : T.ink, T.dark ? 0.45 : 0.4); ctx.lineWidth = Math.max(1, U * 0.002); ctx.beginPath();
    for (let k = 0; k < 96; k++) { const a = k / 96 * TAU, l = k % 8 === 0 ? U * 0.024 : U * 0.01; ctx.moveTo(Math.cos(a) * (R0 * 1.18), Math.sin(a) * (R0 * 1.18)); ctx.lineTo(Math.cos(a) * (R0 * 1.18 + l), Math.sin(a) * (R0 * 1.18 + l)); } ctx.stroke();
    ctx.setLineDash([U * 0.03, U * 0.05]); ctx.lineWidth = Math.max(1.5, U * 0.004); ctx.strokeStyle = rgba(T.hi, T.dark ? 0.7 : 0.6); ctx.beginPath(); ctx.arc(0, 0, R0 * 1.28, 0, TAU * 0.62); ctx.stroke(); ctx.setLineDash([]); ctx.restore();
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    for (let i = 0; i < 26; i++) { const age = frac(t * (0.03 + 0.04 * r(seed, i, 51)) + r(seed, i, 52)), x = W * r(seed, i, 53) + Math.sin(t * 0.5 + i) * U * 0.03, y = H * (1.05 - age * 1.1); ctx.fillStyle = rgba(T.hi, 0.75 * Math.sin(age * Math.PI)); ctx.beginPath(); ctx.arc(x, y, U * (0.0022 + 0.004 * r(seed, i, 54)), 0, TAU); ctx.fill(); }
    ctx.restore();
  });

  /* ================= 稜線ライン ================= */
  K.registerBg('echoRidgeLines', '稜線ライン', ['calm', 'emotional', 'glitch'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.9), T = tones(sc), portrait = H > W, key = `rl|${seed}|${ck(sc)}`;
    const N = portrait ? 34 : 32, speed = 0.32, s = t * speed, base = Math.floor(s), f = s - base, y0 = H * (portrait ? 0.26 : 0.24), y1 = H * 0.98, pts = portrait ? 70 : 110;
    const bgc = T.dark ? mix(sc.bg, '#000000', 0.5) : mix(sc.bg, '#FFFFFF', 0.5);
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.4, (x, w, h) => {
      x.fillStyle = vertical(x, 0, h, [[0, T.dark ? mix(sc.bg, '#000000', 0.7) : mix(sc.bg, sc.accent, 0.22)], [0.6, bgc], [1, T.bg1]]); x.fillRect(0, 0, w, h);
      const mx = w * 0.5, my = H * (portrait ? 0.15 : 0.135); glowDisc(x, mx, my, U * 0.55, T.c2, T.dark ? 0.32 : 0.4);
      x.strokeStyle = rgba(T.c2, T.dark ? 0.8 : 0.7); x.lineWidth = Math.max(1.5, U * 0.004); x.beginPath(); x.arc(mx, my, U * 0.13, 0, TAU); x.stroke(); x.fillStyle = rgba(T.c2, T.dark ? 0.22 : 0.3); x.fill();
      if (T.dark) for (let i = 0; i < 70; i++) { x.fillStyle = rgba('#FFFFFF', 0.2 + 0.5 * r(seed, i, 3)); x.beginPath(); x.arc(r(seed, i, 1) * w, r(seed, i, 2) * y0 * 0.9, U * 0.0015, 0, TAU); x.fill(); }
    }), 0, 0, W, H);
    for (let i = 0; i < N; i++) {
      const q = (i + f) / N, y = y0 + (y1 - y0) * Math.pow(q, 1.75), amp = U * (0.012 + 0.16 * Math.pow(q, 1.3)), wid = i - base;
      const c = T.dark ? mix(mix(sc.accent2, sc.accent, q), '#FFFFFF', 0.12 + 0.3 * q) : mix(mix(sc.accent2, sc.accent, q), T.ink, 0.25 + 0.35 * q);
      ctx.beginPath(); ctx.moveTo(-2, y + U * 0.02);
      const pt = [];
      for (let k = 0; k <= pts; k++) {
        const u = k / pts, env2 = Math.exp(-Math.pow((u - 0.5) / (portrait ? 0.32 : 0.26), 2)), n = 0.5 * Math.sin(u * 9 + r(seed, wid, 1) * 6) + 0.3 * Math.sin(u * 19 + r(seed, wid, 2) * 6 + t * 0.4) + 0.2 * Math.sin(u * 37 + r(seed, wid, 3) * 6), pk = Math.abs(n) * 0.9 + 0.1 * r(seed, wid, k, 4);
        const yy = y - amp * (env2 * pk * 1.2 + 0.08 * Math.abs(Math.sin(u * 5 + wid)));
        pt.push([W * u, yy]); ctx.lineTo(W * u, yy);
      }
      ctx.lineTo(W + 2, y + U * 0.02); ctx.closePath(); ctx.fillStyle = bgc; ctx.fill();
      ctx.strokeStyle = rgba(c, clamp(0.25 + 0.95 * q)); ctx.lineWidth = Math.max(1, U * (0.0012 + 0.0032 * q)); ctx.beginPath(); pt.forEach(([px, py], k) => (k ? ctx.lineTo(px, py) : ctx.moveTo(px, py))); ctx.stroke();
      if (T.dark && q > 0.72) { ctx.save(); ctx.globalCompositeOperation = 'screen'; ctx.strokeStyle = rgba(c, 0.18); ctx.lineWidth = Math.max(3, U * 0.009 * q); ctx.beginPath(); pt.forEach(([px, py], k) => (k ? ctx.lineTo(px, py) : ctx.moveTo(px, py))); ctx.stroke(); ctx.restore(); }
    }
  });

  /* ================= ワープ ================= */
  K.registerBg('echoTunnel', 'ワープ', ['pop', 'glitch', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.7), T = tones(sc), key = `tn|${seed}|${ck(sc)}`;
    const vx = W * 0.5 + Math.sin(t * 0.3) * U * 0.06, vy = H * 0.5 + Math.cos(t * 0.24) * U * 0.04;
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.35, (x, w, h) => { const g = x.createRadialGradient(w / 2, h / 2, 0, w / 2, h / 2, Math.hypot(w, h) * 0.55); g.addColorStop(0, T.dark ? mix(sc.bg, sc.accent, 0.22) : mix(sc.bg, '#FFFFFF', 0.7)); g.addColorStop(0.5, T.bg1); g.addColorStop(1, T.bg0); x.fillStyle = g; x.fillRect(0, 0, w, h); }), 0, 0, W, H);
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over'; ctx.lineJoin = 'round';
    const N = 20, s = t * 0.55, sides = 6;
    for (let i = 0; i < N; i++) {
      const z = frac((i + s) / N), rad = U * 0.03 * Math.pow(64, z), rot = z * 1.7 + t * 0.12, ox = vx + Math.sin(t * 0.5 + z * 2.4) * U * 0.1 * z, oy = vy + Math.cos(t * 0.42 + z * 2.1) * U * 0.07 * z, c = mix(T.c1, T.c2, z);
      const a = Math.pow(Math.sin(z * Math.PI), 0.9) * (T.dark ? 0.95 : 0.7);
      for (const [lw, la] of T.dark ? [[U * (0.01 + 0.03 * z), 0.16], [U * (0.002 + 0.008 * z), 1]] : [[U * (0.002 + 0.006 * z), 1]]) { ctx.strokeStyle = rgba(c, a * la); ctx.lineWidth = lw; ctx.beginPath(); for (let k = 0; k <= sides; k++) { const an = rot + k / sides * TAU; if (k) ctx.lineTo(ox + Math.cos(an) * rad, oy + Math.sin(an) * rad); else ctx.moveTo(ox + Math.cos(an) * rad, oy + Math.sin(an) * rad); } ctx.stroke(); }
    }
    ctx.lineCap = 'round';
    for (let pass = 0; pass < 2; pass++) {
      ctx.strokeStyle = rgba(pass ? T.c2 : T.c1, T.dark ? 0.85 : 0.6); ctx.lineWidth = Math.max(1.2, U * (pass ? 0.0026 : 0.0018)); ctx.beginPath();
      for (let j = 0; j < 46; j++) { const a = r(seed, j, 1) * TAU + pass, sp = 0.35 + 0.6 * r(seed, j, 2), p = frac(t * sp * 0.6 + r(seed, j, 3)), d0 = U * 0.06 + p * p * U * 1.5, len = U * (0.02 + 0.3 * p * p); ctx.moveTo(vx + Math.cos(a) * d0, vy + Math.sin(a) * d0); ctx.lineTo(vx + Math.cos(a) * (d0 + len), vy + Math.sin(a) * (d0 + len)); }
      ctx.stroke();
    }
    ctx.restore();
    glow(ctx, T, vx, vy, U * 0.36, T.hi, 0.6); glow(ctx, T, vx, vy, U * 0.1, '#FFFFFF', 0.7);
  });

  /* ================= コックピット ================= */
  K.registerBg('echoCockpit', 'コックピット', ['glitch', 'pop', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), T = tones(sc), portrait = H > W, key = `ck|${seed}|${ck(sc)}`;
    const cx = W * (portrait ? 0.5 : 0.66), cy = H * (portrait ? 0.46 : 0.5), R = Math.min(U * 0.38, portrait ? W * 0.42 : W * 0.3);
    const ink = T.dark ? T.line : T.ink, hi = T.c1, hi2 = T.c2, mono = `${Math.max(9, Math.round(U * 0.017))}px ui-monospace, Menlo, Consolas, monospace`;
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.5, (x, w, h) => {
      x.fillStyle = vertical(x, 0, h, [[0, T.bg0], [1, T.bg1]]); x.fillRect(0, 0, w, h); glowDisc(x, cx, cy, R * 1.9, hi, T.dark ? 0.16 : 0.22);
      const st = U * 0.045; x.fillStyle = rgba(ink, T.dark ? 0.16 : 0.22); for (let gx = st / 2; gx < w; gx += st) for (let gy = st / 2; gy < h; gy += st) { x.beginPath(); x.arc(gx, gy, U * 0.0016, 0, TAU); x.fill(); }
      x.strokeStyle = rgba(ink, 0.5); x.lineWidth = Math.max(1, U * 0.0022); const m = U * 0.04, l = U * 0.05;
      for (const [ax, ay, sx, sy] of [[m, m, 1, 1], [w - m, m, -1, 1], [m, h - m, 1, -1], [w - m, h - m, -1, -1]]) { x.beginPath(); x.moveTo(ax + sx * l, ay); x.lineTo(ax, ay); x.lineTo(ax, ay + sy * l); x.stroke(); }
      x.strokeStyle = rgba(ink, 0.16); x.beginPath(); x.moveTo(0, cy); x.lineTo(w, cy); x.moveTo(cx, 0); x.lineTo(cx, h); x.stroke();
      [0.32, 0.56, 0.78, 1].forEach((k, i) => { x.strokeStyle = rgba(i === 3 ? hi : ink, i === 3 ? 0.65 : 0.34); x.lineWidth = Math.max(1, U * (i === 3 ? 0.003 : 0.0018)); x.beginPath(); x.arc(cx, cy, R * k, 0, TAU); x.stroke(); });
    }), 0, 0, W, H);
    ctx.save(); ctx.translate(cx, cy);
    ctx.save(); ctx.rotate(t * 0.06); ctx.strokeStyle = rgba(ink, 0.7); ctx.lineWidth = Math.max(1, U * 0.0018); ctx.beginPath(); for (let k = 0; k < 120; k++) { const a = k / 120 * TAU, l = k % 10 === 0 ? U * 0.026 : k % 5 === 0 ? U * 0.016 : U * 0.008; ctx.moveTo(Math.cos(a) * R * 1.06, Math.sin(a) * R * 1.06); ctx.lineTo(Math.cos(a) * (R * 1.06 + l), Math.sin(a) * (R * 1.06 + l)); } ctx.stroke(); ctx.restore();
    ctx.save(); ctx.rotate(-t * 0.13); ctx.setLineDash([U * 0.05, U * 0.03]); ctx.strokeStyle = rgba(hi2, 0.85); ctx.lineWidth = Math.max(2, U * 0.005); ctx.beginPath(); ctx.arc(0, 0, R * 1.16, 0, TAU * 0.7); ctx.stroke(); ctx.setLineDash([]); ctx.restore();
    /* the sweep */
    const sw = t * 0.9; ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    for (let k = 0; k < 26; k++) { ctx.fillStyle = rgba(hi, (T.dark ? 0.3 : 0.32) * (1 - k / 26)); ctx.beginPath(); ctx.moveTo(0, 0); ctx.arc(0, 0, R, sw - k * 0.035, sw - k * 0.035 + 0.04); ctx.closePath(); ctx.fill(); }
    ctx.strokeStyle = rgba(hi, 0.95); ctx.lineWidth = Math.max(1.5, U * 0.003); ctx.beginPath(); ctx.moveTo(0, 0); ctx.lineTo(Math.cos(sw) * R, Math.sin(sw) * R); ctx.stroke();
    /* blips that light up as the beam passes */
    for (let i = 0; i < 9; i++) { const a = r(seed, i, 1) * TAU, d = R * (0.25 + 0.7 * r(seed, i, 2)), since = ((sw - a) % TAU + TAU) % TAU, b = Math.exp(-since * 1.2); ctx.fillStyle = rgba(i % 3 ? hi2 : hi, 0.15 + 0.85 * b); ctx.beginPath(); ctx.arc(Math.cos(a) * d, Math.sin(a) * d, U * (0.005 + 0.006 * b), 0, TAU); ctx.fill(); if (b > 0.3) { ctx.strokeStyle = rgba(hi2, b * 0.6); ctx.lineWidth = Math.max(1, U * 0.0018); ctx.beginPath(); ctx.arc(Math.cos(a) * d, Math.sin(a) * d, U * (0.014 + 0.02 * (1 - b)), 0, TAU); ctx.stroke(); } }
    ctx.restore();
    [0.56, 0.78].forEach((k, i) => { const a = t * (0.3 - i * 0.5) + i * 2; ctx.fillStyle = i ? hi2 : hi; ctx.beginPath(); ctx.arc(Math.cos(a) * R * k, Math.sin(a) * R * k, U * 0.009, 0, TAU); ctx.fill(); ctx.strokeStyle = rgba(i ? hi2 : hi, 0.5); ctx.lineWidth = Math.max(1, U * 0.002); ctx.beginPath(); ctx.arc(0, 0, R * k, a - 0.7, a); ctx.stroke(); });
    ctx.fillStyle = hi; ctx.beginPath(); ctx.arc(0, 0, U * 0.014, 0, TAU); ctx.fill(); ctx.strokeStyle = rgba(hi, 0.6); ctx.beginPath(); ctx.arc(0, 0, U * 0.03 + Math.sin(t * 2) * U * 0.004, 0, TAU); ctx.stroke();
    ctx.restore();
    /* readouts: a scrolling hex ticker, live numbers, a spectrum of bars and a trace */
    ctx.font = mono; ctx.textBaseline = 'top'; ctx.fillStyle = rgba(ink, 0.7);
    const tx = U * 0.075, lh = U * 0.026, rows = portrait ? 9 : 14, off = frac(t * 0.5) * lh;
    for (let i = 0; i < rows; i++) { const id = i + Math.floor(t * 0.5), hex = (J.h(seed, id, 9) & 0xFFFFFF).toString(16).toUpperCase().padStart(6, '0'); ctx.globalAlpha = e * (0.2 + 0.7 * Math.sin((i + 0.5 - frac(t * 0.5)) / rows * Math.PI)); ctx.fillText(`${hex}  ${(J.h(seed, id, 10) % 900 / 10 + 10).toFixed(1)}`, tx, H * 0.16 + i * lh - off); }
    ctx.globalAlpha = e; ctx.fillStyle = hi; ctx.fillText('ECHO // SCAN', tx, U * 0.075); ctx.fillStyle = rgba(ink, 0.85);
    ctx.textAlign = 'right'; ctx.fillText(`AZ ${(((sw / TAU) % 1) * 360).toFixed(1).padStart(5, '0')}°`, W - U * 0.075, U * 0.075); ctx.fillText(`SIG ${(0.62 + 0.3 * Math.sin(t * 1.3)).toFixed(2)}`, W - U * 0.075, U * 0.075 + lh); ctx.textAlign = 'left';
    const bx0 = U * 0.075, by0 = H - U * 0.09, nb = portrait ? 18 : 26, bw = U * 0.011;
    for (let i = 0; i < nb; i++) { const hgt = U * (0.012 + 0.09 * Math.pow(0.5 + 0.5 * Math.sin(t * (2 + r(seed, i, 1) * 3) + i * 0.7) * Math.sin(t * 0.7 + i * 0.3), 1.5)); ctx.fillStyle = rgba(i % 5 === 0 ? hi2 : hi, 0.85); ctx.fillRect(bx0 + i * bw * 1.5, by0 - hgt, bw, hgt); }
    ctx.strokeStyle = rgba(hi2, 0.9); ctx.lineWidth = Math.max(1.4, U * 0.0028); ctx.beginPath(); const wx0 = W - U * 0.075 - U * 0.46, wy = H - U * 0.13; for (let k = 0; k <= 80; k++) { const u = k / 80, y = wy + Math.sin(u * 14 + t * 3) * U * 0.02 * Math.sin(u * 3 + t * 0.6) + Math.sin(u * 31 + t * 5) * U * 0.006; if (k) ctx.lineTo(wx0 + u * U * 0.46, y); else ctx.moveTo(wx0, y); } ctx.stroke();
  });

  /* ================= ライブステージ ================= */
  K.registerBg('echoLiveStage', 'ライブステージ', ['pop', 'emotional', 'glitch'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), T = tones(sc), portrait = H > W, key = `ls|${seed}|${ck(sc)}`;
    const trussY = H * 0.075, floorY = H * 0.86, lamps = portrait ? 4 : 6, blend = T.dark ? 'screen' : 'source-over', beamCols = [T.c1, T.c2, T.hi, T.c3];
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.45, (x, w, h) => {
      x.fillStyle = vertical(x, 0, h, [[0, T.dark ? mix(sc.bg, '#000000', 0.78) : mix(sc.bg, sc.accent, 0.3)], [0.6, T.dark ? mix(sc.bg, sc.accent, 0.16) : mix(sc.bg, '#FFFFFF', 0.4)], [1, T.dark ? mix(sc.bg, '#000000', 0.7) : sc.bg]]); x.fillRect(0, 0, w, h);
      const ring = (rad, col, a) => { x.strokeStyle = rgba(col, a); x.lineWidth = Math.max(3, U * 0.012); x.beginPath(); x.arc(w * 0.5, h * 0.5, rad, 0, TAU); x.stroke(); }; ring(U * 0.3, T.c1, T.dark ? 0.5 : 0.35); ring(U * 0.215, T.c2, T.dark ? 0.4 : 0.3);
      glowDisc(x, w * 0.5, h * 0.5, U * 0.55, T.c1, T.dark ? 0.16 : 0.2);
      x.fillStyle = T.dark ? '#000000' : mix(sc.fg, '#000000', 0.6); x.globalAlpha = T.dark ? 0.92 : 0.8; x.fillRect(0, trussY - U * 0.012, w, U * 0.024); x.fillRect(0, trussY + U * 0.03, w, U * 0.01);
      x.strokeStyle = x.fillStyle; x.lineWidth = Math.max(1.5, U * 0.004); x.beginPath(); for (let i = 0; i < w / (U * 0.05); i++) { x.moveTo(i * U * 0.05, trussY + U * 0.03); x.lineTo(i * U * 0.05 + U * 0.025, trussY - U * 0.012); x.lineTo(i * U * 0.05 + U * 0.05, trussY + U * 0.03); } x.stroke(); x.globalAlpha = 1;
    }), 0, 0, W, H);
    ctx.save(); ctx.globalCompositeOperation = blend;
    for (let i = 0; i < lamps; i++) {
      const lx = W * (i + 0.5) / lamps, sp = 0.35 + 0.25 * r(seed, i, 1), ang = Math.PI / 2 + Math.sin(t * sp + i * 1.9) * (0.45 + 0.12 * r(seed, i, 2)) * (i % 2 ? 1 : -1), half = 0.045 + 0.025 * r(seed, i, 3), L = H * 1.25, c = beamCols[i % beamCols.length];
      const ex1 = lx + Math.cos(ang - half) * L, ey1 = trussY + Math.sin(ang - half) * L, ex2 = lx + Math.cos(ang + half) * L, ey2 = trussY + Math.sin(ang + half) * L;
      const g = ctx.createLinearGradient(lx, trussY, lx + Math.cos(ang) * L, trussY + Math.sin(ang) * L); g.addColorStop(0, rgba(c, T.dark ? 0.6 : 0.32)); g.addColorStop(0.6, rgba(c, T.dark ? 0.16 : 0.1)); g.addColorStop(1, rgba(c, 0));
      ctx.fillStyle = g; ctx.beginPath(); ctx.moveTo(lx, trussY); ctx.lineTo(ex1, ey1); ctx.lineTo(ex2, ey2); ctx.fill();
      const hitX = lx + Math.cos(ang) * (floorY - trussY) / Math.max(0.2, Math.sin(ang)), hitY = floorY; ctx.save(); ctx.translate(hitX, hitY); ctx.scale(1, 0.22); glowDisc(ctx, 0, 0, U * 0.24, c, T.dark ? 0.6 : 0.35); ctx.restore();
      glowDisc(ctx, lx, trussY + U * 0.02, U * 0.05, c, T.dark ? 0.9 : 0.5);
    }
    for (let i = 0; i < 4; i++) { const x = W * (0.15 + 0.7 * frac(r(seed, i, 41) + t * 0.008 * (i % 2 ? 1 : -1))), y = H * (0.3 + 0.35 * r(seed, i, 42)); glowDisc(ctx, x, y, U * 0.5, beamCols[i % 4], T.dark ? 0.08 : 0.05); }
    for (let i = 0; i < 30; i++) { const age = frac(t * (0.05 + 0.05 * r(seed, i, 51)) + r(seed, i, 52)), x = W * r(seed, i, 53), y = H * (0.9 - age * 0.8); ctx.fillStyle = rgba(T.hi, 0.6 * Math.sin(age * Math.PI)); ctx.beginPath(); ctx.arc(x, y, U * (0.002 + 0.003 * r(seed, i, 54)), 0, TAU); ctx.fill(); }
    ctx.restore();
    /* the crowd: two rows of heads and shoulders, some with raised arms and light sticks */
    const crowd = T.dark ? '#000000' : mix(sc.fg, '#000000', 0.55);
    ctx.drawImage(layer(env, `${key}|crowd`, 0.5, (x, w, h) => {
      for (const [row, base, size, col, a] of [[0, floorY + U * 0.02, 0.026, mix(crowd, T.dark ? sc.accent : '#FFFFFF', T.dark ? 0.12 : 0.2), 0.95], [1, h + U * 0.02, 0.046, crowd, 1]]) {
        x.fillStyle = col; x.globalAlpha = a; const n = Math.round(w / (U * size * 1.7));
        for (let i = 0; i < n; i++) { const cxp = w * (i + 0.5 + (r(seed, row, i, 1) - 0.5) * 0.6) / n, hh = U * size * (0.9 + 0.5 * r(seed, row, i, 2)), cyp = base - hh * (row ? 2.4 : 2.2); x.beginPath(); x.arc(cxp, cyp, U * size * 0.5, 0, TAU); x.fill(); x.beginPath(); x.moveTo(cxp - U * size * 1.1, base + 4); x.quadraticCurveTo(cxp - U * size * 1.0, cyp + U * size * 0.6, cxp, cyp + U * size * 0.5); x.quadraticCurveTo(cxp + U * size * 1.0, cyp + U * size * 0.6, cxp + U * size * 1.1, base + 4); x.fill(); }
      }
      x.globalAlpha = 1;
    }), 0, 0, W, H);
    ctx.save(); ctx.strokeStyle = crowd; ctx.lineCap = 'round';
    for (let i = 0; i < (portrait ? 9 : 16); i++) {
      const bx = W * (i + 0.5 + (r(seed, i, 61) - 0.5) * 0.4) / (portrait ? 9 : 16), by = H + U * 0.01, len = U * (0.14 + 0.06 * r(seed, i, 62)), sw = Math.sin(t * (1.6 + r(seed, i, 63)) + i * 1.3) * 0.32;
      for (const side of [-1, 1]) { if ((i + (side > 0 ? 1 : 0)) % 3 === 0) continue; const a = -Math.PI / 2 + side * (0.2 + 0.12 * r(seed, i, 64)) + sw * (side > 0 ? 1 : -1), hx = bx + side * U * 0.03 + Math.cos(a) * len, hy = by + Math.sin(a) * len; ctx.lineWidth = U * 0.014; ctx.beginPath(); ctx.moveTo(bx + side * U * 0.03, by); ctx.lineTo(hx, hy); ctx.stroke(); ctx.fillStyle = crowd; ctx.beginPath(); ctx.arc(hx, hy, U * 0.011, 0, TAU); ctx.fill();
        if ((i + side) % 2 === 0) { const c = beamCols[(i + 1) % 4]; ctx.save(); ctx.translate(hx, hy); ctx.rotate(a + Math.PI / 2 - side * 0.1); ctx.globalCompositeOperation = blend; ctx.fillStyle = rgba(c, 0.9); ctx.fillRect(-U * 0.004, -U * 0.055, U * 0.008, U * 0.05); glowDisc(ctx, 0, -U * 0.03, U * 0.05, c, 0.5); ctx.restore(); } }
    }
    ctx.restore();
  });
})();
