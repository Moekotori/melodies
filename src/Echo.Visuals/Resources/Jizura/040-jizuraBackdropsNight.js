/* ECHO first-party backdrops, night looks: 雨の夜景 (rain city), 夏祭り (festival), 傾奇 (kabuki stage).
   Static art is painted once into cached layers (kit.layer); only what moves or flickers is redrawn each frame. */
(() => {
  'use strict';
  const K = J.echo, E = K.E, TAU = K.TAU, clamp = K.clamp, mix = K.mix, rgba = K.rgba;
  const { U: Umin, isDark, wrap, glowOf, hues, fade, vertical, glowDisc, star, polygon, layer, scroll } = K;
  const colourKey = sc => [sc.bg, sc.fg, sc.accent, sc.accent2, sc.ghostA, sc.ghostB].join('');
  /* a deep tone from the ground colour: keeps a hot ground (a pink flip, say) from turning the whole scene garish */
  const deep = (colour, amount) => mix(mix(colour, '#0B0716', 0.55), '#000000', amount);

  /* ================= 雨の夜景 ================= */
  const towers = (seed, layerId, W, U, from, to, hMin, hMax, wMin, wMax) => {
    const out = [];
    let x = from, i = 0;
    while (x < to) {
      const w = U * (wMin + (wMax - wMin) * J.r(seed, layerId, i, 1)), h = (hMin + (hMax - hMin) * Math.pow(J.r(seed, layerId, i, 2), 0.8));
      out.push({ x, w, h, i, id: layerId * 1000 + i });
      x += w + U * (0.002 + 0.006 * J.r(seed, layerId, i, 3)); i++;
    }
    return out;
  };
  const cityGround = 0.84;
  const paintTowers = (x, list, W, H, U, seed, layerId, colour, windowLit, palette, detail, inverse) => {
    const ground = H * cityGround;
    for (const b of list) {
      const top = ground - b.h * H;
      x.fillStyle = colour; x.fillRect(b.x, top, b.w + 1, ground - top);
      const roof = J.r(seed, b.id, 4);
      if (roof > 0.72) x.fillRect(b.x + b.w * 0.1, top - U * 0.012, b.w * 0.8, U * 0.012);
      if (roof > 0.55 && roof <= 0.72) { x.fillRect(b.x + b.w * 0.46, top - H * 0.05, b.w * 0.05, H * 0.05); }
      if (roof <= 0.2) { x.beginPath(); x.moveTo(b.x, top); x.lineTo(b.x + b.w * 0.5, top - U * 0.03); x.lineTo(b.x + b.w, top); x.closePath(); x.fill(); }
      if (detail) {                                                             // edge light on the side facing the glow
        x.fillStyle = rgba(palette[b.id % palette.length], 0.16); x.fillRect(b.x + b.w - U * 0.0016, top, U * 0.0016, ground - top);
        x.fillStyle = colour;
      }
      const cw = U * (detail ? 0.017 : 0.011), ch = U * (detail ? 0.026 : 0.017), cols = Math.max(2, Math.floor((b.w - U * 0.01) / cw)), rows = Math.floor((ground - top - U * 0.03) / ch);
      for (let cx = 0; cx < cols; cx++) for (let ry = 0; ry < rows; ry++) {
        const id = b.id * 131 + cx * 31 + ry * 7;
        if (J.r(seed, id, 5) > windowLit) continue;
        const shade = palette[J.h(seed, id, 6) % palette.length];
        x.fillStyle = inverse ? rgba('#4A0A2C', 0.3 + 0.35 * J.r(seed, id, 8)) : rgba(J.r(seed, id, 7) > 0.7 ? shade : mix(shade, '#FFE8B0', 0.5), 0.5 + 0.45 * J.r(seed, id, 8));
        x.fillRect(b.x + U * 0.006 + cx * cw, top + U * 0.02 + ry * ch, cw * 0.55, ch * 0.5);
      }
      x.fillStyle = colour;
    }
  };
  K.registerBg('echoRainCity', '雨の夜景', ['glitch', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), key = `city-${seed}-${colourKey(sc)}`;
    const palette = hues(sc), pink = palette[0], cyan = palette[1] || palette[0], amber = palette[2] || cyan, ground = H * cityGround;
    const inverse = J.lum(sc.bg) > 0.3, tint = (dark, light) => (inverse ? light : dark);
    const haze = inverse ? mix(sc.bg, '#FFFFFF', 0.45) : mix(deep(sc.bg, 0.05), pink, 0.28);
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}-sky`, 0.25, (x, w, h) => {
      x.fillStyle = inverse ? vertical(x, 0, h, [[0, mix(sc.bg, '#5A0A30', 0.4)], [0.5, sc.bg], [0.85, mix(sc.bg, '#FFFFFF', 0.5)], [1, mix(sc.bg, '#FFFFFF', 0.2)]]) : vertical(x, 0, h, [[0, deep(sc.bg, 0.35)], [0.45, deep(sc.bg, 0.1)], [0.8, mix(deep(sc.bg, 0.05), pink, 0.34)], [1, deep(sc.bg, 0.2)]]); x.fillRect(0, 0, w, h);
      x.globalCompositeOperation = 'screen';
      glowDisc(x, w * 0.25, h * 0.78, U * 1.0, pink, 0.36); glowDisc(x, w * 0.78, h * 0.74, U * 0.9, cyan, 0.3);
      x.filter = 'none';
      for (let i = 0; i < 5; i++) {                                              // low cloud streaks lit from below
        const cy = h * (0.4 + 0.07 * i), cx = w * J.r(seed, i, 71), cw = w * (0.3 + 0.3 * J.r(seed, i, 72));
        const g = x.createLinearGradient(cx - cw / 2, 0, cx + cw / 2, 0); g.addColorStop(0, rgba(pink, 0)); g.addColorStop(0.5, rgba(i % 2 ? cyan : pink, 0.09)); g.addColorStop(1, rgba(pink, 0));
        x.fillStyle = g; x.fillRect(cx - cw / 2, cy, cw, h * 0.02);
      }
    }), 0, 0, W, H);
    // two rooftops throw searchlights into the cloud
    ctx.globalCompositeOperation = 'screen';
    for (let i = 0; i < 2; i++) {
      const bx = W * (i ? 0.66 : 0.3), by = ground - H * 0.3, angle = -Math.PI / 2 + Math.sin(t * 0.35 + i * 2 + seed) * 0.6 + (i ? 0.25 : -0.25), reach = H * 0.9, spread = 0.035;
      const beam = ctx.createLinearGradient(bx, by, bx + Math.cos(angle) * reach, by + Math.sin(angle) * reach);
      beam.addColorStop(0, rgba(i ? cyan : pink, 0.22)); beam.addColorStop(1, rgba(i ? cyan : pink, 0));
      ctx.fillStyle = beam; ctx.beginPath(); ctx.moveTo(bx, by);
      ctx.lineTo(bx + Math.cos(angle - spread) * reach, by + Math.sin(angle - spread) * reach); ctx.lineTo(bx + Math.cos(angle + spread) * reach, by + Math.sin(angle + spread) * reach); ctx.closePath(); ctx.fill();
    }
    ctx.globalCompositeOperation = 'source-over';
    // skyline in three planes: hazy far, dense mid, framing near
    const far = layer(env, `${key}-far`, 0.5, (x) => { paintTowers(x, towers(seed, 1, W, U, -20, W + 20, 0.12, 0.3, 0.035, 0.07), W, H, U, seed, 1, inverse ? mix(sc.bg, '#FFFFFF', 0.34) : mix(haze, deep(sc.bg, 0.2), 0.5), 0.07, palette, false, inverse); });
    const mid = layer(env, `${key}-mid`, 0.75, (x) => { paintTowers(x, towers(seed, 2, W, U, -20, W + 20, 0.16, 0.42, 0.05, 0.1), W, H, U, seed, 2, inverse ? mix(sc.bg, '#FFFFFF', 0.16) : deep(sc.bg, 0.55), 0.24, palette, true, inverse); });
    const near = layer(env, `${key}-near`, 0.75, (x) => {
      const left = towers(seed, 3, W, U, -U * 0.05, W * 0.19, 0.3, 0.62, 0.09, 0.14), right = towers(seed, 4, W, U, W * 0.83, W + U * 0.05, 0.28, 0.58, 0.09, 0.14);
      paintTowers(x, [...left, ...right], W, H, U, seed, 3, inverse ? mix(sc.bg, '#5A0A30', 0.45) : deep(sc.bg, 0.8), 0.2, palette, true, inverse);
    });
    ctx.drawImage(far, 0, 0, W, H);
    // an elevated train crossing the mid plane
    const cycle = 11, phase = wrap(t + J.r(seed, 5) * cycle, cycle) / 3.4;
    if (phase < 1) {
      const trainY = ground - H * 0.24, length = U * 0.9, x0 = -length + phase * (W + length * 1.4);
      ctx.fillStyle = inverse ? mix(sc.bg, '#3A0620', 0.7) : deep(sc.bg, 0.7); ctx.fillRect(x0, trainY, length, U * 0.028);
      for (let c = 0; c < 22; c++) { ctx.fillStyle = rgba('#FFF1C6', 0.9); ctx.fillRect(x0 + U * 0.012 + c * (length / 22), trainY + U * 0.006, length / 22 * 0.62, U * 0.012); }
      ctx.globalCompositeOperation = 'screen'; glowDisc(ctx, x0 + length, trainY + U * 0.014, U * 0.14, '#FFF1C6', 0.5); ctx.globalCompositeOperation = 'source-over';
    }
    ctx.drawImage(mid, 0, 0, W, H);
    // neon signs flicker on the mid and near planes; roofs blink red
    ctx.globalCompositeOperation = 'screen';
    const midTowers = towers(seed, 2, W, U, -20, W + 20, 0.16, 0.42, 0.05, 0.1);
    const signs = [];
    midTowers.forEach(b => { if (J.r(seed, b.id, 20) > 0.6) signs.push({ x: b.x + b.w * (0.2 + 0.6 * J.r(seed, b.id, 21)), y: ground - b.h * H + U * 0.03, len: Math.min(b.h * H * 0.45, U * 0.16), colour: palette[J.h(seed, b.id, 22) % palette.length], id: b.id, vertical: J.r(seed, b.id, 23) > 0.35, w: b.w }); });
    signs.forEach(sign => {
      const flick = 0.72 + 0.28 * Math.sin(t * (2 + J.r(seed, sign.id, 24) * 4) + sign.id) * (J.r(seed, sign.id, 25) > 0.85 ? Math.sign(Math.sin(t * 9 + sign.id)) : 1);
      const a = clamp(flick, 0.2, 1);
      if (sign.vertical) {
        ctx.fillStyle = rgba(sign.colour, 0.16 * a); ctx.fillRect(sign.x - U * 0.02, sign.y - U * 0.012, U * 0.04, sign.len + U * 0.024);
        ctx.fillStyle = rgba(mix(sign.colour, '#FFFFFF', 0.45), 0.92 * a); ctx.fillRect(sign.x - U * 0.0032, sign.y, U * 0.0064, sign.len);
        for (let s = 1; s < 4; s++) { ctx.fillStyle = rgba(sign.colour, 0.75 * a); ctx.fillRect(sign.x - U * 0.011, sign.y + sign.len * (s / 4) - U * 0.003, U * 0.022, U * 0.006); }
      } else {
        const sw = Math.min(sign.w * 0.7, U * 0.12);
        ctx.fillStyle = rgba(sign.colour, 0.16 * a); ctx.fillRect(sign.x - sw / 2 - U * 0.012, sign.y - U * 0.02, sw + U * 0.024, U * 0.04);
        ctx.strokeStyle = rgba(mix(sign.colour, '#FFFFFF', 0.4), 0.95 * a); ctx.lineWidth = Math.max(1.5, U * 0.004); ctx.strokeRect(sign.x - sw / 2, sign.y - U * 0.01, sw, U * 0.02);
      }
    });
    midTowers.forEach(b => { if (J.r(seed, b.id, 4) > 0.55 && J.r(seed, b.id, 4) <= 0.72) { const on = Math.sin(t * 2.2 + b.id) > 0.2; if (on) { ctx.fillStyle = rgba('#FF4A55', 0.9); ctx.beginPath(); ctx.arc(b.x + b.w * 0.485, ground - b.h * H - H * 0.05, U * 0.0035, 0, TAU); ctx.fill(); glowDisc(ctx, b.x + b.w * 0.485, ground - b.h * H - H * 0.05, U * 0.02, '#FF4A55', 0.6); } } });
    ctx.globalCompositeOperation = 'source-over';
    ctx.drawImage(near, 0, 0, W, H);
    // the big signs on the framing towers
    ctx.globalCompositeOperation = 'screen';
    [[0.055, pink], [0.93, cyan], [0.13, cyan], [0.87, amber]].forEach(([u, colour], i) => {
      const flick = 0.78 + 0.22 * Math.sin(t * (3 + i) + i * 2) * (i === 1 ? Math.sign(Math.sin(t * 7.3)) : 1), sx = W * u, sy = ground - H * (0.5 - i * 0.04), len = H * (0.18 + 0.03 * (i % 2));
      glowDisc(ctx, sx, sy + len / 2, U * 0.13, colour, 0.3 * flick);
      ctx.fillStyle = rgba(mix(colour, '#FFFFFF', 0.4), 0.95 * clamp(flick, 0.2, 1)); ctx.fillRect(sx - U * 0.005, sy, U * 0.01, len);
      for (let s = 0; s < 5; s++) { ctx.fillStyle = rgba(colour, 0.85 * clamp(flick, 0.2, 1)); ctx.fillRect(sx - U * 0.018, sy + len * (s / 5) + U * 0.004, U * 0.036, U * 0.008); }
    });
    ctx.globalCompositeOperation = 'source-over';
    // the wet street: glow, a flipped skyline, drifting light, ripples
    ctx.fillStyle = inverse ? vertical(ctx, ground, H, [[0, mix(sc.bg, '#5A0A30', 0.2)], [1, mix(sc.bg, '#3A0620', 0.6)]]) : vertical(ctx, ground, H, [[0, mix(deep(sc.bg, 0.3), pink, 0.16)], [1, deep(sc.bg, 0.6)]]); ctx.fillRect(0, ground, W, H - ground);
    ctx.save(); ctx.beginPath(); ctx.rect(0, ground, W, H - ground); ctx.clip();
    ctx.translate(0, ground * 2); ctx.scale(1, -1); ctx.globalAlpha = e * 0.26;
    ctx.drawImage(mid, 0, 0, W, H); ctx.drawImage(near, 0, 0, W, H);
    ctx.restore();
    ctx.globalCompositeOperation = 'screen';
    for (let i = 0; i < 16; i++) {
      const x = wrap(J.r(seed, i, 11) * W + Math.sin(t * 0.5 + i) * U * 0.02, W), w = U * (0.04 + 0.16 * J.r(seed, i, 12)), y = ground + (H - ground) * (0.1 + 0.85 * J.r(seed, i, 13));
      ctx.fillStyle = rgba(palette[i % palette.length], (0.26 + 0.14 * Math.sin(t * 1.3 + i)) * (0.6 + 0.6 * (y - ground) / (H - ground))); ctx.fillRect(x - w / 2, y, w, Math.max(1.5, U * 0.003));
    }
    ctx.globalCompositeOperation = 'source-over';
    ctx.lineWidth = Math.max(1, U * 0.002);
    for (let i = 0; i < 7; i++) {
      const age = wrap(t * 0.9 + J.r(seed, i, 31) * 3, 1.6) / 1.6, rx = J.r(seed, i, 32) * W, ry = ground + (H - ground) * (0.25 + 0.7 * J.r(seed, i, 33));
      ctx.strokeStyle = rgba(palette[i % palette.length], 0.5 * (1 - age)); ctx.beginPath(); ctx.ellipse(rx, ry, U * 0.03 * age + 2, U * 0.008 * age + 1, 0, 0, TAU); ctx.stroke();
    }
    // fog rolling through the streets
    for (let i = 0; i < 2; i++) {
      const fx = wrap(t * U * (0.012 + 0.006 * i) + i * W * 0.5, W * 1.6) - W * 0.3, fogY = ground - H * (0.05 + 0.06 * i);
      const fog = ctx.createRadialGradient(fx, fogY, 0, fx, fogY, W * 0.5); fog.addColorStop(0, rgba(haze, 0.22)); fog.addColorStop(1, rgba(haze, 0));
      ctx.save(); ctx.translate(0, fogY); ctx.scale(1, 0.25); ctx.translate(0, -fogY); ctx.fillStyle = fog; ctx.fillRect(fx - W * 0.5, fogY - W * 0.5, W, W); ctx.restore();
    }
    // rain in two depths
    const slope = 0.2;
    [[70, 0.0012, 0.14, 1.1], [45, 0.002, 0.24, 1.5]].forEach(([count, width, alpha, speed], depth) => {
      const n = Math.round(count * Math.sqrt(W * H / (1920 * 1080)));
      ctx.lineCap = 'round'; ctx.lineWidth = Math.max(1, U * width); ctx.strokeStyle = rgba(mix(sc.fg, cyan, 0.3), alpha); ctx.beginPath();
      for (let i = 0; i < n; i++) {
        const v = H * (0.9 + 0.7 * J.r(seed, depth, i, 21)) * speed, len = U * (0.035 + 0.05 * J.r(seed, depth, i, 22)) * (1 + depth * 0.5);
        const y = wrap(J.r(seed, depth, i, 23) * H * 1.3 + t * v, H * 1.3) - H * 0.15, x = wrap(J.r(seed, depth, i, 24) * W * 1.2 - y * slope, W * 1.2) - W * 0.1;
        ctx.moveTo(x, y); ctx.lineTo(x - len * slope, y - len);
      }
      ctx.stroke();
    });
  });
  /* in front of the lyric: long rain, drifting neon bokeh and a wet sheen along the foot */
  K.registerFront('echoRainFront', '雨と光', (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, U = Umin(env), seed = P.seed || 1, palette = hues(sc), e = fade(env, 1);
    ctx.globalAlpha = e; ctx.lineCap = 'round';
    ctx.strokeStyle = rgba(mix(sc.fg, palette[1] || palette[0], 0.25), 0.16); ctx.lineWidth = Math.max(1.2, U * 0.0022); ctx.beginPath();
    const n = Math.round(26 * Math.sqrt(W * H / (1920 * 1080)));
    for (let i = 0; i < n; i++) {
      const v = H * (1.5 + 0.9 * J.r(seed, i, 1)), len = U * (0.1 + 0.12 * J.r(seed, i, 2)), y = wrap(J.r(seed, i, 3) * H * 1.4 + t * v, H * 1.4) - H * 0.2, x = wrap(J.r(seed, i, 4) * W * 1.2 - y * 0.2, W * 1.2) - W * 0.1;
      ctx.moveTo(x, y); ctx.lineTo(x - len * 0.2, y - len);
    }
    ctx.stroke();
    ctx.globalCompositeOperation = 'screen';
    for (let i = 0; i < 7; i++) {
      const r = U * (0.03 + 0.05 * J.r(seed, i, 5)), x = wrap(J.r(seed, i, 6) * W + t * U * 0.01 * (i % 2 ? 1 : -1), W), y = H * (0.15 + 0.7 * J.r(seed, i, 7)) + Math.sin(t * 0.4 + i) * U * 0.02;
      const g = ctx.createRadialGradient(x, y, r * 0.6, x, y, r); g.addColorStop(0, rgba(palette[i % palette.length], 0.03)); g.addColorStop(0.85, rgba(palette[i % palette.length], 0.13)); g.addColorStop(1, rgba(palette[i % palette.length], 0));
      ctx.fillStyle = g; ctx.beginPath(); ctx.arc(x, y, r, 0, TAU); ctx.fill();
    }
    const sheen = ctx.createLinearGradient(0, H * 0.9, 0, H); sheen.addColorStop(0, rgba(palette[0], 0)); sheen.addColorStop(1, rgba(palette[0], 0.1)); ctx.fillStyle = sheen; ctx.fillRect(0, H * 0.9, W, H * 0.1);
    if (K.luxe) K.luxe(ctx, env, { seed: (P && P.seed) || 1, tint: ['#FF3D8B', '#35E4FF', '#B26BFF'], strength: 0.75, bokeh: 4, glitter: 8 });
  });

  /* ================= 夏祭り ================= */
  const torii = (x, cx, baseY, size, colour) => {
    const pillar = size * 0.058, half = size * 0.33, top = baseY - size;
    x.fillStyle = colour;
    x.fillRect(cx - half - pillar / 2, top + size * 0.05, pillar, size * 0.95); x.fillRect(cx + half - pillar / 2, top + size * 0.05, pillar, size * 0.95);
    x.fillRect(cx - half * 1.32, top + size * 0.24, half * 2.64, size * 0.05);                       // nuki
    x.fillRect(cx - size * 0.02, top + size * 0.09, size * 0.04, size * 0.15);                        // gakuzuka
    x.beginPath(); x.moveTo(cx - size * 0.62, top - size * 0.05); x.quadraticCurveTo(cx, top + size * 0.05, cx + size * 0.62, top - size * 0.05);
    x.lineTo(cx + size * 0.585, top + size * 0.035); x.quadraticCurveTo(cx, top + size * 0.13, cx - size * 0.585, top + size * 0.035); x.closePath(); x.fill();
    x.fillRect(cx - half * 1.5, top + size * 0.055, half * 3, size * 0.03);                            // shimaki
  };
  const pagoda = (x, cx, baseY, size, colour) => {
    x.fillStyle = colour;
    for (let tier = 0; tier < 5; tier++) {
      const w = size * (0.62 - tier * 0.085), y = baseY - tier * size * 0.19;
      x.fillRect(cx - w * 0.36, y - size * 0.13, w * 0.72, size * 0.13);
      x.beginPath(); x.moveTo(cx - w * 0.62, y - size * 0.115); x.quadraticCurveTo(cx - w * 0.3, y - size * 0.135, cx, y - size * 0.2); x.quadraticCurveTo(cx + w * 0.3, y - size * 0.135, cx + w * 0.62, y - size * 0.115);
      x.quadraticCurveTo(cx, y - size * 0.15, cx - w * 0.62, y - size * 0.115); x.closePath(); x.fill();
    }
    x.fillRect(cx - size * 0.008, baseY - size * 1.2, size * 0.016, size * 0.22);
  };
  K.registerBg('echoFestival', '提灯と花火', ['pop', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), dark = isDark(sc.bg), key = `festival-${seed}-${colourKey(sc)}`;
    const palette = hues(sc), riverY = H * 0.79, shoreSide = J.r(seed, 3) > 0.5 ? 1 : -1;
    const ink = dark ? mix(sc.bg, '#000000', 0.62) : mix(sc.bg, sc.fg, 0.42), toriiColour = dark ? mix('#5A0F14', sc.bg, 0.25) : mix(sc.accent, '#000000', 0.15);
    ctx.globalAlpha = e;
    const scene = layer(env, `${key}-scene`, 0.75, (x, w, h) => {
      x.fillStyle = dark ? vertical(x, 0, h * 0.8, [[0, mix(sc.bg, '#000000', 0.42)], [0.55, sc.bg], [1, mix(sc.bg, sc.accent, 0.42)]]) : vertical(x, 0, h * 0.8, [[0, mix(sc.bg, sc.accent2, 0.2)], [0.6, sc.bg], [1, mix(sc.bg, sc.accent, 0.2)]]);
      x.fillRect(0, 0, w, h * 0.8);
      if (dark) for (let i = 0; i < 60; i++) { x.fillStyle = rgba('#FFFFFF', 0.15 + 0.5 * J.r(seed, i, 2)); x.beginPath(); x.arc(J.r(seed, i, 1) * w, J.r(seed, i, 3) * h * 0.42, U * (0.001 + 0.0012 * J.r(seed, i, 4)), 0, TAU); x.fill(); }
      // hills, a pagoda, the far shore with its lights
      x.fillStyle = mix(ink, sc.bg, 0.35); x.beginPath(); x.moveTo(0, h * 0.8);
      for (let px = 0; px <= w; px += 20) x.lineTo(px, h * 0.71 - h * 0.05 * (0.5 + 0.5 * Math.sin(px / w * 4 + seed)) - h * 0.02 * Math.sin(px / w * 13 + seed * 2));
      x.lineTo(w, h * 0.8); x.closePath(); x.fill();
      pagoda(x, w * (shoreSide > 0 ? 0.3 : 0.7), h * 0.75, U * 0.2, mix(ink, sc.bg, 0.15));
      x.fillStyle = ink; x.fillRect(0, h * 0.75, w, h * 0.05);
      for (let i = 0; i < 90; i++) { x.fillStyle = rgba(J.r(seed, i, 6) > 0.5 ? '#FFD98A' : '#FFB36B', 0.6 + 0.4 * J.r(seed, i, 7)); x.fillRect(J.r(seed, i, 5) * w, h * (0.755 + 0.035 * J.r(seed, i, 8)), U * 0.0035, U * 0.0035); }
      // the river
      x.fillStyle = dark ? vertical(x, riverY, h, [[0, mix(sc.bg, sc.accent, 0.3)], [0.25, mix(sc.bg, '#000000', 0.25)], [1, mix(sc.bg, '#000000', 0.55)]]) : vertical(x, riverY, h, [[0, mix(sc.bg, '#BFE3FF', 0.45)], [1, mix(sc.bg, '#7FB6E8', 0.6)]]);
      x.fillRect(0, riverY, w, h - riverY);
    });
    ctx.drawImage(scene, 0, 0, W, H);
    // a torii standing in the water, reflected
    const toriiX = W * (shoreSide > 0 ? 0.85 : 0.15), toriiSize = U * 0.34, toriiLayer = layer(env, `${key}-torii`, 0.75, (x) => torii(x, toriiX, riverY + H * 0.03, toriiSize, toriiColour));
    ctx.drawImage(toriiLayer, 0, 0, W, H);
    ctx.save(); ctx.beginPath(); ctx.rect(0, riverY + H * 0.03, W, H); ctx.clip(); ctx.translate(0, (riverY + H * 0.03) * 2); ctx.scale(1, -1); ctx.globalAlpha = e * 0.3; ctx.drawImage(toriiLayer, 0, 0, W, H); ctx.restore();
    // fireworks: peony, willow and ring shells in two overlapping streams, mirrored in the river
    const period = 1.9, launch = 0.5, life = 2.4, sky = { compo: dark ? 'screen' : 'source-over' };
    const drawShell = (kind, ox, oy, a, colour, salt, alpha) => {
      const grow = E.outCubic(clamp(a / 1.1)), fadeOut = Math.pow(clamp(1 - a / life), 1.2), lw = Math.max(1.6, U * 0.0048), R = U * (kind === 2 ? 0.22 : 0.3 + 0.16 * J.r(seed, salt, 5));
      if (kind === 0) {
        const rays = 38 + (J.h(seed, salt, 6) % 12), sag = U * 0.09 * a * a * 0.5;
        ctx.lineWidth = lw; ctx.lineCap = 'round';
        for (let i = 0; i < rays; i++) {
          const angle = (i / rays) * TAU + J.r(seed, salt, i, 7) * 0.1, reach = R * grow * (0.7 + 0.3 * J.r(seed, salt, i, 8)), tail = 0.18 + 0.14 * J.r(seed, salt, i, 9);
          const px = ox + Math.cos(angle) * reach, py = oy + Math.sin(angle) * reach + sag;
          ctx.strokeStyle = rgba(colour, Math.min(1, fadeOut) * alpha); ctx.beginPath(); ctx.moveTo(px, py); ctx.lineTo(ox + Math.cos(angle) * reach * (1 - tail), oy + Math.sin(angle) * reach * (1 - tail) + sag * (1 - tail)); ctx.stroke();
          ctx.fillStyle = rgba(dark ? '#FFFFFF' : colour, fadeOut * alpha * (0.5 + 0.5 * Math.sin(t * 14 + i * 2.1))); ctx.beginPath(); ctx.arc(px, py, lw * 0.95, 0, TAU); ctx.fill();
        }
      } else if (kind === 1) {                                              // willow: gold particles trailing down
        const n = 46, gold = dark ? '#FFD98A' : mix(colour, '#E0A030', 0.5);
        for (let i = 0; i < n; i++) {
          const angle = (i / n) * TAU + J.r(seed, salt, i, 7) * 0.12, reach = R * (0.8 + 0.25 * J.r(seed, salt, i, 8));
          for (let k = 0; k < 7; k++) {
            const q = a - k * 0.065; if (q < 0) break;
            const ease = E.outCubic(clamp(q / 1.0)), px = ox + Math.cos(angle) * reach * ease, py = oy + Math.sin(angle) * reach * ease + U * 0.2 * q * q * 0.5;
            ctx.fillStyle = rgba(gold, fadeOut * alpha * (1 - k / 7) * 0.9); ctx.beginPath(); ctx.arc(px, py, lw * (1.15 - k * 0.1), 0, TAU); ctx.fill();
          }
        }
      } else {                                                              // ring with a bright heart
        for (let ring = 0; ring < 2; ring++) {
          const dots = ring ? 26 : 40, r = R * grow * (ring ? 0.58 : 1);
          for (let i = 0; i < dots; i++) { const angle = (i / dots) * TAU + ring * 0.2; ctx.fillStyle = rgba(ring ? palette[(J.h(seed, salt, 2) + 1) % palette.length] : colour, fadeOut * alpha); ctx.beginPath(); ctx.arc(ox + Math.cos(angle) * r, oy + Math.sin(angle) * r, lw * 1.25, 0, TAU); ctx.fill(); }
        }
        ctx.fillStyle = rgba(dark ? '#FFFFFF' : colour, fadeOut * alpha * 0.9); ctx.beginPath(); ctx.arc(ox, oy, R * 0.07 * (1 + grow), 0, TAU); ctx.fill();
      }
    };
    const shells = [];
    for (let stream = 0; stream < 2; stream++) {
      const cycle = period * (stream ? 1.31 : 1), current = Math.floor(t / cycle);
      for (let burst = current - 1; burst <= current; burst++) {
        const salt = burst * 2 + stream, age = t - (burst * cycle + J.r(seed, salt, 1) * 0.5 + stream * 0.7);
        if (age < 0 || age > launch + life) continue;
        shells.push({ salt, age, kind: J.h(seed, salt, 9) % 3, ox: W * (stream ? 0.5 + 0.4 * J.r(seed, salt, 2) : 0.08 + 0.42 * J.r(seed, salt, 2)), oy: H * (0.2 + 0.24 * J.r(seed, salt, 3)), colour: palette[J.h(seed, salt, 4) % palette.length] });
      }
    }
    ctx.globalCompositeOperation = sky.compo;
    for (const shell of shells) {
      const { salt, age, kind, ox, oy, colour } = shell;
      if (age < launch) {
        const q = E.outQuad(age / launch), y = H * 0.77 + (oy - H * 0.77) * q, y2 = H * 0.77 + (oy - H * 0.77) * E.outQuad(Math.max(0, age - 0.14) / launch);
        ctx.strokeStyle = rgba(colour, 0.85); ctx.lineWidth = Math.max(1.5, U * 0.004); ctx.lineCap = 'round'; ctx.beginPath(); ctx.moveTo(ox, y); ctx.lineTo(ox, y2); ctx.stroke();
        continue;
      }
      const a = age - launch;
      if (a < 0.25 && dark) glowDisc(ctx, ox, oy, U * 1.1, '#FFFFFF', 0.22 * (1 - a / 0.25));
      glowDisc(ctx, ox, oy, U * 0.5, colour, (dark ? 0.4 : 0.26) * Math.pow(clamp(1 - a / life), 1.2));
      drawShell(kind, ox, oy, a, colour, salt, 1);
    }
    // the same shells, softer, upside down in the water
    ctx.save(); ctx.beginPath(); ctx.rect(0, riverY, W, H - riverY); ctx.clip(); ctx.translate(0, riverY * 2); ctx.scale(1, -1);
    for (const shell of shells) if (shell.age >= launch) { glowDisc(ctx, shell.ox, shell.oy, U * 0.45, shell.colour, 0.16); drawShell(shell.kind, shell.ox, shell.oy, shell.age - launch, shell.colour, shell.salt, 0.32); }
    ctx.restore();
    ctx.globalCompositeOperation = 'source-over';
    // ripples that shimmer the reflection
    ctx.strokeStyle = rgba(dark ? '#FFFFFF' : '#FFFFFF', 0.08); ctx.lineWidth = Math.max(1, U * 0.002);
    for (let i = 0; i < 18; i++) { const y = riverY + (H - riverY) * (0.05 + 0.95 * J.r(seed, i, 41)), x0 = J.r(seed, i, 42) * W, len = U * (0.05 + 0.15 * J.r(seed, i, 43)), drift = Math.sin(t * 0.6 + i) * U * 0.02; ctx.beginPath(); ctx.moveTo(x0 + drift, y); ctx.lineTo(x0 + drift + len, y); ctx.stroke(); }
    // lanterns: two strings, ribbed paper glowing from inside
    [[0.055, 0.03, 0.95], [0.13, 0.05, 0.62]].forEach(([level, sag, size], row) => {
      const y0 = H * level, count = Math.round(W / (U * 0.19));
      ctx.strokeStyle = rgba(dark ? sc.fg : sc.ink, 0.3); ctx.lineWidth = Math.max(1, U * 0.0018);
      ctx.beginPath(); ctx.moveTo(-10, y0 - H * sag * 0.2); ctx.quadraticCurveTo(W * 0.5, y0 + H * sag * 2.4, W + 10, y0 - H * sag * 0.2); ctx.stroke();
      for (let i = 0; i < count; i++) {
        const u = (i + 0.5 + (row ? 0.5 : 0)) / count, lx0 = u * W;
        if (lx0 < 0 || lx0 > W) continue;
        const curve = (1 - u) * (1 - u) * (y0 - H * sag * 0.2) + 2 * u * (1 - u) * (y0 + H * sag * 2.4) + u * u * (y0 - H * sag * 0.2);
        const swing = Math.sin(t * 1.1 + i * 0.9 + row) * U * 0.004, r = U * 0.031 * size, lx = lx0 + swing, ly = curve + U * 0.03 * size, colour = i % 2 ? sc.accent2 : sc.accent, breath = 0.86 + 0.14 * Math.sin(t * 2.3 + i * 1.7);
        ctx.globalCompositeOperation = dark ? 'screen' : 'source-over'; glowDisc(ctx, lx, ly + r * 0.9, r * 3.8, colour, (dark ? 0.34 : 0.2) * breath); ctx.globalCompositeOperation = 'source-over';
        ctx.strokeStyle = rgba(dark ? sc.fg : sc.ink, 0.35); ctx.beginPath(); ctx.moveTo(lx, curve); ctx.lineTo(lx, ly); ctx.stroke();
        ctx.fillStyle = mix(sc.bg, '#000000', 0.6); ctx.fillRect(lx - r * 0.42, ly - r * 0.12, r * 0.84, r * 0.2);
        const body = ctx.createRadialGradient(lx, ly + r * 0.9, r * 0.1, lx, ly + r * 0.9, r * 1.1); body.addColorStop(0, mix(colour, '#FFFFFF', 0.55 * breath)); body.addColorStop(0.7, colour); body.addColorStop(1, mix(colour, '#000000', 0.25));
        ctx.fillStyle = body; ctx.beginPath(); ctx.ellipse(lx, ly + r * 0.9, r * 0.82, r * 1.02, 0, 0, TAU); ctx.fill();
        ctx.strokeStyle = rgba('#000000', 0.2); ctx.lineWidth = Math.max(1, U * 0.0015);
        for (let ring = -2; ring <= 2; ring++) { ctx.beginPath(); ctx.ellipse(lx, ly + r * 0.9 + ring * r * 0.34, r * 0.82 * Math.sqrt(1 - 0.09 * ring * ring), r * 0.1, 0, 0, TAU); ctx.stroke(); }
        ctx.fillStyle = rgba('#FFFFFF', 0.85); ctx.fillRect(lx - r * 0.26, ly + r * 0.72, r * 0.52, r * 0.36);
        ctx.fillStyle = rgba(mix(colour, '#000000', 0.5), 0.9); ctx.beginPath(); ctx.arc(lx, ly + r * 0.9, r * 0.09, 0, TAU); ctx.fill();
        ctx.fillStyle = mix(sc.bg, '#000000', 0.6); ctx.fillRect(lx - r * 0.34, ly + r * 1.84, r * 0.68, r * 0.16);
        ctx.strokeStyle = rgba(colour, 0.8); ctx.beginPath(); ctx.moveTo(lx, ly + r * 2); ctx.lineTo(lx + swing * 0.5, ly + r * 2.5); ctx.stroke();
      }
    });
  });
  /* in front: embers lifting off the festival and a few paper-lantern bokeh */
  K.registerFront('echoFestivalSparks', '火の粉', (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, U = Umin(env), seed = P.seed || 1, palette = hues(sc), dark = isDark(sc.bg), e = fade(env, 1);
    ctx.globalAlpha = e; ctx.globalCompositeOperation = dark ? 'screen' : 'source-over';
    for (let i = 0; i < 42; i++) {
      const speed = H * (0.05 + 0.1 * J.r(seed, i, 1)), y = H - wrap(J.r(seed, i, 2) * H * 1.2 + t * speed, H * 1.2) + H * 0.1, x = J.r(seed, i, 3) * W + Math.sin(t * (0.6 + J.r(seed, i, 4)) + i) * U * 0.03;
      const flick = 0.5 + 0.5 * Math.sin(t * (4 + 6 * J.r(seed, i, 5)) + i * 3), colour = palette[i % palette.length];
      ctx.fillStyle = rgba(colour, (0.35 + 0.55 * flick) * (0.5 + 0.5 * (y / H))); ctx.beginPath(); ctx.arc(x, y, U * (0.0016 + 0.0032 * J.r(seed, i, 6)), 0, TAU); ctx.fill();
    }
    for (let i = 0; i < 5; i++) {
      const r = U * (0.035 + 0.04 * J.r(seed, i, 9)), x = W * J.r(seed, i, 10) + Math.sin(t * 0.3 + i) * U * 0.03, y = H * (0.15 + 0.65 * J.r(seed, i, 11)) + Math.cos(t * 0.25 + i) * U * 0.02, colour = palette[i % palette.length];
      const g = ctx.createRadialGradient(x, y, r * 0.5, x, y, r); g.addColorStop(0, rgba(colour, 0.02)); g.addColorStop(0.85, rgba(colour, 0.11)); g.addColorStop(1, rgba(colour, 0));
      ctx.fillStyle = g; ctx.beginPath(); ctx.arc(x, y, r, 0, TAU); ctx.fill();
    }
    if (K.luxe) K.luxe(ctx, env, { seed: (P && P.seed) || 1, tint: ['#FFB060', sc.accent, '#FFE9A0'], strength: 0.75, bokeh: 4, glitter: 8 });
  });

  /* ================= 傾奇 ================= */
  /* gold as leaf on a folding screen: it has to read against whatever the ground is */
  const goldFor = sc => {
    for (const gold of ['#E7C468', '#B48A2C', '#7A5713', '#3F2C08']) if (J.contrast(gold, sc.bg) >= 1.9) return gold;
    return '#3F2C08';
  };
  const kasumiBand = (x, cx, cy, w, h, wobble) => {
    /* a flat-bottomed cloud lozenge with square-ish ends, the way it is cut on a 金屏風 */
    x.moveTo(cx - w / 2, cy);
    x.bezierCurveTo(cx - w * 0.5, cy - h * 0.9, cx - w * 0.28, cy - h * (0.7 + wobble), cx - w * 0.12, cy - h * 0.5);
    x.bezierCurveTo(cx, cy - h * (1.05 + wobble), cx + w * 0.2, cy - h * 0.6, cx + w * 0.5, cy - h * 0.15);
    x.lineTo(cx + w * 0.5, cy + h * 0.28); x.lineTo(cx - w / 2, cy + h * 0.28); x.closePath();
  };
  K.registerBg('echoGoldWa', '金屏風', ['emotional', 'editorial', 'graphic'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), dark = isDark(sc.bg), gold = goldFor(sc);
    const key = `kabuki|${seed}|${colourKey(sc)}`;
    ctx.globalAlpha = e;
    /* the screen: faint folding panels, gold seams, pooled light */
    const panels = layer(env, `${key}|panels`, 0.5, (x, w, h) => {
      const n = W > H ? 6 : 4, pw = w / n;
      for (let i = 0; i < n; i++) {
        const g = x.createLinearGradient(i * pw, 0, (i + 1) * pw, 0), lean = i % 2;
        g.addColorStop(0, rgba(lean ? '#000000' : '#FFFFFF', lean ? 0.16 : 0.05)); g.addColorStop(lean ? 0.35 : 0.65, rgba('#000000', 0)); g.addColorStop(1, rgba(lean ? '#FFFFFF' : '#000000', lean ? 0.05 : 0.16));
        x.fillStyle = g; x.fillRect(i * pw, 0, pw + 1, h);
        x.fillStyle = rgba(gold, 0.35); x.fillRect(i * pw - 1, 0, 2, h);
      }
    });
    ctx.drawImage(panels, 0, 0, W, H);
    const pool = ctx.createRadialGradient(W * 0.5, H * 0.42, U * 0.05, W * 0.5, H * 0.45, Math.max(W, H) * 0.7);
    pool.addColorStop(0, rgba(gold, dark ? 0.16 : 0.2)); pool.addColorStop(1, rgba(gold, 0));
    ctx.fillStyle = pool; ctx.fillRect(0, 0, W, H);
    /* 金雲: gold-leaf cloud bands that drift at three speeds */
    const bands = layer(env, `${key}|kasumi`, 0.5, (x, w, h) => {
      for (let i = 0; i < 9; i++) {
        const cy = h * (0.12 + 0.6 * J.r(seed, i, 1)), cw = U * (0.5 + 0.7 * J.r(seed, i, 2)), ch = U * (0.05 + 0.05 * J.r(seed, i, 3)), cx = w * J.r(seed, i, 4), wob = 0.2 * J.r(seed, i, 5);
        const layerAlpha = 0.16 + 0.24 * J.r(seed, i, 6);
        for (const shift of [-w, 0, w]) {
          x.beginPath(); kasumiBand(x, cx + shift, cy, cw, ch, wob);
          const g = x.createLinearGradient(0, cy - ch, 0, cy + ch * 0.3); g.addColorStop(0, rgba(gold, layerAlpha)); g.addColorStop(1, rgba(gold, layerAlpha * 0.4));
          x.fillStyle = g; x.fill();
          x.strokeStyle = rgba(gold, layerAlpha * 1.1); x.lineWidth = Math.max(1, U * 0.0022); x.stroke();
        }
      }
    });
    scroll(ctx, bands, env, t * U * 0.014, 1, 0, 0.95);
    scroll(ctx, bands, env, -t * U * 0.006 + W * 0.37, 1, H * 0.11, 0.55);
    /* the sun-disc with beaten gold rings */
    const cx = W * (0.7 + 0.025 * Math.sin(t * 0.13 + seed)), cy = H * (H > W ? 0.28 : 0.42), R = U * 0.3;
    glowDisc(ctx, cx, cy, R * 2.3, sc.accent, dark ? 0.3 : 0.22);
    const disc = ctx.createRadialGradient(cx - R * 0.25, cy - R * 0.3, R * 0.1, cx, cy, R);
    disc.addColorStop(0, mix(sc.accent, '#FFFFFF', dark ? 0.16 : 0.08)); disc.addColorStop(1, mix(sc.accent, '#000000', dark ? 0.28 : 0.2));
    ctx.fillStyle = disc; ctx.beginPath(); ctx.arc(cx, cy, R, 0, TAU); ctx.fill();
    ctx.strokeStyle = rgba(gold, 0.75); ctx.lineWidth = Math.max(1.5, U * 0.004); ctx.beginPath(); ctx.arc(cx, cy, R * 1.08, 0, TAU); ctx.stroke();
    ctx.strokeStyle = rgba(gold, 0.4); ctx.lineWidth = Math.max(1, U * 0.002); ctx.beginPath(); ctx.arc(cx, cy, R * 1.15, 0, TAU); ctx.stroke();
    ctx.setLineDash([U * 0.012, U * 0.02]); ctx.lineDashOffset = -t * U * 0.01; ctx.strokeStyle = rgba(gold, 0.42); ctx.lineWidth = Math.max(1, U * 0.0025);
    ctx.beginPath(); ctx.arc(cx, cy, R * 1.24, 0, TAU); ctx.stroke(); ctx.setLineDash([]);
    /* a few gold flecks pressed into the disc, glinting in turn */
    for (let i = 0; i < 26; i++) {
      const a = J.r(seed, i, 21) * TAU, d = Math.sqrt(J.r(seed, i, 22)) * R * 0.94, s = U * (0.003 + 0.006 * J.r(seed, i, 23)), glint = 0.35 + 0.65 * Math.pow(0.5 + 0.5 * Math.sin(t * 1.4 + i * 1.9), 3);
      ctx.fillStyle = rgba(gold, 0.35 + 0.5 * glint); ctx.fillRect(cx + Math.cos(a) * d - s / 2, cy + Math.sin(a) * d - s / 2, s, s);
    }
    /* 青海波 along the foot of the stage, the tiling period divides the frame so the drift loops */
    const count = Math.max(4, Math.round(W / (U * 0.16))), r = W / count, top = H * 0.64;
    const waves = layer(env, `${key}|seigaiha`, 0.7, (x, w, h) => {
      const rows = Math.ceil((h - top) / (r * 0.5)) + 2, ground = mix(sc.bg, '#000000', dark ? 0.35 : 0.06);
      for (let row = 0; row < rows; row++) {
        const y = top + row * r * 0.5, shift = row % 2 ? r : 0, depth = clamp(row / rows);
        for (let cx0 = -r * 2 + shift; cx0 < w + r * 2; cx0 += r * 2) {
          x.fillStyle = ground; x.beginPath(); x.arc(cx0, y, r, 0, TAU); x.fill();
          for (let ring = 1; ring <= 4; ring++) {
            x.strokeStyle = rgba(gold, (0.2 + 0.22 * depth) * (ring === 4 ? 1.25 : 0.85)); x.lineWidth = Math.max(1, U * (ring === 4 ? 0.0028 : 0.002));
            x.beginPath(); x.arc(cx0, y, r * ring / 4.2, Math.PI, 0); x.stroke();
          }
        }
      }
      x.globalCompositeOperation = 'destination-in';
      const g = x.createLinearGradient(0, top - r * 0.2, 0, top + r * 1.6); g.addColorStop(0, 'rgba(0,0,0,0)'); g.addColorStop(1, 'rgba(0,0,0,1)');
      x.fillStyle = g; x.fillRect(0, 0, w, h);
    });
    scroll(ctx, waves, env, t * U * 0.012, 1, 0, 1);
    /* edge shadow keeps the corners quiet so the lyric owns the middle */
    const edge = dark ? mix(sc.bg, '#000000', 0.6) : mix(sc.bg, sc.fg, 0.2);
    const vignette = ctx.createRadialGradient(W * 0.5, H * 0.46, U * 0.25, W * 0.5, H * 0.5, Math.max(W, H) * 0.78);
    vignette.addColorStop(0, rgba(edge, 0)); vignette.addColorStop(1, rgba(edge, dark ? 0.8 : 0.5));
    ctx.fillStyle = vignette; ctx.fillRect(0, 0, W, H);
    /* gold dust rising */
    for (let i = 0; i < 40; i++) {
      const y = H - wrap(J.r(seed, i, 1) * H * 1.2 + t * H * (0.018 + 0.03 * J.r(seed, i, 2)), H * 1.2) + H * 0.1;
      const x = J.r(seed, i, 3) * W + Math.sin(t * 0.5 + i) * U * 0.02, s = U * (0.0022 + 0.004 * J.r(seed, i, 4));
      ctx.globalAlpha = e * (0.25 + 0.5 * (0.5 + 0.5 * Math.sin(t * (1 + J.r(seed, i, 5) * 2) + i)));
      ctx.fillStyle = gold; ctx.beginPath(); ctx.moveTo(x, y - s * 1.6); ctx.lineTo(x + s, y); ctx.lineTo(x, y + s * 1.6); ctx.lineTo(x - s, y); ctx.closePath(); ctx.fill();
    }
  });
  /* in front: gold leaf flakes and a few maple leaves turning as they fall */
  K.registerFront('echoGoldLeaf', '金箔と紅葉', (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, U = Umin(env), seed = P.seed || 1, e = fade(env, 1), gold = goldFor(sc);
    const red = J.contrast('#C8281E', sc.bg) >= 1.6 ? '#C8281E' : (J.contrast('#F0C85A', sc.bg) >= 1.6 ? '#F0C85A' : gold);
    ctx.globalAlpha = e;
    for (let i = 0; i < 26; i++) {
      const speed = H * (0.04 + 0.06 * J.r(seed, i, 1)), span = H * 1.2, y = wrap(J.r(seed, i, 2) * span + t * speed, span) - H * 0.1;
      const x = J.r(seed, i, 3) * W + Math.sin(t * (0.5 + J.r(seed, i, 4)) + i * 2) * U * 0.03, s = U * (0.004 + 0.008 * J.r(seed, i, 5)), turn = t * (1 + 2 * J.r(seed, i, 6)) + i;
      const flip = Math.abs(Math.cos(turn)), glint = 0.35 + 0.65 * Math.pow(flip, 6);
      ctx.save(); ctx.translate(x, y); ctx.rotate(J.r(seed, i, 7) * TAU + turn * 0.3); ctx.scale(1, 0.25 + 0.75 * flip);
      ctx.fillStyle = rgba(gold, 0.35 + 0.5 * glint); ctx.fillRect(-s, -s * 0.7, s * 2, s * 1.4);
      ctx.restore();
    }
    for (let i = 0; i < 7; i++) {
      const speed = H * (0.05 + 0.05 * J.r(seed, i, 31)), span = H * 1.3, y = wrap(J.r(seed, i, 32) * span + t * speed, span) - H * 0.15;
      const x = J.r(seed, i, 33) * W + Math.sin(t * 0.7 + i * 1.7) * U * 0.06, s = U * (0.014 + 0.012 * J.r(seed, i, 35)), turn = t * (0.8 + J.r(seed, i, 36)) + i * 2;
      ctx.save(); ctx.translate(x, y); ctx.rotate(Math.sin(turn * 0.6) * 0.9 + J.r(seed, i, 37) * TAU); ctx.scale(Math.max(0.2, Math.abs(Math.cos(turn))), 1);
      ctx.fillStyle = rgba(red, 0.75); star(ctx, 0, 0, s, 5, 0.42); ctx.fill();
      ctx.strokeStyle = rgba(mix(red, '#000000', 0.4), 0.6); ctx.lineWidth = Math.max(1, U * 0.0015); ctx.beginPath(); ctx.moveTo(0, s * 0.2); ctx.lineTo(0, -s * 0.8); ctx.stroke();
      ctx.restore();
    }
    if (K.luxe) K.luxe(ctx, env, { seed: (P && P.seed) || 1, tint: ['#F1D27A', '#D9A93F', '#FFF3C8'], strength: 0.75, bokeh: 4, glitter: 8 });
  });
})();
