/* ECHO first-party backdrops, more scenes for the night looks: 雨夜ネオン (ネオン横丁, 自販機と雨), 夏祭り (屋台の並び, 灯籠流し) and 傾奇 (能舞台, 金雲と鶴).
   Static art goes into cached layers; glows, rain, steam, lanterns, cranes and people are redrawn every frame. Each has a dark and a bright reading. */
(() => {
  'use strict';
  const K = J.echo, E = K.E, TAU = K.TAU, clamp = K.clamp, mix = K.mix, rgba = K.rgba;
  const { U: Umin, isDark, wrap, fade, vertical, glowDisc, star, layer, shapes } = K;
  const { roundRect, cat, sparkle } = shapes;
  const colourKey = sc => [sc.bg, sc.fg, sc.accent, sc.accent2, sc.ghostA, sc.ghostB].join('');
  const r = J.r;
  const deep = (colour, amount) => mix(mix(colour, '#0B0716', 0.55), '#000000', amount);
  const FONT = '"Yu Gothic","Noto Sans JP","Hiragino Sans",sans-serif';
  const stackText = (x, str, cx, top, size) => { x.textAlign = 'center'; x.textBaseline = 'middle'; x.font = `bold ${size}px ${FONT}`; [...str].forEach((ch, i) => x.fillText(ch, cx, top + size * (0.5 + i * 1.05))); };

  /* ================= ネオン横丁 ================= */
  K.registerBg('echoNeonAlley', 'ネオン横丁', ['glitch', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), dark = isDark(sc.bg), portrait = H > W;
    const pal = dark ? ['#FF3D8B', '#35E4FF', '#FFB13B', '#B26BFF'] : ['#35E4FF', '#FFF3A8', '#FFFFFF', '#FFB13B'];
    const key = `alley|${seed}|${colourKey(sc)}`, y0 = H * 0.74, base = dark ? deep(sc.bg, 0.1) : mix(sc.bg, '#4A1030', 0.45), wall = dark ? mix(base, '#000000', 0.25) : mix(sc.bg, '#3A0A26', 0.55);
    const sideW = W * (portrait ? 0.26 : 0.3), words = ['ラーメン', '居酒屋', '焼鳥', 'カラオケ', '純喫茶', 'BAR', '中華', 'ゲーム', 'スナック', '酒', '夜', '寿司'];
    const signs = [];
    for (let side = 0; side < 2; side++) for (let i = 0; i < (portrait ? 5 : 6); i++) {
      const vert = r(seed, side, i, 1) > 0.4, word = words[Math.floor(r(seed, side, i, 2) * words.length)], len = vert ? Math.min(word.length, 4) : word.length, size = U * (vert ? 0.05 : 0.05 + 0.02 * r(seed, side, i, 3));
      signs.push({ side, vert, word: vert ? word.slice(0, 4) : word, size, col: pal[Math.floor(r(seed, side, i, 4) * pal.length)], x: side ? W - sideW * (0.15 + 0.7 * r(seed, side, i, 5)) : sideW * (0.15 + 0.7 * r(seed, side, i, 5)), y: H * (0.06 + 0.5 * (i / 6) + 0.05 * r(seed, side, i, 6)), w: vert ? size * 1.3 : size * (len + 0.6), h: vert ? size * (len * 1.05 + 0.3) : size * 1.4, dead: r(seed, side, i, 7) > 0.85, ph: r(seed, side, i, 8) * 6 });
    }
    ctx.globalAlpha = e;
    const scene = layer(env, `${key}|scene`, 0.6, (x, w, h) => {
      x.fillStyle = vertical(x, 0, y0, [[0, dark ? deep(sc.bg, 0.3) : mix(sc.bg, '#7A1A50', 0.4)], [1, dark ? mix(sc.bg, '#3A1A5A', 0.45) : mix(sc.bg, '#FFFFFF', 0.2)]]); x.fillRect(0, 0, w, y0 + 2);
      for (let i = 0; i < 20; i++) { const bx = sideW * 0.9 + (w - sideW * 1.8) * r(seed, i, 11), bw = U * (0.06 + 0.06 * r(seed, i, 12)), bh = H * (0.14 + 0.28 * r(seed, i, 13)); x.fillStyle = dark ? mix(base, '#3A2A6A', 0.25) : mix(sc.bg, '#6A1A48', 0.4); x.fillRect(bx, y0 - bh, bw, bh); for (let k = 0; k < 6; k++) if (r(seed, i, k, 14) > 0.5) { x.fillStyle = rgba(pal[(i + k) % pal.length], 0.65); x.fillRect(bx + bw * 0.2 + (k % 2) * bw * 0.4, y0 - bh + U * 0.02 + Math.floor(k / 2) * U * 0.03, bw * 0.2, U * 0.014); } }
      for (let side = 0; side < 2; side++) {                                                   /* the two facades, windows and rails */
        const x0 = side ? w - sideW : 0; x.fillStyle = wall; x.beginPath(); if (side) { x.moveTo(w, 0); x.lineTo(w - sideW, H * 0.08); x.lineTo(w - sideW * 0.8, y0); x.lineTo(w, h); } else { x.moveTo(0, 0); x.lineTo(sideW, H * 0.08); x.lineTo(sideW * 0.8, y0); x.lineTo(0, h); } x.closePath(); x.fill();
        x.strokeStyle = rgba('#FFFFFF', dark ? 0.06 : 0.14); x.lineWidth = Math.max(1, U * 0.002); for (let i = 1; i < 9; i++) { const yy = H * (0.08 + i * 0.1); x.beginPath(); x.moveTo(side ? w : 0, yy * 0.9); x.lineTo(side ? w - sideW * 0.95 : sideW * 0.95, yy); x.stroke(); }
        for (let i = 0; i < 12; i++) { if (r(seed, side, i, 21) < 0.4) continue; const wx = side ? w - sideW * (0.15 + 0.6 * r(seed, side, i, 22)) : sideW * (0.05 + 0.6 * r(seed, side, i, 22)), wy = H * (0.1 + 0.62 * r(seed, side, i, 23)); x.fillStyle = rgba(r(seed, side, i, 24) > 0.5 ? '#FFD98A' : pal[i % pal.length], 0.7); x.fillRect(wx, wy, U * 0.022, U * 0.03); }
      }
      x.fillStyle = vertical(x, y0, h, [[0, dark ? mix(base, '#2A1A4A', 0.35) : mix(sc.bg, '#7A1A50', 0.35)], [1, dark ? mix(base, '#000000', 0.45) : mix(sc.bg, '#3A0A26', 0.5)]]); x.fillRect(0, y0, w, h - y0);
      glowDisc(x, w * 0.5, y0 - H * 0.08, U * 0.8, pal[0], dark ? 0.32 : 0.55);
      x.fillStyle = rgba('#FFFFFF', dark ? 0.05 : 0.12); for (let i = 1; i < 7; i++) x.fillRect(0, y0 + (h - y0) * (i / 7) ** 1.4, w, Math.max(1, U * 0.0014));
      for (const s of signs) {                                                                  /* the signs themselves and their wet reflections */
        x.fillStyle = rgba('#0A0814', 0.7); x.fillRect(s.x - s.w / 2, s.y, s.w, s.h); x.strokeStyle = s.col; x.lineWidth = Math.max(1.5, U * 0.004); x.strokeRect(s.x - s.w / 2 + U * 0.004, s.y + U * 0.004, s.w - U * 0.008, s.h - U * 0.008);
        x.fillStyle = mix(s.col, '#FFFFFF', 0.35); if (s.vert) stackText(x, s.word, s.x, s.y + s.size * 0.15, s.size * 0.82); else { x.textAlign = 'center'; x.textBaseline = 'middle'; x.font = `bold ${s.size}px ${FONT}`; x.fillText(s.word, s.x, s.y + s.h / 2); }
        const g = x.createLinearGradient(0, y0, 0, h); g.addColorStop(0, rgba(s.col, 0.3)); g.addColorStop(1, rgba(s.col, 0)); x.fillStyle = g; x.fillRect(s.x - s.w * 0.4, y0, s.w * 0.8, (h - y0) * 0.9);
      }
    });
    ctx.drawImage(scene, 0, 0, W, H);
    /* paper lanterns strung across the alley: they sway, and pool their colour on the wet street */
    {
      const strings = portrait ? [[0.2, 0.06, 6]] : [[0.17, 0.07, 9], [0.33, 0.05, 7]], xa = sideW * 1.0, xb = W - sideW * 1.0;
      strings.forEach(([yk, sagk, n], si) => {
        const at = (sv) => [xa + (xb - xa) * sv, H * yk + H * sagk * 4 * sv * (1 - sv) + Math.sin(t * 0.9 + si * 2) * U * 0.004];
        ctx.strokeStyle = rgba('#000000', dark ? 0.7 : 0.5); ctx.lineWidth = Math.max(1, U * 0.002); ctx.beginPath(); for (let k = 0; k <= 24; k++) { const [px, py] = at(k / 24); k ? ctx.lineTo(px, py) : ctx.moveTo(px, py); } ctx.stroke();
        for (let i = 0; i < n; i++) {
          const sv = (i + 0.5) / n, [px, py0] = at(sv), sw = Math.sin(t * 1.3 + i * 1.9 + si) * 0.08, ls = U * (portrait ? 0.03 : 0.036) * (si ? 0.8 : 1), lc = i % 3 === 1 ? '#FFB13B' : '#FF4A4A', py = py0 + ls * 1.1;
          ctx.save(); ctx.translate(px, py0); ctx.rotate(sw); ctx.translate(0, ls * 1.1);
          const g = ctx.createRadialGradient(-ls * 0.25, -ls * 0.3, ls * 0.1, 0, 0, ls * 1.1); g.addColorStop(0, mix(lc, '#FFFFFF', 0.55)); g.addColorStop(0.5, lc); g.addColorStop(1, mix(lc, '#300000', 0.5));
          ctx.fillStyle = g; ctx.beginPath(); ctx.ellipse(0, 0, ls * 0.78, ls, 0, 0, TAU); ctx.fill();
          ctx.strokeStyle = rgba('#400000', 0.45); ctx.lineWidth = Math.max(1, U * 0.0016); for (const k of [-0.5, -0.1, 0.3, 0.7]) { ctx.beginPath(); ctx.ellipse(0, 0, ls * 0.78, ls * Math.abs(k) + 0.1, 0, Math.PI * (k < 0 ? 1 : 0), Math.PI * (k < 0 ? 2 : 1)); ctx.stroke(); }
          ctx.fillStyle = '#2A1410'; ctx.fillRect(-ls * 0.3, -ls * 1.08, ls * 0.6, ls * 0.14); ctx.fillRect(-ls * 0.3, ls * 0.94, ls * 0.6, ls * 0.14);
          ctx.restore();
          ctx.save(); ctx.globalCompositeOperation = dark ? 'screen' : 'lighter'; const fl = 0.9 + 0.1 * Math.sin(t * 5 + i * 3);
          glowDisc(ctx, px, py, ls * 2.6, lc, (dark ? 0.55 : 0.32) * fl);
          if (i % 2 === 0) { ctx.save(); ctx.translate(px, y0 + (H - y0) * (0.25 + 0.5 * sv * (1 - sv) * 2)); ctx.scale(0.6, 2.8); glowDisc(ctx, 0, 0, ls * 1.5, lc, (dark ? 0.36 : 0.2) * fl); ctx.restore(); }
          ctx.restore();
        }
      });
      ctx.save(); ctx.globalCompositeOperation = dark ? 'screen' : 'lighter'; glowDisc(ctx, W * 0.5, y0 - H * 0.06, U * 0.9, pal[1], dark ? 0.2 : 0.14); ctx.restore();
    }
    ctx.save(); ctx.globalCompositeOperation = dark ? 'screen' : 'lighter';
    for (const s of signs) { const fl = s.dead ? (Math.sin(t * 11 + s.ph) > 0.2 ? 0.2 : 1) * (Math.sin(t * 3 + s.ph) > -0.6 ? 1 : 0.15) : 0.85 + 0.15 * Math.sin(t * 2.5 + s.ph); glowDisc(ctx, s.x, s.y + s.h / 2, Math.max(s.w, s.h) * 1.2, s.col, (dark ? 0.45 : 0.3) * fl); ctx.strokeStyle = rgba(mix(s.col, '#FFFFFF', 0.5), 0.55 * fl); ctx.lineWidth = Math.max(1, U * 0.0025); ctx.strokeRect(s.x - s.w / 2 + U * 0.004, s.y + U * 0.004, s.w - U * 0.008, s.h - U * 0.008); }
    ctx.restore();
    for (let i = 0; i < 12; i++) { const ph = wrap(t * 0.1 + i / 12, 1), x = W * (portrait ? 0.86 : 0.8) + Math.sin(ph * 6 + i) * U * 0.03, y = y0 - ph * U * 0.4, s = U * (0.02 + 0.05 * ph); ctx.fillStyle = rgba('#FFFFFF', 0.12 * Math.sin(ph * Math.PI)); ctx.beginPath(); ctx.arc(x, y, s, 0, TAU); ctx.fill(); }
    ctx.strokeStyle = rgba('#DDEBFF', dark ? 0.32 : 0.4); ctx.lineWidth = Math.max(1, U * 0.0016); ctx.lineCap = 'round';
    for (let i = 0; i < 90; i++) { const z = r(seed, i, 31), sp = H * (0.9 + 0.8 * z), x = r(seed, i, 32) * W, y = wrap(r(seed, i, 33) * H * 1.2 + t * sp, H * 1.2) - H * 0.1, l = U * (0.03 + 0.05 * z); ctx.globalAlpha = e * (0.25 + 0.5 * z); ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(x - l * 0.12, y + l); ctx.stroke(); }
    ctx.globalAlpha = e;
    for (let i = 0; i < 14; i++) { const cyc = 0.9, n = Math.floor(t / cyc + r(seed, i, 41) * 9), ph = wrap(t + r(seed, i, 42) * cyc * 9 + i * 0.37, cyc) / cyc, x = W * (0.32 + 0.36 * r(seed, i, n, 43)), y = y0 + (H - y0) * (0.1 + 0.85 * r(seed, i, n, 44)); ctx.strokeStyle = rgba('#FFFFFF', 0.4 * (1 - ph)); ctx.lineWidth = Math.max(1, U * 0.0016); ctx.beginPath(); ctx.ellipse(x, y, U * 0.03 * ph, U * 0.008 * ph, 0, 0, TAU); ctx.stroke(); }
  });

  /* ================= 自販機と雨 ================= */
  K.registerBg('echoVendingRain', '自販機と雨', ['glitch', 'emotional', 'calm'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), dark = isDark(sc.bg), portrait = H > W;
    const key = `vend|${seed}|${colourKey(sc)}`, y0 = H * 0.78, mx = W * (portrait ? 0.68 : 0.7), mw = U * (portrait ? 0.36 : 0.3), mh = U * (portrait ? 0.62 : 0.52), lx = W * (portrait ? 0.2 : 0.18);
    const cans = ['#FF5A4A', '#4AA8FF', '#FFC94A', '#6ADB8A', '#FF8AC8', '#FFFFFF'], coin = '#FFB13B', body = dark ? '#2A2E48' : '#5A2A52';
    ctx.globalAlpha = e;
    const scene = layer(env, `${key}|scene`, 0.6, (x, w, h) => {
      x.fillStyle = vertical(x, 0, y0, [[0, dark ? deep(sc.bg, 0.3) : mix(sc.bg, '#7A1A50', 0.35)], [1, dark ? mix(sc.bg, '#2A2A6A', 0.4) : mix(sc.bg, '#FFFFFF', 0.25)]]); x.fillRect(0, 0, w, y0 + 2);
      for (let i = 0; i < 16; i++) { const bx = w * r(seed, i, 1), bw = U * (0.08 + 0.1 * r(seed, i, 2)), bh = H * (0.14 + 0.3 * r(seed, i, 3)); x.fillStyle = dark ? mix(deep(sc.bg, 0.2), '#3A3A7A', 0.2) : mix(sc.bg, '#6A1A48', 0.4); x.fillRect(bx, y0 - bh, bw, bh); for (let k = 0; k < 8; k++) if (r(seed, i, k, 4) > 0.7) { x.fillStyle = rgba('#FFD98A', 0.6); x.fillRect(bx + bw * (0.15 + 0.5 * (k % 2)), y0 - bh + U * 0.02 + Math.floor(k / 2) * U * 0.035, bw * 0.22, U * 0.016); } }
      x.fillStyle = vertical(x, y0, h, [[0, dark ? mix(deep(sc.bg, 0.2), '#22224A', 0.4) : mix(sc.bg, '#7A1A50', 0.4)], [1, dark ? '#05050C' : mix(sc.bg, '#3A0A26', 0.55)]]); x.fillRect(0, y0, w, h - y0);
      x.strokeStyle = rgba('#FFFFFF', dark ? 0.12 : 0.3); x.lineWidth = U * 0.006; x.setLineDash([U * 0.05, U * 0.04]); x.beginPath(); x.moveTo(0, y0 + (h - y0) * 0.55); x.lineTo(w, y0 + (h - y0) * 0.55); x.stroke(); x.setLineDash([]);
      x.fillStyle = '#12121C'; x.fillRect(w * 0.06 - U * 0.006, H * 0.06, U * 0.012, y0 - H * 0.06 + U * 0.02);                 /* pole with wires */
      x.strokeStyle = rgba('#12121C', 0.8); x.lineWidth = Math.max(1, U * 0.002); for (let k = 0; k < 3; k++) { x.beginPath(); x.moveTo(-5, H * (0.1 + k * 0.03)); x.quadraticCurveTo(w * 0.5, H * (0.16 + k * 0.03), w + 5, H * (0.1 + k * 0.03)); x.stroke(); }
      x.fillStyle = '#12121C'; x.fillRect(lx - U * 0.005, H * 0.26, U * 0.01, y0 - H * 0.26 + U * 0.01); x.fillRect(lx - U * 0.005, H * 0.26, U * 0.11, U * 0.01);                /* street lamp */
      /* the machines */
      const m2x = mx - mw * 1.08, m2w = mw * 0.85; x.fillStyle = mix(body, '#000000', 0.25); x.fillRect(m2x - m2w / 2, y0 - mh * 0.92, m2w, mh * 0.92); x.fillStyle = rgba('#DDEBFF', 0.7); x.fillRect(m2x - m2w * 0.42, y0 - mh * 0.85, m2w * 0.84, mh * 0.5);
      for (let rr = 0; rr < 3; rr++) for (let c = 0; c < 4; c++) { x.fillStyle = cans[(rr * 2 + c * 3) % cans.length]; x.fillRect(m2x - m2w * 0.38 + c * m2w * 0.2, y0 - mh * 0.82 + rr * mh * 0.15, m2w * 0.14, mh * 0.11); }
      x.fillStyle = body; x.fillRect(mx - mw / 2, y0 - mh, mw, mh); x.fillStyle = rgba('#FFFFFF', 0.08); x.fillRect(mx - mw / 2, y0 - mh, mw * 0.06, mh); x.fillStyle = '#F4F8FF'; x.fillRect(mx - mw * 0.44, y0 - mh * 0.93, mw * 0.68, mh * 0.6);
      for (let rr = 0; rr < 4; rr++) for (let c = 0; c < 5; c++) { x.fillStyle = cans[(rr * 2 + c) % cans.length]; x.beginPath(); x.roundRect(mx - mw * 0.41 + c * mw * 0.13, y0 - mh * 0.9 + rr * mh * 0.14, mw * 0.1, mh * 0.11, U * 0.004); x.fill(); x.fillStyle = rgba('#FFFFFF', 0.5); x.fillRect(mx - mw * 0.41 + c * mw * 0.13 + mw * 0.012, y0 - mh * 0.9 + rr * mh * 0.14 + mh * 0.012, mw * 0.014, mh * 0.08); x.fillStyle = '#FFF'; x.fillRect(mx - mw * 0.41 + c * mw * 0.13, y0 - mh * 0.9 + rr * mh * 0.14 + mh * 0.115, mw * 0.1, mh * 0.008); }
      x.fillStyle = '#E04B3A'; x.fillRect(mx - mw * 0.44, y0 - mh, mw * 0.68, mh * 0.06); x.fillStyle = '#FFFFFF'; x.font = `bold ${mh * 0.045}px ${FONT}`; x.textAlign = 'center'; x.textBaseline = 'middle'; x.fillText('つめた〜い  あったか〜い', mx - mw * 0.1, y0 - mh * 0.97);
      x.fillStyle = rgba('#FFFFFF', 0.1); x.fillRect(mx + mw * 0.28, y0 - mh * 0.93, mw * 0.18, mh * 0.6); for (let k = 0; k < 4; k++) { x.fillStyle = k % 2 ? '#FF5A4A' : '#4AA8FF'; x.fillRect(mx + mw * 0.31, y0 - mh * 0.88 + k * mh * 0.11, mw * 0.12, mh * 0.06); }
      x.fillStyle = '#0A0A12'; x.fillRect(mx - mw * 0.44, y0 - mh * 0.24, mw * 0.62, mh * 0.16); x.fillStyle = coin; x.fillRect(mx + mw * 0.24, y0 - mh * 0.28, mw * 0.16, mh * 0.03);
    });
    ctx.drawImage(scene, 0, 0, W, H);
    ctx.save(); ctx.globalCompositeOperation = dark ? 'screen' : 'lighter';
    const fl = 0.93 + 0.07 * Math.sin(t * 17) * (Math.sin(t * 0.7) > 0.6 ? 2 : 0.5);
    glowDisc(ctx, mx, y0 - mh * 0.6, mh * 1.5, '#DDEBFF', (dark ? 0.55 : 0.4) * fl); glowDisc(ctx, mx - mw * 1.08, y0 - mh * 0.55, mh * 0.9, '#BFD8FF', dark ? 0.25 : 0.2);
    const lampY = H * 0.26 + U * 0.008; { const cg = ctx.createLinearGradient(0, lampY, 0, y0 + (H - y0) * 0.5); cg.addColorStop(0, rgba('#FFE8B0', dark ? 0.2 : 0.22)); cg.addColorStop(1, rgba('#FFE8B0', 0.02)); ctx.fillStyle = cg; ctx.beginPath(); ctx.moveTo(lx + U * 0.1, lampY); ctx.lineTo(lx + U * 0.12, lampY); ctx.lineTo(lx + U * 0.5, y0 + (H - y0) * 0.5); ctx.lineTo(lx - U * 0.25, y0 + (H - y0) * 0.5); ctx.fill(); } glowDisc(ctx, lx + U * 0.11, lampY, U * 0.35, '#FFE8B0', dark ? 0.5 : 0.4);
    /* puddle under the machine: a mirrored slab of glow, rippled by the rain */
    const pg = ctx.createLinearGradient(0, y0, 0, H); pg.addColorStop(0, rgba('#DDEBFF', dark ? 0.4 : 0.35)); pg.addColorStop(1, rgba('#DDEBFF', 0)); ctx.fillStyle = pg; ctx.fillRect(mx - mw * 0.5, y0, mw, (H - y0) * 0.9);
    ctx.restore();
    ctx.fillStyle = '#F4C070'; ctx.beginPath(); ctx.arc(lx + U * 0.105, lampY + U * 0.004, U * 0.014, 0, TAU); ctx.fill();
    const catx = mx + mw * 0.72, caty = y0 + U * 0.012; cat(ctx, catx, caty, U * 0.16, { sil: dark ? '#10101C' : '#3A1030', t, phase: 1, look: -0.6, flip: true });
    ctx.strokeStyle = rgba('#DDEBFF', dark ? 0.34 : 0.42); ctx.lineWidth = Math.max(1, U * 0.0016); ctx.lineCap = 'round';
    for (let i = 0; i < 110; i++) { const z = r(seed, i, 51), sp = H * (0.9 + 0.8 * z), x = r(seed, i, 52) * W, y = wrap(r(seed, i, 53) * H * 1.2 + t * sp, H * 1.2) - H * 0.1, l = U * (0.03 + 0.05 * z), inCone = Math.abs(x - (lx + U * 0.2)) < (y - lampY) * 0.5 + U * 0.03 && y > lampY, near = Math.abs(x - mx) < mw ? 0.4 : 0; ctx.globalAlpha = e * clamp(0.2 + 0.4 * z + (inCone ? 0.45 : 0) + near); ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(x - l * 0.1, y + l); ctx.stroke(); }
    ctx.globalAlpha = e;
    for (let i = 0; i < 16; i++) { const cyc = 0.85, n = Math.floor(t / cyc + r(seed, i, 61) * 9), ph = wrap(t + r(seed, i, 62) * cyc * 9 + i * 0.41, cyc) / cyc, x = W * (0.1 + 0.85 * r(seed, i, n, 63)), y = y0 + (H - y0) * (0.1 + 0.85 * r(seed, i, n, 64)); ctx.strokeStyle = rgba('#FFFFFF', 0.4 * (1 - ph)); ctx.lineWidth = Math.max(1, U * 0.0016); ctx.beginPath(); ctx.ellipse(x, y, U * 0.03 * ph, U * 0.008 * ph, 0, 0, TAU); ctx.stroke(); }
  });

  /* ================= 屋台の並び ================= */
  const person = (x, cx, base, s, o) => {                          /* a walking figure in yukata, seen from the side */
    const sway = Math.sin(o.t * 5 + o.ph) * s * 0.03, step = Math.sin(o.t * 4 + o.ph) * s * 0.05;
    x.fillStyle = o.cloth; x.beginPath(); x.moveTo(cx - s * 0.12 + sway, base - s * 0.7); x.lineTo(cx + s * 0.12 + sway, base - s * 0.7); x.lineTo(cx + s * 0.15 + step, base - s * 0.04); x.lineTo(cx - s * 0.15 - step, base - s * 0.04); x.fill();
    x.fillStyle = o.obi; x.fillRect(cx - s * 0.13 + sway, base - s * 0.5, s * 0.26, s * 0.09); x.fillStyle = o.skin; x.beginPath(); x.arc(cx + sway * 1.3, base - s * 0.8, s * 0.09, 0, TAU); x.fill();
    x.fillStyle = o.hair; x.beginPath(); x.arc(cx + sway * 1.3, base - s * 0.83, s * 0.095, Math.PI, TAU); x.fill(); if (o.bun) { x.beginPath(); x.arc(cx + sway * 1.3 - s * 0.03, base - s * 0.93, s * 0.045, 0, TAU); x.fill(); }
    x.fillStyle = rgba('#FFFFFF', 0.85); x.fillRect(cx - s * 0.12 + sway, base - s * 0.7, s * 0.24, s * 0.03);
  };
  K.registerBg('echoYatai', '屋台の並び', ['pop', 'emotional', 'calm'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), dark = isDark(sc.bg), portrait = H > W;
    const key = `yatai|${seed}|${colourKey(sc)}`, y0 = H * 0.74, nStall = portrait ? 2 : 3, sw = portrait ? W * 0.46 : Math.min(W * 0.28, U * 0.62), sh = sw * 0.8, stallX = i => W * (portrait ? 0.26 + i * 0.48 : 0.17 + i * 0.33);
    const red = '#E5432B', blue = '#3A6AC8', signs = [['たこ焼き', '#FFB13B'], ['かき氷', '#5BB8FF'], ['金魚', '#FF8A5A'], ['りんご飴', '#FF5A6A'], ['焼きそば', '#FFCB47']];
    ctx.globalAlpha = e;
    const scene = layer(env, `${key}|scene`, 0.6, (x, w, h) => {
      x.fillStyle = vertical(x, 0, y0, [[0, dark ? mix(sc.bg, '#050818', 0.5) : mix(sc.bg, '#4A0F30', 0.35)], [1, dark ? mix(sc.bg, sc.accent, 0.3) : mix(sc.bg, '#FFB060', 0.4)]]); x.fillRect(0, 0, w, y0 + 2);
      if (dark) for (let i = 0; i < 60; i++) { x.fillStyle = rgba('#FFFFFF', 0.2 + 0.5 * r(seed, i, 3)); x.beginPath(); x.arc(r(seed, i, 1) * w, r(seed, i, 2) * y0 * 0.5, U * 0.0018 * (1 + 2 * r(seed, i, 4)), 0, TAU); x.fill(); }
      for (let L = 0; L < 2; L++) { const col = mix(dark ? deep(sc.bg, 0.3) : mix(sc.bg, '#3A0A26', 0.5), '#000000', L * 0.2), b = y0 - U * 0.02; x.fillStyle = col; for (let px = -U * 0.05, i = 0; px < w + U * 0.2; i++) { const bw = U * (0.12 + 0.1 * r(seed, L, i, 5)), bh = U * (0.12 + 0.12 * r(seed, L, i, 6)) * (L ? 0.9 : 1.25); x.fillRect(px, b - bh, bw, bh + 3); x.beginPath(); x.moveTo(px - bw * 0.05, b - bh); x.quadraticCurveTo(px + bw * 0.3, b - bh - U * 0.01, px + bw * 0.5, b - bh - U * 0.04); x.quadraticCurveTo(px + bw * 0.7, b - bh - U * 0.01, px + bw * 1.05, b - bh); x.fill(); if (L) for (let k = 0; k < 3; k++) if (r(seed, i, k, 7) > 0.4) { x.fillStyle = rgba('#FFD98A', 0.75); x.fillRect(px + bw * (0.15 + 0.3 * k), b - bh * 0.6, bw * 0.16, bh * 0.2); x.fillStyle = col; } px += bw + U * 0.01; } }
      x.fillStyle = vertical(x, y0, h, [[0, dark ? mix(sc.bg, '#3A2030', 0.4) : mix(sc.bg, '#7A3A30', 0.3)], [1, dark ? mix(sc.bg, '#0A0610', 0.6) : mix(sc.bg, '#3A1A20', 0.45)]]); x.fillRect(0, y0, w, h - y0);
      for (let i = 0; i < nStall; i++) {
        const cx = stallX(i), top = y0 - sh, stripe = i % 2 ? blue : red; glowDisc(x, cx, y0 + sh * 0.2, sw * 0.8, '#FFB060', dark ? 0.28 : 0.22);
        x.fillStyle = '#4A3028'; x.fillRect(cx - sw * 0.46, top + sh * 0.12, sw * 0.045, sh * 0.88); x.fillRect(cx + sw * 0.415, top + sh * 0.12, sw * 0.045, sh * 0.88); x.fillStyle = dark ? '#3A2A34' : '#5A3A3A'; x.fillRect(cx - sw * 0.44, top + sh * 0.2, sw * 0.88, sh * 0.5);
        const n = 8, aw = sw / n; for (let k = 0; k < n; k++) { x.fillStyle = k % 2 ? '#FFF6EA' : stripe; x.beginPath(); x.moveTo(cx - sw / 2 + k * aw, top); x.lineTo(cx - sw / 2 + (k + 1) * aw, top); x.lineTo(cx - sw / 2 + (k + 1) * aw + sw * 0.02, top + sh * 0.2); x.lineTo(cx - sw / 2 + k * aw - sw * 0.02, top + sh * 0.2); x.fill(); }
        const [word, sc2] = signs[(i + Math.floor(r(seed, 1) * 3)) % signs.length]; x.fillStyle = '#FFF6EA'; x.fillRect(cx - sw * 0.24, top + sh * 0.24, sw * 0.48, sh * 0.16); x.fillStyle = sc2 === '#FFCB47' ? '#B03A1A' : mix(sc2, '#000000', 0.45); x.font = `bold ${sh * 0.12}px ${FONT}`; x.textAlign = 'center'; x.textBaseline = 'middle'; x.fillText(word, cx, top + sh * 0.32);
        x.fillStyle = '#7A4A32'; x.fillRect(cx - sw * 0.48, y0 - sh * 0.3, sw * 0.96, sh * 0.3); x.fillStyle = rgba('#FFFFFF', 0.14); x.fillRect(cx - sw * 0.48, y0 - sh * 0.3, sw * 0.96, sh * 0.03);
        const cy = y0 - sh * 0.3, kind = word;
        if (kind === 'たこ焼き' || kind === '焼きそば') { x.fillStyle = '#2A1A14'; x.fillRect(cx - sw * 0.3, cy - sh * 0.05, sw * 0.6, sh * 0.05); for (let k = 0; k < 8; k++) { x.fillStyle = k % 3 ? '#C8843A' : '#E0A458'; x.beginPath(); x.arc(cx - sw * 0.24 + k * sw * 0.07, cy - sh * 0.09, sw * 0.03, 0, TAU); x.fill(); } }
        else if (kind === 'かき氷') { for (let k = 0; k < 4; k++) { const c = ['#FF8AB4', '#5BB8FF', '#FFE07A', '#7AE0A0'][k]; x.fillStyle = '#FFFFFF'; x.beginPath(); x.moveTo(cx - sw * 0.28 + k * sw * 0.18 - sw * 0.04, cy); x.lineTo(cx - sw * 0.28 + k * sw * 0.18 + sw * 0.04, cy); x.lineTo(cx - sw * 0.28 + k * sw * 0.18, cy - sh * 0.1); x.fill(); x.fillStyle = c; x.beginPath(); x.arc(cx - sw * 0.28 + k * sw * 0.18, cy - sh * 0.12, sw * 0.05, Math.PI, TAU); x.fill(); } }
        else if (kind === '金魚') { x.fillStyle = dark ? '#3A9AC0' : '#8FDCF0'; x.beginPath(); x.ellipse(cx, cy - sh * 0.03, sw * 0.34, sh * 0.05, 0, 0, TAU); x.fill(); for (let k = 0; k < 6; k++) { x.fillStyle = k % 2 ? '#FF7A4A' : '#FFB05A'; x.beginPath(); x.ellipse(cx - sw * 0.24 + k * sw * 0.09, cy - sh * 0.04, sw * 0.02, sw * 0.012, 0, 0, TAU); x.fill(); } }
        else { for (let k = 0; k < 6; k++) { x.strokeStyle = '#B98A55'; x.lineWidth = Math.max(1, sw * 0.008); x.beginPath(); x.moveTo(cx - sw * 0.27 + k * sw * 0.11, cy); x.lineTo(cx - sw * 0.27 + k * sw * 0.11, cy - sh * 0.13); x.stroke(); x.fillStyle = k % 2 ? '#E5323A' : '#FF6A6A'; x.beginPath(); x.arc(cx - sw * 0.27 + k * sw * 0.11, cy - sh * 0.15, sw * 0.03, 0, TAU); x.fill(); x.fillStyle = rgba('#FFFFFF', 0.6); x.beginPath(); x.arc(cx - sw * 0.27 + k * sw * 0.11 - sw * 0.01, cy - sh * 0.16, sw * 0.008, 0, TAU); x.fill(); } }
      }
    });
    ctx.drawImage(scene, 0, 0, W, H);
    /* distant fireworks, small over the rooftops */
    for (let b = 0; b < 2; b++) { const cyc = 5.2, ph = wrap(t + b * 2.6 + r(seed, 4) * cyc, cyc) / cyc, k = Math.floor((t + b * 2.6 + r(seed, 4) * cyc) / cyc), cx = W * (0.2 + 0.6 * r(seed, b, k, 5)), cy = H * (0.1 + 0.15 * r(seed, b, k, 6)), col = ['#FFCB47', '#FF6A8A', '#5BB8FF', '#FFFFFF'][Math.floor(r(seed, b, k, 7) * 4)]; if (ph > 0.75) continue; const p = ph / 0.75, R = U * 0.16 * (1 - (1 - p) ** 2); ctx.save(); ctx.globalCompositeOperation = dark ? 'screen' : 'lighter'; ctx.strokeStyle = rgba(col, (1 - p) * 0.9); ctx.lineWidth = Math.max(1.5, U * 0.004); ctx.lineCap = 'round'; for (let a = 0; a < 20; a++) { const ang = a / 20 * TAU; ctx.beginPath(); ctx.moveTo(cx + Math.cos(ang) * R * 0.75, cy + Math.sin(ang) * R * 0.75 + p * p * U * 0.03); ctx.lineTo(cx + Math.cos(ang) * R, cy + Math.sin(ang) * R + p * p * U * 0.05); ctx.stroke(); } glowDisc(ctx, cx, cy, U * 0.3 * (1 - p), col, 0.3 * (1 - p)); ctx.restore(); }
    /* lanterns strung across, swaying, each one lit */
    [[0.05, 0.13, 0], [0.04, 0.18, 1]].forEach(([ya, yb, off], row) => {
      const count = Math.round(W / (U * 0.16)), yy = u => (1 - u) * (1 - u) * H * ya + 2 * u * (1 - u) * H * yb + u * u * H * ya;
      ctx.strokeStyle = rgba('#2A1818', 0.6); ctx.lineWidth = Math.max(1, U * 0.002); ctx.beginPath(); for (let i = 0; i <= 24; i++) { const u = i / 24; if (i) ctx.lineTo(u * W, yy(u)); else ctx.moveTo(0, yy(0)); } ctx.stroke();
      for (let i = 0; i < count; i++) { const u = (i + 0.5 + off * 0.5) / count; if (u > 1) continue; const lx = u * W + Math.sin(t * 0.9 + i + row * 2) * U * 0.004, ly = yy(u) + U * 0.05, fl = 0.85 + 0.15 * Math.sin(t * 5 + i * 2.1), s = U * 0.03; ctx.save(); ctx.globalCompositeOperation = dark ? 'screen' : 'lighter'; glowDisc(ctx, lx, ly, s * 4.4 * fl, '#FF9A50', dark ? 0.42 : 0.28); ctx.restore(); ctx.fillStyle = (i + row) % 3 ? red : '#FFF6EA'; ctx.beginPath(); ctx.ellipse(lx, ly, s * 0.9, s, 0, 0, TAU); ctx.fill(); ctx.fillStyle = rgba('#FFE0A0', 0.6); ctx.beginPath(); ctx.ellipse(lx - s * 0.2, ly - s * 0.2, s * 0.35, s * 0.5, 0, 0, TAU); ctx.fill(); ctx.fillStyle = '#2A1818'; ctx.fillRect(lx - s * 0.35, ly - s * 1.05, s * 0.7, s * 0.14); ctx.fillRect(lx - s * 0.3, ly + s * 0.92, s * 0.6, s * 0.12); }
    });
    /* steam over the grills, crowd strolling past in front */
    for (let i = 0; i < nStall; i++) { const cx = stallX(i) + sw * 0.2; ctx.strokeStyle = rgba('#FFFFFF', dark ? 0.2 : 0.3); ctx.lineWidth = Math.max(1.4, U * 0.006); ctx.lineCap = 'round'; for (let k = 0; k < 3; k++) { const ph = t * 0.9 + k * 2 + i; ctx.beginPath(); for (let j = 0; j <= 12; j++) { const u = j / 12, xx = cx + (k - 1) * U * 0.02 + Math.sin(u * 5 + ph) * U * 0.008 * u, yy = y0 - sh * 0.36 - u * U * 0.1; if (j) ctx.lineTo(xx, yy); else ctx.moveTo(xx, yy); } ctx.stroke(); } }
    const cloths = ['#3A5AA8', '#C8404A', '#E8B04A', '#5AA88A', '#8A5AB8'], skins = ['#F6D6BC', '#EEC4A4'];
    for (let i = 0; i < 4; i++) { const dir = i % 2 ? -1 : 1, sp = 0.012 + 0.006 * (i % 3), px = wrap((r(seed, i, 71) + dir * t * sp * (dir > 0 ? 1 : -1) * -1) * 1.3, 1.3) * W - W * 0.15, s = U * (0.2 + 0.05 * (i % 2)); person(ctx, px, H * (0.93 + 0.03 * (i % 2)), s, { t, ph: i * 1.9, cloth: cloths[i % 5], obi: i % 2 ? '#FFCB47' : '#FF8AA1', skin: skins[i % 2], hair: '#1A1420', bun: i % 2 === 0 });
      if (i === 1) { ctx.strokeStyle = 'rgba(255,255,255,0.7)'; ctx.lineWidth = Math.max(1, U * 0.002); ctx.beginPath(); ctx.moveTo(px + s * 0.1, H * 0.93 - s * 0.55); ctx.lineTo(px + s * 0.18, H * 0.93 - s * 0.95); ctx.stroke(); ctx.fillStyle = '#FF5A6A'; ctx.beginPath(); ctx.ellipse(px + s * 0.18, H * 0.93 - s * 1.02, s * 0.06, s * 0.075, 0, 0, TAU); ctx.fill(); ctx.fillStyle = 'rgba(255,255,255,0.6)'; ctx.beginPath(); ctx.arc(px + s * 0.16, H * 0.93 - s * 1.04, s * 0.018, 0, TAU); ctx.fill(); } }
    ctx.save(); ctx.globalCompositeOperation = dark ? 'screen' : 'lighter'; for (let i = 0; i < 24; i++) { const sp = H * (0.02 + 0.04 * r(seed, i, 81)), y = wrap(H * 1.1 - r(seed, i, 82) * H * 1.2 - t * sp, H * 1.2) - H * 0.05, x = r(seed, i, 83) * W + Math.sin(t * 0.7 + i) * U * 0.03; ctx.fillStyle = rgba(i % 3 ? '#FFC070' : '#FFE8A8', clamp(0.35 + 0.4 * Math.sin(t * 2 + i)) * (dark ? 0.9 : 0.55)); ctx.beginPath(); ctx.arc(x, y, U * 0.0035, 0, TAU); ctx.fill(); } ctx.restore();
  });

  /* ================= 灯籠流し ================= */
  K.registerBg('echoToroNagashi', '灯籠流し', ['calm', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), dark = isDark(sc.bg), portrait = H > W;
    const key = `toro|${seed}|${colourKey(sc)}`, rv = H * 0.5, mx = W * (portrait ? 0.68 : 0.72), my = H * 0.2;
    const skyTop = dark ? mix(sc.bg, '#03061A', 0.5) : mix(sc.bg, '#4A0F30', 0.3), skyBot = dark ? mix(sc.bg, sc.accent, 0.28) : mix(sc.bg, '#FFB060', 0.5);
    ctx.globalAlpha = e;
    const scene = layer(env, `${key}|scene`, 0.6, (x, w, h) => {
      x.fillStyle = vertical(x, 0, rv, [[0, skyTop], [1, skyBot]]); x.fillRect(0, 0, w, rv + 2);
      if (dark) for (let i = 0; i < 90; i++) { x.fillStyle = rgba('#FFFFFF', 0.2 + 0.6 * r(seed, i, 3)); x.beginPath(); x.arc(r(seed, i, 1) * w, r(seed, i, 2) * rv * 0.85, U * 0.0018 * (1 + 2 * r(seed, i, 4)), 0, TAU); x.fill(); }
      glowDisc(x, mx, my, U * 0.6, dark ? '#FFF1C0' : '#FFE8B8', dark ? 0.4 : 0.7); x.fillStyle = dark ? '#FFF6D8' : '#FFF3D8'; x.beginPath(); x.arc(mx, my, U * 0.065, 0, TAU); x.fill();
      x.fillStyle = dark ? mix(sc.bg, '#0A0A28', 0.6) : mix(sc.bg, '#6A1A48', 0.4); x.beginPath(); x.moveTo(0, rv); for (let i = 0; i <= 10; i++) x.lineTo(w * i / 10, rv - U * (0.08 + 0.14 * Math.sin(i * 1.1 + seed) ** 2)); x.lineTo(w, rv); x.fill();
      x.fillStyle = dark ? mix(sc.bg, '#04041A', 0.7) : mix(sc.bg, '#4A0F30', 0.5); x.beginPath(); x.moveTo(0, rv); for (let i = 0; i <= 14; i++) x.lineTo(w * i / 14, rv - U * (0.02 + 0.03 * Math.sin(i * 1.9 + seed * 3) ** 2)); x.lineTo(w, rv); x.fill();
      const bxc = w * (portrait ? 0.3 : 0.32), bw = U * 0.7; x.fillStyle = dark ? '#06061A' : '#3A0A26'; x.beginPath(); x.moveTo(bxc - bw / 2, rv + U * 0.005); x.quadraticCurveTo(bxc, rv - U * 0.12, bxc + bw / 2, rv + U * 0.005); x.lineTo(bxc + bw / 2, rv + U * 0.03); x.lineTo(bxc - bw / 2, rv + U * 0.03); x.fill();
      x.fillStyle = vertical(x, rv, h, [[0, dark ? mix(skyBot, '#0A0A28', 0.5) : mix(skyBot, '#4A0F30', 0.35)], [1, dark ? '#03040E' : mix(sc.bg, '#3A0A26', 0.55)]]); x.fillRect(0, rv, w, h - rv);
      for (let i = 0; i < 46; i++) { const v = i / 46, y = rv + (h - rv) * v * 0.85, hw = U * (0.012 + 0.09 * v) * (0.6 + r(seed, i, 8)); x.fillStyle = rgba(dark ? '#FFF1C0' : '#FFE8B8', (dark ? 0.16 : 0.3) * (1 - v * 0.6)); x.fillRect(mx - hw + (r(seed, i, 9) - 0.5) * U * 0.03, y, hw * 2, Math.max(1, U * 0.004)); }
      x.fillStyle = dark ? '#03040E' : '#3A0A26'; x.beginPath(); x.moveTo(0, h); x.lineTo(0, h * 0.9); x.quadraticCurveTo(w * 0.15, h * 0.86, w * 0.3, h * 0.92); x.lineTo(w * 0.34, h); x.fill(); x.beginPath(); x.moveTo(w, h); x.lineTo(w, h * 0.92); x.quadraticCurveTo(w * 0.88, h * 0.88, w * 0.7, h * 0.95); x.lineTo(w * 0.66, h); x.fill();
    });
    ctx.drawImage(scene, 0, 0, W, H);
    /* water: sliding glints and soft mist */
    ctx.strokeStyle = rgba('#FFFFFF', dark ? 0.16 : 0.32); ctx.lineWidth = Math.max(1, U * 0.0024); ctx.lineCap = 'round'; for (let i = 0; i < 24; i++) { const v = r(seed, i, 21), y = rv + (H - rv) * (0.05 + 0.9 * v), x0 = wrap(r(seed, i, 22) * W * 1.3 + t * U * (0.01 + 0.03 * v), W * 1.3) - W * 0.15, l = U * (0.05 + 0.12 * v); ctx.beginPath(); ctx.moveTo(x0, y); ctx.quadraticCurveTo(x0 + l / 2, y - U * 0.005, x0 + l, y); ctx.stroke(); }
    /* the lanterns: paper boxes drifting slowly downstream in loose rows, each with its reflection */
    const n = portrait ? 7 : 11;
    for (let i = 0; i < n; i++) {
      const v = r(seed, i, 31), z = 0.7 + 1.0 * v, y = rv + (H - rv) * (0.1 + 0.8 * v) + Math.sin(t * 0.8 + i * 1.3) * U * 0.004, x = wrap(r(seed, i, 32) * W * 1.3 + t * U * (0.012 + 0.03 * v), W * 1.3) - W * 0.15, s = U * 0.05 * z, col = ['#FFC070', '#FF8AA1', '#FFE7A0', '#FFA860'][i % 4], fl = 0.85 + 0.15 * Math.sin(t * 4 + i * 2);
      ctx.save(); ctx.globalCompositeOperation = dark ? 'screen' : 'lighter'; glowDisc(ctx, x, y - s * 0.5, s * 3.4 * fl, col, dark ? 0.5 : 0.35); ctx.restore();
      const g = ctx.createLinearGradient(0, y, 0, y + s * 3); g.addColorStop(0, rgba(col, 0.32)); g.addColorStop(1, rgba(col, 0)); ctx.fillStyle = g; ctx.fillRect(x - s * 0.55, y + s * 0.15, s * 1.1, s * 3);
      ctx.fillStyle = '#5A3A2A'; ctx.beginPath(); ctx.moveTo(x - s * 0.8, y); ctx.lineTo(x + s * 0.8, y); ctx.lineTo(x + s * 0.6, y + s * 0.18); ctx.lineTo(x - s * 0.6, y + s * 0.18); ctx.fill();
      ctx.fillStyle = mix(col, '#FFFFFF', 0.35); ctx.fillRect(x - s * 0.55, y - s * 1.05, s * 1.1, s * 1.05); ctx.fillStyle = rgba('#FFFFFF', 0.4); ctx.fillRect(x - s * 0.55, y - s * 1.05, s * 0.3, s * 1.05);
      ctx.strokeStyle = rgba('#7A4A2A', 0.6); ctx.lineWidth = Math.max(1, s * 0.05); ctx.strokeRect(x - s * 0.55, y - s * 1.05, s * 1.1, s * 1.05); ctx.beginPath(); ctx.moveTo(x, y - s * 1.05); ctx.lineTo(x, y); ctx.stroke();
      ctx.fillStyle = '#5A3A2A'; ctx.beginPath(); ctx.moveTo(x - s * 0.7, y - s * 1.05); ctx.lineTo(x, y - s * 1.35); ctx.lineTo(x + s * 0.7, y - s * 1.05); ctx.fill();
    }
    /* a willow bough drooping in from the top-left, swaying */
    ctx.strokeStyle = dark ? '#0A1418' : '#2A0A20'; ctx.lineWidth = U * 0.012; ctx.lineCap = 'round'; ctx.beginPath(); ctx.moveTo(-U * 0.05, H * 0.04); ctx.quadraticCurveTo(W * 0.18, H * 0.02, W * 0.34, H * 0.1); ctx.stroke();
    ctx.lineWidth = Math.max(1.5, U * 0.004); for (let i = 0; i < 16; i++) { const u = (i + 1) / 17, bx = W * 0.34 * u * 0.95, by = H * 0.04 + (H * 0.06) * u * u, len = H * (0.12 + 0.2 * r(seed, i, 41)); ctx.beginPath(); ctx.moveTo(bx, by); for (let k = 1; k <= 8; k++) { const q = k / 8; ctx.lineTo(bx + Math.sin(t * 0.8 + i + q * 2.5) * U * 0.02 * q, by + len * q); } ctx.stroke(); }
    ctx.save(); ctx.globalCompositeOperation = dark ? 'screen' : 'lighter';
    for (let i = 0; i < 26; i++) { const b = Math.pow(0.5 + 0.5 * Math.sin(t * 1.4 + i * 2.3), 2), x = wrap(r(seed, i, 51) * W + t * U * 0.012 + Math.sin(t * 0.5 + i) * U * 0.03, W), y = H * (0.5 + 0.45 * r(seed, i, 52)) + Math.sin(t * 0.7 + i) * U * 0.02; glowDisc(ctx, x, y, U * 0.022, '#FFF3A0', 0.55 * b); ctx.fillStyle = rgba('#FFF8C0', 0.9 * b); ctx.beginPath(); ctx.arc(x, y, U * 0.0028, 0, TAU); ctx.fill(); }
    ctx.restore();
  });

  /* ================= 能舞台 ================= */
  const pine = (x, cx, base, s, ink, gold, green) => {
    x.strokeStyle = ink; x.lineCap = 'round'; x.lineWidth = s * 0.06; x.beginPath(); x.moveTo(cx, base); x.bezierCurveTo(cx - s * 0.12, base - s * 0.35, cx + s * 0.12, base - s * 0.6, cx - s * 0.04, base - s * 1.0); x.stroke();
    x.lineWidth = s * 0.03; x.beginPath(); x.moveTo(cx - s * 0.02, base - s * 0.55); x.quadraticCurveTo(cx + s * 0.25, base - s * 0.62, cx + s * 0.46, base - s * 0.5); x.moveTo(cx - s * 0.05, base - s * 0.8); x.quadraticCurveTo(cx - s * 0.28, base - s * 0.84, cx - s * 0.5, base - s * 0.72); x.stroke();
    for (const [dx, dy, w0] of [[0, -1.02, 0.34], [0.46, -0.5, 0.3], [-0.5, -0.72, 0.3], [0.2, -0.78, 0.22], [-0.24, -0.5, 0.2]]) for (let k = 0; k < 3; k++) { x.fillStyle = k === 1 ? green : k === 0 ? ink : gold; x.beginPath(); x.ellipse(cx + dx * s, base + dy * s - k * s * 0.03 + s * 0.02, s * w0 * (1 - k * 0.12), s * w0 * 0.22, 0, 0, TAU); x.fill(); }
  };
  K.registerBg('echoNohStage', '能舞台', ['emotional', 'editorial', 'graphic'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), dark = isDark(sc.bg), portrait = H > W;
    const key = `noh|${seed}|${colourKey(sc)}`, y0 = H * 0.7, gold = dark ? '#C89A3A' : '#B48A2C', goldHi = '#F1D27A', ink = dark ? '#0A0808' : '#2A1E14', green = dark ? '#3A5A3A' : '#5A7A4A';
    ctx.globalAlpha = e;
    const scene = layer(env, `${key}|scene`, 0.6, (x, w, h) => {
      x.fillStyle = dark ? mix(sc.bg, '#241608', 0.5) : mix(sc.bg, '#E6C878', 0.35); x.fillRect(0, 0, w, y0);
      const px0 = w * (portrait ? 0.06 : 0.16), px1 = w * (portrait ? 0.94 : 0.84), py0 = H * 0.1;                /* the kagami-ita: a gold panel with a great pine */
      const g = x.createLinearGradient(px0, 0, px1, 0); g.addColorStop(0, dark ? mix(gold, '#000000', 0.45) : mix(gold, '#FFFFFF', 0.35)); g.addColorStop(0.5, dark ? mix(gold, '#000000', 0.15) : mix(gold, '#FFFFFF', 0.5)); g.addColorStop(1, dark ? mix(gold, '#000000', 0.5) : mix(gold, '#FFFFFF', 0.3)); x.fillStyle = g; x.fillRect(px0, py0, px1 - px0, y0 - py0);
      x.fillStyle = rgba(goldHi, dark ? 0.1 : 0.22); for (let yy = py0; yy < y0; yy += U * 0.05) for (let xx = px0 + ((Math.floor((yy - py0) / (U * 0.05)) % 2) * U * 0.025); xx < px1; xx += U * 0.05) if (r(seed, xx | 0, yy | 0, 1) > 0.5) x.fillRect(xx, yy, U * 0.048, U * 0.048);
      pine(x, w * 0.5, y0 - U * 0.01, U * 0.75, ink, gold, green);
      x.strokeStyle = dark ? '#3A2416' : '#6A4A2A'; x.lineWidth = U * 0.012; x.strokeRect(px0, py0, px1 - px0, y0 - py0);
      x.fillStyle = dark ? '#1A0F08' : '#4A2E18'; x.fillRect(0, 0, w, py0 * 0.9); for (let i = 0; i < 30; i++) { x.fillStyle = rgba('#000000', 0.3); x.fillRect(w * i / 30, py0 * 0.4, U * 0.003, py0 * 0.5); }
      x.fillStyle = dark ? '#2A1A10' : '#6A4A2A'; x.beginPath(); x.moveTo(-5, py0 * 0.9); x.quadraticCurveTo(w * 0.5, py0 * 1.25, w + 5, py0 * 0.9); x.lineTo(w + 5, py0 * 0.7); x.lineTo(-5, py0 * 0.7); x.fill();
      x.fillStyle = dark ? '#3A2416' : '#8A5A32'; x.fillRect(px0 - U * 0.03, py0, U * 0.03, y0 - py0 + U * 0.03); x.fillRect(px1, py0, U * 0.03, y0 - py0 + U * 0.03);
      /* the stage: polished planks running toward the wall, with a soft reflection of the panel */
      x.fillStyle = vertical(x, y0, h, [[0, dark ? '#3A2618' : '#B48A5A'], [1, dark ? '#150C06' : '#7A5432']]); x.fillRect(0, y0, w, h - y0);
      x.strokeStyle = rgba('#000000', dark ? 0.4 : 0.22); x.lineWidth = Math.max(1, U * 0.002); for (let i = -12; i <= 12; i++) { x.beginPath(); x.moveTo(w * 0.5 + i * w * 0.015, y0); x.lineTo(w * 0.5 + i * w * 0.11, h); x.stroke(); }
      x.fillStyle = rgba(goldHi, dark ? 0.1 : 0.18); x.fillRect(px0, y0, px1 - px0, (h - y0) * 0.4); x.fillStyle = vertical(x, y0, h, [[0, rgba('#FFFFFF', 0.1)], [0.4, rgba('#FFFFFF', 0)], [1, rgba('#000000', 0.2)]]); x.fillRect(0, y0, w, h - y0);
      x.fillStyle = dark ? '#5A1F1A' : '#A8382A'; x.fillRect(0, y0 - U * 0.004, w, U * 0.01);
      /* the hashigakari bridge on the left, with three small pines standing in white gravel */
      const bw2 = w * (portrait ? 0.34 : 0.22); x.fillStyle = dark ? '#1E1208' : '#8A5A32'; x.beginPath(); x.moveTo(0, y0 + U * 0.02); x.lineTo(bw2, y0 + U * 0.01); x.lineTo(bw2 * 0.95, y0 + U * 0.09); x.lineTo(0, y0 + U * 0.14); x.fill(); x.fillStyle = dark ? '#5A1F1A' : '#C0402E'; x.fillRect(0, y0 - U * 0.05, bw2 * 0.98, U * 0.012); for (let i = 0; i < 4; i++) x.fillRect(bw2 * (0.05 + i * 0.3), y0 - U * 0.05, U * 0.008, U * 0.07);
      x.fillStyle = rgba(dark ? '#B8B4C4' : '#FFFFFF', dark ? 0.3 : 0.75); x.fillRect(0, y0 + U * 0.2, w, h - y0 - U * 0.2); for (let i = 0; i < 90; i++) { x.fillStyle = rgba('#000000', 0.12); x.fillRect(r(seed, i, 31) * w, y0 + U * 0.2 + r(seed, i, 32) * (h - y0 - U * 0.2), U * 0.008, U * 0.003); }
      for (let i = 0; i < 3; i++) pine(x, bw2 * (0.25 + 0.3 * i), y0 + U * 0.21 + i * U * 0.01, U * (0.14 + 0.02 * i), ink, gold, green);
    });
    ctx.drawImage(scene, 0, 0, W, H);
    /* a slow spotlight sweeping the boards, gold dust rising, lanterns hung under the eaves */
    const sx = W * (0.5 + 0.28 * Math.sin(t * 0.25)); ctx.save(); ctx.globalCompositeOperation = dark ? 'screen' : 'lighter'; const sg = ctx.createRadialGradient(sx, y0 + U * 0.06, 0, sx, y0 + U * 0.06, U * 0.5); sg.addColorStop(0, rgba('#FFE8A8', dark ? 0.4 : 0.35)); sg.addColorStop(1, rgba('#FFE8A8', 0)); ctx.fillStyle = sg; ctx.fillRect(sx - U * 0.5, y0 - U * 0.3, U, U * 0.9);
    for (let i = 0; i < 2; i++) { const lx = W * (i ? 0.88 : 0.72) * (portrait ? (i ? 1 : 0.9) : 1), ly = H * 0.24 + Math.sin(t * 0.8 + i) * U * 0.004; glowDisc(ctx, lx, ly, U * 0.2, '#FFB45A', dark ? 0.5 : 0.35); }
    ctx.restore();
    for (let i = 0; i < 2; i++) { const lx = W * (i ? 0.88 : 0.72) * (portrait ? (i ? 1 : 0.9) : 1), ly = H * 0.24 + Math.sin(t * 0.8 + i) * U * 0.004; ctx.strokeStyle = '#2A1810'; ctx.lineWidth = Math.max(1, U * 0.002); ctx.beginPath(); ctx.moveTo(lx, H * 0.11); ctx.lineTo(lx, ly - U * 0.04); ctx.stroke(); ctx.fillStyle = '#D6342A'; ctx.beginPath(); ctx.ellipse(lx, ly, U * 0.03, U * 0.042, 0, 0, TAU); ctx.fill(); ctx.fillStyle = rgba('#FFE0A0', 0.55); ctx.beginPath(); ctx.ellipse(lx - U * 0.008, ly - U * 0.008, U * 0.012, U * 0.022, 0, 0, TAU); ctx.fill(); ctx.fillStyle = '#2A1810'; ctx.fillRect(lx - U * 0.014, ly - U * 0.045, U * 0.028, U * 0.008); ctx.fillRect(lx - U * 0.012, ly + U * 0.037, U * 0.024, U * 0.007); }
    ctx.save(); ctx.globalCompositeOperation = dark ? 'screen' : 'source-over'; for (let i = 0; i < 34; i++) { const b = Math.pow(0.5 + 0.5 * Math.sin(t * (1 + 2 * r(seed, i, 51)) + i * 2.7), 3), x = r(seed, i, 52) * W, y = wrap(r(seed, i, 53) * H - t * H * 0.02 * (0.5 + r(seed, i, 54)), H); ctx.fillStyle = rgba(goldHi, (dark ? 0.85 : 0.75) * b); sparkle(ctx, x, y, U * (0.008 + 0.012 * r(seed, i, 55)) * (0.5 + b)); ctx.fill(); } ctx.restore();
  });

  /* ================= 金雲と鶴 ================= */
  const crane = (x, cx, cy, s, fl, ink, col) => {
    x.save(); x.translate(cx, cy);
    const wing = (dir) => { x.beginPath(); x.moveTo(dir * s * 0.05, -s * 0.02); x.quadraticCurveTo(dir * s * 0.5, -s * (0.3 + fl * 0.45), dir * s * 1.0, -s * (0.05 + fl * 0.5)); x.quadraticCurveTo(dir * s * 0.7, -s * (0.02 + fl * 0.1), dir * s * 0.08, s * 0.1); x.closePath(); };
    x.fillStyle = col; x.strokeStyle = ink; x.lineWidth = Math.max(1, s * 0.025); x.lineJoin = 'round';
    wing(-1); x.fill(); x.stroke(); wing(1); x.fill(); x.stroke();
    x.fillStyle = ink; for (const d of [-1, 1]) { x.beginPath(); x.moveTo(d * s * 0.62, -s * (0.16 + fl * 0.3)); x.quadraticCurveTo(d * s * 0.85, -s * (0.1 + fl * 0.4), d * s * 1.0, -s * (0.05 + fl * 0.5)); x.quadraticCurveTo(d * s * 0.9, -s * (0.02 + fl * 0.18), d * s * 0.7, -s * (0.04 + fl * 0.08)); x.fill(); }
    x.fillStyle = col; x.beginPath(); x.ellipse(0, s * 0.04, s * 0.2, s * 0.08, 0, 0, TAU); x.fill(); x.stroke();
    x.strokeStyle = ink; x.lineWidth = Math.max(1.4, s * 0.05); x.lineCap = 'round'; x.beginPath(); x.moveTo(-s * 0.16, s * 0.02); x.quadraticCurveTo(-s * 0.4, -s * 0.06, -s * 0.5, s * 0.02); x.stroke(); x.beginPath(); x.moveTo(s * 0.18, s * 0.06); x.quadraticCurveTo(s * 0.5, s * 0.16, s * 0.75, s * 0.1); x.stroke();
    x.fillStyle = '#D6342A'; x.beginPath(); x.arc(-s * 0.5, s * 0.0, s * 0.03, 0, TAU); x.fill(); x.restore();
  };
  K.registerBg('echoGoldCrane', '金雲と鶴', ['emotional', 'editorial', 'graphic'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), dark = isDark(sc.bg), portrait = H > W;
    const key = `crane|${seed}|${colourKey(sc)}`, gold = dark ? '#C89A3A' : '#B48A2C', goldHi = '#F1D27A', ink = dark ? '#160E0A' : '#2A1E14';
    ctx.globalAlpha = e;
    const scene = layer(env, `${key}|scene`, 0.6, (x, w, h) => {
      x.fillStyle = dark ? mix(sc.bg, '#3A2810', 0.45) : mix(sc.bg, '#E6C878', 0.4); x.fillRect(0, 0, w, h);
      const n = portrait ? 4 : 6, pw = w / n;
      for (let i = 0; i < n; i++) { const g = x.createLinearGradient(i * pw, 0, (i + 1) * pw, 0); const a = i % 2; g.addColorStop(0, rgba(a ? '#000000' : '#FFFFFF', a ? (dark ? 0.32 : 0.14) : (dark ? 0.08 : 0.3))); g.addColorStop(1, rgba(a ? '#FFFFFF' : '#000000', a ? (dark ? 0.08 : 0.3) : (dark ? 0.32 : 0.14))); x.fillStyle = g; x.fillRect(i * pw, 0, pw, h); }
      const sq = U * 0.045; for (let yy = 0; yy < h; yy += sq) for (let xx = 0; xx < w; xx += sq) { const v = r(seed, xx | 0, yy | 0, 2); x.fillStyle = v > 0.66 ? rgba(goldHi, dark ? 0.16 : 0.3) : v < 0.25 ? rgba('#5A3A10', dark ? 0.2 : 0.1) : rgba(gold, 0.08); x.fillRect(xx + 1, yy + 1, sq - 2, sq - 2); }
      const sx = w * (portrait ? 0.7 : 0.76), sy = h * 0.24; x.fillStyle = dark ? '#B4261E' : '#D6342A'; x.beginPath(); x.arc(sx, sy, U * 0.12, 0, TAU); x.fill(); x.fillStyle = rgba('#FFFFFF', 0.12); x.beginPath(); x.arc(sx - U * 0.03, sy - U * 0.03, U * 0.08, 0, TAU); x.fill();
      for (let L = 0; L < 3; L++) {                                                       /* layered gold clouds with scalloped edges */
        const cy = h * (0.42 + 0.17 * L), amp = U * 0.05; x.fillStyle = L % 2 ? rgba(goldHi, dark ? 0.55 : 0.7) : rgba(gold, dark ? 0.75 : 0.6); x.beginPath(); x.moveTo(-5, h); x.lineTo(-5, cy); for (let i = 0; i <= 12; i++) { const bx = w * i / 12; x.quadraticCurveTo(bx + w / 24 * 0.5, cy - amp * (0.6 + 0.6 * r(seed, L, i, 3)), bx + w / 12 * 0.9, cy + amp * 0.2 * ((i + L) % 2)); } x.lineTo(w + 5, h); x.closePath(); x.fill();
        x.strokeStyle = rgba(ink, dark ? 0.5 : 0.3); x.lineWidth = Math.max(1, U * 0.003); x.stroke();
        x.fillStyle = rgba('#FFFFFF', dark ? 0.06 : 0.16); x.fillRect(0, cy + U * 0.02, w, U * 0.006);
      }
      const wy = h * 0.88; for (let row = 0; row < 3; row++) for (let i = -1; i < w / (U * 0.1) + 1; i++) { const cx = i * U * 0.1 + (row % 2) * U * 0.05, cy = wy + row * U * 0.04; for (let k = 0; k < 3; k++) { x.strokeStyle = k === 0 ? rgba(ink, dark ? 0.6 : 0.4) : rgba(goldHi, dark ? 0.6 : 0.8); x.lineWidth = Math.max(1, U * 0.0028); x.beginPath(); x.arc(cx, cy, U * (0.05 - k * 0.015), Math.PI, TAU); x.stroke(); } }
      const bx = w * (portrait ? 0.1 : 0.06); x.strokeStyle = ink; x.lineCap = 'round'; x.lineWidth = U * 0.02; x.beginPath(); x.moveTo(bx - U * 0.05, h); x.quadraticCurveTo(bx + U * 0.02, h * 0.82, bx + U * 0.16, h * 0.68); x.stroke(); x.lineWidth = U * 0.008; x.beginPath(); x.moveTo(bx + U * 0.02, h * 0.79); x.quadraticCurveTo(bx + U * 0.15, h * 0.76, bx + U * 0.25, h * 0.79); x.stroke();
      for (const [dx, dy] of [[0.16, 0.68], [0.25, 0.79], [0.1, 0.76]]) for (let k = 0; k < 2; k++) { x.fillStyle = k ? (dark ? '#3A5A3A' : '#5A7A4A') : ink; x.beginPath(); x.ellipse(bx + U * dx, h * dy - k * U * 0.006, U * 0.09, U * 0.024, 0, 0, TAU); x.fill(); }
    });
    ctx.drawImage(scene, 0, 0, W, H);
    for (let i = 0; i < (portrait ? 2 : 3); i++) { const cyc = 26 + 8 * i, ph = wrap(t + r(seed, i, 61) * cyc, cyc) / cyc, x = -W * 0.1 + ph * W * 1.2, y = H * (0.2 + 0.14 * i) + Math.sin(ph * TAU * 2 + i) * U * 0.03, fl = Math.sin(t * 2.2 + i * 1.7); ctx.globalAlpha = e * clamp(Math.min(ph, 1 - ph) * 8); crane(ctx, x, y, U * (0.11 - 0.02 * i), fl, ink, '#FFF6E6'); }
    ctx.globalAlpha = e; ctx.save(); ctx.globalCompositeOperation = dark ? 'screen' : 'source-over';
    for (let i = 0; i < 30; i++) { const b = Math.pow(0.5 + 0.5 * Math.sin(t * (1 + 2 * r(seed, i, 71)) + i * 2.7), 3), x = r(seed, i, 72) * W, y = r(seed, i, 73) * H; ctx.fillStyle = rgba(goldHi, (dark ? 0.9 : 0.75) * b); sparkle(ctx, x, y, U * (0.008 + 0.014 * r(seed, i, 74)) * (0.5 + b)); ctx.fill(); }
    const sw = wrap(t * 0.08 + r(seed, 5), 1.4) - 0.2; const g = ctx.createLinearGradient((sw - 0.1) * W, 0, (sw + 0.1) * W, H); g.addColorStop(0, rgba('#FFFFFF', 0)); g.addColorStop(0.5, rgba('#FFF3C8', dark ? 0.12 : 0.22)); g.addColorStop(1, rgba('#FFFFFF', 0)); ctx.fillStyle = g; ctx.fillRect(0, 0, W, H); ctx.restore();
  });
})();
