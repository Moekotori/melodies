/* ECHO first-party backdrops, more scenes for the daylight looks: 青空 (夏の海辺, ひまわり畑), 雪あかり (かまくらの夜, 雪の湯けむり) and 喫茶店 (雨の窓辺, レコード喫茶).
   Static art goes into cached layers; waves, steam, drops, flames and flowers in the wind are redrawn every frame. Every scene has a light and a dark reading. */
(() => {
  'use strict';
  const K = J.echo, E = K.E, TAU = K.TAU, clamp = K.clamp, mix = K.mix, rgba = K.rgba;
  const { U: Umin, isDark, wrap, fade, vertical, glowDisc, star, layer, shapes } = K;
  const { puff, sparkle, roundRect } = shapes;
  const colourKey = sc => [sc.bg, sc.fg, sc.accent, sc.accent2, sc.ghostA, sc.ghostB].join('');
  const r = J.r;

  /* ================= 夏の海辺 ================= */
  K.registerBg('echoSeaside', '夏の海辺', ['pop', 'calm', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), dark = isDark(sc.bg), portrait = H > W;
    const key = `seaside|${seed}|${colourKey(sc)}`, hz = H * 0.5, shore = H * 0.84;
    const sky0 = dark ? mix(sc.bg, '#050B26', 0.55) : mix(sc.bg, '#2E86E0', 0.42), sky1 = dark ? mix(sc.bg, '#2B3E86', 0.4) : mix(sc.bg, '#FFF3DC', 0.6);
    const sea0 = dark ? mix(sc.bg, '#0D3A6A', 0.5) : mix(sc.bg, '#6FE0E8', 0.55), sea1 = dark ? mix(sc.bg, '#05193A', 0.6) : mix(sc.bg, '#1876C8', 0.55), seaNear = dark ? mix(sc.bg, '#0A3A5A', 0.45) : mix(sc.bg, '#4FD8D0', 0.5);
    ctx.globalAlpha = e;
    const scene = layer(env, `${key}|scene`, 0.6, (x, w, h) => {
      x.fillStyle = vertical(x, 0, hz, [[0, sky0], [1, sky1]]); x.fillRect(0, 0, w, hz + 2);
      if (dark) { for (let i = 0; i < 70; i++) { x.fillStyle = rgba('#FFFFFF', 0.25 + 0.6 * r(seed, i, 3)); x.beginPath(); x.arc(r(seed, i, 1) * w, r(seed, i, 2) * hz * 0.85, U * 0.0018 * (1 + 1.8 * r(seed, i, 4)), 0, TAU); x.fill(); } }
      const sx = w * (portrait ? 0.7 : 0.72), sy = hz * (dark ? 0.42 : 0.55);
      glowDisc(x, sx, sy, U * 0.6, dark ? '#DDE8FF' : '#FFF6C8', dark ? 0.35 : 0.8); x.fillStyle = dark ? '#F4F8FF' : '#FFFBE0'; x.beginPath(); x.arc(sx, sy, U * (dark ? 0.05 : 0.07), 0, TAU); x.fill();
      if (dark) { x.fillStyle = rgba(sky0, 0.9); x.beginPath(); x.arc(sx + U * 0.02, sy - U * 0.008, U * 0.047, 0, TAU); x.fill(); }
      for (let i = 0; i < 4; i++) {                                                         /* big summer clouds */
        const cx = w * (0.12 + 0.24 * i + 0.05 * r(seed, i, 5)), cy = hz * (0.3 + 0.28 * r(seed, i, 6)), cw = U * (0.5 + 0.4 * r(seed, i, 7));
        K.puffCloud(x, cx, cy, cw, cw * 0.4, dark ? mix(sky1, '#9FB4E8', 0.5) : (J.lum(sc.fg) > 0.6 ? mix('#FFFFFF', sc.bg, 0.4) : '#FFFFFF'), dark ? 0.7 : 0.97, J.h(seed, i, 9), portrait ? 1 : (cx > w * 0.5 ? 1 : -1), dark);
      }
      x.fillStyle = vertical(x, hz, shore, [[0, sea0], [0.35, sea1], [1, seaNear]]); x.fillRect(0, hz, w, shore - hz + 2);
      /* a low island with a lighthouse on the horizon */
      x.fillStyle = dark ? mix(sc.bg, '#0A1A3A', 0.7) : mix(sc.bg, '#5A86B0', 0.55); x.beginPath(); x.moveTo(w * 0.04, hz + 1); x.quadraticCurveTo(w * 0.13, hz - U * 0.05, w * 0.24, hz + 1); x.fill();
      x.fillStyle = dark ? '#C8D4F0' : '#FFFFFF'; x.fillRect(w * 0.115, hz - U * 0.085, U * 0.008, U * 0.05); x.fillStyle = dark ? '#FFB45A' : '#E0503A'; x.fillRect(w * 0.115 - U * 0.002, hz - U * 0.093, U * 0.012, U * 0.01);
      x.fillStyle = rgba('#FFFFFF', dark ? 0.35 : 0.7); x.fillRect(0, hz - 1, w, Math.max(1, U * 0.003));
      /* moon or sun path on the water */
      const rx = sx; for (let i = 0; i < 40; i++) { const v = i / 40, y = hz + (shore - hz) * v * 0.8, hw = U * (0.012 + 0.09 * v) * (0.6 + r(seed, i, 8)); x.fillStyle = rgba(dark ? '#DDE8FF' : '#FFF6C8', (dark ? 0.16 : 0.3) * (1 - v * 0.5)); x.fillRect(rx - hw + (r(seed, i, 9) - 0.5) * U * 0.03, y, hw * 2, Math.max(1, U * 0.004)); }
      /* sand, umbrella, ball */
      x.fillStyle = dark ? mix('#C9B48A', sc.bg, 0.55) : mix('#F6E4B0', sc.bg, 0.12); x.beginPath(); x.moveTo(0, shore + U * 0.02); x.quadraticCurveTo(w * 0.5, shore - U * 0.03, w, shore + U * 0.01); x.lineTo(w, h); x.lineTo(0, h); x.fill();
      x.fillStyle = vertical(x, shore, h, [[0, rgba('#FFFFFF', dark ? 0.03 : 0.2)], [1, rgba('#000000', dark ? 0.25 : 0.06)]]); x.fillRect(0, shore - U * 0.03, w, h - shore + U * 0.03);
      const ux = w * (portrait ? 0.26 : 0.2), uy = h * 0.93, ur = U * 0.24;
      x.strokeStyle = dark ? '#B9A88A' : '#F0F0F0'; x.lineWidth = U * 0.006; x.beginPath(); x.moveTo(ux + ur * 0.1, uy + U * 0.05); x.lineTo(ux - ur * 0.1, uy - ur * 0.75); x.stroke();
      const nSeg = 8; for (let i = 0; i < nSeg; i++) { x.fillStyle = i % 2 ? (dark ? mix(sc.accent, '#000000', 0.35) : '#FFFFFF') : (dark ? mix(sc.accent2, '#000000', 0.25) : sc.accent); x.beginPath(); x.moveTo(ux - ur * 0.1, uy - ur * 0.78); x.quadraticCurveTo(ux + ur * (-1 + 2 * i / nSeg), uy - ur * 1.2, ux + ur * (-1 + 2 * (i + 0.5) / nSeg) - ur * 0.02, uy - ur * 0.55); x.quadraticCurveTo(ux + ur * (-1 + 2 * (i + 0.5) / nSeg), uy - ur * 0.6, ux - ur * 0.1, uy - ur * 0.78); x.fill(); }
      x.fillStyle = dark ? '#2A3A66' : mix('#FFFFFF', sc.accent2, 0.25); x.beginPath(); x.ellipse(ux + ur * 1.4, uy + U * 0.03, U * 0.03, U * 0.03, 0, 0, TAU); x.fill(); x.fillStyle = rgba('#FFFFFF', 0.6); x.beginPath(); x.arc(ux + ur * 1.4 - U * 0.008, uy + U * 0.02, U * 0.008, 0, TAU); x.fill();
      x.fillStyle = rgba('#000000', 0.1); x.beginPath(); x.ellipse(ux, uy + U * 0.06, ur * 0.9, U * 0.014, 0, 0, TAU); x.fill();
      /* rocks with a wet sheen at the right, a starfish and a shell, a line of footprints */
      const rx0 = w * (portrait ? 0.82 : 0.9), ry0 = h * 0.94;
      [[0, 0, 0.09], [-0.09, 0.012, 0.06], [0.07, 0.02, 0.05]].forEach(([dx, dy, k], i) => { x.fillStyle = dark ? mix('#2A3450', sc.bg, 0.3) : mix('#8A7A72', sc.bg, 0.15); x.beginPath(); x.ellipse(rx0 + dx * U, ry0 + dy * U, k * U * 1.1, k * U * 0.72, 0, Math.PI, TAU); x.fill(); x.fillStyle = rgba('#FFFFFF', dark ? 0.12 : 0.28); x.beginPath(); x.ellipse(rx0 + dx * U - k * U * 0.3, ry0 + dy * U - k * U * 0.35, k * U * 0.4, k * U * 0.16, -0.4, 0, TAU); x.fill(); x.fillStyle = rgba('#000000', 0.12); x.fillRect(rx0 + dx * U - k * U * 1.1, ry0 + dy * U - 1, k * U * 2.2, k * U * 0.16); });
      x.fillStyle = dark ? '#B86A5A' : '#FF8A6A'; star(x, w * 0.5, h * 0.945, U * 0.026, 5, 0.42); x.fill(); x.fillStyle = rgba('#FFFFFF', 0.5); x.beginPath(); x.arc(w * 0.5, h * 0.945, U * 0.005, 0, TAU); x.fill();
      x.fillStyle = dark ? '#C8B8A0' : '#FFF3E0'; x.beginPath(); x.ellipse(w * 0.62, h * 0.965, U * 0.016, U * 0.011, 0.4, 0, TAU); x.fill(); x.strokeStyle = rgba('#B08A6A', 0.6); x.lineWidth = Math.max(1, U * 0.0016); for (let k = -2; k <= 2; k++) { x.beginPath(); x.moveTo(w * 0.62, h * 0.965 + U * 0.008); x.lineTo(w * 0.62 + k * U * 0.006, h * 0.965 - U * 0.008); x.stroke(); }
      for (let i = 0; i < 7; i++) { x.fillStyle = rgba('#000000', dark ? 0.16 : 0.09); x.beginPath(); x.ellipse(w * (0.3 + i * 0.05), h * (0.955 - (i % 2) * 0.014), U * 0.008, U * 0.014, 0.2, 0, TAU); x.fill(); }
    });
    ctx.drawImage(scene, 0, 0, W, H);
    /* waves: long lines sliding along the sea, glitter blinking */
    ctx.strokeStyle = rgba('#FFFFFF', dark ? 0.2 : 0.42); ctx.lineWidth = Math.max(1, U * 0.0028); ctx.lineCap = 'round';
    for (let i = 0; i < 34; i++) { const v = r(seed, i, 21), y = hz + (shore - hz) * (0.06 + 0.9 * v) * 0.92, x0 = wrap(r(seed, i, 22) * W * 1.3 + t * U * (0.02 + 0.05 * v), W * 1.3) - W * 0.15, l = U * (0.05 + 0.12 * v); ctx.beginPath(); ctx.moveTo(x0, y); ctx.quadraticCurveTo(x0 + l / 2, y - U * 0.006 * (1 + v), x0 + l, y); ctx.stroke(); }
    ctx.save(); ctx.globalCompositeOperation = dark ? 'screen' : 'source-over';
    for (let i = 0; i < 30; i++) { const b = Math.pow(0.5 + 0.5 * Math.sin(t * (1.2 + 2 * r(seed, i, 31)) + i * 2.7), 4), v = r(seed, i, 32), x = r(seed, i, 33) * W, y = hz + (shore - hz) * (0.05 + 0.85 * v) * 0.92; ctx.fillStyle = rgba('#FFFFFF', (dark ? 0.9 : 0.95) * b); ctx.fillRect(x, y, U * (0.006 + 0.016 * b), U * 0.0026); }
    ctx.restore();
    /* sailboat bobbing at the horizon */
    const bx = W * (portrait ? 0.3 : 0.38) + Math.sin(t * 0.15) * U * 0.03, by = hz + U * 0.012 + Math.sin(t * 1.3) * U * 0.003, bs = U * 0.09;
    ctx.fillStyle = dark ? '#1A2450' : '#FFFFFF'; ctx.beginPath(); ctx.moveTo(bx, by - bs); ctx.lineTo(bx + bs * 0.55, by - bs * 0.1); ctx.lineTo(bx, by - bs * 0.1); ctx.fill(); ctx.beginPath(); ctx.moveTo(bx - bs * 0.08, by - bs * 0.85); ctx.lineTo(bx - bs * 0.5, by - bs * 0.12); ctx.lineTo(bx - bs * 0.08, by - bs * 0.12); ctx.fill();
    ctx.fillStyle = dark ? '#111A3C' : sc.accent; ctx.beginPath(); ctx.moveTo(bx - bs * 0.6, by - bs * 0.05); ctx.lineTo(bx + bs * 0.6, by - bs * 0.05); ctx.lineTo(bx + bs * 0.4, by + bs * 0.1); ctx.lineTo(bx - bs * 0.4, by + bs * 0.1); ctx.fill();
    /* the shoreline: foam sliding up and back */
    const wash = Math.sin(t * 0.7) * 0.5 + 0.5, fy = H * 0.84 - U * 0.01 + wash * U * 0.03;
    ctx.fillStyle = rgba('#FFFFFF', dark ? 0.4 : 0.7); ctx.beginPath(); ctx.moveTo(0, fy); for (let i = 0; i <= 40; i++) ctx.lineTo(W * i / 40, fy + Math.sin(i * 0.9 + t * 1.5) * U * 0.006); ctx.lineTo(W, fy - U * 0.02); ctx.lineTo(0, fy - U * 0.02); ctx.fill();
    ctx.fillStyle = rgba(dark ? '#7FA4E8' : '#8FD8F0', 0.35); ctx.fillRect(0, fy - U * 0.02, W, U * 0.012);
    /* palm fronds sweeping in from the top-right corner, swaying in the breeze */
    { const px = W + U * 0.02, py = -U * 0.02; ctx.fillStyle = dark ? '#0E2A2A' : '#2E8A4A';
      for (let i = 0; i < 7; i++) { const a = Math.PI * (0.62 + i * 0.075) + Math.sin(t * 0.7 + i) * 0.03, len = U * (0.34 + 0.06 * (i % 3)); ctx.save(); ctx.translate(px, py); ctx.rotate(a); ctx.beginPath(); ctx.moveTo(0, 0); ctx.quadraticCurveTo(len * 0.5, -len * 0.08, len, len * 0.06); ctx.quadraticCurveTo(len * 0.5, len * 0.12, 0, U * 0.008); ctx.fill(); ctx.strokeStyle = dark ? '#1C4A40' : '#4DB868'; ctx.lineWidth = Math.max(1, U * 0.002); for (let k = 1; k < 9; k++) { ctx.beginPath(); ctx.moveTo(len * k / 9, len * 0.02); ctx.lineTo(len * k / 9 + len * 0.03, len * 0.09 * (1 - k / 12)); ctx.stroke(); } ctx.restore(); } }
    /* a little crab scuttling by the rocks */
    { const cyc = 14, ph = wrap(t + r(seed, 9) * cyc, cyc) / cyc, cx0 = W * (portrait ? 0.7 : 0.78) + Math.sin(ph * TAU) * U * 0.09, cy0 = H * 0.955, s = U * 0.02, leg = Math.sin(t * 14) * 0.5; ctx.fillStyle = dark ? '#B04A3A' : '#F0604A'; ctx.beginPath(); ctx.ellipse(cx0, cy0, s * 1.2, s * 0.75, 0, 0, TAU); ctx.fill(); ctx.strokeStyle = ctx.fillStyle; ctx.lineWidth = Math.max(1, s * 0.22); ctx.lineCap = 'round'; for (const d of [-1, 1]) { for (let k = 0; k < 3; k++) { ctx.beginPath(); ctx.moveTo(cx0 + d * s * 0.9, cy0 + s * 0.2); ctx.lineTo(cx0 + d * s * (1.6 + k * 0.2), cy0 + s * (0.5 + k * 0.28) + leg * s * 0.2 * d); ctx.stroke(); } ctx.beginPath(); ctx.moveTo(cx0 + d * s * 0.8, cy0 - s * 0.4); ctx.lineTo(cx0 + d * s * 1.6, cy0 - s * 1.1); ctx.stroke(); ctx.beginPath(); ctx.arc(cx0 + d * s * 1.8, cy0 - s * 1.25, s * 0.42, 0, TAU); ctx.fill(); } ctx.fillStyle = '#FFFFFF'; ctx.beginPath(); ctx.arc(cx0 - s * 0.45, cy0 - s * 0.55, s * 0.22, 0, TAU); ctx.arc(cx0 + s * 0.45, cy0 - s * 0.55, s * 0.22, 0, TAU); ctx.fill(); ctx.fillStyle = '#20202A'; ctx.beginPath(); ctx.arc(cx0 - s * 0.45, cy0 - s * 0.55, s * 0.1, 0, TAU); ctx.arc(cx0 + s * 0.45, cy0 - s * 0.55, s * 0.1, 0, TAU); ctx.fill(); }
    /* gulls */
    ctx.strokeStyle = dark ? rgba('#DDE8FF', 0.7) : rgba('#FFFFFF', 0.95); ctx.lineWidth = Math.max(1.4, U * 0.0035); ctx.lineCap = 'round';
    for (let i = 0; i < 4; i++) { const gx = wrap(W * (0.1 + 0.25 * i) + t * U * (0.05 + 0.02 * i), W * 1.2) - W * 0.1, gy = H * (0.16 + 0.07 * (i % 3)) + Math.sin(t * 0.9 + i) * U * 0.015, fl = Math.sin(t * 5 + i * 2) * 0.35, s = U * (0.024 + 0.008 * (i % 2)); ctx.beginPath(); ctx.moveTo(gx - s, gy - s * fl); ctx.quadraticCurveTo(gx - s * 0.4, gy - s * (0.5 + fl), gx, gy); ctx.quadraticCurveTo(gx + s * 0.4, gy - s * (0.5 + fl), gx + s, gy - s * fl); ctx.stroke(); }
    for (let i = 0; i < 8; i++) { const y = wrap(r(seed, i, 41) * H * 1.1 + t * H * 0.02, H * 1.1) - H * 0.05, x = r(seed, i, 42) * W + Math.sin(t * 0.5 + i) * U * 0.03; ctx.fillStyle = rgba('#FFFFFF', 0.22); ctx.beginPath(); ctx.arc(x, y, U * (0.004 + 0.006 * r(seed, i, 43)), 0, TAU); ctx.fill(); }
  });

  /* ================= ひまわり畑 ================= */
  const sunflower = (x, cx, cy, s, sc, dark, ph, t, stem) => {
    if (stem) { x.strokeStyle = dark ? '#1F5A3A' : '#3FA35A'; x.lineWidth = s * 0.12; x.lineCap = 'round'; x.beginPath(); x.moveTo(cx - stem.dx, cy + stem.len); x.quadraticCurveTo(cx - stem.dx * 0.4, cy + stem.len * 0.5, cx, cy); x.stroke(); x.fillStyle = dark ? '#1F5A3A' : '#4DB868'; x.beginPath(); x.ellipse(cx - stem.dx * 0.5 - s * 0.35, cy + stem.len * 0.55, s * 0.4, s * 0.13, -0.5, 0, TAU); x.fill(); }
    const n = 14; for (let ring = 0; ring < 2; ring++) for (let i = 0; i < n; i++) { const a = (i / n) * TAU + ring * TAU / n / 2 + ph, rr = s * (ring ? 0.98 : 1.1); x.fillStyle = ring ? (dark ? '#B67A16' : '#FFC21A') : (dark ? '#D89A22' : '#FFD84A'); x.beginPath(); x.ellipse(cx + Math.cos(a) * s * 0.62, cy + Math.sin(a) * s * 0.62, rr * 0.4, rr * 0.16, a, 0, TAU); x.fill(); }
    x.fillStyle = dark ? '#3A2210' : '#6B3F1A'; x.beginPath(); x.arc(cx, cy, s * 0.42, 0, TAU); x.fill(); x.fillStyle = dark ? '#5A3618' : '#8A5526'; x.beginPath(); x.arc(cx - s * 0.05, cy - s * 0.05, s * 0.3, 0, TAU); x.fill();
    x.fillStyle = rgba('#FFE7A0', 0.6); for (let i = 0; i < 12; i++) { const a = i * 2.4, rr = s * 0.36 * Math.sqrt((i + 0.5) / 12); x.beginPath(); x.arc(cx + Math.cos(a) * rr, cy + Math.sin(a) * rr, s * 0.025, 0, TAU); x.fill(); }
  };
  K.registerBg('echoSunflower', 'ひまわり畑', ['pop', 'calm', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), dark = isDark(sc.bg), portrait = H > W;
    const key = `sunf|${seed}|${colourKey(sc)}`, hz = H * 0.6;
    ctx.globalAlpha = e;
    const scene = layer(env, `${key}|scene`, 0.6, (x, w, h) => {
      x.fillStyle = vertical(x, 0, hz, [[0, dark ? mix(sc.bg, '#1B1250', 0.5) : mix(sc.bg, '#FFFFFF', 0.08)], [1, dark ? mix(sc.bg, '#B0507A', 0.35) : mix(sc.bg, '#FFFFFF', 0.6)]]); x.fillRect(0, 0, w, hz + 2);
      if (dark) for (let i = 0; i < 60; i++) { x.fillStyle = rgba('#FFFFFF', 0.25 + 0.6 * r(seed, i, 3)); x.beginPath(); x.arc(r(seed, i, 1) * w, r(seed, i, 2) * hz * 0.7, U * 0.0018 * (1 + 1.8 * r(seed, i, 4)), 0, TAU); x.fill(); }
      const sx = w * (portrait ? 0.7 : 0.75), sy = hz * 0.62; glowDisc(x, sx, sy, U * 0.7, dark ? '#FFB27A' : '#FFF3B0', dark ? 0.35 : 0.85); x.fillStyle = dark ? '#FFD9A8' : '#FFFBE0'; x.beginPath(); x.arc(sx, sy, U * 0.06, 0, TAU); x.fill();
      for (let i = 0; i < 3; i++) {                                                         /* towering thunderheads */
        const cx = w * [0.1, 0.9, 0.52][i] + w * 0.03 * (r(seed, i, 5) - 0.5), base = hz - U * (i === 2 ? 0.24 : 0.02), cw = U * (i === 2 ? 0.34 : 0.4 + 0.2 * r(seed, i, 6)), ch = cw * (0.6 + 0.3 * r(seed, i, 7));
        K.puffCloud(x, cx, base, cw * 1.15, ch * 0.9, dark ? mix('#C8A0D0', sc.bg, 0.3) : (J.lum(sc.fg) > 0.6 ? mix('#FFFFFF', sc.bg, 0.45) : '#FFFFFF'), dark ? 0.75 : 0.97, J.h(seed, i, 11), i === 0 ? 1 : -1, dark);
      }
      x.fillStyle = dark ? mix('#204A3A', sc.bg, 0.3) : mix('#8CD07A', sc.bg, 0.12); x.beginPath(); x.moveTo(0, hz); for (let i = 0; i <= 12; i++) x.lineTo(w * i / 12, hz - U * 0.02 * Math.sin(i * 1.1 + seed) ** 2); x.lineTo(w, h); x.lineTo(0, h); x.fill();
      { const hg = x.createLinearGradient(0, hz - U * 0.05, 0, hz + U * 0.16); hg.addColorStop(0, rgba('#FFFFFF', 0)); hg.addColorStop(0.5, rgba(dark ? '#FFB27A' : '#FFF6D8', dark ? 0.2 : 0.4)); hg.addColorStop(1, rgba('#FFFFFF', 0)); x.fillStyle = hg; x.fillRect(0, hz - U * 0.05, w, U * 0.21); }
      /* rows of flowers, small and dense at the back */
      for (let row = 0; row < 5; row++) { const v = row / 5, y = hz + (h - hz) * (v ** 1.3) * 0.75 + U * 0.02, s = U * (0.028 + 0.06 * v), cnt = Math.ceil(w / (s * 2.1)); for (let i = 0; i < cnt; i++) sunflower(x, (i + 0.5 * (row % 2) + 0.2 * r(seed, row, i, 11)) * s * 2.1, y - s * (2.2 + r(seed, row, i, 12)), s, sc, dark, r(seed, row, i, 13), { dx: s * 0.2 * (r(seed, row, i, 14) - 0.5), len: s * 4 }); }
    });
    ctx.drawImage(scene, 0, 0, W, H);
    /* the near flowers nod in the wind */
    const n = portrait ? 3 : 5;
    for (let i = 0; i < n; i++) { const s = U * (0.13 + 0.05 * r(seed, i, 21)), cx = W * ((i + 0.5) / n + 0.05 * (r(seed, i, 22) - 0.5)), cy = H * (0.72 + 0.14 * r(seed, i, 23)), sway = Math.sin(t * 0.9 + i * 1.7) * s * 0.06; ctx.save(); ctx.translate(sway, 0); sunflower(ctx, cx, cy, s, sc, dark, 0, t, { dx: sway * 2, len: H - cy + U * 0.1 }); ctx.restore(); }
    /* big leaves from the lower corners, and butterflies working the flowers */
    ctx.fillStyle = dark ? '#123A2A' : '#3FA35A'; for (const side of [-1, 1]) for (let k = 0; k < 3; k++) { ctx.save(); ctx.translate(side < 0 ? -U * 0.02 : W + U * 0.02, H + U * 0.02); ctx.rotate(side * (-0.9 - k * 0.32) + Math.sin(t * 0.8 + k + side) * 0.02); ctx.beginPath(); ctx.moveTo(0, 0); ctx.quadraticCurveTo(U * 0.12 * side, -U * 0.18, 0, -U * (0.32 + 0.06 * k)); ctx.quadraticCurveTo(-U * 0.1 * side, -U * 0.16, 0, 0); ctx.fill(); ctx.strokeStyle = rgba('#FFFFFF', 0.25); ctx.lineWidth = Math.max(1, U * 0.002); ctx.beginPath(); ctx.moveTo(0, 0); ctx.lineTo(0, -U * (0.3 + 0.06 * k)); ctx.stroke(); ctx.restore(); }
    for (let i = 0; i < 3; i++) { const cyc = 16 + 5 * i, ph = wrap(t + r(seed, i, 91) * cyc, cyc) / cyc, x = W * (0.15 + 0.7 * ph) + Math.sin(t * 2 + i) * U * 0.02, y = H * (0.55 + 0.25 * r(seed, i, 92)) + Math.sin(ph * TAU * 3 + i) * U * 0.05, fl = Math.abs(Math.sin(t * 9 + i * 2)), s2 = U * 0.022; ctx.fillStyle = [sc.accent2, '#FFFFFF', '#FF9DB8'][i % 3]; for (const d of [-1, 1]) { ctx.beginPath(); ctx.ellipse(x + d * s2 * 0.5 * (0.3 + fl), y - s2 * 0.2, s2 * 0.6 * (0.3 + fl), s2 * 0.8, d * 0.5, 0, TAU); ctx.fill(); } ctx.fillStyle = '#3A2A2A'; ctx.fillRect(x - s2 * 0.06, y - s2 * 0.6, s2 * 0.12, s2 * 1.2); }
    ctx.save(); ctx.globalCompositeOperation = dark ? 'screen' : 'source-over';
    for (let i = 0; i < 22; i++) { const b = dark ? Math.pow(0.5 + 0.5 * Math.sin(t * 1.5 + i * 2.3), 2) : 0.5 + 0.5 * Math.sin(t * 0.8 + i), x = wrap(r(seed, i, 31) * W + t * U * (dark ? 0.01 : 0.03), W), y = H * (0.5 + 0.45 * r(seed, i, 32)) + Math.sin(t * 0.7 + i) * U * 0.02; ctx.fillStyle = dark ? rgba('#FFF3A0', 0.85 * b) : rgba('#FFFFFF', 0.5 * b); if (dark) { glowDisc(ctx, x, y, U * 0.02, '#FFF3A0', 0.5 * b); } ctx.beginPath(); ctx.arc(x, y, U * 0.0035, 0, TAU); ctx.fill(); }
    ctx.restore();
    for (let i = 0; i < 3; i++) { const x = wrap(W * (0.1 + 0.35 * i) + t * U * 0.06, W * 1.2) - W * 0.1, y = H * (0.2 + 0.08 * i) + Math.sin(t * 0.8 + i) * U * 0.02; ctx.strokeStyle = rgba(dark ? '#FFD9A8' : '#FFFFFF', 0.7); ctx.lineWidth = Math.max(1, U * 0.002); ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(x + U * 0.01, y + U * 0.02); ctx.stroke(); for (let k = 0; k < 5; k++) { ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(x - U * 0.008 + k * U * 0.004, y - U * 0.01); ctx.stroke(); } }
  });

  /* ================= かまくらの夜 ================= */
  K.registerBg('echoKamakura', 'かまくらの夜', ['calm', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), dark = isDark(sc.bg), portrait = H > W;
    const key = `kama|${seed}|${colourKey(sc)}`, hz = H * 0.62, kx = W * (portrait ? 0.5 : 0.72), ky = H * 0.86, kr = U * (portrait ? 0.34 : 0.36);
    const snow = dark ? mix('#DDE8FF', sc.bg, 0.4) : mix('#FFFFFF', sc.bg, 0.12), shade = dark ? mix(sc.bg, '#0A1030', 0.5) : mix(sc.bg, '#9DB6DA', 0.45);
    ctx.globalAlpha = e;
    const scene = layer(env, `${key}|scene`, 0.6, (x, w, h) => {
      x.fillStyle = vertical(x, 0, hz, [[0, dark ? mix(sc.bg, '#050B26', 0.55) : mix(sc.bg, '#FFFFFF', 0.1)], [1, dark ? mix(sc.bg, '#3B4E96', 0.45) : mix(sc.bg, '#FFFFFF', 0.6)]]); x.fillRect(0, 0, w, hz + 2);
      if (dark) for (let i = 0; i < 90; i++) { x.fillStyle = rgba('#FFFFFF', 0.25 + 0.65 * r(seed, i, 3)); x.beginPath(); x.arc(r(seed, i, 1) * w, r(seed, i, 2) * hz * 0.9, U * 0.0018 * (1 + 2 * r(seed, i, 4)), 0, TAU); x.fill(); }
      const mx = w * (portrait ? 0.3 : 0.22), my = hz * 0.32; glowDisc(x, mx, my, U * 0.5, dark ? '#F4F8FF' : '#FFFFFF', dark ? 0.35 : 0.6); x.fillStyle = dark ? '#F8FAFF' : '#FFFFFF'; x.beginPath(); x.arc(mx, my, U * 0.05, 0, TAU); x.fill();
      x.fillStyle = dark ? mix(sc.bg, '#3A4A8A', 0.5) : mix(sc.bg, '#B8CCEA', 0.6); x.beginPath(); x.moveTo(0, hz); for (let i = 0; i <= 8; i++) x.lineTo(w * i / 8, hz - U * (0.1 + 0.12 * Math.sin(i * 1.2 + seed) ** 2)); x.lineTo(w, hz); x.fill();
      x.fillStyle = snow; x.beginPath(); x.moveTo(0, hz - U * 0.02); for (let i = 0; i <= 10; i++) x.lineTo(w * i / 10, hz - U * 0.03 * Math.sin(i * 0.9 + seed * 2) ** 2); x.lineTo(w, h); x.lineTo(0, h); x.fill();
      x.fillStyle = dark ? vertical(x, hz, h, [[0, rgba('#FFFFFF', 0.06)], [1, rgba(shade, 0.45)]]) : vertical(x, hz, h, [[0, rgba(shade, 0.5)], [0.45, rgba(shade, 0.12)], [1, rgba('#FFFFFF', 0.4)]]); x.fillRect(0, hz - U * 0.03, w, h - hz + U * 0.03);
      for (let i = 0; i < 5; i++) {                                                         /* snowy pines */
        const px = w * (i < 3 ? 0.05 + 0.09 * i : 0.9 - 0.06 * (i - 3)) * (portrait && i > 2 ? 1 : 1), py = hz + U * (0.02 + 0.03 * r(seed, i, 5)), ps = U * (0.14 + 0.1 * r(seed, i, 6));
        for (let k = 0; k < 4; k++) { const y = py - ps * (k * 0.28), ww = ps * (0.5 - k * 0.1); x.fillStyle = dark ? mix('#153A48', sc.bg, 0.3) : mix('#3F8A6A', sc.bg, 0.15); x.beginPath(); x.moveTo(px - ww, y); x.lineTo(px, y - ps * 0.42); x.lineTo(px + ww, y); x.fill(); x.fillStyle = snow; x.beginPath(); x.moveTo(px - ww * 0.75, y - ps * 0.12); x.lineTo(px, y - ps * 0.42); x.lineTo(px + ww * 0.75, y - ps * 0.12); x.quadraticCurveTo(px, y - ps * 0.2, px - ww * 0.75, y - ps * 0.12); x.fill(); }
      }
      x.fillStyle = rgba(shade, 0.35); x.beginPath(); x.ellipse(kx + kr * 0.15, ky + U * 0.012, kr * 1.12, U * 0.032, 0, 0, TAU); x.fill();
      /* the kamakura: a snow dome with a lit doorway */
      x.fillStyle = snow; x.beginPath(); x.moveTo(kx - kr, ky); x.bezierCurveTo(kx - kr, ky - kr * 1.25, kx + kr, ky - kr * 1.25, kx + kr, ky); x.closePath(); x.fill();
      x.fillStyle = vertical(x, ky - kr, ky, [[0, rgba('#FFFFFF', 0.35)], [1, rgba(shade, 0.35)]]); x.fill();
      x.strokeStyle = rgba(shade, 0.5); x.lineWidth = Math.max(1, U * 0.003); for (let i = 1; i < 4; i++) { x.beginPath(); x.ellipse(kx, ky, kr * (1 - i * 0.2), kr * (0.98 - i * 0.22), 0, Math.PI, TAU); x.stroke(); }
      const dw = kr * 0.5, dh = kr * 0.58; x.fillStyle = dark ? '#FFB45A' : '#FFD68A'; x.beginPath(); x.moveTo(kx - dw, ky); x.lineTo(kx - dw, ky - dh * 0.6); x.quadraticCurveTo(kx - dw, ky - dh, kx, ky - dh); x.quadraticCurveTo(kx + dw, ky - dh, kx + dw, ky - dh * 0.6); x.lineTo(kx + dw, ky); x.fill();
      x.fillStyle = rgba('#6A3A1A', 0.35); x.fillRect(kx - dw, ky - dh * 0.55, dw * 2, dh * 0.05);
      x.fillStyle = mix('#B8834A', sc.bg, 0.2); x.fillRect(kx - dw * 0.55, ky - dh * 0.24, dw * 1.1, dh * 0.24);            /* a low table */
      x.fillStyle = '#FFF6E6'; x.beginPath(); x.ellipse(kx - dw * 0.25, ky - dh * 0.3, dw * 0.15, dh * 0.06, 0, 0, TAU); x.ellipse(kx + dw * 0.28, ky - dh * 0.3, dw * 0.15, dh * 0.06, 0, 0, TAU); x.fill();   /* two mochi */
      /* a snowman on the left of the dome */
      const sx0 = kx - kr * 1.5, sy0 = ky + U * 0.03, sr = U * 0.078; x.fillStyle = snow; x.beginPath(); x.arc(sx0, sy0 - sr, sr, 0, TAU); x.fill(); x.beginPath(); x.arc(sx0, sy0 - sr * 2.4, sr * 0.7, 0, TAU); x.fill();
      x.fillStyle = rgba(shade, 0.25); x.beginPath(); x.arc(sx0 + sr * 0.3, sy0 - sr * 0.8, sr * 0.9, -0.6, 1.4); x.lineTo(sx0 + sr * 0.3, sy0 - sr * 0.8); x.fill();
      x.fillStyle = '#26304A'; x.beginPath(); x.arc(sx0 - sr * 0.25, sy0 - sr * 2.5, sr * 0.07, 0, TAU); x.arc(sx0 + sr * 0.25, sy0 - sr * 2.5, sr * 0.07, 0, TAU); x.fill(); x.fillStyle = '#FF8A3A'; x.beginPath(); x.moveTo(sx0, sy0 - sr * 2.4); x.lineTo(sx0 + sr * 0.75, sy0 - sr * 2.3); x.lineTo(sx0, sy0 - sr * 2.25); x.fill();
      x.fillStyle = sc.accent; x.fillRect(sx0 - sr * 0.7, sy0 - sr * 1.75, sr * 1.4, sr * 0.22); x.fillRect(sx0 + sr * 0.3, sy0 - sr * 1.75, sr * 0.22, sr * 0.6);
    });
    ctx.drawImage(scene, 0, 0, W, H);
    /* warm light spilling from the doorway, candle flicker, tiny snow lanterns with flames */
    const fl = 0.85 + 0.15 * Math.sin(t * 6) + 0.05 * Math.sin(t * 13);
    ctx.save(); ctx.globalCompositeOperation = dark ? 'screen' : 'source-over'; glowDisc(ctx, kx, ky - kr * 0.25, kr * (1.5 + 0.05 * fl), '#FFB45A', (dark ? 0.55 : 0.32) * fl); ctx.restore();
    ctx.fillStyle = rgba('#FFC870', dark ? 0.32 : 0.22); ctx.beginPath(); ctx.moveTo(kx - kr * 0.5, ky); ctx.lineTo(kx + kr * 0.5, ky); ctx.lineTo(kx + kr * 1.2, H); ctx.lineTo(kx - kr * 1.2, H); ctx.fill();
    for (let i = 0; i < 4; i++) { const lx = kx - kr * (2.1 + i * 0.5) * (i % 2 ? 1 : -1) * (i < 2 ? 1 : 1.1) + (i < 2 ? -U * 0.15 : U * 0.15), ly = H * (0.9 + 0.05 * (i % 2)); if (lx < 0 || lx > W) continue; const s = U * 0.03; ctx.fillStyle = dark ? mix('#DDE8FF', sc.bg, 0.4) : '#FFFFFF'; ctx.beginPath(); ctx.moveTo(lx - s, ly); ctx.bezierCurveTo(lx - s, ly - s * 1.8, lx + s, ly - s * 1.8, lx + s, ly); ctx.fill(); ctx.save(); ctx.globalCompositeOperation = dark ? 'screen' : 'source-over'; glowDisc(ctx, lx, ly - s * 0.5, s * 3, '#FFB45A', (dark ? 0.45 : 0.28) * (0.85 + 0.15 * Math.sin(t * 5 + i))); ctx.restore(); ctx.fillStyle = '#FFD68A'; ctx.beginPath(); ctx.ellipse(lx, ly - s * 0.5, s * 0.35, s * 0.6, 0, 0, TAU); ctx.fill(); }
    ctx.strokeStyle = rgba(dark ? '#DDE8FF' : '#FFFFFF', 0.5); ctx.lineWidth = Math.max(1.4, U * 0.006); ctx.lineCap = 'round';
    for (let k = 0; k < 2; k++) { ctx.beginPath(); for (let j = 0; j <= 12; j++) { const u = j / 12, xx = kx + (k ? 0.12 : -0.1) * kr + Math.sin(u * 5 + t * 1.3 + k * 2) * kr * 0.05 * u, yy = ky - kr * 1.03 - u * kr * 0.35; if (j) ctx.lineTo(xx, yy); else ctx.moveTo(xx, yy); } ctx.globalAlpha = e * 0.5; ctx.stroke(); ctx.globalAlpha = e; }
    for (let i = 0; i < 90; i++) { const z = r(seed, i, 51), sp = H * (0.03 + 0.08 * z), x = wrap(r(seed, i, 52) * W * 1.2 + Math.sin(t * 0.6 + i) * U * 0.03 * z - t * U * 0.02 * z, W * 1.2) - W * 0.1, y = wrap(r(seed, i, 53) * H * 1.2 + t * sp, H * 1.2) - H * 0.1; ctx.fillStyle = rgba(dark ? '#FFFFFF' : mix('#FFFFFF', sc.accent2, 0.15), 0.35 + 0.5 * z); ctx.beginPath(); ctx.arc(x, y, U * (0.0022 + 0.005 * z), 0, TAU); ctx.fill(); }
    ctx.save(); ctx.globalCompositeOperation = dark ? 'screen' : 'source-over'; for (let i = 0; i < 16; i++) { const p2 = Math.pow(Math.max(0, Math.sin(t * (0.7 + r(seed, i, 96)) + i * 2.9)), 4); ctx.fillStyle = rgba(dark ? '#E8F3FF' : '#FFFFFF', 0.95 * p2); star(ctx, r(seed, i, 97) * W, H * (0.66 + 0.32 * r(seed, i, 98)), U * 0.014 * (0.4 + p2), 4, 0.12); ctx.fill(); } ctx.restore();
  });

  /* ================= 雪の湯けむり ================= */
  K.registerBg('echoOnsen', '雪の湯けむり', ['calm', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), dark = isDark(sc.bg), portrait = H > W;
    const key = `onsen|${seed}|${colourKey(sc)}`, hz = H * 0.58, gy = H * 0.8;
    const snow = dark ? mix('#DDE8FF', sc.bg, 0.4) : mix('#FFFFFF', sc.bg, 0.12), shade = dark ? mix(sc.bg, '#0A1030', 0.5) : mix(sc.bg, '#9DB6DA', 0.45), wood = dark ? mix('#6A4A3A', sc.bg, 0.3) : mix('#8A5A3A', sc.bg, 0.15);
    const bx = W * (portrait ? 0.5 : 0.3), bw = U * (portrait ? 0.9 : 0.75), bh = U * 0.34, by = gy - U * 0.02;
    ctx.globalAlpha = e;
    const scene = layer(env, `${key}|scene`, 0.6, (x, w, h) => {
      x.fillStyle = vertical(x, 0, hz, [[0, dark ? mix(sc.bg, '#0A1236', 0.5) : mix(sc.bg, '#FFFFFF', 0.12)], [1, dark ? mix(sc.bg, '#5A5090', 0.4) : mix(sc.bg, '#FFFFFF', 0.6)]]); x.fillRect(0, 0, w, hz + 2);
      if (dark) for (let i = 0; i < 60; i++) { x.fillStyle = rgba('#FFFFFF', 0.25 + 0.6 * r(seed, i, 3)); x.beginPath(); x.arc(r(seed, i, 1) * w, r(seed, i, 2) * hz * 0.7, U * 0.0018 * (1 + 1.8 * r(seed, i, 4)), 0, TAU); x.fill(); }
      [[0.5, 0.32], [0.7, 0.2]].forEach(([lv, amp], n) => { x.fillStyle = dark ? mix(sc.bg, n ? '#22307A' : '#3B4A8A', 0.5) : mix(sc.bg, n ? '#B4C8E8' : '#CFDDF2', 0.7); x.beginPath(); x.moveTo(0, hz); for (let i = 0; i <= 8; i++) x.lineTo(w * i / 8, hz - U * (0.12 + amp * 0.5 * Math.sin(i * 1.3 + n * 2 + seed) ** 2)); x.lineTo(w, hz); x.fill(); });
      x.fillStyle = snow; x.fillRect(0, hz - 1, w, h - hz); x.fillStyle = dark ? vertical(x, hz, h, [[0, rgba('#FFFFFF', 0.2)], [1, rgba(shade, 0.4)]]) : vertical(x, hz, h, [[0, rgba(shade, 0.5)], [0.4, rgba(shade, 0.1)], [1, rgba('#FFFFFF', 0.4)]]); x.fillRect(0, hz - 1, w, h - hz);
      x.fillStyle = rgba(shade, 0.35); x.beginPath(); x.ellipse(bx + bw * 0.08, by + U * 0.012, bw * 0.55, U * 0.03, 0, 0, TAU); x.fill();
      /* the inn: timber body, deep snow-loaded roof, lit windows and a noren */
      x.fillStyle = wood; x.fillRect(bx - bw * 0.46, by - bh, bw * 0.92, bh); x.fillStyle = rgba('#000000', 0.15); for (let i = 0; i < 14; i++) x.fillRect(bx - bw * 0.46 + i * bw * 0.92 / 14, by - bh, U * 0.002, bh);
      x.fillStyle = dark ? '#3A2A30' : '#5A4046'; x.beginPath(); x.moveTo(bx - bw * 0.62, by - bh + U * 0.01); x.quadraticCurveTo(bx - bw * 0.3, by - bh - U * 0.01, bx, by - bh - bh * 0.5); x.quadraticCurveTo(bx + bw * 0.3, by - bh - U * 0.01, bx + bw * 0.62, by - bh + U * 0.01); x.closePath(); x.fill();
      x.fillStyle = snow; x.beginPath(); x.moveTo(bx - bw * 0.6, by - bh + U * 0.005); x.quadraticCurveTo(bx - bw * 0.3, by - bh - U * 0.018, bx, by - bh - bh * 0.5 - U * 0.012); x.quadraticCurveTo(bx + bw * 0.3, by - bh - U * 0.018, bx + bw * 0.6, by - bh + U * 0.005); x.quadraticCurveTo(bx + bw * 0.3, by - bh - U * 0.003, bx, by - bh - bh * 0.4); x.quadraticCurveTo(bx - bw * 0.3, by - bh - U * 0.003, bx - bw * 0.6, by - bh + U * 0.005); x.fill();
      for (let i = 0; i < 3; i++) { const wx = bx - bw * 0.38 + i * bw * 0.3; if (i === 1) continue; x.fillStyle = '#FFD68A'; x.fillRect(wx, by - bh * 0.72, bw * 0.18, bh * 0.36); x.strokeStyle = wood; x.lineWidth = U * 0.003; x.beginPath(); x.moveTo(wx + bw * 0.09, by - bh * 0.72); x.lineTo(wx + bw * 0.09, by - bh * 0.36); x.moveTo(wx, by - bh * 0.54); x.lineTo(wx + bw * 0.18, by - bh * 0.54); x.stroke(); }
      x.fillStyle = dark ? '#FFD68A' : '#FFE9B8'; x.fillRect(bx - bw * 0.11, by - bh * 0.62, bw * 0.22, bh * 0.62);
      x.fillStyle = sc.accent; x.fillRect(bx - bw * 0.1, by - bh * 0.62, bw * 0.2, bh * 0.3); x.fillStyle = '#FFFFFF'; x.font = `bold ${bh * 0.26}px "Yu Gothic","Noto Sans JP",sans-serif`; x.textAlign = 'center'; x.textBaseline = 'middle'; x.fillText('ゆ', bx, by - bh * 0.47);
      /* the open-air bath: rock rim and hot water */
      const px = bx + bw * (portrait ? 0.0 : 0.85), py = gy + U * 0.13, pw = U * (portrait ? 0.7 : 0.45);
      if (!portrait || true) { x.fillStyle = mix('#7A7688', sc.bg, 0.2); x.beginPath(); x.ellipse(px, py, pw * 0.56, pw * 0.2, 0, 0, TAU); x.fill(); x.fillStyle = dark ? mix('#3A9AC0', sc.bg, 0.3) : mix('#8FDCF0', sc.bg, 0.15); x.beginPath(); x.ellipse(px, py - pw * 0.012, pw * 0.5, pw * 0.16, 0, 0, TAU); x.fill(); x.fillStyle = snow; for (let i = 0; i < 12; i++) { const a = i / 12 * TAU; x.beginPath(); x.arc(px + Math.cos(a) * pw * 0.55, py + Math.sin(a) * pw * 0.19 - U * 0.006, U * (0.012 + 0.01 * r(seed, i, 7)), 0, TAU); x.fill(); } }
      for (let i = 0; i < 4; i++) { const lx = w * (0.62 + 0.1 * i) * (portrait ? 0.9 : 1), ly = gy + U * (0.02 + 0.02 * i); if (!portrait || i > 1) { x.fillStyle = shade; x.fillRect(lx - U * 0.004, ly - U * 0.08, U * 0.008, U * 0.08); x.fillStyle = '#7A7688'; x.fillRect(lx - U * 0.018, ly - U * 0.1, U * 0.036, U * 0.024); x.fillStyle = snow; x.beginPath(); x.ellipse(lx, ly - U * 0.105, U * 0.024, U * 0.01, 0, 0, TAU); x.fill(); } }
    });
    ctx.drawImage(scene, 0, 0, W, H);
    /* lanterns swinging under the eaves, glow breathing */
    for (let i = 0; i < 3; i++) { const lx = bx - bw * 0.42 + i * bw * 0.42 + Math.sin(t * 1.1 + i) * U * 0.004, ly = by - bh + U * 0.05; ctx.save(); ctx.globalCompositeOperation = dark ? 'screen' : 'source-over'; glowDisc(ctx, lx, ly, U * 0.13, '#FFB05A', (dark ? 0.5 : 0.25) * (0.85 + 0.15 * Math.sin(t * 4 + i * 2))); ctx.restore(); ctx.fillStyle = '#E04B3A'; ctx.beginPath(); ctx.ellipse(lx, ly, U * 0.018, U * 0.024, 0, 0, TAU); ctx.fill(); ctx.fillStyle = rgba('#FFE0A0', 0.7); ctx.beginPath(); ctx.ellipse(lx - U * 0.005, ly - U * 0.005, U * 0.008, U * 0.012, 0, 0, TAU); ctx.fill(); ctx.fillStyle = '#3A2A2A'; ctx.fillRect(lx - U * 0.008, ly - U * 0.028, U * 0.016, U * 0.005); }
    /* steam from the bath, in slow billows */
    const px = bx + bw * (portrait ? 0.0 : 0.85), py = H * 0.8 + U * 0.13, pw = U * (portrait ? 0.7 : 0.45);
    for (let i = 0; i < 14; i++) { const ph = wrap(t * 0.11 + i / 14, 1), x = px + Math.sin(ph * 6 + i * 1.7) * pw * 0.14 + (r(seed, i, 61) - 0.5) * pw * 0.4, y = py - ph * U * 0.5, s = U * (0.03 + 0.07 * ph); ctx.fillStyle = rgba('#FFFFFF', 0.22 * Math.sin(ph * Math.PI) * (dark ? 0.9 : 1.3)); ctx.beginPath(); ctx.arc(x, y, s, 0, TAU); ctx.fill(); }
    for (let i = 0; i < 100; i++) { const z = r(seed, i, 51), sp = H * (0.03 + 0.08 * z), x = wrap(r(seed, i, 52) * W * 1.2 + Math.sin(t * 0.6 + i) * U * 0.03 * z - t * U * 0.02 * z, W * 1.2) - W * 0.1, y = wrap(r(seed, i, 53) * H * 1.2 + t * sp, H * 1.2) - H * 0.1; ctx.fillStyle = rgba(dark ? '#FFFFFF' : mix('#FFFFFF', sc.accent2, 0.15), 0.35 + 0.5 * z); ctx.beginPath(); ctx.arc(x, y, U * (0.0022 + 0.005 * z), 0, TAU); ctx.fill(); }
    ctx.save(); ctx.globalCompositeOperation = dark ? 'screen' : 'source-over'; for (let i = 0; i < 16; i++) { const p2 = Math.pow(Math.max(0, Math.sin(t * (0.7 + r(seed, i, 96)) + i * 2.9)), 4); ctx.fillStyle = rgba(dark ? '#E8F3FF' : '#FFFFFF', 0.95 * p2); star(ctx, r(seed, i, 97) * W, H * (0.66 + 0.32 * r(seed, i, 98)), U * 0.014 * (0.4 + p2), 4, 0.12); ctx.fill(); } ctx.restore();
  });

  /* ================= 雨の窓辺 ================= */
  K.registerBg('echoWindowRain', '雨の窓辺', ['calm', 'emotional', 'editorial'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), dark = isDark(sc.bg), portrait = H > W;
    const key = `rainwin|${seed}|${colourKey(sc)}`, sill = H * 0.8;
    const frame = dark ? mix('#3A2A22', sc.bg, 0.25) : mix('#B98A5E', sc.bg, 0.3), lampC = dark ? '#FFB45A' : '#FFE0A8';
    ctx.globalAlpha = e;
    const scene = layer(env, `${key}|scene`, 0.6, (x, w, h) => {
      x.fillStyle = dark ? mix(sc.bg, '#0A0810', 0.35) : mix(sc.bg, '#FFF6E6', 0.4); x.fillRect(0, 0, w, h);
      /* the view outside: blurred lights and dark towers, seen through wet glass */
      const gx0 = w * 0.06, gx1 = w * 0.94, gy0 = h * 0.06;
      x.fillStyle = vertical(x, gy0, sill, [[0, dark ? mix('#1A1838', sc.bg, 0.3) : mix('#B8C4CF', sc.bg, 0.3)], [1, dark ? mix('#3A2A48', sc.bg, 0.3) : mix('#D9DCD8', sc.bg, 0.3)]]); x.fillRect(gx0, gy0, gx1 - gx0, sill - gy0);
      for (let i = 0; i < 26; i++) { const bw = U * (0.06 + 0.12 * r(seed, i, 1)), bhh = (sill - gy0) * (0.2 + 0.5 * r(seed, i, 2)), bx0 = gx0 + (gx1 - gx0) * r(seed, i, 3); x.fillStyle = dark ? rgba(mix('#12102A', sc.bg, 0.2), 0.8) : rgba(mix('#8E9AA6', sc.bg, 0.3), 0.5); x.fillRect(bx0, sill - bhh, bw, bhh); }
      const cols = dark ? [lampC, '#FF6A9A', '#7FD8FF', '#FFE07A'] : ['#FFFFFF', '#FFE0A8', '#BFE0FF']; for (let i = 0; i < 46; i++) { const px = gx0 + (gx1 - gx0) * r(seed, i, 11), py = gy0 + (sill - gy0) * (0.25 + 0.7 * r(seed, i, 12)), rr = U * (0.012 + 0.04 * r(seed, i, 13)); glowDisc(x, px, py, rr * 2.2, cols[i % cols.length], dark ? 0.5 : 0.35); x.fillStyle = rgba(cols[i % cols.length], dark ? 0.55 : 0.4); x.beginPath(); x.arc(px, py, rr, 0, TAU); x.fill(); }
      x.fillStyle = rgba(dark ? '#0A0812' : '#FFFFFF', 0.12); x.fillRect(gx0, gy0, gx1 - gx0, sill - gy0);
      /* window frame with a cross bar, sill and the wall */
      x.fillStyle = frame; const fw = U * 0.03; x.fillRect(0, 0, gx0, h); x.fillRect(gx1, 0, w - gx1, h); x.fillRect(gx0, 0, gx1 - gx0, gy0); x.fillRect(gx0, sill, gx1 - gx0, h - sill); x.fillRect(w * 0.5 - fw / 2, gy0, fw, sill - gy0); x.fillRect(gx0, (gy0 + sill) * 0.5 - fw / 2, gx1 - gx0, fw);
      x.fillStyle = rgba('#FFFFFF', dark ? 0.06 : 0.25); x.fillRect(gx0, sill, gx1 - gx0, U * 0.008); x.fillStyle = rgba('#000000', 0.18); x.fillRect(gx0, sill + U * 0.02, gx1 - gx0, U * 0.01);
      /* on the sill: a cup, a small plant, a book */
      const cx = w * (portrait ? 0.3 : 0.64), cy = sill, cs = U * 0.07;
      x.fillStyle = '#FFF6E6'; x.beginPath(); x.moveTo(cx - cs * 0.6, cy - cs * 0.7); x.lineTo(cx + cs * 0.6, cy - cs * 0.7); x.quadraticCurveTo(cx + cs * 0.55, cy, cx, cy); x.quadraticCurveTo(cx - cs * 0.55, cy, cx - cs * 0.6, cy - cs * 0.7); x.fill();
      x.strokeStyle = '#FFF6E6'; x.lineWidth = cs * 0.13; x.beginPath(); x.arc(cx + cs * 0.68, cy - cs * 0.4, cs * 0.24, -1.3, 1.3); x.stroke(); x.fillStyle = '#5A3420'; x.beginPath(); x.ellipse(cx, cy - cs * 0.68, cs * 0.55, cs * 0.1, 0, 0, TAU); x.fill(); x.fillStyle = rgba('#FFFFFF', 0.4); x.beginPath(); x.ellipse(cx, cy - cs * 0.3, cs * 0.5, cs * 0.06, 0, 0, TAU); x.fill();
      x.fillStyle = '#FFFFFF'; x.beginPath(); x.ellipse(cx, cy + cs * 0.02, cs * 0.9, cs * 0.12, 0, 0, TAU); x.fill();
      const bxx = cx - cs * 2.6; x.fillStyle = sc.accent; x.fillRect(bxx - cs * 0.8, cy - cs * 0.24, cs * 1.6, cs * 0.24); x.fillStyle = '#FFF6E6'; x.fillRect(bxx - cs * 0.76, cy - cs * 0.2, cs * 1.52, cs * 0.06); x.fillStyle = sc.accent2; x.fillRect(bxx - cs * 0.7, cy - cs * 0.48, cs * 1.4, cs * 0.24);
      const ppx = cx + cs * 2.4; x.fillStyle = '#B86A4A'; x.beginPath(); x.moveTo(ppx - cs * 0.42, cy - cs * 0.6); x.lineTo(ppx + cs * 0.42, cy - cs * 0.6); x.lineTo(ppx + cs * 0.3, cy); x.lineTo(ppx - cs * 0.3, cy); x.fill();
      for (let i = 0; i < 6; i++) { const a = -Math.PI / 2 + (i - 2.5) * 0.42; x.fillStyle = i % 2 ? '#4DB868' : '#3FA35A'; x.beginPath(); x.ellipse(ppx + Math.cos(a) * cs * 0.55, cy - cs * 0.7 + Math.sin(a) * cs * 0.55, cs * 0.34, cs * 0.14, a, 0, TAU); x.fill(); }
    });
    ctx.drawImage(scene, 0, 0, W, H);
    /* rain on the glass: drops that sit, then slide with a thin trail */
    const gx0 = W * 0.06, gx1 = W * 0.94, gy0 = H * 0.06;
    ctx.save(); ctx.beginPath(); ctx.rect(gx0, gy0, gx1 - gx0, sill - gy0); ctx.clip();
    for (let i = 0; i < 46; i++) {
      const cyc = 6 + 6 * r(seed, i, 71), ph = wrap(t + r(seed, i, 72) * cyc, cyc) / cyc, x = gx0 + (gx1 - gx0) * r(seed, i, 73), y0 = gy0 + (sill - gy0) * r(seed, i, 74) * 0.7, slide = clamp((ph - 0.55) / 0.45), y = y0 + slide * slide * (sill - gy0) * 0.6, s = U * (0.005 + 0.007 * r(seed, i, 75));
      if (slide > 0) { ctx.strokeStyle = rgba('#FFFFFF', dark ? 0.16 : 0.32); ctx.lineWidth = s * 0.5; ctx.beginPath(); ctx.moveTo(x, y0 + slide * slide * (sill - gy0) * 0.3); ctx.lineTo(x, y); ctx.stroke(); }
      ctx.fillStyle = rgba('#FFFFFF', dark ? 0.32 : 0.5); ctx.beginPath(); ctx.ellipse(x, y, s * 0.8, s * 1.15, 0, 0, TAU); ctx.fill(); ctx.fillStyle = rgba('#000000', dark ? 0.35 : 0.14); ctx.beginPath(); ctx.ellipse(x + s * 0.15, y + s * 0.2, s * 0.5, s * 0.75, 0, 0, TAU); ctx.fill(); ctx.fillStyle = rgba('#FFFFFF', 0.9); ctx.beginPath(); ctx.arc(x - s * 0.25, y - s * 0.35, s * 0.22, 0, TAU); ctx.fill();
    }
    ctx.restore();
    /* steam from the cup, with the lamp's warm glow across the pane */
    const cx = W * (portrait ? 0.3 : 0.64), cs = U * 0.07; ctx.strokeStyle = rgba('#FFFFFF', dark ? 0.35 : 0.55); ctx.lineWidth = Math.max(1.4, cs * 0.09); ctx.lineCap = 'round';
    for (let k = 0; k < 3; k++) { const ph = t * 0.9 + k * 2; ctx.beginPath(); for (let j = 0; j <= 12; j++) { const u = j / 12, xx = cx + (k - 1) * cs * 0.3 + Math.sin(u * 5 + ph) * cs * 0.16 * u, yy = sill - cs * 0.8 - u * cs * 1.7; if (j) ctx.lineTo(xx, yy); else ctx.moveTo(xx, yy); } ctx.stroke(); }
    ctx.save(); ctx.globalCompositeOperation = dark ? 'screen' : 'source-over'; glowDisc(ctx, W * (portrait ? 0.3 : 0.64), sill - U * 0.2, U * 0.6, lampC, dark ? 0.16 : 0.14); ctx.restore();
  });

  /* ================= レコード喫茶 ================= */
  K.registerBg('echoRecordCafe', 'レコード喫茶', ['calm', 'emotional', 'editorial', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), dark = isDark(sc.bg), portrait = H > W;
    const key = `records|${seed}|${colourKey(sc)}`, counter = H * 0.78, wood = dark ? mix('#5A3A2A', sc.bg, 0.3) : mix('#A8744A', sc.bg, 0.25), lampC = dark ? '#FFB45A' : '#FFE0A8';
    const tx = W * (portrait ? 0.5 : 0.7), ty = counter - U * 0.03, tr = U * (portrait ? 0.22 : 0.2);
    ctx.globalAlpha = e;
    const scene = layer(env, `${key}|scene`, 0.6, (x, w, h) => {
      x.fillStyle = dark ? mix(sc.bg, '#120C0A', 0.4) : mix(sc.bg, '#FFF1DC', 0.4); x.fillRect(0, 0, w, h);
      x.fillStyle = rgba(sc.accent, dark ? 0.1 : 0.12); const step = U * 0.06; for (let yy = 0; yy < counter; yy += step) for (let xx = (Math.floor(yy / step) % 2) * step / 2; xx < w; xx += step) { x.beginPath(); x.arc(xx, yy, U * 0.004, 0, TAU); x.fill(); }
      /* the record wall: shelves of sleeves in a run of colours */
      const shH = U * 0.19, rows = Math.ceil((counter * 0.75) / shH), palette = [sc.accent, sc.accent2, '#F2E6CE', mix(sc.accent, '#000000', 0.35), '#7FB8C8', '#E8B04A', '#B85A4A'];
      for (let rr = 0; rr < rows; rr++) { const y = counter * 0.05 + rr * shH; x.fillStyle = wood; x.fillRect(0, y + shH - U * 0.012, w, U * 0.014);
        for (let xx = 0, i = 0; xx < w; i++) { const sw = U * (0.03 + 0.012 * r(seed, rr, i, 1)); if (r(seed, rr, i, 2) > 0.1) { const c = palette[(rr * 3 + i + Math.floor(r(seed, rr, i, 3) * 4)) % palette.length]; x.fillStyle = mix(c, '#000000', dark ? 0.32 : 0.05); x.fillRect(xx, y + shH * 0.14, sw * 0.92, shH * 0.84); x.fillStyle = rgba('#FFFFFF', 0.16); x.fillRect(xx, y + shH * 0.14, sw * 0.92, shH * 0.04); x.fillStyle = rgba('#FFFFFF', 0.5); x.beginPath(); x.arc(xx + sw * 0.46, y + shH * 0.5, sw * 0.24, 0, TAU); x.fill(); x.fillStyle = rgba('#000000', 0.3); x.beginPath(); x.arc(xx + sw * 0.46, y + shH * 0.5, sw * 0.09, 0, TAU); x.fill(); } xx += sw; } }
      x.fillStyle = vertical(x, 0, counter, [[0, rgba('#000000', dark ? 0.35 : 0.12)], [0.3, rgba('#000000', 0)], [1, rgba('#000000', dark ? 0.3 : 0.1)]]); x.fillRect(0, 0, w, counter);
      /* the counter */
      x.fillStyle = wood; x.fillRect(0, counter, w, h - counter); x.fillStyle = rgba('#FFFFFF', dark ? 0.1 : 0.25); x.fillRect(0, counter, w, U * 0.01); x.fillStyle = rgba('#000000', 0.15); for (let i = 0; i < 26; i++) x.fillRect(r(seed, i, 5) * w, counter + U * 0.02 + r(seed, i, 6) * (h - counter - U * 0.04), U * (0.08 + 0.14 * r(seed, i, 7)), U * 0.002);
      /* turntable body and a speaker */
      x.fillStyle = dark ? '#2A1E1A' : '#3A2A22'; x.beginPath(); x.roundRect(tx - tr * 1.35, ty - tr * 0.5, tr * 2.7, tr * 0.72, tr * 0.06); x.fill(); x.fillStyle = rgba('#FFFFFF', 0.08); x.fillRect(tx - tr * 1.35, ty - tr * 0.5, tr * 2.7, tr * 0.05);
      x.fillStyle = '#C8A46A'; x.beginPath(); x.arc(tx + tr * 1.05, ty - tr * 0.38, tr * 0.06, 0, TAU); x.arc(tx - tr * 1.05, ty - tr * 0.38, tr * 0.06, 0, TAU); x.fill();
      const spx = w * (portrait ? 0.12 : 0.36), spw = U * 0.2; x.fillStyle = wood; x.fillRect(spx - spw / 2, counter - spw * 1.5, spw, spw * 1.5); x.fillStyle = dark ? '#1A120E' : '#3A2A22'; x.beginPath(); x.arc(spx, counter - spw * 0.5, spw * 0.34, 0, TAU); x.fill(); x.beginPath(); x.arc(spx, counter - spw * 1.15, spw * 0.18, 0, TAU); x.fill(); x.fillStyle = rgba('#FFFFFF', 0.18); x.beginPath(); x.arc(spx - spw * 0.05, counter - spw * 0.55, spw * 0.09, 0, TAU); x.fill();
    });
    ctx.drawImage(scene, 0, 0, W, H);
    /* pendant lamps with warm cones of light */
    for (let i = 0; i < (portrait ? 2 : 3); i++) { const lx = W * ((i + 0.5) / (portrait ? 2 : 3)), sw = Math.sin(t * 0.8 + i * 2) * U * 0.004, ly = H * 0.16 + U * 0.02 * (i % 2); ctx.strokeStyle = '#2A1E1A'; ctx.lineWidth = Math.max(1, U * 0.003); ctx.beginPath(); ctx.moveTo(lx, 0); ctx.lineTo(lx + sw, ly - U * 0.06); ctx.stroke();
      ctx.save(); ctx.globalCompositeOperation = dark ? 'screen' : 'source-over'; ctx.fillStyle = rgba(lampC, dark ? 0.1 : 0.16); ctx.beginPath(); ctx.moveTo(lx + sw - U * 0.05, ly); ctx.lineTo(lx + sw + U * 0.05, ly); ctx.lineTo(lx + sw + U * 0.32, H * 0.78); ctx.lineTo(lx + sw - U * 0.32, H * 0.78); ctx.fill(); glowDisc(ctx, lx + sw, ly, U * 0.22, lampC, dark ? 0.55 : 0.4); ctx.restore();
      ctx.fillStyle = i % 2 ? sc.accent : '#3A2A22'; ctx.beginPath(); ctx.moveTo(lx + sw - U * 0.07, ly); ctx.quadraticCurveTo(lx + sw - U * 0.06, ly - U * 0.06, lx + sw, ly - U * 0.07); ctx.quadraticCurveTo(lx + sw + U * 0.06, ly - U * 0.06, lx + sw + U * 0.07, ly); ctx.fill(); ctx.fillStyle = lampC; ctx.beginPath(); ctx.ellipse(lx + sw, ly, U * 0.055, U * 0.012, 0, 0, TAU); ctx.fill(); }
    /* the turntable: a record turning, tonearm resting on it */
    const spin = t * 3.6; ctx.save(); ctx.translate(tx, ty - tr * 0.48); ctx.scale(1, 0.26);
    ctx.fillStyle = '#15100E'; ctx.beginPath(); ctx.arc(0, 0, tr, 0, TAU); ctx.fill(); ctx.strokeStyle = rgba('#FFFFFF', 0.08); ctx.lineWidth = tr * 0.012; for (let g = 0; g < 8; g++) { ctx.beginPath(); ctx.arc(0, 0, tr * (0.36 + g * 0.075), 0, TAU); ctx.stroke(); }
    ctx.rotate(spin); ctx.fillStyle = rgba('#FFFFFF', 0.22); ctx.beginPath(); ctx.moveTo(0, 0); ctx.arc(0, 0, tr * 0.95, -0.3, 0.05); ctx.fill(); ctx.beginPath(); ctx.moveTo(0, 0); ctx.arc(0, 0, tr * 0.95, Math.PI - 0.3, Math.PI + 0.05); ctx.fill();
    ctx.fillStyle = sc.accent; ctx.beginPath(); ctx.arc(0, 0, tr * 0.3, 0, TAU); ctx.fill(); ctx.fillStyle = '#FFF6E6'; ctx.beginPath(); ctx.arc(tr * 0.14, 0, tr * 0.05, 0, TAU); ctx.fill(); ctx.restore();
    ctx.strokeStyle = '#C8A46A'; ctx.lineWidth = Math.max(1.5, tr * 0.03); ctx.lineCap = 'round'; ctx.beginPath(); ctx.moveTo(tx + tr * 1.15, ty - tr * 0.4); ctx.lineTo(tx + tr * 0.5, ty - tr * 0.5); ctx.lineTo(tx + tr * 0.16, ty - tr * 0.44); ctx.stroke(); ctx.fillStyle = '#C8A46A'; ctx.beginPath(); ctx.arc(tx + tr * 1.15, ty - tr * 0.4, tr * 0.05, 0, TAU); ctx.fill();
    /* music notes drifting from the speaker and the record */
    ctx.save(); ctx.globalCompositeOperation = dark ? 'screen' : 'source-over'; ctx.fillStyle = rgba(dark ? '#FFE0A8' : sc.accent, 0.8); ctx.font = `${U * 0.045}px "Yu Gothic","Noto Sans JP",serif`; ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    for (let i = 0; i < 7; i++) { const ph = wrap(t * 0.12 + i / 7, 1), fromX = i % 2 ? tx : W * (portrait ? 0.12 : 0.36), x = fromX + Math.sin(ph * 5 + i * 2) * U * 0.05 + ph * U * 0.08, y = counter - U * (0.1 + 0.55 * ph); ctx.globalAlpha = e * Math.sin(ph * Math.PI) * 0.85; ctx.fillText(['♪', '♫', '♩'][i % 3], x, y); }
    ctx.restore(); ctx.globalAlpha = e;
    for (let i = 0; i < 20; i++) { const x = r(seed, i, 81) * W + Math.sin(t * 0.3 + i) * U * 0.03, y = wrap(r(seed, i, 82) * H * 0.9 - t * H * 0.012, H * 0.9), b = 0.4 + 0.6 * Math.sin(t * 0.8 + i * 2) ** 2; ctx.fillStyle = rgba(lampC, 0.28 * b); ctx.beginPath(); ctx.arc(x, y, U * 0.0028, 0, TAU); ctx.fill(); }
  });
})();
