/* ECHO first-party backdrops for ねこ神社: the shrine, an afternoon on the engawa and cats on a moonlit roof.
   The cats are original round little characters drawn here from circles and curves. */
(() => {
  'use strict';
  const K = J.echo, E = K.E, TAU = K.TAU, clamp = K.clamp, mix = K.mix, rgba = K.rgba;
  const { U: Umin, isDark, wrap, hues, fade, vertical, glowDisc, star, layer, scroll, shapes } = K;
  const { heart, petal, sakura, puff, sparkle, roundRect } = shapes;
  const colourKey = sc => [sc.bg, sc.fg, sc.accent, sc.accent2, sc.ghostA, sc.ghostB].join('');
  const FUR = [['#FFF7EC', '#FFFFFF'], ['#F6B26B', '#FFE3BC'], ['#4A4658', '#6C6880'], ['#C4C8D6', '#F0F1F6']];

  /* one cat: mode 'sit' | 'loaf'; with `sil` (a colour) it is a flat silhouette with bright eyes. (x, y) is where it sits, s its height. */
  const cat = (ctx, x, y, s, o) => {
    const [fur, belly] = o.coat || FUR[0], t = o.t || 0, sway = Math.sin(t * 1.4 + (o.phase || 0)) * s * 0.06, flip = o.flip ? -1 : 1, mode = o.mode || 'sit';
    const line = o.line || '#7A4B4B', blink = Math.sin(t * 0.9 + (o.phase || 0) * 3) > 0.97 || o.asleep, sil = !!o.sil, body = sil ? o.sil : fur;
    ctx.save(); ctx.translate(x, y); ctx.scale(flip, 1);
    const fill = c => { ctx.fillStyle = c; ctx.fill(); };
    if (mode === 'loaf') {
      ctx.beginPath(); ctx.moveTo(s * 0.4, -s * 0.05); ctx.bezierCurveTo(s * 0.72 + sway, -s * 0.05, s * 0.72 + sway * 1.5, -s * 0.34, s * 0.5 + sway, -s * 0.34); ctx.lineWidth = s * 0.12; ctx.lineCap = 'round'; ctx.strokeStyle = body; ctx.stroke();
      ctx.beginPath(); ctx.ellipse(0, -s * 0.27, s * 0.5, s * 0.27, 0, 0, TAU); fill(body);
      if (!sil) { ctx.beginPath(); ctx.ellipse(-s * 0.05, -s * 0.12, s * 0.3, s * 0.12, 0, 0, TAU); fill(belly); }
      ctx.save(); ctx.translate(-s * 0.36, -s * 0.36 + Math.sin(t * 1.1) * s * 0.008);
      head(ctx, s * 0.26, body, belly, line, blink, sil, 0, o); ctx.restore();
      if (o.asleep && !sil) { ctx.fillStyle = rgba(line, 0.5); ctx.font = `bold ${s * 0.16}px sans-serif`; const zz = (t * 0.6) % 1; ctx.globalAlpha *= 1 - zz; ctx.fillText('z', -s * 0.6 - zz * s * 0.1, -s * 0.72 - zz * s * 0.3); ctx.font = `bold ${s * 0.11}px sans-serif`; ctx.fillText('z', -s * 0.5 - zz * s * 0.08, -s * 0.62 - zz * s * 0.25); ctx.globalAlpha = o.alpha || 1; }
    } else {
      ctx.beginPath(); ctx.moveTo(s * 0.22, -s * 0.06); ctx.bezierCurveTo(s * 0.7 + sway * 2, -s * 0.02, s * 0.7 + sway * 2.5, -s * 0.55, s * 0.42 + sway * 2.5, -s * 0.62); ctx.lineWidth = s * 0.13; ctx.lineCap = 'round'; ctx.strokeStyle = body; ctx.stroke();
      ctx.beginPath(); ctx.moveTo(-s * 0.26, 0); ctx.bezierCurveTo(-s * 0.36, -s * 0.2, -s * 0.24, -s * 0.66, 0, -s * 0.66); ctx.bezierCurveTo(s * 0.24, -s * 0.66, s * 0.36, -s * 0.2, s * 0.26, 0); ctx.closePath(); fill(body);
      if (!sil) { ctx.beginPath(); ctx.ellipse(0, -s * 0.24, s * 0.15, s * 0.22, 0, 0, TAU); fill(belly); if (o.bib) { ctx.fillStyle = o.bib; ctx.beginPath(); ctx.moveTo(-s * 0.2, -s * 0.5); ctx.lineTo(s * 0.2, -s * 0.5); ctx.lineTo(0, -s * 0.28); ctx.fill(); ctx.fillStyle = '#FFD86B'; ctx.beginPath(); ctx.arc(0, -s * 0.3, s * 0.04, 0, TAU); ctx.fill(); } }
      ctx.fillStyle = body; ctx.beginPath(); ctx.ellipse(-s * 0.1, -s * 0.02, s * 0.09, s * 0.05, 0, 0, TAU); ctx.ellipse(s * 0.1, -s * 0.02, s * 0.09, s * 0.05, 0, 0, TAU); ctx.fill();
      ctx.save(); ctx.translate(0, -s * 0.8 + Math.sin(t * 1.1 + 1) * s * 0.008); head(ctx, s * 0.3, body, belly, line, blink, sil, o.look || 0, o); ctx.restore();
    }
    ctx.restore();
  };
  const head = (ctx, r, body, belly, line, blink, sil, look, o) => {
    const ear = (dir) => { ctx.beginPath(); ctx.moveTo(dir * r * 0.92, -r * 0.15); ctx.quadraticCurveTo(dir * r * 1.02, -r * 1.05, dir * r * 0.72, -r * 1.22); ctx.quadraticCurveTo(dir * r * 0.4, -r * 1.1, dir * r * 0.2, -r * 0.88); ctx.closePath(); ctx.fillStyle = body; ctx.fill(); if (!sil) { ctx.beginPath(); ctx.moveTo(dir * r * 0.78, -r * 0.4); ctx.quadraticCurveTo(dir * r * 0.86, -r * 0.9, dir * r * 0.68, -r * 1.0); ctx.quadraticCurveTo(dir * r * 0.5, -r * 0.92, dir * r * 0.4, -r * 0.78); ctx.closePath(); ctx.fillStyle = '#FFC0CB'; ctx.fill(); } };
    ear(-1); ear(1);
    ctx.fillStyle = body; ctx.beginPath(); ctx.ellipse(0, 0, r * 1.08, r * 0.92, 0, 0, TAU); ctx.fill();
    if (sil) { if (!blink) { ctx.fillStyle = '#FFE58A'; ctx.beginPath(); ctx.ellipse(-r * 0.38 + look * r * 0.1, -r * 0.05, r * 0.12, r * 0.16, 0, 0, TAU); ctx.ellipse(r * 0.38 + look * r * 0.1, -r * 0.05, r * 0.12, r * 0.16, 0, 0, TAU); ctx.fill(); } return; }
    if (o.marks) { ctx.fillStyle = o.marks; ctx.beginPath(); ctx.ellipse(-r * 0.45, -r * 0.62, r * 0.32, r * 0.2, -0.4, 0, TAU); ctx.fill(); }
    ctx.fillStyle = rgba('#FF9DB0', 0.55); ctx.beginPath(); ctx.ellipse(-r * 0.62, r * 0.2, r * 0.2, r * 0.12, 0, 0, TAU); ctx.ellipse(r * 0.62, r * 0.2, r * 0.2, r * 0.12, 0, 0, TAU); ctx.fill();
    ctx.strokeStyle = line; ctx.fillStyle = line; ctx.lineWidth = Math.max(1, r * 0.06); ctx.lineCap = 'round';
    if (blink) { for (const d of [-1, 1]) { ctx.beginPath(); ctx.arc(d * r * 0.4, -r * 0.02, r * 0.14, Math.PI * 0.1, Math.PI * 0.9); ctx.stroke(); } }
    else for (const d of [-1, 1]) { const ex = d * r * 0.4 + look * r * 0.08; ctx.beginPath(); ctx.ellipse(ex, -r * 0.05, r * 0.11, r * 0.15, 0, 0, TAU); ctx.fill(); ctx.fillStyle = '#FFFFFF'; ctx.beginPath(); ctx.arc(ex + r * 0.03, -r * 0.1, r * 0.04, 0, TAU); ctx.fill(); ctx.fillStyle = line; }
    ctx.fillStyle = '#FF8FA3'; ctx.beginPath(); ctx.moveTo(-r * 0.09, r * 0.2); ctx.lineTo(r * 0.09, r * 0.2); ctx.lineTo(0, r * 0.3); ctx.fill();
    ctx.beginPath(); ctx.moveTo(0, r * 0.3); ctx.quadraticCurveTo(-r * 0.05, r * 0.42, -r * 0.16, r * 0.38); ctx.moveTo(0, r * 0.3); ctx.quadraticCurveTo(r * 0.05, r * 0.42, r * 0.16, r * 0.38); ctx.stroke();
    ctx.lineWidth = Math.max(1, r * 0.03); ctx.strokeStyle = rgba(line, 0.6);
    for (const d of [-1, 1]) for (const k of [-0.05, 0.1, 0.25]) { ctx.beginPath(); ctx.moveTo(d * r * 0.55, r * (0.2 + k * 0.4)); ctx.lineTo(d * r * 1.25, r * (0.12 + k)); ctx.stroke(); }
  };
  K.shapes.cat = cat;

  const tonefor = (sc, dark) => c => (dark ? mix(c, mix(sc.bg, '#0F1A38', 0.5), 0.42) : c);
  const canopy = (x, cx, cy, size, sc, tone, seed, id) => {
    for (let k = 0; k < 12; k++) {
      const a = J.r(seed, id, k, 4) * TAU, rr = Math.sqrt(J.r(seed, id, k, 5)) * size * 0.42, r = size * (0.16 + 0.12 * J.r(seed, id, k, 6)), px = cx + Math.cos(a) * rr * 1.3, py = cy + Math.sin(a) * rr * 0.75;
      x.fillStyle = tone(mix('#FFD3E1', sc.accent, 0.1 + 0.4 * J.r(seed, id, k, 7))); x.beginPath(); x.arc(px, py, r, 0, TAU); x.fill();
      x.fillStyle = rgba('#FFFFFF', 0.28); x.beginPath(); x.arc(px - r * 0.25, py - r * 0.3, r * 0.5, 0, TAU); x.fill();
    }
  };

  Object.assign(K.shapes, { FUR, tonefor, canopy });

  /* ================= ねこ神社 ================= */
  K.registerBg('echoNekoShrine', 'ねこ神社', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), dark = isDark(sc.bg), tone = tonefor(sc, dark), portrait = H > W;
    const key = `shrine|${seed}|${colourKey(sc)}`, y0 = H * 0.6, vermilion = tone(mix('#F0705E', sc.accent, 0.25));
    ctx.globalAlpha = e;
    const scene = layer(env, `${key}|scene`, 0.6, (x, w, h) => {
      x.fillStyle = vertical(x, 0, y0, [[0, dark ? mix(sc.bg, '#0F1A48', 0.4) : mix(sc.bg, '#A8DAFF', 0.4)], [1, dark ? mix(sc.bg, sc.accent, 0.2) : mix(sc.bg, '#FFFFFF', 0.45)]]); x.fillRect(0, 0, w, y0 + 2);
      const sx = w * (portrait ? 0.7 : 0.72), sy = h * 0.2; glowDisc(x, sx, sy, U * 0.5, dark ? '#FFF1B8' : '#FFFFFF', dark ? 0.3 : 0.6);
      x.fillStyle = dark ? '#FFF6D8' : '#FFFDEB'; x.beginPath(); x.arc(sx, sy, U * (dark ? 0.05 : 0.07), 0, TAU); x.fill();
      [[0.55, 0.14, 0.6], [0.7, 0.2, 0.4]].forEach(([lvl, amp, sh], n) => { x.fillStyle = tone(mix('#C9E9D2', sc.bg, sh * 0.5)); x.beginPath(); x.moveTo(0, y0); for (let i = 0; i <= 10; i++) x.lineTo(w * i / 10, y0 - U * 0.09 - Math.sin(i * 1.3 + n * 2 + J.r(seed, n, 1) * 3) * U * amp * 0.28 - n * U * 0.02); x.lineTo(w, y0); x.fill(); });
      x.fillStyle = tone(mix('#D8EFC6', sc.bg, 0.15)); x.fillRect(0, y0, w, h - y0);
      /* stone stairs climbing to a small torii */
      const cx = w * 0.5, top = y0 + U * 0.01, tw = U * 0.05, bw = w * (portrait ? 0.5 : 0.3);
      x.fillStyle = tone('#EDEBF0'); x.beginPath(); x.moveTo(cx - tw, top); x.lineTo(cx + tw, top); x.lineTo(cx + bw, h); x.lineTo(cx - bw, h); x.closePath(); x.fill();
      x.strokeStyle = rgba(tone('#B9B7C6'), 0.7); x.lineWidth = Math.max(1, U * 0.003);
      for (let i = 1; i < 9; i++) { const d = (i / 9) ** 1.6, y = top + (h - top) * d, half = tw + (bw - tw) * d; x.beginPath(); x.moveTo(cx - half, y); x.lineTo(cx + half, y); x.stroke(); x.fillStyle = rgba('#FFFFFF', dark ? 0.06 : 0.35); x.fillRect(cx - half, y, half * 2, U * 0.004); }
      const tH = U * 0.2, tWi = U * 0.17, ty = top;
      x.fillStyle = vermilion; x.fillRect(cx - tWi * 0.46, ty - tH, U * 0.014, tH); x.fillRect(cx + tWi * 0.46 - U * 0.014, ty - tH, U * 0.014, tH);
      x.fillRect(cx - tWi * 0.58, ty - tH * 0.78, tWi * 1.16, U * 0.012);
      x.beginPath(); x.moveTo(cx - tWi * 0.72, ty - tH * 1.02); x.quadraticCurveTo(cx, ty - tH * 0.93, cx + tWi * 0.72, ty - tH * 1.02); x.lineTo(cx + tWi * 0.66, ty - tH * 0.93); x.quadraticCurveTo(cx, ty - tH * 0.85, cx - tWi * 0.66, ty - tH * 0.93); x.closePath(); x.fill();
      x.fillStyle = tone('#3F3A4A'); x.beginPath(); x.moveTo(cx - tWi * 0.76, ty - tH * 1.02); x.quadraticCurveTo(cx, ty - tH * 0.96, cx + tWi * 0.76, ty - tH * 1.02); x.lineTo(cx + tWi * 0.76, ty - tH * 1.05); x.quadraticCurveTo(cx, ty - tH * 0.99, cx - tWi * 0.76, ty - tH * 1.05); x.fill();
      /* two blossom trees framing the scene */
      [[0.05, -1], [0.95, 1]].forEach(([ux, side], n) => {
        const bx = w * ux, base = h * 0.92, tsz = U * (portrait ? 0.6 : 0.72);
        x.fillStyle = tone('#7A5A52'); x.beginPath(); x.moveTo(bx - tsz * 0.07, base); x.lineTo(bx - tsz * 0.045, base - tsz * 0.8); x.lineTo(bx + tsz * 0.045, base - tsz * 0.8); x.lineTo(bx + tsz * 0.075, base); x.fill();
        canopy(x, bx - side * tsz * 0.12, base - tsz * 0.95, tsz, sc, tone, seed, n);
      });
      /* statue pedestals */
      [[0.2, 0], [0.8, 1]].forEach(([ux]) => { const px = w * ux, py = h * 0.9; x.fillStyle = tone('#DDDAE4'); x.beginPath(); x.roundRect(px - U * 0.075, py, U * 0.15, h - py, U * 0.01); x.fill(); x.fillStyle = rgba('#FFFFFF', dark ? 0.08 : 0.5); x.fillRect(px - U * 0.07, py, U * 0.14, U * 0.006); });
    });
    ctx.drawImage(scene, 0, 0, W, H);
    /* statues that blink and swish their tails; a string of lanterns swaying overhead */
    [[0.2, false, 0], [0.8, true, 1.7]].forEach(([ux, flip, ph], n) => cat(ctx, W * ux, H * 0.9, U * 0.17, { coat: FUR[n ? 1 : 0], t, phase: ph, flip, bib: '#F2705E', look: flip ? -0.6 : 0.6, alpha: 1 }));
    {
      const yTop = H * 0.05, count = Math.round(W / (U * 0.22)), sag = H * 0.04;
      ctx.strokeStyle = rgba(tone('#7A5A52'), 0.6); ctx.lineWidth = Math.max(1, U * 0.002); ctx.beginPath(); ctx.moveTo(0, yTop - sag * 0.3); ctx.quadraticCurveTo(W * 0.5, yTop + sag * 2.2, W, yTop - sag * 0.3); ctx.stroke();
      for (let i = 0; i < count; i++) {
        const u = (i + 0.5) / count, lx0 = u * W, y = (1 - u) * (1 - u) * (yTop - sag * 0.3) + 2 * u * (1 - u) * (yTop + sag * 2.2) + u * u * (yTop - sag * 0.3), swing = Math.sin(t * 1.1 + i * 0.9) * U * 0.005, r = U * 0.026, ly = y + U * 0.035, lx = lx0 + swing, breath = 0.85 + 0.15 * Math.sin(t * 2.3 + i * 1.7);
        const c = i % 2 ? tone('#FFF3E0') : vermilion;
        ctx.globalCompositeOperation = dark ? 'screen' : 'source-over'; glowDisc(ctx, lx, ly, r * 3.4, '#FFB870', (dark ? 0.32 : 0.14) * breath); ctx.globalCompositeOperation = 'source-over';
        ctx.strokeStyle = rgba(tone('#7A5A52'), 0.6); ctx.beginPath(); ctx.moveTo(lx, y); ctx.lineTo(lx, ly - r); ctx.stroke();
        ctx.fillStyle = tone('#5A4650'); ctx.fillRect(lx - r * 0.45, ly - r * 1.05, r * 0.9, r * 0.16);
        ctx.fillStyle = c; ctx.beginPath(); ctx.ellipse(lx, ly, r * 0.82, r, 0, 0, TAU); ctx.fill(); ctx.strokeStyle = rgba('#000000', 0.12); for (let g = -1; g <= 1; g++) { ctx.beginPath(); ctx.ellipse(lx, ly + g * r * 0.4, r * 0.8 * Math.sqrt(1 - 0.16 * g * g), r * 0.08, 0, 0, TAU); ctx.stroke(); }
        ctx.fillStyle = rgba(i % 2 ? vermilion : '#FFFFFF', 0.9); heart(ctx, lx, ly, r * 0.28); ctx.fill();
      }
    }
    for (let i = 0; i < 18; i++) {
      const y = wrap(J.r(seed, i, 71) * H * 1.2 + t * H * (0.04 + 0.05 * J.r(seed, i, 72)), H * 1.2) - H * 0.1, x = wrap(J.r(seed, i, 73) * W + Math.sin(t * 0.6 + i) * U * 0.05 - t * U * 0.008, W + 40) - 20;
      ctx.save(); ctx.translate(x, y); ctx.rotate(t * (0.4 + J.r(seed, i, 74)) + i); ctx.scale(1, 0.55 + 0.45 * Math.abs(Math.sin(t * 0.9 + i)));
      ctx.fillStyle = rgba(tone('#FFC4D6'), 0.85); petal(ctx, 0, 0, U * (0.007 + 0.009 * J.r(seed, i, 75))); ctx.fill(); ctx.restore();
    }
  });

  /* ================= 縁側の午後 ================= */
  K.registerBg('echoEngawa', '縁側の午後', ['calm', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), dark = isDark(sc.bg), tone = tonefor(sc, dark), portrait = H > W;
    const key = `engawa|${seed}|${colourKey(sc)}`, floorY = H * 0.78, gx = W * (portrait ? 0.1 : 0.34), gw = W * (portrait ? 0.8 : 0.62), gy = H * 0.1, gh = floorY - H * 0.14;
    ctx.globalAlpha = e;
    const scene = layer(env, `${key}|scene`, 0.6, (x, w, h) => {
      x.fillStyle = tone(mix(sc.bg, '#F6E4C8', 0.4)); x.fillRect(0, 0, w, h);
      /* the garden through the open shoji */
      x.save(); x.beginPath(); x.rect(gx, gy, gw, gh); x.clip();
      x.fillStyle = vertical(x, gy, gy + gh, [[0, dark ? '#1B2B5C' : '#A9DCFF'], [0.6, dark ? '#3A3A70' : '#E5F5FF'], [1, tone('#BFE6B6')]]); x.fillRect(gx, gy, gw, gh);
      glowDisc(x, gx + gw * 0.8, gy + gh * 0.2, U * 0.3, dark ? '#FFF1B8' : '#FFFFFF', dark ? 0.3 : 0.6);
      x.fillStyle = tone('#A8DDA0'); x.beginPath(); x.moveTo(gx, gy + gh); x.lineTo(gx, gy + gh * 0.72); for (let i = 0; i <= 8; i++) x.lineTo(gx + gw * i / 8, gy + gh * (0.68 + 0.05 * Math.sin(i * 1.7 + J.r(seed, 2) * 4))); x.lineTo(gx + gw, gy + gh); x.fill();
      for (let i = 0; i < 9; i++) { const hx = gx + gw * (0.05 + 0.11 * i + 0.03 * J.r(seed, i, 3)), hy = gy + gh * (0.78 + 0.1 * J.r(seed, i, 4)), r = U * (0.045 + 0.03 * J.r(seed, i, 5)), c = [sc.ghostB, sc.accent, sc.accent2, '#C7A8FF'][i % 4]; x.fillStyle = tone(mix('#8FCF9A', sc.bg, 0.1)); x.beginPath(); x.arc(hx, hy, r, 0, TAU); x.fill(); for (let k = 0; k < 7; k++) { x.fillStyle = tone(mix(c, '#FFFFFF', 0.3)); x.beginPath(); x.arc(hx + (J.r(seed, i, k, 6) - 0.5) * r * 1.5, hy - r * 0.3 - J.r(seed, i, k, 7) * r * 0.6, r * 0.3, 0, TAU); x.fill(); } }
      x.restore();
      /* shoji frames: paper panels with a fine grid, left one closed */
      const pw = gx - U * 0.02;
      x.fillStyle = tone('#FFF9EE'); x.fillRect(0, 0, Math.max(0, pw), floorY);
      x.strokeStyle = tone('#B98A5E'); x.lineWidth = Math.max(1.4, U * 0.004);
      for (let c = 0; c <= 3; c++) { const px = pw * c / 3; x.beginPath(); x.moveTo(px, 0); x.lineTo(px, floorY); x.stroke(); }
      for (let r = 0; r <= 6; r++) { const py = floorY * r / 6; x.beginPath(); x.moveTo(0, py); x.lineTo(pw, py); x.stroke(); }
      x.strokeStyle = tone('#A87A50'); x.lineWidth = Math.max(2, U * 0.008); x.strokeRect(gx, gy, gw, gh); x.beginPath(); x.moveTo(gx + gw * 0.5, gy); x.lineTo(gx + gw * 0.5, gy + gh); x.stroke();
      x.fillRect(0, gy - U * 0.02, w, U * 0.02);
      /* engawa boards */
      x.fillStyle = tone(mix('#E8C48F', sc.bg, 0.15)); x.fillRect(0, floorY, w, h - floorY);
      x.strokeStyle = rgba('#8A5E38', 0.22); x.lineWidth = Math.max(1, U * 0.003);
      for (let i = 1; i < 6; i++) { const y = floorY + (h - floorY) * (i / 6) ** 1.3; x.beginPath(); x.moveTo(0, y); x.lineTo(w, y); x.stroke(); }
      x.fillStyle = rgba('#FFFFFF', dark ? 0.06 : 0.3); x.fillRect(0, floorY, w, U * 0.008);
      /* a plate of dango and a cushion */
      const dx = w * (portrait ? 0.24 : 0.5), dy = floorY + U * 0.05; x.fillStyle = tone('#F4F1F7'); x.beginPath(); x.ellipse(dx, dy, U * 0.06, U * 0.016, 0, 0, TAU); x.fill();
      ['#FFB6CE', '#FFFFFF', '#A8E0A8'].forEach((c, i) => { x.fillStyle = tone(c); x.beginPath(); x.arc(dx - U * 0.03 + i * U * 0.03, dy - U * 0.016, U * 0.016, 0, TAU); x.fill(); });
      const cx0 = w * (portrait ? 0.68 : 0.8), cy0 = floorY + U * 0.07; x.fillStyle = tone(mix('#F2705E', sc.accent, 0.3)); x.beginPath(); x.ellipse(cx0, cy0, U * 0.17, U * 0.038, 0, 0, TAU); x.fill(); x.fillStyle = rgba('#FFFFFF', 0.35); x.beginPath(); x.ellipse(cx0, cy0 - U * 0.008, U * 0.14, U * 0.022, 0, Math.PI, TAU); x.fill();
    });
    ctx.drawImage(scene, 0, 0, W, H);
    /* sunlight through the shoji grid falling across the boards, breathing */
    ctx.save(); ctx.globalAlpha = e * (dark ? 0.18 : 0.3 + 0.06 * Math.sin(t * 0.6)); ctx.fillStyle = dark ? '#FFD9A0' : '#FFFBEA';
    for (let i = 0; i < 4; i++) { ctx.beginPath(); ctx.moveTo(gx + gw * (0.05 + i * 0.24), floorY); ctx.lineTo(gx + gw * (0.19 + i * 0.24), floorY); ctx.lineTo(gx + gw * (0.05 + i * 0.24) - U * 0.2, H); ctx.lineTo(gx + gw * (-0.09 + i * 0.24) - U * 0.2, H); ctx.fill(); } ctx.restore();
    ctx.globalAlpha = e;
    /* wind chime swaying under the eave */
    { const fx = gx + gw * (portrait ? 0.7 : 0.15), fy = gy + U * 0.01, sw = Math.sin(t * 1.3 + seed) * U * 0.012;
      ctx.strokeStyle = rgba(tone('#7A5A52'), 0.8); ctx.lineWidth = Math.max(1, U * 0.002); ctx.beginPath(); ctx.moveTo(fx, fy); ctx.lineTo(fx + sw, fy + U * 0.06); ctx.stroke();
      const bx = fx + sw, by = fy + U * 0.08; const g = ctx.createRadialGradient(bx - U * 0.006, by - U * 0.01, U * 0.004, bx, by, U * 0.03); g.addColorStop(0, 'rgba(230,246,255,0.95)'); g.addColorStop(1, 'rgba(150,210,245,0.85)');
      ctx.fillStyle = g; ctx.beginPath(); ctx.arc(bx, by, U * 0.026, Math.PI, 0); ctx.lineTo(bx + U * 0.028, by + U * 0.008); ctx.lineTo(bx - U * 0.028, by + U * 0.008); ctx.fill();
      ctx.strokeStyle = rgba(sc.accent, 0.9); ctx.beginPath(); ctx.moveTo(bx, by + U * 0.008); ctx.lineTo(bx + sw * 0.4, by + U * 0.055); ctx.stroke();
      ctx.fillStyle = rgba(sc.accent2, 0.95); ctx.save(); ctx.translate(bx + sw * 0.4, by + U * 0.055); ctx.rotate(sw * 0.02 + Math.sin(t * 1.9) * 0.15); ctx.fillRect(-U * 0.012, 0, U * 0.024, U * 0.05); ctx.restore(); }
    /* the cats: one asleep on the cushion, one watching the garden */
    cat(ctx, W * (portrait ? 0.68 : 0.8), H * 0.78 + U * 0.075, U * 0.2, { coat: FUR[1], mode: 'loaf', asleep: true, t, phase: 0.6, marks: '#E58E43', line: '#7A4B4B' });
    cat(ctx, W * (portrait ? 0.38 : 0.62), H * 0.78 + U * 0.09, U * 0.22, { coat: FUR[0], t, phase: 2.1, look: 0.7, marks: '#F4C79A', flip: false });
    for (let i = 0; i < 10; i++) {
      const y = wrap(J.r(seed, i, 71) * H * 1.2 + t * H * (0.03 + 0.04 * J.r(seed, i, 72)), H * 1.2) - H * 0.1, x = wrap(J.r(seed, i, 73) * W + Math.sin(t * 0.6 + i) * U * 0.05, W);
      ctx.save(); ctx.translate(x, y); ctx.rotate(t * 0.5 + i); ctx.fillStyle = rgba(tone('#FFC4D6'), 0.8); petal(ctx, 0, 0, U * (0.007 + 0.007 * J.r(seed, i, 75))); ctx.fill(); ctx.restore();
    }
    if (dark) { ctx.globalCompositeOperation = 'screen'; for (let i = 0; i < 12; i++) { const b = Math.pow(0.5 + 0.5 * Math.sin(t * (1 + J.r(seed, i, 81)) + i * 2), 2); glowDisc(ctx, W * J.r(seed, i, 82) + Math.sin(t * 0.4 + i) * U * 0.03, H * (0.3 + 0.4 * J.r(seed, i, 83)) + Math.cos(t * 0.5 + i) * U * 0.03, U * 0.014, '#FFF3A0', 0.7 * b); } ctx.globalCompositeOperation = 'source-over'; }
  });

  /* ================= 月夜の屋根 ================= */
  K.registerBg('echoRoofCats', '月夜の屋根', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.9), dark = isDark(sc.bg), portrait = H > W;
    const key = `roofcats|${seed}|${colourKey(sc)}`, ridge = H * 0.7, spark = dark ? ['#FFFFFF', sc.accent, sc.accent2] : hues(sc);
    const sil = dark ? mix(sc.bg, '#000000', 0.55) : mix(sc.fg, sc.bg, 0.15);
    const mx = W * (portrait ? 0.5 : 0.7), my = H * (portrait ? 0.3 : 0.32), mr = U * 0.2;
    ctx.globalAlpha = e;
    const scene = layer(env, `${key}|scene`, 0.6, (x, w, h) => {
      x.fillStyle = vertical(x, 0, h, [[0, dark ? mix(sc.bg, '#000000', 0.3) : mix(sc.bg, '#FFFFFF', 0.3)], [0.6, sc.bg], [1, mix(sc.bg, sc.accent, dark ? 0.3 : 0.2)]]); x.fillRect(0, 0, w, h);
      for (let i = 0; i < 110; i++) { x.fillStyle = rgba(spark[i % spark.length], dark ? 0.25 + 0.55 * J.r(seed, i, 12) : 0.3); x.beginPath(); x.arc(w * J.r(seed, i, 10), h * J.r(seed, i, 11) * 0.62, U * (0.0015 + 0.0028 * J.r(seed, i, 13)), 0, TAU); x.fill(); }
      glowDisc(x, mx, my, mr * 3.4, dark ? '#FFF1B8' : sc.accent2, dark ? 0.36 : 0.3);
      const g = x.createRadialGradient(mx - mr * 0.3, my - mr * 0.3, mr * 0.1, mx, my, mr); g.addColorStop(0, dark ? '#FFFCEA' : '#FFFDF0'); g.addColorStop(1, dark ? '#FFE9A6' : mix('#FFE9A6', sc.accent2, 0.4)); x.fillStyle = g; x.beginPath(); x.arc(mx, my, mr, 0, TAU); x.fill();
      /* tiled roof: rows of overlapping scallops under a ridge cap */
      const tw = U * 0.06; x.fillStyle = sil; x.fillRect(0, ridge, w, h - ridge);
      for (let row = 0; row < 8; row++) { const y = ridge + U * 0.012 + row * tw * 0.42; for (let c = -1; c < w / tw + 1; c++) { const cx = c * tw + (row % 2 ? tw / 2 : 0); x.fillStyle = mix(sil, dark ? '#3A3F6A' : sc.accent, 0.1 + 0.05 * row); x.beginPath(); x.arc(cx, y, tw * 0.52, 0, Math.PI); x.fill(); x.strokeStyle = rgba(dark ? '#8FA0E0' : '#FFFFFF', 0.13); x.lineWidth = Math.max(1, U * 0.002); x.beginPath(); x.arc(cx, y, tw * 0.52, 0.1, Math.PI - 0.1); x.stroke(); } }
      x.fillStyle = mix(sil, '#000000', 0.15); x.fillRect(0, ridge - U * 0.02, w, U * 0.026); x.fillStyle = rgba(dark ? '#FFF6D0' : '#FFFFFF', 0.25); x.fillRect(0, ridge - U * 0.02, w, U * 0.004);
      for (let c = 0; c < w / (U * 0.09) + 1; c++) { x.fillStyle = mix(sil, '#000000', 0.1); x.beginPath(); x.arc(c * U * 0.09, ridge - U * 0.02, U * 0.026, Math.PI, 0); x.fill(); }
    });
    ctx.drawImage(scene, 0, 0, W, H);
    /* fish-shaped clouds drifting and twinkles */
    for (let i = 0; i < 3; i++) {
      const cw = U * (0.16 + 0.08 * J.r(seed, i, 21)), cx = wrap(J.r(seed, i, 22) * W * 1.4 + t * U * (0.01 + 0.006 * i), W * 1.4 + cw) - cw * 0.5, cy = H * (0.1 + 0.32 * J.r(seed, i, 24));
      ctx.fillStyle = rgba(dark ? mix(sc.bg, '#FFFFFF', 0.3) : '#FFFFFF', dark ? 0.35 : 0.7); puff(ctx, cx, cy, cw, cw * 0.4); ctx.fill(); ctx.beginPath(); ctx.moveTo(cx + cw * 0.5, cy - cw * 0.08); ctx.lineTo(cx + cw * 0.78, cy - cw * 0.24); ctx.lineTo(cx + cw * 0.78, cy + cw * 0.06); ctx.closePath(); ctx.fill();
    }
    ctx.globalCompositeOperation = dark ? 'screen' : 'source-over';
    for (let i = 0; i < 16; i++) { const b = Math.pow(0.5 + 0.5 * Math.sin(t * (0.9 + 1.4 * J.r(seed, i, 41)) + i * 3.1), 2); ctx.fillStyle = rgba(spark[i % spark.length], (dark ? 0.4 : 0.5) + 0.5 * b); sparkle(ctx, W * J.r(seed, i, 43), H * J.r(seed, i, 44) * 0.6, U * (0.008 + 0.014 * J.r(seed, i, 42)) * (0.6 + b)); ctx.fill(); }
    ctx.globalCompositeOperation = 'source-over';
    /* cats on the ridge: one gazing at the moon, one loafing, one with its tail up, a tiny kitten */
    const cy0 = ridge - U * 0.006;
    [[0.16, 0.24, 'sit', 0, 0.3, false], [0.34, 0.15, 'loaf', 1.3, 0, true], [0.84, 0.21, 'sit', 2.4, -0.5, true], [0.6, 0.11, 'sit', 3.3, 0.4, false]].forEach(([ux, hs, mode, ph, look, flip], i) => {
      if (portrait && ux > 0.8) return;
      cat(ctx, W * ux, cy0, U * hs, { mode, sil, t, phase: ph, look, flip, alpha: 1 });
    });
  });

  /* in front: a trail of paw prints padding across the frame, and a few hearts drifting up */
  const paw = (ctx, x, y, s, rot) => {
    ctx.save(); ctx.translate(x, y); ctx.rotate(rot);
    ctx.beginPath(); ctx.moveTo(0, s * 0.05); ctx.bezierCurveTo(-s * 0.6, s * 0.05, -s * 0.5, s * 0.75, 0, s * 0.62); ctx.bezierCurveTo(s * 0.5, s * 0.75, s * 0.6, s * 0.05, 0, s * 0.05); ctx.fill();
    for (const [dx, dy, r] of [[-0.62, -0.18, 0.2], [-0.22, -0.55, 0.22], [0.22, -0.55, 0.22], [0.62, -0.18, 0.2]]) { ctx.beginPath(); ctx.ellipse(dx * s, dy * s, r * s, r * s * 1.15, dx * 0.5, 0, TAU); ctx.fill(); }
    ctx.restore();
  };
  K.shapes.paw = paw;
  K.registerFront('echoNekoPaws', '肉球', (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, U = Umin(env), seed = P.seed || 1, e = fade(env, 1), dark = isDark(sc.bg);
    const tint = dark ? mix(sc.accent, '#FFFFFF', 0.25) : sc.accent, cyc = 11, ph = wrap(t + J.r(seed, 4) * cyc, cyc), n = Math.floor((t + J.r(seed, 4) * cyc) / cyc);
    const x0 = W * (0.05 + 0.2 * J.r(seed, n, 1)), y0 = H * (0.75 + 0.15 * J.r(seed, n, 2)), x1 = W * (0.75 + 0.2 * J.r(seed, n, 3)), y1 = H * (0.1 + 0.2 * J.r(seed, n, 4));
    const dx = x1 - x0, dy = y1 - y0, len = Math.hypot(dx, dy), ux = dx / len, uy = dy / len, rot = Math.atan2(dy, dx) + Math.PI / 2, steps = 16;
    ctx.globalAlpha = e;
    for (let i = 0; i < steps; i++) {
      const born = i * 0.5, age = ph - born;
      if (age < 0 || age > 4.2) continue;
      const a = Math.min(1, age / 0.25) * (1 - clamp((age - 2.6) / 1.6)), side = i % 2 ? 1 : -1, u = i / (steps - 1);
      const wob = Math.sin(u * 6 + seed) * U * 0.02, px = x0 + dx * u + -uy * (side * U * 0.018 + wob), py = y0 + dy * u + ux * (side * U * 0.018 + wob);
      ctx.fillStyle = rgba(tint, 0.42 * a); paw(ctx, px, py, U * 0.02, rot + side * 0.15);
    }
    for (let i = 0; i < 4; i++) {
      const y = wrap(J.r(seed, i, 21) * H * 1.2 - t * H * (0.03 + 0.03 * J.r(seed, i, 22)), H * 1.2) - H * 0.1, x = J.r(seed, i, 23) * W + Math.sin(t * 0.8 + i) * U * 0.03;
      ctx.fillStyle = rgba(i % 2 ? sc.accent2 : tint, 0.5); heart(ctx, x, y, U * (0.008 + 0.008 * J.r(seed, i, 24))); ctx.fill();
    }
    if (K.luxe) K.luxe(ctx, env, { seed, tint: dark ? ['#FFD3E0', sc.accent2, '#FFE9B0'] : [sc.accent, '#FFE3A8', '#FFFFFF'], strength: 0.85, bokeh: 5, glitter: 8 });
  });
})();
