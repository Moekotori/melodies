/* ECHO first-party backdrops, general "play" scenes: hi-fi gear with faces (ターンテーブル, カセット) and card-table scenes (トランプ卓, トランプ渦).
   Cute, flat, bold outlines. Every colour is taken from the current scheme, so any look can use them. */
(() => {
  'use strict';
  const K = J.echo, TAU = K.TAU, clamp = K.clamp, mix = K.mix, rgba = K.rgba, r = J.r;
  const { U: Umin, isDark, wrap, fade, vertical, glowDisc, layer } = K;
  const ck = sc => [sc.bg, sc.fg, sc.accent, sc.accent2, sc.ghostA, sc.ghostB].join('');
  const frac = v => v - Math.floor(v);
  const ease = v => { v = clamp(v); return v * v * (3 - 2 * v); };
  const dot = (ctx, x, y, rad) => { ctx.beginPath(); ctx.arc(x, y, rad, 0, TAU); ctx.fill(); };
  const FONT = '"Arial Rounded MT Bold","Hiragino Maru Gothic ProN","Trebuchet MS","Arial Black",sans-serif';
  /* the colours every scene draws from */
  const tones = sc => {
    const dark = isDark(sc.bg);
    const line = dark ? mix(sc.bg, '#000000', 0.82) : mix(sc.fg, '#000000', 0.25);
    return {
      dark, line,
      bg0: dark ? mix(sc.bg, '#000000', 0.5) : mix(sc.bg, '#FFFFFF', 0.55), bg1: dark ? mix(sc.bg, '#000000', 0.2) : sc.bg,
      c1: dark ? mix(sc.accent, '#FFFFFF', 0.1) : mix(sc.accent, '#000000', 0.04), c2: dark ? mix(sc.accent2, '#FFFFFF', 0.08) : mix(sc.accent2, '#000000', 0.03), c3: sc.ghostA || sc.accent,
      hi: mix(sc.accent, '#FFFFFF', dark ? 0.6 : 0.7), cream: mix('#FFF6E2', sc.accent2, 0.12), body: dark ? mix(sc.bg, sc.fg, 0.2) : mix(sc.bg, '#FFFFFF', 0.5), body2: dark ? mix(sc.bg, sc.fg, 0.1) : mix(sc.bg, sc.fg, 0.08), metal: dark ? mix(sc.fg, sc.bg, 0.55) : mix('#FFFFFF', sc.fg, 0.16),
      blush: '#FF8FAE',
    };
  };
  const glow = (ctx, T, x, y, rad, c, a) => { ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over'; glowDisc(ctx, x, y, rad, c, T.dark ? a : a * 0.7); ctx.restore(); };
  const rr = (ctx, x, y, w, h, rad) => { ctx.beginPath(); ctx.roundRect(x, y, w, h, rad); };
  const beat = (t, bpm) => { const p = frac(t * bpm / 60); return Math.pow(Math.max(0, 1 - p * 2.4), 2); };
  const face = (ctx, x, y, s, T, t, seed, mouth) => { /* two dot eyes with a glint, blush, and a small mouth */
    const blink = frac(t / 4.3 + seed * 0.37) > 0.955 ? 0.12 : 1;
    ctx.fillStyle = T.line; for (const sx of [-1, 1]) { ctx.save(); ctx.translate(x + sx * s * 0.62, y); ctx.scale(1, blink); dot(ctx, 0, 0, s * 0.2); ctx.fillStyle = '#FFFFFF'; if (blink > 0.5) { dot(ctx, -s * 0.06, -s * 0.07, s * 0.07); } ctx.restore(); ctx.fillStyle = T.line; }
    ctx.fillStyle = rgba(T.blush, 0.75); for (const sx of [-1, 1]) { ctx.beginPath(); ctx.ellipse(x + sx * s * 1.02, y + s * 0.34, s * 0.24, s * 0.14, 0, 0, TAU); ctx.fill(); }
    ctx.strokeStyle = T.line; ctx.lineWidth = Math.max(1.5, s * 0.09); ctx.lineCap = 'round'; ctx.beginPath();
    if (mouth) { ctx.fillStyle = T.line; ctx.ellipse(x, y + s * 0.42, s * 0.2, s * (0.1 + 0.2 * mouth), 0, 0, TAU); ctx.fill(); } else { ctx.arc(x, y + s * 0.2, s * 0.24, 0.15 * Math.PI, 0.85 * Math.PI); ctx.stroke(); }
  };
  const note = (ctx, x, y, s, col, rot) => { ctx.save(); ctx.translate(x, y); ctx.rotate(rot); ctx.fillStyle = col; ctx.strokeStyle = col; ctx.lineWidth = Math.max(1.5, s * 0.14); ctx.beginPath(); ctx.ellipse(0, 0, s * 0.5, s * 0.36, -0.4, 0, TAU); ctx.fill(); ctx.beginPath(); ctx.moveTo(s * 0.4, -s * 0.1); ctx.lineTo(s * 0.4, -s * 1.5); ctx.quadraticCurveTo(s * 0.4 + s * 0.2, -s * 1.1, s * 1.05, -s * 0.95); ctx.stroke(); ctx.restore(); };
  const heart = (ctx, x, y, z) => { ctx.beginPath(); ctx.moveTo(x, y + 0.85 * z); ctx.bezierCurveTo(x - 1.25 * z, y + 0.1 * z, x - 0.7 * z, y - 0.9 * z, x, y - 0.35 * z); ctx.bezierCurveTo(x + 0.7 * z, y - 0.9 * z, x + 1.25 * z, y + 0.1 * z, x, y + 0.85 * z); ctx.fill(); };
  /* suits: 0 spade, 1 heart, 2 club, 3 diamond */
  const suit = (ctx, k, x, y, z) => {
    ctx.beginPath();
    if (k === 1) { heart(ctx, x, y, z); return; }
    if (k === 3) { ctx.moveTo(x, y - z); ctx.lineTo(x + 0.72 * z, y); ctx.lineTo(x, y + z); ctx.lineTo(x - 0.72 * z, y); ctx.closePath(); ctx.fill(); return; }
    if (k === 0) { ctx.moveTo(x, y - 0.9 * z); ctx.bezierCurveTo(x - 1.3 * z, y - 0.05 * z, x - 0.75 * z, y + 0.85 * z, x, y + 0.3 * z); ctx.bezierCurveTo(x + 0.75 * z, y + 0.85 * z, x + 1.3 * z, y - 0.05 * z, x, y - 0.9 * z); ctx.moveTo(x, y + 0.15 * z); ctx.lineTo(x - 0.4 * z, y + 1.0 * z); ctx.lineTo(x + 0.4 * z, y + 1.0 * z); ctx.closePath(); ctx.fill(); return; }
    ctx.arc(x, y - 0.45 * z, 0.48 * z, 0, TAU); ctx.moveTo(x - 0.5 * z + 0.48 * z, y + 0.25 * z); ctx.arc(x - 0.5 * z, y + 0.25 * z, 0.48 * z, 0, TAU); ctx.moveTo(x + 0.5 * z + 0.48 * z, y + 0.25 * z); ctx.arc(x + 0.5 * z, y + 0.25 * z, 0.48 * z, 0, TAU); ctx.moveTo(x, y); ctx.lineTo(x - 0.35 * z, y + 1.0 * z); ctx.lineTo(x + 0.35 * z, y + 1.0 * z); ctx.closePath(); ctx.fill();
  };
  const RANKS = ['A', '2', '3', '4', '5', '6', '7', '8', '9', '10', 'J', 'Q', 'K'];
  /* a playing card centred on (x, y): face up, face down, or a cheerful joker */
  const cardRaw = (ctx, T, sc, x, y, w, h, rot, mode, rank, sk, t) => {
    ctx.save(); ctx.translate(x, y); ctx.rotate(rot);
    const lw = Math.max(1.5, w * 0.04), face0 = mix('#FFFFFF', sc.bg, T.dark ? 0.05 : 0.08);
    ctx.fillStyle = rgba('#000000', T.dark ? 0.45 : 0.2); rr(ctx, -w / 2 + w * 0.05, -h / 2 + w * 0.07, w, h, w * 0.1); ctx.fill();
    ctx.fillStyle = mode === 'back' ? mix(sc.accent, '#000000', 0.12) : face0; rr(ctx, -w / 2, -h / 2, w, h, w * 0.1); ctx.fill(); ctx.strokeStyle = T.line; ctx.lineWidth = lw; ctx.stroke();
    if (mode === 'back') {
      ctx.save(); rr(ctx, -w / 2 + w * 0.08, -h / 2 + w * 0.08, w - w * 0.16, h - w * 0.16, w * 0.06); ctx.clip(); ctx.fillStyle = mix(sc.accent, '#FFFFFF', 0.25); ctx.fillRect(-w, -h, w * 2, h * 2);
      ctx.strokeStyle = rgba('#FFFFFF', 0.5); ctx.lineWidth = Math.max(1, w * 0.03); ctx.beginPath(); const st = w * 0.2; for (let k = -8; k <= 8; k++) { ctx.moveTo(k * st - h, -h); ctx.lineTo(k * st + h, h); ctx.moveTo(k * st + h, -h); ctx.lineTo(k * st - h, h); } ctx.stroke(); ctx.restore();
      ctx.strokeStyle = rgba('#FFFFFF', 0.9); ctx.lineWidth = Math.max(1, w * 0.03); rr(ctx, -w / 2 + w * 0.08, -h / 2 + w * 0.08, w - w * 0.16, h - w * 0.16, w * 0.06); ctx.stroke();
      ctx.fillStyle = rgba('#FFFFFF', 0.95); heart(ctx, 0, 0, w * 0.2);
    } else if (mode === 'joker') {
      const col = rank ? T.c2 : T.c1; ctx.fillStyle = mix(col, '#FFFFFF', 0.55); rr(ctx, -w / 2 + w * 0.07, -h / 2 + w * 0.07, w - w * 0.14, h - w * 0.14, w * 0.07); ctx.fill();
      ctx.fillStyle = col; ctx.beginPath(); for (let i = 0; i < 16; i++) { const a = i / 16 * TAU + t * 0.3, rad2 = w * (i % 2 ? 0.26 : 0.4); ctx.lineTo(Math.cos(a) * rad2, -h * 0.08 + Math.sin(a) * rad2); } ctx.closePath(); ctx.fill(); ctx.strokeStyle = T.line; ctx.lineWidth = lw; ctx.stroke();
      ctx.fillStyle = '#FFFFFF'; dot(ctx, 0, -h * 0.08, w * 0.25); ctx.stroke(); face(ctx, 0, -h * 0.09, w * 0.16, T, t, 1, 0.5 + 0.5 * Math.sin(t * 6));
      ctx.fillStyle = col; ctx.font = `bold ${w * 0.15}px ${FONT}`; ctx.textAlign = 'center'; ctx.textBaseline = 'middle'; ctx.fillText('JOKER', 0, h * 0.36);
    } else {
      const red = sk === 1 || sk === 3, col = red ? mix('#E5233B', sc.accent, 0.25) : mix('#1A1A26', sc.bg, 0.1);
      ctx.fillStyle = col; ctx.font = `bold ${w * (rank === '10' ? 0.24 : 0.3)}px ${FONT}`; ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
      ctx.fillText(rank, -w * 0.31, -h * 0.38); suit(ctx, sk, -w * 0.31, -h * 0.24, w * 0.09);
      ctx.save(); ctx.rotate(Math.PI); ctx.fillText(rank, -w * 0.31, -h * 0.38); suit(ctx, sk, -w * 0.31, -h * 0.24, w * 0.09); ctx.restore();
      suit(ctx, sk, 0, h * 0.02, w * 0.3);
    }
    ctx.restore();
  };

  /* cards are drawn once into small sprites and then only rotated and blitted */
  const CARDS = new Map();
  const card = (ctx, T, sc, x, y, w, h, rot, mode, rank, sk, t) => {
    if (mode === 'joker') { cardRaw(ctx, T, sc, x, y, w, h, rot, mode, rank, sk, t); return; }
    const q = Math.max(8, Math.round(w / 6) * 6), hh = q * h / w, pad = Math.ceil(q * 0.14), key = `${ck(sc)}|${mode}|${mode === 'back' ? 0 : rank}|${mode === 'back' ? 0 : sk}|${q}`;
    let c = CARDS.get(key);
    if (!c) {
      c = document.createElement('canvas'); c.width = Math.ceil(q + pad * 2); c.height = Math.ceil(hh + pad * 2); const g = c.getContext('2d');
      cardRaw(g, T, sc, c.width / 2, c.height / 2, q, hh, 0, mode, rank, sk, 0); c.pad = pad; CARDS.set(key, c);
      if (CARDS.size > 160) CARDS.delete(CARDS.keys().next().value);
    }
    const k = w / q; ctx.save(); ctx.translate(x, y); ctx.rotate(rot); ctx.drawImage(c, -c.width * k / 2, -c.height * k / 2, c.width * k, c.height * k); ctx.restore();
  };

  /* ================= ターンテーブル ================= */
  K.registerBg('echoTurntable', 'ターンテーブル', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `tt|${seed}|${ck(sc)}`;
    const bw = Math.min(W * 0.94, H * (portrait ? 0.86 : 1.5)), bh = portrait ? bw * 1.08 : bw * 0.66, bx = W / 2 - bw / 2, by = H / 2 - bh / 2, u = bw / 100;
    const cx = bx + bw * (portrait ? 0.5 : 0.42), cy = by + bh * (portrait ? 0.46 : 0.5), R = Math.min(bw * (portrait ? 0.44 : 0.32), bh * 0.44), bt = beat(t, 96);
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.4, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [1, T.bg1]]); x.fillRect(0, 0, W, H);
      glowDisc(x, cx, cy, U * 0.9, T.c1, T.dark ? 0.2 : 0.3);
      x.strokeStyle = rgba(T.line, T.dark ? 0.25 : 0.14); x.lineWidth = Math.max(1.5, U * 0.004); for (let i = 1; i <= 4; i++) { x.beginPath(); x.arc(cx, cy, R * (1.2 + i * 0.28), 0, TAU); x.stroke(); }
    }), 0, 0, W, H);
    /* plinth */
    ctx.fillStyle = rgba('#000000', T.dark ? 0.4 : 0.15); rr(ctx, bx + u * 1.5, by + u * 2.2, bw, bh, u * 5); ctx.fill();
    const pg = ctx.createLinearGradient(bx, by, bx + bw, by + bh); pg.addColorStop(0, mix(T.body, '#FFFFFF', 0.12)); pg.addColorStop(1, T.body2); ctx.fillStyle = pg; rr(ctx, bx, by, bw, bh, u * 5); ctx.fill(); ctx.strokeStyle = T.line; ctx.lineWidth = Math.max(2.5, u * 0.7); ctx.stroke();
    ctx.fillStyle = rgba('#FFFFFF', T.dark ? 0.1 : 0.4); rr(ctx, bx + u * 2, by + u * 1.5, bw - u * 4, u * 1.6, u); ctx.fill();
    /* platter and record */
    ctx.fillStyle = T.metal; dot(ctx, cx, cy, R * 1.06); ctx.strokeStyle = T.line; ctx.lineWidth = Math.max(2.5, u * 0.7); ctx.stroke();
    const vy = mix(T.c1, T.c2, 0.5), spin = t * 0.9 + bt * 0.05;
    ctx.fillStyle = T.dark ? '#0B0B10' : '#20202A'; dot(ctx, cx, cy, R * 0.98);
    ctx.strokeStyle = rgba('#FFFFFF', 0.08); ctx.lineWidth = 1; for (let i = 0; i < 16; i++) { ctx.beginPath(); ctx.arc(cx, cy, R * (0.36 + i * 0.038), 0, TAU); ctx.stroke(); }
    ctx.save(); ctx.beginPath(); ctx.arc(cx, cy, R * 0.98, 0, TAU); ctx.clip();
    for (const a of [-0.9, 2.24]) { const g = ctx.createLinearGradient(cx, cy, cx + Math.cos(a) * R, cy + Math.sin(a) * R); g.addColorStop(0, rgba('#FFFFFF', 0)); g.addColorStop(0.5, rgba('#FFFFFF', 0.2)); g.addColorStop(1, rgba('#FFFFFF', 0)); ctx.fillStyle = g; ctx.beginPath(); ctx.moveTo(cx, cy); ctx.arc(cx, cy, R, a - 0.2, a + 0.2); ctx.fill(); }
    ctx.restore();
    ctx.save(); ctx.translate(cx, cy); ctx.rotate(spin);
    ctx.fillStyle = mix(vy, '#FFFFFF', 0.15); dot(ctx, 0, 0, R * 0.34); ctx.strokeStyle = T.line; ctx.lineWidth = Math.max(2, u * 0.5); ctx.stroke();
    ctx.fillStyle = mix(T.c2, '#FFFFFF', 0.55); for (let i = 0; i < 6; i++) { const a = i / 6 * TAU; ctx.beginPath(); ctx.moveTo(0, 0); ctx.arc(0, 0, R * 0.33, a, a + TAU / 12); ctx.closePath(); ctx.fill(); }
    ctx.fillStyle = T.cream; dot(ctx, 0, 0, R * 0.19); ctx.stroke(); ctx.rotate(-spin); face(ctx, 0, -R * 0.015, R * 0.09, T, t, seed, 0);
    ctx.restore(); ctx.fillStyle = T.metal; dot(ctx, cx, cy, R * 0.028);
    /* tonearm sweeping in as the record plays */
    const pvx = bx + bw * (portrait ? 0.86 : 0.86), pvy = by + bh * (portrait ? 0.14 : 0.2), prog = 0.5 + 0.5 * Math.sin(t * 0.08), tipx = cx + Math.cos(-0.5 - prog * 0.3) * R * (0.62 + 0.3 * (1 - prog)) , tipy = cy + Math.sin(-0.5 - prog * 0.3) * R * (0.62 + 0.3 * (1 - prog)) + R * 0.1;
    ctx.lineCap = 'round'; ctx.strokeStyle = rgba('#000000', 0.25); ctx.lineWidth = Math.max(4, u * 1.5); ctx.beginPath(); ctx.moveTo(pvx + u, pvy + u); ctx.lineTo(tipx + u, tipy + u); ctx.stroke();
    ctx.strokeStyle = T.line; ctx.lineWidth = Math.max(5, u * 1.9); ctx.beginPath(); ctx.moveTo(pvx, pvy); ctx.lineTo(tipx, tipy); ctx.stroke(); ctx.strokeStyle = mix(T.metal, '#FFFFFF', 0.4); ctx.lineWidth = Math.max(3, u * 1.1); ctx.stroke();
    ctx.fillStyle = T.metal; dot(ctx, pvx, pvy, u * 3.6); ctx.strokeStyle = T.line; ctx.lineWidth = Math.max(2, u * 0.6); ctx.stroke(); ctx.fillStyle = T.c1; dot(ctx, pvx, pvy, u * 1.2);
    ctx.save(); ctx.translate(tipx, tipy); ctx.rotate(Math.atan2(tipy - pvy, tipx - pvx)); ctx.fillStyle = T.c2; rr(ctx, -u * 0.5, -u * 1.8, u * 4.6, u * 3.6, u); ctx.fill(); ctx.strokeStyle = T.line; ctx.lineWidth = Math.max(2, u * 0.5); ctx.stroke(); ctx.restore();
    /* knobs and a little lamp on the plinth */
    for (let i = 0; i < 3; i++) { const kx = bx + bw * (portrait ? 0.15 + i * 0.13 : 0.78 + i * 0.07), ky = by + bh * (portrait ? 0.9 : 0.82); ctx.fillStyle = mix(T.metal, '#FFFFFF', 0.3); dot(ctx, kx, ky, u * 2.6); ctx.strokeStyle = T.line; ctx.lineWidth = Math.max(2, u * 0.5); ctx.stroke(); ctx.strokeStyle = T.c1; ctx.lineWidth = Math.max(2, u * 0.7); ctx.beginPath(); ctx.moveTo(kx, ky); ctx.lineTo(kx + Math.cos(-1.2 + i * 0.7) * u * 2, ky + Math.sin(-1.2 + i * 0.7) * u * 2); ctx.stroke(); }
    glow(ctx, T, bx + bw * 0.07, by + bh * 0.9, u * 6, T.c2, 0.7 * (0.6 + 0.4 * bt)); ctx.fillStyle = T.c2; dot(ctx, bx + bw * 0.07, by + bh * 0.9, u * 1.4);
    /* notes floating from the stylus */
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    for (let i = 0; i < 6; i++) { const age = frac(t * 0.14 + i / 6); ctx.globalAlpha = e * Math.sin(age * Math.PI); note(ctx, tipx + Math.sin(t * 1.3 + i * 2.1) * u * 5 + age * (i % 2 ? 1 : -1) * u * 14, tipy - age * bh * 0.5, u * (2.6 + r(seed, i, 2)), i % 2 ? T.c1 : T.c2, Math.sin(t + i) * 0.3); }
    ctx.restore();
  });

  /* ================= カセット ================= */
  K.registerBg('echoCassette', 'カセット', ['pop', 'calm', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `ca|${seed}|${ck(sc)}`;
    const cw = Math.min(W * 0.84, H * (portrait ? 0.9 : 1.32)), ch = cw * 0.63, cx = W / 2, cy = H * (portrait ? 0.42 : 0.5), u = cw / 100, tilt = -0.07 + Math.sin(t * 0.35) * 0.015;
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.4, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [1, T.bg1]]); x.fillRect(0, 0, W, H);
      x.strokeStyle = rgba(T.c1, T.dark ? 0.12 : 0.16); x.lineWidth = U * 0.03; const st = U * 0.14; x.beginPath(); for (let k = -20; k < 40; k++) { x.moveTo(k * st, H); x.lineTo(k * st + H * 0.6, 0); } x.stroke();
      glowDisc(x, cx, cy, U * 0.9, T.c2, T.dark ? 0.2 : 0.3);
    }), 0, 0, W, H);
    /* a ribbon of tape streaming across the screen, behind and in front of the case */
    const ribbon = (yBase, amp, sp, col, a, thick, ph) => { ctx.fillStyle = rgba(col, a); ctx.beginPath(); const n = 50; for (let i = 0; i <= n; i++) { const x = W * i / n, y = yBase + Math.sin(x / U * 3.2 + t * sp + ph) * amp + Math.sin(x / U * 7 - t * sp * 0.7) * amp * 0.25; i ? ctx.lineTo(x, y - thick / 2) : ctx.moveTo(x, y - thick / 2); } for (let i = n; i >= 0; i--) { const x = W * i / n, y = yBase + Math.sin(x / U * 3.2 + t * sp + ph) * amp + Math.sin(x / U * 7 - t * sp * 0.7) * amp * 0.25; ctx.lineTo(x, y + thick / 2); } ctx.fill(); };
    ribbon(H * 0.1, U * 0.05, 1.1, mix(sc.fg, sc.bg, 0.7), T.dark ? 0.45 : 0.4, U * 0.08, 0); ribbon(H * 0.17, U * 0.04, 0.9, T.c1, T.dark ? 0.55 : 0.5, U * 0.05, 2);
    ctx.save(); ctx.translate(cx, cy); ctx.rotate(tilt);
    ctx.fillStyle = rgba('#000000', T.dark ? 0.4 : 0.17); rr(ctx, -cw / 2 + u * 2, -ch / 2 + u * 3, cw, ch, u * 4); ctx.fill();
    const bg = ctx.createLinearGradient(0, -ch / 2, 0, ch / 2); bg.addColorStop(0, mix(T.c1, '#FFFFFF', T.dark ? 0.1 : 0.3)); bg.addColorStop(1, mix(T.c1, T.dark ? '#000000' : sc.fg, 0.28)); ctx.fillStyle = bg; rr(ctx, -cw / 2, -ch / 2, cw, ch, u * 4); ctx.fill(); ctx.strokeStyle = T.line; ctx.lineWidth = Math.max(3, u * 1); ctx.stroke();
    /* label */
    ctx.fillStyle = T.cream; rr(ctx, -cw * 0.42, -ch * 0.44, cw * 0.84, ch * 0.42, u * 2); ctx.fill(); ctx.stroke();
    ctx.fillStyle = T.c2; ctx.fillRect(-cw * 0.42 + u * 0.5, -ch * 0.44 + ch * 0.28, cw * 0.84 - u, ch * 0.05); ctx.fillStyle = T.c1; ctx.fillRect(-cw * 0.42 + u * 0.5, -ch * 0.44 + ch * 0.33, cw * 0.84 - u, ch * 0.03);
    ctx.strokeStyle = T.line; ctx.lineWidth = Math.max(2, u * 0.7); ctx.lineCap = 'round'; ctx.beginPath(); ctx.moveTo(-cw * 0.36, -ch * 0.34); for (let i = 0; i <= 20; i++) ctx.lineTo(-cw * 0.36 + cw * 0.5 * i / 20, -ch * 0.34 + Math.sin(i * 1.3) * ch * 0.02 * (i % 3 ? 1 : -1)); ctx.stroke(); ctx.beginPath(); ctx.moveTo(-cw * 0.36, -ch * 0.25); ctx.lineTo(-cw * 0.06, -ch * 0.25); ctx.stroke();
    ctx.fillStyle = mix('#FF6FA3', sc.accent, 0.2); heart(ctx, cw * 0.32, -ch * 0.3, u * 3.4);
    /* the tape window with two turning reels */
    ctx.fillStyle = T.dark ? '#0B0B10' : '#2A2A34'; rr(ctx, -cw * 0.3, ch * 0.0, cw * 0.6, ch * 0.32, ch * 0.16); ctx.fill(); ctx.stroke();
    const fill = 0.5 + 0.5 * Math.sin(t * 0.15);
    for (const [sx, rad] of [[-1, 0.09 + 0.05 * fill], [1, 0.09 + 0.05 * (1 - fill)]]) {
      const rx = sx * cw * 0.17, ry = ch * 0.16; ctx.fillStyle = mix(sc.fg, sc.bg, 0.8); dot(ctx, rx, ry, cw * rad); ctx.strokeStyle = T.line; ctx.lineWidth = Math.max(1.5, u * 0.4); ctx.stroke();
      ctx.save(); ctx.translate(rx, ry); ctx.rotate(t * 1.6 * -sx); ctx.fillStyle = '#FFFFFF'; dot(ctx, 0, 0, cw * 0.062); ctx.stroke(); ctx.fillStyle = T.line; for (let i = 0; i < 6; i++) { ctx.rotate(TAU / 6); ctx.fillRect(cw * 0.024, -cw * 0.008, cw * 0.032, cw * 0.016); } ctx.restore();
    }
    /* bottom trapezoid and a smiling mouth */
    ctx.fillStyle = mix(T.c1, '#000000', 0.25); ctx.beginPath(); ctx.moveTo(-cw * 0.32, ch * 0.5); ctx.lineTo(-cw * 0.25, ch * 0.36); ctx.lineTo(cw * 0.25, ch * 0.36); ctx.lineTo(cw * 0.32, ch * 0.5); ctx.closePath(); ctx.fill(); ctx.strokeStyle = T.line; ctx.lineWidth = Math.max(2, u * 0.7); ctx.stroke();
    ctx.fillStyle = T.line; for (const sx of [-1, 1]) dot(ctx, sx * cw * 0.14, ch * 0.44, u * 0.9); face(ctx, 0, ch * 0.41, cw * 0.035, T, t, seed, 0);
    for (const [sx, sy] of [[-0.45, -0.45], [0.45, -0.45], [-0.45, 0.45], [0.45, 0.45]]) { ctx.fillStyle = mix(T.metal, '#FFFFFF', 0.3); dot(ctx, cw * sx * 1.0, ch * sy * 1.0, u * 1.3); ctx.stroke(); }
    ctx.restore();
    ribbon(H * (portrait ? 0.86 : 0.93), U * 0.035, 1.3, T.c2, T.dark ? 0.7 : 0.6, U * 0.05, 4);
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    for (let i = 0; i < 12; i++) { const age = frac(t * 0.1 + i / 12); ctx.globalAlpha = e * Math.sin(age * Math.PI); const a = r(seed, i, 1) * TAU, d = cw * (0.55 + 0.4 * age); if (i % 3 === 0) note(ctx, cx + Math.cos(a) * d, cy + Math.sin(a) * d * 0.7, u * 3, i % 2 ? T.c1 : T.c2, Math.sin(t + i) * 0.3); else { ctx.fillStyle = T.hi; dot(ctx, cx + Math.cos(a) * d, cy + Math.sin(a) * d * 0.7, u * (0.5 + r(seed, i, 2))); } }
    ctx.restore();
  });

  /* ================= トランプ卓 ================= */
  const fan = (ctx, T, sc, px, py, base, n, span, R, cw, ch, faceUp, t, seed, hover) => {
    for (let i = 0; i < n; i++) {
      const q = n > 1 ? i / (n - 1) - 0.5 : 0, a = base + q * span, lift = hover === i ? ch * 0.16 * (0.5 + 0.5 * Math.sin(t * 2)) : 0, rr2 = R + lift;
      const x = px + Math.cos(a) * rr2, y = py + Math.sin(a) * rr2, k = Math.floor(r(seed, i, 7) * 13), s = Math.floor(r(seed, i, 8) * 4);
      card(ctx, T, sc, x, y, cw, ch, a + Math.PI / 2, faceUp ? 'face' : 'back', RANKS[k], s, t);
    }
  };
  K.registerBg('echoCardTable', 'トランプ卓', ['pop', 'glitch', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `ct|${seed}|${ck(sc)}`;
    const felt = T.dark ? mix(sc.bg, sc.accent2, 0.38) : mix(sc.bg, sc.accent2, 0.42), cwid = U * (portrait ? 0.2 : 0.14), chei = cwid * 1.4;
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.5, (x) => {
      const g = x.createRadialGradient(W / 2, H * 0.5, 0, W / 2, H * 0.5, Math.hypot(W, H) * 0.6); g.addColorStop(0, mix(felt, '#FFFFFF', T.dark ? 0.22 : 0.2)); g.addColorStop(0.7, felt); g.addColorStop(1, mix(felt, '#000000', T.dark ? 0.6 : 0.3)); x.fillStyle = g; x.fillRect(0, 0, W, H);
      x.strokeStyle = rgba('#FFFFFF', 0.22); x.lineWidth = Math.max(2, U * 0.005); x.beginPath(); x.ellipse(W / 2, H * 0.5, W * 0.4, H * 0.36, 0, 0, TAU); x.stroke(); x.setLineDash([U * 0.02, U * 0.02]); x.beginPath(); x.ellipse(W / 2, H * 0.5, W * 0.43, H * 0.4, 0, 0, TAU); x.stroke(); x.setLineDash([]);
      x.fillStyle = rgba('#FFFFFF', 0.06); for (let i = 0; i < 4; i++) { x.font = `bold ${U * 0.12}px ${FONT}`; x.textAlign = 'center'; x.textBaseline = 'middle'; x.fillText('♠♥♣♦'[i], W * (0.2 + i * 0.2), H * (i % 2 ? 0.7 : 0.3)); }
    }), 0, 0, W, H);
    /* three cards for the landlord, turning over in turn */
    for (let i = 0; i < 3; i++) { const fl = Math.cos(clamp(frac(t / 9) * 6 - i * 0.8, 0, Math.PI) ), up = fl < 0; ctx.save(); const cx0 = W / 2 + (i - 1) * cwid * 1.15, cy0 = H * (portrait ? 0.16 : 0.17); ctx.translate(cx0, cy0); ctx.scale(Math.max(0.05, Math.abs(fl)), 1); ctx.translate(-cx0, -cy0); card(ctx, T, sc, cx0, cy0, cwid * 0.85, chei * 0.85, 0, up ? 'face' : 'back', RANKS[[11, 2, 0][i]], [1, 0, 3][i], t); ctx.restore(); }
    fan(ctx, T, sc, -U * (portrait ? 0.55 : 0.3), H * 0.5, 0, 9, portrait ? 0.7 : 0.9, U * (portrait ? 0.75 : 0.5), cwid * 0.85, chei * 0.85, false, t, seed, -1);
    fan(ctx, T, sc, W + U * (portrait ? 0.55 : 0.3), H * 0.5, Math.PI, 9, portrait ? 0.7 : 0.9, U * (portrait ? 0.75 : 0.5), cwid * 0.85, chei * 0.85, false, t, seed + 3, -1);
    /* the rocket: two jokers charge, then blast off */
    const cyc = 7.5, q = frac(t / cyc), launch = ease((q - 0.2) / 0.32), gone = q > 0.52;
    if (!gone) {
      const y0 = H * (portrait ? 0.5 : 0.52), y = y0 - launch * H * 0.75 - (q < 0.2 ? Math.abs(Math.sin(t * 30)) * U * 0.004 * (q / 0.2) : 0), wob = q < 0.2 ? Math.sin(t * 40) * U * 0.004 * (q / 0.2) : 0;
      if (launch > 0) { ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over'; for (let k = 0; k < 9; k++) { const s = frac(t * 2.2 + k / 9), fy = y + chei * 0.55 + s * chei * (1.3 + launch), fw = cwid * (0.9 - 0.7 * s); ctx.fillStyle = rgba(k % 2 ? T.c1 : T.c2, 0.75 * (1 - s)); ctx.beginPath(); ctx.ellipse(W / 2 + Math.sin(t * 9 + k) * U * 0.012, fy, fw * 0.5, fw * 0.75, 0, 0, TAU); ctx.fill(); } ctx.restore(); ctx.fillStyle = rgba('#FFFFFF', 0.6 * (1 - launch)); for (let k = 0; k < 6; k++) dot(ctx, W / 2 + Math.sin(k * 2.3 + t) * cwid * (0.5 + launch * 2), y + chei * (0.8 + k * 0.25 + launch * 2), cwid * 0.18 * (1 + k * 0.2)); }
      card(ctx, T, sc, W / 2 - cwid * 0.5 + wob, y, cwid, chei, -0.1 - launch * 0.03, 'joker', 0, 0, t); card(ctx, T, sc, W / 2 + cwid * 0.5 + wob, y, cwid, chei, 0.1 + launch * 0.03, 'joker', 1, 0, t);
    } else { const b = clamp((q - 0.52) / 0.12); ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over'; ctx.fillStyle = rgba('#FFFFFF', 0.5 * (1 - b)); ctx.fillRect(0, 0, W, H); for (let i = 0; i < 22; i++) { const a = i / 22 * TAU; ctx.strokeStyle = rgba(i % 2 ? T.c1 : T.c2, 0.9 * (1 - b)); ctx.lineWidth = Math.max(2, U * 0.008); ctx.beginPath(); ctx.moveTo(W / 2 + Math.cos(a) * U * (0.1 + b * 0.2), H * 0.13 + Math.sin(a) * U * (0.1 + b * 0.2)); ctx.lineTo(W / 2 + Math.cos(a) * U * (0.2 + b * 0.7), H * 0.13 + Math.sin(a) * U * (0.2 + b * 0.7)); ctx.stroke(); } ctx.restore(); }
    /* the player's own hand at the bottom, one card lifting in turn */
    fan(ctx, T, sc, W / 2, H + U * (portrait ? 0.7 : 0.6), -Math.PI / 2, 12, 0.8, U * (portrait ? 0.95 : 0.85), cwid * 1.3, chei * 1.3, true, t, seed + 9, Math.floor(t / 1.6) % 12);
    for (let i = 0; i < 3; i++) { const cx0 = W * (i === 1 ? 0.86 : 0.14 + i * 0.05), cy0 = H * (portrait ? 0.3 : 0.78); void cx0; void cy0; }
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over'; for (let i = 0; i < 24; i++) { const age = frac(t * 0.08 + r(seed, i, 1)); ctx.globalAlpha = e * Math.sin(age * Math.PI) * 0.8; ctx.fillStyle = i % 2 ? T.c1 : T.c2; suit(ctx, i % 4, W * r(seed, i, 2), H * (1.05 - age * 1.1), U * 0.012); } ctx.restore();
  });

  /* ================= トランプ渦 ================= */
  K.registerBg('echoCardVortex', 'トランプの渦', ['pop', 'glitch', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `cv|${seed}|${ck(sc)}`;
    const cx = W / 2, cy = H * 0.5, Rm = Math.hypot(W, H) * 0.56, bt = beat(t, 100);
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.4, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [1, T.bg1]]); x.fillRect(0, 0, W, H);
      const n = 24; for (let i = 0; i < n; i++) { x.fillStyle = rgba(i % 2 ? T.c1 : T.c2, T.dark ? 0.1 : 0.12); x.beginPath(); x.moveTo(cx, cy); x.arc(cx, cy, Rm * 1.6, i / n * TAU, (i + 0.5) / n * TAU); x.fill(); }
      glowDisc(x, cx, cy, U * 0.7, T.hi, T.dark ? 0.5 : 0.5);
    }), 0, 0, W, H);
    const N = portrait ? 46 : 60, arms = 3;
    for (let i = 0; i < N; i++) {
      const arm = i % arms, s = (Math.floor(i / arms) + 0.4 + r(seed, i, 1) * 0.5) / (N / arms), sp = Math.pow(s, 1.25), ang = arm * TAU / arms + sp * 5.4 + t * 0.14, d = sp * Rm, x = cx + Math.cos(ang) * d, y = cy + Math.sin(ang) * d * 0.9;
      const w = U * (0.05 + 0.15 * s), k = Math.floor(r(seed, i, 2) * 13), su = Math.floor(r(seed, i, 3) * 4), mode = r(seed, i, 4) < 0.28 ? 'back' : (r(seed, i, 4) > 0.94 ? 'joker' : 'face');
      ctx.globalAlpha = e * clamp(0.25 + s * 1.6); card(ctx, T, sc, x, y, w, w * 1.4, ang + Math.PI / 2 + Math.sin(t * 0.7 + i) * 0.25, mode, mode === 'joker' ? i % 2 : RANKS[k], su, t);
    }
    ctx.globalAlpha = e;
    /* a crown in the eye of it, and stars thrown out */
    const cs = U * (0.11 + 0.012 * bt), gold = mix('#FFD24A', sc.accent, 0.2);
    glow(ctx, T, cx, cy, cs * 3, gold, 0.9);
    ctx.save(); ctx.translate(cx, cy); ctx.scale(1 + 0.05 * bt, 1 + 0.05 * bt); ctx.rotate(Math.sin(t * 0.8) * 0.06);
    ctx.fillStyle = gold; ctx.strokeStyle = T.line; ctx.lineWidth = Math.max(3, cs * 0.06); ctx.lineJoin = 'round'; ctx.beginPath(); ctx.moveTo(-cs, cs * 0.55); ctx.lineTo(-cs * 1.05, -cs * 0.6); ctx.lineTo(-cs * 0.5, -cs * 0.05); ctx.lineTo(0, -cs * 0.85); ctx.lineTo(cs * 0.5, -cs * 0.05); ctx.lineTo(cs * 1.05, -cs * 0.6); ctx.lineTo(cs, cs * 0.55); ctx.closePath(); ctx.fill(); ctx.stroke();
    ctx.fillStyle = mix(gold, '#FFFFFF', 0.45); ctx.fillRect(-cs * 0.96, cs * 0.55, cs * 1.92, cs * 0.25); ctx.strokeRect(-cs * 0.96, cs * 0.55, cs * 1.92, cs * 0.25);
    for (const [gx, gy, c] of [[-1.05, -0.6, T.c1], [0, -0.85, T.c2], [1.05, -0.6, T.c1]]) { ctx.fillStyle = c; dot(ctx, gx * cs, gy * cs, cs * 0.14); ctx.stroke(); }
    ctx.fillStyle = mix('#FF6FA3', sc.accent, 0.2); heart(ctx, 0, cs * 0.1, cs * 0.22); face(ctx, 0, cs * 0.34, cs * 0.16, T, t, seed, 0);
    ctx.restore();
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over'; for (let i = 0; i < 22; i++) { const age = frac(t * 0.1 + i / 22), a = r(seed, i, 5) * TAU + t * 0.1, d = cs * 1.2 + age * Rm * 0.9; ctx.globalAlpha = e * Math.sin(age * Math.PI); ctx.fillStyle = i % 2 ? T.c1 : T.c2; suit(ctx, i % 4, cx + Math.cos(a) * d, cy + Math.sin(a) * d * 0.9, U * (0.012 + 0.012 * age)); } ctx.restore();
  });
})();
