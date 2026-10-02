/* ECHO first-party backdrops for the default look 纸声: 水墨 (ink-wash mountains with a red sun and a seal), 和紙 (layered paper-cut hills and folded cranes),
   竹林 (brushed bamboo in moonlight), 錦鯉 (koi under lily pads, seen from above). Everything sits on a paper ground with fibres and grain. */
(() => {
  'use strict';
  const K = J.echo, TAU = K.TAU, clamp = K.clamp, mix = K.mix, rgba = K.rgba, paint = K.paint;
  const { U: Umin, isDark, wrap, glowDisc, star, vertical, fade, layer, scroll } = K;
  const r = J.r, ck = sc => [sc.bg, sc.fg, sc.accent, sc.accent2].join('');

  const paperTone = sc => {
    const dark = isDark(sc.bg);
    return dark
      ? { dark, paper: sc.bg, ink: sc.fg, red: sc.accent, tan: sc.accent2, wash: mix(sc.fg, sc.bg, 0.55), shadow: '#000000', fibre: mix(sc.fg, sc.bg, 0.8) }
      : { dark, paper: sc.bg, ink: sc.fg, red: sc.accent, tan: sc.accent2, wash: mix(sc.fg, sc.bg, 0.6), shadow: mix(sc.fg, sc.bg, 0.2), fibre: mix(sc.fg, sc.bg, 0.7) };
  };
  /* the ground: warm paper with long fibres, soft blotches and darker edges */
  const paperGround = (x, w, h, U, tone, seed) => {
    x.fillStyle = tone.paper; x.fillRect(0, 0, w, h);
    for (let i = 0; i < 14; i++) { const px = r(seed, i, 1) * w, py = r(seed, i, 2) * h, rr = U * (0.2 + 0.4 * r(seed, i, 3)), g = x.createRadialGradient(px, py, 0, px, py, rr); g.addColorStop(0, rgba(i % 2 ? tone.tan : tone.wash, tone.dark ? 0.05 : 0.07)); g.addColorStop(1, rgba(tone.wash, 0)); x.fillStyle = g; x.fillRect(px - rr, py - rr, rr * 2, rr * 2); }
    x.lineCap = 'round';
    for (let i = 0; i < 700; i++) { const px = r(seed, i, 11) * w, py = r(seed, i, 12) * h, a = r(seed, i, 13) * TAU, l = U * (0.008 + 0.03 * r(seed, i, 14)); x.strokeStyle = rgba(tone.fibre, (tone.dark ? 0.07 : 0.16) * r(seed, i, 15)); x.lineWidth = Math.max(0.5, U * 0.0007); x.beginPath(); x.moveTo(px, py); x.quadraticCurveTo(px + Math.cos(a + 1) * l * 0.5, py + Math.sin(a + 1) * l * 0.5, px + Math.cos(a) * l, py + Math.sin(a) * l); x.stroke(); }
    const vg = x.createRadialGradient(w / 2, h / 2, Math.min(w, h) * 0.35, w / 2, h / 2, Math.max(w, h) * 0.75); vg.addColorStop(0, rgba(tone.shadow, 0)); vg.addColorStop(1, rgba(tone.shadow, tone.dark ? 0.35 : 0.13)); x.fillStyle = vg; x.fillRect(0, 0, w, h);
  };
  const seal = (x, cx, cy, s, colour, paper) => {
    x.fillStyle = colour; x.beginPath(); x.roundRect(cx - s / 2, cy - s / 2, s, s, s * 0.06); x.fill();
    x.strokeStyle = paper; x.lineWidth = Math.max(1, s * 0.07); x.lineCap = 'square';
    x.beginPath(); x.moveTo(cx - s * 0.28, cy - s * 0.26); x.lineTo(cx + s * 0.28, cy - s * 0.26); x.moveTo(cx, cy - s * 0.26); x.lineTo(cx, cy + s * 0.3); x.moveTo(cx - s * 0.28, cy); x.lineTo(cx + s * 0.28, cy); x.moveTo(cx - s * 0.26, cy + s * 0.28); x.lineTo(cx - s * 0.05, cy + s * 0.05); x.moveTo(cx + s * 0.26, cy + s * 0.28); x.lineTo(cx + s * 0.05, cy + s * 0.05); x.stroke();
  };

  /* ================= 水墨 ================= */
  K.registerBg('echoSumi', '水墨', ['calm', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), tone = paperTone(sc), key = `sm|${seed}|${ck(sc)}`, portrait = H > W;
    const sunX = W * (portrait ? 0.68 : 0.7), sunY = H * (portrait ? 0.22 : 0.26);
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|paper`, 0.5, (x, w, h) => paperGround(x, w, h, U, tone, seed)), 0, 0, W, H);
    /* the sun: a vermilion disc bled into the paper (a pale moon on black paper) */
    ctx.drawImage(layer(env, `${key}|sun`, 0.5, (x, w, h) => {
      const c = tone.dark ? mix(tone.ink, tone.paper, 0.15) : tone.red;
      glowDisc(x, sunX, sunY, U * 0.28, c, tone.dark ? 0.16 : 0.22);
      if ('filter' in x) x.filter = `blur(${Math.max(1, U * 0.004).toFixed(1)}px)`;
      x.fillStyle = rgba(c, 0.94); x.beginPath(); x.arc(sunX, sunY, U * 0.088, 0, TAU); x.fill();
      x.filter = 'none';
      for (let i = 0; i < 90; i++) { const a = r(seed, i, 1) * TAU, d = U * 0.088 * Math.sqrt(r(seed, i, 2)); x.fillStyle = rgba(tone.paper, 0.05 + 0.1 * r(seed, i, 3)); x.beginPath(); x.arc(sunX + Math.cos(a) * d, sunY + Math.sin(a) * d, U * (0.002 + 0.006 * r(seed, i, 4)), 0, TAU); x.fill(); }
    }), 0, 0, W, H);
    const layers = [[0.5, 0.26, 0.16, 5, 0.2], [0.62, 0.3, 0.3, 3, 0.36], [0.76, 0.32, 0.5, 2, 0.55], [0.92, 0.24, 0.78, 1, 0.8]];
    layers.forEach(([level, amp, depth, blur, alpha], i) => {
      const ridge = layer(env, `${key}|m${i}`, i < 2 ? 0.4 : 0.55, (x, w, h) => {
        const fn = paint.profile(seed, i * 5 + 2, [[1, 0.45], [2 + (i % 2), 0.3], [4 + i, 0.2], [9 + i * 2, 0.1]]), sharp = u => Math.pow(fn(u), 1.7);
        if ('filter' in x) x.filter = `blur(${(U * 0.0011 * blur).toFixed(1)}px)`;
        const pts = paint.ridge(x, w, h, h * level, h * amp, sharp, top => vertical(x, top, top + h * (1 - level + amp) * 0.9, [[0, rgba(tone.ink, alpha)], [0.4, rgba(tone.ink, alpha * 0.55)], [1, rgba(tone.ink, 0)]]));
        x.filter = 'none';
        /* dry-brush strokes down the slopes */
        x.lineCap = 'round';
        for (let k = 0; k < 90 * (i + 1); k++) { const [px, py] = pts[Math.floor(r(seed, i, k, 1) * pts.length)], len = U * (0.02 + 0.06 * r(seed, i, k, 2)) * (0.6 + i * 0.3), a = Math.PI * (0.55 + 0.25 * (r(seed, i, k, 3) - 0.5)); x.strokeStyle = rgba(tone.ink, alpha * (0.15 + 0.35 * r(seed, i, k, 4))); x.lineWidth = Math.max(0.6, U * (0.0008 + 0.0018 * r(seed, i, k, 5))); x.beginPath(); x.moveTo(px, py + U * 0.004); x.quadraticCurveTo(px + Math.cos(a) * len * 0.4 + U * 0.004, py + Math.sin(a) * len * 0.4, px + Math.cos(a) * len * 0.5 - U * 0.006, py + Math.sin(a) * len); x.stroke(); }
        if (i === 3) for (let k = 0; k < 7; k++) {                     /* pines clinging to the nearest ridge */
          const px = w * (0.04 + 0.1 * k) * (k < 4 ? 1 : 0.4) + (k >= 4 ? w * 0.62 : 0), [, py] = pts[Math.min(pts.length - 1, Math.max(0, Math.round(px / 6) + 1))], hh = U * (0.09 + 0.1 * r(seed, k, 9));
          x.strokeStyle = rgba(tone.ink, 0.85); x.lineWidth = Math.max(1.5, U * 0.004); x.beginPath(); x.moveTo(px, py + U * 0.01); x.quadraticCurveTo(px + hh * 0.12, py - hh * 0.5, px - hh * 0.05, py - hh); x.stroke();
          for (let b = 0; b < 4; b++) { const by = py - hh * (0.35 + 0.2 * b), bx = px + (b % 2 ? 1 : -1) * hh * 0.06; x.lineWidth = Math.max(1, U * 0.0028); x.beginPath(); x.moveTo(bx, by); x.lineTo(bx + (b % 2 ? 1 : -1) * hh * 0.34, by - hh * 0.05); x.stroke();
            for (let n = 0; n < 26; n++) { const a = -Math.PI * 0.9 + r(seed, k, b, n) * Math.PI * 0.8, ll = hh * (0.05 + 0.07 * r(seed, k, b, n, 2)), cx = bx + (b % 2 ? 1 : -1) * hh * 0.34; x.lineWidth = Math.max(0.8, U * 0.0016); x.beginPath(); x.moveTo(cx, by - hh * 0.05); x.lineTo(cx + Math.cos(a) * ll, by - hh * 0.05 + Math.sin(a) * ll); x.stroke(); } }
        }
      });
      K.bandScroll(ctx, ridge, env, t * U * (0.0012 + 0.003 * depth) * (i % 2 ? 1 : -1), H * (level - amp) - U * (i === 3 ? 0.22 : 0.03), H);
      if (i < 3) { const mist = layer(env, `${key}|mist${i}`, 0.3, (x, w, h) => paint.fog(x, w, h, h * (level + 0.03), h * 0.1, tone.paper, 0.85, seed + i * 7, 6)); K.bandScroll(ctx, mist, env, t * U * (0.005 + 0.006 * depth) * (i % 2 ? -1 : 1) + i * 90, H * (level + 0.03 - 0.11), H * (level + 0.03 + 0.11)); }
    });
    /* an ink drop falls on the water and opens */
    const cyc = 9, dp = wrap(t + r(seed, 6) * cyc, cyc) / cyc, dx = W * (portrait ? 0.35 : 0.3), dy = H * 0.9;
    for (let k = 0; k < 3; k++) { const ph = clamp(dp * 1.6 - k * 0.18); if (ph <= 0 || ph >= 1) continue; ctx.strokeStyle = rgba(tone.ink, 0.5 * (1 - ph)); ctx.lineWidth = Math.max(1, U * 0.0022 * (1 - ph)); ctx.beginPath(); ctx.ellipse(dx, dy, U * (0.01 + 0.13 * ph), U * (0.003 + 0.028 * ph), 0, 0, TAU); ctx.stroke(); }
    paint.birds(ctx, t, W, H, U, seed, rgba(tone.ink, 0.65), 4, 0.18);
    /* boat with a fisherman */
    const bx = W * (portrait ? 0.62 : 0.56) + Math.sin(t * 0.15) * U * 0.03, by = H * 0.9 + Math.sin(t * 0.9) * U * 0.002;
    ctx.strokeStyle = rgba(tone.ink, 0.9); ctx.lineWidth = Math.max(1.5, U * 0.004); ctx.lineCap = 'round'; ctx.beginPath(); ctx.moveTo(bx - U * 0.06, by - U * 0.004); ctx.quadraticCurveTo(bx, by + U * 0.014, bx + U * 0.06, by - U * 0.008); ctx.stroke();
    ctx.fillStyle = rgba(tone.ink, 0.9); ctx.beginPath(); ctx.moveTo(bx - U * 0.008, by - U * 0.028); ctx.lineTo(bx + U * 0.014, by - U * 0.028); ctx.lineTo(bx + U * 0.012, by - U * 0.005); ctx.lineTo(bx - U * 0.008, by - U * 0.005); ctx.fill(); ctx.beginPath(); ctx.moveTo(bx - U * 0.024, by - U * 0.03); ctx.lineTo(bx + U * 0.018, by - U * 0.03); ctx.lineTo(bx - U * 0.003, by - U * 0.05); ctx.fill();
    ctx.lineWidth = Math.max(1, U * 0.0016); ctx.beginPath(); ctx.moveTo(bx + U * 0.014, by - U * 0.024); ctx.lineTo(bx + U * 0.09, by - U * 0.07); ctx.stroke();
    for (let k = 0; k < 6; k++) { ctx.strokeStyle = rgba(tone.ink, 0.22 - k * 0.028); ctx.beginPath(); ctx.moveTo(bx - U * (0.08 + k * 0.02), by + U * (0.012 + k * 0.007)); ctx.lineTo(bx + U * (0.08 + k * 0.02), by + U * (0.012 + k * 0.007)); ctx.stroke(); }
    ctx.drawImage(layer(env, `${key}|seal`, 0.5, (x, w, h) => { x.globalAlpha = 0.92; seal(x, w * (portrait ? 0.86 : 0.93), h * 0.9, U * 0.07, tone.red, tone.paper); }), 0, 0, W, H);
  });

  /* ================= 和紙 ================= */
  const crane = (x, s, c1, c2, flap) => {
    x.save(); x.lineJoin = 'round';
    const body = (pts, col) => { x.fillStyle = col; x.beginPath(); pts.forEach(([px, py], i) => i ? x.lineTo(px * s, py * s) : x.moveTo(px * s, py * s)); x.closePath(); x.fill(); };
    body([[-0.5, 0.02], [-0.08, -0.06], [0.28, 0.02], [-0.08, 0.12]], c1);                     /* body */
    body([[0.28, 0.02], [0.62, -0.2], [0.58, -0.16], [0.24, 0.0]], c1);                       /* neck */
    body([[0.62, -0.2], [0.78, -0.17], [0.6, -0.15]], c2);                                    /* head */
    body([[-0.5, 0.02], [-0.72, 0.09], [-0.38, 0.06]], c2);                                   /* tail */
    x.save(); x.translate(-0.06 * s, -0.03 * s); x.scale(1, 0.3 + 0.7 * flap); body([[0, 0], [-0.12, -0.5], [0.3, -0.16]], c2); x.restore();
    x.save(); x.translate(-0.06 * s, 0.0); x.scale(1, -(0.3 + 0.7 * flap)); body([[0, 0], [-0.2, -0.42], [0.34, -0.12]], mix(c1, c2, 0.5)); x.restore();
    x.restore();
  };
  K.registerBg('echoWashi', '和紙', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), tone = paperTone(sc), key = `wa|${seed}|${ck(sc)}`, portrait = H > W;
    ctx.globalAlpha = e;
    const cols = tone.dark
      ? [mix(sc.bg, sc.accent2, 0.3), mix(sc.bg, sc.accent, 0.35), mix(sc.bg, sc.accent2, 0.55), mix(sc.bg, sc.accent, 0.6), mix(sc.bg, '#000000', 0.35)]
      : [mix(sc.bg, sc.accent2, 0.45), mix(sc.bg, sc.accent, 0.35), mix(sc.bg, sc.accent2, 0.7), mix(sc.bg, sc.accent, 0.62), mix(sc.fg, sc.bg, 0.25)];
    ctx.drawImage(layer(env, `${key}|paper`, 0.5, (x, w, h) => { paperGround(x, w, h, U, tone, seed); }), 0, 0, W, H);
    /* paper sun and paper clouds, each casting a shadow */
    ctx.drawImage(layer(env, `${key}|sky`, 0.5, (x, w, h) => {
      const sx = w * (portrait ? 0.68 : 0.72), sy = h * 0.27;
      x.shadowColor = rgba(tone.shadow, tone.dark ? 0.6 : 0.3); x.shadowBlur = U * 0.02; x.shadowOffsetY = U * 0.008;
      x.fillStyle = tone.dark ? mix(sc.fg, sc.bg, 0.15) : tone.red; x.beginPath(); x.arc(sx, sy, U * 0.11, 0, TAU); x.fill();
      x.fillStyle = tone.dark ? '#F4EEDC' : '#FFFFFF';
      for (let i = 0; i < 4; i++) { const cx = w * (0.1 + 0.26 * i + 0.05 * r(seed, i, 1)), cy = h * (0.16 + 0.14 * r(seed, i, 2)), cw = U * (0.16 + 0.1 * r(seed, i, 3)); x.beginPath(); x.roundRect(cx - cw / 2, cy, cw, cw * 0.22, cw * 0.11); x.arc(cx - cw * 0.14, cy, cw * 0.15, 0, TAU); x.arc(cx + cw * 0.08, cy - cw * 0.03, cw * 0.19, 0, TAU); x.fill(); }
    }), 0, 0, W, H);
    /* five cut layers of hills, each slowly sliding, each throwing a shadow on the layer behind */
    const levels = [[0.5, 0.16, 0], [0.6, 0.18, 1], [0.7, 0.2, 2], [0.8, 0.2, 3], [0.9, 0.16, 4]];
    levels.forEach(([level, amp, ci], i) => {
      const sheet = layer(env, `${key}|h${i}`, 0.55, (x, w, h) => {
        const fn = paint.profile(seed, i * 11 + 3, [[1 + (i % 2), 0.5], [3 + i, 0.3], [6 + i * 2, 0.2]]);
        x.shadowColor = rgba(tone.shadow, tone.dark ? 0.7 : 0.4); x.shadowBlur = U * 0.03; x.shadowOffsetY = -U * 0.012;
        const pts = paint.ridge(x, w, h, h * level, h * amp, fn, top => vertical(x, top, h, [[0, cols[ci]], [1, mix(cols[ci], tone.dark ? '#000000' : tone.ink, 0.22)]]), 5);
        x.shadowColor = 'transparent';
        x.strokeStyle = rgba('#FFFFFF', tone.dark ? 0.16 : 0.5); x.lineWidth = Math.max(1, U * 0.0018); x.beginPath(); pts.forEach(([px, py], k) => k ? x.lineTo(px, py + U * 0.0015) : x.moveTo(px, py + U * 0.0015)); x.stroke();
        for (let k = 0; k < 60; k++) { x.fillStyle = rgba(k % 2 ? '#FFFFFF' : tone.ink, tone.dark ? 0.05 : 0.06); x.fillRect(r(seed, i, k, 1) * w, h * level + (h - h * level) * r(seed, i, k, 2), U * (0.02 + 0.06 * r(seed, i, k, 3)), Math.max(1, U * 0.0012)); }
      });
      scroll(ctx, sheet, env, t * U * (0.0016 + 0.004 * i) * (i % 2 ? 1 : -1), 1, Math.sin(t * 0.3 + i) * U * 0.004);
    });
    /* folded cranes crossing, flapping out of step */
    for (let c = 0; c < 5; c++) {
      const sp = (t / (34 + 6 * c) + r(seed, c, 1)) % 1, cx = W * (-0.1 + 1.2 * sp), cy = H * (0.22 + 0.22 * r(seed, c, 2)) + Math.sin(t * 0.8 + c * 2) * U * 0.03 + sp * H * 0.06, s = U * (0.07 + 0.05 * r(seed, c, 3)), flap = 0.5 + 0.5 * Math.sin(t * 2.4 + c * 1.9);
      ctx.save(); ctx.translate(cx, cy); ctx.rotate(0.12 + Math.sin(t * 0.5 + c) * 0.05);
      ctx.shadowColor = rgba(tone.shadow, 0.35); ctx.shadowBlur = U * 0.012; ctx.shadowOffsetY = U * 0.014;
      crane(ctx, s, c % 2 ? '#FFFFFF' : mix(tone.red, '#FFFFFF', 0.1), c % 2 ? mix(tone.red, '#FFFFFF', 0.3) : mix('#FFFFFF', tone.tan, 0.35), flap); ctx.restore();
    }
    ctx.shadowColor = 'transparent';
    for (let i = 0; i < 26; i++) { const life = wrap(t * (0.03 + 0.02 * r(seed, i, 41)) + r(seed, i, 42), 1), px = r(seed, i, 43) * W + Math.sin(t * 0.6 + i) * U * 0.04, py = -U * 0.02 + life * H * 1.05; ctx.save(); ctx.translate(px, py); ctx.rotate(t * 0.6 + i); ctx.scale(1, Math.cos(t * 1.4 + i)); ctx.fillStyle = rgba(i % 2 ? tone.red : '#FFFFFF', 0.85); ctx.fillRect(-U * 0.005, -U * 0.005, U * 0.01, U * 0.01); ctx.restore(); }
  });

  /* ================= 竹林 ================= */
  const leafSprite = (ctx, colour, hi) => K.sprite(ctx, `bl|${colour}|${hi}`, 120, 60, (g, w, h) => {
    g.translate(0, h * 0.5);
    for (let i = 0; i < 7; i++) {
      const a = -0.5 + i * 0.16 + (i % 2) * 0.05, l = w * (0.62 + 0.3 * ((i * 7) % 5) / 5);
      g.save(); g.rotate(a); const gr = g.createLinearGradient(0, 0, l, 0); gr.addColorStop(0, colour); gr.addColorStop(1, hi);
      g.fillStyle = gr; g.beginPath(); g.moveTo(0, 0); g.quadraticCurveTo(l * 0.5, -h * 0.13, l, 0); g.quadraticCurveTo(l * 0.5, h * 0.03, 0, 0); g.fill(); g.restore();
    }
  });
  K.registerBg('echoBambooInk', '竹林', ['calm', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), tone = paperTone(sc), key = `bb|${seed}|${ck(sc)}`, portrait = H > W;
    const mx = W * (portrait ? 0.7 : 0.66), my = H * 0.3;
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|paper`, 0.5, (x, w, h) => {
      paperGround(x, w, h, U, tone, seed);
      glowDisc(x, mx, my, U * 0.55, tone.dark ? tone.ink : tone.tan, tone.dark ? 0.14 : 0.2);
      if ('filter' in x) x.filter = `blur(${Math.max(1, U * 0.0035).toFixed(1)}px)`;
      x.fillStyle = rgba(tone.dark ? mix(tone.ink, tone.paper, 0.1) : tone.red, tone.dark ? 0.95 : 0.9); x.beginPath(); x.arc(mx, my, U * 0.11, 0, TAU); x.fill(); x.filter = 'none';
      for (let i = 0; i < 6; i++) { x.fillStyle = rgba(tone.paper, 0.1); x.beginPath(); x.arc(mx + (r(seed, i, 1) - 0.5) * U * 0.14, my + (r(seed, i, 2) - 0.5) * U * 0.14, U * (0.01 + 0.02 * r(seed, i, 3)), 0, TAU); x.fill(); }
    }), 0, 0, W, H);
    /* stalks at three depths: pale and thin far away, dark and thick near */
    const groups = [[16, 0.22, 0.012, 0.16, 0.5], [11, 0.34, 0.02, 0.32, 0.75], [7, 0.5, 0.032, 0.62, 1]];
    groups.forEach(([n, top, width, alpha, blurK], gi) => {
      const stalks = layer(env, `${key}|s${gi}`, 0.55, (x, w, h) => {
        if ('filter' in x) x.filter = `blur(${(U * 0.0008 * (3 - gi)).toFixed(1)}px)`;
        for (let i = 0; i < n; i++) {
          const px = w * (i + 0.3 + 0.7 * r(seed, gi, i, 1)) / n, sw = U * width * (0.75 + 0.5 * r(seed, gi, i, 2)), lean = (r(seed, gi, i, 3) - 0.5) * U * 0.02, seg = U * (0.09 + 0.05 * r(seed, gi, i, 4)) * (1 + gi * 0.4);
          const topY = h * top * (0.25 + 0.75 * r(seed, gi, i, 5)) * (gi === 2 ? 0.2 : 1);
          for (let y = h + seg, k = 0; y > topY - seg; y -= seg, k++) {
            const x0 = px + lean * (1 - y / h), x1 = px + lean * (1 - (y - seg) / h);
            x.fillStyle = rgba(tone.ink, alpha * (0.72 + 0.28 * r(seed, gi, i, k, 6))); x.beginPath(); x.moveTo(x0 - sw / 2, y); x.lineTo(x1 - sw / 2, y - seg + sw * 0.35); x.lineTo(x1 + sw / 2, y - seg + sw * 0.35); x.lineTo(x0 + sw / 2, y); x.fill();
            x.fillStyle = rgba(tone.paper, 0.35 * alpha); x.fillRect(x0 - sw * 0.28, y - seg + sw * 0.4, sw * 0.1, seg - sw * 0.6);
            x.fillStyle = rgba(tone.ink, alpha * 0.95); x.fillRect(x1 - sw * 0.62, y - seg - sw * 0.05, sw * 1.24, sw * 0.32);
            if (k > 1 && r(seed, gi, i, k, 7) > 0.6) { const dir = r(seed, gi, i, k, 8) > 0.5 ? 1 : -1; x.strokeStyle = rgba(tone.ink, alpha * 0.9); x.lineWidth = Math.max(0.8, sw * 0.18); x.lineCap = 'round'; x.beginPath(); x.moveTo(x1, y - seg); x.quadraticCurveTo(x1 + dir * seg * 0.35, y - seg - seg * 0.12, x1 + dir * seg * 0.75, y - seg - seg * 0.32); x.stroke(); }
          }
        }
        x.filter = 'none';
      });
      scroll(ctx, stalks, env, Math.sin(t * 0.2 + gi) * U * 0.004 * (gi + 1));
    });
    /* leaves swaying at the nodes of the middle and near stalks */
    const lc = tone.dark ? mix(tone.ink, tone.paper, 0.2) : tone.ink, sprite = leafSprite(ctx, rgba(lc, 0.9), rgba(lc, 0.2));
    for (let gi = 1; gi < 3; gi++) {
      const n = groups[gi][0] * 2 + 3;
      for (let i = 0; i < n; i++) {
        const px = W * (i + 0.3 + 0.7 * r(seed, gi, i, 21)) / n, py = H * (0.08 + 0.55 * r(seed, gi, i, 22)), s = U * (0.12 + 0.07 * gi) * (0.7 + 0.5 * r(seed, gi, i, 23)), flip = r(seed, gi, i, 24) > 0.5 ? 1 : -1;
        ctx.save(); ctx.globalAlpha = e * (gi === 1 ? 0.6 : 0.95); ctx.translate(px, py); ctx.scale(flip, 1); ctx.rotate(Math.sin(t * (0.7 + 0.3 * r(seed, gi, i, 25)) + i * 1.7) * 0.07 + 0.15 + 0.5 * (r(seed, gi, i, 26) - 0.5)); ctx.drawImage(sprite, 0, -s * 0.25, s, s * 0.5); ctx.restore();
      }
    }
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|ground`, 0.5, (x, w, h) => {
      x.fillStyle = vertical(x, h * 0.9, h, [[0, rgba(tone.ink, 0)], [1, rgba(tone.ink, tone.dark ? 0.4 : 0.32)]]); x.fillRect(0, h * 0.9, w, h * 0.1);
      for (let i = 0; i < 5; i++) { const sx = w * (0.08 + 0.2 * i + 0.05 * r(seed, i, 31)), sy = h * 0.985, sr = U * (0.02 + 0.03 * r(seed, i, 32)); x.fillStyle = rgba(tone.ink, 0.55); x.beginPath(); x.ellipse(sx, sy, sr * 1.4, sr * 0.7, 0, Math.PI, TAU); x.fill(); }
      x.globalAlpha = 0.92; seal(x, w * (portrait ? 0.14 : 0.07), h * 0.88, U * 0.062, tone.red, tone.paper);
    }), 0, 0, W, H);
    for (let i = 0; i < 14; i++) {                                     /* single leaves let go */
      const life = wrap(t * (0.04 + 0.03 * r(seed, i, 41)) + r(seed, i, 42), 1), px = r(seed, i, 43) * W + Math.sin(t * 0.8 + i * 2) * U * 0.06 + life * U * 0.08, py = life * H * 1.05 - U * 0.02;
      ctx.save(); ctx.translate(px, py); ctx.rotate(Math.sin(t + i) * 0.8 + i); ctx.fillStyle = rgba(tone.ink, 0.6 * Math.sin(Math.PI * life)); ctx.beginPath(); ctx.moveTo(-U * 0.02, 0); ctx.quadraticCurveTo(0, -U * 0.005, U * 0.02, 0); ctx.quadraticCurveTo(0, U * 0.002, -U * 0.02, 0); ctx.fill(); ctx.restore();
    }
  });

  /* ================= 錦鯉 ================= */
  const koiPath = (k, tt, seed, W, H, U) => {
    const cx = W * (0.3 + 0.4 * r(seed, k, 1)), cy = H * (0.35 + 0.3 * r(seed, k, 2)), rx = W * (0.14 + 0.16 * r(seed, k, 3)), ry = H * (0.12 + 0.14 * r(seed, k, 4)), sp = 0.16 + 0.1 * r(seed, k, 5), ph = r(seed, k, 6) * TAU;
    return [cx + Math.cos(tt * sp + ph) * rx + Math.sin(tt * sp * 2.3 + ph) * rx * 0.22, cy + Math.sin(tt * sp * 1.31 + ph) * ry + Math.cos(tt * sp * 0.7) * ry * 0.3];
  };
  const drawKoi = (ctx, t, k, seed, W, H, U, tone, pal) => {
    const N = 16, len = U * (0.3 + 0.06 * r(seed, k, 9)), spine = [];
    for (let i = 0; i < N; i++) { const [x, y] = koiPath(k, t - i * 0.14, seed, W, H, U), s = i / (N - 1); spine.push([x + Math.sin(t * 4 + i * 0.6 + k) * len * 0.02 * s, y + Math.cos(t * 4 + i * 0.6 + k) * len * 0.02 * s]); }
    const scaleD = len / Math.hypot(spine[0][0] - spine[N - 1][0], spine[0][1] - spine[N - 1][1]) ; void scaleD;
    const pts = spine.map(([x, y], i) => { const [x0, y0] = spine[0]; return [x0 + (x - x0) * 1.0, y0 + (y - y0) * 1.0]; });
    const wAt = s => len * 0.12 * Math.pow(Math.sin(Math.PI * clamp(0.1 + (s * 0.88))), 0.85) * (s < 0.12 ? 0.6 + s * 3 : 1);
    const left = [], right = [];
    for (let i = 0; i < N; i++) { const a = pts[Math.max(0, i - 1)], b = pts[Math.min(N - 1, i + 1)], dx = b[0] - a[0], dy = b[1] - a[1], l = Math.hypot(dx, dy) || 1, nx = -dy / l, ny = dx / l, w = wAt(i / (N - 1)); left.push([pts[i][0] + nx * w, pts[i][1] + ny * w]); right.push([pts[i][0] - nx * w, pts[i][1] - ny * w]); }
    const body = new Path2D(); body.moveTo(left[0][0], left[0][1]); left.forEach(([x, y]) => body.lineTo(x, y)); for (let i = N - 1; i >= 0; i--) body.lineTo(right[i][0], right[i][1]); body.closePath();
    /* shadow on the pond floor */
    ctx.save(); ctx.translate(U * 0.012, U * 0.018); ctx.fillStyle = rgba('#000000', tone.dark ? 0.25 : 0.12); ctx.fill(body); ctx.restore();
    const head = pts[0], tail = pts[N - 1], tail2 = pts[N - 3], tdx = tail[0] - tail2[0], tdy = tail[1] - tail2[1], tl = Math.hypot(tdx, tdy) || 1;
    /* tail fin and pectoral fins, translucent */
    ctx.fillStyle = rgba(mix(pal.base, '#FFFFFF', 0.5), 0.5);
    const fx = tail[0] + tdx / tl * len * 0.18, fy = tail[1] + tdy / tl * len * 0.18, sway = Math.sin(t * 4 + k) * 0.35;
    ctx.beginPath(); ctx.moveTo(tail[0], tail[1]); ctx.quadraticCurveTo(fx + (-tdy / tl) * len * (0.14 + sway * 0.1), fy + (tdx / tl) * len * (0.14 + sway * 0.1), tail[0] + tdx / tl * len * 0.26 + (-tdy / tl) * len * 0.02, tail[1] + tdy / tl * len * 0.26 + (tdx / tl) * len * 0.02); ctx.quadraticCurveTo(fx - (-tdy / tl) * len * (0.14 - sway * 0.1), fy - (tdx / tl) * len * (0.14 - sway * 0.1), tail[0], tail[1]); ctx.fill();
    [-1, 1].forEach(sd => { const a = pts[3], b = pts[2], dx = b[0] - a[0], dy = b[1] - a[1], l = Math.hypot(dx, dy) || 1; ctx.beginPath(); ctx.ellipse(a[0] + (-dy / l) * sd * len * 0.1, a[1] + (dx / l) * sd * len * 0.1, len * 0.08, len * 0.028, Math.atan2(dy, dx) + sd * (0.9 + 0.3 * Math.sin(t * 3 + k)), 0, TAU); ctx.fill(); });
    ctx.save(); ctx.clip(body);
    ctx.fillStyle = pal.base; ctx.fillRect(0, 0, W, H);
    pal.patches.forEach(([s0, s1, col, off]) => { const i0 = Math.round(s0 * (N - 1)), i1 = Math.round(s1 * (N - 1)); ctx.fillStyle = col; ctx.beginPath(); for (let i = i0; i <= i1; i++) { const p = left[i], q = pts[i]; const x = q[0] + (p[0] - q[0]) * off, y = q[1] + (p[1] - q[1]) * off; i === i0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y); } for (let i = i1; i >= i0; i--) { const p = right[i], q = pts[i]; const x = q[0] + (p[0] - q[0]) * (off * 0.8 - 0.1), y = q[1] + (p[1] - q[1]) * (off * 0.8 - 0.1); ctx.lineTo(x, y); } ctx.closePath(); ctx.fill(); });
    ctx.fillStyle = rgba('#FFFFFF', 0.16); ctx.beginPath(); ctx.moveTo(left[1][0], left[1][1]); for (let i = 1; i < N - 3; i++) ctx.lineTo((left[i][0] + pts[i][0]) / 2, (left[i][1] + pts[i][1]) / 2); for (let i = N - 4; i >= 1; i--) ctx.lineTo(pts[i][0], pts[i][1]); ctx.fill();
    ctx.restore();
    ctx.fillStyle = '#1A1A1A'; [-1, 1].forEach(sd => { const a = pts[0], b = pts[1], dx = b[0] - a[0], dy = b[1] - a[1], l = Math.hypot(dx, dy) || 1; ctx.beginPath(); ctx.arc(a[0] + dx / l * len * 0.03 + (-dy / l) * sd * len * 0.04, a[1] + dy / l * len * 0.03 + (dx / l) * sd * len * 0.04, len * 0.014, 0, TAU); ctx.fill(); });
  };
  K.registerBg('echoKoi', '錦鯉', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), tone = paperTone(sc), key = `ko|${seed}|${ck(sc)}`;
    ctx.globalAlpha = e;
    const water0 = tone.dark ? mix(sc.bg, sc.accent2, 0.18) : mix(sc.bg, sc.accent2, 0.5), water1 = tone.dark ? mix(sc.bg, '#000000', 0.3) : mix(sc.bg, sc.accent, 0.3);
    ctx.drawImage(layer(env, `${key}|pond`, 0.5, (x, w, h) => {
      x.fillStyle = vertical(x, 0, h, [[0, water0], [1, water1]]); x.fillRect(0, 0, w, h);
      for (let i = 0; i < 22; i++) { const px = r(seed, i, 1) * w, py = r(seed, i, 2) * h, rr = U * (0.1 + 0.3 * r(seed, i, 3)), g = x.createRadialGradient(px, py, 0, px, py, rr); g.addColorStop(0, rgba(i % 2 ? tone.ink : '#FFFFFF', tone.dark ? 0.07 : 0.09)); g.addColorStop(1, rgba(tone.ink, 0)); x.fillStyle = g; x.fillRect(px - rr, py - rr, rr * 2, rr * 2); }
      x.lineCap = 'round'; for (let i = 0; i < 500; i++) { const px = r(seed, i, 11) * w, py = r(seed, i, 12) * h, a = r(seed, i, 13) * TAU, l = U * (0.008 + 0.03 * r(seed, i, 14)); x.strokeStyle = rgba(tone.fibre, 0.06 + 0.06 * r(seed, i, 15)); x.lineWidth = Math.max(0.5, U * 0.0007); x.beginPath(); x.moveTo(px, py); x.lineTo(px + Math.cos(a) * l, py + Math.sin(a) * l); x.stroke(); }
    }), 0, 0, W, H);
    /* caustics: light drifting over the floor */
    ctx.globalCompositeOperation = 'screen';
    for (let i = 0; i < 16; i++) { const px = wrap(r(seed, i, 51) * W + Math.sin(t * 0.3 + i) * U * 0.05, W), py = wrap(r(seed, i, 52) * H + t * U * 0.006 * (1 + r(seed, i, 53)), H); ctx.fillStyle = rgba('#FFFFFF', tone.dark ? 0.03 : 0.06); ctx.beginPath(); ctx.ellipse(px, py, U * (0.05 + 0.08 * r(seed, i, 54)), U * (0.02 + 0.03 * r(seed, i, 55)), r(seed, i, 56) * 3, 0, TAU); ctx.fill(); }
    ctx.globalCompositeOperation = 'source-over';
    const palettes = [
      { base: '#FFF6EA', patches: [[0.05, 0.4, tone.red, 0.95], [0.5, 0.85, '#F09A38', 0.85], [0.3, 0.5, '#1A1A1A', 0.4]] },
      { base: '#F08A34', patches: [[0.1, 0.55, '#FFF3E0', 0.9], [0.6, 0.9, tone.red, 0.85]] },
      { base: '#FFF6EA', patches: [[0.0, 0.3, '#1F1F1F', 0.95], [0.35, 0.7, tone.red, 0.95], [0.75, 0.95, '#1F1F1F', 0.7]] },
      { base: mix('#E8E2D0', sc.accent2, 0.2), patches: [[0.0, 0.5, '#F0B040', 0.9], [0.55, 0.95, '#FFF6EA', 0.8]] },
    ];
    for (let k = 0; k < 4; k++) drawKoi(ctx, t + k * 3, k, seed, W, H, U, tone, palettes[k]);
    /* lily pads, lotus flowers and ripples */
    ctx.drawImage(layer(env, `${key}|pads`, 0.5, (x, w, h) => {
      const green = tone.dark ? mix('#2E6A4E', sc.bg, 0.4) : mix('#5AA07A', sc.bg, 0.25), green2 = mix(green, '#000000', 0.2);
      for (let i = 0; i < 9; i++) {
        const px = w * (i < 4 ? 0.04 + 0.16 * r(seed, i, 61) : 0.62 + 0.36 * r(seed, i, 61)), py = h * (0.08 + 0.85 * r(seed, i, 62)), rr = U * (0.06 + 0.06 * r(seed, i, 63)), a = r(seed, i, 64) * TAU;
        x.shadowColor = 'rgba(0,0,0,0.25)'; x.shadowBlur = U * 0.012; x.shadowOffsetY = U * 0.008;
        x.fillStyle = i % 2 ? green : green2; x.beginPath(); x.moveTo(px, py); x.arc(px, py, rr, a + 0.25, a + TAU - 0.25); x.closePath(); x.fill(); x.shadowColor = 'transparent';
        x.strokeStyle = rgba('#FFFFFF', 0.25); x.lineWidth = Math.max(1, U * 0.0012); for (let v = 0; v < 6; v++) { x.beginPath(); x.moveTo(px, py); x.lineTo(px + Math.cos(a + 0.6 + v * 0.9) * rr * 0.9, py + Math.sin(a + 0.6 + v * 0.9) * rr * 0.9); x.stroke(); }
        if (i % 3 === 0) { for (let ring = 0; ring < 2; ring++) for (let pt = 0; pt < 9; pt++) { const pa = (pt / 9) * TAU + ring * 0.3; x.save(); x.translate(px + rr * 0.1, py); x.rotate(pa); x.fillStyle = ring ? '#FFFFFF' : mix('#FFB3C6', tone.red, 0.25); x.beginPath(); x.ellipse(rr * (0.28 - ring * 0.1), 0, rr * (0.26 - ring * 0.06), rr * 0.09, 0, 0, TAU); x.fill(); x.restore(); } x.fillStyle = '#F6D24A'; x.beginPath(); x.arc(px + rr * 0.1, py, rr * 0.08, 0, TAU); x.fill(); }
      }
    }), 0, 0, W, H);
    ctx.lineWidth = Math.max(1, U * 0.002);
    for (let i = 0; i < 4; i++) { const ph = wrap(t * 0.22 + i / 4, 1), px = W * (0.2 + 0.6 * r(seed, i, 71)), py = H * (0.25 + 0.5 * r(seed, i, 72)); ctx.strokeStyle = rgba('#FFFFFF', 0.4 * (1 - ph)); ctx.beginPath(); ctx.ellipse(px, py, U * (0.02 + 0.12 * ph), U * (0.02 + 0.12 * ph) * 0.8, 0, 0, TAU); ctx.stroke(); }
    for (let i = 0; i < 12; i++) { const life = wrap(t * (0.02 + 0.015 * r(seed, i, 81)) + r(seed, i, 82), 1), px = W * life * 1.15 - W * 0.07 + Math.sin(t * 0.6 + i) * U * 0.03, py = H * r(seed, i, 83) + Math.sin(t * 0.4 + i * 2) * U * 0.04; ctx.save(); ctx.translate(px, py); ctx.rotate(t * 0.3 + i); ctx.fillStyle = rgba(i % 2 ? '#E0502A' : '#F0A030', 0.85); ctx.beginPath(); ctx.moveTo(0, -U * 0.014); ctx.lineTo(U * 0.006, -U * 0.004); ctx.lineTo(U * 0.014, 0); ctx.lineTo(U * 0.006, U * 0.006); ctx.lineTo(0, U * 0.014); ctx.lineTo(-U * 0.006, U * 0.006); ctx.lineTo(-U * 0.014, 0); ctx.lineTo(-U * 0.006, -U * 0.004); ctx.closePath(); ctx.fill(); ctx.restore(); }
  });

  /* in front of the lyric: a few ink flecks drifting, a faint band of sumi at the top edge; light enough that text always reads */
  K.registerFront('echoInkDrift', '墨の流れ', (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, U = Umin(env), seed = P.seed || 1, e = fade(env, 1.2), tone = paperTone(sc);
    ctx.globalAlpha = e;
    for (let i = 0; i < 26; i++) { const px = W * r(seed, i, 1), py = H - wrap(r(seed, i, 2) * H + t * H * (0.006 + 0.012 * r(seed, i, 3)), H), s = U * (0.0012 + 0.003 * Math.pow(r(seed, i, 4), 2)); ctx.fillStyle = rgba(i % 9 === 0 ? tone.red : tone.ink, 0.22 + 0.25 * r(seed, i, 5)); ctx.beginPath(); ctx.ellipse(px + Math.sin(t * 0.3 + i) * U * 0.02, py, s * 1.4, s, r(seed, i, 6) * 3, 0, TAU); ctx.fill(); }
    const g = ctx.createLinearGradient(0, 0, 0, H * 0.16); g.addColorStop(0, rgba(tone.ink, tone.dark ? 0.16 : 0.1)); g.addColorStop(1, rgba(tone.ink, 0)); ctx.fillStyle = g; ctx.fillRect(0, 0, W, H * 0.16);
    if (K.luxe && tone.dark) K.luxe(ctx, env, { seed: (P && P.seed) || 1, tint: ['#FFFFFF', sc.accent, sc.accent2], strength: 0.35, bokeh: 2, glitter: 4 });
  });
})();
