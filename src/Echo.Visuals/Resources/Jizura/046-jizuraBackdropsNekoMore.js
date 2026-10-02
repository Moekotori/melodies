/* ECHO first-party backdrops, more ねこ scenes: 猫の夜市 (a lantern street with cat-face lanterns) and 猫とお花見 (cherry blossoms over a river).
   Static art lives in cached layers; lantern glow, steam, petals and the cats themselves are redrawn every frame. */
(() => {
  'use strict';
  const K = J.echo, TAU = K.TAU, clamp = K.clamp, mix = K.mix, rgba = K.rgba;
  const { U: Umin, isDark, wrap, fade, vertical, glowDisc, star, layer, shapes } = K;
  const { heart, petal, sakura, sparkle, roundRect, cat, FUR, tonefor, canopy } = shapes;
  const colourKey = sc => [sc.bg, sc.fg, sc.accent, sc.accent2, sc.ghostA, sc.ghostB].join('');
  const r = J.r;

  /* a round paper lantern; `face` gives it cat ears and a smile */
  const lantern = (x, cx, cy, r, c, face, glow) => {
    x.strokeStyle = rgba('#5A3A3A', 0.7); x.lineWidth = Math.max(1, r * 0.08); x.beginPath(); x.moveTo(cx, cy - r * 1.25); x.lineTo(cx, cy - r * 0.9); x.stroke();
    if (face) { x.fillStyle = c; for (const d of [-1, 1]) { x.beginPath(); x.moveTo(cx + d * r * 0.95, cy - r * 0.45); x.lineTo(cx + d * r * 0.8, cy - r * 1.18); x.lineTo(cx + d * r * 0.3, cy - r * 0.86); x.fill(); } }
    const g = x.createRadialGradient(cx - r * 0.2, cy - r * 0.2, r * 0.1, cx, cy, r * 1.05); g.addColorStop(0, mix(c, '#FFFFFF', 0.55 * glow + 0.15)); g.addColorStop(1, c); x.fillStyle = g; x.beginPath(); x.ellipse(cx, cy, r * 0.94, r, 0, 0, TAU); x.fill();
    x.fillStyle = '#4A2E2E'; x.fillRect(cx - r * 0.34, cy - r * 1.02, r * 0.68, r * 0.13); x.fillRect(cx - r * 0.3, cy + r * 0.9, r * 0.6, r * 0.12);
    x.strokeStyle = rgba('#7A2A2A', 0.28); x.lineWidth = Math.max(1, r * 0.04); for (const k of [-0.5, 0, 0.5]) { x.beginPath(); x.ellipse(cx, cy, r * 0.94 * Math.abs(k || 0.02) + 0.01, r, 0, 0, TAU); x.stroke(); }
    if (face) {
      x.fillStyle = '#4A2E2E'; for (const d of [-1, 1]) { x.beginPath(); x.ellipse(cx + d * r * 0.36, cy - r * 0.08, r * 0.09, r * 0.13, 0, 0, TAU); x.fill(); }
      x.strokeStyle = '#4A2E2E'; x.lineWidth = Math.max(1, r * 0.06); x.lineCap = 'round'; x.beginPath(); x.moveTo(cx, cy + r * 0.12); x.quadraticCurveTo(cx - r * 0.1, cy + r * 0.26, cx - r * 0.22, cy + r * 0.2); x.moveTo(cx, cy + r * 0.12); x.quadraticCurveTo(cx + r * 0.1, cy + r * 0.26, cx + r * 0.22, cy + r * 0.2); x.stroke();
      x.fillStyle = rgba('#FF7A90', 0.5); for (const d of [-1, 1]) { x.beginPath(); x.ellipse(cx + d * r * 0.58, cy + r * 0.18, r * 0.14, r * 0.08, 0, 0, TAU); x.fill(); }
    }
  };
  const taiyaki = (x, cx, cy, s) => {
    x.fillStyle = '#D9913E'; x.beginPath(); x.ellipse(cx, cy, s, s * 0.5, -0.1, 0, TAU); x.fill(); x.beginPath(); x.moveTo(cx + s * 0.85, cy - s * 0.1); x.lineTo(cx + s * 1.35, cy - s * 0.42); x.lineTo(cx + s * 1.3, cy + s * 0.32); x.fill();
    x.fillStyle = '#F0B764'; x.beginPath(); x.ellipse(cx - s * 0.05, cy - s * 0.1, s * 0.82, s * 0.32, -0.1, 0, TAU); x.fill();
    x.fillStyle = '#4A2E2E'; x.beginPath(); x.arc(cx - s * 0.6, cy - s * 0.1, s * 0.07, 0, TAU); x.fill();
    x.strokeStyle = rgba('#8A5A20', 0.6); x.lineWidth = Math.max(1, s * 0.05); for (let i = 0; i < 3; i++) { x.beginPath(); x.arc(cx + s * (0.05 + i * 0.22), cy, s * 0.24, -1, 1); x.stroke(); }
  };
  const dango = (x, cx, cy, s, cols) => {
    x.strokeStyle = '#B98A55'; x.lineWidth = Math.max(1, s * 0.08); x.beginPath(); x.moveTo(cx, cy + s * 0.9); x.lineTo(cx, cy - s * 3.6); x.stroke();
    cols.forEach((c, i) => { x.fillStyle = c; x.beginPath(); x.arc(cx, cy - s * (0.3 + i * 1.15), s * 0.56, 0, TAU); x.fill(); x.fillStyle = rgba('#FFFFFF', 0.45); x.beginPath(); x.arc(cx - s * 0.18, cy - s * (0.5 + i * 1.15), s * 0.18, 0, TAU); x.fill(); });
  };
  const goldfish = (x, cx, cy, s, c) => {
    x.fillStyle = c; x.beginPath(); x.ellipse(cx, cy, s, s * 0.55, 0, 0, TAU); x.fill();
    x.beginPath(); x.moveTo(cx + s * 0.8, cy); x.quadraticCurveTo(cx + s * 1.6, cy - s * 0.7, cx + s * 1.5, cy); x.quadraticCurveTo(cx + s * 1.6, cy + s * 0.7, cx + s * 0.8, cy); x.fill();
    x.fillStyle = '#FFFFFF'; x.beginPath(); x.arc(cx - s * 0.55, cy - s * 0.1, s * 0.16, 0, TAU); x.fill(); x.fillStyle = '#4A2E2E'; x.beginPath(); x.arc(cx - s * 0.58, cy - s * 0.1, s * 0.08, 0, TAU); x.fill();
  };
  /* a little stall: striped awning, counter, and a hanging noren curtain */
  const stall = (x, cx, base, w, h, sc, stripe, tone) => {
    const wood = tone('#9A6A4A'), top = base - h;
    x.fillStyle = tone('#5C3E3E'); x.fillRect(cx - w * 0.46, top + h * 0.15, w * 0.05, h * 0.85); x.fillRect(cx + w * 0.41, top + h * 0.15, w * 0.05, h * 0.85);
    x.fillStyle = tone(mix(sc.bg, '#3A2848', 0.5)); x.fillRect(cx - w * 0.44, top + h * 0.2, w * 0.88, h * 0.5);         /* dim interior */
    const n = 8, aw = w / n; for (let i = 0; i < n; i++) { x.fillStyle = tone(i % 2 ? '#FFF6EA' : stripe); x.beginPath(); x.moveTo(cx - w / 2 + i * aw, top); x.lineTo(cx - w / 2 + (i + 1) * aw, top); x.lineTo(cx - w / 2 + (i + 1) * aw + w * 0.02, top + h * 0.22); x.lineTo(cx - w / 2 + i * aw - w * 0.02, top + h * 0.22); x.fill(); x.beginPath(); x.arc(cx - w / 2 + (i + 0.5) * aw, top + h * 0.22, aw * 0.5, 0, Math.PI); x.fill(); }
    x.fillStyle = wood; x.fillRect(cx - w * 0.48, base - h * 0.32, w * 0.96, h * 0.32); x.fillStyle = rgba('#FFFFFF', 0.16); x.fillRect(cx - w * 0.48, base - h * 0.32, w * 0.96, h * 0.04);
    x.fillStyle = tone(mix('#7A4A32', '#000000', 0.15)); x.fillRect(cx - w * 0.48, base - h * 0.26, w * 0.96, h * 0.02);
  };

  /* a heavy cluster of blossom in three tones, with pale flecks: reads as a real tree crown rather than flat discs */
  const blossomMass = (x, cx, cy, size, sc, tone, seed, id) => {
    const base = mix('#FFD3E1', sc.accent, 0.18);
    [[0, mix(base, '#B85A8A', 0.42), 18, 1], [1, base, 24, 0.82], [2, mix(base, '#FFFFFF', 0.6), 16, 0.55]].forEach(([lk, col, n, rk]) => {
      for (let k = 0; k < n; k++) {
        const a = r(seed, id, k, lk * 7 + 1) * TAU, rr = Math.sqrt(r(seed, id, k, lk * 7 + 2)) * size * 0.42 * rk, px = cx + Math.cos(a) * rr * 1.35, py = cy + Math.sin(a) * rr * 0.8 - lk * size * 0.035, rad = size * (0.08 + 0.08 * r(seed, id, k, lk * 7 + 3));
        x.fillStyle = tone(col); x.beginPath(); x.arc(px, py, rad, 0, TAU); x.fill();
        if (lk === 2) { x.fillStyle = rgba('#FFFFFF', 0.3); x.beginPath(); x.arc(px - rad * 0.25, py - rad * 0.3, rad * 0.5, 0, TAU); x.fill(); }
      }
    });
    for (let k = 0; k < 34; k++) { const a = r(seed, id, k, 41) * TAU, rr = Math.sqrt(r(seed, id, k, 42)) * size * 0.5, px = cx + Math.cos(a) * rr * 1.35, py = cy + Math.sin(a) * rr * 0.82; x.fillStyle = rgba(tone(k % 2 ? '#FFFFFF' : '#C8547F'), k % 2 ? 0.75 : 0.5); x.beginPath(); x.arc(px, py, size * (0.008 + 0.01 * r(seed, id, k, 43)), 0, TAU); x.fill(); }
  };

  /* ================= 猫の夜市 ================= */
  K.registerBg('echoNekoMarket', '猫の夜市', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), dark = isDark(sc.bg), portrait = H > W, tone = tonefor(sc, dark);
    const key = `market|${seed}|${colourKey(sc)}`, y0 = H * 0.66, red = tone('#F0584E');
    const nStall = portrait ? 2 : 3, sw = (portrait ? W * 0.5 : Math.min(W * 0.27, U * 0.6)), sh = sw * 0.72;
    const stallX = i => W * (portrait ? 0.27 + i * 0.46 : 0.17 + i * 0.33);
    ctx.globalAlpha = e;
    const scene = layer(env, `${key}|scene`, 0.6, (x, w, h) => {
      x.fillStyle = vertical(x, 0, y0, [[0, dark ? mix(sc.bg, '#1A1240', 0.55) : mix(sc.bg, '#FFB59A', 0.45)], [0.7, dark ? mix(sc.bg, sc.accent, 0.22) : mix(sc.bg, '#FFDDB8', 0.55)], [1, dark ? mix(sc.bg, '#7A3A78', 0.35) : mix(sc.bg, '#FFF0D0', 0.6)]]); x.fillRect(0, 0, w, y0 + 2);
      if (dark) for (let i = 0; i < 46; i++) { x.fillStyle = rgba('#FFFFFF', 0.25 + 0.5 * J.r(seed, i, 3)); x.beginPath(); x.arc(J.r(seed, i, 1) * w, J.r(seed, i, 2) * y0 * 0.55, U * 0.0018 * (1 + 2 * J.r(seed, i, 4)), 0, TAU); x.fill(); }
      const mx = w * 0.78, my = y0 * 0.24; glowDisc(x, mx, my, U * 0.4, dark ? '#FFF0C0' : '#FFFFFF', dark ? 0.3 : 0.5); x.fillStyle = dark ? '#FFF6D8' : '#FFFBEA'; x.beginPath(); x.arc(mx, my, U * 0.05, 0, TAU); x.fill();
      /* rooftops: two layers, the near one with warm windows */
      for (let L = 0; L < 2; L++) {
        const base = y0 - U * (0.02 + 0.03 * (1 - L)), col = tone(mix(sc.bg, dark ? '#20163C' : '#8A6A90', L ? 0.62 : 0.4)); x.fillStyle = col;
        for (let i = 0, px = -U * 0.05; px < w + U * 0.2; i++) { const bw = U * (0.14 + 0.1 * J.r(seed, L, i, 5)), bh = U * (0.1 + 0.11 * J.r(seed, L, i, 6)) * (L ? 1 : 1.25); x.fillRect(px, base - bh, bw, bh + 4);
          x.beginPath(); x.moveTo(px - bw * 0.06, base - bh); x.quadraticCurveTo(px + bw * 0.25, base - bh - U * 0.008, px + bw * 0.5, base - bh - U * 0.045); x.quadraticCurveTo(px + bw * 0.75, base - bh - U * 0.008, px + bw * 1.06, base - bh); x.fill();
          if (L) for (let wy = 0; wy < 2; wy++) for (let wx = 0; wx < 2; wx++) if (J.r(seed, i, wx, wy, 7) > 0.35) { x.fillStyle = rgba('#FFD98A', dark ? 0.85 : 0.45); x.fillRect(px + bw * (0.2 + wx * 0.4), base - bh * (0.75 - wy * 0.33), bw * 0.18, bh * 0.16); }
          x.fillStyle = col; px += bw + U * 0.012 * J.r(seed, L, i, 8); }
      }
      /* the street: dark stone with a warm sheen where the lanterns fall */
      x.fillStyle = vertical(x, y0, h, [[0, tone(mix(sc.bg, dark ? '#2A1E44' : '#B48A98', 0.6))], [1, tone(mix(sc.bg, dark ? '#150F2A' : '#8A6478', 0.7))]]); x.fillRect(0, y0, w, h - y0);
      x.strokeStyle = rgba('#FFFFFF', dark ? 0.06 : 0.14); x.lineWidth = Math.max(1, U * 0.002); for (let i = 1; i < 6; i++) { const y = y0 + (h - y0) * (i / 6) ** 1.5; x.beginPath(); x.moveTo(0, y); x.lineTo(w, y); x.stroke(); }
      for (let row = 0; row < 8; row++) {                                                    /* cobbles, widening toward the viewer */
        const ya = y0 + (h - y0) * (row / 8) ** 1.4, yb = y0 + (h - y0) * ((row + 1) / 8) ** 1.4, sk = 0.5 + row * 0.22, sw2 = U * 0.07 * sk;
        for (let cx0 = -sw2 * (row % 2 ? 0.5 : 0); cx0 < w + sw2; cx0 += sw2) { const v = J.r(seed, row, Math.round(cx0 / 7), 3); x.fillStyle = rgba(v > 0.5 ? '#FFFFFF' : '#000000', 0.03 + 0.05 * Math.abs(v - 0.5) * 2); x.beginPath(); x.roundRect(cx0 + 1, ya + 1, sw2 - 2, (yb - ya) - 2, U * 0.006); x.fill(); }
      }
      for (const lxp of [w * 0.06, w * 0.94]) {                                               /* two low paper lamps at the edge of the street */
        const ly = h * 0.93, s = U * 0.045; glowDisc(x, lxp, ly, s * 4.2, '#FFB060', dark ? 0.42 : 0.3);
        x.fillStyle = '#5A3A2A'; x.fillRect(lxp - s * 0.6, ly + s * 0.9, s * 1.2, s * 0.16); x.fillRect(lxp - s * 0.6, ly - s * 1.02, s * 1.2, s * 0.12);
        x.fillStyle = dark ? '#FFE0A8' : '#FFF0D0'; x.fillRect(lxp - s * 0.5, ly - s * 0.9, s, s * 1.8); x.strokeStyle = rgba('#7A4A2A', 0.6); x.lineWidth = Math.max(1, s * 0.06); x.strokeRect(lxp - s * 0.5, ly - s * 0.9, s, s * 1.8); x.beginPath(); x.moveTo(lxp, ly - s * 0.9); x.lineTo(lxp, ly + s * 0.9); x.moveTo(lxp - s * 0.5, ly); x.lineTo(lxp + s * 0.5, ly); x.stroke();
      }
      for (let i = 0; i < nStall; i++) {
        const cx = stallX(i), stripe = [red, tone(sc.accent), tone('#4FB0D8')][i % 3];
        glowDisc(x, cx, y0 + sh * 0.25, sw * 0.75, dark ? '#FFB060' : '#FFFFFF', dark ? 0.22 : 0.3);
        x.fillStyle = rgba('#000000', 0.18); x.beginPath(); x.ellipse(cx, y0 + sh * 0.02, sw * 0.55, sh * 0.07, 0, 0, TAU); x.fill();
        stall(x, cx, y0, sw, sh, sc, stripe, tone);
        const cy = y0 - sh * 0.32;                                                               /* goods on the counter */
        if (i % 3 === 0) for (let k = 0; k < 4; k++) taiyaki(x, cx - sw * 0.3 + k * sw * 0.2, cy - sh * 0.04, sw * 0.055);
        if (i % 3 === 1) for (let k = 0; k < 5; k++) dango(x, cx - sw * 0.34 + k * sw * 0.17, cy, sw * 0.045, [['#FFB6CE', '#FFFFFF', '#B6E3A0'], ['#FFE58A', '#FFFFFF', '#FFB6CE']][k % 2]);
        if (i % 3 === 2) { x.fillStyle = rgba('#FFFFFF', 0.2); x.beginPath(); x.ellipse(cx, cy - sh * 0.02, sw * 0.32, sh * 0.06, 0, 0, TAU); x.fill(); x.fillStyle = rgba('#7AC8F0', 0.4); x.beginPath(); x.ellipse(cx, cy - sh * 0.06, sw * 0.3, sh * 0.055, 0, 0, TAU); x.fill(); [-0.16, -0.05, 0.08, 0.19].forEach((dx, k) => goldfish(x, cx + dx * sw, cy - sh * 0.06 + (k % 2 - 0.5) * sh * 0.02, sw * 0.03, ['#FF7A4A', '#FFB05A', '#FF9DB8', '#FFFFFF'][k])); }
      }
    });
    ctx.drawImage(scene, 0, 0, W, H);
    /* the long string of lanterns, a few with cat faces; flames breathe */
    const rows = [[0.06, 0.13, 1], [0.05, 0.18, 0]];
    rows.forEach(([ya, yb, off], r) => {
      const count = Math.round(W / (U * 0.17)), y = u => (1 - u) * (1 - u) * H * ya + 2 * u * (1 - u) * H * yb + u * u * H * (ya + (r ? 0.03 : 0.01));
      ctx.strokeStyle = rgba('#3A2828', 0.55); ctx.lineWidth = Math.max(1, U * 0.002); ctx.beginPath(); for (let i = 0; i <= 24; i++) { const u = i / 24; if (i) ctx.lineTo(u * W, y(u)); else ctx.moveTo(0, y(0)); } ctx.stroke();
      for (let i = 0; i < count; i++) {
        const u = (i + 0.5 + off * 0.5) / count; if (u > 1) continue; const lx = u * W + Math.sin(t * 0.9 + i + r * 2) * U * 0.004, ly = y(u) + U * 0.05, face = (i + r) % 3 === 0, fl = 0.85 + 0.15 * Math.sin(t * 5 + i * 2.1 + J.r(seed, i, r, 9) * 6);
        if (dark) { ctx.save(); ctx.globalCompositeOperation = 'screen'; glowDisc(ctx, lx, ly, U * 0.15 * fl, '#FFB060', 0.4); ctx.restore(); }
        lantern(ctx, lx, ly, U * (face ? 0.036 : 0.03), face ? red : [tone('#FF9A5A'), tone('#FFC86A'), red][i % 3], face, fl);
      }
    });
    /* cats minding the stalls or wandering by */
    const coats = [FUR[1], FUR[0], FUR[2], FUR[3]];
    for (let i = 0; i < nStall; i++) { const cx = stallX(i), bibs = ['#F0584E', '#4FB0D8', '#FFC24A']; cat(ctx, cx + sw * (i % 2 ? -0.12 : 0.1), y0 - sh * 0.3, sh * 0.62, { coat: coats[i % 4], t, phase: i * 1.7, bib: bibs[i % 3], look: i % 2 ? -0.6 : 0.6 }); }
    const wk = wrap(t * 0.028 + J.r(seed, 3), 1.25) - 0.12, wx = wk * W;
    cat(ctx, wx, H * 0.985, U * 0.22, { coat: FUR[2], t, phase: 2, flip: false, look: 0.8, sil: dark ? tone('#2A2440') : null, mode: 'sit' });
    /* steam over the stalls, drifting sparks and embers */
    for (let i = 0; i < nStall; i++) { const cx = stallX(i) + sw * 0.25; ctx.strokeStyle = rgba('#FFFFFF', dark ? 0.2 : 0.32); ctx.lineWidth = Math.max(1.4, U * 0.006); ctx.lineCap = 'round'; for (let k = 0; k < 3; k++) { const ph = t * 0.9 + k * 2 + i; ctx.beginPath(); for (let j = 0; j <= 12; j++) { const u = j / 12, xx = cx + (k - 1) * U * 0.02 + Math.sin(u * 5 + ph) * U * 0.008 * u, yy = y0 - sh * 0.36 - u * U * 0.09; if (j) ctx.lineTo(xx, yy); else ctx.moveTo(xx, yy); } ctx.stroke(); } }
    ctx.save(); ctx.globalCompositeOperation = 'screen';
    for (let i = 0; i < 24; i++) { const sp = H * (0.02 + 0.04 * J.r(seed, i, 61)), y = wrap(H * 1.1 - J.r(seed, i, 62) * H * 1.2 + -t * sp, H * 1.2) - H * 0.05, x = J.r(seed, i, 63) * W + Math.sin(t * 0.7 + i) * U * 0.03, a = 0.35 + 0.4 * Math.sin(t * 2 + i); ctx.fillStyle = rgba(i % 3 ? '#FFC070' : '#FFE8A8', clamp(a) * (dark ? 0.9 : 0.55)); ctx.beginPath(); ctx.arc(x, y, U * 0.0035, 0, TAU); ctx.fill(); }
    ctx.restore();
  });

  /* ================= 猫とお花見 ================= */
  K.registerBg('echoHanami', '猫とお花見', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), dark = isDark(sc.bg), portrait = H > W, tone = c => (dark ? mix(c, mix(sc.bg, '#0F1A38', 0.5), 0.26) : c);
    const key = `hanami|${seed}|${colourKey(sc)}`, yFar = H * 0.5, yRiv = H * 0.58, yNear = H * 0.76;
    ctx.globalAlpha = e;
    const scene = layer(env, `${key}|scene`, 0.6, (x, w, h) => {
      x.fillStyle = vertical(x, 0, yFar, [[0, dark ? mix(sc.bg, '#12103A', 0.5) : mix(sc.bg, '#9CD8FF', 0.45)], [1, dark ? mix(sc.bg, sc.accent, 0.2) : mix(sc.bg, '#FFFFFF', 0.55)]]); x.fillRect(0, 0, w, yFar + 2);
      if (dark) for (let i = 0; i < 36; i++) { x.fillStyle = rgba('#FFFFFF', 0.25 + 0.5 * J.r(seed, i, 3)); x.beginPath(); x.arc(J.r(seed, i, 1) * w, J.r(seed, i, 2) * yFar * 0.6, U * 0.0018 * (1 + 2 * J.r(seed, i, 4)), 0, TAU); x.fill(); }
      const sx = w * (portrait ? 0.72 : 0.62), sy = h * 0.16; glowDisc(x, sx, sy, U * 0.45, dark ? '#FFF1B8' : '#FFFFFF', dark ? 0.3 : 0.6); x.fillStyle = dark ? '#FFF6D8' : '#FFFDEB'; x.beginPath(); x.arc(sx, sy, U * (dark ? 0.05 : 0.065), 0, TAU); x.fill();
      /* far bank: soft hills, a row of blossoming trees and little houses */
      x.fillStyle = tone(mix(sc.bg, dark ? '#243060' : '#B7E0C8', 0.55)); x.beginPath(); x.moveTo(0, yFar); for (let i = 0; i <= 12; i++) x.lineTo(w * i / 12, yFar - U * (0.05 + 0.06 * Math.sin(i * 0.9 + seed) ** 2)); x.lineTo(w, yFar); x.fill();
      for (let i = 0, px = 0; px < w + U * 0.2; i++) { const s = U * (0.06 + 0.05 * J.r(seed, i, 11)); x.fillStyle = tone(mix('#FFD3E1', sc.accent, 0.15 + 0.4 * J.r(seed, i, 12))); x.beginPath(); x.arc(px, yFar - s * 0.55, s * 0.6, 0, TAU); x.arc(px + s * 0.45, yFar - s * 0.4, s * 0.5, 0, TAU); x.arc(px - s * 0.4, yFar - s * 0.35, s * 0.45, 0, TAU); x.fill(); x.fillStyle = rgba('#FFFFFF', 0.25); x.beginPath(); x.arc(px - s * 0.15, yFar - s * 0.75, s * 0.28, 0, TAU); x.fill(); px += s * 1.05; }
      x.fillStyle = tone(mix(sc.bg, dark ? '#20305A' : '#9AD0B0', 0.6)); x.fillRect(0, yFar, w, yRiv - yFar);
      /* river with a sky reflection and long ripples */
      x.fillStyle = vertical(x, yRiv, yNear, [[0, tone(mix(dark ? '#243A78' : '#BFE6FF', sc.accent, 0.12))], [1, tone(mix(dark ? '#182860' : '#9AD3F0', sc.accent2, 0.16))]]); x.fillRect(0, yRiv - U * 0.04, w, yNear - yRiv + U * 0.04);
      x.strokeStyle = rgba('#FFFFFF', dark ? 0.16 : 0.4); x.lineWidth = Math.max(1, U * 0.0025); x.lineCap = 'round';
      for (let i = 0; i < 26; i++) { const y = yRiv + (yNear - yRiv) * J.r(seed, i, 21), a = J.r(seed, i, 22) * w, l = U * (0.05 + 0.12 * J.r(seed, i, 23)); x.beginPath(); x.moveTo(a, y); x.quadraticCurveTo(a + l / 2, y - U * 0.006, a + l, y); x.stroke(); }
      /* near bank: grass with a soft edge and clover flecks */
      x.fillStyle = tone(mix(dark ? '#3A6A64' : '#B8E6A8', sc.bg, 0.25)); x.beginPath(); x.moveTo(0, yNear + U * 0.02); for (let i = 0; i <= 14; i++) x.lineTo(w * i / 14, yNear - U * 0.012 * Math.sin(i * 1.6 + seed) ** 2); x.lineTo(w, h); x.lineTo(0, h); x.fill();
      x.fillStyle = vertical(x, yNear, h, [[0, rgba('#FFFFFF', dark ? 0.04 : 0.16)], [1, rgba('#000000', dark ? 0.2 : 0.06)]]); x.fillRect(0, yNear - U * 0.02, w, h - yNear + U * 0.02);
      for (let i = 0; i < 40; i++) { x.fillStyle = rgba(i % 3 ? '#FFFFFF' : '#FFE58A', dark ? 0.3 : 0.6); x.beginPath(); x.arc(J.r(seed, i, 31) * w, yNear + U * 0.03 + (h - yNear - U * 0.03) * J.r(seed, i, 32), U * 0.004, 0, TAU); x.fill(); }
      /* the tree: a dark branch sweeping in from the top-left, and a second from the right, both heavy with blossom */
      const branch = (sx0, sy0, ex, ey, wd, dir) => { x.strokeStyle = tone('#6A4650'); x.lineCap = 'round'; x.lineWidth = wd; x.beginPath(); x.moveTo(sx0, sy0); x.bezierCurveTo(sx0 + (ex - sx0) * 0.3, sy0 + (ey - sy0) * 0.9, sx0 + (ex - sx0) * 0.7, sy0 + (ey - sy0) * 0.2 + U * 0.05, ex, ey); x.stroke(); x.lineWidth = wd * 0.45; x.beginPath(); x.moveTo(sx0 + (ex - sx0) * 0.5, sy0 + (ey - sy0) * 0.35); x.quadraticCurveTo(sx0 + (ex - sx0) * 0.6, sy0 + (ey - sy0) * 0.9 + U * 0.1, ex - dir * U * 0.2, ey + U * 0.13); x.stroke(); };
      branch(-U * 0.05, h * 0.04, w * 0.42, h * 0.12, U * 0.05, 1); branch(w + U * 0.05, h * 0.02, w * (portrait ? 0.62 : 0.68), h * 0.1, U * 0.04, -1);
      const blooms = [[0.08, 0.05, 1.4], [0.22, 0.1, 1.2], [0.36, 0.13, 1.0], [0.5, 0.05, 0.8], [0.9, 0.05, 1.3], [0.78, 0.11, 1.1], [0.66, 0.1, 0.9], [0.14, 0.2, 0.7], [0.3, 0.19, 0.6], [0.86, 0.18, 0.7]];
      blooms.forEach(([ux, uy, k], n) => { if (portrait && n > 6) return; blossomMass(x, w * ux, h * uy, U * 0.36 * k, sc, tone, seed, 30 + n); });
    });
    ctx.drawImage(scene, 0, 0, W, H);
    /* river: glints sliding on the water and petal rafts floating by */
    ctx.save(); ctx.beginPath(); ctx.rect(0, yRiv - U * 0.02, W, yNear - yRiv + U * 0.02); ctx.clip();
    for (let i = 0; i < 18; i++) { const b = Math.pow(0.5 + 0.5 * Math.sin(t * (1.3 + J.r(seed, i, 41)) + i * 3.1), 3), x = J.r(seed, i, 42) * W, y = yRiv + (yNear - yRiv) * J.r(seed, i, 43); ctx.fillStyle = rgba('#FFFFFF', (dark ? 0.7 : 0.8) * b); ctx.fillRect(x, y, U * (0.015 + 0.02 * b), U * 0.003); }
    for (let i = 0; i < 26; i++) { const x = wrap(J.r(seed, i, 51) * W * 1.2 + t * U * (0.02 + 0.02 * J.r(seed, i, 52)), W * 1.2) - W * 0.1, y = yRiv + (yNear - yRiv) * (0.1 + 0.85 * J.r(seed, i, 53)) + Math.sin(t * 0.8 + i) * U * 0.004; ctx.save(); ctx.translate(x, y); ctx.scale(1, 0.4); ctx.rotate(i + t * 0.1); ctx.fillStyle = rgba(mix('#FFD3E1', sc.accent, 0.25), 0.85); petal(ctx, 0, 0, U * 0.012); ctx.fill(); ctx.restore(); }
    ctx.restore();
    /* picnic mat on the near bank, cats and dango */
    const mx = W * (portrait ? 0.5 : 0.36), my = H * 0.86, mw = U * (portrait ? 0.85 : 0.75), mh = U * 0.16;
    ctx.fillStyle = rgba('#000000', 0.12); ctx.beginPath(); ctx.ellipse(mx, my + mh * 0.55, mw * 0.56, mh * 0.2, 0, 0, TAU); ctx.fill();
    ctx.fillStyle = tone('#FFF6EA'); ctx.beginPath(); ctx.moveTo(mx - mw * 0.42, my - mh * 0.5); ctx.lineTo(mx + mw * 0.42, my - mh * 0.5); ctx.lineTo(mx + mw * 0.52, my + mh * 0.5); ctx.lineTo(mx - mw * 0.52, my + mh * 0.5); ctx.fill();
    ctx.save(); ctx.clip(); for (let i = -6; i < 7; i++) { ctx.fillStyle = rgba(i % 2 ? sc.accent : sc.accent2, dark ? 0.4 : 0.5); ctx.beginPath(); ctx.moveTo(mx + i * mw * 0.07 - mw * 0.04, my - mh * 0.5); ctx.lineTo(mx + i * mw * 0.07 + mw * 0.03, my - mh * 0.5); ctx.lineTo(mx + i * mw * 0.09 + mw * 0.04, my + mh * 0.5); ctx.lineTo(mx + i * mw * 0.09 - mw * 0.03, my + mh * 0.5); ctx.fill(); } ctx.restore();
    ctx.fillStyle = tone('#FFFFFF'); ctx.beginPath(); ctx.ellipse(mx, my - mh * 0.05, mw * 0.14, mh * 0.2, 0, 0, TAU); ctx.fill();          /* plate */
    [['#FFB6CE', '#FFFFFF', '#B6E3A0'], ['#FFB6CE', '#FFFFFF', '#B6E3A0']].forEach((cols, k) => dango(ctx, mx + (k ? 0.03 : -0.03) * mw, my - mh * 0.08, U * 0.016, cols));
    cat(ctx, mx - mw * 0.3, my + mh * 0.1, U * 0.24, { coat: FUR[0], t, phase: 0.3, look: 0.5, bib: sc.accent });
    cat(ctx, mx + mw * 0.3, my + mh * 0.12, U * 0.2, { coat: FUR[1], t, phase: 1.9, flip: true, look: -0.5, mode: 'loaf' });
    /* petals blowing across, a few big ones in front for depth */
    for (let i = 0; i < 34; i++) {
      const z = J.r(seed, i, 61), sp = U * (0.03 + 0.06 * z), x = wrap(J.r(seed, i, 62) * W * 1.3 - t * sp * 0.6, W * 1.3) - W * 0.15, y = wrap(J.r(seed, i, 63) * H * 1.2 + t * sp * 0.8, H * 1.2) - H * 0.1;
      ctx.save(); ctx.translate(x + Math.sin(t * 0.8 + i) * U * 0.02, y); ctx.rotate(t * (0.4 + z) + i); ctx.scale(1, 0.55 + 0.45 * Math.sin(t * 1.5 + i)); ctx.fillStyle = rgba(mix('#FFD3E1', sc.accent, 0.3 * z), 0.55 + 0.4 * z); petal(ctx, 0, 0, U * (0.008 + 0.014 * z)); ctx.fill(); ctx.restore();
    }
  });
})();
