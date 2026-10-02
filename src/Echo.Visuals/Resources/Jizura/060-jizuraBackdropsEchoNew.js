/* ECHO first-party backdrops, "new scenes": a meteor hill, power lines at dusk, paper cranes, hot-air balloons, a night crossing,
   a snow forest, a siphon, water drops, a flip clock, a kaleidoscope and a spirograph. Original art only;
   every colour is drawn from the current scheme (dark = additive light, light = inked washes). */
(() => {
  'use strict';
  const K = J.echo, TAU = K.TAU, clamp = K.clamp, mix = K.mix, rgba = K.rgba, r = J.r;
  const { U: Umin, isDark, wrap, fade, vertical, glowDisc, layer } = K;
  const FONT = '"Helvetica Neue","Arial","Yu Gothic","Noto Sans JP",sans-serif';
  const ck = sc => [sc.bg, sc.fg, sc.accent, sc.accent2, sc.ghostA, sc.ghostB].join('');
  const frac = v => v - Math.floor(v);
  const ease = v => { v = clamp(v); return v * v * (3 - 2 * v); };
  const tones = sc => {
    const dark = isDark(sc.bg);
    return dark
      ? { dark, bg0: mix(sc.bg, '#000000', 0.6), bg1: mix(sc.bg, '#000000', 0.3), c1: mix(sc.accent, '#FFFFFF', 0.12), c2: mix(sc.accent2, '#FFFFFF', 0.1), c3: mix(sc.ghostA || sc.accent, '#FFFFFF', 0.25), line: mix(sc.fg, sc.bg, 0.55), hi: mix(sc.accent, '#FFFFFF', 0.6), ink: '#FFFFFF' }
      : { dark, bg0: mix(sc.bg, '#FFFFFF', 0.55), bg1: sc.bg, c1: mix(sc.accent, '#000000', 0.06), c2: mix(sc.accent2, '#000000', 0.04), c3: sc.ghostA || sc.accent, line: mix(sc.fg, sc.bg, 0.25), hi: mix(sc.accent, '#FFFFFF', 0.4), ink: sc.ink || sc.fg };
  };
  const glow = (ctx, T, x, y, rad, c, a) => { ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over'; glowDisc(ctx, x, y, rad, c, T.dark ? a : a * 0.7); ctx.restore(); };
  const dot = (ctx, x, y, rad) => { ctx.beginPath(); ctx.arc(x, y, rad, 0, TAU); ctx.fill(); };
  /* a rolling ridge line as a filled silhouette */
  const ridge = (x, W, H, base, amp, seed, k, fill) => {
    x.fillStyle = fill; x.beginPath(); x.moveTo(0, H);
    for (let px = 0; px <= W + 4; px += Math.max(4, W / 90)) { const v = px / W; x.lineTo(px, base - amp * (0.5 * Math.sin(v * 5 + seed * 3 + k) + 0.3 * Math.sin(v * 11 + k * 2.1) + 0.2 * Math.sin(v * 23 + seed))); }
    x.lineTo(W, H); x.closePath(); x.fill();
  };

  /* ================= 流星の丘 ================= */
  K.registerBg('echoMeteorHill', '流星の丘', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `mh|${seed}|${ck(sc)}`;
    const sil = T.dark ? mix(sc.bg, '#000000', 0.88) : mix(sc.bg, '#101428', 0.72), hy = H * (portrait ? 0.74 : 0.78);
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.6, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [0.55, T.bg1], [0.85, mix(T.bg1, T.c1, T.dark ? 0.4 : 0.35)], [1, mix(T.bg1, T.c2, T.dark ? 0.3 : 0.3)]]); x.fillRect(0, 0, W, H);
      /* the band of the galaxy, slanting across */
      x.save(); x.translate(W * 0.5, H * 0.3); x.rotate(-0.5);
      for (let i = 0; i < 6; i++) { x.save(); x.scale(1, 0.14 + 0.05 * i); glowDisc(x, 0, 0, W * (0.5 - 0.04 * i), i % 2 ? T.c3 : T.c1, T.dark ? 0.1 + 0.025 * i : 0.12); x.restore(); }
      x.restore();
      for (let i = 0; i < 160; i++) { const sx = r(seed, i, 1) * W, sy = r(seed, i, 2) * hy * 0.95; x.fillStyle = rgba(i % 7 ? '#FFFFFF' : T.hi, (T.dark ? 0.25 : 0.5) + 0.5 * r(seed, i, 3)); dot(x, sx, sy, U * (0.0012 + 0.0025 * r(seed, i, 4) * r(seed, i, 4))); }
      ridge(x, W, H, hy - U * 0.07, U * 0.12, seed, 1, mix(sil, T.bg1, 0.55)); ridge(x, W, H, hy, U * 0.1, seed, 4, mix(sil, T.bg1, 0.25));
      /* the near hill with one tree, a bench and a lit tent */
      const near = hy + U * 0.07; ridge(x, W, H, near, U * 0.06, seed, 7, sil);
      const tx = W * (portrait ? 0.3 : 0.24), ty = near - U * 0.018;
      x.fillStyle = sil; x.fillRect(tx - U * 0.006, ty - U * 0.2, U * 0.012, U * 0.2);
      for (let i = 0; i < 9; i++) { const a = r(seed, i, 11) * TAU, d = U * 0.07 * Math.sqrt(r(seed, i, 12)); x.beginPath(); x.arc(tx + Math.cos(a) * d * 1.3, ty - U * 0.24 + Math.sin(a) * d, U * (0.04 + 0.03 * r(seed, i, 13)), 0, TAU); x.fill(); }
      const ex = W * (portrait ? 0.7 : 0.7), ey = near + U * 0.02;
      x.beginPath(); x.moveTo(ex - U * 0.06, ey); x.lineTo(ex, ey - U * 0.075); x.lineTo(ex + U * 0.06, ey); x.closePath(); x.fill();
      x.fillStyle = mix('#FFD98A', T.hi, 0.2); x.beginPath(); x.moveTo(ex - U * 0.018, ey); x.lineTo(ex, ey - U * 0.038); x.lineTo(ex + U * 0.018, ey); x.closePath(); x.fill();
      glowDisc(x, ex, ey - U * 0.015, U * 0.16, '#FFD98A', T.dark ? 0.45 : 0.3);
      x.fillStyle = sil; x.fillRect(W * 0.46, near + U * 0.005, U * 0.06, U * 0.006); x.fillRect(W * 0.46 + U * 0.004, near + U * 0.011, U * 0.004, U * 0.014); x.fillRect(W * 0.46 + U * 0.052, near + U * 0.011, U * 0.004, U * 0.014);
    }), 0, 0, W, H);
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    /* meteors: a few per cycle, each a quick bright head with a long fading tail */
    for (let k = 0; k < 4; k++) {
      const per = 4.2 + k * 1.3, n = Math.floor((t + k * 1.7) / per), ph = frac((t + k * 1.7) / per) * per / 1.1;
      if (ph >= 1) continue;
      const sx = W * (0.15 + 0.75 * r(seed, k, n, 21)), sy = hy * (0.04 + 0.35 * r(seed, k, n, 22)), len = U * (0.5 + 0.5 * r(seed, k, n, 23)), a = 0.55 + 0.25 * r(seed, k, n, 24);
      const hx = sx - Math.cos(a) * len * ph * 1.6, hyy = sy + Math.sin(a) * len * ph * 1.6, tl = len * (0.35 + 0.65 * Math.sin(ph * Math.PI)), tx = hx + Math.cos(a) * tl, ty = hyy - Math.sin(a) * tl;
      const g = ctx.createLinearGradient(hx, hyy, tx, ty); g.addColorStop(0, rgba('#FFFFFF', 0.95)); g.addColorStop(0.2, rgba(T.hi, 0.6)); g.addColorStop(1, rgba(T.c1, 0));
      ctx.strokeStyle = g; ctx.lineWidth = Math.max(1.5, U * 0.004); ctx.lineCap = 'round'; ctx.beginPath(); ctx.moveTo(hx, hyy); ctx.lineTo(tx, ty); ctx.stroke();
      glowDisc(ctx, hx, hyy, U * 0.05, '#FFFFFF', 0.7 * Math.sin(ph * Math.PI));
    }
    for (let i = 0; i < 14; i++) { const s = 0.5 + 0.5 * Math.sin(t * (0.8 + r(seed, i, 31)) + i * 3); ctx.fillStyle = rgba('#FFFFFF', 0.7 * s * s); dot(ctx, r(seed, i, 32) * W, r(seed, i, 33) * hy * 0.7, U * 0.003); }
    /* fireflies low in the grass */
    for (let i = 0; i < 16; i++) { const fx = W * r(seed, i, 41) + Math.sin(t * 0.4 + i * 2) * U * 0.04, fy = hy + U * (0.02 + 0.2 * r(seed, i, 42)) + Math.sin(t * 0.55 + i) * U * 0.02, s = Math.max(0, Math.sin(t * 1.1 + i * 5)); if (s <= 0) continue; glowDisc(ctx, fx, fy, U * 0.025, '#E6FF9A', 0.7 * s); }
    ctx.restore();
  });

  /* ================= 電線と夕空 ================= */
  K.registerBg('echoPowerLines', '電線と夕空', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `pl|${seed}|${ck(sc)}`;
    const sil = T.dark ? mix(sc.bg, '#000000', 0.9) : mix(sc.bg, '#101428', 0.78), hy = H * 0.74, sx = W * (portrait ? 0.62 : 0.7), sy = hy - U * 0.02;
    const poleX = [W * (portrait ? -0.02 : 0.06), W * (portrait ? 1.04 : 0.94)], arms = [H * 0.22, H * 0.3];
    const wires = [[0.0, 0.05], [0.05, 0.062], [0.1, 0.07], [0.15, 0.075]];
    const wy = (k, v) => arms[0] + (arms[1] - arms[0]) * v + H * (0.02 + wires[k][1] * 1.4) * 4 * v * (1 - v) + H * wires[k][0] * 0.45 - H * 0.02;
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.6, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, mix(T.bg0, T.c3, T.dark ? 0.25 : 0.2)], [0.45, mix(T.bg1, T.c2, T.dark ? 0.45 : 0.4)], [0.78, mix(T.bg1, T.c1, T.dark ? 0.7 : 0.6)], [1, mix(T.bg1, T.hi, T.dark ? 0.35 : 0.4)]]); x.fillRect(0, 0, W, H);
      glowDisc(x, sx, sy, U * 1.3, T.hi, T.dark ? 0.55 : 0.6); glowDisc(x, sx, sy, U * 0.5, '#FFFFFF', 0.5);
      x.fillStyle = mix('#FFFFFF', T.hi, 0.35); dot(x, sx, sy, U * 0.1);
      /* long streaks of cloud lit from below */
      for (let i = 0; i < 9; i++) { const cx = W * r(seed, i, 1), cy = H * (0.12 + 0.5 * r(seed, i, 2)), w = U * (0.5 + 0.8 * r(seed, i, 3)); x.save(); x.translate(cx, cy); x.scale(1, 0.07); glowDisc(x, 0, 0, w, i % 2 ? T.c1 : T.hi, T.dark ? 0.28 : 0.32); x.restore(); x.fillStyle = rgba(mix(sil, T.c3, 0.5), 0.2); x.fillRect(cx - w * 0.5, cy + U * 0.012, w, U * 0.004); }
      /* town below the line */
      x.fillStyle = sil; x.fillRect(0, hy, W, H - hy);
      for (let i = 0, px = 0; px < W; i++) { const bw = U * (0.04 + 0.07 * r(seed, i, 51)), bh = U * (0.03 + 0.12 * r(seed, i, 52)); x.fillRect(px, hy - bh, bw, bh + 2); if (r(seed, i, 53) > 0.6) { x.beginPath(); x.moveTo(px - 2, hy - bh); x.lineTo(px + bw / 2, hy - bh - bw * 0.35); x.lineTo(px + bw + 2, hy - bh); x.fill(); } px += bw + U * 0.004; }
      for (let i = 0; i < 40; i++) { x.fillStyle = rgba('#FFD98A', 0.65); x.fillRect(W * r(seed, i, 61), hy + U * 0.008 + (H - hy) * 0.5 * r(seed, i, 62), U * 0.006, U * 0.009); }
      /* the poles and the wires between them */
      x.strokeStyle = sil; x.fillStyle = sil;
      poleX.forEach((px, pi) => {
        const w = U * 0.014; x.fillRect(px - w / 2, 0, w, H); x.fillRect(px - U * 0.1, H * 0.2 + pi * H * 0.09, U * 0.2, U * 0.008); x.fillRect(px - U * 0.075, H * 0.25 + pi * H * 0.09, U * 0.15, U * 0.006);
        for (const dx of [-0.09, -0.04, 0.04, 0.09]) { x.fillRect(px + U * dx - U * 0.004, H * 0.2 + pi * H * 0.09 - U * 0.016, U * 0.008, U * 0.016); }
      });
      x.lineWidth = Math.max(1.2, U * 0.0028); x.lineCap = 'round';
      for (let k = 0; k < wires.length; k++) { x.beginPath(); for (let i = 0; i <= 40; i++) { const v = i / 40, px = poleX[0] + (poleX[1] - poleX[0]) * v, py = wy(k, v); i ? x.lineTo(px, py) : x.moveTo(px, py); } x.stroke(); }
      for (let k = 0; k < wires.length; k++) { x.beginPath(); for (let i = 0; i <= 20; i++) { const v = i / 20, px = poleX[1] + (W * 0.2) * v, py = wy(k, 1) - v * H * 0.04; i ? x.lineTo(px, py) : x.moveTo(px, py); } x.stroke(); }
    }), 0, 0, W, H);
    /* birds sitting on the wires, and a flock crossing */
    ctx.fillStyle = sil; ctx.strokeStyle = sil; ctx.lineWidth = Math.max(1.4, U * 0.003); ctx.lineCap = 'round';
    for (let i = 0; i < 7; i++) { if (r(seed, i, 71) < 0.15) continue; const k = i % wires.length, v = 0.18 + 0.66 * r(seed, i, 72), px = poleX[0] + (poleX[1] - poleX[0]) * v, py = wy(k, v) - U * 0.011; ctx.beginPath(); ctx.ellipse(px, py, U * 0.009, U * 0.0062, -0.2, 0, TAU); ctx.fill(); dot(ctx, px + U * 0.009, py - U * 0.005, U * 0.0035); ctx.beginPath(); ctx.moveTo(px - U * 0.007, py); ctx.lineTo(px - U * 0.019, py + U * 0.006); ctx.stroke(); }
    const fl = 12, cyc = 36, ph = frac(t / cyc), fx0 = W * (1.15 - ph * 1.5);
    if (fx0 > -W * 0.2 && fx0 < W * 1.2) for (let i = 0; i < fl; i++) {
      const bx = fx0 + (r(seed, i, 81) - 0.5) * U * 0.6 + i * U * 0.03, by = H * 0.4 + Math.sin(ph * 6 + i * 0.7) * U * 0.03 + (r(seed, i, 82) - 0.5) * U * 0.2 - i * U * 0.01, fp = Math.sin(t * 7 + i * 1.3), s = U * (0.014 + 0.006 * r(seed, i, 83));
      ctx.beginPath(); ctx.moveTo(bx - s, by - s * (0.4 + 0.5 * fp)); ctx.quadraticCurveTo(bx - s * 0.4, by - s * (0.5 + 0.5 * fp) - s * 0.2, bx, by); ctx.quadraticCurveTo(bx + s * 0.4, by - s * (0.5 + 0.5 * fp) - s * 0.2, bx + s, by - s * (0.4 + 0.5 * fp)); ctx.stroke();
    }
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    for (let i = 0; i < 20; i++) { const age = frac(t * (0.02 + 0.03 * r(seed, i, 91)) + r(seed, i, 92)); ctx.fillStyle = rgba(T.hi, 0.5 * Math.sin(age * Math.PI)); dot(ctx, W * r(seed, i, 93) + Math.sin(t * 0.3 + i) * U * 0.02, hy * (1 - age * 0.9), U * (0.0025 + 0.003 * r(seed, i, 94))); }
    ctx.restore();
  });

  /* ================= 折り鶴 ================= */
  const CRANE = {
    body: [[[-1.15, -0.4], [-0.3, 0.0], [-0.25, 0.22], 0.78], [[-0.3, 0.0], [-0.25, 0.22], [0.35, 0.12], 0.92], [[-0.3, 0.0], [0.35, 0.12], [0.3, -0.12], 1.12], [[0.3, -0.12], [0.35, 0.12], [1.0, -0.62], 0.88], [[1.0, -0.62], [1.22, -0.56], [0.97, -0.5], 1.22]]
  };
  const paintCrane = (ctx, col, s, flap, alpha) => {
    const shade = (f) => f >= 1 ? mix(col, '#FFFFFF', (f - 1) * 1.5) : mix(col, '#000000', (1 - f) * 0.8);
    const wing = (dir, f, k) => { const up = -(0.18 + 0.9 * flap) * dir; ctx.fillStyle = shade(f); ctx.beginPath(); ctx.moveTo(-0.3 * s, 0.02 * s); ctx.lineTo(0.3 * s, 0.0); ctx.lineTo(-0.28 * s * k, up * s); ctx.closePath(); ctx.fill(); };
    ctx.globalAlpha *= alpha;
    wing(1, 0.8, 1); /* far wing */
    for (const [a, b, c, f] of CRANE.body) { ctx.fillStyle = shade(f); ctx.beginPath(); ctx.moveTo(a[0] * s, a[1] * s); ctx.lineTo(b[0] * s, b[1] * s); ctx.lineTo(c[0] * s, c[1] * s); ctx.closePath(); ctx.fill(); }
    wing(1, 1.18, 1.25); /* near wing */
    ctx.strokeStyle = rgba('#000000', 0.12); ctx.lineWidth = Math.max(0.6, s * 0.012); ctx.beginPath(); ctx.moveTo(-0.3 * s, 0); ctx.lineTo(0.35 * s, 0.12 * s); ctx.moveTo(0.3 * s, -0.12 * s); ctx.lineTo(1.0 * s, -0.62 * s); ctx.stroke();
  };
  K.registerBg('echoOrigami', '折り鶴', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `og|${seed}|${ck(sc)}`;
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.5, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [1, T.bg1]]); x.fillRect(0, 0, W, H);
      glowDisc(x, W * 0.3, H * 0.3, U * 1.3, T.c1, T.dark ? 0.38 : 0.4); glowDisc(x, W * 0.75, H * 0.7, U * 1.2, T.c2, T.dark ? 0.3 : 0.34); glowDisc(x, W * 0.55, H * 0.45, U * 0.8, T.hi, T.dark ? 0.12 : 0.2);
      /* big folded facets across the sheet, as if the paper itself was creased */
      for (let i = 0; i < 9; i++) { const ax = W * r(seed, i, 1), ay = H * r(seed, i, 2), s = U * (0.5 + 0.9 * r(seed, i, 3)), a = r(seed, i, 4) * TAU; x.fillStyle = rgba(i % 2 ? '#FFFFFF' : '#000000', T.dark ? (i % 2 ? 0.045 : 0.08) : (i % 2 ? 0.14 : 0.045)); x.beginPath(); x.moveTo(ax, ay); x.lineTo(ax + Math.cos(a) * s, ay + Math.sin(a) * s); x.lineTo(ax + Math.cos(a + 1.1) * s * 0.8, ay + Math.sin(a + 1.1) * s * 0.8); x.closePath(); x.fill(); }
      x.strokeStyle = rgba(T.dark ? '#FFFFFF' : '#000000', T.dark ? 0.05 : 0.05); x.lineWidth = 1; for (let i = 0; i < 160; i++) { const fx = r(seed, i, 11) * W, fy = r(seed, i, 12) * H, a = r(seed, i, 13) * TAU, l = U * (0.01 + 0.03 * r(seed, i, 14)); x.beginPath(); x.moveTo(fx, fy); x.lineTo(fx + Math.cos(a) * l, fy + Math.sin(a) * l); x.stroke(); }
    }), 0, 0, W, H);
    const pal = [T.c1, T.c2, T.c3, mix('#FFFFFF', sc.accent, 0.08), mix(sc.accent, '#FFFFFF', 0.35), mix(sc.accent2, '#FFFFFF', 0.3)];
    const N = portrait ? 6 : 8, list = [];
    for (let i = 0; i < N; i++) {
      const z = 0.4 + 0.6 * r(seed, i, 5), sp = (0.012 + 0.016 * z) * (i % 5 === 4 ? -1 : 1), span = W + U * 1.4;
      const prog = frac(r(seed, i, 6) + t * sp * U / span * 1.0), x = prog * span - U * 0.7, dir = sp < 0 ? -1 : 1, X = dir < 0 ? W - x : x;
      const y = H * (0.15 + 0.7 * r(seed, i, 7)) + Math.sin(t * 0.5 + i * 2) * U * 0.05 + Math.sin(prog * 7 + i) * U * 0.04;
      list.push({ i, z, X, y, dir, s: U * (0.05 + 0.1 * z), col: pal[i % pal.length], fl: 0.5 + 0.5 * Math.sin(t * (1.6 + 0.6 * z) + i * 1.9), tilt: Math.cos(prog * 7 + i) * 0.12 });
    }
    list.sort((a, b) => a.z - b.z);
    for (const c of list) {
      ctx.save(); ctx.globalAlpha = e * 0.18; ctx.fillStyle = T.dark ? '#000000' : mix(sc.fg, sc.bg, 0.2); ctx.translate(c.X + c.s * 0.35, c.y + c.s * 0.9); ctx.scale(c.dir, 0.2); ctx.beginPath(); ctx.ellipse(0, 0, c.s * 1.1, c.s * 0.5, 0, 0, TAU); ctx.fill(); ctx.restore();
      ctx.save(); ctx.globalAlpha = e; ctx.translate(c.X, c.y); ctx.rotate(c.tilt * c.dir); ctx.scale(c.dir, 1); paintCrane(ctx, c.col, c.s, c.fl, 0.55 + 0.45 * c.z); ctx.restore();
    }
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    for (let i = 0; i < 22; i++) { const age = frac(t * (0.015 + 0.02 * r(seed, i, 71)) + r(seed, i, 72)); ctx.fillStyle = rgba(i % 2 ? T.hi : '#FFFFFF', (T.dark ? 0.5 : 0.6) * Math.sin(age * Math.PI)); const s = U * (0.004 + 0.005 * r(seed, i, 74)); ctx.save(); ctx.translate(W * r(seed, i, 73) + Math.sin(t * 0.3 + i) * U * 0.03, H * (1.02 - age * 1.04)); ctx.rotate(t * 0.4 + i); ctx.fillRect(-s, -s, s * 2, s * 2); ctx.restore(); }
    ctx.restore();
  });

  /* ================= 気球 ================= */
  K.registerBg('echoHotAir', '気球', ['calm', 'pop', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `ha|${seed}|${ck(sc)}`;
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.5, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [0.6, T.bg1], [1, mix(T.bg1, T.hi, T.dark ? 0.2 : 0.35)]]); x.fillRect(0, 0, W, H);
      glowDisc(x, W * 0.8, H * 0.25, U * 0.9, T.hi, T.dark ? 0.3 : 0.5); glowDisc(x, W * 0.15, H * 0.7, U * 0.7, T.c2, T.dark ? 0.16 : 0.28);
      const tone = T.dark ? { light: mix(sc.bg, '#FFFFFF', 0.35), body: mix(sc.bg, '#FFFFFF', 0.18), shade: mix(sc.bg, '#000000', 0.3), belly: mix(sc.accent, sc.bg, 0.6) } : { light: '#FFFFFF', body: mix('#FFFFFF', sc.bg, 0.4), shade: mix(sc.bg, sc.accent, 0.35), belly: mix(sc.bg, sc.accent2, 0.35) };
      for (let i = 0; i < (portrait ? 3 : 5); i++) K.cloud(x, W * (i / (portrait ? 3 : 5) + 0.06 * r(seed, i, 1)) - U * 0.1, H * (0.55 + 0.35 * r(seed, i, 2)), U * (0.55 + 0.4 * r(seed, i, 3)), U * (0.18 + 0.1 * r(seed, i, 4)), J.h(seed, i, 5), tone, 1, true);
    }), 0, 0, W, H);
    const pal = [T.c1, T.c2, T.c3, '#F2EFE6', mix(sc.accent, '#FFFFFF', 0.3), '#F4D03F'];
    const N = portrait ? 4 : 6, list = [];
    for (let i = 0; i < N; i++) { const z = 0.35 + 0.65 * r(seed, i, 5), R = U * (0.05 + 0.1 * z), x = wrap(W * r(seed, i, 6) + t * U * (0.004 + 0.008 * z) * (i % 2 ? 1 : -1), W + R * 6) - R * 3, y = H * (0.18 + 0.5 * r(seed, i, 7)) + Math.sin(t * 0.3 + i * 2) * U * 0.03 * z; list.push({ i, z, R, x, y }); }
    list.sort((a, b) => a.z - b.z);
    for (const b of list) {
      const { R, x, y, z, i } = b, a1 = pal[i % pal.length], a2 = pal[(i + 3) % pal.length], sway = Math.sin(t * 0.5 + i) * 0.04, fade2 = 0.45 + 0.55 * z, hb = R * 1.28;
      ctx.save(); ctx.translate(x, y); ctx.rotate(sway); ctx.globalAlpha = e * fade2;
      const env2 = () => { ctx.beginPath(); ctx.moveTo(0, -R * 1.05); ctx.bezierCurveTo(R * 0.85, -R * 1.05, R * 1.15, -R * 0.2, R * 0.22, R * 0.95); ctx.lineTo(-R * 0.22, R * 0.95); ctx.bezierCurveTo(-R * 1.15, -R * 0.2, -R * 0.85, -R * 1.05, 0, -R * 1.05); ctx.closePath(); };
      ctx.save(); env2(); ctx.clip();
      ctx.fillStyle = a2; ctx.fillRect(-R * 1.3, -R * 1.2, R * 2.6, R * 2.4); const gores = 8; for (let g = 0; g < gores; g++) { if (!(g % 2)) continue; ctx.fillStyle = a1; const u0 = -1.2 + 2.4 * g / gores, u1 = -1.2 + 2.4 * (g + 1) / gores; ctx.beginPath(); ctx.moveTo(0, -R * 1.1); ctx.quadraticCurveTo(u0 * R * 1.3, -R * 0.1, u0 * R * 0.2, R); ctx.lineTo(u1 * R * 0.2, R); ctx.quadraticCurveTo(u1 * R * 1.3, -R * 0.1, 0, -R * 1.1); ctx.closePath(); ctx.fill(); }
      const sh = ctx.createLinearGradient(-R, 0, R, 0); sh.addColorStop(0, rgba('#FFFFFF', 0.28)); sh.addColorStop(0.35, rgba('#FFFFFF', 0)); sh.addColorStop(0.75, rgba('#000000', 0.08)); sh.addColorStop(1, rgba('#000000', 0.32)); ctx.fillStyle = sh; ctx.fillRect(-R * 1.3, -R * 1.2, R * 2.6, R * 2.4);
      ctx.fillStyle = rgba('#000000', 0.2); ctx.fillRect(-R * 1.2, R * 0.78, R * 2.4, R * 0.2);
      ctx.restore();
      ctx.strokeStyle = rgba(T.dark ? '#FFFFFF' : '#000000', 0.35); ctx.lineWidth = Math.max(0.8, R * 0.012); env2(); ctx.stroke();
      ctx.beginPath(); ctx.moveTo(-R * 0.22, R * 0.95); ctx.lineTo(-R * 0.12, hb); ctx.moveTo(R * 0.22, R * 0.95); ctx.lineTo(R * 0.12, hb); ctx.stroke();
      ctx.fillStyle = mix('#8A5A3A', sc.accent2, 0.1); ctx.fillRect(-R * 0.16, hb, R * 0.32, R * 0.2); ctx.fillStyle = rgba('#FFFFFF', 0.2); ctx.fillRect(-R * 0.16, hb, R * 0.32, R * 0.04);
      ctx.restore();
      glow(ctx, T, x + Math.sin(sway) * R * 1.1, y + R * 1.0, R * 0.7, '#FFB347', (0.5 + 0.2 * Math.sin(t * 9 + i * 3)) * fade2);
    }
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    for (let i = 0; i < 14; i++) { const age = frac(t * (0.02 + 0.025 * r(seed, i, 71)) + r(seed, i, 72)); ctx.fillStyle = rgba('#FFFFFF', 0.6 * Math.sin(age * Math.PI)); dot(ctx, W * r(seed, i, 73), H * (1.02 - age * 1.04), U * (0.003 + 0.004 * r(seed, i, 74))); }
    ctx.restore();
  });

  /* ================= 夜の交差点 ================= */
  K.registerBg('echoCrosswalk', '夜の交差点', ['glitch', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `cw|${seed}|${ck(sc)}`;
    const hy = H * 0.5, vx = W * 0.5, sil = T.dark ? mix(sc.bg, '#000000', 0.82) : mix(sc.bg, sc.fg, 0.55);
    const neon = [T.c1, T.c2, T.c3, '#FFD98A'];
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.6, (x) => {
      x.fillStyle = vertical(x, 0, hy, [[0, T.bg0], [1, mix(T.bg1, T.c1, T.dark ? 0.35 : 0.3)]]); x.fillRect(0, 0, W, hy + 2);
      /* buildings either side, in rough perspective, windows lit at random */
      for (const side of [-1, 1]) {
        let px = side < 0 ? 0 : W;
        for (let i = 0; i < 7; i++) {
          const bw = W * (0.07 + 0.05 * r(seed, i, side + 2)), bh = H * (0.25 + 0.35 * r(seed, i, side + 3)) * (1 - i * 0.07), x0 = side < 0 ? px : px - bw; x.fillStyle = mix(sil, T.bg1, 0.12 * i / 7); x.fillRect(x0, hy - bh, bw, bh + 4);
          for (let wy = hy - bh + U * 0.03; wy < hy - U * 0.03; wy += U * 0.045) for (let wx = x0 + bw * 0.12; wx < x0 + bw * 0.85; wx += U * 0.035) if (r(seed, i, side, Math.round(wx * 3 + wy)) > 0.55) { x.fillStyle = rgba(r(seed, i, Math.round(wy)) > 0.7 ? neon[i % 3] : '#FFD98A', 0.7); x.fillRect(wx, wy, U * 0.016, U * 0.022); }
          px += side * bw * 0.93;
        }
      }
      for (let i = 0; i < 26; i++) { const bx = W * (0.14 + 0.72 * i / 26), bw = W * (0.02 + 0.025 * r(seed, i, 81)), bh = H * (0.05 + 0.18 * r(seed, i, 82)); x.fillStyle = mix(sil, T.c1, 0.28); x.fillRect(bx, hy - bh, bw, bh + 3); if (r(seed, i, 83) > 0.5) { x.fillStyle = rgba(neon[i % 4], 0.6); x.fillRect(bx + bw * 0.25, hy - bh * 0.8, bw * 0.2, bh * 0.1); } }
      /* the road: wet asphalt receding to the vanishing point */
      x.fillStyle = vertical(x, hy, H, [[0, mix(sil, T.c1, 0.2)], [0.4, mix(sil, T.bg0, 0.4)], [1, mix(sil, '#000000', T.dark ? 0.5 : 0.1)]]); x.fillRect(0, hy, W, H - hy);
      glowDisc(x, vx, hy, U * 0.9, T.c1, T.dark ? 0.4 : 0.4);
      /* the crossing: bars that grow toward us */
      x.fillStyle = rgba(T.dark ? '#FFFFFF' : '#FFFFFF', T.dark ? 0.62 : 0.8);
      for (let i = 0; i < 9; i++) {
        const d0 = 0.30 + i * 0.075, y1 = hy + (H - hy) * Math.pow(d0, 1.6), y2 = hy + (H - hy) * Math.pow(d0 + 0.045, 1.6), k1 = (y1 - hy) / (H - hy), k2 = (y2 - hy) / (H - hy);
        for (let b = -6; b <= 6; b++) { if (b % 2 === 0) { const hw1 = W * 0.04 * k1 * 1.1, hw2 = W * 0.04 * k2 * 1.1, c1 = vx + b * W * 0.085 * k1 * 1.0, c2 = vx + b * W * 0.085 * k2 * 1.0; } }
      }
      const bars = 13, zy = [0.42, 0.62, 0.84];
      x.fillStyle = rgba('#FFFFFF', T.dark ? 0.7 : 0.85);
      for (let b = 0; b < bars; b++) {
        const u0 = -0.5 + (b + 0.12) / bars, u1 = -0.5 + (b + 0.78) / bars, ya = hy + (H - hy) * 0.5, yb = hy + (H - hy) * 0.78, ka = (ya - hy) / (H - hy), kb = (yb - hy) / (H - hy);
        x.beginPath(); x.moveTo(vx + u0 * W * 1.1 * ka, ya); x.lineTo(vx + u1 * W * 1.1 * ka, ya); x.lineTo(vx + u1 * W * 1.1 * kb, yb); x.lineTo(vx + u0 * W * 1.1 * kb, yb); x.closePath(); x.fill();
      }
      x.fillStyle = rgba(T.hi, 0.2); x.fillRect(0, hy, W, 2);
    }), 0, 0, W, H);
    ctx.fillStyle = sil; ctx.fillRect(vx + W * 0.1 - U * 0.004, hy - H * 0.2, U * 0.008, H * 0.2 + (H - hy) * 0.1); ctx.fillRect(vx + W * 0.1 - U * 0.02, hy - H * 0.215, U * 0.04, U * 0.05);
    /* the signal: red, then green, with people crossing on green */
    const cyc = 18, ph = frac(t / cyc), green = ph > 0.45 && ph < 0.9, amber = ph >= 0.9, sx = vx + W * 0.1, sy = hy - H * 0.2;
    const lampC = green ? '#5CFF9A' : amber ? '#FFC24A' : '#FF4A4A';
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'lighter';
    ctx.fillStyle = rgba(lampC, 0.95); dot(ctx, sx, sy, U * 0.012); glowDisc(ctx, sx, sy, U * 0.16, lampC, 0.7); glowDisc(ctx, sx, sy, U * 0.06, '#FFFFFF', 0.4);
    ctx.save(); ctx.translate(sx, hy + (H - hy) * 0.12); ctx.scale(0.5, 2.6); glowDisc(ctx, 0, 0, U * 0.1, lampC, 0.35); ctx.restore();
    /* cars passing the far junction: two glowing lamps and a wake of light */
    for (let i = 0; i < 3; i++) {
      const per = 5 + i * 1.7, n = Math.floor((t + i * 2.3) / per), pp = frac((t + i * 2.3) / per); if (green && pp < 0.9 && i !== 1) continue;
      const dir = (n + i) % 2 ? 1 : -1, cx = W * (dir > 0 ? -0.1 + 1.2 * pp : 1.1 - 1.2 * pp), cy = hy + U * 0.03 + i * U * 0.01, col = (n + i) % 3 ? '#FFF1C8' : '#FF5A5A';
      ctx.fillStyle = rgba(col, 0.9); dot(ctx, cx, cy, U * 0.008); dot(ctx, cx + U * 0.03, cy, U * 0.008); glowDisc(ctx, cx + U * 0.015, cy, U * 0.1, col, 0.55);
      const g = ctx.createLinearGradient(cx, cy, cx - dir * U * 0.5, cy); g.addColorStop(0, rgba(col, 0.35)); g.addColorStop(1, rgba(col, 0)); ctx.fillStyle = g; ctx.fillRect(Math.min(cx, cx - dir * U * 0.5), cy - U * 0.004, U * 0.5, U * 0.008);
      ctx.save(); ctx.translate(cx + U * 0.015, hy + (H - hy) * 0.09); ctx.scale(1.2, 2.2); glowDisc(ctx, 0, 0, U * 0.06, col, 0.22); ctx.restore();
    }
    ctx.restore();
    if (green) {
      const gp = (ph - 0.45) / 0.45;
      for (let i = 0; i < 5; i++) {
        const dir = i % 2 ? 1 : -1, v = clamp(gp * 1.5 - i * 0.12) / 1, pos = dir > 0 ? v : 1 - v; if (v <= 0 || v >= 1) continue;
        const yy = hy + (H - hy) * (0.52 + 0.05 * i), k = (yy - hy) / (H - hy), px = vx + (pos - 0.5) * W * 0.9 * (0.5 + k), s = U * 0.12 * k * 1.1 + U * 0.025, bob = Math.sin(t * 6 + i * 2) * s * 0.03;
        ctx.save(); ctx.translate(px, yy); ctx.fillStyle = sil; ctx.globalAlpha = e * 0.95;
        ctx.beginPath(); ctx.ellipse(0, -s * 1.55 + bob, s * 0.11, s * 0.12, 0, 0, TAU); ctx.fill();
        ctx.beginPath(); ctx.moveTo(-s * 0.12, -s * 1.42 + bob); ctx.lineTo(s * 0.12, -s * 1.42 + bob); ctx.lineTo(s * 0.16, -s * 0.6); ctx.lineTo(-s * 0.16, -s * 0.6); ctx.closePath(); ctx.fill();
        const sw = Math.sin(t * 6 + i * 2) * s * 0.14; ctx.lineWidth = s * 0.09; ctx.strokeStyle = sil; ctx.lineCap = 'round';
        ctx.beginPath(); ctx.moveTo(-s * 0.05, -s * 0.6); ctx.lineTo(-s * 0.05 + sw, 0); ctx.moveTo(s * 0.05, -s * 0.6); ctx.lineTo(s * 0.05 - sw, 0); ctx.stroke();
        ctx.beginPath(); ctx.moveTo(i % 2 ? -s * 0.9 : s * 0.9, 0); ctx.restore();
        if (i === 2) { ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'lighter'; glowDisc(ctx, px + (dir > 0 ? -1 : 1) * s * 0.4, yy - s * 1.2, s * 1.4, '#FFB1C4', 0.2); ctx.restore(); }
      }
    }
    /* light spilling onto the wet road, and rain */
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'lighter';
    ctx.save(); ctx.translate(sx, hy + (H - hy) * 0.45); ctx.scale(0.35, 3.2); glowDisc(ctx, 0, 0, U * 0.2, lampC, 0.2); ctx.restore();
    ctx.restore();
    ctx.strokeStyle = rgba('#DDEBFF', T.dark ? 0.28 : 0.4); ctx.lineWidth = Math.max(1, U * 0.0015); ctx.lineCap = 'round';
    for (let i = 0; i < 70; i++) { const z = r(seed, i, 31), sp = H * (0.9 + 0.8 * z), x = r(seed, i, 32) * W, y = wrap(r(seed, i, 33) * H * 1.2 + t * sp, H * 1.2) - H * 0.1, l = U * (0.03 + 0.05 * z); ctx.globalAlpha = e * (0.2 + 0.4 * z); ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(x - l * 0.12, y + l); ctx.stroke(); }
    ctx.globalAlpha = e;
  });

  /* ================= 雪の森 ================= */
  K.registerBg('echoSnowForest', '雪の森', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `sf|${seed}|${ck(sc)}`;
    const pine = (x, px, base, h, col, snow) => {
      const w = h * 0.3, tiers = 6, tri = (ya, ww, hh) => { x.beginPath(); x.moveTo(px, ya); x.lineTo(px + ww, ya + hh); x.lineTo(px - ww, ya + hh); x.closePath(); };
      x.fillStyle = col; x.fillRect(px - h * 0.012, base - h * 0.06, h * 0.024, h * 0.08);
      for (let k = 0; k < tiers; k++) { const f = k / tiers, ya = base - h * (1 - f * 0.9), ww = w * (0.3 + 0.7 * (k + 1) / tiers), hh = h * 0.24; tri(ya, ww, hh); x.fill(); }
      if (snow) { x.fillStyle = snow; for (let k = 0; k < tiers; k++) { const f = k / tiers, ya = base - h * (1 - f * 0.9), ww = w * (0.3 + 0.7 * (k + 1) / tiers), hh = h * 0.24; x.beginPath(); x.moveTo(px, ya); x.lineTo(px + ww * 0.5, ya + hh * 0.5); x.quadraticCurveTo(px + ww * 0.25, ya + hh * 0.42, px + ww * 0.12, ya + hh * 0.56); x.quadraticCurveTo(px, ya + hh * 0.44, px - ww * 0.15, ya + hh * 0.55); x.quadraticCurveTo(px - ww * 0.3, ya + hh * 0.42, px - ww * 0.5, ya + hh * 0.5); x.closePath(); x.fill(); } }
    };
    const band = (x, n, base, h0, h1, col, snow, k) => { for (let i = 0; i < n; i++) { const px = W * (i + 0.5 + (r(seed, i, k) - 0.5) * 0.7) / n; pine(x, px, base + U * 0.02 * r(seed, i, k + 1), h0 + (h1 - h0) * r(seed, i, k + 2), col, snow); } };
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.6, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [0.6, mix(T.bg1, T.c1, T.dark ? 0.18 : 0.15)], [1, mix(T.bg1, T.hi, T.dark ? 0.25 : 0.35)]]); x.fillRect(0, 0, W, H);
      glowDisc(x, W * 0.72, H * 0.2, U * 0.5, T.hi, T.dark ? 0.55 : 0.65); x.fillStyle = mix('#FFFFFF', T.hi, 0.25); dot(x, W * 0.72, H * 0.2, U * 0.045);
      if (T.dark) for (let i = 0; i < 70; i++) { x.fillStyle = rgba('#FFFFFF', 0.2 + 0.5 * r(seed, i, 3)); dot(x, r(seed, i, 1) * W, r(seed, i, 2) * H * 0.45, U * (0.0012 + 0.0022 * r(seed, i, 4))); }
      const far = mix(T.bg1, T.c1, T.dark ? 0.25 : 0.2), mid = mix(T.bg0, T.c1, T.dark ? 0.15 : 0.3), near = T.dark ? mix(sc.bg, '#000000', 0.78) : mix(sc.bg, '#18223A', 0.6);
      band(x, portrait ? 8 : 18, H * 0.62, U * 0.34, U * 0.5, far, rgba('#FFFFFF', T.dark ? 0.18 : 0.5), 11);
      x.fillStyle = vertical(x, H * 0.4, H * 0.7, [[0, rgba(far, 0)], [1, rgba(mix(T.bg1, T.hi, 0.25), 0.7)]]); x.fillRect(0, H * 0.4, W, H * 0.3);
      band(x, portrait ? 6 : 12, H * 0.74, U * 0.5, U * 0.72, mid, rgba('#FFFFFF', T.dark ? 0.55 : 0.85), 21);
      x.fillStyle = vertical(x, H * 0.5, H * 0.82, [[0, rgba(mid, 0)], [1, rgba(mix(T.bg1, T.hi, 0.22), 0.6)]]); x.fillRect(0, H * 0.5, W, H * 0.34);
      /* a cabin in the clearing, its window lit */
      const cx = W * (portrait ? 0.3 : 0.34), cyb = H * 0.8; x.fillStyle = near; x.fillRect(cx - U * 0.06, cyb - U * 0.06, U * 0.12, U * 0.06); x.beginPath(); x.moveTo(cx - U * 0.08, cyb - U * 0.058); x.lineTo(cx, cyb - U * 0.115); x.lineTo(cx + U * 0.08, cyb - U * 0.058); x.closePath(); x.fill();
      x.fillStyle = rgba('#FFFFFF', 0.85); x.beginPath(); x.moveTo(cx - U * 0.085, cyb - U * 0.056); x.lineTo(cx, cyb - U * 0.118); x.lineTo(cx + U * 0.085, cyb - U * 0.056); x.lineTo(cx + U * 0.07, cyb - U * 0.056); x.lineTo(cx, cyb - U * 0.104); x.lineTo(cx - U * 0.07, cyb - U * 0.056); x.closePath(); x.fill();
      x.fillStyle = '#FFD98A'; x.fillRect(cx - U * 0.03, cyb - U * 0.044, U * 0.026, U * 0.024); glowDisc(x, cx - U * 0.017, cyb - U * 0.032, U * 0.22, '#FFD98A', T.dark ? 0.5 : 0.35);
      x.fillStyle = vertical(x, H * 0.78, H, [[0, mix(T.bg1, '#FFFFFF', T.dark ? 0.18 : 0.6)], [1, mix(T.bg1, '#FFFFFF', T.dark ? 0.35 : 0.8)]]); x.beginPath(); x.moveTo(0, H); x.lineTo(0, H * 0.84); for (let px = 0; px <= W; px += W / 40) x.lineTo(px, H * 0.84 + Math.sin(px / W * 6 + seed) * U * 0.02 - U * 0.01 * Math.sin(px / W * 15)); x.lineTo(W, H); x.fill();
      band(x, portrait ? 3 : 5, H * 0.99, U * 0.95, U * 1.25, near, rgba('#FFFFFF', T.dark ? 0.4 : 0.8), 31);
    }), 0, 0, W, H);
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    /* drifting mist and three depths of falling snow */
    for (let i = 0; i < 5; i++) { const mx = wrap(r(seed, i, 51) * W * 1.4 + t * U * (0.006 + 0.008 * r(seed, i, 52)), W * 1.4) - W * 0.2, my = H * (0.58 + 0.2 * r(seed, i, 53)); ctx.save(); ctx.translate(mx, my); ctx.scale(2.6, 0.5); glowDisc(ctx, 0, 0, U * (0.35 + 0.25 * r(seed, i, 54)), T.hi, T.dark ? 0.1 : 0.2); ctx.restore(); }
    for (let i = 0; i < (portrait ? 70 : 100); i++) {
      const z = r(seed, i, 61), sp = U * (0.03 + 0.07 * z), x = wrap(W * r(seed, i, 62) + Math.sin(t * 0.5 + i) * U * 0.03 * z + t * U * 0.01 * z, W + 20) - 10, y = wrap(H * r(seed, i, 63) + t * sp * 2.2, H + 20) - 10, s = U * (0.002 + 0.005 * z * z);
      ctx.fillStyle = rgba('#FFFFFF', (T.dark ? 0.35 : 0.8) + 0.3 * z); dot(ctx, x, y, s);
    }
    ctx.restore();
  });

  /* ================= サイフォン ================= */
  K.registerBg('echoSiphon', 'サイフォン', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `sp|${seed}|${ck(sc)}`;
    const cx = W * (portrait ? 0.5 : 0.64), R = U * 0.15, by = H * 0.7, topH = U * 0.26;
    const wood = T.dark ? mix(sc.bg, sc.accent, 0.25) : mix(sc.bg, sc.accent, 0.4), glass = rgba('#FFFFFF', T.dark ? 0.16 : 0.45), coffee = T.dark ? mix('#3A1E10', sc.accent, 0.15) : mix('#4A2A18', sc.accent, 0.1);
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.5, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, mix(T.bg0, sc.accent, 0.1)], [1, mix(T.bg1, sc.accent, 0.2)]]); x.fillRect(0, 0, W, H);
      /* warm, out-of-focus shop lights and a shelf of cups */
      for (let i = 0; i < 24; i++) { const lx = W * r(seed, i, 1), ly = H * (0.05 + 0.5 * r(seed, i, 2)), lr = U * (0.02 + 0.05 * r(seed, i, 3)); x.fillStyle = rgba(i % 3 ? '#FFD98A' : T.c1, T.dark ? 0.14 + 0.18 * r(seed, i, 4) : 0.2 + 0.2 * r(seed, i, 4)); dot(x, lx, ly, lr); x.strokeStyle = rgba('#FFE9B8', 0.12); x.lineWidth = 1; x.beginPath(); x.arc(lx, ly, lr, 0, TAU); x.stroke(); }
      glowDisc(x, cx, by - R * 2, U * 0.9, '#FFD98A', T.dark ? 0.2 : 0.3);
      const sy = H * 0.34; x.fillStyle = wood; x.fillRect(0, sy, W * (portrait ? 1 : 0.4), U * 0.018); x.fillStyle = rgba('#FFFFFF', 0.2); x.fillRect(0, sy, W * (portrait ? 1 : 0.4), U * 0.004);
      for (let i = 0; i < (portrait ? 4 : 5); i++) { const px = W * (portrait ? 0.1 : 0.05) + i * U * 0.17, ph2 = U * (0.06 + 0.02 * r(seed, i, 7)); x.fillStyle = mix(wood, i % 2 ? '#F2EFE6' : sc.accent2, 0.5); x.beginPath(); x.moveTo(px, sy - ph2); x.lineTo(px + U * 0.09, sy - ph2); x.quadraticCurveTo(px + U * 0.09, sy, px + U * 0.045, sy); x.quadraticCurveTo(px, sy, px, sy - ph2); x.fill(); x.fillStyle = rgba('#FFFFFF', 0.25); x.fillRect(px + U * 0.008, sy - ph2 + U * 0.004, U * 0.01, ph2 * 0.6); }
      /* counter */
      const cy2 = by + R * 1.35; x.fillStyle = vertical(x, cy2, H, [[0, mix(wood, '#FFFFFF', 0.1)], [1, mix(wood, '#000000', 0.5)]]); x.fillRect(0, cy2, W, H - cy2); x.fillStyle = rgba('#FFFFFF', 0.25); x.fillRect(0, cy2, W, U * 0.004);
      x.fillStyle = rgba('#000000', 0.3); x.beginPath(); x.ellipse(cx, cy2 + U * 0.02, R * 1.5, U * 0.025, 0, 0, TAU); x.fill();
    }), 0, 0, W, H);
    const cyc = 30, p = frac(t / cyc), up = ease((p - 0.18) / 0.12) * (1 - ease((p - 0.74) / 0.12)), boil = clamp((p - 0.06) / 0.14), tubeX = cx, topY = by - R * 1.55 - topH, topW = R * 0.82;
    /* lower flask: round glass with the water; upper: tall chamber */
    const flask = () => { ctx.beginPath(); ctx.arc(cx, by, R, 0, TAU); };
    ctx.save(); flask(); ctx.clip();
    const lvl = by + R * (0.3 - 0.75 * (1 - up) * 0.0) - R * 0.55 * (1 - up) + R * 0.7 * up; ctx.fillStyle = rgba(mix(T.dark ? '#7A4A2A' : '#9A6A3A', sc.accent, 0.1), 0.88); ctx.fillRect(cx - R, lvl, R * 2, R * 2);
    ctx.fillStyle = rgba('#FFFFFF', 0.1); ctx.fillRect(cx - R, lvl, R * 2, R * 0.04);
    for (let i = 0; i < 12; i++) { const age = frac(t * (0.4 + 0.5 * boil) + i / 12); if (boil < 0.2 || up > 0.8) break; ctx.fillStyle = rgba('#FFFFFF', 0.5 * (1 - age)); dot(ctx, cx + Math.sin(i * 3.1) * R * 0.45, by + R * 0.9 - age * (by + R * 0.9 - lvl), R * (0.025 + 0.03 * ((i * 7) % 5) / 5)); }
    ctx.restore();
    ctx.strokeStyle = rgba('#FFFFFF', T.dark ? 0.7 : 0.8); ctx.lineWidth = Math.max(1.5, U * 0.004); flask(); ctx.stroke(); ctx.fillStyle = glass; flask(); ctx.fill();
    ctx.strokeStyle = rgba('#FFFFFF', 0.55); ctx.lineWidth = Math.max(2, U * 0.007); ctx.beginPath(); ctx.arc(cx, by, R * 0.8, Math.PI * 1.05, Math.PI * 1.4); ctx.stroke();
    /* neck of the flask and the chamber */
    ctx.fillStyle = glass; ctx.fillRect(cx - R * 0.26, by - R * 1.4, R * 0.52, R * 0.5);
    const chamber = () => { ctx.beginPath(); ctx.moveTo(cx - topW, topY); ctx.lineTo(cx + topW, topY); ctx.lineTo(cx + topW * 0.95, by - R * 1.6); ctx.quadraticCurveTo(cx + topW * 0.9, by - R * 1.5, cx + R * 0.26, by - R * 1.42); ctx.lineTo(cx - R * 0.26, by - R * 1.42); ctx.quadraticCurveTo(cx - topW * 0.9, by - R * 1.5, cx - topW * 0.95, by - R * 1.6); ctx.closePath(); };
    const cLvl = by - R * 1.6 - topH * 0.0 - (by - R * 1.6 - topY) * 0.62 * up;
    ctx.save(); chamber(); ctx.clip(); const g = ctx.createLinearGradient(cx - topW, 0, cx + topW, 0); g.addColorStop(0, mix(coffee, '#000000', 0.3)); g.addColorStop(0.4, coffee); g.addColorStop(1, mix(coffee, '#000000', 0.45)); ctx.fillStyle = g; ctx.fillRect(cx - topW, cLvl, topW * 2, by - cLvl);
    ctx.fillStyle = rgba('#C8946A', 0.5); ctx.fillRect(cx - topW, cLvl, topW * 2, R * 0.05);
    for (let i = 0; i < 10; i++) { const age = frac(t * 0.7 + i / 10 + r(seed, i, 5)); if (up < 0.4) break; ctx.fillStyle = rgba('#F5E3C8', 0.55 * (1 - age)); dot(ctx, cx + Math.sin(i * 2.3 + t) * topW * 0.6, by - R * 1.55 - age * (by - R * 1.55 - cLvl), R * (0.03 + 0.02 * ((i * 3) % 4) / 4)); }
    ctx.restore(); ctx.fillStyle = glass; chamber(); ctx.fill(); ctx.strokeStyle = rgba('#FFFFFF', T.dark ? 0.7 : 0.8); ctx.lineWidth = Math.max(1.5, U * 0.004); chamber(); ctx.stroke();
    ctx.strokeStyle = rgba('#FFFFFF', 0.5); ctx.lineWidth = Math.max(2, U * 0.006); ctx.beginPath(); ctx.moveTo(cx - topW * 0.7, topY + topH * 0.12); ctx.lineTo(cx - topW * 0.72, by - R * 1.7); ctx.stroke();
    ctx.fillStyle = mix(wood, '#000000', 0.2); ctx.fillRect(cx - topW * 1.05, topY - U * 0.012, topW * 2.1, U * 0.014); ctx.fillRect(cx - R * 0.5, by - R * 1.42, R, U * 0.012);
    /* the stand, and the little flame beneath */
    ctx.strokeStyle = mix(wood, '#000000', 0.25); ctx.lineWidth = Math.max(3, U * 0.01); ctx.beginPath(); ctx.moveTo(cx + topW * 1.2, topY - U * 0.005); ctx.lineTo(cx + topW * 1.2, by + R * 1.35); ctx.stroke();
    const lx = cx, ly = by + R * 1.35, lamp = mix(wood, '#000000', 0.1); ctx.fillStyle = rgba('#FFFFFF', 0.28); ctx.beginPath(); ctx.ellipse(lx, ly - R * 0.18, R * 0.34, R * 0.2, 0, 0, TAU); ctx.fill(); ctx.fillRect(lx - R * 0.28, ly - R * 0.18, R * 0.56, R * 0.2); ctx.strokeStyle = rgba('#FFFFFF', 0.5); ctx.lineWidth = 1.2; ctx.beginPath(); ctx.ellipse(lx, ly - R * 0.18, R * 0.34, R * 0.2, 0, 0, TAU); ctx.stroke();
    const fl = 0.8 + 0.2 * Math.sin(t * 17) + 0.1 * Math.sin(t * 29), fh = R * 0.42 * fl * (p > 0.78 ? 0.2 : 1);
    ctx.save(); ctx.globalCompositeOperation = 'screen'; glowDisc(ctx, lx, ly - R * 0.5, R * 1.4, '#FFAA4A', 0.35 * fl); ctx.restore();
    ctx.fillStyle = '#FFB347'; ctx.beginPath(); ctx.moveTo(lx, ly - R * 0.38 - fh); ctx.quadraticCurveTo(lx + R * 0.14, ly - R * 0.36, lx, ly - R * 0.24); ctx.quadraticCurveTo(lx - R * 0.14, ly - R * 0.36, lx, ly - R * 0.38 - fh); ctx.fill(); ctx.fillStyle = '#FFF1C0'; ctx.beginPath(); ctx.ellipse(lx, ly - R * 0.3, R * 0.05, fh * 0.35, 0, 0, TAU); ctx.fill();
    /* steam */
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    for (let i = 0; i < 9; i++) { const age = frac(t * 0.1 + i / 9), sxm = cx + Math.sin(age * 5 + i) * topW * 0.4, sym = topY - age * U * 0.4; ctx.fillStyle = rgba('#FFFFFF', (T.dark ? 0.12 : 0.22) * Math.sin(age * Math.PI) * (0.4 + up)); dot(ctx, sxm, sym, U * (0.02 + 0.05 * age)); }
    ctx.restore();
  });

  /* ================= 水滴の輪 ================= */
  K.registerBg('echoDroplets', '水滴の輪', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `dr|${seed}|${ck(sc)}`;
    const hy = H * 0.3;
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.5, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [0.3, mix(T.bg1, T.c3, T.dark ? 0.2 : 0.2)], [1, mix(T.bg0, T.c1, T.dark ? 0.2 : 0.1)]]); x.fillRect(0, 0, W, H);
      /* reflections of coloured lights lying on the water */
      for (let i = 0; i < 16; i++) { const lx = W * r(seed, i, 1), ly = hy + (H - hy) * (0.05 + 0.9 * r(seed, i, 2)), lr = U * (0.04 + 0.08 * r(seed, i, 3)); x.save(); x.translate(lx, ly); x.scale(1, 0.35); glowDisc(x, 0, 0, lr * 1.8, [T.c1, T.c2, T.c3, T.hi][i % 4], T.dark ? 0.35 : 0.35); x.restore(); }
      x.fillStyle = rgba(T.hi, T.dark ? 0.12 : 0.3); x.fillRect(0, hy, W, Math.max(1.5, U * 0.003));
      for (let i = 0; i < 40; i++) { x.strokeStyle = rgba(T.hi, 0.04 + 0.05 * r(seed, i, 6)); x.lineWidth = 1; const ly = hy + (H - hy) * Math.pow(r(seed, i, 7), 1.5), lx = W * r(seed, i, 8), ll = U * (0.1 + 0.3 * r(seed, i, 9)) * (0.4 + (ly - hy) / (H - hy)); x.beginPath(); x.moveTo(lx, ly); x.lineTo(lx + ll, ly); x.stroke(); }
    }), 0, 0, W, H);
    const slots = portrait ? 5 : 7;
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    for (let i = 0; i < slots; i++) {
      const per = 2.4 + 0.5 * r(seed, i, 11), off = r(seed, i, 12) * per, n = Math.floor((t + off) / per), ph = frac((t + off) / per) * per, ix = W * (0.12 + 0.76 * r(seed, i, n, 13)), iy = hy + (H - hy) * (0.2 + 0.7 * r(seed, i, n, 14)), k = (iy - hy) / (H - hy), fallT = 0.5;
      const col = [T.c1, T.c2, T.c3, T.hi][(i + n) % 4];
      if (ph < fallT) { const f = ph / fallT, dy = iy - hy * (1 - f) * 1.0 - (iy - (-H * 0.05)) * (1 - f) * f * 0.0; const py = -U * 0.05 + (iy - U * 0.01 + U * 0.05) * f * f; ctx.fillStyle = rgba('#FFFFFF', 0.9); ctx.beginPath(); ctx.ellipse(ix, py, U * 0.0045, U * (0.012 + 0.02 * f), 0, 0, TAU); ctx.fill(); const g = ctx.createLinearGradient(ix, py - U * 0.12 * f, ix, py); g.addColorStop(0, rgba(col, 0)); g.addColorStop(1, rgba(col, 0.5)); ctx.fillStyle = g; ctx.fillRect(ix - U * 0.002, py - U * 0.12 * f, U * 0.004, U * 0.12 * f); continue; }
      const a = ph - fallT, rs = U * (0.05 + 0.2 * (0.5 + k)) ;
      for (let j = 0; j < 4; j++) { const d = a - j * 0.18; if (d <= 0) continue; const rr = rs * d * 0.9 * (0.4 + k) * 1.4, life = clamp(1 - d / 2.4); if (life <= 0) continue; ctx.strokeStyle = rgba(j ? col : '#FFFFFF', (T.dark ? 0.75 : 0.7) * life * (1 - j * 0.2)); ctx.lineWidth = Math.max(1, U * 0.003 * (1 - j * 0.15)); ctx.beginPath(); ctx.ellipse(ix, iy, rr, rr * 0.34, 0, 0, TAU); ctx.stroke(); }
      if (a < 0.35) { const s = a / 0.35; ctx.strokeStyle = rgba('#FFFFFF', 0.8 * (1 - s)); ctx.lineWidth = Math.max(1.2, U * 0.0035); for (let b = -2; b <= 2; b++) { ctx.beginPath(); ctx.moveTo(ix + b * U * 0.008 * (1 + s), iy); ctx.lineTo(ix + b * U * 0.014 * (1 + s * 1.5), iy - U * (0.03 * Math.sin(s * Math.PI)) * (1 - Math.abs(b) * 0.25)); ctx.stroke(); } glowDisc(ctx, ix, iy, U * 0.06, col, 0.6 * (1 - s)); }
    }
    for (let i = 0; i < 18; i++) { const age = frac(t * (0.02 + 0.03 * r(seed, i, 71)) + r(seed, i, 72)); ctx.fillStyle = rgba(T.hi, 0.5 * Math.sin(age * Math.PI)); dot(ctx, W * r(seed, i, 73), H * (1.02 - age * 1.04), U * (0.0025 + 0.004 * r(seed, i, 74))); }
    ctx.restore();
  });

  /* ================= フリップクロック ================= */
  K.registerBg('echoFlipClock', 'フリップクロック', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `fc|${seed}|${ck(sc)}`;
    const cw = U * (portrait ? 0.22 : 0.2), ch = cw * 1.32, gapIn = cw * 0.08, gapGrp = cw * 0.38, cols = 3, total = cols * (cw * 2 + gapIn) + (cols - 1) * gapGrp, cx0 = W / 2 - total / 2, cy0 = H * (portrait ? 0.56 : 0.5) - ch / 2;
    const cardC = T.dark ? mix(sc.bg, '#000000', 0.55) : mix(sc.bg, sc.fg, 0.82), cardL = mix(cardC, '#FFFFFF', 0.08), numC = T.dark ? mix(sc.fg, sc.accent, 0.1) : '#FFFFFF';
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.5, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [1, T.bg1]]); x.fillRect(0, 0, W, H);
      glowDisc(x, W / 2, cy0 + ch / 2, U * 1.1, T.c1, T.dark ? 0.2 : 0.3); glowDisc(x, W * 0.15, H * 0.2, U * 0.6, T.c2, T.dark ? 0.12 : 0.2);
      /* wall panels */
      x.strokeStyle = rgba(T.line, T.dark ? 0.12 : 0.18); x.lineWidth = 1; for (let i = -2; i < 12; i++) { const px = W / 2 + i * U * 0.34; x.beginPath(); x.moveTo(px, 0); x.lineTo(px, H); x.stroke(); }
      const sy = cy0 + ch + U * 0.05; x.fillStyle = vertical(x, sy, H, [[0, mix(T.bg1, sc.accent, T.dark ? 0.2 : 0.14)], [1, mix(T.bg0, '#000000', T.dark ? 0.55 : 0.12)]]); x.fillRect(0, sy, W, H - sy);
      x.fillStyle = rgba('#FFFFFF', 0.14); x.fillRect(0, sy, W, Math.max(1.5, U * 0.003)); x.fillStyle = rgba('#000000', T.dark ? 0.45 : 0.18); x.beginPath(); x.ellipse(W / 2, sy + U * 0.02, total * 0.55, U * 0.03, 0, 0, TAU); x.fill();
    }), 0, 0, W, H);
    const now = new Date(), sNow = now.getSeconds() + now.getMilliseconds() / 1000, dg = (v, k) => String(v).padStart(2, '0')[k];
    const tick = (n, mod) => n % mod;
    const parts = [[now.getHours(), 24, 3600 * 1000], [now.getMinutes(), 60, 60 * 1000], [Math.floor(sNow), 60, 1000]];
    const FT = 0.5;  /* a flip takes half a second */
    const card = (cx, cy, w, h, curTop, curBot, flipP, oldTxt, newTxt) => {
      const rad = w * 0.09, fs = h * 0.78;
      const half = (y0, txt, fy, sy0) => {
        ctx.save(); ctx.beginPath(); ctx.rect(cx - 1, y0, w + 2, h / 2); ctx.clip();
        ctx.fillStyle = fy ? cardL : cardC; ctx.beginPath(); if (ctx.roundRect) ctx.roundRect(cx, cy, w, h, rad); else ctx.rect(cx, cy, w, h); ctx.fill();
        ctx.fillStyle = numC; ctx.font = `bold ${fs}px ${FONT}`; ctx.textAlign = 'center'; ctx.textBaseline = 'middle'; ctx.fillText(txt, cx + w / 2, cy + h * 0.53);
        const g = ctx.createLinearGradient(0, y0, 0, y0 + h / 2); if (fy) { g.addColorStop(0, rgba('#000000', 0)); g.addColorStop(1, rgba('#000000', 0.35)); } else { g.addColorStop(0, rgba('#000000', 0.3)); g.addColorStop(1, rgba('#000000', 0)); } ctx.fillStyle = g; ctx.fillRect(cx - 1, y0, w + 2, h / 2);
        ctx.restore();
      };
      half(cy, flipP > 0 && flipP < 0.5 ? oldTxt : newTxt, true); /* static top shows new once folding passes mid */
      if (flipP >= 0 && flipP < 1) { }
      half(cy + h / 2, flipP > 0 && flipP < 0.5 ? oldTxt : newTxt, false);
      if (flipP > 0 && flipP < 1) {
        const f = flipP < 0.5 ? flipP * 2 : (flipP - 0.5) * 2;
        ctx.save(); ctx.beginPath(); ctx.rect(cx - 1, flipP < 0.5 ? cy : cy + h / 2, w + 2, h / 2); ctx.clip();
        if (flipP < 0.5) { ctx.translate(0, cy + h / 2); ctx.scale(1, 1 - f); ctx.translate(0, -(cy + h / 2)); half(cy, oldTxt, true); ctx.fillStyle = rgba('#000000', 0.4 * f); ctx.fillRect(cx, cy, w, h / 2); }
        else { ctx.translate(0, cy + h / 2); ctx.scale(1, f); ctx.translate(0, -(cy + h / 2)); half(cy + h / 2, newTxt, false); ctx.fillStyle = rgba('#000000', 0.4 * (1 - f)); ctx.fillRect(cx, cy + h / 2, w, h / 2); }
        ctx.restore();
      }
      ctx.fillStyle = rgba('#000000', 0.85); ctx.fillRect(cx - 1, cy + h / 2 - Math.max(1, h * 0.006), w + 2, Math.max(2, h * 0.012));
      ctx.fillStyle = rgba('#FFFFFF', 0.06); ctx.fillRect(cx, cy + h / 2 + h * 0.008, w, Math.max(1, h * 0.004));
    };
    let x0 = cx0;
    for (let c = 0; c < cols; c++) {
      const [v, mod, unit] = parts[c], nextAt = c === 2 ? frac(sNow) : null;
      /* how far into a flip each unit is: seconds flip every second; minutes/hours flip when they roll over */
      let fp = -1, from = v, to = v;
      if (c === 2) { const fr = frac(sNow); if (fr < FT) { fp = fr / FT; from = (v + 59) % 60; to = v; } }
      else if (c === 1) { const sec = sNow; if (sec < FT) { fp = sec / FT; from = (v + 59) % 60; to = v; } }
      else { if (now.getMinutes() === 0 && sNow < FT) { fp = sNow / FT; from = (v + 23) % 24; to = v; } }
      for (let k = 0; k < 2; k++) {
        const cxk = x0 + k * (cw + gapIn);
        ctx.save(); ctx.globalAlpha = e; card(cxk, cy0, cw, ch, 0, 0, fp, dg(from, k), dg(to, k)); ctx.restore();
        if (fp > 0 && dg(from, k) === dg(to, k)) { /* identical digit: just redraw static */ }
      }
      x0 += cw * 2 + gapIn + gapGrp;
    }
    /* colon lamps */
    for (let c = 0; c < 2; c++) { const lx = cx0 + (c + 1) * (cw * 2 + gapIn) + c * gapGrp + gapGrp / 2; ctx.fillStyle = rgba(T.hi, 0.8 * (0.5 + 0.5 * Math.round(frac(sNow)))); dot(ctx, lx, cy0 + ch * 0.38, cw * 0.035); dot(ctx, lx, cy0 + ch * 0.62, cw * 0.035); }
    glow(ctx, T, W / 2, cy0 + ch * 0.5, total * 0.6, T.c1, 0.1);
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    for (let i = 0; i < 20; i++) { const age = frac(t * (0.015 + 0.02 * r(seed, i, 71)) + r(seed, i, 72)); ctx.fillStyle = rgba(T.hi, 0.5 * Math.sin(age * Math.PI)); dot(ctx, W * r(seed, i, 73), H * (1.02 - age * 1.04), U * (0.0025 + 0.004 * r(seed, i, 74))); }
    ctx.restore();
  });

  /* ================= 万華鏡 (a stained-glass rose window, ring by ring) ================= */
  const ROSE = new Map();
  const roseRing = (env, id, R, draw) => {
    const s = env.scale || 1, key = `${id}|${Math.round(R)}|${Math.round(s * 100)}`;
    let c = ROSE.get(key); if (c) { ROSE.delete(key); ROSE.set(key, c); return c; }
    c = document.createElement('canvas'); c.width = c.height = Math.ceil(R * 2 * s); const g = c.getContext('2d'); g.scale(s, s); g.translate(R, R); draw(g); ROSE.set(key, c);
    while (ROSE.size > 24) { const k = ROSE.keys().next().value, o = ROSE.get(k); o.width = o.height = 0; ROSE.delete(k); }
    return c;
  };
  K.registerBg('echoKaleido', '万華鏡', ['pop', 'emotional', 'calm'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), key = `kd|${seed}|${ck(sc)}`;
    const cx = W * 0.5, cy = H * 0.5, R = Math.min(U * 0.5, Math.hypot(W, H) * 0.5) * 0.96;
    const plate = T.dark ? mix(sc.bg, '#000000', 0.5) : mix(sc.accent2, '#0C0C18', 0.8), gold = mix('#F2D791', T.hi, 0.18), cream = '#F8EEDA';
    const jewel = [mix(T.c1, '#000000', 0.12), mix(T.c2, '#000000', 0.12), mix(T.c3, '#000000', 0.12), mix(sc.accent, '#FFFFFF', 0.1)];
    const lw = Math.max(1.1, R * 0.0055), lwThin = Math.max(0.8, R * 0.0028);
    const petal = (g, r0, r1, wb) => { g.beginPath(); g.moveTo(-wb / 2, -r0); g.bezierCurveTo(-wb * 0.62, -(r0 + (r1 - r0) * 0.55), -wb * 0.14, -(r0 + (r1 - r0) * 0.86), 0, -r1); g.bezierCurveTo(wb * 0.14, -(r0 + (r1 - r0) * 0.86), wb * 0.62, -(r0 + (r1 - r0) * 0.55), wb / 2, -r0); g.closePath(); };
    const grad = (g, r0, r1, c0, c1) => { const gr = g.createLinearGradient(0, -r0, 0, -r1); gr.addColorStop(0, c0); gr.addColorStop(1, c1); return gr; };
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.5, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [1, T.bg1]]); x.fillRect(0, 0, W, H);
      x.strokeStyle = rgba(T.line, T.dark ? 0.2 : 0.28); x.lineWidth = 1; for (const k of [1.18, 1.34, 1.52]) { x.beginPath(); x.arc(cx, cy, R * k, 0, TAU); x.stroke(); }
      for (let i = 0; i < 48; i++) { const a = i / 48 * TAU; x.beginPath(); x.moveTo(cx + Math.cos(a) * R * 1.18, cy + Math.sin(a) * R * 1.18); x.lineTo(cx + Math.cos(a) * R * (i % 4 ? 1.22 : 1.28), cy + Math.sin(a) * R * (i % 4 ? 1.22 : 1.28)); x.stroke(); }
      x.fillStyle = plate; x.beginPath(); x.arc(cx, cy, R * 1.0, 0, TAU); x.fill();
      x.strokeStyle = gold; x.lineWidth = lw * 1.6; x.beginPath(); x.arc(cx, cy, R * 1.005, 0, TAU); x.stroke(); x.lineWidth = lwThin; x.beginPath(); x.arc(cx, cy, R * 1.04, 0, TAU); x.stroke();
    }), 0, 0, W, H);
    /* outer band: studs of coloured glass between two gold lines, turning slowly one way */
    const band = roseRing(env, `rb|${ck(sc)}`, R, (g) => {
      g.strokeStyle = gold; g.lineWidth = lwThin; for (const k of [0.985, 0.915]) { g.beginPath(); g.arc(0, 0, R * k, 0, TAU); g.stroke(); }
      for (let i = 0; i < 36; i++) { g.save(); g.rotate(i / 36 * TAU); g.fillStyle = i % 3 === 0 ? cream : jewel[i % 3 === 1 ? 0 : 1]; g.beginPath(); if (i % 3 === 0) g.arc(0, -R * 0.95, R * 0.012, 0, TAU); else { g.moveTo(0, -R * 0.972); g.lineTo(R * 0.016, -R * 0.95); g.lineTo(0, -R * 0.928); g.lineTo(-R * 0.016, -R * 0.95); g.closePath(); } g.fill(); g.restore(); }
    });
    /* ring one: twelve tall pointed arches, each with an inner arch and a stone */
    const arches = roseRing(env, `ra|${ck(sc)}`, R, (g) => {
      const N = 12, r0 = R * 0.7, r1 = R * 0.9, wb = 2 * Math.sin(Math.PI / N) * r0 * 0.98;
      for (let i = 0; i < N; i++) {
        g.save(); g.rotate(i / N * TAU);
        /* the spandrel between arches: a little triangle with hatching */
        g.fillStyle = mix(jewel[(i + 1) % 3], '#000000', 0.35); g.beginPath(); g.moveTo(-wb / 2 * 1.02, -r0); g.lineTo(wb / 2 * 1.02, -r0); g.lineTo(0, -r0 - (r1 - r0) * 0.0 + 0); g.closePath();
        g.save(); g.rotate(Math.PI / N); g.fillStyle = mix(jewel[(i + 2) % 3], '#000000', 0.45); g.beginPath(); g.moveTo(-wb * 0.2, -r0 * 1.0); g.lineTo(wb * 0.2, -r0); g.lineTo(0, -r1 * 0.97); g.closePath(); g.fill(); g.strokeStyle = rgba(gold, 0.55); g.lineWidth = lwThin; g.stroke(); g.restore();
        petal(g, r0, r1, wb); g.fillStyle = grad(g, r0, r1, mix(jewel[i % 3], '#000000', 0.5), jewel[i % 3]); g.fill(); g.strokeStyle = gold; g.lineWidth = lw; g.stroke();
        petal(g, r0 + (r1 - r0) * 0.1, r0 + (r1 - r0) * 0.8, wb * 0.58); g.fillStyle = grad(g, r0, r1, rgba('#FFFFFF', 0.04), rgba(cream, 0.28)); g.fill(); g.strokeStyle = rgba(gold, 0.8); g.lineWidth = lwThin; g.stroke();
        const sy = r0 + (r1 - r0) * 0.36, gr = g.createRadialGradient(-R * 0.004, -sy - R * 0.004, 0, 0, -sy, R * 0.032); gr.addColorStop(0, '#FFFFFF'); gr.addColorStop(0.3, mix(jewel[(i + 1) % 3], '#FFFFFF', 0.4)); gr.addColorStop(1, mix(jewel[(i + 1) % 3], '#000000', 0.25));
        g.fillStyle = gr; g.beginPath(); g.arc(0, -sy, R * 0.026, 0, TAU); g.fill(); g.strokeStyle = gold; g.lineWidth = lwThin; g.stroke();
        g.restore();
      }
      g.strokeStyle = gold; g.lineWidth = lw; g.beginPath(); g.arc(0, 0, r0, 0, TAU); g.stroke();
    });
    /* ring two: twenty-four small petals in turning colours */
    const lotus = roseRing(env, `rl|${ck(sc)}`, R, (g) => {
      const N = 24, r0 = R * 0.47, r1 = R * 0.68, wb = 2 * Math.sin(Math.PI / N) * r0 * 1.7;
      for (let pass = 0; pass < 2; pass++) for (let i = 0; i < N; i++) {
        if ((i % 2) !== pass) continue; g.save(); g.rotate(i / N * TAU + (pass ? 0 : 0)); const c = jewel[(i >> 1) % 3];
        petal(g, r0, r1 - (pass ? R * 0.025 : 0), wb * (pass ? 0.8 : 1)); g.fillStyle = grad(g, r0, r1, mix(c, '#000000', 0.45), mix(c, cream, pass ? 0.35 : 0.1)); g.fill(); g.strokeStyle = rgba(gold, 0.9); g.lineWidth = lwThin * 1.3; g.stroke();
        g.strokeStyle = rgba(cream, 0.6); g.lineWidth = lwThin; g.beginPath(); g.moveTo(0, -r0 - (r1 - r0) * 0.12); g.lineTo(0, -r1 + (r1 - r0) * 0.18); g.stroke(); g.restore();
      }
      g.strokeStyle = gold; g.lineWidth = lw; g.beginPath(); g.arc(0, 0, r0, 0, TAU); g.stroke();
    });
    /* ring three: a twelve-pointed star drawn with one thread, jewelled at the corners */
    const star = roseRing(env, `rs|${ck(sc)}`, R, (g) => {
      const N = 12, r = R * 0.44, pts = (k) => [Math.cos(k / N * TAU - Math.PI / 2) * r, Math.sin(k / N * TAU - Math.PI / 2) * r];
      g.fillStyle = rgba(mix(plate, jewel[0], 0.35), 1); g.beginPath(); g.arc(0, 0, r * 1.02, 0, TAU); g.fill();
      g.strokeStyle = rgba(gold, 0.85); g.lineWidth = lwThin * 1.2;
      for (const step of [5, 4]) { g.beginPath(); for (let i = 0; i <= N; i++) { const [x, y] = pts((i * step) % N); i ? g.lineTo(x, y) : g.moveTo(x, y); } g.stroke(); }
      g.beginPath(); for (let i = 0; i <= N; i++) { const [x, y] = pts(i % N); i ? g.lineTo(x, y) : g.moveTo(x, y); } g.stroke();
      for (let i = 0; i < N; i++) { const [x, y] = pts(i); g.fillStyle = i % 2 ? cream : jewel[3]; g.beginPath(); g.arc(x, y, R * 0.013, 0, TAU); g.fill(); g.strokeStyle = gold; g.lineWidth = lwThin; g.stroke(); }
      g.strokeStyle = gold; g.lineWidth = lw; g.beginPath(); g.arc(0, 0, r * 1.02, 0, TAU); g.stroke();
    });
    /* heart: an eight-petal flower over a jewel */
    const heart = roseRing(env, `rh|${ck(sc)}`, R, (g) => {
      const N = 8; for (let i = 0; i < N; i++) { g.save(); g.rotate(i / N * TAU); petal(g, R * 0.03, R * 0.27, R * 0.17); g.fillStyle = grad(g, R * 0.03, R * 0.27, mix(jewel[i % 2], '#000000', 0.3), mix(jewel[i % 2], cream, 0.3)); g.globalAlpha = 0.92; g.fill(); g.globalAlpha = 1; g.strokeStyle = gold; g.lineWidth = lwThin * 1.3; g.stroke(); g.restore(); }
      const gr = g.createRadialGradient(-R * 0.012, -R * 0.012, 0, 0, 0, R * 0.075); gr.addColorStop(0, '#FFFFFF'); gr.addColorStop(0.35, mix(jewel[2], '#FFFFFF', 0.35)); gr.addColorStop(1, mix(jewel[2], '#000000', 0.35));
      g.fillStyle = gr; g.beginPath(); g.arc(0, 0, R * 0.07, 0, TAU); g.fill(); g.strokeStyle = gold; g.lineWidth = lw; g.stroke();
    });
    ctx.save(); ctx.translate(cx, cy);
    for (const [cv, rot] of [[band, t * 0.012], [arches, -t * 0.02], [lotus, t * 0.032], [star, -t * 0.045], [heart, t * 0.06]]) { ctx.save(); ctx.rotate(rot); ctx.drawImage(cv, -R, -R, R * 2, R * 2); ctx.restore(); }
    /* a faint gleam crossing the glass, once in a while */
    ctx.save(); ctx.beginPath(); ctx.arc(0, 0, R * 0.99, 0, TAU); ctx.clip();
    const ga = wrap(t * 0.18, TAU), gw = 0.5; const gl = ctx.createLinearGradient(Math.cos(ga) * R * 0.2, Math.sin(ga) * R * 0.2, Math.cos(ga + 1.1) * R * 1.1, Math.sin(ga + 1.1) * R * 1.1);
    gl.addColorStop(0, rgba('#FFFFFF', 0.0)); gl.addColorStop(0.5, rgba('#FFFFFF', 0.07)); gl.addColorStop(1, rgba('#FFFFFF', 0)); ctx.fillStyle = gl; ctx.beginPath(); ctx.moveTo(0, 0); ctx.arc(0, 0, R, ga, ga + gw); ctx.closePath(); ctx.fill();
    ctx.restore(); ctx.restore();
  });

  /* ================= スピログラフ ================= */
  K.registerBg('echoSpiro', 'スピログラフ', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `sg|${seed}|${ck(sc)}`;
    const cx = W * (portrait ? 0.5 : 0.64), cy = H * 0.5, R0 = U * 0.38;
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.5, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [1, T.bg1]]); x.fillRect(0, 0, W, H);
      glowDisc(x, cx, cy, U * 0.9, T.c1, T.dark ? 0.18 : 0.26);
      /* graph paper */
      x.strokeStyle = rgba(T.line, T.dark ? 0.1 : 0.16); x.lineWidth = 1; const g = U * 0.06; for (let px = (W / 2) % g; px < W; px += g) { x.beginPath(); x.moveTo(px, 0); x.lineTo(px, H); x.stroke(); } for (let py = (H / 2) % g; py < H; py += g) { x.beginPath(); x.moveTo(0, py); x.lineTo(W, py); x.stroke(); }
      x.strokeStyle = rgba(T.line, T.dark ? 0.2 : 0.3); x.beginPath(); x.moveTo(0, cy); x.lineTo(W, cy); x.moveTo(cx, 0); x.lineTo(cx, H); x.stroke();
    }), 0, 0, W, H);
    const SETS = [[7, 3, 0.75, 3], [5, 2, 0.8, 2], [8, 5, 0.9, 5], [9, 4, 0.85, 4], [11, 4, 0.7, 4]];
    const cyc = 22, n = Math.floor(t / cyc), p = frac(t / cyc), [A, B, dd, turns] = SETS[(n + Math.floor(r(seed, 1, 1) * 5)) % SETS.length], drawP = ease(clamp(p / 0.7)), fadeOut = 1 - ease((p - 0.9) / 0.1), th1 = drawP * TAU * turns;
    const rr = R0 * B / A, d = rr * dd * 1.3, pt = (th) => [cx + (R0 - rr) * Math.cos(th) + d * Math.cos((R0 - rr) / rr * th), cy + (R0 - rr) * Math.sin(th) - d * Math.sin((R0 - rr) / rr * th)];
    const steps = Math.max(2, Math.ceil(th1 / TAU * 220));
    ctx.save(); ctx.globalAlpha = e * fadeOut; ctx.lineCap = 'round'; ctx.lineJoin = 'round';
    const segs = 8; for (let sI = 0; sI < segs; sI++) {
      const a0 = Math.floor(steps * sI / segs), a1 = Math.floor(steps * (sI + 1) / segs); if (a1 <= a0) continue;
      const col = mix(T.c1, T.c2, sI / (segs - 1)); ctx.strokeStyle = rgba(col, T.dark ? 0.95 : 0.9); ctx.lineWidth = Math.max(1.5, U * 0.0028); ctx.beginPath();
      for (let i = a0; i <= a1; i++) { const [px, py] = pt(i / steps * th1); i === a0 ? ctx.moveTo(px, py) : ctx.lineTo(px, py); } ctx.stroke();
      if (T.dark) { ctx.save(); ctx.globalCompositeOperation = 'screen'; ctx.strokeStyle = rgba(col, 0.25); ctx.lineWidth = Math.max(3, U * 0.008); ctx.stroke(); ctx.restore(); }
    }
    /* the gears: the fixed ring, the rolling disc and its pen arm */
    if (p < 0.78) {
      const ga = th1, gx = cx + (R0 - rr) * Math.cos(ga), gy = cy + (R0 - rr) * Math.sin(ga), [px, py] = pt(th1), al = 1 - ease((p - 0.7) / 0.08);
      ctx.strokeStyle = rgba(T.line, 0.55 * al); ctx.lineWidth = Math.max(1.2, U * 0.0028); ctx.beginPath(); ctx.arc(cx, cy, R0, 0, TAU); ctx.stroke();
      ctx.strokeStyle = rgba(T.c3, 0.8 * al); ctx.beginPath(); ctx.arc(gx, gy, rr, 0, TAU); ctx.stroke();
      ctx.beginPath(); ctx.moveTo(gx, gy); ctx.lineTo(px, py); ctx.stroke();
      ctx.fillStyle = rgba(T.c3, 0.9 * al); dot(ctx, gx, gy, U * 0.005);
      ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over'; ctx.globalAlpha = e * fadeOut; glowDisc(ctx, px, py, U * 0.05, '#FFFFFF', 0.8); ctx.restore();
    }
    ctx.restore();
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    for (let i = 0; i < 16; i++) { const age = frac(t * (0.02 + 0.03 * r(seed, i, 71)) + r(seed, i, 72)); ctx.fillStyle = rgba(T.hi, 0.5 * Math.sin(age * Math.PI)); dot(ctx, W * r(seed, i, 73), H * (1.02 - age * 1.04), U * (0.0025 + 0.004 * r(seed, i, 74))); }
    ctx.restore();
  });
})();
