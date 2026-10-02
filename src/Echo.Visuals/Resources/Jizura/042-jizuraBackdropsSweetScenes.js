/* ECHO first-party backdrops, more sweet scenes: 放課後 (rooftop, cherry-blossom road) and 星のまほう (moon over a cloud sea,
   soap-bubble dream). Same rules as the rest: static art in cached layers, only what lives is redrawn. */
(() => {
  'use strict';
  const K = J.echo, E = K.E, TAU = K.TAU, clamp = K.clamp, mix = K.mix, rgba = K.rgba;
  const { U: Umin, isDark, wrap, hues, fade, vertical, glowDisc, star, layer, scroll, shapes } = K;
  const { heart, petal, sakura, puff, sparkle, roundRect } = shapes;
  const colourKey = sc => [sc.bg, sc.fg, sc.accent, sc.accent2, sc.ghostA, sc.ghostB].join('');

  /* ================= 放課後の屋上 ================= */
  K.registerBg('echoRooftop', '放課後の屋上', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), dark = isDark(sc.bg);
    const key = `rooftop|${seed}|${colourKey(sc)}`, horizon = H * 0.68;
    ctx.globalAlpha = e;
    const top = dark ? mix(sc.bg, '#1B2455', 0.65) : mix(sc.bg, '#9FD3FF', 0.45), mid = dark ? mix(sc.bg, sc.accent, 0.3) : mix(sc.bg, '#FFFFFF', 0.25), low = dark ? mix(sc.accent, '#FFB88A', 0.5) : mix('#FFC9A0', sc.accent, 0.4);
    const scene = layer(env, `${key}|scene`, 0.6, (x, w, h) => {
      x.fillStyle = vertical(x, 0, horizon, [[0, top], [0.55, mid], [1, low]]); x.fillRect(0, 0, w, horizon + 2);
      const sx = w * (0.28 + 0.4 * J.r(seed, 1)), sy = horizon - U * 0.1;
      glowDisc(x, sx, sy, U * 0.7, dark ? '#FFE9B0' : '#FFF1C0', dark ? 0.35 : 0.6);
      x.fillStyle = dark ? '#FFF6D8' : '#FFFBE6'; x.beginPath(); x.arc(sx, sy, U * (dark ? 0.06 : 0.085), 0, TAU); x.fill();
      /* town in soft silhouette, school building with a clock tower on the left */
      const townColour = dark ? mix(top, '#000000', 0.25) : mix(low, sc.accent, 0.35);
      x.fillStyle = rgba(townColour, dark ? 0.95 : 0.7);
      let bx = -U * 0.05, i = 0;
      while (bx < w + U * 0.1) { const bw = U * (0.05 + 0.08 * J.r(seed, i, 2)), bh = U * (0.04 + 0.13 * Math.pow(J.r(seed, i, 3), 1.4)); x.fillRect(bx, horizon - bh, bw + 1, bh + 2); if (dark) { x.fillStyle = 'rgba(255,225,150,0.85)'; for (let wy = horizon - bh + U * 0.012; wy < horizon - U * 0.01; wy += U * 0.022) for (let wx = bx + U * 0.008; wx < bx + bw - U * 0.01; wx += U * 0.02) if (J.r(seed, i, wy | 0, wx | 0) > 0.55) x.fillRect(wx, wy, U * 0.008, U * 0.011); x.fillStyle = rgba(townColour, 0.95); } bx += bw + U * 0.004; i++; }
      const sw = U * 0.3, sxl = w * 0.04, sh = U * 0.11;
      x.fillStyle = mix(townColour, sc.bg, dark ? 0.1 : 0.3); x.fillRect(sxl, horizon - sh, sw, sh + 2); x.fillRect(sxl + sw * 0.4, horizon - sh - U * 0.07, sw * 0.2, U * 0.07 + 2);
      x.beginPath(); x.moveTo(sxl + sw * 0.38, horizon - sh - U * 0.07); x.lineTo(sxl + sw * 0.5, horizon - sh - U * 0.11); x.lineTo(sxl + sw * 0.62, horizon - sh - U * 0.07); x.fill();
      x.fillStyle = '#FFF6E0'; x.beginPath(); x.arc(sxl + sw * 0.5, horizon - sh - U * 0.035, U * 0.014, 0, TAU); x.fill(); x.strokeStyle = '#6B4B4B'; x.lineWidth = Math.max(1, U * 0.0016); x.beginPath(); x.moveTo(sxl + sw * 0.5, horizon - sh - U * 0.035); x.lineTo(sxl + sw * 0.5, horizon - sh - U * 0.046); x.moveTo(sxl + sw * 0.5, horizon - sh - U * 0.035); x.lineTo(sxl + sw * 0.51 + U * 0.006, horizon - sh - U * 0.03); x.stroke();
      x.fillStyle = rgba('#FFFFFF', dark ? 0.35 : 0.7); for (let wi = 0; wi < 6; wi++) x.fillRect(sxl + sw * (0.06 + wi * 0.15), horizon - sh * 0.75, sw * 0.09, sh * 0.35);
      /* the roof itself: pale tiles, a water tank and the fence */
      const floor = vertical(x, horizon, h, [[0, mix(sc.bg, '#FBE4EA', dark ? 0.15 : 0.5)], [1, mix(sc.bg, '#F0BFD0', dark ? 0.3 : 0.4)]]);
      x.fillStyle = floor; x.fillRect(0, horizon, w, h - horizon);
      x.strokeStyle = rgba('#FFFFFF', dark ? 0.12 : 0.4); x.lineWidth = Math.max(1, U * 0.002);
      for (let r = 1; r < 6; r++) { const y = horizon + (h - horizon) * (r / 6) ** 1.4; x.beginPath(); x.moveTo(0, y); x.lineTo(w, y); x.stroke(); }
      for (let c = -10; c <= 10; c++) { x.beginPath(); x.moveTo(w * 0.5 + c * U * 0.06, horizon); x.lineTo(w * 0.5 + c * U * 0.45, h); x.stroke(); }
      x.fillStyle = mix('#C9D2E2', sc.bg, dark ? 0.55 : 0.3); const tx = w * 0.84; x.fillRect(tx, horizon - U * 0.12, U * 0.09, U * 0.12); x.beginPath(); x.moveTo(tx - U * 0.008, horizon - U * 0.12); x.lineTo(tx + U * 0.045, horizon - U * 0.16); x.lineTo(tx + U * 0.098, horizon - U * 0.12); x.fill();
      x.fillRect(tx + U * 0.005, horizon - U * 0.02, U * 0.008, U * 0.02); x.fillRect(tx + U * 0.075, horizon - U * 0.02, U * 0.008, U * 0.02);
      const fence = h * 0.9, fenceTop = horizon - U * 0.005;
      x.strokeStyle = rgba(mix(sc.fg, sc.bg, 0.3), dark ? 0.35 : 0.42); x.lineWidth = Math.max(1, U * 0.0022);
      for (let i2 = -60; i2 < 60; i2++) { x.beginPath(); x.moveTo(i2 * U * 0.04, fenceTop); x.lineTo(i2 * U * 0.04 + (fence - fenceTop), fence); x.moveTo(i2 * U * 0.04 + (fence - fenceTop), fenceTop); x.lineTo(i2 * U * 0.04, fence); x.stroke(); }
      x.globalCompositeOperation = 'destination-out'; x.fillStyle = 'rgba(0,0,0,1)'; x.fillRect(0, fenceTop, w, 0);
      x.globalCompositeOperation = 'source-over';
    });
    ctx.drawImage(scene, 0, 0, W, H);
    /* fence rails and posts on top, drawn as solid bars */
    ctx.fillStyle = rgba(mix(sc.fg, sc.bg, 0.35), dark ? 0.55 : 0.6);
    ctx.fillRect(0, H * 0.9, W, U * 0.008); ctx.fillRect(0, horizon - U * 0.008, W, U * 0.008);
    for (let i = 0; i < 9; i++) ctx.fillRect(W * (i / 8) - U * 0.004, horizon - U * 0.008, U * 0.008, H * 0.9 - horizon + U * 0.016);
    /* clouds, a paper plane, birds */
    for (let i = 0; i < 5; i++) {
      const cw = U * (0.24 + 0.24 * J.r(seed, i, 21)), cx = wrap(J.r(seed, i, 22) * W * 1.4 + t * U * (0.008 + 0.008 * J.r(seed, i, 23)), W * 1.4 + cw) - cw * 0.5, cy = horizon * (0.16 + 0.5 * J.r(seed, i, 24));
      K.puffCloud(ctx, cx, cy, cw, cw * 0.48, dark ? mix(top, '#FFFFFF', 0.25) : '#FFFFFF', dark ? 0.7 : 0.95, J.h(seed, i, 25), 1, dark);
    }
    {
      const cyc = 7, ph = wrap(t + J.r(seed, 5) * cyc, cyc) / cyc, px = -U * 0.1 + (W + U * 0.2) * ph, py = H * 0.52 - Math.sin(ph * Math.PI) * H * 0.3 + Math.sin(t * 2.4) * U * 0.012;
      ctx.save(); ctx.translate(px, py); ctx.rotate(-0.25 + Math.cos(ph * Math.PI) * -0.35 + Math.sin(t * 2.4) * 0.08);
      ctx.fillStyle = '#FFFFFF'; ctx.beginPath(); ctx.moveTo(U * 0.04, 0); ctx.lineTo(-U * 0.035, -U * 0.02); ctx.lineTo(-U * 0.02, 0); ctx.closePath(); ctx.fill();
      ctx.fillStyle = mix('#DDE6F5', sc.accent, 0.2); ctx.beginPath(); ctx.moveTo(U * 0.04, 0); ctx.lineTo(-U * 0.02, 0); ctx.lineTo(-U * 0.03, U * 0.017); ctx.closePath(); ctx.fill(); ctx.restore();
    }
    ctx.strokeStyle = rgba(dark ? '#1B2455' : mix(sc.fg, sc.bg, 0.2), 0.7); ctx.lineWidth = Math.max(1.4, U * 0.003); ctx.lineCap = 'round';
    for (let i = 0; i < 4; i++) {
      const bx = wrap(t * U * 0.05 + i * U * 0.09 + J.r(seed, i, 31) * W, W + 60) - 30, by = H * (0.16 + 0.08 * J.r(seed, i, 32)) + Math.sin(t * 0.8 + i) * U * 0.02, flap = Math.sin(t * 6 + i * 2) * U * 0.008;
      ctx.beginPath(); ctx.moveTo(bx - U * 0.014, by + flap); ctx.quadraticCurveTo(bx - U * 0.006, by - U * 0.008, bx, by); ctx.quadraticCurveTo(bx + U * 0.006, by - U * 0.008, bx + U * 0.014, by + flap); ctx.stroke();
    }
    /* a red and a pink balloon drifting up behind the fence */
    for (let i = 0; i < 2; i++) {
      const by = H * 0.96 - wrap(t * U * 0.02 + i * H * 0.5, H * 1.1), bx = W * (i ? 0.66 : 0.24) + Math.sin(t * 0.6 + i * 2) * U * 0.03, r = U * 0.026, c = i ? sc.accent2 : sc.accent;
      ctx.strokeStyle = rgba('#FFFFFF', 0.5); ctx.lineWidth = Math.max(1, U * 0.0015); ctx.beginPath(); ctx.moveTo(bx, by + r * 1.2); ctx.quadraticCurveTo(bx + r * 0.4, by + r * 2.2, bx - r * 0.2, by + r * 3.2); ctx.stroke();
      ctx.fillStyle = rgba(c, 0.95); ctx.beginPath(); ctx.ellipse(bx, by, r * 0.85, r, 0, 0, TAU); ctx.fill(); ctx.beginPath(); ctx.moveTo(bx, by + r); ctx.lineTo(bx - r * 0.18, by + r * 1.25); ctx.lineTo(bx + r * 0.18, by + r * 1.25); ctx.fill();
      ctx.fillStyle = rgba('#FFFFFF', 0.55); ctx.beginPath(); ctx.ellipse(bx - r * 0.3, by - r * 0.35, r * 0.15, r * 0.28, -0.4, 0, TAU); ctx.fill();
    }
  });

  /* ================= 桜並木の校道 ================= */
  K.registerBg('echoSakuraRoad', '桜並木', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), dark = isDark(sc.bg);
    const key = `road|${seed}|${colourKey(sc)}`, vx = W * 0.5, vy = H * 0.5, tone = c => (dark ? mix(c, mix(sc.bg, '#0F1A38', 0.5), 0.42) : c);
    ctx.globalAlpha = e;
    const back = layer(env, `${key}|back`, 0.5, (x, w, h) => {
      x.fillStyle = vertical(x, 0, h, [[0, dark ? mix(sc.bg, '#12204A', 0.6) : mix(sc.bg, '#A6D9FF', 0.45)], [0.5, dark ? mix(sc.bg, '#3A2A5A', 0.4) : mix(sc.bg, '#FFFFFF', 0.4)], [1, dark ? mix(sc.bg, '#000000', 0.25) : mix(sc.bg, sc.accent, 0.2)]]); x.fillRect(0, 0, w, h);
      /* the road narrowing to the vanishing point, strewn with petals */
      const road = x.createLinearGradient(0, vy, 0, h); road.addColorStop(0, rgba(tone('#FFE6EE'), 0.9)); road.addColorStop(1, rgba(tone(mix('#FFD3E1', sc.accent, 0.25)), 0.98));
      x.fillStyle = road; x.beginPath(); x.moveTo(vx - U * 0.015, vy); x.lineTo(vx + U * 0.015, vy); x.lineTo(w * 1.15, h); x.lineTo(-w * 0.15, h); x.closePath(); x.fill();
      x.strokeStyle = rgba('#FFFFFF', dark ? 0.15 : 0.5); x.lineWidth = Math.max(1, U * 0.003); x.setLineDash([U * 0.03, U * 0.04]); x.beginPath(); x.moveTo(vx, vy); x.lineTo(vx, h); x.stroke(); x.setLineDash([]);
      for (let i = 0; i < 90; i++) { const d = Math.pow(J.r(seed, i, 1), 0.6), ry = vy + (h - vy) * d, half = (U * 0.015 + (w * 0.65 - U * 0.015) * d), rx = vx + (J.r(seed, i, 2) * 2 - 1) * half; x.fillStyle = rgba(tone('#FFB6CB'), 0.5); x.beginPath(); x.ellipse(rx, ry, U * (0.003 + 0.012 * d), U * (0.0018 + 0.006 * d), J.r(seed, i, 3) * 3, 0, TAU); x.fill(); }
      glowDisc(x, vx, vy, U * 0.5, dark ? '#FFD9A0' : '#FFFFFF', dark ? 0.35 : 0.7);
    });
    ctx.drawImage(back, 0, 0, W, H);
    /* trees stream toward the camera; canopies are clusters of round blossom clumps */
    const N = 9, speed = 0.028, order = [];
    for (let side = -1; side <= 1; side += 2) for (let i = 0; i < N; i++) order.push({ side, z: wrap(i / N + t * speed + (side > 0 ? 0.5 / N : 0), 1), i });
    order.sort((a, b) => a.z - b.z);
    for (const { side, z, i } of order) {
      const d = Math.pow(z, 2.1) + 0.02, x0 = vx + side * (U * 0.05 + (W * 0.5 - U * 0.02) * d * 1.25), base = vy + (H - vy) * d * 0.98, size = U * (0.03 + 0.95 * d), fadeIn = clamp(z / 0.08);
      ctx.globalAlpha = e * fadeIn;
      ctx.fillStyle = tone('#7A5A52'); ctx.beginPath(); ctx.moveTo(x0 - size * 0.05, base); ctx.lineTo(x0 - size * 0.035, base - size * 0.75); ctx.lineTo(x0 + size * 0.035, base - size * 0.75); ctx.lineTo(x0 + size * 0.06, base); ctx.fill();
      const cx = x0 - side * size * 0.12, cy = base - size * 0.9;
      for (let k = 0; k < 11; k++) {
        const a = J.r(seed, i + (side + 1) * 40, k, 4) * TAU, rr = Math.sqrt(J.r(seed, i + (side + 1) * 40, k, 5)) * size * 0.42, r = size * (0.16 + 0.12 * J.r(seed, i, k, 6));
        const px = cx + Math.cos(a) * rr * 1.3, py = cy + Math.sin(a) * rr * 0.75, shade = J.r(seed, i, k, 7);
        ctx.fillStyle = tone(mix('#FFD3E1', sc.accent, 0.1 + 0.4 * shade)); ctx.beginPath(); ctx.arc(px, py, r, 0, TAU); ctx.fill();
        ctx.fillStyle = rgba('#FFFFFF', 0.28); ctx.beginPath(); ctx.arc(px - r * 0.25, py - r * 0.3, r * 0.5, 0, TAU); ctx.fill();
      }
      if (dark && d > 0.35) { glowDisc(ctx, x0 + side * size * 0.22, base - size * 0.32, size * 0.16, '#FFD9A0', 0.55); }
    }
    ctx.globalAlpha = e;
    /* light shafts through the branches */
    ctx.globalCompositeOperation = dark ? 'screen' : 'source-over';
    for (let i = 0; i < 5; i++) { const x0 = W * (0.1 + 0.8 * J.r(seed, i, 61)), sw = U * (0.04 + 0.06 * J.r(seed, i, 62)), a = (0.05 + 0.04 * Math.sin(t * 0.5 + i * 2)) * (dark ? 0.7 : 1); const g = ctx.createLinearGradient(x0, 0, x0 - U * 0.2, H * 0.8); g.addColorStop(0, `rgba(255,250,230,${a})`); g.addColorStop(1, 'rgba(255,250,230,0)'); ctx.fillStyle = g; ctx.beginPath(); ctx.moveTo(x0, 0); ctx.lineTo(x0 + sw, 0); ctx.lineTo(x0 + sw - U * 0.2, H * 0.8); ctx.lineTo(x0 - U * 0.2, H * 0.8); ctx.fill(); }
    ctx.globalCompositeOperation = 'source-over';
    for (let i = 0; i < 26; i++) {
      const y = wrap(J.r(seed, i, 71) * H * 1.2 + t * H * (0.04 + 0.05 * J.r(seed, i, 72)), H * 1.2) - H * 0.1, x = wrap(J.r(seed, i, 73) * W + Math.sin(t * 0.6 + i) * U * 0.05 - t * U * 0.008, W + 40) - 20;
      ctx.save(); ctx.translate(x, y); ctx.rotate(t * (0.4 + J.r(seed, i, 74)) + i); ctx.scale(1, 0.55 + 0.45 * Math.abs(Math.sin(t * 0.9 + i)));
      ctx.fillStyle = rgba(tone('#FFC4D6'), 0.85); petal(ctx, 0, 0, U * (0.007 + 0.009 * J.r(seed, i, 75))); ctx.fill(); ctx.restore();
    }
  });

  /* ================= 月の雲海 ================= */
  K.registerBg('echoMoonSea', '月の雲海', ['calm', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.9), dark = isDark(sc.bg), portrait = H > W;
    const key = `moonsea|${seed}|${colourKey(sc)}`, spark = dark ? ['#FFFFFF', sc.accent, sc.accent2] : hues(sc);
    ctx.globalAlpha = e;
    const mx = W * (portrait ? 0.5 : 0.64), my = H * (portrait ? 0.34 : 0.4), mr = U * 0.26;
    const base = layer(env, `${key}|base`, 0.5, (x, w, h) => {
      x.fillStyle = vertical(x, 0, h, [[0, dark ? mix(sc.bg, '#000000', 0.3) : mix(sc.bg, '#FFFFFF', 0.35)], [0.6, sc.bg], [1, mix(sc.bg, sc.accent, dark ? 0.3 : 0.22)]]); x.fillRect(0, 0, w, h);
      for (let i = 0; i < 130; i++) { x.fillStyle = rgba(spark[i % spark.length], dark ? 0.25 + 0.55 * J.r(seed, i, 12) : 0.3); x.beginPath(); x.arc(w * J.r(seed, i, 10), h * J.r(seed, i, 11) * 0.7, U * (0.0015 + 0.0028 * J.r(seed, i, 13)), 0, TAU); x.fill(); }
      glowDisc(x, mx, my, mr * 3.2, dark ? '#FFF1B8' : sc.accent2, dark ? 0.32 : 0.28);
      const g = x.createRadialGradient(mx - mr * 0.3, my - mr * 0.3, mr * 0.1, mx, my, mr); g.addColorStop(0, dark ? '#FFFCEA' : '#FFFDF0'); g.addColorStop(1, dark ? '#FFE9A6' : mix('#FFE9A6', sc.accent2, 0.4));
      x.fillStyle = g; x.beginPath(); x.arc(mx, my, mr, 0, TAU); x.fill();
      x.fillStyle = rgba(dark ? '#E7C97A' : '#E9C476', 0.35); [[-0.3, -0.2, 0.16], [0.28, 0.12, 0.2], [-0.05, 0.4, 0.11], [0.35, -0.38, 0.09]].forEach(([dx, dy, r]) => { x.beginPath(); x.arc(mx + mr * dx, my + mr * dy, mr * r, 0, TAU); x.fill(); });
    });
    ctx.drawImage(base, 0, 0, W, H);
    /* floating islands, each with a round tree, a tiny house and a hanging star lantern */
    for (let i = 0; i < 3; i++) {
      const ix = W * (i === 0 ? 0.13 : i === 1 ? 0.88 : 0.36) * (portrait && i === 2 ? 1.2 : 1), iy = H * (i === 0 ? 0.3 : i === 1 ? 0.6 : 0.78) + Math.sin(t * 0.6 + i * 2.1) * U * 0.014, s = U * (i === 2 ? 0.05 : 0.08);
      if (portrait && ix > W * 0.9) continue;
      ctx.save(); ctx.translate(ix, iy);
      ctx.fillStyle = mix('#B48A72', sc.bg, dark ? 0.4 : 0.15); ctx.beginPath(); ctx.moveTo(-s * 1.2, 0); ctx.quadraticCurveTo(-s * 0.8, s * 1.6, 0, s * 2.1); ctx.quadraticCurveTo(s * 0.8, s * 1.6, s * 1.2, 0); ctx.fill();
      ctx.fillStyle = mix('#9EE0A8', sc.bg, dark ? 0.5 : 0.12); ctx.beginPath(); ctx.ellipse(0, 0, s * 1.25, s * 0.28, 0, 0, TAU); ctx.fill();
      ctx.fillStyle = mix('#8A5E48', sc.bg, 0.1); ctx.fillRect(-s * 0.55, -s * 0.5, s * 0.14, s * 0.55);
      ctx.fillStyle = mix('#FFB6CE', sc.accent, 0.15); ctx.beginPath(); ctx.arc(-s * 0.48, -s * 0.85, s * 0.5, 0, TAU); ctx.arc(-s * 0.85, -s * 0.55, s * 0.32, 0, TAU); ctx.arc(-s * 0.15, -s * 0.6, s * 0.34, 0, TAU); ctx.fill();
      if (i !== 2) { ctx.fillStyle = '#FFF4E8'; ctx.fillRect(s * 0.25, -s * 0.5, s * 0.6, s * 0.5); ctx.fillStyle = mix('#F08A98', sc.accent, 0.3); ctx.beginPath(); ctx.moveTo(s * 0.15, -s * 0.5); ctx.lineTo(s * 0.55, -s * 0.95); ctx.lineTo(s * 0.95, -s * 0.5); ctx.fill(); ctx.fillStyle = '#FFD86B'; ctx.fillRect(s * 0.45, -s * 0.34, s * 0.2, s * 0.22); }
      const sw = Math.sin(t * 1.2 + i) * s * 0.06; ctx.strokeStyle = rgba('#FFFFFF', 0.5); ctx.lineWidth = Math.max(1, U * 0.0015); ctx.beginPath(); ctx.moveTo(s * 0.9, -s * 0.2); ctx.quadraticCurveTo(s * 1.3, s * 0.3, s * 1.2 + sw, s * 0.7); ctx.stroke();
      ctx.globalCompositeOperation = dark ? 'screen' : 'source-over'; glowDisc(ctx, s * 1.2 + sw, s * 0.85, s * 0.7, '#FFE58A', 0.5); ctx.fillStyle = '#FFE58A'; sparkle(ctx, s * 1.2 + sw, s * 0.85, s * 0.22); ctx.fill(); ctx.globalCompositeOperation = 'source-over';
      ctx.restore();
    }
    /* three cloud seas at three speeds */
    const sea = (n, row, shade, alpha) => layer(env, `${key}|sea${n}`, 0.5, (x, w, h) => {
      for (let i = 0; i < 10; i++) { const cw = U * (0.36 + 0.3 * J.r(seed, n * 30 + i, 31)), cx0 = w * ((i + J.r(seed, n * 30 + i, 32) * 0.5) / 10), cy0 = h * (row + 0.03 * J.r(seed, n * 30 + i, 33)); for (const shift of [-w, 0, w]) { K.puffCloud(x, cx0 + shift, cy0, cw, cw * 0.55, shade, alpha, J.h(seed, n * 30 + i, 34), n % 2 ? 1 : -1, dark); } }
      x.fillStyle = rgba(shade, alpha); x.fillRect(0, h * row, w, h);
    });
    const c1 = sea(1, 0.8, mix(sc.bg, sc.accent, dark ? 0.45 : 0.3), 0.55), c2 = sea(2, 0.87, mix(sc.bg, '#FFFFFF', dark ? 0.3 : 0.55), 0.85), c3 = sea(3, 0.94, mix(sc.bg, dark ? sc.accent : sc.accent2, dark ? 0.55 : 0.3), 1);
    scroll(ctx, c1, env, t * U * 0.004, 1, -U * 0.02, 1); scroll(ctx, c2, env, -t * U * 0.008, 1, 0, 1); scroll(ctx, c3, env, t * U * 0.013, 1, U * 0.02, 1);
    ctx.globalCompositeOperation = dark ? 'screen' : 'source-over';
    for (let i = 0; i < 18; i++) { const b = Math.pow(0.5 + 0.5 * Math.sin(t * (0.9 + 1.4 * J.r(seed, i, 41)) + i * 3.1), 2); ctx.fillStyle = rgba(spark[i % spark.length], (dark ? 0.4 : 0.5) + 0.5 * b); sparkle(ctx, W * J.r(seed, i, 43), H * J.r(seed, i, 44) * 0.7, U * (0.008 + 0.014 * J.r(seed, i, 42)) * (0.6 + b)); ctx.fill(); }
    ctx.globalCompositeOperation = 'source-over';
  });

  /* ================= 夢のシャボン玉 ================= */
  K.registerBg('echoBubbleDream', '夢のシャボン玉', ['emotional', 'pop', 'calm'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), dark = isDark(sc.bg);
    const key = `bubble|${seed}|${colourKey(sc)}`, palette = dark ? [sc.accent, sc.accent2, '#9FE3F5', '#C7A8FF'] : [sc.accent, sc.accent2, sc.ghostB, sc.ghostA];
    ctx.globalAlpha = e;
    const back = layer(env, `${key}|back`, 0.5, (x, w, h) => {
      x.fillStyle = vertical(x, 0, h, [[0, dark ? mix(sc.bg, '#000000', 0.25) : mix(sc.bg, '#FFFFFF', 0.4)], [1, dark ? mix(sc.bg, sc.accent, 0.3) : mix(sc.bg, sc.accent, 0.25)]]); x.fillRect(0, 0, w, h);
      x.globalCompositeOperation = dark ? 'screen' : 'source-over';
      for (let i = 0; i < 12; i++) { const cx = w * J.r(seed, i, 1), cy = h * J.r(seed, i, 2), r = U * (0.16 + 0.28 * J.r(seed, i, 3)), c = palette[i % palette.length], g = x.createRadialGradient(cx, cy, 0, cx, cy, r); g.addColorStop(0, rgba(c, dark ? 0.24 : 0.28)); g.addColorStop(1, rgba(c, 0)); x.fillStyle = g; x.fillRect(cx - r, cy - r, r * 2, r * 2); }
      x.globalCompositeOperation = 'source-over';
      for (let i = 0; i < 30; i++) { x.fillStyle = rgba(palette[i % palette.length], dark ? 0.16 : 0.22); x.beginPath(); x.arc(w * J.r(seed, i, 11), h * J.r(seed, i, 12), U * (0.008 + 0.02 * J.r(seed, i, 13)), 0, TAU); x.fill(); }
    });
    ctx.drawImage(back, 0, 0, W, H);
    /* streamers that ribbon across the frame */
    ctx.lineCap = 'round';
    for (let k = 0; k < 3; k++) {
      const y0 = H * (0.2 + 0.3 * k), c = palette[k % palette.length], amp = U * (0.05 + 0.03 * k), ph = t * (0.4 + 0.15 * k) + k * 2;
      ctx.strokeStyle = rgba(c, dark ? 0.32 : 0.38); ctx.lineWidth = U * 0.016; ctx.beginPath();
      for (let i = 0; i <= 40; i++) { const u = i / 40, x = W * u, y = y0 + Math.sin(u * 5.5 + ph) * amp + Math.sin(u * 2.3 - ph * 0.7) * amp * 0.6; if (i) ctx.lineTo(x, y); else ctx.moveTo(x, y); }
      ctx.stroke(); ctx.strokeStyle = rgba('#FFFFFF', dark ? 0.2 : 0.4); ctx.lineWidth = U * 0.004; ctx.stroke();
    }
    /* candy hearts and stars drifting, then the glossy bubbles in three depths */
    for (let i = 0; i < 14; i++) {
      const x = wrap(J.r(seed, i, 21) * W + t * U * (0.006 + 0.01 * J.r(seed, i, 22)) * (i % 2 ? 1 : -1), W + 80) - 40, y = H * J.r(seed, i, 23) + Math.sin(t * 0.7 + i) * U * 0.03, s = U * (0.014 + 0.018 * J.r(seed, i, 24)), c = palette[i % palette.length];
      ctx.save(); ctx.translate(x, y); ctx.rotate(Math.sin(t * 0.5 + i) * 0.4); ctx.fillStyle = rgba(c, dark ? 0.7 : 0.75);
      if (i % 3 === 0) star(ctx, 0, 0, s * 1.2, 5, 0.45); else heart(ctx, 0, 0, s); ctx.fill(); ctx.fillStyle = rgba('#FFFFFF', 0.5); ctx.beginPath(); ctx.arc(-s * 0.3, -s * 0.3, s * 0.18, 0, TAU); ctx.fill(); ctx.restore();
    }
    for (let i = 0; i < 26; i++) {
      const depth = J.r(seed, i, 31), r = U * (0.02 + 0.075 * depth * depth), speed = U * (0.012 + 0.03 * (1 - depth)), y = H + r - wrap(J.r(seed, i, 32) * H * 1.3 + t * speed, H * 1.3 + r * 2) + r, x = J.r(seed, i, 33) * W + Math.sin(t * (0.5 + J.r(seed, i, 34)) + i * 2) * U * 0.04 * (0.5 + depth);
      const wob = 1 + Math.sin(t * 2 + i) * 0.03, c = palette[i % palette.length];
      ctx.save(); ctx.translate(x, y); ctx.scale(wob, 1 / wob);
      const g = ctx.createRadialGradient(0, 0, r * 0.55, 0, 0, r); g.addColorStop(0, rgba(c, 0.02)); g.addColorStop(0.8, rgba(c, dark ? 0.14 : 0.16)); g.addColorStop(1, rgba(mix(c, '#FFFFFF', 0.4), dark ? 0.55 : 0.6));
      ctx.fillStyle = g; ctx.beginPath(); ctx.arc(0, 0, r, 0, TAU); ctx.fill();
      ctx.strokeStyle = rgba(palette[(i + 1) % palette.length], 0.35); ctx.lineWidth = Math.max(1, r * 0.06); ctx.beginPath(); ctx.arc(0, 0, r * 0.94, Math.PI * 0.15, Math.PI * 0.75); ctx.stroke();
      ctx.strokeStyle = rgba('#FFFFFF', 0.85); ctx.lineWidth = Math.max(1.2, r * 0.07); ctx.beginPath(); ctx.arc(0, 0, r * 0.78, Math.PI * 1.08, Math.PI * 1.42); ctx.stroke();
      ctx.fillStyle = rgba('#FFFFFF', 0.85); ctx.beginPath(); ctx.arc(r * 0.45, -r * 0.5, r * 0.09, 0, TAU); ctx.fill();
      ctx.restore();
    }
  });
})();
