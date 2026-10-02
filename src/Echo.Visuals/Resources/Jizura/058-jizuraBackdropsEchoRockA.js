/* ECHO first-party backdrops, "rock" tributes A: the looks Echoes (deep water, sonar) and Time (clockwork).
   Original imagery only; every colour comes from the current scheme (dark = additive light, light = inked washes). */
(() => {
  'use strict';
  const K = J.echo, TAU = K.TAU, clamp = K.clamp, mix = K.mix, rgba = K.rgba, r = J.r;
  const { U: Umin, isDark, wrap, fade, vertical, glowDisc, layer } = K;
  const ck = sc => [sc.bg, sc.fg, sc.accent, sc.accent2, sc.ghostA, sc.ghostB].join('');
  const frac = v => v - Math.floor(v);
  const ease = v => { v = clamp(v); return v * v * (3 - 2 * v); };
  const tones = sc => {
    const dark = isDark(sc.bg);
    return dark
      ? { dark, bg0: mix(sc.bg, '#000000', 0.6), bg1: mix(sc.bg, '#000000', 0.3), c1: mix(sc.accent, '#FFFFFF', 0.12), c2: mix(sc.accent2, '#FFFFFF', 0.1), c3: mix(sc.ghostA || sc.accent, '#FFFFFF', 0.25), line: mix(sc.fg, sc.bg, 0.25), hi: mix(sc.accent, '#FFFFFF', 0.6), ink: '#000000' }
      : { dark, bg0: mix(sc.bg, '#FFFFFF', 0.55), bg1: sc.bg, c1: mix(sc.accent, '#000000', 0.06), c2: mix(sc.accent2, '#000000', 0.04), c3: sc.ghostA || sc.accent, line: mix(sc.fg, sc.bg, 0.25), hi: mix(sc.accent, '#FFFFFF', 0.7), ink: mix(sc.fg, '#000000', 0.2) };
  };
  const glow = (ctx, T, x, y, rad, c, a) => { ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over'; glowDisc(ctx, x, y, rad, c, T.dark ? a : a * 0.7); ctx.restore(); };
  const dot = (ctx, x, y, rad) => { ctx.beginPath(); ctx.arc(x, y, rad, 0, TAU); ctx.fill(); };

  /* ================= 深海ソナー ================= */
  K.registerBg('echoSonar', 'ソナー', ['calm', 'emotional', 'glitch'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `so|${seed}|${ck(sc)}`;
    const ox = W * (portrait ? 0.5 : 0.56), oy = H * (portrait ? 0.46 : 0.5), R = Math.hypot(W, H) * 0.62;
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.4, (x, w, h) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.dark ? mix(sc.bg, '#000000', 0.35) : mix(sc.bg, '#FFFFFF', 0.55)], [1, T.dark ? mix(sc.bg, '#000000', 0.78) : mix(sc.bg, sc.accent2, 0.25)]]); x.fillRect(0, 0, W, H);
      /* depth scale down the left edge and a faint grid */
      x.strokeStyle = rgba(T.line, T.dark ? 0.12 : 0.18); x.lineWidth = 1; x.beginPath();
      for (let i = 1; i < 12; i++) { const y = H * i / 12; x.moveTo(0, y); x.lineTo(W, y); } x.stroke();
      x.strokeStyle = rgba(T.line, T.dark ? 0.5 : 0.5); x.beginPath();
      for (let i = 0; i <= 48; i++) { const y = H * i / 48, l = i % 4 === 0 ? U * 0.03 : U * 0.014; x.moveTo(U * 0.03, y); x.lineTo(U * 0.03 + l, y); } x.stroke();
      x.fillStyle = rgba(T.line, T.dark ? 0.6 : 0.6); x.font = `${Math.max(9, U * 0.016)}px ui-monospace,Menlo,Consolas,monospace`;
      for (let i = 0; i < 12; i += 2) x.fillText(String(i * 40).padStart(4, '0') + ' m', U * 0.07, H * i / 12 + U * 0.006);
      /* the seabed */
      x.fillStyle = T.dark ? mix(sc.bg, '#000000', 0.86) : mix(sc.bg, sc.accent2, 0.5); x.beginPath(); x.moveTo(0, H);
      for (let i = 0; i <= 40; i++) { const px = W * i / 40; x.lineTo(px, H * 0.9 - U * 0.05 * (0.5 + 0.5 * Math.sin(i * 0.5 + seed) * Math.sin(i * 0.17 + 2)) - U * 0.02 * r(seed, i, 1)); } x.lineTo(W, H); x.fill();
    }), 0, 0, W, H);
    /* hidden things out in the dark, revealed only when a ping reaches them */
    const period = 5.2, blips = [];
    for (let i = 0; i < 9; i++) { const a = r(seed, i, 11) * TAU, d = R * (0.16 + 0.55 * r(seed, i, 12)); blips.push([ox + Math.cos(a) * d * (portrait ? 0.7 : 1), oy + Math.sin(a) * d * 0.7, d]); }
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    for (let k = 0; k < 2; k++) {
      const age = frac(t / period + k * 0.5) , rad = age * R, fa = Math.pow(1 - age, 1.6);
      for (let j = 0; j < 4; j++) { /* the ping and its echoes trailing behind */
        const rr = rad - j * U * 0.055; if (rr <= 0) continue;
        ctx.strokeStyle = rgba(j ? T.c2 : T.c1, fa * (j ? 0.5 / j : 0.95)); ctx.lineWidth = Math.max(1.2, U * (j ? 0.0022 : 0.005)) ; ctx.beginPath(); ctx.ellipse(ox, oy, rr, rr * 0.7, 0, 0, TAU); ctx.stroke();
      }
      ctx.fillStyle = rgba(T.c1, fa * 0.06); ctx.beginPath(); ctx.ellipse(ox, oy, rad, rad * 0.7, 0, 0, TAU); ctx.fill();
      blips.forEach(([bx, by, d], i) => { const dd = rad - d * 1.0, w0 = U * 0.06; if (dd > -w0 * 0.1 && dd < R * 0.55) { const a = Math.exp(-dd * 0.0035) * fa * 1.4; ctx.fillStyle = rgba(T.hi, clamp(a)); dot(ctx, bx, by, U * (0.006 + 0.005 * r(seed, i, 13))); ctx.strokeStyle = rgba(T.c2, clamp(a) * 0.7); ctx.lineWidth = Math.max(1, U * 0.002); ctx.beginPath(); ctx.arc(bx, by, U * (0.02 + 0.045 * clamp(dd / (R * 0.3))), 0, TAU); ctx.stroke(); } });
    }
    glow(ctx, T, ox, oy, U * 0.16, T.hi, 0.7); ctx.fillStyle = rgba(T.dark ? '#FFFFFF' : T.ink, 0.95); dot(ctx, ox, oy, U * 0.006);
    /* plankton */
    for (let i = 0; i < 40; i++) { const age = frac(t * (0.02 + 0.03 * r(seed, i, 21)) + r(seed, i, 22)); ctx.fillStyle = rgba(T.c3, 0.55 * Math.sin(age * Math.PI)); dot(ctx, W * r(seed, i, 23) + Math.sin(t * 0.4 + i) * U * 0.02, H * (1.02 - age * 1.04), U * (0.0018 + 0.0035 * r(seed, i, 24))); }
    ctx.restore();
  });

  let CAU = null;
  /* ================= 水面の光 ================= */
  K.registerBg('echoCaustics', '水底の光', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W;
    ctx.globalAlpha = e;
    ctx.fillStyle = vertical(ctx, 0, H, [[0, T.dark ? mix(sc.accent, sc.bg, 0.5) : mix(sc.accent, '#FFFFFF', 0.5)], [0.45, T.dark ? mix(sc.bg, sc.accent, 0.15) : mix(sc.bg, '#FFFFFF', 0.4)], [1, T.dark ? mix(sc.bg, '#000000', 0.8) : mix(sc.bg, sc.accent2, 0.35)]]); ctx.fillRect(0, 0, W, H);
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    /* shafts of light from the surface */
    for (let i = 0; i < 9; i++) {
      const x0 = W * (r(seed, i, 1) * 1.2 - 0.1) + Math.sin(t * 0.13 + i) * U * 0.05, wd = U * (0.05 + 0.1 * r(seed, i, 2)), lean = (0.16 + 0.1 * r(seed, i, 3)) * H, y1 = H * (0.7 + 0.3 * r(seed, i, 4));
      const g = ctx.createLinearGradient(0, 0, 0, y1); g.addColorStop(0, rgba(T.hi, (T.dark ? 0.22 : 0.3) * (0.6 + 0.4 * Math.sin(t * 0.5 + i * 2)))); g.addColorStop(1, rgba(T.hi, 0));
      ctx.fillStyle = g; ctx.beginPath(); ctx.moveTo(x0 - wd * 0.3, 0); ctx.lineTo(x0 + wd * 0.3, 0); ctx.lineTo(x0 + wd * 1.6 + lean * 0.4, y1); ctx.lineTo(x0 - wd * 1.6 + lean * 0.4, y1); ctx.fill();
    }
    /* caustics: a small field of bright ridges, computed per frame and stretched over the screen */
    const cw = Math.round(clamp(W / 6, 80, 200)), chh = Math.max(24, Math.round(cw * H / W));
    if (!CAU || CAU.width !== cw || CAU.height !== chh) { CAU = document.createElement('canvas'); CAU.width = cw; CAU.height = chh; CAU.g = CAU.getContext('2d'); CAU.img = CAU.g.createImageData(cw, chh); }
    const cc = T.hi, hexv = parseInt(cc.slice(1, 7), 16), cr = hexv >> 16 & 255, cg = hexv >> 8 & 255, cb = hexv & 255, D = CAU.img.data, sx = 26 / cw;
    for (let y = 0; y < chh; y++) {
      const v = y * sx, dep = 1 - 0.65 * y / chh;
      for (let x = 0; x < cw; x++) {
        const u = x * sx, a = Math.sin(u * 1.3 + Math.sin(v * 1.1 + t * 0.6) * 1.5 + t * 0.3), b = Math.sin(v * 1.5 + Math.sin(u * 1.2 - t * 0.5) * 1.5 - t * 0.35), c2 = Math.sin((u + v) * 0.9 + Math.sin((u - v) * 1.3 + t * 0.4) * 1.3);
        const q = 1 - Math.min(1, Math.abs(a + b + c2) / 1.3), i = (y * cw + x) * 4; D[i] = cr; D[i + 1] = cg; D[i + 2] = cb; D[i + 3] = Math.pow(q, 3.2) * 255 * dep;
      }
    }
    CAU.g.putImageData(CAU.img, 0, 0);
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over'; ctx.globalAlpha = e * (T.dark ? 0.6 : 0.5); ctx.imageSmoothingEnabled = true; ctx.drawImage(CAU, 0, 0, W, H); ctx.restore(); ctx.globalAlpha = e;
    /* the surface seen from below, and bubbles */
    ctx.fillStyle = rgba(T.hi, T.dark ? 0.35 : 0.5); ctx.beginPath(); ctx.moveTo(0, 0); for (let k = 0; k <= 40; k++) ctx.lineTo(W * k / 40, U * 0.03 + Math.sin(k * 0.6 + t * 0.8) * U * 0.012); ctx.lineTo(W, 0); ctx.fill();
    for (let i = 0; i < 26; i++) {
      const age = frac(t * (0.03 + 0.04 * r(seed, i, 31)) + r(seed, i, 32)), x = W * r(seed, i, 33) + Math.sin(t * 0.7 + i * 3) * U * 0.02, y = H * (1.04 - age * 1.1), rad = U * (0.006 + 0.016 * r(seed, i, 34)) * (0.7 + 0.5 * age);
      ctx.strokeStyle = rgba(T.dark ? '#FFFFFF' : T.ink, 0.55 * Math.sin(Math.min(1, age * 1.4) * Math.PI)); ctx.lineWidth = Math.max(1, U * 0.0018); ctx.beginPath(); ctx.arc(x, y, rad, 0, TAU); ctx.stroke();
      ctx.fillStyle = rgba('#FFFFFF', 0.5 * Math.sin(age * Math.PI)); dot(ctx, x - rad * 0.35, y - rad * 0.35, rad * 0.25);
    }
  });

  /* ================= 夜光クラゲ ================= */
  K.registerBg('echoJelly', '夜光クラゲ', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `je|${seed}|${ck(sc)}`;
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.4, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.dark ? mix(sc.bg, '#000000', 0.4) : mix(sc.bg, '#FFFFFF', 0.6)], [1, T.dark ? mix(sc.bg, '#000000', 0.8) : mix(sc.bg, sc.accent2, 0.3)]]); x.fillRect(0, 0, W, H);
      glowDisc(x, W * 0.5, H * 0.35, U * 0.9, T.c2, T.dark ? 0.16 : 0.22);
    }), 0, 0, W, H);
    const cols = [T.c1, T.c2, T.c3];
    const jelly = (cx, cy, s, ph, col, a, k) => {
      const pulse = Math.sin(t * 0.9 + ph), sq = 1 + 0.14 * pulse, bw = s * (1 - 0.1 * pulse), bh = s * 0.78 * sq;
      ctx.save(); ctx.translate(cx, cy); ctx.rotate(Math.sin(t * 0.25 + ph) * 0.14);
      /* tentacles first, so the bell sits over them */
      ctx.lineCap = 'round';
      for (let i = 0; i < 9; i++) {
        const u = (i / 8 - 0.5) * 1.7, len = s * (1.6 + 0.9 * r(seed, k, i, 41)), tx = u * bw * 0.85;
        ctx.strokeStyle = rgba(col, a * 0.55); ctx.lineWidth = Math.max(1, s * (i % 3 ? 0.014 : 0.03)); ctx.beginPath(); ctx.moveTo(tx, 0);
        for (let j = 1; j <= 12; j++) { const q = j / 12; ctx.lineTo(tx + Math.sin(q * 5 - t * 1.5 + i + ph) * s * 0.12 * q + u * s * 0.1 * q, q * len + pulse * s * 0.1 * q); } ctx.stroke();
      }
      const g = ctx.createRadialGradient(0, -bh * 0.3, 0, 0, -bh * 0.1, bw * 1.1); g.addColorStop(0, rgba(mix(col, '#FFFFFF', 0.5), a * 0.75)); g.addColorStop(0.7, rgba(col, a * 0.32)); g.addColorStop(1, rgba(col, a * 0.5));
      ctx.fillStyle = g; ctx.beginPath(); ctx.moveTo(-bw, 0); ctx.bezierCurveTo(-bw, -bh * 1.35, bw, -bh * 1.35, bw, 0); ctx.bezierCurveTo(bw * 0.6, bh * 0.16, bw * 0.3, -bh * 0.05, 0, bh * 0.12); ctx.bezierCurveTo(-bw * 0.3, -bh * 0.05, -bw * 0.6, bh * 0.16, -bw, 0); ctx.fill();
      ctx.strokeStyle = rgba(mix(col, '#FFFFFF', 0.6), a * 0.8); ctx.lineWidth = Math.max(1, s * 0.02); ctx.stroke();
      ctx.strokeStyle = rgba('#FFFFFF', a * 0.3); ctx.lineWidth = Math.max(1, s * 0.012); for (let i = -2; i <= 2; i++) { ctx.beginPath(); ctx.moveTo(i * bw * 0.34, -bh * 0.05); ctx.quadraticCurveTo(i * bw * 0.4, -bh * 0.7, i * bw * 0.12, -bh * 1.02); ctx.stroke(); }
      ctx.restore();
    };
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    const N = portrait ? 4 : 6;
    for (let i = N - 1; i >= 0; i--) {
      const z = 0.35 + 0.65 * r(seed, i, 1), s = U * (0.05 + 0.14 * z), sp = 0.012 + 0.02 * z, age = frac(t * sp * 0.6 + r(seed, i, 2)), cx = W * (0.1 + 0.8 * r(seed, i, 3)) + Math.sin(t * 0.2 + i * 2) * U * 0.06, cy = H * (1.35 - age * 1.7);
      jelly(cx, cy, s, i * 1.9, cols[i % 3], T.dark ? 0.45 + 0.4 * z : 0.35 + 0.4 * z, i);
    }
    for (let i = 0; i < 46; i++) { const age = frac(t * (0.015 + 0.02 * r(seed, i, 51)) + r(seed, i, 52)); ctx.fillStyle = rgba(T.c3, 0.5 * Math.sin(age * Math.PI)); dot(ctx, W * r(seed, i, 53) + Math.sin(t * 0.3 + i) * U * 0.02, H * (1 - age * 1.02), U * (0.0015 + 0.003 * r(seed, i, 54))); }
    ctx.restore();
  });

  K.registerFront('echoBubbleRise', '泡', (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, U = Umin(env), seed = P.seed || 1, e = fade(env, 1.2), T = tones(sc);
    ctx.globalAlpha = e; ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    for (let i = 0; i < 16; i++) { const age = frac(t * (0.03 + 0.04 * r(seed, i, 1)) + r(seed, i, 2)), x = W * r(seed, i, 3) + Math.sin(t * 0.8 + i * 2) * U * 0.02, y = H * (1.05 - age * 1.12), rad = U * (0.005 + 0.012 * r(seed, i, 4)); ctx.strokeStyle = rgba(T.dark ? '#FFFFFF' : T.ink, 0.4 * Math.sin(age * Math.PI)); ctx.lineWidth = Math.max(1, U * 0.0016); ctx.beginPath(); ctx.arc(x, y, rad, 0, TAU); ctx.stroke(); }
    const pa = frac(t / 9 + 0.3), pr = pa * Math.hypot(W, H) * 0.7; ctx.strokeStyle = rgba(T.c1, 0.16 * (1 - pa)); ctx.lineWidth = Math.max(1.5, U * 0.004); ctx.beginPath(); ctx.ellipse(W * 0.5, H * 0.55, pr, pr * 0.7, 0, 0, TAU); ctx.stroke();
    ctx.restore();
    if (K.luxe) K.luxe(ctx, env, { seed, tint: ['#FFFFFF', sc.accent, sc.accent2], strength: 0.45, bokeh: 2, glitter: 6 });
  });

  /* ================= 歯車 ================= */
  const gearPath = (ctx, n, ro, ri, rot, hole, spokes) => {
    ctx.beginPath();
    const step = TAU / n;
    for (let i = 0; i < n; i++) { const a = rot + i * step, a1 = a + step * 0.12, a2 = a + step * 0.28, a3 = a + step * 0.52, a4 = a + step * 0.68; if (i === 0) ctx.moveTo(Math.cos(a) * ri, Math.sin(a) * ri); else ctx.lineTo(Math.cos(a) * ri, Math.sin(a) * ri); ctx.lineTo(Math.cos(a1) * ro, Math.sin(a1) * ro); ctx.lineTo(Math.cos(a3 - step * 0.1) * ro, Math.sin(a3 - step * 0.1) * ro); ctx.lineTo(Math.cos(a3 + step * 0.08) * ri, Math.sin(a3 + step * 0.08) * ri); }
    ctx.closePath();
    if (spokes) { /* windows between spokes, punched out with even-odd */
      const rin = ri * 0.28, rout = ri * 0.86, gap = 0.12;
      for (let i = 0; i < spokes; i++) { const a0 = rot + i * TAU / spokes + gap, a1 = rot + (i + 1) * TAU / spokes - gap; ctx.moveTo(Math.cos(a0) * rout, Math.sin(a0) * rout); ctx.arc(0, 0, rout, a0, a1); ctx.lineTo(Math.cos(a1) * rin, Math.sin(a1) * rin); ctx.arc(0, 0, rin, a1, a0, true); ctx.closePath(); }
    }
    if (hole) { ctx.moveTo(hole, 0); ctx.arc(0, 0, hole, 0, TAU, true); }
  };
  K.registerBg('echoClockwork', '歯車時計', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `cw|${seed}|${ck(sc)}`;
    const cx = W * (portrait ? 0.5 : 0.64), cy = H * (portrait ? 0.4 : 0.48), D = U * (portrait ? 0.4 : 0.42);
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.4, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [1, T.bg1]]); x.fillRect(0, 0, W, H);
      glowDisc(x, cx, cy, U * 0.85, T.c1, T.dark ? 0.2 : 0.3);
      /* the dial behind everything: ring, roman-ish ticks */
      x.strokeStyle = rgba(T.line, T.dark ? 0.55 : 0.6); x.lineWidth = Math.max(1.5, U * 0.004); x.beginPath(); x.arc(cx, cy, D, 0, TAU); x.stroke(); x.beginPath(); x.arc(cx, cy, D * 0.9, 0, TAU); x.stroke();
      for (let i = 0; i < 60; i++) { const a = i / 60 * TAU, big = i % 5 === 0; x.lineWidth = Math.max(1, U * (big ? 0.005 : 0.0018)); x.beginPath(); x.moveTo(cx + Math.cos(a) * D * (big ? 0.78 : 0.84), cy + Math.sin(a) * D * (big ? 0.78 : 0.84)); x.lineTo(cx + Math.cos(a) * D * 0.9, cy + Math.sin(a) * D * 0.9); x.stroke(); }
    }), 0, 0, W, H);
    /* a chain of meshing gears: same tooth speed, alternating direction */
    const spec = [[34, 0.33], [22, 0.21], [16, 0.155], [26, 0.25], [14, 0.13], [30, 0.29], [18, 0.17]];
    const mod = U * 0.0102; let px = cx - D * 1.02, py = cy + D * 0.92, ang = -0.62; const gears = [];
    spec.forEach(([n, _], i) => { const rp = n * mod; if (i) { const q = gears[i - 1]; ang += (r(seed, i, 5) - 0.42) * 0.9; px = q.x + Math.cos(ang) * (q.rp + rp); py = q.y + Math.sin(ang) * (q.rp + rp); } gears.push({ n, rp, x: px, y: py }); });
    const gc = T.dark ? mix(sc.accent, sc.bg, 0.25) : mix(sc.accent, sc.fg, 0.2);
    const speed = 0.16;
    gears.forEach((g, i) => {
      const dir = i % 2 ? -1 : 1, rot = dir * t * speed * 30 / g.n + (g.n % 2 ? Math.PI / g.n : 0), ro = g.rp + mod * 1.0, ri = g.rp - mod * 1.15;
      ctx.save(); ctx.translate(g.x, g.y);
      gearPath(ctx, g.n, ro, ri, rot, ri * 0.11, g.n > 15 ? 5 : 0);
      const gr = ctx.createLinearGradient(-ro, -ro, ro, ro); gr.addColorStop(0, rgba(mix(gc, '#FFFFFF', 0.35), T.dark ? 0.5 : 0.75)); gr.addColorStop(1, rgba(mix(gc, T.dark ? '#000000' : sc.fg, 0.35), T.dark ? 0.55 : 0.7));
      ctx.fillStyle = gr; ctx.fill('evenodd'); ctx.lineWidth = Math.max(1.2, U * 0.003); ctx.strokeStyle = rgba(T.dark ? mix(gc, '#FFFFFF', 0.5) : T.ink, 0.85); ctx.stroke();
      ctx.beginPath(); ctx.arc(0, 0, ri * 0.2, 0, TAU); ctx.fillStyle = rgba(T.dark ? T.bg0 : '#FFFFFF', 0.9); ctx.fill(); ctx.stroke();
      ctx.restore();
    });
    /* second hand, ticking in steps with a little overshoot, and the pendulum */
    const s = Math.floor(t), fr = t - s, tick = fr < 0.25 ? (1 - Math.pow(1 - fr / 0.25, 3)) + 0.06 * Math.sin(fr / 0.25 * Math.PI) : 1, sa = (s + tick) / 60 * TAU - Math.PI / 2;
    ctx.save(); ctx.translate(cx, cy); ctx.lineCap = 'round';
    const hand = (a, len, w, col, al) => { ctx.strokeStyle = rgba(col, al); ctx.lineWidth = w; ctx.beginPath(); ctx.moveTo(-Math.cos(a) * len * 0.12, -Math.sin(a) * len * 0.12); ctx.lineTo(Math.cos(a) * len, Math.sin(a) * len); ctx.stroke(); };
    hand(t / 3600 * TAU * 6 - Math.PI / 2 + 1.2, D * 0.5, Math.max(3, U * 0.011), T.dark ? T.hi : T.ink, 0.9); hand(t / 600 * TAU - Math.PI / 2 - 0.3, D * 0.74, Math.max(2, U * 0.007), T.dark ? T.hi : T.ink, 0.9);
    glow(ctx, T, Math.cos(sa) * D * 0.86, Math.sin(sa) * D * 0.86, U * 0.07, T.c2, 0.5); hand(sa, D * 0.86, Math.max(1.5, U * 0.003), T.c2, 1);
    ctx.fillStyle = rgba(T.c2, 1); dot(ctx, 0, 0, U * 0.011); ctx.restore();
    const pa = Math.sin(t * 1.9) * 0.34, pl = U * 0.55, pbx = W * (portrait ? 0.22 : 0.16), pby = -U * 0.04;
    ctx.strokeStyle = rgba(T.line, 0.8); ctx.lineWidth = Math.max(1.5, U * 0.004); ctx.beginPath(); ctx.moveTo(pbx, pby); ctx.lineTo(pbx + Math.sin(pa) * pl, pby + Math.cos(pa) * pl); ctx.stroke();
    glow(ctx, T, pbx + Math.sin(pa) * pl, pby + Math.cos(pa) * pl, U * 0.1, T.c1, 0.5); ctx.fillStyle = rgba(gc, 0.95); dot(ctx, pbx + Math.sin(pa) * pl, pby + Math.cos(pa) * pl, U * 0.028);
    for (let i = 0; i < 24; i++) { const age = frac(t * (0.02 + 0.03 * r(seed, i, 61)) + r(seed, i, 62)); ctx.fillStyle = rgba(T.hi, 0.5 * Math.sin(age * Math.PI)); dot(ctx, W * r(seed, i, 63) + Math.sin(t * 0.3 + i) * U * 0.02, H * (1.02 - age * 1.04), U * (0.0015 + 0.003 * r(seed, i, 64))); }
  });

  /* ================= 時計の壁 ================= */
  K.registerBg('echoClockWall', '時計の壁', ['calm', 'glitch', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `cl|${seed}|${ck(sc)}`;
    const cols = portrait ? 3 : 6, rows = portrait ? 5 : 3, cw = W / cols, ch = H / rows, R = Math.min(cw, ch) * 0.4;
    const pos = (i) => { const c = i % cols, rr = Math.floor(i / cols); return [cw * (c + 0.5) + (r(seed, i, 1) - 0.5) * cw * 0.12, ch * (rr + 0.5) + (r(seed, i, 2) - 0.5) * ch * 0.12]; };
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.5, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [1, T.bg1]]); x.fillRect(0, 0, W, H);
      x.fillStyle = rgba(T.line, T.dark ? 0.05 : 0.07); for (let i = 0; i < 40; i++) x.fillRect(W * i / 40, 0, W / 80, H);
      for (let i = 0; i < cols * rows; i++) {
        const [px, py] = pos(i), rr = R * (0.86 + 0.14 * r(seed, i, 3));
        x.fillStyle = rgba(T.dark ? T.bg0 : '#FFFFFF', T.dark ? 0.75 : 0.65); x.beginPath(); x.arc(px, py, rr, 0, TAU); x.fill();
        x.strokeStyle = rgba(i % 2 ? T.c1 : T.c2, T.dark ? 0.75 : 0.8); x.lineWidth = Math.max(2, U * 0.007); x.stroke();
        x.strokeStyle = rgba(T.line, 0.8); x.lineWidth = Math.max(1, U * 0.0018);
        x.beginPath(); for (let k = 0; k < 12; k++) { const a = k / 12 * TAU; x.moveTo(px + Math.cos(a) * rr * 0.8, py + Math.sin(a) * rr * 0.8); x.lineTo(px + Math.cos(a) * rr * 0.93, py + Math.sin(a) * rr * 0.93); } x.stroke();
      }
    }), 0, 0, W, H);
    ctx.save(); ctx.lineCap = 'round';
    for (let i = 0; i < cols * rows; i++) {
      const [px, py] = pos(i), rr = R * (0.86 + 0.14 * r(seed, i, 3)), spd = 0.6 + 6 * Math.pow(r(seed, i, 4), 2), base = r(seed, i, 5) * TAU, dir = r(seed, i, 6) < 0.15 ? -1 : 1;
      const am = base + dir * t * spd * 0.1, ah = base * 0.7 + dir * t * spd * 0.1 / 12;
      ctx.strokeStyle = rgba(T.dark ? T.hi : T.ink, 0.95); ctx.lineWidth = Math.max(2, U * 0.0055); ctx.beginPath(); ctx.moveTo(px, py); ctx.lineTo(px + Math.sin(ah) * rr * 0.5, py - Math.cos(ah) * rr * 0.5); ctx.stroke();
      ctx.lineWidth = Math.max(1.5, U * 0.0035); ctx.beginPath(); ctx.moveTo(px, py); ctx.lineTo(px + Math.sin(am) * rr * 0.76, py - Math.cos(am) * rr * 0.76); ctx.stroke();
      ctx.fillStyle = rgba(i % 2 ? T.c1 : T.c2, 1); dot(ctx, px, py, U * 0.005);
    }
    /* somewhere on the wall a bell rings: a pulse leaves one clock, and its neighbours flare */
    ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    const slot = Math.floor(t / 3.2), age = frac(t / 3.2), ci = Math.floor(r(seed, slot, 9) * cols * rows), [bx, by] = pos(ci);
    for (let j = 0; j < 3; j++) { const a2 = clamp(age * 1.3 - j * 0.12); if (a2 <= 0 || a2 >= 1) continue; ctx.strokeStyle = rgba(T.c1, 0.7 * (1 - a2)); ctx.lineWidth = Math.max(1.5, U * 0.004); ctx.beginPath(); ctx.arc(bx, by, R * (0.9 + a2 * 3.2), 0, TAU); ctx.stroke(); }
    glow(ctx, T, bx, by, R * 1.4, T.c1, 0.5 * Math.pow(1 - age, 2));
    ctx.restore();
  });

  /* ================= 砂時計 ================= */
  K.registerBg('echoHourglass', '砂時計', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `hg|${seed}|${ck(sc)}`;
    const cx = W * (portrait ? 0.5 : 0.62), cy = H * 0.5, HH = U * (portrait ? 0.62 : 0.72), HW = HH * 0.28, neck = HW * 0.07;
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.4, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [1, T.bg1]]); x.fillRect(0, 0, W, H);
      glowDisc(x, cx, cy, U * 0.9, T.c1, T.dark ? 0.2 : 0.3);
      x.strokeStyle = rgba(T.line, T.dark ? 0.25 : 0.3); x.lineWidth = 1; for (let i = 1; i <= 5; i++) { x.beginPath(); x.arc(cx, cy, HH * (0.5 + i * 0.28), 0, TAU); x.stroke(); }
      /* a clock face behind it: twelve ticks on the outer ring */
      for (let i = 0; i < 60; i++) { const a = i / 60 * TAU, big = i % 5 === 0, R0 = HH * 1.06, R1 = R0 + HH * (big ? 0.07 : 0.03); x.strokeStyle = rgba(T.line, (T.dark ? 0.32 : 0.4) * (big ? 1 : 0.55)); x.lineWidth = big ? Math.max(1.5, U * 0.003) : 1; x.beginPath(); x.moveTo(cx + Math.cos(a) * R0, cy + Math.sin(a) * R0); x.lineTo(cx + Math.cos(a) * R1, cy + Math.sin(a) * R1); x.stroke(); }
      /* window light falling in a slanted shaft */
      for (const [w0, al] of [[1, 0.05], [0.62, 0.06], [0.3, 0.07]]) { const g = x.createLinearGradient(0, 0, W * 0.4, H); g.addColorStop(0, rgba(T.hi, (T.dark ? 1 : 0.8) * al)); g.addColorStop(1, rgba(T.hi, 0)); x.fillStyle = g; x.beginPath(); x.moveTo(cx - U * 1.5 * w0, -10); x.lineTo(cx - U * 0.4 * w0, -10); x.lineTo(cx + U * 0.7 + U * 0.4 * w0, H); x.lineTo(cx - U * 0.2 - U * 0.4 * w0, H); x.closePath(); x.fill(); }
      /* the tabletop it stands on, with a soft pool of shadow */
      const ty = cy + HH * 0.5 + U * 0.03;
      x.fillStyle = vertical(x, ty, H, [[0, T.dark ? mix(T.bg1, sc.accent, 0.22) : mix(T.bg1, sc.fg, 0.1)], [0.35, T.dark ? mix(T.bg0, sc.accent, 0.1) : mix(T.bg1, sc.fg, 0.2)], [1, mix(T.bg0, '#000000', T.dark ? 0.5 : 0.1)]]); x.fillRect(0, ty, W, H - ty);
      x.fillStyle = rgba(T.hi, T.dark ? 0.22 : 0.4); x.fillRect(0, ty, W, Math.max(1.5, U * 0.003));
      x.fillStyle = rgba('#000000', T.dark ? 0.45 : 0.2); x.beginPath(); x.ellipse(cx, ty + U * 0.012, HW * 1.9, U * 0.03, 0, 0, TAU); x.fill();
      glowDisc(x, cx, ty + U * 0.01, HW * 2.8, T.c1, T.dark ? 0.2 : 0.25);
    }), 0, 0, W, H);
    const cyc = 26, p = frac(t / cyc), flipStart = 0.93, fill = clamp(p / flipStart), flip = ease((p - flipStart) / (1 - flipStart)), rot = Math.PI * flip;
    const half = HH * 0.5, prof = (v) => neck + (HW - neck) * Math.pow(Math.sin(Math.PI * clamp(v)), 0.75);
    ctx.save(); ctx.translate(cx, cy); ctx.rotate(rot);
    const bulb = (sgn) => { ctx.beginPath(); for (let i = 0; i <= 40; i++) { const v = 1 - i / 40, y = sgn * v * half; ctx.lineTo(prof(v) * (i ? 1 : 1), y); } for (let i = 40; i >= 0; i--) { const v = 1 - i / 40, y = sgn * v * half; ctx.lineTo(-prof(v), y); } ctx.closePath(); };
    const wood0 = T.dark ? mix(sc.accent, '#000000', 0.5) : mix(sc.accent, sc.fg, 0.45), sandC = T.dark ? mix(sc.accent, '#FFFFFF', 0.15) : mix(sc.accent, sc.fg, 0.1), sandD = mix(sandC, T.dark ? '#000000' : sc.fg, 0.35);
    /* turned posts either side, behind the glass */
    for (const sx of [-1, 1]) { const px = sx * HW * 1.02, pg = ctx.createLinearGradient(px - U * 0.008, 0, px + U * 0.008, 0); pg.addColorStop(0, mix(wood0, '#000000', 0.25)); pg.addColorStop(0.45, mix(wood0, '#FFFFFF', 0.28)); pg.addColorStop(1, mix(wood0, '#000000', 0.35)); ctx.fillStyle = pg; ctx.fillRect(px - U * 0.008, -half, U * 0.016, half * 2); for (const yy of [-0.55, 0, 0.55]) { ctx.fillStyle = mix(wood0, '#FFFFFF', 0.2); ctx.beginPath(); ctx.ellipse(px, yy * half, U * 0.011, U * 0.006, 0, 0, TAU); ctx.fill(); } }
    /* sand in the top bulb (surface dips toward the neck) and the growing mound below */
    const botH = half * 0.8 * fill;
    ctx.save(); bulb(-1); ctx.clip();
    /* the pile: fills from the neck up to a level, surface slightly scooped */
    const level = -(half * 0.1 + (half * 0.82) * (1 - fill));
    const sg = ctx.createLinearGradient(0, -half, 0, 0); sg.addColorStop(0, rgba(sandC, 0.95)); sg.addColorStop(1, rgba(sandD, 0.95)); ctx.fillStyle = sg;
    ctx.beginPath(); ctx.moveTo(-HW * 1.1, 0); ctx.lineTo(-HW * 1.1, level); ctx.quadraticCurveTo(0, level + half * 0.12 * (1 - fill * 0.5), HW * 1.1, level); ctx.lineTo(HW * 1.1, 0); ctx.closePath(); if (fill < 0.985) ctx.fill();
    for (let i = 0; i < 70; i++) { const gx = (r(seed, i, 81) - 0.5) * HW * 2.1, gy = -half * r(seed, i, 82); if (gy < level) continue; ctx.fillStyle = rgba(i % 2 ? mix(sandC, '#FFFFFF', 0.5) : sandD, 0.5); dot(ctx, gx, gy, Math.max(0.8, U * 0.0016)); }
    ctx.restore();
    ctx.save(); bulb(1); ctx.clip();
    const mound = half * 0.05 + botH; ctx.fillStyle = rgba(sandC, 0.95);
    ctx.beginPath(); ctx.moveTo(-HW * 1.1, half); ctx.lineTo(-HW * 1.1, half - mound * 0.45); ctx.quadraticCurveTo(-HW * 0.25, half - mound * 0.55, 0, half - mound); ctx.quadraticCurveTo(HW * 0.25, half - mound * 0.55, HW * 1.1, half - mound * 0.45); ctx.lineTo(HW * 1.1, half); ctx.closePath(); ctx.fill();
    ctx.restore();
    /* falling stream: thin line plus grains */
    if (fill < 0.985 && flip <= 0) {
      ctx.strokeStyle = rgba(sandC, 0.95); ctx.lineWidth = Math.max(1.5, U * 0.004); ctx.beginPath(); ctx.moveTo(0, -half * 0.06); ctx.lineTo(0, half - mound); ctx.stroke();
      ctx.fillStyle = rgba(T.hi, 0.9); for (let i = 0; i < 16; i++) { const a = frac(t * 1.7 + i / 16); dot(ctx, Math.sin(i * 7.3) * HW * 0.035, -half * 0.05 + (half * 1.02 - mound) * a, Math.max(1, U * 0.0025)); }
    }
    /* glass outline, highlights and the two turned-wood plates */
    { const gg = ctx.createLinearGradient(-HW, 0, HW, 0); gg.addColorStop(0, rgba('#FFFFFF', T.dark ? 0.16 : 0.3)); gg.addColorStop(0.35, rgba('#FFFFFF', T.dark ? 0.02 : 0.08)); gg.addColorStop(0.8, rgba('#FFFFFF', T.dark ? 0.05 : 0.14)); gg.addColorStop(1, rgba('#FFFFFF', T.dark ? 0.18 : 0.32)); ctx.fillStyle = gg; bulb(-1); ctx.fill(); bulb(1); ctx.fill(); }
    ctx.strokeStyle = rgba(T.dark ? '#FFFFFF' : T.ink, T.dark ? 0.75 : 0.7); ctx.lineWidth = Math.max(1.5, U * 0.0035); bulb(-1); ctx.stroke(); bulb(1); ctx.stroke();
    ctx.strokeStyle = rgba('#FFFFFF', T.dark ? 0.4 : 0.6); ctx.lineWidth = Math.max(1.5, U * 0.004); ctx.beginPath(); for (let i = 6; i <= 30; i++) { const v = i / 40, y = -v * half; i === 6 ? ctx.moveTo(-prof(v) * 0.82, y) : ctx.lineTo(-prof(v) * 0.82, y); } ctx.stroke();
    ctx.beginPath(); for (let i = 6; i <= 30; i++) { const v = i / 40, y = v * half; i === 6 ? ctx.moveTo(-prof(v) * 0.82, y) : ctx.lineTo(-prof(v) * 0.82, y); } ctx.stroke();
    const wood = T.dark ? mix(sc.accent, '#000000', 0.55) : mix(sc.accent, sc.fg, 0.5);
    for (const sy of [-1, 1]) { const py = sy * (half + U * 0.012), wg = ctx.createLinearGradient(0, py - U * 0.014, 0, py + U * 0.014); wg.addColorStop(0, mix(wood, '#FFFFFF', 0.3)); wg.addColorStop(0.5, wood); wg.addColorStop(1, mix(wood, '#000000', 0.35)); ctx.fillStyle = wg; ctx.fillRect(-HW * 1.12, py - U * 0.014, HW * 2.24, U * 0.028); ctx.fillStyle = rgba('#FFFFFF', 0.18); ctx.fillRect(-HW * 1.12, py - U * 0.014, HW * 2.24, Math.max(1, U * 0.003)); ctx.strokeStyle = rgba(T.dark ? mix(sc.accent, '#FFFFFF', 0.4) : T.ink, 0.9); ctx.lineWidth = Math.max(1, U * 0.002); ctx.strokeRect(-HW * 1.12, py - U * 0.014, HW * 2.24, U * 0.028); }
    ctx.restore();
    /* dust of the hours drifting out of the glass */
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    for (let i = 0; i < 40; i++) { const age = frac(t * (0.03 + 0.04 * r(seed, i, 71)) + r(seed, i, 72)), a = r(seed, i, 73) * TAU, d = HH * (0.5 + 0.9 * age); ctx.fillStyle = rgba(T.hi, 0.6 * Math.sin(age * Math.PI)); dot(ctx, cx + Math.cos(a) * d * 0.9, cy + Math.sin(a) * d, U * (0.0018 + 0.003 * r(seed, i, 74))); }
    ctx.restore();
  });

  K.registerFront('echoDustMotes', '塵', (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, U = Umin(env), seed = P.seed || 1, e = fade(env, 1.2), T = tones(sc);
    ctx.globalAlpha = e; ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    for (let i = 0; i < 26; i++) { const age = frac(t * (0.012 + 0.02 * r(seed, i, 1)) + r(seed, i, 2)), x = W * r(seed, i, 3) + Math.sin(t * 0.25 + i * 3) * U * 0.04, y = H * (0.1 + 0.8 * r(seed, i, 4)) - age * U * 0.08; ctx.fillStyle = rgba(T.hi, (T.dark ? 0.55 : 0.5) * Math.sin(age * Math.PI)); dot(ctx, x, y, U * (0.0016 + 0.003 * r(seed, i, 5))); }
    const ph = frac(t); ctx.strokeStyle = rgba(T.c1, 0.18 * (1 - ph)); ctx.lineWidth = Math.max(1, U * 0.003); ctx.beginPath(); ctx.arc(W * 0.5, H * 0.5, U * (0.3 + ph * 0.08), 0, TAU); ctx.stroke();
    ctx.restore();
    if (K.luxe) K.luxe(ctx, env, { seed, tint: ['#FFFFFF', sc.accent, sc.accent2], strength: 0.4, bokeh: 2, glitter: 8 });
  });
})();
