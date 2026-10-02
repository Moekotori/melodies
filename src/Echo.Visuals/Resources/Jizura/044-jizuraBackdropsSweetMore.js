/* ECHO first-party backdrops, still more sweet scenes: 放課後 (おやつの時間, 夕焼けの帰り道) and 星のまほう (星の遊園地, 星の森).
   Static art goes into cached layers; wheels, steam, flames and fireflies are redrawn every frame. */
(() => {
  'use strict';
  const K = J.echo, E = K.E, TAU = K.TAU, clamp = K.clamp, mix = K.mix, rgba = K.rgba;
  const { U: Umin, isDark, wrap, hues, fade, vertical, glowDisc, star, layer, scroll, shapes } = K;
  const { heart, petal, sakura, puff, sparkle, roundRect } = shapes;
  const colourKey = sc => [sc.bg, sc.fg, sc.accent, sc.accent2, sc.ghostA, sc.ghostB].join('');

  /* ---------- little sweets, drawn from circles and curves ---------- */
  const plate = (x, cx, cy, r, c) => { x.fillStyle = rgba('#000000', 0.08); x.beginPath(); x.ellipse(cx, cy + r * 0.12, r, r * 0.28, 0, 0, TAU); x.fill(); x.fillStyle = c; x.beginPath(); x.ellipse(cx, cy, r, r * 0.26, 0, 0, TAU); x.fill(); x.fillStyle = rgba('#FFFFFF', 0.55); x.beginPath(); x.ellipse(cx, cy - r * 0.03, r * 0.82, r * 0.19, 0, 0, TAU); x.fill(); };
  const strawberry = (x, cx, cy, s) => {
    x.fillStyle = '#F0435F'; x.beginPath(); x.moveTo(cx - s, cy - s * 0.5); x.bezierCurveTo(cx - s * 1.1, cy + s * 0.5, cx - s * 0.2, cy + s * 1.1, cx, cy + s * 1.1); x.bezierCurveTo(cx + s * 0.2, cy + s * 1.1, cx + s * 1.1, cy + s * 0.5, cx + s, cy - s * 0.5); x.bezierCurveTo(cx + s * 0.5, cy - s * 0.8, cx - s * 0.5, cy - s * 0.8, cx - s, cy - s * 0.5); x.fill();
    x.fillStyle = '#FFE3E8'; for (const [a, b] of [[-0.4, -0.1], [0.3, -0.2], [0, 0.3], [-0.3, 0.5], [0.4, 0.4]]) { x.beginPath(); x.ellipse(cx + a * s, cy + b * s, s * 0.06, s * 0.09, 0, 0, TAU); x.fill(); }
    x.fillStyle = '#5DBB63'; for (let i = -2; i <= 2; i++) { x.beginPath(); x.ellipse(cx + i * s * 0.28, cy - s * 0.62, s * 0.2, s * 0.09, i * 0.5, 0, TAU); x.fill(); }
    x.fillStyle = rgba('#FFFFFF', 0.4); x.beginPath(); x.ellipse(cx - s * 0.45, cy - s * 0.15, s * 0.1, s * 0.22, 0.4, 0, TAU); x.fill();
  };
  const cupcake = (x, cx, cy, s, frost, liner) => {
    x.fillStyle = liner; x.beginPath(); x.moveTo(cx - s * 0.8, cy - s * 0.3); x.lineTo(cx + s * 0.8, cy - s * 0.3); x.lineTo(cx + s * 0.6, cy + s * 0.7); x.lineTo(cx - s * 0.6, cy + s * 0.7); x.fill();
    x.strokeStyle = rgba('#FFFFFF', 0.45); x.lineWidth = Math.max(1, s * 0.05); for (let i = -2; i <= 2; i++) { x.beginPath(); x.moveTo(cx + i * s * 0.28, cy - s * 0.25); x.lineTo(cx + i * s * 0.2, cy + s * 0.65); x.stroke(); }
    x.fillStyle = frost; for (const [dy, r] of [[-0.35, 0.95], [-0.75, 0.72], [-1.08, 0.46]]) { x.beginPath(); x.ellipse(cx, cy + dy * s, r * s, s * 0.4, 0, 0, TAU); x.fill(); }
    x.fillStyle = rgba('#FFFFFF', 0.45); x.beginPath(); x.ellipse(cx - s * 0.28, cy - s * 0.78, s * 0.14, s * 0.07, -0.4, 0, TAU); x.fill();
    x.fillStyle = '#F0435F'; x.beginPath(); x.arc(cx, cy - s * 1.42, s * 0.2, 0, TAU); x.fill();
    for (const [a, b, c] of [[-0.5, -0.3, '#FFD86B'], [0.4, -0.45, '#8FD0FF'], [0.05, -0.62, '#FFFFFF'], [-0.2, -0.05, '#FF9DB8']]) { x.fillStyle = c; x.fillRect(cx + a * s, cy + b * s - s * 0.2, s * 0.14, s * 0.06); }
  };
  const macaron = (x, cx, cy, s, c) => {
    x.fillStyle = c; x.beginPath(); x.ellipse(cx, cy - s * 0.2, s, s * 0.34, 0, Math.PI, TAU); x.fill(); x.beginPath(); x.ellipse(cx, cy + s * 0.2, s, s * 0.32, 0, 0, Math.PI); x.fill();
    x.fillStyle = '#FFF6EA'; x.fillRect(cx - s * 0.92, cy - s * 0.2, s * 1.84, s * 0.38);
    x.fillStyle = rgba('#FFFFFF', 0.4); x.beginPath(); x.ellipse(cx - s * 0.35, cy - s * 0.38, s * 0.28, s * 0.09, -0.15, 0, TAU); x.fill();
  };
  const steam = (ctx, x0, y0, s, t, a, seed) => { ctx.strokeStyle = rgba('#FFFFFF', a); ctx.lineWidth = Math.max(1.4, s * 0.09); ctx.lineCap = 'round'; for (let k = 0; k < 3; k++) { const ph = t * 0.9 + k * 2 + seed; ctx.beginPath(); for (let i = 0; i <= 12; i++) { const u = i / 12, xx = x0 + (k - 1) * s * 0.5 + Math.sin(u * 5 + ph) * s * 0.16 * u, yy = y0 - u * s * 1.6; if (i) ctx.lineTo(xx, yy); else ctx.moveTo(xx, yy); } ctx.globalAlpha *= 1; ctx.stroke(); } };

  /* ================= おやつの時間 ================= */
  K.registerBg('echoSweets', 'おやつの時間', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), dark = isDark(sc.bg), portrait = H > W;
    const key = `sweets|${seed}|${colourKey(sc)}`, table = H * 0.72, pastel = [sc.accent, sc.accent2, sc.ghostB, '#FFFFFF'];
    ctx.globalAlpha = e;
    const scene = layer(env, `${key}|scene`, 0.6, (x, w, h) => {
      x.fillStyle = vertical(x, 0, table, [[0, dark ? mix(sc.bg, '#000000', 0.25) : mix(sc.bg, '#FFFFFF', 0.4)], [1, sc.bg]]); x.fillRect(0, 0, w, table);
      const step = U * 0.11;                                            /* polka wallpaper */
      for (let r = 0; r * step * 0.5 < table + step; r++) for (let c = -1; c * step < w + step; c++) { x.fillStyle = rgba(pastel[(r + c) % 3], dark ? 0.14 : 0.24); x.beginPath(); x.arc(c * step + (r % 2 ? step / 2 : 0), r * step * 0.5, U * 0.013, 0, TAU); x.fill(); }
      x.fillStyle = rgba(sc.accent, dark ? 0.3 : 0.4); x.fillRect(0, table - U * 0.075, w, U * 0.006);       /* picture rail and wainscot */
      x.fillStyle = rgba(mix(sc.bg, sc.accent, 0.25), dark ? 0.5 : 0.7); x.fillRect(0, table - U * 0.068, w, U * 0.068);
      /* gingham table, rows widening toward the viewer */
      x.fillStyle = mix(sc.bg, '#FFFFFF', dark ? 0.1 : 0.7); x.fillRect(0, table, w, h - table);
      const rows = 9, gA = dark ? 0.22 : 0.3, cw = U * 0.1, hh = h - table;
      const ppos = r => table + hh * (r / rows) ** 1.3;
      for (let r = 0; r < rows; r += 2) { x.fillStyle = rgba(sc.accent, gA); x.fillRect(0, ppos(r), w, ppos(r + 1) - ppos(r)); }
      for (let c = -30; c < 30; c += 2) { x.fillStyle = rgba(sc.accent, gA); x.beginPath(); const k0 = 0.55, k1 = 1.9; x.moveTo(w * 0.5 + c * cw * k0, table); x.lineTo(w * 0.5 + (c + 1) * cw * k0, table); x.lineTo(w * 0.5 + (c + 1) * cw * k1, h); x.lineTo(w * 0.5 + c * cw * k1, h); x.fill(); }
      x.fillStyle = vertical(x, table, h, [[0, rgba('#000000', 0.08)], [0.25, rgba('#000000', 0)], [1, rgba('#FFFFFF', 0.12)]]); x.fillRect(0, table, w, hh);
      x.fillStyle = rgba('#000000', 0.06); x.fillRect(0, table, w, U * 0.012);
      /* a cake stand on the right: three plates, macarons, cupcakes and berries */
      const sx = w * (portrait ? 0.78 : 0.84), sy = table + U * 0.05;
      x.fillStyle = rgba('#FFFFFF', 0.85); x.fillRect(sx - U * 0.006, sy - U * 0.3, U * 0.012, U * 0.3); x.beginPath(); x.ellipse(sx, sy, U * 0.06, U * 0.014, 0, 0, TAU); x.fill();
      [[0.06, U * 0.1], [0.04, U * 0.2], [0.028, U * 0.3]].forEach(([r0, dy], i) => plate(x, sx, sy - dy, U * (0.14 - i * 0.03), i % 2 ? mix('#FFFFFF', sc.accent, 0.3) : '#FFFFFF'));
      [-0.07, -0.02, 0.05].forEach((dx, i) => macaron(x, sx + dx * U * 1.3, sy - U * 0.12, U * 0.022, ['#FFB6CE', '#BFEAFF', '#FFE58A'][i]));
      cupcake(x, sx - U * 0.03, sy - U * 0.2, U * 0.032, mix('#FFFFFF', sc.accent, 0.45), '#FFD7E4'); cupcake(x, sx + U * 0.05, sy - U * 0.2, U * 0.028, '#FFF1B8', '#BFE7FF');
      strawberry(x, sx, sy - U * 0.335, U * 0.03);
      /* a strawberry shortcake slice on the left, teapot behind */
      const cx0 = w * (portrait ? 0.2 : 0.16), cy0 = table + U * 0.07;
      plate(x, cx0, cy0, U * 0.11, '#FFFFFF');
      x.fillStyle = '#FFF4DC'; x.beginPath(); x.moveTo(cx0 - U * 0.07, cy0 - U * 0.01); x.lineTo(cx0 + U * 0.07, cy0 - U * 0.035); x.lineTo(cx0 + U * 0.07, cy0 - U * 0.1); x.lineTo(cx0 - U * 0.07, cy0 - U * 0.075); x.fill();
      x.fillStyle = '#FFB6C6'; x.beginPath(); x.moveTo(cx0 - U * 0.07, cy0 - U * 0.055); x.lineTo(cx0 + U * 0.07, cy0 - U * 0.075); x.lineTo(cx0 + U * 0.07, cy0 - U * 0.085); x.lineTo(cx0 - U * 0.07, cy0 - U * 0.065); x.fill();
      x.fillStyle = '#FFFFFF'; x.beginPath(); x.moveTo(cx0 - U * 0.07, cy0 - U * 0.075); x.quadraticCurveTo(cx0, cy0 - U * 0.14, cx0 + U * 0.07, cy0 - U * 0.1); x.lineTo(cx0 + U * 0.07, cy0 - U * 0.1); x.lineTo(cx0 - U * 0.07, cy0 - U * 0.075); x.fill();
      strawberry(x, cx0 - U * 0.005, cy0 - U * 0.115, U * 0.03);
      const tx = cx0 + U * 0.2, ty = table + U * 0.045;
      x.fillStyle = mix('#FFFFFF', sc.accent, 0.35); x.beginPath(); x.ellipse(tx, ty - U * 0.05, U * 0.055, U * 0.048, 0, 0, TAU); x.fill(); x.fillRect(tx - U * 0.03, ty - U * 0.108, U * 0.06, U * 0.014); x.beginPath(); x.arc(tx, ty - U * 0.112, U * 0.008, 0, TAU); x.fill();
      x.strokeStyle = mix('#FFFFFF', sc.accent, 0.35); x.lineWidth = U * 0.01; x.beginPath(); x.arc(tx + U * 0.06, ty - U * 0.05, U * 0.024, -1.2, 1.2); x.stroke(); x.beginPath(); x.moveTo(tx - U * 0.05, ty - U * 0.06); x.quadraticCurveTo(tx - U * 0.09, ty - U * 0.085, tx - U * 0.085, ty - U * 0.1); x.stroke();
      x.fillStyle = rgba('#FFFFFF', 0.5); x.beginPath(); x.ellipse(tx - U * 0.02, ty - U * 0.065, U * 0.012, U * 0.025, 0.4, 0, TAU); x.fill(); heart(x, tx, ty - U * 0.05, U * 0.014); x.fillStyle = rgba(sc.accent, 0.9); x.fill();
    });
    ctx.drawImage(scene, 0, 0, W, H);
    /* balloons and a heart garland swaying up on the wall */
    ctx.strokeStyle = rgba(mix(sc.fg, sc.bg, 0.4), 0.5); ctx.lineWidth = Math.max(1, U * 0.002); ctx.beginPath(); ctx.moveTo(-5, H * 0.03); ctx.quadraticCurveTo(W * 0.5, H * 0.16, W + 5, H * 0.03); ctx.stroke();
    const count = Math.round(W / (U * 0.1));
    for (let i = 0; i < count; i++) { const u = (i + 0.5) / count, y = (1 - u) * (1 - u) * H * 0.03 + 2 * u * (1 - u) * H * 0.16 + u * u * H * 0.03, sw = Math.sin(t * 1.4 + i) * U * 0.004; ctx.fillStyle = rgba(pastel[i % 4], 0.95); heart(ctx, u * W + sw, y + U * 0.03, U * 0.026); ctx.fill(); ctx.fillStyle = rgba('#FFFFFF', 0.5); ctx.beginPath(); ctx.arc(u * W - U * 0.009 + sw, y + U * 0.02, U * 0.006, 0, TAU); ctx.fill(); }
    [[0.08, 0.34, sc.accent], [0.13, 0.4, sc.accent2], [0.05, 0.44, '#BFEAFF']].forEach(([ux, uy, c], i) => {
      if (portrait && i === 2) return; const bx = W * ux + Math.sin(t * 0.7 + i * 2) * U * 0.012, by = H * uy + Math.sin(t * 0.9 + i) * U * 0.01, r = U * 0.05;
      ctx.strokeStyle = rgba('#FFFFFF', 0.55); ctx.lineWidth = Math.max(1, U * 0.0016); ctx.beginPath(); ctx.moveTo(bx, by + r * 1.15); ctx.quadraticCurveTo(bx + r * 0.3, by + r * 2.4, W * 0.1, H * 0.7); ctx.stroke();
      const g = ctx.createRadialGradient(bx - r * 0.3, by - r * 0.35, r * 0.1, bx, by, r); g.addColorStop(0, rgba(mix(c, '#FFFFFF', 0.6), 0.98)); g.addColorStop(1, rgba(c, 0.95)); ctx.fillStyle = g; ctx.beginPath(); ctx.ellipse(bx, by, r * 0.86, r, 0, 0, TAU); ctx.fill();
      ctx.fillStyle = rgba(c, 0.95); ctx.beginPath(); ctx.moveTo(bx, by + r); ctx.lineTo(bx - r * 0.14, by + r * 1.2); ctx.lineTo(bx + r * 0.14, by + r * 1.2); ctx.fill();
    });
    /* steam over the teacup, sprinkles falling, sugar glints on the table */
    steam(ctx, W * (portrait ? 0.2 : 0.16) + U * 0.2, H * 0.72 + U * 0.045 - U * 0.11, U * 0.05, t, 0.32, seed);
    for (let i = 0; i < 26; i++) { const sp = H * (0.03 + 0.05 * J.r(seed, i, 41)), y = wrap(J.r(seed, i, 42) * H * 1.2 + t * sp, H * 1.2) - H * 0.1, x = J.r(seed, i, 43) * W + Math.sin(t * 0.6 + i) * U * 0.02; ctx.save(); ctx.translate(x, y); ctx.rotate(t * (0.6 + J.r(seed, i, 44)) + i); ctx.fillStyle = rgba(pastel[i % 4], 0.9); ctx.fillRect(-U * 0.008, -U * 0.0022, U * 0.016, U * 0.0044); ctx.restore(); }
    ctx.globalCompositeOperation = dark ? 'screen' : 'source-over';
    for (let i = 0; i < 8; i++) { const b = Math.pow(0.5 + 0.5 * Math.sin(t * (1 + J.r(seed, i, 51)) + i * 2.3), 3), x = W * (0.1 + 0.8 * J.r(seed, i, 52)), y = H * (0.74 + 0.2 * J.r(seed, i, 53)); ctx.fillStyle = rgba('#FFFFFF', 0.85 * b); sparkle(ctx, x, y, U * 0.014 * (0.5 + b)); ctx.fill(); }
    ctx.globalCompositeOperation = 'source-over';
  });

  /* ================= 夕焼けの帰り道 ================= */
  K.registerBg('echoHomeward', '夕焼けの帰り道', ['calm', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.9), dark = isDark(sc.bg), portrait = H > W;
    const key = `homeward|${seed}|${colourKey(sc)}`, hz = H * 0.66, sunX = W * (portrait ? 0.5 : 0.7), sunY = hz - U * 0.02;
    const skyTop = dark ? mix(sc.bg, '#1B1A5A', 0.55) : mix(sc.bg, '#B9C4FF', 0.5), skyMid = mix(sc.accent, '#FFB6C8', 0.4), skyLow = dark ? mix(sc.accent2, '#FF8A5C', 0.55) : mix('#FFC77A', sc.accent2, 0.35);
    ctx.globalAlpha = e;
    const back = layer(env, `${key}|back`, 0.5, (x, w, h) => {
      x.fillStyle = vertical(x, 0, hz, [[0, skyTop], [0.5, mix(skyMid, skyTop, dark ? 0.45 : 0.3)], [0.85, skyMid], [1, skyLow]]); x.fillRect(0, 0, w, hz + 2);
      glowDisc(x, sunX, sunY, U * 0.9, '#FFE0A8', dark ? 0.5 : 0.6); glowDisc(x, sunX, sunY, U * 0.3, '#FFF4D0', 0.75);
      x.fillStyle = '#FFF3C8'; x.beginPath(); x.arc(sunX, sunY, U * 0.09, 0, TAU); x.fill();
      for (let c = 0; c < 7; c++) { const cx = w * J.r(seed, c, 1), cy = hz * (0.15 + 0.6 * J.r(seed, c, 2)), cw = U * (0.3 + 0.35 * J.r(seed, c, 3)), lit = 0.4 + 0.6 * (cy / hz);
        K.puffCloud(x, cx, cy, cw, cw * 0.35, mix(skyMid, '#FFFFFF', 0.25), 0.7, J.h(seed, c, 4), 1, dark); x.fillStyle = rgba(mix(skyLow, '#FFF3D0', 0.3), 0.45 * lit); puff(x, cx, cy + cw * 0.05, cw * 0.86, cw * 0.22); x.fill(); }
      x.fillStyle = mix(skyLow, dark ? '#2B1F4A' : sc.fg, dark ? 0.6 : 0.26);        /* distant town and hills */
      x.beginPath(); x.moveTo(0, hz); for (let i = 0; i <= 12; i++) x.lineTo(w * i / 12, hz - U * 0.03 - Math.sin(i * 1.4 + seed) * U * 0.02); x.lineTo(w, hz); x.fill();
      let bx = 0, i = 0; x.fillStyle = mix(skyLow, dark ? '#1A1238' : sc.fg, dark ? 0.75 : 0.42); while (bx < w) { const bw = U * (0.03 + 0.05 * J.r(seed, i, 4)), bh = U * (0.03 + 0.08 * J.r(seed, i, 5)); x.fillRect(bx, hz - bh, bw, bh + 2); bx += bw + U * 0.004; i++; }
      /* the road, warm on top, with the long slanting shadows */
      x.fillStyle = vertical(x, hz, h, [[0, mix(skyLow, dark ? '#3A2A5A' : '#E9C6C0', 0.5)], [1, mix(skyMid, dark ? '#2A2050' : sc.fg, dark ? 0.6 : 0.3)]]); x.fillRect(0, hz, w, h - hz);
      x.strokeStyle = rgba('#FFFFFF', dark ? 0.16 : 0.55); x.lineWidth = Math.max(1.5, U * 0.004); x.setLineDash([U * 0.05, U * 0.05]); x.beginPath(); x.moveTo(w * 0.5, hz); x.lineTo(w * (0.5 + (sunX / w - 0.5) * -0.2), h); x.stroke(); x.setLineDash([]);
      x.fillStyle = rgba('#000000', dark ? 0.2 : 0.1); for (let p = 0; p < 2; p++) { const px = w * (p ? 0.9 : 0.1); x.beginPath(); x.moveTo(px - U * 0.006, hz + U * 0.02); x.lineTo(px + U * 0.006, hz + U * 0.02); x.lineTo(px + (sunX < w * 0.5 ? U * 0.5 : -U * 0.5), h); x.lineTo(px + (sunX < w * 0.5 ? U * 0.42 : -U * 0.58), h); x.fill(); }
    });
    ctx.drawImage(back, 0, 0, W, H);
    /* utility poles and sagging wires, silhouetted, with birds perched and flying */
    const pole = mix(skyLow, dark ? '#150E30' : '#3A2A44', dark ? 0.8 : 0.7);
    ctx.fillStyle = pole; ctx.strokeStyle = pole; ctx.lineWidth = Math.max(1.4, U * 0.003);
    [[0.1, 1], [0.9, 1]].forEach(([ux], n) => { const px = W * ux, top = H * 0.16; ctx.fillRect(px - U * 0.006, top, U * 0.012, hz + U * 0.05 - top); ctx.fillRect(px - U * 0.05, top + U * 0.02, U * 0.1, U * 0.006); ctx.fillRect(px - U * 0.04, top + U * 0.05, U * 0.08, U * 0.005); });
    for (let w = 0; w < 3; w++) { const yy = H * (0.18 + w * 0.012); ctx.beginPath(); ctx.moveTo(W * 0.1 - U * 0.045 + w * U * 0.045, yy + U * 0.02); ctx.quadraticCurveTo(W * 0.5, yy + U * 0.11 + w * U * 0.012, W * 0.9 - U * 0.045 + w * U * 0.045, yy + U * 0.02); ctx.stroke(); }
    [[0.34, 0.5], [0.4, 0.51], [0.62, 0.52]].forEach(([u, v], i) => { const bx = W * u, by = H * 0.18 + U * 0.03 + Math.sin(u * 20) * 0, sy = (1 - u) * 0; const wy = H * 0.18 + U * 0.02 + (1 - Math.pow(2 * ((bx - W * 0.1) / (W * 0.8)) - 1, 2)) * U * 0.05 * 1.9; ctx.beginPath(); ctx.ellipse(bx, wy - U * 0.011, U * 0.012, U * 0.014, 0, 0, TAU); ctx.fill(); ctx.beginPath(); ctx.arc(bx + U * 0.008, wy - U * 0.026, U * 0.007, 0, TAU); ctx.fill(); });
    for (let i = 0; i < 5; i++) { const bx = wrap(t * U * 0.07 + i * U * 0.11 + J.r(seed, i, 31) * W, W + 60) - 30, by = H * (0.1 + 0.15 * J.r(seed, i, 32)) + Math.sin(t * 0.8 + i) * U * 0.02, fl = Math.sin(t * 7 + i * 2) * U * 0.008; ctx.lineWidth = Math.max(1.4, U * 0.003); ctx.beginPath(); ctx.moveTo(bx - U * 0.014, by + fl); ctx.quadraticCurveTo(bx - U * 0.006, by - U * 0.008, bx, by); ctx.quadraticCurveTo(bx + U * 0.006, by - U * 0.008, bx + U * 0.014, by + fl); ctx.stroke(); }
    /* dragonflies and drifting petals in the golden light, and the sun glinting on the road */
    ctx.globalCompositeOperation = 'screen';
    for (let i = 0; i < 22; i++) { const b = Math.pow(0.5 + 0.5 * Math.sin(t * (0.8 + J.r(seed, i, 61)) + i * 2.1), 2), x = W * J.r(seed, i, 62) + Math.sin(t * 0.3 + i) * U * 0.03, y = H * (0.3 + 0.6 * J.r(seed, i, 63)) + Math.cos(t * 0.35 + i) * U * 0.03; glowDisc(ctx, x, y, U * (0.01 + 0.016 * J.r(seed, i, 64)), '#FFE7B0', 0.5 * b); }
    ctx.globalCompositeOperation = 'source-over';
    for (let i = 0; i < 14; i++) { const y = wrap(J.r(seed, i, 71) * H * 1.2 + t * H * (0.03 + 0.04 * J.r(seed, i, 72)), H * 1.2) - H * 0.1, x = wrap(J.r(seed, i, 73) * W - t * U * 0.02, W + 40) - 20; ctx.save(); ctx.translate(x, y); ctx.rotate(t * 0.6 + i); ctx.scale(1, 0.55 + 0.45 * Math.abs(Math.sin(t + i))); ctx.fillStyle = rgba('#FFC4D6', 0.85); petal(ctx, 0, 0, U * (0.007 + 0.008 * J.r(seed, i, 75))); ctx.fill(); ctx.restore(); }
  });

  /* ================= 星の遊園地 ================= */
  K.registerBg('echoStarPark', '星の遊園地', ['emotional', 'pop', 'calm'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.9), dark = isDark(sc.bg), portrait = H > W;
    const key = `starpark|${seed}|${colourKey(sc)}`, pal = dark ? [sc.accent, sc.accent2, '#9FE3F5', '#C7A8FF'] : [sc.accent, sc.accent2, sc.ghostB, sc.ghostA], gy = H * 0.84, blend = dark ? 'screen' : 'source-over';
    ctx.globalAlpha = e;
    const back = layer(env, `${key}|back`, 0.5, (x, w, h) => {
      x.fillStyle = vertical(x, 0, h, [[0, dark ? mix(sc.bg, '#000000', 0.32) : mix(sc.bg, '#FFFFFF', 0.3)], [0.65, sc.bg], [1, mix(sc.bg, sc.accent, dark ? 0.3 : 0.2)]]); x.fillRect(0, 0, w, h);
      for (let i = 0; i < 100; i++) { x.fillStyle = rgba(pal[i % 4], dark ? 0.25 + 0.55 * J.r(seed, i, 12) : 0.28); x.beginPath(); x.arc(w * J.r(seed, i, 10), h * J.r(seed, i, 11) * 0.7, U * (0.0015 + 0.0028 * J.r(seed, i, 13)), 0, TAU); x.fill(); }
      x.fillStyle = mix(sc.bg, dark ? '#1B1442' : sc.accent, dark ? 0.5 : 0.3); x.beginPath(); x.moveTo(0, gy); for (let i = 0; i <= 10; i++) x.lineTo(w * i / 10, gy - U * 0.04 - Math.sin(i * 1.3 + seed) * U * 0.03); x.lineTo(w, gy); x.lineTo(w, h); x.lineTo(0, h); x.fill();
      x.fillStyle = mix(sc.bg, dark ? '#0F0A2C' : sc.fg, dark ? 0.6 : 0.2); x.fillRect(0, gy, w, h - gy);
    });
    ctx.drawImage(back, 0, 0, W, H);
    /* the big wheel: rim, twelve spokes, gondolas that stay upright, a ring of blinking lights */
    const wx = W * (portrait ? 0.5 : 0.7), wr = U * (portrait ? 0.34 : 0.36), wy = gy - wr - U * 0.04, spin = t * 0.12;
    ctx.globalCompositeOperation = blend; glowDisc(ctx, wx, wy, wr * 1.5, pal[0], dark ? 0.18 : 0.12); ctx.globalCompositeOperation = 'source-over';
    ctx.strokeStyle = rgba(dark ? '#FFF6FF' : mix(sc.fg, sc.bg, 0.3), dark ? 0.75 : 0.7); ctx.lineWidth = Math.max(2, U * 0.006);
    ctx.beginPath(); ctx.moveTo(wx - wr * 0.5, gy); ctx.lineTo(wx, wy); ctx.lineTo(wx + wr * 0.5, gy); ctx.stroke();
    ctx.beginPath(); ctx.arc(wx, wy, wr, 0, TAU); ctx.stroke(); ctx.lineWidth = Math.max(1, U * 0.0028); ctx.beginPath(); ctx.arc(wx, wy, wr * 0.86, 0, TAU); ctx.stroke();
    for (let i = 0; i < 12; i++) { const a = spin + i / 12 * TAU; ctx.beginPath(); ctx.moveTo(wx, wy); ctx.lineTo(wx + Math.cos(a) * wr, wy + Math.sin(a) * wr); ctx.stroke(); }
    ctx.fillStyle = rgba(pal[1], 0.95); ctx.beginPath(); ctx.arc(wx, wy, U * 0.02, 0, TAU); ctx.fill();
    for (let i = 0; i < 36; i++) { const a = spin + i / 36 * TAU, on = Math.floor(t * 3 + i) % 3 !== 0, c = pal[i % 4]; ctx.globalCompositeOperation = blend; if (on) glowDisc(ctx, wx + Math.cos(a) * wr, wy + Math.sin(a) * wr, U * 0.02, c, 0.7); ctx.globalCompositeOperation = 'source-over'; ctx.fillStyle = rgba(mix(c, '#FFFFFF', on ? 0.6 : 0.1), on ? 1 : 0.5); ctx.beginPath(); ctx.arc(wx + Math.cos(a) * wr, wy + Math.sin(a) * wr, U * 0.005, 0, TAU); ctx.fill(); }
    for (let i = 0; i < 12; i++) { const a = spin + (i + 0.5) / 12 * TAU, gx = wx + Math.cos(a) * wr, gyy = wy + Math.sin(a) * wr, c = pal[i % 4], sw = Math.sin(t * 1.2 + i) * 0.05; ctx.save(); ctx.translate(gx, gyy); ctx.rotate(sw); ctx.strokeStyle = rgba('#FFFFFF', 0.6); ctx.lineWidth = Math.max(1, U * 0.002); ctx.beginPath(); ctx.moveTo(-U * 0.012, 0); ctx.lineTo(0, U * 0.02); ctx.lineTo(U * 0.012, 0); ctx.stroke(); ctx.fillStyle = rgba(c, 0.97); roundRect(ctx, -U * 0.024, U * 0.02, U * 0.048, U * 0.034, U * 0.008); ctx.fill(); ctx.fillStyle = rgba('#FFFFFF', 0.85); ctx.fillRect(-U * 0.016, U * 0.027, U * 0.032, U * 0.014); ctx.fillStyle = rgba(c, 0.97); heart(ctx, 0, U * 0.02, U * 0.008); ctx.fill(); ctx.restore(); }
    /* carousel: a scalloped canopy on the left with turning lights */
    const cx = W * (portrait ? 0.24 : 0.22), cw = U * 0.24, cy = gy - U * 0.02;
    ctx.fillStyle = rgba(mix(sc.bg, '#FFFFFF', dark ? 0.2 : 0.6), 0.95); ctx.fillRect(cx - cw * 0.45, cy - U * 0.16, cw * 0.9, U * 0.16);
    for (let i = 0; i < 6; i++) { ctx.fillStyle = rgba(pal[i % 2 ? 0 : 1], 0.92); ctx.fillRect(cx - cw * 0.5 + i * cw / 6, cy - U * 0.13, cw / 6 + 1, U * 0.13); }
    ctx.fillStyle = rgba(pal[0], 0.98); ctx.beginPath(); ctx.moveTo(cx - cw * 0.62, cy - U * 0.16); ctx.lineTo(cx, cy - U * 0.29); ctx.lineTo(cx + cw * 0.62, cy - U * 0.16); ctx.closePath(); ctx.fill();
    for (let i = 0; i < 7; i++) { ctx.fillStyle = rgba(i % 2 ? '#FFFFFF' : pal[1], 0.95); ctx.beginPath(); ctx.arc(cx - cw * 0.62 + i * cw * 1.24 / 6, cy - U * 0.16, U * 0.012, 0, Math.PI); ctx.fill(); }
    ctx.fillStyle = rgba(pal[1], 0.98); star(ctx, cx, cy - U * 0.31, U * 0.026, 5, 0.45); ctx.fill();
    for (let i = 0; i < 5; i++) { const u = (i + 0.5) / 5, px = cx - cw * 0.45 + u * cw * 0.9, hy = cy - U * 0.05 + Math.sin(t * 1.5 + i * 1.3) * U * 0.02; ctx.strokeStyle = rgba('#FFFFFF', 0.7); ctx.lineWidth = Math.max(1, U * 0.0025); ctx.beginPath(); ctx.moveTo(px, cy - U * 0.13); ctx.lineTo(px, cy); ctx.stroke(); ctx.fillStyle = rgba(pal[(i + 2) % 4], 0.95); ctx.beginPath(); ctx.ellipse(px, hy, U * 0.02, U * 0.014, 0, 0, TAU); ctx.fill(); ctx.beginPath(); ctx.arc(px + U * 0.015, hy - U * 0.012, U * 0.008, 0, TAU); ctx.fill(); }
    ctx.globalCompositeOperation = blend; for (let i = 0; i < 9; i++) { const b = Math.floor(t * 4 + i) % 2; glowDisc(ctx, cx - cw * 0.45 + i * cw * 0.9 / 8, cy - U * 0.155, U * 0.02, pal[i % 4], 0.5 + 0.4 * b); } ctx.globalCompositeOperation = 'source-over';
    /* lamps along the path and star balloons floating up */
    for (let i = 0; i < 7; i++) { const lx = W * (i + 0.5) / 7, on = 0.8 + 0.2 * Math.sin(t * 2 + i); ctx.strokeStyle = rgba(mix(sc.fg, sc.bg, 0.4), 0.6); ctx.lineWidth = Math.max(1, U * 0.003); ctx.beginPath(); ctx.moveTo(lx, gy + U * 0.03); ctx.lineTo(lx, gy - U * 0.03); ctx.stroke(); ctx.globalCompositeOperation = blend; glowDisc(ctx, lx, gy - U * 0.035, U * 0.045, '#FFE7A8', 0.5 * on); ctx.globalCompositeOperation = 'source-over'; ctx.fillStyle = '#FFF3C8'; ctx.beginPath(); ctx.arc(lx, gy - U * 0.035, U * 0.008, 0, TAU); ctx.fill(); }
    for (let i = 0; i < 4; i++) { const bx = W * (0.05 + 0.9 * J.r(seed, i, 21)) + Math.sin(t * 0.5 + i * 2) * U * 0.02, by = H * 0.95 - wrap(t * U * 0.018 + i * H * 0.3, H * 1.05), r = U * 0.034, c = pal[i % 4]; ctx.strokeStyle = rgba('#FFFFFF', 0.5); ctx.lineWidth = Math.max(1, U * 0.0016); ctx.beginPath(); ctx.moveTo(bx, by + r * 0.9); ctx.quadraticCurveTo(bx + r * 0.4, by + r * 2.2, bx - r * 0.1, by + r * 3.2); ctx.stroke(); ctx.fillStyle = rgba(c, 0.95); star(ctx, bx, by, r, 5, 0.5); ctx.fill(); ctx.fillStyle = rgba('#FFFFFF', 0.5); ctx.beginPath(); ctx.arc(bx - r * 0.25, by - r * 0.3, r * 0.14, 0, TAU); ctx.fill(); }
    ctx.globalCompositeOperation = blend; for (let i = 0; i < 20; i++) { const b = Math.pow(0.5 + 0.5 * Math.sin(t * (0.9 + 1.4 * J.r(seed, i, 41)) + i * 3.1), 2); ctx.fillStyle = rgba(pal[i % 4], 0.35 + 0.55 * b); sparkle(ctx, W * J.r(seed, i, 43), H * J.r(seed, i, 44) * 0.6, U * (0.008 + 0.014 * J.r(seed, i, 42)) * (0.6 + b)); ctx.fill(); } ctx.globalCompositeOperation = 'source-over';
  });

  /* ================= 星の森 ================= */
  K.registerBg('echoGlowForest', '星の森', ['calm', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.9), dark = isDark(sc.bg), portrait = H > W;
    const key = `glowforest|${seed}|${colourKey(sc)}`, pal = dark ? [sc.accent, sc.accent2, '#9FE3F5', '#C7A8FF'] : [sc.accent, sc.accent2, sc.ghostB, sc.ghostA], blend = dark ? 'screen' : 'source-over', gy = H * 0.86;
    const tree = (x, cx, base, hgt, w, colour, layerId, i) => {
      const tiers = 6, lit = mix(colour, dark ? '#9A88E0' : '#FFFFFF', dark ? 0.24 : 0.32);
      for (let k = 0; k < tiers; k++) {
        const f = k / (tiers - 1), lw = w * (1 - f * 0.72) * (1 + 0.06 * Math.sin(i + k)), top = base - hgt * (0.16 + 0.84 * f), bot = top + hgt * 0.26;
        x.fillStyle = colour; x.beginPath(); x.moveTo(cx, top - hgt * 0.1); x.quadraticCurveTo(cx - lw * 0.28, top + hgt * 0.05, cx - lw * 0.5, bot); x.quadraticCurveTo(cx - lw * 0.33, bot + hgt * 0.05, cx - lw * 0.17, bot); x.quadraticCurveTo(cx, bot + hgt * 0.06, cx + lw * 0.17, bot); x.quadraticCurveTo(cx + lw * 0.33, bot + hgt * 0.05, cx + lw * 0.5, bot); x.quadraticCurveTo(cx + lw * 0.28, top + hgt * 0.05, cx, top - hgt * 0.1); x.fill();
        x.strokeStyle = rgba(lit, 0.5); x.lineWidth = Math.max(1, w * 0.018); x.beginPath(); x.moveTo(cx, top - hgt * 0.1); x.quadraticCurveTo(cx + lw * 0.28, top + hgt * 0.05, cx + lw * 0.5, bot); x.stroke();
      }
      x.fillRect(cx - w * 0.04, base - hgt * 0.16, w * 0.08, hgt * 0.2);
    };
    ctx.globalAlpha = e;
    const back = layer(env, `${key}|back`, 0.5, (x, w, h) => {
      x.fillStyle = vertical(x, 0, h, [[0, dark ? mix(sc.bg, '#000000', 0.35) : mix(sc.bg, '#FFFFFF', 0.3)], [0.6, sc.bg], [1, mix(sc.bg, sc.accent, dark ? 0.35 : 0.25)]]); x.fillRect(0, 0, w, h);
      for (let i = 0; i < 90; i++) { x.fillStyle = rgba(pal[i % 4], dark ? 0.25 + 0.55 * J.r(seed, i, 12) : 0.28); x.beginPath(); x.arc(w * J.r(seed, i, 10), h * J.r(seed, i, 11) * 0.55, U * (0.0015 + 0.0028 * J.r(seed, i, 13)), 0, TAU); x.fill(); }
      const mx = w * (portrait ? 0.7 : 0.78), my = h * 0.2; glowDisc(x, mx, my, U * 0.4, dark ? '#FFF1B8' : sc.accent2, dark ? 0.3 : 0.25); x.fillStyle = dark ? '#FFF6D0' : mix('#FFF3B0', sc.accent2, 0.3); x.beginPath(); x.arc(mx, my, U * 0.07, 0, TAU); x.fill();
      [[0.55, 0.3, 0.5], [0.66, 0.42, 0.35], [0.78, 0.6, 0.25]].forEach(([base, hh, sh], l) => {
        const col = mix(sc.bg, dark ? '#0F0A2C' : sc.fg, dark ? 0.3 + l * 0.26 : 0.12 + l * 0.14);
        for (let i = 0; i < 9 + l * 2; i++) tree(x, w * (i + J.r(seed, l * 30 + i, 5) * 0.6) / (9 + l * 2), h * base + U * 0.06, U * (0.3 + l * 0.12) * (0.8 + 0.4 * J.r(seed, l * 30 + i, 6)), U * (0.24 + l * 0.07), col, l, i);
        const my0 = h * (base + 0.06), mg = x.createLinearGradient(0, my0 - U * 0.22, 0, my0 + U * 0.08); mg.addColorStop(0, rgba(mix(sc.bg, dark ? '#6A5AC0' : '#FFFFFF', 0.5), 0)); mg.addColorStop(0.7, rgba(mix(sc.bg, dark ? '#6A5AC0' : '#FFFFFF', 0.5), dark ? 0.22 : 0.3)); mg.addColorStop(1, rgba(mix(sc.bg, dark ? '#6A5AC0' : '#FFFFFF', 0.5), 0)); x.fillStyle = mg; x.fillRect(0, my0 - U * 0.22, w, U * 0.3);
      });
      x.fillStyle = mix(sc.bg, dark ? '#0A0620' : sc.fg, dark ? 0.75 : 0.28); x.beginPath(); x.moveTo(0, gy); for (let i = 0; i <= 10; i++) x.lineTo(w * i / 10, gy - Math.sin(i * 1.1 + seed) * U * 0.02); x.lineTo(w, h); x.lineTo(0, h); x.fill();
    });
    ctx.drawImage(back, 0, 0, W, H);
    /* aurora ribbons */
    ctx.globalCompositeOperation = blend;
    [[pal[0], 0.13, 0.16], [pal[2], 0.1, 0.26], [pal[3], 0.08, 0.1]].forEach(([c, a, lvl], b) => { const y = H * lvl, sw = Math.sin(t * 0.18 + b * 2 + seed) * H * 0.04, path = new Path2D(); path.moveTo(0, y + sw); for (let x2 = 0; x2 <= W; x2 += W / 10) path.lineTo(x2, y + sw + Math.sin(x2 / W * 5 + t * 0.22 + b) * H * 0.06); for (let x2 = W; x2 >= 0; x2 -= W / 10) path.lineTo(x2, y + sw + H * 0.24 + Math.sin(x2 / W * 4 + t * 0.2 + b) * H * 0.04); path.closePath(); const g = ctx.createLinearGradient(0, y - H * 0.06, 0, y + H * 0.3); g.addColorStop(0, rgba(c, 0)); g.addColorStop(0.35, rgba(c, a * (dark ? 1 : 0.9))); g.addColorStop(1, rgba(c, 0)); ctx.fillStyle = g; ctx.fill(path); });
    ctx.globalCompositeOperation = 'source-over';
    /* glowing mushrooms in the foreground, each breathing at its own pace */
    for (let i = 0; i < 9; i++) {
      const mx = W * (0.04 + 0.92 * ((i + J.r(seed, i, 51) * 0.6) / 9)), my = gy + U * (0.03 + 0.05 * J.r(seed, i, 52)), s = U * (0.024 + 0.03 * J.r(seed, i, 53)), c = pal[i % 4], br = 0.7 + 0.3 * Math.sin(t * (0.8 + J.r(seed, i, 54)) + i * 2);
      ctx.globalCompositeOperation = blend; glowDisc(ctx, mx, my - s * 0.8, s * 3.2, c, 0.4 * br); ctx.globalCompositeOperation = 'source-over';
      ctx.fillStyle = mix('#FFF6E8', sc.bg, 0.15); roundRect(ctx, mx - s * 0.2, my - s * 0.9, s * 0.4, s * 0.9, s * 0.15); ctx.fill();
      const g = ctx.createRadialGradient(mx - s * 0.3, my - s * 1.3, s * 0.1, mx, my - s * 0.9, s * 1.2); g.addColorStop(0, rgba(mix(c, '#FFFFFF', 0.55), 1)); g.addColorStop(1, rgba(c, 1)); ctx.fillStyle = g; ctx.beginPath(); ctx.ellipse(mx, my - s * 0.9, s * 1.1, s * 0.75, 0, Math.PI, TAU); ctx.fill(); ctx.fillRect(mx - s * 1.1, my - s * 0.92, s * 2.2, s * 0.09);
      ctx.fillStyle = rgba('#FFFFFF', 0.85); for (const [dx, dy, r] of [[-0.5, -1.15, 0.13], [0.3, -1.3, 0.16], [0.65, -1.05, 0.1]]) { ctx.beginPath(); ctx.arc(mx + dx * s, my + dy * s, r * s, 0, TAU); ctx.fill(); }
    }
    /* fireflies, and a few hanging star charms swaying */
    ctx.globalCompositeOperation = blend;
    for (let i = 0; i < 34; i++) { const b = Math.pow(0.5 + 0.5 * Math.sin(t * (0.6 + J.r(seed, i, 61)) + i * 2.3), 2), x = W * J.r(seed, i, 62) + Math.sin(t * 0.3 * (1 + J.r(seed, i, 63)) + i) * U * 0.06, y = H * (0.3 + 0.6 * J.r(seed, i, 64)) + Math.cos(t * 0.35 + i * 1.7) * U * 0.05; glowDisc(ctx, x, y, U * (0.012 + 0.02 * J.r(seed, i, 65)), pal[i % 4], 0.55 * b); ctx.fillStyle = rgba('#FFFFFF', 0.8 * b); ctx.beginPath(); ctx.arc(x, y, U * 0.0022, 0, TAU); ctx.fill(); }
    for (let i = 0; i < 5; i++) { const sx = W * (0.1 + 0.8 * J.r(seed, i, 71)), sway = Math.sin(t * 0.9 + i * 1.7) * U * 0.012, ln = U * (0.06 + 0.09 * J.r(seed, i, 72)); ctx.strokeStyle = rgba('#FFFFFF', 0.4); ctx.lineWidth = Math.max(1, U * 0.0016); ctx.beginPath(); ctx.moveTo(sx, 0); ctx.lineTo(sx + sway, ln); ctx.stroke(); glowDisc(ctx, sx + sway, ln, U * 0.04, pal[i % 4], 0.5); ctx.fillStyle = rgba('#FFFFFF', 0.95); sparkle(ctx, sx + sway, ln, U * 0.018); ctx.fill(); }
    ctx.globalCompositeOperation = 'source-over';
  });
})();
