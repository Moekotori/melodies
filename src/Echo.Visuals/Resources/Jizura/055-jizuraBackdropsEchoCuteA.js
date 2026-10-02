/* ECHO first-party backdrops, more cute scenes (1/2): 図書室のひだまり and 雨あがりの昇降口 (放課後), ほうきの夜間飛行 and お菓子のお城 (魔法).
   Static art is painted once into cached layers; curtains, dust, drips, the witch, flags and sparkles are redrawn each frame.
   Every character here (the sleepy cat, the frog, the witch, her kitten) is an original round little figure built from circles and curves. */
(() => {
  'use strict';
  const K = J.echo, TAU = K.TAU, clamp = K.clamp, mix = K.mix, rgba = K.rgba, r = J.r;
  const { U: Umin, isDark, wrap, fade, vertical, glowDisc, star, layer, shapes } = K;
  const { heart, petal, puff, sparkle, cat, FUR, tonefor } = shapes;
  const colourKey = sc => [sc.bg, sc.fg, sc.accent, sc.accent2, sc.ghostA, sc.ghostB].join('');
  const SPINES = sc => [sc.accent, sc.accent2, '#FFD86B', '#9AD0F5', '#B8E2B0', '#F5A9C0', '#C9B6F0', '#FF9E7A'];
  const rrect = (x, a, b, w, h, rad) => { x.beginPath(); x.roundRect(a, b, w, h, rad); };
  const disc = (x, cx, cy, rad, c) => { x.fillStyle = c; x.beginPath(); x.arc(cx, cy, rad, 0, TAU); x.fill(); };

  /* ================= 図書室のひだまり ================= */
  const shelf = (x, sx, sy, sw, sh, rows, seed, id, tone, sc, U) => {
    const wood = tone(mix('#C99A6B', sc.bg, 0.08)), dark = mix(wood, '#000000', 0.3), pal = SPINES(sc), rh = sh / rows;
    x.fillStyle = mix(wood, '#000000', 0.42); x.fillRect(sx, sy, sw, sh);
    for (let row = 0; row < rows; row++) {
      const top = sy + row * rh, base = top + rh - U * 0.012;
      let px = sx + U * 0.012, i = 0;
      while (px < sx + sw - U * 0.03) {
        const bw = U * (0.013 + 0.016 * r(seed, id, row, i, 1)), bh = rh * (0.5 + 0.38 * r(seed, id, row, i, 2)), c = tone(pal[Math.floor(r(seed, id, row, i, 3) * pal.length)]);
        if (r(seed, id, row, i, 4) > 0.9) { px += bw * 1.6; i++; continue; }                     /* a gap */
        if (r(seed, id, row, i, 5) > 0.93) {                                                     /* a book leaning on its neighbour */
          x.save(); x.translate(px + bw, base); x.rotate(-0.28); x.fillStyle = c; x.fillRect(-bw, -bh, bw, bh); x.restore(); px += bw * 1.5; i++; continue;
        }
        x.fillStyle = c; rrect(x, px, base - bh, bw, bh, bw * 0.15); x.fill();
        x.fillStyle = rgba('#FFFFFF', 0.4); x.fillRect(px + bw * 0.15, base - bh + bh * 0.16, bw * 0.7, bh * 0.05);
        x.fillStyle = rgba('#000000', 0.16); x.fillRect(px + bw * 0.82, base - bh, bw * 0.18, bh);
        if (r(seed, id, row, i, 6) > 0.6) { x.fillStyle = rgba('#FFFFFF', 0.55); x.fillRect(px + bw * 0.2, base - bh * 0.5, bw * 0.6, bh * 0.1); }
        px += bw + U * 0.0015; i++;
      }
      if (row === 1 || row === 3) {                                                             /* a trinket sits on the shelf */
        const tx = sx + sw * (0.35 + 0.35 * r(seed, id, row, 91)), ty = base;
        if ((id + row) % 3 === 0) { disc(x, tx, ty - U * 0.03, U * 0.03, tone('#9AD0F5')); x.fillStyle = tone('#B8E2B0'); x.beginPath(); x.ellipse(tx - U * 0.008, ty - U * 0.036, U * 0.014, U * 0.009, 0.6, 0, TAU); x.fill(); x.fillStyle = dark; x.fillRect(tx - U * 0.004, ty - U * 0.005, U * 0.008, U * 0.006); }
        else if ((id + row) % 3 === 1) { x.fillStyle = tone('#FFF6EA'); x.beginPath(); x.ellipse(tx, ty - U * 0.024, U * 0.026, U * 0.028, 0, 0, TAU); x.fill(); x.beginPath(); x.moveTo(tx - U * 0.024, ty - U * 0.04); x.lineTo(tx - U * 0.02, ty - U * 0.066); x.lineTo(tx - U * 0.006, ty - U * 0.046); x.moveTo(tx + U * 0.024, ty - U * 0.04); x.lineTo(tx + U * 0.02, ty - U * 0.066); x.lineTo(tx + U * 0.006, ty - U * 0.046); x.fill(); disc(x, tx - U * 0.009, ty - U * 0.028, U * 0.003, '#5A3A3A'); disc(x, tx + U * 0.009, ty - U * 0.028, U * 0.003, '#5A3A3A'); }
        else { x.fillStyle = tone('#E08A6A'); x.fillRect(tx - U * 0.017, ty - U * 0.024, U * 0.034, U * 0.024); x.fillStyle = tone('#7AC27A'); for (const d of [-1, 0, 1]) { x.beginPath(); x.ellipse(tx + d * U * 0.012, ty - U * 0.04, U * 0.007, U * 0.02, d * 0.4, 0, TAU); x.fill(); } }
      }
      x.fillStyle = wood; x.fillRect(sx, base + U * 0.006, sw, U * 0.012 + 1); x.fillStyle = rgba('#FFFFFF', 0.2); x.fillRect(sx, base + U * 0.006, sw, U * 0.0025);
    }
    x.fillStyle = dark; x.fillRect(sx - U * 0.008, sy, U * 0.012, sh); x.fillRect(sx + sw - U * 0.004, sy, U * 0.012, sh);
  };

  K.registerBg('echoLibrary', '図書室のひだまり', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), dark = isDark(sc.bg), portrait = H > W, tone = tonefor(sc, dark);
    const key = `lib|${seed}|${colourKey(sc)}`, floorY = H * (portrait ? 0.76 : 0.74);
    const sideW = W * (portrait ? 0.17 : 0.25), wx0 = sideW + U * 0.05, wx1 = W - sideW - U * 0.05, ww = wx1 - wx0, wcx = (wx0 + wx1) / 2, wy0 = H * (portrait ? 0.15 : 0.1), wy1 = floorY - U * 0.1, arch = Math.min(ww * 0.5, U * 0.34);
    const winPath = x => { x.beginPath(); x.moveTo(wx0, wy1); x.lineTo(wx0, wy0 + arch); x.bezierCurveTo(wx0, wy0 + arch * 0.45, wcx - arch * 0.5, wy0, wcx, wy0); x.bezierCurveTo(wcx + arch * 0.5, wy0, wx1, wy0 + arch * 0.45, wx1, wy0 + arch); x.lineTo(wx1, wy1); x.closePath(); };
    const moonX = wcx + ww * 0.18, moonY = wy0 + arch * 1.05;
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|room`, 0.6, (x, w, h) => {
      x.fillStyle = vertical(x, 0, floorY, [[0, tone(mix(sc.bg, '#FFF6E2', dark ? 0.05 : 0.55))], [1, tone(mix(sc.bg, '#FFE3C2', dark ? 0.05 : 0.45))]]); x.fillRect(0, 0, w, floorY + 2);
      x.save(); winPath(x); x.clip();
      x.fillStyle = dark ? vertical(x, wy0, wy1, [[0, '#141B46'], [1, mix('#3B3F86', sc.accent, 0.25)]]) : vertical(x, wy0, wy1, [[0, mix('#8CCBFF', sc.bg, 0.12)], [1, '#EAF7FF']]); x.fillRect(wx0, wy0, ww, wy1 - wy0);
      if (dark) { for (let i = 0; i < 60; i++) { x.fillStyle = rgba('#FFFFFF', 0.35 + 0.6 * r(seed, i, 3)); x.beginPath(); x.arc(wx0 + r(seed, i, 1) * ww, wy0 + r(seed, i, 2) * (wy1 - wy0) * 0.75, U * (0.0012 + 0.002 * r(seed, i, 4)), 0, TAU); x.fill(); }
        glowDisc(x, moonX, moonY, U * 0.26, '#DCE6FF', 0.4); disc(x, moonX, moonY, U * 0.06, '#FFF9E2'); disc(x, moonX + U * 0.02, moonY - U * 0.008, U * 0.052, rgba('#141B46', 0.0)); }
      else { glowDisc(x, wx0 + ww * 0.22, wy0 + arch * 0.9, U * 0.5, '#FFF3C8', 0.7); disc(x, wx0 + ww * 0.22, wy0 + arch * 0.9, U * 0.06, '#FFF7D0'); }
      x.fillStyle = tone(mix('#8FD0A0', sc.bg, dark ? 0.55 : 0.1)); x.beginPath(); x.moveTo(wx0, wy1); for (let i = 0; i <= 10; i++) x.lineTo(wx0 + ww * i / 10, wy1 - U * (0.06 + 0.04 * Math.sin(i * 1.3 + seed))); x.lineTo(wx1, wy1); x.fill();
      for (let i = 0; i < 5; i++) { const tx = wx0 + ww * (0.08 + 0.2 * i + 0.05 * r(seed, i, 7)), ty = wy1 - U * (0.07 + 0.03 * r(seed, i, 8)); disc(x, tx, ty - U * 0.04, U * (0.035 + 0.02 * r(seed, i, 9)), tone(mix('#FFD3E1', sc.accent, 0.3))); }
      x.restore();
    }), 0, 0, W, H);
    /* clouds slide across the glass, and a paper plane now and then */
    ctx.save(); winPath(ctx); ctx.clip();
    for (let i = 0; i < 3; i++) { const cw = U * (0.24 + 0.1 * i), cx = wrap(t * U * (0.006 + 0.003 * i) + r(seed, i, 1) * ww, ww + cw * 2) - cw + wx0, cy = wy0 + arch * (0.6 + 0.4 * i) + U * 0.02 * i; K.puffCloud(ctx, cx, cy, cw, cw * 0.4, dark ? '#B7BEEA' : '#FFFFFF', dark ? 0.45 : 0.92, seed + i, 1, dark); }
    const pp = wrap(t / 13, 1); if (pp < 0.6) { const u = pp / 0.6, px = wx0 - U * 0.05 + (ww + U * 0.1) * u, py = wy0 + arch * 0.9 + Math.sin(u * 7) * U * 0.02 - u * U * 0.05; ctx.save(); ctx.translate(px, py); ctx.rotate(-0.15 + Math.sin(u * 7) * 0.12); ctx.fillStyle = '#FFFFFF'; ctx.beginPath(); ctx.moveTo(U * 0.03, 0); ctx.lineTo(-U * 0.026, -U * 0.014); ctx.lineTo(-U * 0.012, 0); ctx.lineTo(-U * 0.026, U * 0.014); ctx.fill(); ctx.fillStyle = rgba('#8CB4E6', 0.6); ctx.beginPath(); ctx.moveTo(U * 0.03, 0); ctx.lineTo(-U * 0.012, 0); ctx.lineTo(-U * 0.026, U * 0.014); ctx.fill(); ctx.restore(); }
    ctx.restore();
    ctx.drawImage(layer(env, `${key}|fore`, 0.6, (x, w, h) => {
      /* window frame */
      const wood = tone(mix('#E8C9A0', sc.bg, 0.1)); x.save(); winPath(x); x.lineWidth = U * 0.026; x.strokeStyle = wood; x.stroke(); x.lineWidth = U * 0.006; x.strokeStyle = rgba('#FFFFFF', 0.4); x.stroke();
      x.clip(); x.fillStyle = wood; x.fillRect(wcx - U * 0.006, wy0, U * 0.012, wy1 - wy0); x.fillRect(wx0, wy0 + arch + (wy1 - wy0 - arch) * 0.45, ww, U * 0.012); x.restore();
      x.fillStyle = tone(mix('#D9B48A', sc.bg, 0.1)); rrect(x, wx0 - U * 0.03, wy1 - U * 0.005, ww + U * 0.06, U * 0.03, U * 0.008); x.fill();
      /* the shelves on both sides */
      shelf(x, 0, H * 0.03, sideW, floorY - H * 0.03, 5, seed, 1, tone, sc, U); shelf(x, W - sideW, H * 0.03, sideW, floorY - H * 0.03, 5, seed, 2, tone, sc, U);
      /* skirting and floor */
      x.fillStyle = tone(mix('#B98A5E', sc.bg, 0.15)); x.fillRect(0, floorY - U * 0.02, w, U * 0.026);
      x.fillStyle = vertical(x, floorY, h, [[0, tone(mix('#D8A878', sc.bg, 0.1))], [1, tone(mix('#B98558', sc.bg, 0.16))]]); x.fillRect(0, floorY + U * 0.006, w, h - floorY);
      x.strokeStyle = rgba('#7A4A2E', 0.22); x.lineWidth = Math.max(1, U * 0.002); for (let i = 1; i < 9; i++) { const yy = floorY + (h - floorY) * Math.pow(i / 9, 1.5); x.beginPath(); x.moveTo(0, yy); x.lineTo(w, yy); x.stroke(); }
      for (let i = -8; i <= 8; i++) { x.beginPath(); x.moveTo(w * 0.5 + i * U * 0.05, floorY); x.lineTo(w * 0.5 + i * U * 0.16, h); x.stroke(); }
      /* the reading desk, with a stack of books, an open book and a lamp */
      const dx = W * (portrait ? 0.5 : 0.52), dw = W * (portrait ? 0.86 : 0.5), dy = H * 0.845;
      x.fillStyle = rgba('#000000', 0.12); x.beginPath(); x.ellipse(dx, dy + U * 0.11, dw * 0.52, U * 0.03, 0, 0, TAU); x.fill();
      x.fillStyle = tone('#A8744A'); x.fillRect(dx - dw * 0.44, dy + U * 0.02, U * 0.03, U * 0.1); x.fillRect(dx + dw * 0.44 - U * 0.03, dy + U * 0.02, U * 0.03, U * 0.1);
      x.fillStyle = tone(mix('#E0B080', sc.bg, 0.1)); rrect(x, dx - dw / 2, dy - U * 0.012, dw, U * 0.05, U * 0.014); x.fill(); x.fillStyle = rgba('#FFFFFF', 0.3); x.fillRect(dx - dw / 2 + U * 0.01, dy - U * 0.008, dw - U * 0.02, U * 0.006);
      const pal = SPINES(sc); [0, 1, 2, 3].forEach(i => { const bw = U * (0.13 - i * 0.008), bh = U * 0.026; x.fillStyle = tone(pal[(i * 2 + 1) % pal.length]); rrect(x, dx - dw * 0.4 + (i % 2) * U * 0.01, dy - U * 0.012 - bh * (i + 1), bw, bh, U * 0.006); x.fill(); x.fillStyle = tone('#FFF6EA'); x.fillRect(dx - dw * 0.4 + (i % 2) * U * 0.01 + U * 0.004, dy - U * 0.012 - bh * (i + 1) + bh * 0.3, bw - U * 0.008, bh * 0.36); });
      const bx = dx - dw * 0.08, by = dy - U * 0.013; x.fillStyle = tone('#FFFBF0'); x.beginPath(); x.moveTo(bx - U * 0.09, by); x.quadraticCurveTo(bx - U * 0.045, by - U * 0.02, bx, by - U * 0.008); x.quadraticCurveTo(bx + U * 0.045, by - U * 0.02, bx + U * 0.09, by); x.lineTo(bx + U * 0.09, by - U * 0.006); x.quadraticCurveTo(bx + U * 0.045, by - U * 0.03, bx, by - U * 0.016); x.quadraticCurveTo(bx - U * 0.045, by - U * 0.03, bx - U * 0.09, by - U * 0.006); x.fill();
      x.strokeStyle = rgba(sc.accent, 0.6); x.lineWidth = Math.max(1, U * 0.0016); for (let i = 0; i < 3; i++) for (const d of [-1, 1]) { x.beginPath(); x.moveTo(bx + d * U * 0.014, by - U * (0.011 + i * 0.004)); x.lineTo(bx + d * U * 0.075, by - U * (0.006 + i * 0.004)); x.stroke(); }
      const lx = dx + dw * 0.36; x.strokeStyle = tone('#7A5A44'); x.lineWidth = U * 0.008; x.lineCap = 'round'; x.beginPath(); x.moveTo(lx, dy - U * 0.01); x.lineTo(lx, dy - U * 0.1); x.quadraticCurveTo(lx, dy - U * 0.16, lx - U * 0.04, dy - U * 0.15); x.stroke();
      x.fillStyle = tone(mix('#FFD86B', sc.accent2, 0.3)); x.beginPath(); x.moveTo(lx - U * 0.085, dy - U * 0.125); x.quadraticCurveTo(lx - U * 0.055, dy - U * 0.2, lx - U * 0.02, dy - U * 0.125); x.closePath(); x.fill(); x.fillStyle = tone('#7A5A44'); x.beginPath(); x.ellipse(lx, dy - U * 0.008, U * 0.03, U * 0.008, 0, 0, TAU); x.fill();
    }), 0, 0, W, H);
    /* sunbeams (moonbeams at night) leaning down across the room, breathing slowly */
    ctx.save(); ctx.globalCompositeOperation = 'screen';
    for (let k = 0; k < 3; k++) {
      const bx0 = wx0 + ww * (0.12 + 0.3 * k), bw = ww * 0.15, dx = U * (dark ? -0.42 : 0.5), yTop = wy0 + arch * 0.9, yEnd = H * 0.97, a = (dark ? 0.16 : 0.34) * (0.75 + 0.25 * Math.sin(t * 0.5 + k * 2));
      const g = ctx.createLinearGradient(bx0, yTop, bx0 + dx, yEnd); g.addColorStop(0, rgba(dark ? '#BFD2FF' : '#FFF3C8', a)); g.addColorStop(1, rgba(dark ? '#BFD2FF' : '#FFF3C8', a * 0.15));
      ctx.fillStyle = g; ctx.beginPath(); ctx.moveTo(bx0, yTop); ctx.lineTo(bx0 + bw, yTop); ctx.lineTo(bx0 + bw + dx, yEnd); ctx.lineTo(bx0 + dx, yEnd); ctx.fill();
    }
    for (let i = 0; i < 34; i++) {                                                                 /* dust in the light */
      const age = wrap(t * (0.02 + 0.02 * r(seed, i, 61)) + r(seed, i, 62), 1), px = wx0 + ww * r(seed, i, 63) + (age - 0.5) * U * 0.3 + Math.sin(t * 0.6 + i) * U * 0.02, py = H * (0.85 - age * 0.7), tw = 0.4 + 0.6 * Math.pow(0.5 + 0.5 * Math.sin(t * 1.6 + i * 2.1), 2);
      ctx.fillStyle = rgba(dark ? '#DDE6FF' : '#FFF6D8', 0.75 * tw); ctx.beginPath(); ctx.arc(px, py, U * (0.002 + 0.003 * r(seed, i, 64)), 0, TAU); ctx.fill();
    }
    ctx.restore();
    /* the curtains, moving in a draught, and a garland of paper hearts across the top */
    const rodY = wy0 + arch * 0.55;
    ctx.strokeStyle = tone('#8A6A50'); ctx.lineWidth = U * 0.008; ctx.lineCap = 'round'; ctx.beginPath(); ctx.moveTo(wx0 - U * 0.05, rodY); ctx.lineTo(wx1 + U * 0.05, rodY); ctx.stroke();
    [-1, 1].forEach(side => {
      const x0 = side < 0 ? wx0 - U * 0.03 : wx1 + U * 0.03, cw = ww * 0.2, hh = (wy1 - rodY) * 0.96, folds = 5;
      const cc = tone(mix(sc.accent, dark ? '#3A2A5A' : '#FFFFFF', dark ? 0.35 : 0.45));
      ctx.fillStyle = cc; ctx.beginPath(); ctx.moveTo(x0, rodY); ctx.lineTo(x0 - side * cw, rodY);
      for (let i = 0; i <= 12; i++) { const u = i / 12, y = rodY + hh * u, sw = Math.sin(t * 0.9 + u * 3 + (side > 0 ? 1.4 : 0)) * U * 0.012 * u; ctx.lineTo(x0 - side * (cw * (0.75 - 0.5 * Math.sin(u * Math.PI) * 0.4) + sw * side * -1), y); }
      ctx.lineTo(x0, rodY + hh); ctx.closePath(); ctx.fill();
      ctx.strokeStyle = rgba('#FFFFFF', 0.28); ctx.lineWidth = Math.max(1, U * 0.003); for (let f = 1; f < folds; f++) { const fx = x0 - side * cw * f / folds; ctx.beginPath(); ctx.moveTo(fx, rodY + U * 0.01); ctx.quadraticCurveTo(fx + side * U * 0.012, rodY + hh * 0.5, fx - side * U * 0.006, rodY + hh * 0.96); ctx.stroke(); }
      ctx.fillStyle = rgba('#000000', 0.06); ctx.fillRect(Math.min(x0, x0 - side * cw), rodY + hh * 0.5, cw, U * 0.006);
      ctx.fillStyle = tone(mix(sc.accent2, '#FFFFFF', 0.3)); ctx.beginPath(); ctx.ellipse(x0 - side * cw * 0.4, rodY + hh * 0.56, cw * 0.45, U * 0.012, 0, 0, TAU); ctx.fill(); heart(ctx, x0 - side * cw * 0.4, rodY + hh * 0.56, U * 0.014); ctx.fillStyle = tone('#FFFFFF'); ctx.fill();
    });
    ctx.strokeStyle = rgba(mix(sc.fg, sc.bg, 0.3), 0.5); ctx.lineWidth = Math.max(1, U * 0.002); ctx.beginPath(); ctx.moveTo(sideW, H * 0.06); ctx.quadraticCurveTo(W * 0.5, H * 0.16, W - sideW, H * 0.06); ctx.stroke();
    const flags = Math.max(5, Math.round((W - sideW * 2) / (U * 0.09)));
    for (let i = 0; i < flags; i++) { const u = (i + 0.5) / flags, xx = sideW + (W - sideW * 2) * u, yy = (1 - u) * (1 - u) * H * 0.06 + 2 * u * (1 - u) * H * 0.16 + u * u * H * 0.06; ctx.save(); ctx.translate(xx, yy); ctx.rotate(Math.sin(t * 1.3 + i) * 0.08); ctx.fillStyle = tone(SPINES(sc)[i % 8]); if (i % 2) { star(ctx, 0, U * 0.028, U * 0.026, 5, 0.5); ctx.fill(); } else { heart(ctx, 0, U * 0.026, U * 0.024); ctx.fill(); } ctx.restore(); }
    /* a sleeping cat curled beside the open book */
    const cx = W * (portrait ? 0.62 : 0.6), cy = H * 0.845 - U * 0.012;
    cat(ctx, cx, cy, U * 0.2, { mode: 'loaf', asleep: true, coat: FUR[1], marks: '#E5A870', t, phase: 0.8, flip: true });
    /* tea steam */
    ctx.strokeStyle = rgba('#FFFFFF', 0.35); ctx.lineWidth = Math.max(1.4, U * 0.004); ctx.lineCap = 'round';
    const mx = W * (portrait ? 0.3 : 0.4), my = H * 0.845 - U * 0.03; ctx.fillStyle = tone('#FFF6EA'); rrect(ctx, mx - U * 0.024, my - U * 0.004, U * 0.048, U * 0.036, U * 0.012); ctx.fill(); ctx.strokeStyle = tone('#FFF6EA'); ctx.lineWidth = U * 0.006; ctx.beginPath(); ctx.arc(mx + U * 0.028, my + U * 0.014, U * 0.013, -1.4, 1.4); ctx.stroke(); disc(ctx, mx, my + U * 0.001, U * 0.02, rgba('#8A5A3A', 0.0));
    ctx.strokeStyle = rgba('#FFFFFF', 0.4); ctx.lineWidth = Math.max(1.4, U * 0.004);
    for (let k = 0; k < 2; k++) { ctx.beginPath(); for (let i = 0; i <= 10; i++) { const u = i / 10, yy = my - U * 0.01 - u * U * 0.09; const xx = mx + (k - 0.5) * U * 0.02 + Math.sin(t * 1.2 + u * 5 + k * 2) * U * 0.008 * u; if (i) ctx.lineTo(xx, yy); else ctx.moveTo(xx, yy); } ctx.globalAlpha = e * 0.6; ctx.stroke(); ctx.globalAlpha = e; }
    /* lamp glow */
    const lgx = W * (portrait ? 0.5 : 0.52) + W * (portrait ? 0.86 : 0.5) * 0.36 - U * 0.055, lgy = H * 0.845 - U * 0.12;
    ctx.globalCompositeOperation = 'screen'; glowDisc(ctx, lgx, lgy, U * (dark ? 0.7 : 0.3), '#FFD08A', dark ? 0.85 : 0.32); glowDisc(ctx, lgx, lgy + U * 0.04, U * (dark ? 0.26 : 0.12), '#FFE9B8', dark ? 0.7 : 0.25); ctx.globalCompositeOperation = 'source-over';
    /* a drifting heart-shaped petal or two, and a butterfly */
    const bt = wrap(t * 0.045, 1), bxx = wx0 + ww * (0.1 + 0.8 * bt), byy = wy1 - U * 0.14 - Math.sin(bt * 9) * U * 0.05 - bt * U * 0.1, flap = Math.abs(Math.sin(t * 9));
    ctx.save(); ctx.translate(bxx, byy); ctx.fillStyle = tone(sc.accent2); for (const d of [-1, 1]) { ctx.beginPath(); ctx.ellipse(d * U * 0.01 * flap, -U * 0.004, U * 0.011 * (0.3 + 0.7 * flap), U * 0.014, d * 0.4, 0, TAU); ctx.fill(); } ctx.fillStyle = '#4A2E3A'; ctx.fillRect(-U * 0.0012, -U * 0.008, U * 0.0024, U * 0.018); ctx.restore();
  });

  /* ================= 雨あがりの昇降口 ================= */
  const sneaker = (x, cx, cy, s, c) => { x.fillStyle = '#FFFFFF'; x.beginPath(); x.ellipse(cx, cy, s, s * 0.42, 0, 0, TAU); x.fill(); x.fillStyle = c; x.beginPath(); x.ellipse(cx - s * 0.2, cy - s * 0.12, s * 0.62, s * 0.3, 0, 0, TAU); x.fill(); x.fillStyle = rgba('#FFFFFF', 0.85); x.fillRect(cx - s * 0.5, cy - s * 0.12, s * 0.7, s * 0.06); x.fillStyle = rgba('#000000', 0.18); x.fillRect(cx - s, cy + s * 0.22, s * 2, s * 0.14); };
  /* a furled umbrella leaning in the stand: a slim spindle with its strap, a ferrule at the tip and a hooked handle */
  const umbrella = (x, cx, base, len, cw, c1, c2, lean) => {
    x.save(); x.translate(cx, base); x.rotate(lean);
    x.strokeStyle = '#6A5A6A'; x.lineWidth = Math.max(1.5, cw * 0.14); x.lineCap = 'round'; x.beginPath(); x.moveTo(0, 0); x.lineTo(0, -len * 0.3); x.arc(-cw * 0.28, 0, cw * 0.28, 0, Math.PI * 0.9); x.stroke();
    x.fillStyle = c1; x.beginPath(); x.moveTo(0, -len); x.bezierCurveTo(cw * 0.7, -len * 0.72, cw * 0.6, -len * 0.42, cw * 0.14, -len * 0.3); x.lineTo(-cw * 0.14, -len * 0.3); x.bezierCurveTo(-cw * 0.6, -len * 0.42, -cw * 0.7, -len * 0.72, 0, -len); x.fill();
    x.fillStyle = c2; x.beginPath(); x.moveTo(0, -len); x.bezierCurveTo(cw * 0.16, -len * 0.7, cw * 0.14, -len * 0.5, 0, -len * 0.3); x.bezierCurveTo(-cw * 0.14, -len * 0.5, -cw * 0.16, -len * 0.7, 0, -len); x.fill();
    x.fillStyle = rgba('#FFFFFF', 0.4); x.beginPath(); x.ellipse(-cw * 0.28, -len * 0.62, cw * 0.06, len * 0.12, 0.1, 0, TAU); x.fill();
    x.fillStyle = '#F0C860'; x.fillRect(-cw * 0.2, -len * 0.42, cw * 0.4, len * 0.05); x.fillStyle = '#6A5A6A'; x.fillRect(-cw * 0.02, -len - len * 0.04, cw * 0.04, len * 0.05);
    x.restore();
  };
  const ajisai = (x, cx, cy, s, seed, id, tone, sc) => {
    const cols = ['#9AB8F0', '#B5A2F0', '#F0A2CE', '#8CD0F0'];
    for (let k = 0; k < 5; k++) { const bx = cx + (k - 2) * s * 0.42, by = cy - Math.sin(k * 1.7 + id) * s * 0.12 - (k === 2 ? s * 0.25 : 0); x.fillStyle = tone(mix('#5FAE6A', sc.bg, 0.05)); x.beginPath(); x.ellipse(bx + s * 0.12, by + s * 0.34, s * 0.34, s * 0.16, 0.3 * (k - 2), 0, TAU); x.fill(); }
    for (let k = 0; k < 5; k++) { const bx = cx + (k - 2) * s * 0.42, by = cy - Math.sin(k * 1.7 + id) * s * 0.12 - (k === 2 ? s * 0.25 : 0), col = cols[(k + id) % 4]; for (let i = 0; i < 18; i++) { const a = r(seed, id, k, i, 1) * TAU, rr = Math.sqrt(r(seed, id, k, i, 2)) * s * 0.3; x.fillStyle = tone(mix(col, '#FFFFFF', 0.25 * r(seed, id, k, i, 3))); x.beginPath(); x.arc(bx + Math.cos(a) * rr, by + Math.sin(a) * rr * 0.9, s * 0.075, 0, TAU); x.fill(); } }
  };
  const frog = (ctx, cx, cy, s, t, dark, tone) => {
    const body = tone('#8FD46A'), belly = tone('#E8F5B8'), breathe = 1 + 0.03 * Math.sin(t * 2.2), blink = Math.sin(t * 0.8) > 0.96;
    ctx.save(); ctx.translate(cx, cy); ctx.scale(1, breathe); ctx.fillStyle = body;
    ctx.beginPath(); ctx.ellipse(0, -s * 0.34, s * 0.5, s * 0.34, 0, 0, TAU); ctx.fill(); ctx.fillStyle = belly; ctx.beginPath(); ctx.ellipse(0, -s * 0.26, s * 0.3, s * 0.2, 0, 0, TAU); ctx.fill();
    ctx.fillStyle = body; for (const d of [-1, 1]) { ctx.beginPath(); ctx.ellipse(d * s * 0.42, -s * 0.06, s * 0.2, s * 0.08, d * -0.2, 0, TAU); ctx.fill(); ctx.beginPath(); ctx.ellipse(d * s * 0.26, -s * 0.66, s * 0.17, s * 0.17, 0, 0, TAU); ctx.fill(); }
    for (const d of [-1, 1]) { ctx.fillStyle = '#FFFFFF'; ctx.beginPath(); ctx.arc(d * s * 0.26, -s * 0.68, s * 0.12, 0, TAU); ctx.fill(); if (!blink) { ctx.fillStyle = '#3A3A4A'; ctx.beginPath(); ctx.arc(d * s * 0.26, -s * 0.67, s * 0.065, 0, TAU); ctx.fill(); ctx.fillStyle = '#FFFFFF'; ctx.beginPath(); ctx.arc(d * s * 0.28, -s * 0.7, s * 0.02, 0, TAU); ctx.fill(); } else { ctx.strokeStyle = '#3A3A4A'; ctx.lineWidth = Math.max(1, s * 0.03); ctx.beginPath(); ctx.moveTo(d * s * 0.2, -s * 0.68); ctx.lineTo(d * s * 0.32, -s * 0.68); ctx.stroke(); } }
    ctx.strokeStyle = '#3A3A4A'; ctx.lineWidth = Math.max(1, s * 0.03); ctx.lineCap = 'round'; ctx.beginPath(); ctx.arc(0, -s * 0.46, s * 0.14, 0.25, Math.PI - 0.25); ctx.stroke();
    ctx.fillStyle = rgba('#FF8FA3', 0.6); for (const d of [-1, 1]) { ctx.beginPath(); ctx.ellipse(d * s * 0.36, -s * 0.44, s * 0.07, s * 0.045, 0, 0, TAU); ctx.fill(); }
    ctx.restore();
  };

  K.registerBg('echoRainyEntrance', '雨あがりの昇降口', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), dark = isDark(sc.bg), portrait = H > W, tone = tonefor(sc, dark);
    const key = `rain|${seed}|${colourKey(sc)}`, floorY = H * 0.74, ox0 = W * (portrait ? 0.08 : 0.22), ox1 = W * (portrait ? 0.92 : 0.78), oy0 = H * 0.07, gy = H * 0.63, oxc = (ox0 + ox1) / 2;
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|out`, 0.5, (x, w, h) => {
      x.fillStyle = dark ? vertical(x, 0, floorY, [[0, '#161B45'], [0.7, mix('#3A3D82', sc.accent, 0.25)], [1, mix('#6A5BA0', sc.accent2, 0.3)]]) : vertical(x, 0, floorY, [[0, mix('#7FC4FF', sc.bg, 0.2)], [0.6, mix('#BEE3FF', sc.bg, 0.3)], [1, '#FFF3E6']]); x.fillRect(0, 0, w, floorY);
      if (!dark) {                                                                                 /* a rainbow over the yard */
        const cols = ['#FF8A8A', '#FFB877', '#FFE58A', '#A6E6A0', '#9AD0F5', '#B5A2F0']; const rc = oxc + W * 0.06, rr = Math.min(ox1 - ox0, H) * 0.48; x.lineWidth = U * 0.018; cols.forEach((c, i) => { x.strokeStyle = rgba(c, 0.62); x.beginPath(); x.arc(rc, gy + U * 0.06, rr - i * U * 0.017, Math.PI * 1.03, Math.PI * 1.97); x.stroke(); });
      } else { for (let i = 0; i < 40; i++) { x.fillStyle = rgba('#FFFFFF', 0.3 + 0.5 * r(seed, i, 3)); x.beginPath(); x.arc(r(seed, i, 1) * w, r(seed, i, 2) * gy * 0.55, U * 0.002, 0, TAU); x.fill(); } }
      for (let i = 0; i < 4; i++) K.puffCloud(x, w * (0.12 + 0.25 * i + 0.05 * r(seed, i, 5)), gy * (0.32 + 0.16 * (i % 2)), U * (0.34 + 0.1 * r(seed, i, 6)), U * 0.12, dark ? '#8B90D0' : '#FFFFFF', dark ? 0.5 : 0.9, seed + i, 1, dark);
      x.fillStyle = tone(mix('#8FD0A0', sc.bg, dark ? 0.5 : 0.1)); x.beginPath(); x.moveTo(0, gy); for (let i = 0; i <= 14; i++) x.lineTo(w * i / 14, gy - U * (0.05 + 0.05 * Math.sin(i * 1.1 + seed))); x.lineTo(w, gy); x.fill();
      for (let i = 0; i < 9; i++) { const tx = w * (i + 0.5 * r(seed, i, 7)) / 9, ty = gy - U * 0.03; x.fillStyle = tone(mix('#6FBE80', sc.bg, dark ? 0.55 : 0.05)); x.beginPath(); x.arc(tx, ty - U * 0.05, U * (0.04 + 0.03 * r(seed, i, 8)), 0, TAU); x.fill(); x.fillStyle = tone('#8A6A50'); x.fillRect(tx - U * 0.004, ty - U * 0.02, U * 0.008, U * 0.05); }
      x.fillStyle = tone(mix('#C8B89A', sc.bg, dark ? 0.6 : 0.2)); x.fillRect(0, gy, w, floorY - gy);
      x.strokeStyle = rgba('#FFFFFF', 0.35); x.lineWidth = Math.max(1, U * 0.002); for (let i = 0; i < 8; i++) { const yy = gy + (floorY - gy) * i / 8; x.beginPath(); x.moveTo(0, yy); x.lineTo(w, yy); x.stroke(); }
      const puddles = [[0.34, 0.52, 0.22], [0.64, 0.7, 0.28], [0.5, 0.9, 0.34]];                  /* puddles mirroring the sky */
      puddles.forEach(([u, v, s]) => { const px = ox0 + (ox1 - ox0) * u, py = gy + (floorY - gy) * v, pw = (ox1 - ox0) * s * 0.55; const g = x.createLinearGradient(0, py - U * 0.04, 0, py + U * 0.04); g.addColorStop(0, dark ? mix('#3A3D82', sc.accent, 0.3) : '#CBE8FF'); g.addColorStop(1, dark ? '#20255A' : '#F6E9FF'); x.fillStyle = g; x.beginPath(); x.ellipse(px, py, pw, U * 0.03 + pw * 0.06, 0, 0, TAU); x.fill(); x.strokeStyle = rgba('#FFFFFF', 0.6); x.lineWidth = Math.max(1, U * 0.0022); x.beginPath(); x.ellipse(px, py, pw, U * 0.03 + pw * 0.06, 0, 0.2, 2.6); x.stroke(); });
      if (dark) { const lx = ox1 - (ox1 - ox0) * 0.16; x.fillStyle = tone('#5A4A6A'); x.fillRect(lx - U * 0.005, gy - U * 0.18, U * 0.01, U * 0.2); glowDisc(x, lx, gy - U * 0.19, U * 0.3, '#FFD9A0', 0.55); disc(x, lx, gy - U * 0.19, U * 0.02, '#FFF3D0'); }
      ajisai(x, ox0 + (ox1 - ox0) * 0.16, floorY - U * 0.03, U * 0.16, seed, 1, tone, sc); ajisai(x, ox1 - (ox1 - ox0) * 0.2, floorY - U * 0.02, U * 0.13, seed, 2, tone, sc);
    }), 0, 0, W, H);
    /* rain still dripping from the eave into the puddles, and a rainbow shimmer */
    ctx.save(); ctx.strokeStyle = rgba(dark ? '#CFD8FF' : '#FFFFFF', dark ? 0.35 : 0.75); ctx.lineWidth = Math.max(1.2, U * 0.0026); ctx.lineCap = 'round';
    for (let i = 0; i < 16; i++) {
      const cyc = 1.2 + 0.7 * r(seed, i, 21), ph = wrap(t / cyc + r(seed, i, 22), 1), px = ox0 + (ox1 - ox0) * (0.04 + 0.92 * r(seed, i, 23)), y0 = oy0 + U * 0.03, y1 = gy + (floorY - gy) * (0.25 + 0.7 * r(seed, i, 24));
      if (ph < 0.85) { const u = ph / 0.85, yy = y0 + (y1 - y0) * u * u; ctx.beginPath(); ctx.moveTo(px, yy - U * 0.018 * u); ctx.lineTo(px, yy); ctx.stroke(); }
      else { const u = (ph - 0.85) / 0.15; ctx.globalAlpha = e * (1 - u); ctx.beginPath(); ctx.ellipse(px, y1, U * 0.005 + U * 0.025 * u, U * 0.002 + U * 0.008 * u, 0, 0, TAU); ctx.stroke(); ctx.globalAlpha = e; }
    }
    if (dark) { ctx.strokeStyle = rgba('#CFD8FF', 0.16); for (let i = 0; i < 46; i++) { const px = ox0 + (ox1 - ox0) * r(seed, i, 31), sp = 0.9 + r(seed, i, 32), yy = wrap(r(seed, i, 33) * H + t * H * sp * 0.9, floorY - oy0) + oy0; ctx.beginPath(); ctx.moveTo(px, yy); ctx.lineTo(px - U * 0.008, yy + U * 0.045); ctx.stroke(); } }
    ctx.restore();
    ctx.drawImage(layer(env, `${key}|in`, 0.6, (x, w, h) => {
      x.fillStyle = tone(mix(sc.bg, '#FFF6E8', dark ? 0.08 : 0.6)); x.fillRect(0, 0, w, oy0); x.fillRect(0, 0, ox0, h); x.fillRect(ox1, 0, w - ox1, h);            /* walls around the doorway */
      x.fillStyle = tone(mix('#B98A5E', sc.bg, 0.2)); x.fillRect(ox0 - U * 0.02, oy0 - U * 0.02, ox1 - ox0 + U * 0.04, U * 0.03); x.fillRect(ox0 - U * 0.02, oy0 - U * 0.02, U * 0.03, floorY - oy0 + U * 0.03); x.fillRect(ox1 - U * 0.01, oy0 - U * 0.02, U * 0.03, floorY - oy0 + U * 0.03);
      x.fillStyle = tone(mix('#8A5A3A', sc.bg, 0.2)); x.beginPath(); x.moveTo(ox0 - U * 0.05, oy0 + U * 0.01); x.lineTo(ox1 + U * 0.05, oy0 + U * 0.01); x.lineTo(ox1 + U * 0.02, oy0 + U * 0.07); x.lineTo(ox0 - U * 0.02, oy0 + U * 0.07); x.fill(); x.fillStyle = rgba('#FFFFFF', 0.2); x.fillRect(ox0 - U * 0.05, oy0 + U * 0.01, ox1 - ox0 + U * 0.1, U * 0.006);
      x.fillStyle = vertical(x, floorY, h, [[0, tone(mix('#E9D8C0', sc.bg, dark ? 0.4 : 0.2))], [1, tone(mix('#C9B698', sc.bg, dark ? 0.4 : 0.3))]]); x.fillRect(0, floorY, w, h - floorY);
      const tile = U * 0.1; for (let i = 0; i < 14; i++) for (let j = 0; j < 8; j++) if ((i + j) % 2 === 0) { x.fillStyle = rgba('#FFFFFF', 0.16); x.fillRect(i * tile * 1.2 - tile, floorY + j * (h - floorY) / 8, tile * 1.2, (h - floorY) / 8); }
      x.fillStyle = tone(mix('#8A5A3A', sc.bg, 0.25)); x.fillRect(ox0 - U * 0.04, floorY - U * 0.008, ox1 - ox0 + U * 0.08, U * 0.016);
      /* shoe lockers on either side */
      [[0, ox0 - U * 0.02, 1], [ox1 + U * 0.02, W, 2]].forEach(([lx0, lx1, id]) => {
        const lw = lx1 - lx0, cols = Math.max(2, Math.round(lw / (U * 0.11))), rows = 5, cw = lw / cols, ch = (floorY - H * 0.14) / rows;
        x.fillStyle = tone(mix('#C9A57A', sc.bg, 0.2)); x.fillRect(lx0, H * 0.14, lw, floorY - H * 0.14);
        for (let c = 0; c < cols; c++) for (let rr = 0; rr < rows; rr++) {
          const cx0 = lx0 + c * cw + U * 0.006, cy0 = H * 0.14 + rr * ch + U * 0.006; x.fillStyle = tone(mix('#5A3E32', sc.bg, 0.15)); x.fillRect(cx0, cy0, cw - U * 0.012, ch - U * 0.012); x.fillStyle = rgba('#000000', 0.22); x.fillRect(cx0, cy0, cw - U * 0.012, U * 0.008);
          const pr = r(seed, id, c, rr, 1); if (pr > 0.35) sneaker(x, cx0 + (cw - U * 0.012) * 0.5, cy0 + ch - U * 0.03, Math.min(cw * 0.32, U * 0.038), tone(SPINES(sc)[Math.floor(r(seed, id, c, rr, 2) * 8)]));
          x.fillStyle = rgba('#FFFFFF', 0.7); x.fillRect(cx0 + U * 0.006, cy0 + U * 0.012, cw * 0.28, U * 0.01);
        }
      });
      /* umbrella stand with umbrellas */
      const ux = ox1 - (ox1 - ox0) * 0.07, uy = floorY + U * 0.05;
      [[-0.05, '#FF8FA3', '#FFD3DC', -0.14, 0.3], [0.0, '#7AB8F0', '#C4E0FA', 0.03, 0.34], [0.05, '#FFD86B', '#FFF0B0', 0.17, 0.28]].forEach(([dx, c1, c2, lean, len]) => umbrella(x, ux + dx * U * 1.6, uy - U * 0.13, U * len, U * 0.05, tone(c1), tone(c2), lean));
      x.fillStyle = tone('#8FA0B8'); x.beginPath(); x.moveTo(ux - U * 0.1, uy - U * 0.15); x.lineTo(ux + U * 0.1, uy - U * 0.15); x.lineTo(ux + U * 0.085, uy + U * 0.03); x.lineTo(ux - U * 0.085, uy + U * 0.03); x.closePath(); x.fill();
      x.fillStyle = rgba('#000000', 0.16); x.beginPath(); x.ellipse(ux, uy - U * 0.15, U * 0.1, U * 0.02, 0, 0, TAU); x.fill(); x.fillStyle = tone('#A9B8CE'); x.fillRect(ux - U * 0.1, uy - U * 0.16, U * 0.2, U * 0.012);
      x.fillStyle = rgba('#FFFFFF', 0.28); x.fillRect(ux - U * 0.075, uy - U * 0.14, U * 0.014, U * 0.16); heart(x, ux, uy - U * 0.06, U * 0.022); x.fillStyle = tone(sc.accent); x.fill();
    }), 0, 0, W, H);
    /* a frog on the umbrella stand, a teru-teru-bozu swinging under the eave */
    const ux = ox1 - (ox1 - ox0) * 0.07, uy = floorY + U * 0.05;
    frog(ctx, ux + U * 0.01, uy - U * 0.15, U * 0.12, t, dark, tone);
    const tx = ox0 + (ox1 - ox0) * 0.3, ang = Math.sin(t * 1.3) * 0.16; ctx.save(); ctx.translate(tx, oy0 + U * 0.07); ctx.rotate(ang); ctx.strokeStyle = tone('#8A6A50'); ctx.lineWidth = Math.max(1, U * 0.002); ctx.beginPath(); ctx.moveTo(0, 0); ctx.lineTo(0, U * 0.07); ctx.stroke();
    ctx.fillStyle = tone('#FFFFFF'); ctx.beginPath(); ctx.arc(0, U * 0.09, U * 0.028, 0, TAU); ctx.fill(); ctx.beginPath(); ctx.moveTo(-U * 0.03, U * 0.105); ctx.quadraticCurveTo(-U * 0.045, U * 0.17, -U * 0.026, U * 0.16); ctx.quadraticCurveTo(-U * 0.012, U * 0.18, 0, U * 0.16); ctx.quadraticCurveTo(U * 0.012, U * 0.18, U * 0.026, U * 0.16); ctx.quadraticCurveTo(U * 0.045, U * 0.17, U * 0.03, U * 0.105); ctx.fill();
    ctx.fillStyle = tone(sc.accent); ctx.fillRect(-U * 0.022, U * 0.112, U * 0.044, U * 0.008); disc(ctx, -U * 0.01, U * 0.088, U * 0.003, '#5A3A3A'); disc(ctx, U * 0.01, U * 0.088, U * 0.003, '#5A3A3A'); ctx.strokeStyle = '#5A3A3A'; ctx.lineWidth = Math.max(1, U * 0.0018); ctx.beginPath(); ctx.arc(0, U * 0.093, U * 0.008, 0.2, Math.PI - 0.2); ctx.stroke(); ctx.restore();
    /* a snail on the hydrangea, slowly */
    const sx = ox0 + (ox1 - ox0) * (0.2 + 0.03 * Math.sin(t * 0.12)), sy = floorY - U * 0.095; ctx.fillStyle = tone('#F0D6A8'); ctx.beginPath(); ctx.ellipse(sx, sy + U * 0.01, U * 0.03, U * 0.008, 0, 0, TAU); ctx.fill(); disc(ctx, sx - U * 0.025, sy - U * 0.002, U * 0.008, tone('#F0D6A8')); ctx.strokeStyle = tone('#F0D6A8'); ctx.lineWidth = Math.max(1, U * 0.0015); ctx.beginPath(); ctx.moveTo(sx - U * 0.027, sy - U * 0.008); ctx.lineTo(sx - U * 0.03, sy - U * 0.02); ctx.moveTo(sx - U * 0.022, sy - U * 0.008); ctx.lineTo(sx - U * 0.021, sy - U * 0.02); ctx.stroke(); disc(ctx, sx + U * 0.005, sy - U * 0.005, U * 0.016, tone(sc.accent2)); ctx.strokeStyle = rgba('#FFFFFF', 0.55); ctx.beginPath(); ctx.arc(sx + U * 0.005, sy - U * 0.005, U * 0.009, 0, 4.4); ctx.stroke();
    /* sun sparkling on the wet yard */
    ctx.globalCompositeOperation = 'screen';
    for (let i = 0; i < 12; i++) { const b = Math.pow(0.5 + 0.5 * Math.sin(t * (1.1 + r(seed, i, 41)) + i * 2.3), 3), px = ox0 + (ox1 - ox0) * r(seed, i, 42), py = gy + (floorY - gy) * (0.1 + 0.85 * r(seed, i, 43)); ctx.fillStyle = rgba('#FFFFFF', 0.85 * b); sparkle(ctx, px, py, U * 0.016 * (0.5 + b)); ctx.fill(); }
    ctx.globalCompositeOperation = 'source-over';
  });

  /* ================= ほうきの夜間飛行 ================= */
  const witch = (ctx, x, y, s, t, dark, cols) => {
    /* (x, y): the broom's middle; s: witch height. Faces right, leaning into the wind. */
    const bob = Math.sin(t * 2.4) * s * 0.03, tilt = -0.1 + Math.sin(t * 1.6) * 0.04;
    ctx.save(); ctx.translate(x, y + bob); ctx.rotate(tilt);
    ctx.strokeStyle = '#A0703F'; ctx.lineWidth = s * 0.05; ctx.lineCap = 'round'; ctx.beginPath(); ctx.moveTo(-s * 0.62, s * 0.02); ctx.lineTo(s * 0.62, -s * 0.03); ctx.stroke();
    ctx.fillStyle = '#D9A860'; ctx.beginPath(); ctx.moveTo(-s * 0.58, -s * 0.03); for (let i = 0; i <= 7; i++) ctx.lineTo(-s * (0.86 + 0.06 * (i % 2)) - Math.sin(t * 8 + i) * s * 0.02, -s * 0.09 + i * s * 0.03); ctx.lineTo(-s * 0.58, s * 0.13); ctx.closePath(); ctx.fill();
    ctx.strokeStyle = '#C08040'; ctx.lineWidth = Math.max(1, s * 0.012); for (let i = 0; i < 4; i++) { ctx.beginPath(); ctx.moveTo(-s * 0.6, -s * 0.02 + i * s * 0.035); ctx.lineTo(-s * 0.82, -s * 0.05 + i * s * 0.04); ctx.stroke(); }
    ctx.fillStyle = cols.robe; ctx.beginPath(); ctx.moveTo(-s * 0.16, -s * 0.05); ctx.quadraticCurveTo(-s * 0.06, -s * 0.5, s * 0.06, -s * 0.5); ctx.quadraticCurveTo(s * 0.22, -s * 0.4, s * 0.2, -s * 0.02); ctx.quadraticCurveTo(s * 0.06, s * 0.06, -s * 0.16, -s * 0.05); ctx.fill();
    ctx.fillStyle = cols.robe2; ctx.beginPath(); ctx.moveTo(-s * 0.16, -s * 0.05); ctx.quadraticCurveTo(-s * 0.3 - Math.sin(t * 6) * s * 0.03, s * 0.02, -s * 0.34, s * 0.14); ctx.quadraticCurveTo(-s * 0.12, s * 0.08, s * 0.04, s * 0.06); ctx.fill();
    ctx.fillStyle = '#FFE4D0'; ctx.beginPath(); ctx.ellipse(s * 0.28, s * 0.06, s * 0.05, s * 0.03, 0.3, 0, TAU); ctx.fill(); ctx.beginPath(); ctx.ellipse(s * 0.02, s * 0.04, s * 0.05, s * 0.03, -0.2, 0, TAU); ctx.fill();
    ctx.fillStyle = cols.boot; ctx.beginPath(); ctx.ellipse(-s * 0.04, s * 0.13, s * 0.06, s * 0.045, 0.3, 0, TAU); ctx.ellipse(s * 0.12, s * 0.12, s * 0.06, s * 0.045, 0.2, 0, TAU); ctx.fill();
    /* hair streaming behind, head, hat */
    ctx.fillStyle = cols.hair; ctx.beginPath(); ctx.moveTo(s * 0.04, -s * 0.66); ctx.quadraticCurveTo(-s * 0.2 - Math.sin(t * 5) * s * 0.03, -s * 0.66, -s * 0.34 - Math.sin(t * 5.5) * s * 0.03, -s * 0.44); ctx.quadraticCurveTo(-s * 0.16, -s * 0.5, s * 0.02, -s * 0.5); ctx.fill();
    ctx.fillStyle = '#FFE4D0'; ctx.beginPath(); ctx.arc(s * 0.1, -s * 0.6, s * 0.16, 0, TAU); ctx.fill();
    ctx.fillStyle = cols.hair; ctx.beginPath(); ctx.arc(s * 0.1, -s * 0.66, s * 0.165, Math.PI * 1.02, Math.PI * 1.98); ctx.quadraticCurveTo(s * 0.25, -s * 0.66, s * 0.22, -s * 0.6); ctx.quadraticCurveTo(s * 0.12, -s * 0.72, -s * 0.05, -s * 0.62); ctx.fill();
    ctx.fillStyle = '#3A2E3A'; ctx.beginPath(); ctx.ellipse(s * 0.17, -s * 0.59, s * 0.016, s * 0.024, 0, 0, TAU); ctx.ellipse(s * 0.06, -s * 0.59, s * 0.016, s * 0.024, 0, 0, TAU); ctx.fill();
    ctx.fillStyle = rgba('#FF8FA3', 0.6); ctx.beginPath(); ctx.ellipse(s * 0.2, -s * 0.55, s * 0.03, s * 0.018, 0, 0, TAU); ctx.ellipse(s * 0.03, -s * 0.55, s * 0.03, s * 0.018, 0, 0, TAU); ctx.fill();
    ctx.strokeStyle = '#3A2E3A'; ctx.lineWidth = Math.max(1, s * 0.012); ctx.beginPath(); ctx.arc(s * 0.115, -s * 0.555, s * 0.03, 0.2, Math.PI - 0.2); ctx.stroke();
    ctx.fillStyle = cols.hat; ctx.beginPath(); ctx.ellipse(s * 0.1, -s * 0.72, s * 0.3, s * 0.06, -0.1, 0, TAU); ctx.fill();
    ctx.beginPath(); ctx.moveTo(-s * 0.06, -s * 0.74); ctx.quadraticCurveTo(s * 0.06, -s * 0.9, s * 0.1 - s * 0.06 - Math.sin(t * 3) * s * 0.02, -s * 1.14); ctx.quadraticCurveTo(s * 0.28, -s * 0.96, s * 0.28, -s * 0.72); ctx.fill();
    ctx.fillStyle = cols.band; ctx.beginPath(); ctx.moveTo(-s * 0.05, -s * 0.76); ctx.quadraticCurveTo(s * 0.12, -s * 0.8, s * 0.27, -s * 0.76); ctx.lineTo(s * 0.27, -s * 0.72); ctx.quadraticCurveTo(s * 0.12, -s * 0.75, -s * 0.05, -s * 0.72); ctx.fill();
    ctx.fillStyle = '#FFE58A'; star(ctx, s * 0.08, -s * 0.9, s * 0.05, 5, 0.5); ctx.fill();
    /* the kitten riding on the back of the broom */
    const kx = -s * 0.42, ky = -s * 0.03; ctx.fillStyle = '#3A3450'; ctx.beginPath(); ctx.ellipse(kx, ky - s * 0.1, s * 0.11, s * 0.13, 0, 0, TAU); ctx.fill();
    ctx.beginPath(); ctx.arc(kx + s * 0.02, ky - s * 0.27, s * 0.09, 0, TAU); ctx.fill(); for (const d of [-1, 1]) { ctx.beginPath(); ctx.moveTo(kx + s * 0.02 + d * s * 0.08, ky - s * 0.3); ctx.lineTo(kx + s * 0.02 + d * s * 0.06, ky - s * 0.4); ctx.lineTo(kx + s * 0.02 + d * s * 0.015, ky - s * 0.34); ctx.fill(); }
    ctx.strokeStyle = '#3A3450'; ctx.lineWidth = s * 0.03; ctx.beginPath(); ctx.moveTo(kx - s * 0.08, ky - s * 0.06); ctx.quadraticCurveTo(kx - s * 0.2, ky - s * 0.1 - Math.sin(t * 5) * s * 0.04, kx - s * 0.18, ky - s * 0.24); ctx.stroke();
    ctx.fillStyle = '#FFE58A'; for (const d of [-1, 1]) { ctx.beginPath(); ctx.ellipse(kx + s * 0.02 + d * s * 0.034, ky - s * 0.275, s * 0.018, s * 0.024, 0, 0, TAU); ctx.fill(); }
    ctx.fillStyle = cols.hat; ctx.beginPath(); ctx.moveTo(kx - s * 0.03, ky - s * 0.34); ctx.lineTo(kx + s * 0.02, ky - s * 0.5); ctx.lineTo(kx + s * 0.08, ky - s * 0.34); ctx.fill();
    ctx.restore();
  };
  const house = (x, cx, base, w, h, c, roof, win, lit, seed, id) => {
    x.fillStyle = c; x.fillRect(cx - w / 2, base - h, w, h + 2);
    x.fillStyle = roof; x.beginPath(); x.moveTo(cx - w * 0.62, base - h + 1); x.lineTo(cx, base - h - w * 0.5); x.lineTo(cx + w * 0.62, base - h + 1); x.fill();
    if (r(seed, id, 1) > 0.5) { x.fillStyle = mix(roof, '#000000', 0.2); x.fillRect(cx + w * 0.22, base - h - w * 0.52, w * 0.12, w * 0.3); }
    const cols = 2, rows = Math.max(1, Math.round(h / (w * 0.5)));
    for (let i = 0; i < cols; i++) for (let j = 0; j < rows; j++) { const on = r(seed, id, i, j, 4) > 0.35; x.fillStyle = on ? lit : win; x.fillRect(cx - w * 0.3 + i * w * 0.36, base - h + w * 0.14 + j * w * 0.45, w * 0.24, w * 0.28); }
  };
  K.registerBg('echoWitchFlight', 'ほうきの夜間飛行', ['emotional', 'pop', 'calm'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.9), dark = isDark(sc.bg), portrait = H > W;
    const key = `witch|${seed}|${colourKey(sc)}`, hz = H * 0.8, moonX = W * (portrait ? 0.68 : 0.76), moonY = H * (portrait ? 0.2 : 0.26);
    const top = dark ? mix(sc.bg, '#0B0E34', 0.6) : mix(sc.bg, '#B9A5F0', 0.5), mid = dark ? mix(sc.accent, '#3A2E86', 0.5) : mix(sc.accent, '#FFB6D0', 0.4), low = dark ? mix(sc.accent2, '#7A4AA0', 0.45) : mix('#FFD9C0', sc.accent2, 0.3);
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|sky`, 0.45, (x, w, h) => {
      x.fillStyle = vertical(x, 0, h, [[0, top], [0.55, mid], [1, low]]); x.fillRect(0, 0, w, h);
      glowDisc(x, moonX, moonY, U * 0.7, dark ? '#DCE6FF' : '#FFFFFF', dark ? 0.38 : 0.6);
      disc(x, moonX, moonY, U * 0.11, dark ? '#FFF9E2' : '#FFFFFF'); x.fillStyle = rgba(dark ? '#B7BEEA' : '#E4D6FF', 0.5); for (const [a, b, s] of [[-0.03, -0.02, 0.022], [0.035, 0.03, 0.016], [0.01, -0.05, 0.012]]) { x.beginPath(); x.arc(moonX + a * U, moonY + b * U, U * s, 0, TAU); x.fill(); }
      for (let i = 0; i < (dark ? 110 : 40); i++) { x.fillStyle = rgba('#FFFFFF', (dark ? 0.35 : 0.5) + 0.5 * r(seed, i, 3)); x.beginPath(); x.arc(r(seed, i, 1) * w, r(seed, i, 2) * h * 0.65, U * (0.0012 + 0.0025 * r(seed, i, 4)), 0, TAU); x.fill(); }
    }), 0, 0, W, H);
    for (let i = 0; i < 4; i++) { const cw = U * (0.42 + 0.18 * r(seed, i, 5)), sp = 0.008 + 0.006 * i, cx = wrap(t * U * sp + r(seed, i, 6) * W * 1.2, W + cw * 2) - cw, cy = H * (0.32 + 0.14 * i); K.puffCloud(ctx, cx, cy, cw, cw * 0.36, dark ? mix(mid, '#FFFFFF', 0.25) : '#FFFFFF', dark ? 0.55 : 0.85, seed + i, 1, dark); }
    for (let i = 0; i < 18; i++) { const b = Math.pow(0.5 + 0.5 * Math.sin(t * (0.8 + r(seed, i, 51)) + i * 2.7), 3); ctx.fillStyle = rgba('#FFFFFF', 0.85 * b); sparkle(ctx, W * r(seed, i, 52), H * 0.55 * r(seed, i, 53), U * 0.014 * (0.4 + b)); ctx.fill(); }
    /* the town far below, in three rows, windows lit */
    ctx.drawImage(layer(env, `${key}|town`, 0.55, (x, w, h) => {
      [[hz - U * 0.01, 0.06, 0.4, 0.75], [hz + U * 0.03, 0.09, 0.28, 0.9], [hz + U * 0.09, 0.13, 0.15, 1]].forEach(([base, hs, mixk, litK], row) => {
        const body = mix(low, dark ? '#150F38' : '#8A6AB0', dark ? 0.55 + 0.15 * row : 0.3 + 0.12 * row), roof = mix(body, dark ? sc.accent : '#FFFFFF', dark ? 0.2 : 0.15);
        x.fillStyle = body; x.fillRect(0, base, w, h - base);
        let px = -U * 0.02, i = 0; while (px < w + U * 0.1) { const bw = U * (0.07 + 0.08 * r(seed, row, i, 1)) * (1 + row * 0.3), bh = U * (hs + 0.08 * r(seed, row, i, 2)) * (0.6 + row * 0.3); house(x, px + bw / 2, base + 1, bw, bh, body, roof, mix(body, '#000000', 0.2), dark ? mix('#FFD98A', sc.accent2, 0.15) : mix('#FFFFFF', low, 0.4), seed, row * 50 + i); px += bw * (0.9 + 0.3 * r(seed, row, i, 3)); i++; }
        if (row === 0) { const tx = w * 0.18; x.fillStyle = body; x.fillRect(tx - U * 0.018, base - U * 0.26, U * 0.036, U * 0.27); x.fillStyle = roof; x.beginPath(); x.moveTo(tx - U * 0.03, base - U * 0.26); x.lineTo(tx, base - U * 0.34); x.lineTo(tx + U * 0.03, base - U * 0.26); x.fill(); disc(x, tx, base - U * 0.2, U * 0.011, dark ? '#FFE9A8' : '#FFFFFF'); }
      });
    }), 0, 0, W, H);
    /* the witch: crosses in a slow loop, sparkles trailing behind on her path */
    const path = tt => { const u = wrap(tt / 22, 1); return [W * (-0.15 + 1.3 * u), H * (portrait ? 0.34 : 0.4) + Math.sin(u * TAU * 1.5 + 0.6) * H * 0.09 - Math.sin(u * Math.PI) * H * 0.05]; };
    ctx.globalCompositeOperation = 'screen';
    for (let k = 1; k < 34; k++) { const tt = t - k * 0.09, [px, py] = path(tt), a = 1 - k / 34, sz = U * (0.006 + 0.014 * r(seed, k, 71)) * (0.5 + a), dx = Math.sin(k * 1.9 + t * 2) * U * 0.012, dy = Math.cos(k * 2.3 + t * 2) * U * 0.012 + k * U * 0.002; ctx.fillStyle = rgba(k % 3 ? '#FFE9A8' : mix(sc.accent, '#FFFFFF', 0.5), 0.9 * a); if (k % 2) { sparkle(ctx, px - U * 0.13 + dx, py + dy, sz * 1.5); ctx.fill(); } else { ctx.beginPath(); ctx.arc(px - U * 0.13 + dx, py + dy, sz * 0.55, 0, TAU); ctx.fill(); } }
    ctx.globalCompositeOperation = 'source-over';
    const [wx, wy] = path(t); witch(ctx, wx, wy, U * 0.36, t, dark, { robe: dark ? '#6A4AA8' : '#8A6AD0', robe2: dark ? '#5A3A96' : '#7A5AC0', boot: '#3A2A54', hair: '#FF9E5A', hat: dark ? '#3A2A6E' : '#5A3E9A', band: '#FF8FB0' });
    ctx.globalCompositeOperation = 'screen'; glowDisc(ctx, wx - U * 0.32, wy + U * 0.02, U * 0.22, '#FFE9A8', 0.22); ctx.globalCompositeOperation = 'source-over';
    for (let i = 0; i < 4; i++) { const bx = wrap(t * U * (0.03 + 0.01 * i) + r(seed, i, 81) * W, W + 60) - 30, by = H * (0.18 + 0.07 * i) + Math.sin(t * 0.9 + i) * U * 0.02, fl = Math.sin(t * 6 + i) * U * 0.006; ctx.strokeStyle = rgba(dark ? '#141030' : '#5A4A8A', 0.7); ctx.lineWidth = Math.max(1.2, U * 0.0028); ctx.lineCap = 'round'; ctx.beginPath(); ctx.moveTo(bx - U * 0.012, by + fl); ctx.quadraticCurveTo(bx - U * 0.004, by - U * 0.006, bx, by); ctx.quadraticCurveTo(bx + U * 0.004, by - U * 0.006, bx + U * 0.012, by + fl); ctx.stroke(); }
  });

  /* ================= お菓子のお城 ================= */
  const swirlDisc = (x, cx, cy, rad, c1, c2, rot) => { x.save(); x.translate(cx, cy); x.rotate(rot); x.fillStyle = c1; x.beginPath(); x.arc(0, 0, rad, 0, TAU); x.fill(); x.fillStyle = c2; for (let i = 0; i < 3; i++) { x.beginPath(); x.moveTo(0, 0); x.arc(0, 0, rad * 0.98, i * TAU / 3 + 0.1, i * TAU / 3 + TAU / 6 + 0.1); x.closePath(); x.fill(); } x.fillStyle = rgba('#FFFFFF', 0.55); x.beginPath(); x.arc(-rad * 0.3, -rad * 0.34, rad * 0.16, 0, TAU); x.fill(); x.restore(); };
  const cone = (x, cx, base, w, h, c, dark2, waffle) => {
    x.fillStyle = c; x.beginPath(); x.moveTo(cx - w / 2, base); x.lineTo(cx, base - h); x.lineTo(cx + w / 2, base); x.closePath(); x.fill();
    x.fillStyle = dark2; x.beginPath(); x.moveTo(cx + w * 0.1, base); x.lineTo(cx, base - h); x.lineTo(cx + w / 2, base); x.closePath(); x.fill();
    if (waffle) { x.strokeStyle = rgba('#FFFFFF', 0.4); x.lineWidth = Math.max(1, w * 0.02); for (let i = 1; i < 4; i++) { const yy = base - h * i / 4, hw = w / 2 * (1 - i / 4); x.beginPath(); x.moveTo(cx - hw, yy); x.lineTo(cx + hw, yy); x.stroke(); } }
  };
  K.registerBg('echoCandyCastle', 'お菓子のお城', ['pop', 'calm', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.9), dark = isDark(sc.bg), portrait = H > W, tone = c => (dark ? mix(c, mix(sc.bg, '#0F1A38', 0.5), 0.2) : c);
    const key = `candy|${seed}|${colourKey(sc)}`, gy = H * 0.76, cx0 = W * (portrait ? 0.5 : 0.52), pal = [sc.accent, sc.accent2, '#FFD86B', '#9AD0F5', '#B8E2B0', '#F5A9C0'];
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.5, (x, w, h) => {
      x.fillStyle = dark ? vertical(x, 0, gy, [[0, '#1E1A58'], [0.6, mix('#5A4AA8', sc.accent, 0.3)], [1, mix('#9A70C8', sc.accent2, 0.35)]]) : vertical(x, 0, gy, [[0, mix('#A6D8FF', sc.bg, 0.2)], [0.55, mix('#FFD9F0', sc.bg, 0.3)], [1, '#FFF3D6']]); x.fillRect(0, 0, w, gy + 2);
      if (dark) for (let i = 0; i < 70; i++) { x.fillStyle = rgba('#FFFFFF', 0.3 + 0.6 * r(seed, i, 3)); x.beginPath(); x.arc(r(seed, i, 1) * w, r(seed, i, 2) * gy * 0.6, U * 0.002, 0, TAU); x.fill(); }
      const rcx = cx0, rr = Math.min(W * 0.46, H * 0.62); ['#FF8A9A', '#FFB877', '#FFE58A', '#A6E6A0', '#9AD0F5', '#C4A6F5'].forEach((c, i) => { x.strokeStyle = rgba(c, dark ? 0.35 : 0.6); x.lineWidth = U * 0.03; x.beginPath(); x.arc(rcx, gy + U * 0.1, rr - i * U * 0.028, Math.PI * 1.06, Math.PI * 1.94); x.stroke(); });
      for (let i = 0; i < 4; i++) K.puffCloud(x, w * (0.1 + 0.27 * i + 0.05 * r(seed, i, 5)), gy * (0.22 + 0.2 * (i % 2)), U * (0.42 + 0.14 * r(seed, i, 6)), U * 0.15, mix(i % 2 ? sc.accent : '#B5DDFF', '#FFFFFF', dark ? 0.2 : 0.55), dark ? 0.55 : 0.92, seed + i, 1, dark);
      /* gumdrop hills */
      [[0.12, 0.9, 0.5], [0.42, 1.0, 0.36], [0.78, 0.92, 0.5], [1.0, 0.85, 0.36]].forEach(([u, s, f], i) => { const c = tone(mix(pal[(i + 2) % 6], '#FFFFFF', 0.25)); x.fillStyle = c; x.beginPath(); x.ellipse(w * u, gy + U * 0.02, U * 0.55 * s, U * 0.16 * s + f * U * 0.04, 0, Math.PI, TAU); x.fill(); x.fillStyle = rgba('#FFFFFF', 0.28); x.beginPath(); x.ellipse(w * u - U * 0.1 * s, gy - U * 0.08 * s, U * 0.1, U * 0.03, -0.4, 0, TAU); x.fill(); for (let k = 0; k < 8; k++) { x.fillStyle = rgba(k % 2 ? '#FFFFFF' : pal[k % 6], 0.6); x.beginPath(); x.arc(w * u + (r(seed, i, k, 1) - 0.5) * U * 0.7 * s, gy - U * 0.02 - r(seed, i, k, 2) * U * 0.12 * s, U * 0.007, 0, TAU); x.fill(); } });
      /* the castle */
      const cw = U * (portrait ? 0.62 : 0.7), ch = U * 0.34, bx = cx0 - cw / 2, by = gy - ch;
      const wafer = tone(mix('#F5D9A8', sc.bg, 0.1)), cream = tone('#FFFFFF'), pink = tone(mix('#FFB6CE', sc.accent, 0.3)), mint = tone('#BFEDD2');
      const tower = (tx, tw, th, roofC, roofD, body) => {
        x.fillStyle = body; x.fillRect(tx - tw / 2, gy - th, tw, th + 2); x.strokeStyle = rgba('#B98A55', 0.35); x.lineWidth = Math.max(1, U * 0.002); for (let i = 1; i < 6; i++) { x.beginPath(); x.moveTo(tx - tw / 2, gy - th * i / 6); x.lineTo(tx + tw / 2, gy - th * i / 6); x.stroke(); } for (let i = 1; i < 3; i++) { x.beginPath(); x.moveTo(tx - tw / 2 + tw * i / 3, gy - th); x.lineTo(tx - tw / 2 + tw * i / 3, gy); x.stroke(); }
        x.fillStyle = cream; for (let i = 0; i < 5; i++) { x.beginPath(); x.arc(tx - tw / 2 + tw * (i + 0.5) / 5, gy - th, tw / 10, Math.PI, TAU); x.fill(); }
        cone(x, tx, gy - th - tw * 0.06, tw * 1.24, tw * 1.5, roofC, roofD, false); x.strokeStyle = rgba('#FFFFFF', 0.6); x.lineWidth = Math.max(1.5, U * 0.004); for (let i = 1; i < 4; i++) { const yy = gy - th - tw * 0.06 - tw * 1.5 * i / 4, hw = tw * 0.62 * (1 - i / 4); x.beginPath(); x.moveTo(tx - hw, yy + tw * 0.2 * (1 - i / 4)); x.quadraticCurveTo(tx, yy - tw * 0.12, tx + hw, yy + tw * 0.2 * (1 - i / 4)); x.stroke(); }
        disc(x, tx, gy - th * 0.7, tw * 0.17, tone('#FFD86B')); disc(x, tx, gy - th * 0.7, tw * 0.1, tone('#FFF3B8'));
      };
      tower(cx0 - cw * 0.44, cw * 0.2, ch * 1.32, tone('#FF9EBC'), tone('#F07A9E'), wafer); tower(cx0 + cw * 0.44, cw * 0.2, ch * 1.32, tone('#9AD0F5'), tone('#6FB0E6'), wafer);
      x.fillStyle = wafer; x.fillRect(bx + cw * 0.08, by, cw * 0.84, ch + 2); x.strokeStyle = rgba('#B98A55', 0.32); x.lineWidth = Math.max(1, U * 0.002); for (let i = 1; i < 8; i++) { x.beginPath(); x.moveTo(bx + cw * 0.08, by + ch * i / 8); x.lineTo(bx + cw * 0.92, by + ch * i / 8); x.stroke(); } for (let i = 1; i < 12; i++) { x.beginPath(); x.moveTo(bx + cw * 0.08 + cw * 0.84 * i / 12, by); x.lineTo(bx + cw * 0.08 + cw * 0.84 * i / 12, gy); x.stroke(); }
      x.fillStyle = cream; for (let i = 0; i < 9; i++) { x.beginPath(); x.arc(bx + cw * 0.08 + cw * 0.84 * (i + 0.5) / 9, by, cw * 0.84 / 18, Math.PI, TAU); x.fill(); } x.fillStyle = pink; for (let i = 0; i < 9; i++) { x.beginPath(); x.arc(bx + cw * 0.08 + cw * 0.84 * (i + 0.5) / 9, by + ch * 0.06, cw * 0.84 / 26, Math.PI, TAU); x.fill(); }
      tower(cx0, cw * 0.26, ch * 1.75, tone('#FFB6CE'), tone('#F08AAE'), tone(mix('#FFE3F0', sc.accent, 0.15)));
      x.fillStyle = tone('#8A5A3A'); x.beginPath(); x.moveTo(cx0 - cw * 0.09, gy); x.lineTo(cx0 - cw * 0.09, gy - ch * 0.35); x.arc(cx0, gy - ch * 0.35, cw * 0.09, Math.PI, TAU); x.lineTo(cx0 + cw * 0.09, gy); x.fill(); x.strokeStyle = rgba('#FFFFFF', 0.4); x.lineWidth = Math.max(1, U * 0.003); x.beginPath(); x.moveTo(cx0, gy - ch * 0.35 - cw * 0.09); x.lineTo(cx0, gy); x.stroke(); disc(x, cx0 + cw * 0.04, gy - ch * 0.17, cw * 0.012, '#FFD86B');
      [-0.27, 0.27].forEach((d, i) => { const wx = cx0 + cw * d, wy = gy - ch * 0.62; swirlDisc(x, wx, wy, cw * 0.06, tone(pal[i]), tone('#FFFFFF'), 0.3 * i); x.strokeStyle = rgba('#FFFFFF', 0.7); x.lineWidth = Math.max(1, U * 0.003); x.beginPath(); x.arc(wx, wy, cw * 0.066, 0, TAU); x.stroke(); });
      [-0.16, 0.16].forEach(d => { x.fillStyle = tone('#FFF6EA'); x.beginPath(); x.arc(cx0 + cw * d, gy - ch * 0.18, cw * 0.035, 0, TAU); x.fill(); disc(x, cx0 + cw * d, gy - ch * 0.18, cw * 0.016, tone('#FFB6CE')); });
      /* lollipop trees and candy canes */
      [[0.08, 0.32, 0], [0.22, 0.22, 1], [0.86, 0.3, 2], [0.94, 0.2, 3]].forEach(([u, s, i]) => { const lx = w * u, ly = gy + U * 0.02; x.strokeStyle = tone('#FFFFFF'); x.lineWidth = Math.max(2, U * 0.012); x.lineCap = 'round'; x.beginPath(); x.moveTo(lx, ly); x.lineTo(lx, ly - U * s); x.stroke(); swirlDisc(x, lx, ly - U * s - U * 0.07, U * 0.085, tone(pal[i % 6]), tone('#FFFFFF'), i); });
      [[0.32, 0.12], [0.7, 0.14]].forEach(([u, s], i) => { const lx = w * u, ly = gy + U * 0.025; x.lineWidth = U * 0.014; x.lineCap = 'round'; x.strokeStyle = tone('#FFFFFF'); x.beginPath(); x.moveTo(lx, ly); x.lineTo(lx, ly - U * s); x.arc(lx + U * 0.035, ly - U * s, U * 0.035, Math.PI, 0); x.stroke(); x.strokeStyle = tone(i ? '#9AD0F5' : '#FF8FA3'); x.setLineDash([U * 0.012, U * 0.014]); x.beginPath(); x.moveTo(lx, ly); x.lineTo(lx, ly - U * s); x.arc(lx + U * 0.035, ly - U * s, U * 0.035, Math.PI, 0); x.stroke(); x.setLineDash([]); });
      x.fillStyle = vertical(x, gy, h, [[0, tone(mix('#F8E0B8', sc.bg, 0.1))], [1, tone(mix('#E8C494', sc.bg, 0.15))]]); x.fillRect(0, gy + U * 0.01, w, h - gy);
      x.fillStyle = tone(mix('#FFB6CE', sc.accent, 0.3)); x.beginPath(); x.moveTo(0, gy + U * 0.01); for (let i = 0; i <= 26; i++) x.lineTo(w * i / 26, gy + U * 0.01 + Math.abs(Math.sin(i * 0.9 + seed)) * U * 0.016 + U * 0.012); x.lineTo(w, gy - U * 0.005); x.lineTo(0, gy - U * 0.005); x.fill();
      for (let i = 0; i < 26; i++) { x.fillStyle = tone(pal[i % 6]); x.beginPath(); x.ellipse(r(seed, i, 41) * w, gy + U * 0.06 + r(seed, i, 42) * (h - gy - U * 0.06), U * 0.02, U * 0.011, r(seed, i, 43) * 3, 0, TAU); x.fill(); x.fillStyle = rgba('#FFFFFF', 0.5); x.beginPath(); x.arc(r(seed, i, 41) * w - U * 0.006, gy + U * 0.06 + r(seed, i, 42) * (h - gy - U * 0.06) - U * 0.003, U * 0.004, 0, TAU); x.fill(); }
    }), 0, 0, W, H);
    /* flags on the towers, fluttering */
    [[-0.44, 1.32, '#FF8FA3'], [0.44, 1.32, '#7AB8F0'], [0, 1.75, '#FFD86B']].forEach(([d, hh, c], i) => {
      const cw = U * (portrait ? 0.62 : 0.7), ch = U * 0.34, tw = cw * (i === 2 ? 0.26 : 0.2), fx = cx0 + cw * d, fy = gy - ch * hh - tw * 1.56;
      ctx.strokeStyle = tone('#8A6A50'); ctx.lineWidth = Math.max(1.5, U * 0.004); ctx.beginPath(); ctx.moveTo(fx, fy); ctx.lineTo(fx, fy - U * 0.07); ctx.stroke();
      ctx.fillStyle = tone(c); ctx.beginPath(); ctx.moveTo(fx, fy - U * 0.07); for (let k = 0; k <= 8; k++) { const u = k / 8; ctx.lineTo(fx + u * U * 0.06, fy - U * 0.06 + Math.sin(t * 5 + u * 5 + i) * U * 0.006 * u); } for (let k = 8; k >= 0; k--) { const u = k / 8; ctx.lineTo(fx + u * U * 0.06 * (1 - 0.0), fy - U * 0.04 - u * U * 0.008 + Math.sin(t * 5 + u * 5 + i) * U * 0.006 * u); } ctx.fill();
    });
    if (dark) {                                                                                    /* the castle's windows and door glow at night */
      const cw = U * (portrait ? 0.62 : 0.7), ch = U * 0.34; ctx.globalCompositeOperation = 'screen';
      [[-0.27, 0.62], [0.27, 0.62], [0, 0.17]].forEach(([d, hh], i) => glowDisc(ctx, cx0 + cw * d, gy - ch * hh, cw * (i === 2 ? 0.2 : 0.14), '#FFD98A', 0.42 + 0.12 * Math.sin(t * 1.4 + i)));
      glowDisc(ctx, cx0, gy - ch * 1.2, cw * 0.5, '#FFB6E0', 0.16); ctx.globalCompositeOperation = 'source-over';
    }
    /* twinkling lights strung along the walls at dusk, sparkles drifting near the castle */
    ctx.globalCompositeOperation = 'screen';
    for (let i = 0; i < 24; i++) { const b = Math.pow(0.5 + 0.5 * Math.sin(t * (1 + r(seed, i, 61)) + i * 2.3), 3), px = cx0 + (r(seed, i, 62) - 0.5) * U * 1.3, py = gy - U * (0.06 + 0.7 * r(seed, i, 63)); ctx.fillStyle = rgba(i % 2 ? '#FFFFFF' : '#FFE9A8', 0.9 * b); sparkle(ctx, px, py, U * 0.016 * (0.5 + b)); ctx.fill(); }
    ctx.globalCompositeOperation = 'source-over';
    /* macarons and balloons floating up, sprinkles falling */
    [[0.16, 0.35], [0.84, 0.28]].forEach(([u, v], i) => { const bx = W * u + Math.sin(t * 0.7 + i * 2) * U * 0.02, by = H * v + Math.sin(t * 0.9 + i) * U * 0.02, rad = U * 0.06, c = pal[i * 2]; ctx.strokeStyle = rgba('#FFFFFF', 0.6); ctx.lineWidth = Math.max(1, U * 0.002); ctx.beginPath(); ctx.moveTo(bx, by + rad); ctx.quadraticCurveTo(bx + rad * 0.3, by + rad * 2.5, W * (u < 0.5 ? 0.14 : 0.86), gy); ctx.stroke(); const g = ctx.createRadialGradient(bx - rad * 0.3, by - rad * 0.35, rad * 0.1, bx, by, rad); g.addColorStop(0, rgba(mix(c, '#FFFFFF', 0.6), 0.98)); g.addColorStop(1, rgba(c, 0.95)); ctx.fillStyle = g; ctx.beginPath(); ctx.ellipse(bx, by, rad * 0.86, rad, 0, 0, TAU); ctx.fill(); ctx.fillStyle = rgba(c, 0.95); ctx.beginPath(); ctx.moveTo(bx, by + rad); ctx.lineTo(bx - rad * 0.14, by + rad * 1.2); ctx.lineTo(bx + rad * 0.14, by + rad * 1.2); ctx.fill(); heart(ctx, bx, by, rad * 0.32); ctx.fillStyle = rgba('#FFFFFF', 0.85); ctx.fill(); });
    for (let i = 0; i < 26; i++) { const sp = H * (0.03 + 0.05 * r(seed, i, 71)), y = wrap(r(seed, i, 72) * H * 1.2 + t * sp, H * 1.2) - H * 0.1, px = r(seed, i, 73) * W + Math.sin(t * 0.6 + i) * U * 0.02; ctx.save(); ctx.translate(px, y); ctx.rotate(t * (0.6 + r(seed, i, 74)) + i); ctx.fillStyle = rgba(pal[i % 6], 0.9); ctx.fillRect(-U * 0.008, -U * 0.0025, U * 0.016, U * 0.005); ctx.restore(); }
  });
})();
