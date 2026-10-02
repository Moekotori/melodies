/* ECHO first-party backdrops for the default look 落霞: 海上の鳥居 (a torii standing in a burning sea), 踏切 (a level crossing at the last light),
   芒の原 (pampas grass, red dragonflies, a low gold sun). */
(() => {
  'use strict';
  const K = J.echo, TAU = K.TAU, clamp = K.clamp, mix = K.mix, rgba = K.rgba, paint = K.paint;
  const { U: Umin, isDark, wrap, glowDisc, star, vertical, fade, layer, scroll } = K;
  const r = J.r, ck = sc => [sc.bg, sc.fg, sc.accent, sc.accent2].join('');

  const duskTone = sc => {
    const dark = isDark(sc.bg);
    const sun = mix(sc.accent2, '#FFF0C0', 0.55);
    return dark
      ? { dark, sun, sky0: mix(sc.bg, '#07031A', 0.62), sky1: mix(sc.bg, sc.accent, 0.3), sky2: mix(sc.bg, sc.accent2, 0.62), sea0: mix(sc.bg, sc.accent2, 0.35), sea1: mix(sc.bg, '#07031A', 0.55), ink: mix(sc.bg, '#000000', 0.72), inkFar: mix(sc.bg, sc.accent, 0.35), cloud: mix(sc.accent, sc.accent2, 0.5), cloudLight: mix(sc.accent2, '#FFE6B0', 0.5), lamp: '#FFC873' }
      : { dark, sun, sky0: mix(sc.bg, '#8A5AB0', 0.38), sky1: mix(sc.bg, sc.accent2, 0.36), sky2: mix(sc.bg, '#FFF0B8', 0.6), sea0: mix(sc.bg, sc.accent2, 0.4), sea1: mix(sc.bg, sc.accent, 0.45), ink: mix(sc.fg, '#2A1030', 0.45), inkFar: mix(sc.bg, sc.accent, 0.4), cloud: mix(sc.bg, sc.accent, 0.4), cloudLight: '#FFF0C8', lamp: '#FFE7A0' };
  };
  const skyLayer = (env, key, tone, hz, U, sunX, sunY, stars) => layer(env, key, 0.5, (x, w, h) => {
    x.fillStyle = vertical(x, 0, hz, [[0, tone.sky0], [0.55, tone.sky1], [1, tone.sky2]]); x.fillRect(0, 0, w, hz + 2);
    if (stars && tone.dark) paint.stars(x, w, hz, 7, U, 90, 0.4, 0.8);
    glowDisc(x, sunX, sunY, U * 1.2, tone.sun, tone.dark ? 0.6 : 0.75); glowDisc(x, sunX, sunY, U * 0.4, '#FFFFFF', 0.45);
  });
  const sunDisc = (ctx, t, sunX, sunY, U, tone) => {
    ctx.fillStyle = rgba(tone.sun, 0.35); ctx.beginPath(); ctx.arc(sunX, sunY, U * 0.19 * (1 + 0.012 * Math.sin(t * 0.7)), 0, TAU); ctx.fill();
    ctx.fillStyle = rgba(mix(tone.sun, '#FFFFFF', 0.4), 0.98); ctx.beginPath(); ctx.arc(sunX, sunY, U * 0.135, 0, TAU); ctx.fill();
  };
  /* long streaks of cloud lit from below by the sun */
  const streaks = (x, w, h, y0, y1, seed, tone, U, count = 10) => {
    for (let i = 0; i < count; i++) {
      const cy = y0 + (y1 - y0) * (i / count) + (r(seed, i, 1) - 0.5) * U * 0.03, cw = U * (0.5 + 0.9 * r(seed, i, 2)), cx = w * r(seed, i, 3), ch = U * (0.012 + 0.03 * r(seed, i, 4));
      for (const shift of [-w, 0, w]) {
        const px = cx + shift; if (px + cw < -20 || px - cw > w + 20) continue;
        x.save(); x.translate(px, cy); x.scale(1, ch / cw);
        const g = x.createRadialGradient(0, 0, 0, 0, 0, cw / 2); g.addColorStop(0, rgba(tone.cloud, 0.75)); g.addColorStop(0.7, rgba(tone.cloud, 0.4)); g.addColorStop(1, rgba(tone.cloud, 0));
        x.fillStyle = g; x.beginPath(); x.arc(0, 0, cw / 2, 0, TAU); x.fill();
        x.translate(0, -cw * 0.09); const g2 = x.createRadialGradient(0, 0, 0, 0, 0, cw * 0.42); g2.addColorStop(0, rgba(tone.cloudLight, 0.8)); g2.addColorStop(1, rgba(tone.cloudLight, 0));
        x.fillStyle = g2; x.beginPath(); x.arc(0, 0, cw * 0.42, 0, TAU); x.fill(); x.restore();
      }
    }
  };
  const groundDusk = (x, w, h, y, U, colour) => { x.fillStyle = colour; x.fillRect(0, y, w, h - y + 2); };

  /* ================= 海上の鳥居 ================= */
  const torii = (x, cx, baseY, s, colour, rim) => {
    const pw = s * 0.06, gap = s * 0.36, top = baseY - s;
    x.fillStyle = colour;
    for (const side of [-1, 1]) { const px = cx + side * gap; x.beginPath(); x.moveTo(px - pw * 0.62, baseY); x.lineTo(px - pw * 0.5, top + s * 0.05); x.lineTo(px + pw * 0.5, top + s * 0.05); x.lineTo(px + pw * 0.62, baseY); x.closePath(); x.fill(); x.fillRect(px - pw * 0.85, baseY - s * 0.09, pw * 1.7, s * 0.03); }
    x.fillRect(cx - gap - pw * 0.7, baseY - s * 0.72, gap * 2 + pw * 1.4, s * 0.04);
    x.fillRect(cx - gap - pw * 0.6, top + s * 0.11, gap * 2 + pw * 1.2, s * 0.032);
    x.fillRect(cx - s * 0.035, top + s * 0.1, s * 0.07, s * 0.14);
    x.beginPath(); x.moveTo(cx - s * 0.68, top - s * 0.085); x.quadraticCurveTo(cx - s * 0.5, top + s * 0.03, cx, top + s * 0.03); x.quadraticCurveTo(cx + s * 0.5, top + s * 0.03, cx + s * 0.68, top - s * 0.085);
    x.lineTo(cx + s * 0.63, top + s * 0.005); x.quadraticCurveTo(cx + s * 0.5, top + s * 0.085, cx, top + s * 0.085); x.quadraticCurveTo(cx - s * 0.5, top + s * 0.085, cx - s * 0.63, top + s * 0.005); x.closePath(); x.fill();
    x.fillRect(cx - s * 0.6, top + s * 0.07, s * 1.2, s * 0.03);
    if (rim) { x.strokeStyle = rim; x.lineWidth = Math.max(1, s * 0.006); x.beginPath(); x.moveTo(cx - s * 0.68, top - s * 0.085); x.quadraticCurveTo(cx - s * 0.5, top + s * 0.03, cx, top + s * 0.03); x.quadraticCurveTo(cx + s * 0.5, top + s * 0.03, cx + s * 0.68, top - s * 0.085); x.stroke(); }
  };
  K.registerBg('echoTorii', '海上の鳥居', ['calm', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), tone = duskTone(sc), key = `to|${seed}|${ck(sc)}`, hz = H * 0.6, portrait = H > W;
    const sunX = W * (portrait ? 0.62 : 0.66), sunY = hz - U * 0.16;
    ctx.globalAlpha = e;
    ctx.drawImage(skyLayer(env, `${key}|sky`, tone, hz, U, sunX, sunY, true), 0, 0, W, H);
    ctx.drawImage(layer(env, `${key}|streak`, 0.4, (x, w, h) => streaks(x, w, h, H * 0.08, hz - U * 0.02, seed, tone, U, 11)), 0, 0, W, H);
    sunDisc(ctx, t, sunX, sunY, U, tone);
    ctx.drawImage(layer(env, `${key}|far`, 0.5, (x, w, h) => {
      paint.ridge(x, w, hz + 2, hz + 1, hz * 0.16, paint.profile(seed, 3, [[1, 0.4], [2, 0.3], [5, 0.2], [13, 0.1]]), top => vertical(x, top, hz, [[0, mix(tone.inkFar, tone.sky2, 0.4)], [1, mix(tone.inkFar, tone.sky2, 0.75)]]));
      paint.ridge(x, w, hz + 2, hz + 1, hz * 0.09, paint.profile(seed, 6, [[2, 0.4], [3, 0.3], [7, 0.2], [17, 0.1]]), mix(tone.inkFar, tone.ink, 0.35));
    }), 0, 0, W, H);
    const birds = [rgba(tone.ink, 0.7)]; paint.birds(ctx, t, W, H, U, seed, birds[0], 6, 0.2);
    /* sea */
    ctx.fillStyle = vertical(ctx, hz, H, [[0, mix(tone.sea0, tone.sun, 0.25)], [0.3, tone.sea0], [1, tone.sea1]]); ctx.fillRect(0, hz, W, H - hz);
    ctx.globalCompositeOperation = 'screen';
    for (let i = 0; i < 70; i++) {                                     /* the burning path */
      const v = i / 70, y = hz + (H - hz) * (0.005 + 0.95 * v * v), hw = U * (0.006 + 0.13 * v) * (0.4 + r(seed, i, 8)), b = 0.5 + 0.5 * Math.sin(t * (1 + r(seed, i, 9) * 1.5) + i * 2.3);
      ctx.fillStyle = rgba(mix(tone.sun, '#FFFFFF', 0.4), (0.34 - 0.16 * v) * b); ctx.fillRect(sunX - hw + (r(seed, i, 10) - 0.5) * U * 0.06, y, hw * 2, Math.max(1, U * (0.002 + 0.004 * v)));
    }
    ctx.globalCompositeOperation = 'source-over';
    ctx.lineCap = 'round';
    for (let i = 0; i < 60; i++) {
      const v = r(seed, i, 21), y = hz + (H - hz) * (0.02 + 0.96 * v * v), l = U * (0.03 + 0.18 * v), x0 = wrap(r(seed, i, 22) * W * 1.3 + t * U * (0.006 + 0.02 * v), W * 1.3) - W * 0.15;
      ctx.strokeStyle = rgba(tone.dark ? tone.sun : '#FFFFFF', (tone.dark ? 0.16 : 0.34) * (0.4 + v)); ctx.lineWidth = Math.max(1, U * (0.0014 + 0.003 * v));
      ctx.beginPath(); ctx.moveTo(x0, y); ctx.quadraticCurveTo(x0 + l / 2, y - U * 0.004 * (1 + v), x0 + l, y); ctx.stroke();
    }
    /* the gate, its reflection wobbling, ripples ringing its feet */
    const gx = W * (portrait ? 0.4 : 0.34), gy = hz + (H - hz) * 0.3, gs = U * (portrait ? 0.5 : 0.52), rim = rgba(tone.sun, tone.dark ? 0.5 : 0.7);
    ctx.save(); ctx.beginPath(); ctx.rect(0, gy, W, H - gy); ctx.clip();
    for (let k = 0; k < 4; k++) { ctx.save(); ctx.translate(Math.sin(t * 1.3 + k * 1.9) * U * 0.007 * (k + 1) * 0.6, gy * 2 + U * 0.006 * k); ctx.scale(1, -1); ctx.globalAlpha = e * 0.13; torii(ctx, gx, gy, gs, tone.ink, null); ctx.restore(); }
    ctx.restore(); ctx.globalAlpha = e;
    torii(ctx, gx, gy, gs, tone.ink, rim);
    ctx.strokeStyle = rgba('#FFFFFF', 0.4); ctx.lineWidth = Math.max(1, U * 0.002);
    for (const side of [-1, 1]) for (let k = 0; k < 3; k++) { const ph = wrap(t * 0.35 + k / 3 + side * 0.2, 1); ctx.globalAlpha = e * (1 - ph) * 0.8; ctx.beginPath(); ctx.ellipse(gx + side * gs * 0.36, gy + U * 0.004, gs * (0.04 + 0.13 * ph), gs * (0.008 + 0.028 * ph), 0, 0, TAU); ctx.stroke(); }
    ctx.globalAlpha = e;
    /* a far sail, a nearer swell */
    const bx = W * (portrait ? 0.78 : 0.82) + Math.sin(t * 0.1) * U * 0.02, by = hz + U * 0.012;
    ctx.fillStyle = mix(tone.ink, tone.inkFar, 0.4); ctx.beginPath(); ctx.moveTo(bx, by - U * 0.06); ctx.lineTo(bx + U * 0.025, by - U * 0.006); ctx.lineTo(bx, by - U * 0.006); ctx.fill(); ctx.beginPath(); ctx.moveTo(bx - U * 0.004, by - U * 0.05); ctx.lineTo(bx - U * 0.024, by - U * 0.008); ctx.lineTo(bx - U * 0.004, by - U * 0.008); ctx.fill();
    ctx.beginPath(); ctx.moveTo(bx - U * 0.03, by - U * 0.004); ctx.lineTo(bx + U * 0.03, by - U * 0.004); ctx.lineTo(bx + U * 0.02, by + U * 0.006); ctx.lineTo(bx - U * 0.022, by + U * 0.006); ctx.fill();
    ctx.fillStyle = mix(tone.sea1, tone.ink, 0.5);
    ctx.beginPath(); ctx.moveTo(0, H); for (let px = 0; px <= W + 8; px += 8) ctx.lineTo(px, H * 0.95 + Math.sin(px / (U * 0.16) + t * 0.6) * U * 0.011 + Math.sin(px / (U * 0.06) + t * 1.2) * U * 0.004); ctx.lineTo(W, H); ctx.fill();
  });

  /* ================= 踏切 ================= */
  const silhouetteTown = (x, w, h, hz, U, seed, colour, lit, lamp) => {
    let bx = -U * 0.02, i = 0;
    while (bx < w) {
      const bw = U * (0.035 + 0.07 * r(seed, i, 1)), bh = U * (0.04 + 0.13 * Math.pow(r(seed, i, 2), 1.6));
      x.fillStyle = colour; x.fillRect(bx, hz - bh, bw, bh + 2);
      if (r(seed, i, 3) > 0.62) { x.beginPath(); x.moveTo(bx - bw * 0.06, hz - bh); x.lineTo(bx + bw * 0.5, hz - bh - U * 0.02); x.lineTo(bx + bw * 1.06, hz - bh); x.fill(); }
      if (r(seed, i, 4) > 0.86) { x.fillRect(bx + bw * 0.45, hz - bh - U * 0.05, U * 0.002, U * 0.05); }
      if (lit) for (let wy = hz - bh + U * 0.012; wy < hz - U * 0.01; wy += U * 0.016) for (let wx = bx + U * 0.008; wx < bx + bw - U * 0.01; wx += U * 0.014) if (r(seed, i, Math.round(wx), Math.round(wy)) > 0.68) { x.fillStyle = rgba(lamp, 0.9); x.fillRect(wx, wy, U * 0.006, U * 0.008); }
      bx += bw + U * (0.002 + 0.006 * r(seed, i, 5)); i++;
    }
    /* a water tower */
    x.fillStyle = colour; const wx = w * 0.7; x.fillRect(wx - U * 0.002, hz - U * 0.17, U * 0.004, U * 0.1); x.fillRect(wx + U * 0.026, hz - U * 0.17, U * 0.004, U * 0.1); x.beginPath(); x.roundRect(wx - U * 0.012, hz - U * 0.21, U * 0.05, U * 0.04, U * 0.008); x.fill();
  };
  K.registerBg('echoFumikiri', '踏切', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), tone = duskTone(sc), key = `fk|${seed}|${ck(sc)}`, hz = H * 0.62, portrait = H > W;
    const sunX = W * (portrait ? 0.3 : 0.72), sunY = hz - U * 0.08, railY = H * 0.84;
    ctx.globalAlpha = e;
    ctx.drawImage(skyLayer(env, `${key}|sky`, tone, hz, U, sunX, sunY, true), 0, 0, W, H);
    ctx.drawImage(layer(env, `${key}|clouds`, 0.5, (x, w, h) => {
      streaks(x, w, h, H * 0.06, hz - U * 0.08, seed, tone, U, 8);
      const ct = { light: tone.cloudLight, body: mix(tone.cloud, tone.sky1, 0.3), shade: mix(tone.cloud, tone.ink, 0.45), belly: 0.75 };
      for (let i = 0; i < 4; i++) K.cloud(x, w * (i / 4 + 0.05 * r(seed, i, 1)), H * (0.14 + 0.24 * r(seed, i, 2)), U * (0.45 + 0.35 * r(seed, i, 3)), U * (0.16 + 0.1 * r(seed, i, 4)), J.h(seed, i, 5), ct, sunX > w / 2 ? 1 : -1, false);
    }), 0, 0, W, H);
    sunDisc(ctx, t, sunX, sunY, U, tone);
    ctx.drawImage(layer(env, `${key}|town`, 0.5, (x, w, h) => {
      paint.ridge(x, w, hz + 2, hz, hz * 0.08, paint.profile(seed, 3, [[2, 0.4], [3, 0.3], [9, 0.3]]), mix(tone.inkFar, tone.sky2, 0.3));
      silhouetteTown(x, w, h, hz + U * 0.005, U, seed, mix(tone.ink, tone.inkFar, 0.5), tone.dark, tone.lamp);
      x.fillStyle = tone.ink; x.fillRect(0, hz, w, h - hz);
    }), 0, 0, W, H);
    /* the ground, the road climbing to the crossing, the rails in the last light */
    ctx.fillStyle = vertical(ctx, hz, H, [[0, mix(tone.ink, tone.sky2, tone.dark ? 0.12 : 0.22)], [1, tone.ink]]); ctx.fillRect(0, hz, W, H - hz);
    const ballast = vertical(ctx, railY - U * 0.05, H, [[0, mix(tone.ink, tone.sun, 0.14)], [1, mix(tone.ink, '#000000', 0.2)]]);
    ctx.fillStyle = ballast; ctx.beginPath(); ctx.moveTo(0, railY - U * 0.03); ctx.lineTo(W, railY - U * 0.05); ctx.lineTo(W, H); ctx.lineTo(0, H); ctx.fill();
    for (let i = 0; i < 40; i++) { const px = W * (i / 39), y = railY - U * 0.03 - (px / W) * U * 0.02; ctx.fillStyle = rgba(tone.sun, 0.1); ctx.fillRect(px - U * 0.012, y + U * 0.008, U * 0.024, U * 0.006); }
    { /* the road climbing over the tracks */
      const road = ctx.createLinearGradient(0, railY - U * 0.05, 0, H); road.addColorStop(0, rgba(mix(tone.ink, tone.sun, 0.22), 0.95)); road.addColorStop(1, rgba(mix(tone.ink, tone.sun, 0.06), 1));
      ctx.fillStyle = road; ctx.beginPath(); ctx.moveTo(W * 0.42, hz + U * 0.005); ctx.lineTo(W * 0.58, hz + U * 0.005); ctx.lineTo(W * 0.86, H); ctx.lineTo(W * 0.14, H); ctx.closePath(); ctx.fill();
      ctx.strokeStyle = rgba(mix(tone.sun, '#FFFFFF', 0.5), 0.7); ctx.lineWidth = Math.max(1.2, U * 0.003); ctx.setLineDash([U * 0.035, U * 0.03]); ctx.beginPath(); ctx.moveTo(W * 0.5, hz + U * 0.01); ctx.lineTo(W * 0.5, H); ctx.stroke(); ctx.setLineDash([]);
    }
    ctx.strokeStyle = rgba(mix(tone.sun, '#FFFFFF', 0.3), 0.95); ctx.lineWidth = Math.max(2, U * 0.0055);
    for (const dy of [0, U * 0.028]) { ctx.beginPath(); ctx.moveTo(0, railY + dy - U * 0.005); ctx.lineTo(W, railY + dy - U * 0.025); ctx.stroke(); }
    /* poles and sagging wires with birds on them */
    const poles = [0.12, 0.42, 0.72, 0.98].map(v => W * v);
    ctx.strokeStyle = tone.ink; ctx.lineWidth = Math.max(1, U * 0.0022);
    poles.forEach((px, i) => { const top = hz - U * (0.18 + 0.03 * i); ctx.fillStyle = tone.ink; ctx.fillRect(px - U * 0.004, top, U * 0.008, hz - top + U * 0.06); ctx.fillRect(px - U * 0.05, top + U * 0.012, U * 0.1, U * 0.004); ctx.fillRect(px - U * 0.04, top + U * 0.045, U * 0.08, U * 0.003);
      if (i) for (const off of [-0.04, 0, 0.04]) { const px0 = poles[i - 1], t0 = hz - U * (0.18 + 0.03 * (i - 1)); ctx.beginPath(); ctx.moveTo(px0 + off * U, t0 + U * 0.013); ctx.quadraticCurveTo((px0 + px) / 2, Math.max(t0, top) + U * 0.07, px + off * U, top + U * 0.013); ctx.stroke(); } });
    { /* sparrows on the first span of wire */
      const p0 = poles[0], p1 = poles[1], y0 = hz - U * 0.18 + U * 0.013, y1 = hz - U * 0.21 + U * 0.013, yc = Math.max(hz - U * 0.18, hz - U * 0.21) + U * 0.07;
      ctx.fillStyle = tone.ink;
      [0.22, 0.27, 0.33, 0.62, 0.67].forEach((sv, i) => { const px = (1 - sv) * (1 - sv) * p0 + 2 * sv * (1 - sv) * (p0 + p1) / 2 + sv * sv * p1, py = (1 - sv) * (1 - sv) * y0 + 2 * sv * (1 - sv) * yc + sv * sv * y1 - U * 0.002, s2 = U * (0.011 + 0.003 * (i % 2));
        ctx.beginPath(); ctx.ellipse(px, py - s2 * 0.5, s2 * 0.7, s2 * 0.5, 0, 0, TAU); ctx.fill(); ctx.beginPath(); ctx.arc(px + s2 * 0.55, py - s2 * 0.85, s2 * 0.3, 0, TAU); ctx.fill(); ctx.fillRect(px - s2 * 1.1, py - s2 * 0.5, s2 * 0.6, s2 * 0.12); });
    }
    /* the crossing: gates, warning lamps, a bell that rings in light */
    const cyc = 16, ph = wrap(t + r(seed, 5) * cyc, cyc), down = ph > 1 && ph < 10, arm = down ? clamp(Math.min((ph - 1) / 1.6, (10 - ph) / 1.4)) : 0, flash = down ? Math.floor(ph * 3.4) % 2 : -1;
    const gate = (gx, dir) => {
      const gy = railY - U * 0.05 + (gx / W) * -U * 0.02;
      ctx.fillStyle = tone.ink; ctx.fillRect(gx - U * 0.006, gy - U * 0.3, U * 0.012, U * 0.32);
      ctx.fillStyle = mix(tone.ink, '#FFFFFF', 0.06); ctx.fillRect(gx - U * 0.03, gy - U * 0.29, U * 0.06, U * 0.006);
      ctx.save(); ctx.translate(gx, gy - U * 0.26); ctx.rotate(-dir * Math.PI / 4); ctx.fillRect(-U * 0.09, -U * 0.008, U * 0.18, U * 0.016); ctx.rotate(dir * Math.PI / 2); ctx.fillRect(-U * 0.09, -U * 0.008, U * 0.18, U * 0.016); ctx.restore();
      for (let k = 0; k < 2; k++) { const lx = gx + (k ? 1 : -1) * U * 0.045, ly = gy - U * 0.22, on = flash === k; ctx.fillStyle = on ? '#FF3A3A' : mix(tone.ink, '#5A1010', 0.4); ctx.beginPath(); ctx.arc(lx, ly, U * 0.011, 0, TAU); ctx.fill(); if (on) { ctx.globalCompositeOperation = 'screen'; glowDisc(ctx, lx, ly, U * 0.09, '#FF3A3A', 0.65); ctx.globalCompositeOperation = 'source-over'; } }
      ctx.save(); ctx.translate(gx, gy - U * 0.09); ctx.rotate(-dir * (Math.PI / 2 * (1 - arm) * 0.98 + 0.04)); ctx.translate(0, 0);
      const len = U * 0.42 * dir; for (let s = 0; s < 8; s++) { ctx.fillStyle = s % 2 ? '#F4E8C0' : '#B02418'; ctx.fillRect(Math.min(0, len * (s + 1) / 8), -U * 0.006, Math.abs(len) / 8 + 0.5, U * 0.012); } ctx.restore();
    };
    void poles;
    gate(W * 0.2, 1); gate(W * 0.8, -1);
    /* the train: three lit cars in the low sun */
    const tph = (ph - 3.2) / 5, on = tph > 0 && tph < 1;
    if (on) {
      const cw = U * 0.5, ch = U * 0.15, x0 = -cw * 3.1 + (W + cw * 3.3) * (tph * tph * (3 - 2 * tph)), ty = railY - U * 0.028 - (x0 / W) * U * 0.02;
      for (let c = 0; c < 3; c++) {
        const cx = x0 + c * (cw + U * 0.012); ctx.fillStyle = tone.ink; ctx.beginPath(); ctx.roundRect(cx, ty - ch, cw, ch, ch * 0.1); ctx.fill();
        ctx.fillStyle = mix(tone.ink, tone.sun, 0.3); ctx.fillRect(cx, ty - ch * 0.42, cw, ch * 0.05);
        for (let k = 0; k < 6; k++) { ctx.fillStyle = rgba(tone.lamp, tone.dark ? 0.95 : 0.85); ctx.fillRect(cx + cw * (0.05 + k * 0.155), ty - ch * 0.82, cw * 0.11, ch * 0.32); }
      }
      ctx.globalCompositeOperation = 'screen'; glowDisc(ctx, x0 + cw * 1.5, ty - ch * 0.5, cw * 1.6, tone.lamp, 0.12); ctx.globalCompositeOperation = 'source-over';
    }
    /* grass at the verge and a few drifting seeds */
    ctx.strokeStyle = tone.ink; ctx.lineWidth = Math.max(1.5, U * 0.0035); ctx.lineCap = 'round';
    for (let i = 0; i < 46; i++) { const px = W * r(seed, i, 61), h0 = U * (0.03 + 0.07 * r(seed, i, 62)), sw = Math.sin(t * 1 + i * 1.3) * U * 0.008; ctx.beginPath(); ctx.moveTo(px, H + 2); ctx.quadraticCurveTo(px + sw * 0.3, H - h0 * 0.6, px + sw + (r(seed, i, 63) - 0.5) * U * 0.03, H - h0); ctx.stroke(); }
    ctx.globalCompositeOperation = 'screen';
    for (let i = 0; i < 20; i++) { const age = wrap(t * 0.03 + r(seed, i, 71), 1), px = W * age * 1.1 - W * 0.05, py = hz + (H - hz) * 0.1 + (H - hz) * 0.6 * r(seed, i, 72) + Math.sin(t * 0.8 + i) * U * 0.03; ctx.fillStyle = rgba(tone.sun, 0.55); ctx.beginPath(); ctx.arc(px, py, U * (0.0015 + 0.0025 * r(seed, i, 73)), 0, TAU); ctx.fill(); }
    ctx.globalCompositeOperation = 'source-over';
  });

  /* ================= 芒の原 ================= */
  const plumeSprite = (ctx, tone, size) => K.sprite(ctx, `plume|${tone.ink}|${tone.sun}|${tone.dark}`, size, size * 2.6, (g, w, h) => {
    const cx = w / 2, plume = tone.dark ? mix(tone.sun, '#FFFFFF', 0.25) : mix(tone.sun, '#FFFFFF', 0.4), shade = tone.dark ? mix(tone.ink, tone.sky1, 0.3) : mix(tone.sky1, tone.ink, 0.35);
    g.lineCap = 'round';
    for (let i = 0; i < 46; i++) {
      const v = i / 46, side = (i % 2 ? 1 : -1), spread = w * 0.42 * Math.sin(Math.PI * Math.min(1, v * 1.15 + 0.08)) * (0.4 + 0.6 * ((i * 37) % 10) / 10), y0 = h * (0.06 + 0.5 * v);
      g.strokeStyle = rgba(mix(plume, shade, 0.2 + 0.35 * v), 0.85); g.lineWidth = Math.max(0.7, w * 0.03);
      g.beginPath(); g.moveTo(cx, h * (0.06 + 0.5 * v) + h * 0.12); g.quadraticCurveTo(cx + side * spread * 0.5, y0 + h * 0.02, cx + side * spread, y0 - h * 0.02 - h * 0.08 * (1 - v)); g.stroke();
    }
    g.strokeStyle = rgba(mix(plume, '#FFFFFF', 0.4), 0.95); g.lineWidth = Math.max(0.8, w * 0.024);
    for (let i = 0; i < 20; i++) { const side = i % 2 ? 1 : -1, v = i / 20; g.beginPath(); g.moveTo(cx, h * (0.12 + 0.4 * v)); g.quadraticCurveTo(cx + side * w * 0.14, h * (0.05 + 0.38 * v), cx + side * w * (0.06 + 0.2 * Math.sin(Math.PI * v)), h * (0.02 + 0.4 * v)); g.stroke(); }
    g.strokeStyle = rgba(shade, 0.8); g.lineWidth = Math.max(0.8, w * 0.02); g.beginPath(); g.moveTo(cx, h * 0.04); g.lineTo(cx, h * 0.7); g.stroke();
  });
  K.registerBg('echoSusuki', '芒の原', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), tone = duskTone(sc), key = `ss|${seed}|${ck(sc)}`, hz = H * 0.5, portrait = H > W;
    const sunX = W * (portrait ? 0.62 : 0.7), sunY = hz - U * 0.06;
    ctx.globalAlpha = e;
    ctx.drawImage(skyLayer(env, `${key}|sky`, tone, hz + U * 0.1, U, sunX, sunY, true), 0, 0, W, H);
    ctx.drawImage(layer(env, `${key}|clouds`, 0.4, (x, w, h) => streaks(x, w, h, H * 0.06, hz - U * 0.06, seed + 4, tone, U, 8)), 0, 0, W, H);
    sunDisc(ctx, t, sunX, sunY, U, tone);
    /* a skein of geese */
    ctx.strokeStyle = rgba(tone.ink, 0.7); ctx.lineWidth = Math.max(1.2, U * 0.0022); ctx.lineCap = 'round';
    for (let b = 0; b < 9; b++) { const row = b - 4, bx = wrap(t * U * 0.02 + W * 0.2, W * 1.3) - W * 0.15 - Math.abs(row) * U * 0.03, by = H * 0.18 + Math.abs(row) * U * 0.015 + row * U * 0.004 + Math.sin(t * 0.7 + b) * U * 0.004, s = U * 0.012, f = Math.sin(t * 5 + b * 1.7) * s * 0.5; ctx.beginPath(); ctx.moveTo(bx - s, by - f); ctx.quadraticCurveTo(bx - s * 0.4, by - s * 0.5, bx, by); ctx.quadraticCurveTo(bx + s * 0.4, by - s * 0.5, bx + s, by - f); ctx.stroke(); }
    ctx.drawImage(layer(env, `${key}|hills`, 0.5, (x, w, h) => {
      paint.ridge(x, w, h, hz + U * 0.05, hz * 0.22, paint.profile(seed, 3, [[1, 0.5], [2, 0.3], [5, 0.2]]), top => vertical(x, top, hz + U * 0.06, [[0, mix(tone.inkFar, tone.sky2, 0.45)], [1, mix(tone.inkFar, tone.sky2, 0.8)]]));
      paint.ridge(x, w, h, hz + U * 0.1, hz * 0.14, paint.profile(seed, 9, [[2, 0.5], [3, 0.3], [8, 0.2]]), top => vertical(x, top, hz + U * 0.12, [[0, mix(tone.inkFar, tone.ink, 0.3)], [1, mix(tone.inkFar, tone.sky1, 0.4)]]));
      x.fillStyle = vertical(x, hz, h, [[0, mix(tone.sun, tone.sky1, 0.3)], [0.35, mix(tone.sun, tone.ink, 0.6)], [1, tone.ink]]); x.fillRect(0, hz + U * 0.09, w, h - hz);
    }), 0, 0, W, H);
    /* the field in four rows: stems drawn with a wind that runs along them */
    const rows = [[0.56, 0.15, 0.06, 0.5], [0.67, 0.2, 0.085, 0.7], [0.8, 0.29, 0.12, 0.88], [0.97, 0.42, 0.17, 1]];
    rows.forEach(([ry, stemLen, plumeSize, alpha], row) => {
      const n = [46, 32, 20, 13][row], sprite = plumeSprite(ctx, tone, U * plumeSize), ps = U * plumeSize, tips = [];
      ctx.globalAlpha = e * alpha;
      ctx.strokeStyle = mix(tone.ink, tone.sky1, 0.45 * (1 - alpha)); ctx.lineWidth = Math.max(1, U * (0.0012 + 0.0012 * row)); ctx.lineCap = 'round';
      ctx.beginPath();                                                  /* every stem and leaf of the row in one stroke */
      for (let i = 0; i < n; i++) {
        const px = W * (i + r(seed, row, i, 1) * 0.9) / n, base = H * (ry + 0.03 * r(seed, row, i, 2)), len = U * stemLen * (0.75 + 0.5 * r(seed, row, i, 3));
        const wind = Math.sin(t * 0.9 + px / (U * 0.28) + row * 0.7) * 0.5 + Math.sin(t * 1.9 + px / (U * 0.11)) * 0.18 + 0.2, bend = len * (0.16 + 0.1 * wind);
        const tipX = px + bend, tipY = base - len;
        ctx.moveTo(px, base + U * 0.02); ctx.quadraticCurveTo(px + bend * 0.15, base - len * 0.5, tipX, tipY);
        for (let k = 1; k <= 3; k++) { const v = k * 0.2, lx = px + bend * v * v, ly = base - len * v; ctx.moveTo(lx, ly); ctx.quadraticCurveTo(lx + U * 0.02 * (1 + row * 0.3), ly - len * 0.06, lx + U * 0.05 * (1 + row * 0.3) + wind * U * 0.01, ly + len * 0.06 * (k % 2 ? 1 : 0.4)); }
        tips.push(tipX, tipY, Math.atan2(tipX - (px + bend * 0.15), len * 0.5) * 0.9 + wind * 0.05);
      }
      ctx.stroke();
      if (row < 2) { for (let i = 0; i < tips.length; i += 3) ctx.drawImage(sprite, tips[i] - ps / 2 + tips[i + 2] * ps * 0.9, tips[i + 1] + ps * 0.3 - ps * 2.05, ps, ps * 2.6); }   /* far plumes are small: lean by shifting, no rotation */
      else for (let i = 0; i < tips.length; i += 3) { ctx.save(); ctx.translate(tips[i], tips[i + 1] + ps * 0.3); ctx.rotate(tips[i + 2]); ctx.drawImage(sprite, -ps / 2, -ps * 2.05, ps, ps * 2.6); ctx.restore(); }
    });
    ctx.globalAlpha = e;
    /* red dragonflies and drifting seed fluff caught by the low sun */
    for (let d = 0; d < 5; d++) {
      const ph = t * 0.3 + d * 1.7, dx = W * (0.15 + 0.7 * ((d * 0.37 + r(seed, d, 1)) % 1)) + Math.sin(ph * 1.7) * U * 0.16, dy = H * (0.4 + 0.18 * r(seed, d, 2)) + Math.sin(ph * 2.3 + d) * U * 0.05, s = U * 0.02, dir = Math.cos(ph * 1.7) > 0 ? 1 : -1;
      ctx.save(); ctx.translate(dx, dy); ctx.scale(dir, 1); ctx.rotate(-0.15);
      ctx.strokeStyle = '#D6402C'; ctx.lineWidth = Math.max(1.5, s * 0.16); ctx.lineCap = 'round'; ctx.beginPath(); ctx.moveTo(-s * 0.7, 0); ctx.lineTo(s * 0.7, 0); ctx.stroke(); ctx.fillStyle = '#B02A1E'; ctx.beginPath(); ctx.arc(s * 0.78, 0, s * 0.13, 0, TAU); ctx.fill();
      ctx.fillStyle = rgba('#FFFFFF', 0.5); const flap = 0.6 + 0.4 * Math.sin(t * 40 + d); for (const [wx, wy, wr] of [[0.25, -1, 1], [0.05, -1, 0.85], [0.25, 1, 1], [0.05, 1, 0.85]]) { ctx.beginPath(); ctx.ellipse(s * wx, s * 0.5 * wy * flap, s * 0.5 * wr, s * 0.1, 0, 0, TAU); ctx.fill(); }
      ctx.restore();
    }
    ctx.globalCompositeOperation = 'screen';
    for (let i = 0; i < 40; i++) { const age = wrap(t * (0.02 + 0.03 * r(seed, i, 81)) + r(seed, i, 82), 1), px = W * (age * 1.2 - 0.1), py = H * (0.3 + 0.55 * r(seed, i, 83)) + Math.sin(t * 0.9 + i * 2) * U * 0.03; ctx.fillStyle = rgba(mix(tone.sun, '#FFFFFF', 0.4), 0.7 * (0.4 + 0.6 * Math.sin(t + i))); ctx.beginPath(); ctx.arc(px, py, U * (0.0015 + 0.0025 * r(seed, i, 84)), 0, TAU); ctx.fill(); }
    ctx.globalCompositeOperation = 'source-over';
  });

  /* in front of the lyric: a warm light leak breathing at the sun's corner, dust in the beams; light enough that text always reads */
  K.registerFront('echoDuskLeak', '夕光', (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, U = Umin(env), seed = P.seed || 1, e = fade(env, 1.2), tone = duskTone(sc);
    ctx.globalAlpha = e; ctx.globalCompositeOperation = 'screen';
    const corner = r(seed, 3) > 0.5 ? 1 : 0, lx = corner ? W : 0, ly = H * 0.2, leak = ctx.createRadialGradient(lx, ly, 0, lx, ly, Math.max(W, H) * 0.7);
    leak.addColorStop(0, rgba(tone.sun, (tone.dark ? 0.28 : 0.34) * (0.85 + 0.15 * Math.sin(t * 0.5)))); leak.addColorStop(0.4, rgba(sc.accent, 0.1)); leak.addColorStop(1, rgba(sc.accent, 0));
    ctx.fillStyle = leak; ctx.fillRect(0, 0, W, H);
    for (let i = 0; i < 26; i++) { const px = W * r(seed, i, 1), py = H - wrap(r(seed, i, 2) * H + t * H * (0.01 + 0.02 * r(seed, i, 3)), H), b = 0.5 + 0.5 * Math.sin(t * 0.8 + i * 2); ctx.fillStyle = rgba(tone.sun, 0.45 * b); ctx.beginPath(); ctx.arc(px + Math.sin(t * 0.4 + i) * U * 0.02, py, U * (0.0016 + 0.003 * r(seed, i, 4)), 0, TAU); ctx.fill(); }
    if (K.luxe) K.luxe(ctx, env, { seed: (P && P.seed) || 1, tint: [tone.sun, sc.accent, '#FFFFFF'], strength: 0.6, bokeh: 4, glitter: 6 });
  });
})();
