/* ECHO first-party backdrops, calm and daylight looks: 青空 (blue sky), 雪あかり (snow light), 純喫茶 (retro café).
   Static art is painted once into cached layers (kit.layer) and moved with parallax; only the living parts are redrawn. */
(() => {
  'use strict';
  const K = J.echo, E = K.E, TAU = K.TAU, clamp = K.clamp, mix = K.mix, rgba = K.rgba;
  const { U: Umin, isDark, wrap, glowOf, hues, fade, vertical, glowDisc, star, polygon, layer, scroll } = K;
  const colourKey = sc => [sc.bg, sc.fg, sc.accent, sc.accent2].join('');

  /* ================= 青空 ================= */
  const skyTones = sc => {
    const dark = J.lum(sc.bg) < 0.38, pale = J.lum(sc.bg) > 0.8;
    if (dark) { const lift = mix(sc.bg, '#FFFFFF', 0.3); return { dark, pale, light: mix(mix(sc.accent, '#FFFFFF', 0.7), lift, 0.25), body: mix(lift, sc.accent, 0.16), shade: mix(sc.bg, '#0A0A30', 0.2), belly: 0.75, rim: sc.accent }; }
    if (pale) return { dark, pale, light: '#FFFFFF', body: mix(sc.bg, '#FFFFFF', 0.6), shade: mix(sc.bg, sc.fg, 0.2), belly: 0.55, rim: sc.accent };
    return { dark, pale, light: '#FFFFFF', body: mix('#FFFFFF', sc.bg, 0.12), shade: mix(sc.bg, '#4C6FD0', 0.5), belly: 0.8, rim: mix(sc.accent, '#FFFFFF', 0.4) };
  };
  const cloudLayer = (env, sc, seed, name, count, [hMin, hMax], [yMin, yMax], alpha, tone, side) => layer(env, `sky-${name}-${seed}-${colourKey(sc)}`, name === 'far' ? 0.5 : 0.75, (x, W, H) => {
    const U = Math.min(W, H), flat = name === 'far';
    x.globalAlpha = alpha;
    /* thin veils first, far behind the cumulus */
    if (name === 'far') for (let i = 0; i < 5; i++) K.wisp(x, W * (i + 0.5) / 5, H * (yMin + (yMax - yMin) * J.r(seed, i, 21)), U * 0.9, U * 0.05, tone.dark ? mix(sc.bg, sc.accent, 0.6) : '#FFFFFF', tone.dark ? 0.16 : 0.5, J.h(seed, i, 22));
    const t2 = flat ? { ...tone, body: mix(tone.body, sc.bg, tone.dark ? 0.35 : 0.2) } : tone;
    for (let i = 0; i < count; i++) {
      const cw = U * (hMin + (hMax - hMin) * J.r(seed, i, 11)) * (2.1 + 0.9 * J.r(seed, i, 12)), ch = cw * (0.4 + 0.14 * J.r(seed, i, 13));
      const px = ((i + J.r(seed, i, 14) * 0.7) / count) * W, py = H * (yMin + (yMax - yMin) * J.r(seed, i, 15));
      for (const shift of [-W, 0, W]) if (px + shift + cw > -50 && px + shift < W + 50) K.cloud(x, px + shift, py, cw, ch, J.h(seed, i, name.length), t2, side, flat);
    }
  });
  K.registerBg('echoSky', '青空と雲', ['pop', 'calm', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.9), tone = skyTones(sc);
    const portrait = H > W, sunX = W * (portrait ? 0.68 : 0.72 + 0.06 * Math.sin(seed)), sunY = H * (portrait ? 0.2 : 0.27), side = sunX > W / 2 ? 1 : -1;
    ctx.globalAlpha = e;
    // sky: deep at the zenith, luminous at the horizon
    const sky = layer(env, `sky-bg-${colourKey(sc)}-${Math.round(sunX)}`, 0.25, (x, w, h) => {
      const zenith = tone.dark ? mix(sc.bg, '#03061C', 0.55) : tone.pale ? mix(sc.bg, sc.accent, 0.1) : mix(sc.bg, '#1E4FC8', 0.4);
      const horizon = tone.dark ? mix(sc.bg, sc.accent, 0.5) : tone.pale ? '#FFFFFF' : mix(sc.bg, '#FFFFFF', 0.5);
      x.fillStyle = vertical(x, 0, h, [[0, zenith], [0.5, sc.bg], [1, horizon]]); x.fillRect(0, 0, w, h);
      const glow = x.createRadialGradient(sunX, sunY, 0, sunX, sunY, Math.max(w, h) * 0.75);
      glow.addColorStop(0, rgba(tone.dark ? sc.accent : '#FFFFFF', tone.dark ? 0.5 : 0.62)); glow.addColorStop(0.2, rgba(tone.dark ? sc.accent : '#FFFFFF', 0.2)); glow.addColorStop(1, rgba('#FFFFFF', 0));
      x.fillStyle = glow; x.fillRect(0, 0, w, h);
    });
    ctx.drawImage(sky, 0, 0, W, H);
    if (tone.dark) {
      for (let i = 0; i < 70; i++) {
        const sx = J.r(seed, i, 31) * W, sy = J.r(seed, i, 32) * H * 0.62, tw = 0.35 + 0.65 * (0.5 + 0.5 * Math.sin(t * (0.8 + J.r(seed, i, 33) * 2.2) + i * 3.1));
        ctx.fillStyle = rgba('#FFFFFF', 0.85 * tw * (1 - sy / (H * 0.7))); ctx.beginPath(); ctx.arc(sx, sy, U * (0.0011 + 0.0016 * J.r(seed, i, 34)), 0, TAU); ctx.fill();
      }
      const cycle = 6.5, phase = wrap(t + J.r(seed, 91) * cycle, cycle) / 1.1;
      if (phase < 1) {
        const x0 = W * (0.15 + 0.5 * J.r(seed, Math.floor((t + J.r(seed, 91) * cycle) / cycle), 92)), y0 = H * 0.06, dx = U * 0.5 * phase, dy = U * 0.22 * phase;
        const trail = ctx.createLinearGradient(x0 + dx, y0 + dy, x0 + dx - U * 0.22, y0 + dy - U * 0.09);
        trail.addColorStop(0, rgba('#FFFFFF', 0.9 * (1 - phase))); trail.addColorStop(1, rgba('#FFFFFF', 0));
        ctx.strokeStyle = trail; ctx.lineWidth = Math.max(1.5, U * 0.003); ctx.lineCap = 'round';
        ctx.beginPath(); ctx.moveTo(x0 + dx, y0 + dy); ctx.lineTo(x0 + dx - U * 0.22, y0 + dy - U * 0.09); ctx.stroke();
      }
    }
    // the sun: a hard core, a soft corona and slow rings
    ctx.globalCompositeOperation = 'screen';
    glowDisc(ctx, sunX, sunY, U * 0.8, tone.dark ? sc.accent : '#FFF6D8', tone.dark ? 0.5 : 0.7);
    ctx.globalCompositeOperation = 'source-over';
    ctx.strokeStyle = rgba('#FFFFFF', tone.pale ? 0.5 : 0.22); ctx.lineWidth = Math.max(1, U * 0.002);
    for (let ring = 1; ring <= 3; ring++) { ctx.beginPath(); ctx.arc(sunX, sunY, U * (0.12 + ring * 0.09) + Math.sin(t * 0.5 + ring) * U * 0.004, 0, TAU); ctx.stroke(); }
    ctx.fillStyle = rgba(tone.dark ? '#FFF3D0' : '#FFFFFF', 0.96); ctx.beginPath(); ctx.arc(sunX, sunY, U * (tone.dark ? 0.045 : 0.05), 0, TAU); ctx.fill();
    ctx.fillStyle = rgba(tone.dark ? '#FFF3D0' : '#FFFFFF', 0.55); ctx.beginPath(); ctx.arc(sunX, sunY, U * 0.07, 0, TAU); ctx.fill();
    // clouds in four depths
    const far = cloudLayer(env, sc, seed, 'far', 8, [0.05, 0.08], [0.36, 0.56], 0.5, tone, side);
    const mid = cloudLayer(env, sc, seed, 'mid', 5, [0.09, 0.14], [0.26, 0.5], 0.92, tone, side);
    const near = cloudLayer(env, sc, seed, 'near', 4, [0.15, 0.22], [0.5, 0.76], 1, tone, side);
    const sea = cloudLayer(env, sc, seed, 'sea', 6, [0.2, 0.3], [0.86, 0.99], 1, tone, side);
    scroll(ctx, far, env, t * U * 0.006); scroll(ctx, mid, env, t * U * 0.013);
    // light through the cloud: soft rays fanning from the sun
    ctx.globalCompositeOperation = 'screen';
    const rays = 9;
    for (let i = 0; i < rays; i++) {
      const angle = (i / rays) * TAU * 0.5 + Math.PI * 0.32 * side + t * 0.02 * side, spread = 0.045 + 0.03 * J.r(seed, i, 41), reach = Math.hypot(W, H);
      const ray = ctx.createLinearGradient(sunX, sunY, sunX + Math.cos(angle) * reach * 0.8, sunY + Math.sin(angle) * reach * 0.8);
      ray.addColorStop(0, rgba('#FFFFFF', (0.16 + 0.08 * Math.sin(t * 0.5 + i * 1.9)) * (tone.dark ? 0.6 : 1))); ray.addColorStop(1, rgba('#FFFFFF', 0));
      ctx.fillStyle = ray; ctx.beginPath(); ctx.moveTo(sunX, sunY);
      ctx.lineTo(sunX + Math.cos(angle - spread) * reach, sunY + Math.sin(angle - spread) * reach); ctx.lineTo(sunX + Math.cos(angle + spread) * reach, sunY + Math.sin(angle + spread) * reach); ctx.closePath(); ctx.fill();
    }
    ctx.globalCompositeOperation = 'source-over';
    scroll(ctx, near, env, t * U * 0.024); scroll(ctx, sea, env, t * U * 0.009 + 300);
    // birds crossing far away
    if (!tone.dark) {
      ctx.strokeStyle = rgba(tone.pale ? sc.fg : '#274A9E', 0.55); ctx.lineWidth = Math.max(1.2, U * 0.0022); ctx.lineCap = 'round';
      for (let b = 0; b < 5; b++) {
        const bx = wrap(J.r(seed, b, 51) * W * 0.4 + t * U * (0.03 + 0.004 * b) - W * 0.1, W * 1.2) - W * 0.1 + b * U * 0.03, by = H * (0.24 + 0.05 * J.r(seed, b, 52)) + Math.sin(t * 0.8 + b) * U * 0.01 + b * U * 0.012;
        const s = U * (0.014 + 0.005 * J.r(seed, b, 53)), flap = Math.sin(t * 6 + b * 2) * s * 0.55;
        ctx.beginPath(); ctx.moveTo(bx - s, by - flap); ctx.quadraticCurveTo(bx - s * 0.4, by - s * 0.5 - flap * 0.3, bx, by); ctx.quadraticCurveTo(bx + s * 0.4, by - s * 0.5 - flap * 0.3, bx + s, by - flap); ctx.stroke();
      }
    }
    // glints and motes
    ctx.globalCompositeOperation = 'screen';
    for (let i = 0; i < 22; i++) {
      const gx = J.r(seed, i, 61) * W, gy = J.r(seed, i, 62) * H * 0.85, pulse = Math.pow(Math.max(0, Math.sin(t * (0.6 + J.r(seed, i, 63)) + i * 2.3)), 3), s = U * (0.008 + 0.014 * J.r(seed, i, 64)) * (0.4 + pulse);
      ctx.fillStyle = rgba('#FFFFFF', (tone.pale ? 0.85 : 0.9) * pulse); star(ctx, gx, gy, s, 4, 0.12); ctx.fill();
    }
    ctx.globalCompositeOperation = 'source-over';
    for (let i = 0; i < 26; i++) {
      const y = H - wrap(J.r(seed, i, 71) * H * 1.2 + t * H * (0.015 + 0.03 * J.r(seed, i, 72)), H * 1.2) + H * 0.1;
      const x = J.r(seed, i, 73) * W + Math.sin(t * 0.6 + i) * U * 0.03;
      ctx.fillStyle = rgba(tone.pale ? sc.accent : '#FFFFFF', 0.15 + 0.3 * (0.5 + 0.5 * Math.sin(t * 1.3 + i * 1.7)));
      ctx.beginPath(); ctx.arc(x, y, U * (0.002 + 0.0035 * J.r(seed, i, 74)), 0, TAU); ctx.fill();
    }
  });
  /* in front of the lyric: a wash of light off the sun's corner and a chain of lens ghosts */
  K.registerFront('echoSkyGlare', '空の光', (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, U = Umin(env), tone = skyTones(sc), seed = P.seed || 1, e = fade(env, 1.1);
    const portrait = H > W, sunX = W * (portrait ? 0.68 : 0.72 + 0.06 * Math.sin(seed)), sunY = H * (portrait ? 0.2 : 0.27);
    ctx.globalAlpha = e; ctx.globalCompositeOperation = 'screen';
    const wash = ctx.createRadialGradient(sunX, sunY, 0, sunX, sunY, Math.max(W, H) * 0.7);
    wash.addColorStop(0, rgba(tone.dark ? sc.accent : '#FFFFFF', tone.pale ? 0.16 : 0.2)); wash.addColorStop(1, rgba('#FFFFFF', 0));
    ctx.fillStyle = wash; ctx.fillRect(0, 0, W, H);
    const axisX = W / 2 - sunX, axisY = H / 2 - sunY, pulse = 0.8 + 0.2 * Math.sin(t * 0.6);
    [[0.45, 0.05, 0.22], [0.9, 0.035, 0.18], [1.35, 0.085, 0.12], [1.75, 0.045, 0.16], [2.15, 0.11, 0.09]].forEach(([along, radius, alpha], i) => {
      const gx = sunX + axisX * along, gy = sunY + axisY * along, r = U * radius;
      ctx.fillStyle = rgba(i % 2 ? '#BFE3FF' : '#FFE9B0', alpha * pulse * (tone.pale ? 0.7 : 1));
      polygon(ctx, gx, gy, r, 6, 0.4 * i); ctx.fill();
      ctx.strokeStyle = rgba('#FFFFFF', alpha * 0.9 * pulse); ctx.lineWidth = Math.max(1, U * 0.0016); ctx.stroke();
    });
    if (K.luxe) K.luxe(ctx, env, { seed: (P && P.seed) || 1, tint: ['#FFFFFF', sc.accent, '#FFE9B0'], strength: 0.75, bokeh: 4, glitter: 8 });
  });

  /* ================= 雪あかり ================= */
  const pine = (x, ground, px, base, height, width, colour, snow, lean) => {
    const tiers = 5;
    for (let i = 0; i < tiers; i++) {
      const u0 = i / tiers, u1 = (i + 1.35) / tiers, y0 = base - height * (1 - u0), y1 = base - height * (1 - Math.min(1, u1)) + height * 0.02;
      const half0 = width * (0.14 + 0.86 * u0) * 0.5, half1 = width * (0.14 + 0.86 * Math.min(1, u1)) * 0.5;
      x.fillStyle = colour; x.beginPath(); x.moveTo(px + lean * u0 * width, y0 - height * 0.02); x.lineTo(px + half1 + lean * u1 * width, y1); x.lineTo(px - half1 + lean * u1 * width, y1); x.closePath(); x.fill();
      if (snow) {
        x.fillStyle = snow; x.beginPath(); x.moveTo(px + lean * u0 * width, y0 - height * 0.02); x.lineTo(px + half1 * 0.84 + lean * u1 * width, y1 - (y1 - y0) * 0.4);
        x.quadraticCurveTo(px + lean * u1 * width, y1 - (y1 - y0) * 0.62, px - half1 * 0.84 + lean * u1 * width, y1 - (y1 - y0) * 0.36); x.closePath(); x.fill();
      }
    }
    x.fillStyle = colour; x.fillRect(px - width * 0.03, base - height * 0.02, width * 0.06, ground - base + height * 0.02);
  };
  const ridge = (x, W, H, level, amp, seed, colour, salt) => {
    x.fillStyle = colour; x.beginPath(); x.moveTo(-10, H + 10);
    for (let px = -10; px <= W + 10; px += 16) {
      const u = px / W, peak = Math.abs(Math.sin(u * (3 + salt) * Math.PI + seed)) * 0.6 + 0.4 * Math.abs(Math.sin(u * (9 + salt * 2) * Math.PI + seed * 2));
      x.lineTo(px, H * level - H * amp * peak);
    }
    x.lineTo(W + 10, H + 10); x.closePath(); x.fill();
  };
  const snowTone = sc => {
    const dark = J.lum(sc.bg) < 0.4;
    return dark ? { dark, sky0: mix(sc.bg, '#000000', 0.45), sky1: sc.bg, sky2: mix(sc.bg, sc.accent, 0.22), far: mix(sc.bg, '#8DB4E4', 0.22), forest: mix(sc.bg, '#000000', 0.55), forest2: mix(sc.bg, '#000000', 0.25), snow: mix(sc.bg, '#BFD9F5', 0.2), shadow: mix(sc.bg, '#000000', 0.22), cap: 'rgba(200,225,250,0.5)', flake: '#FFFFFF' }
      : { dark, sky0: mix(sc.bg, sc.accent, 0.14), sky1: sc.bg, sky2: mix(sc.bg, '#FFFFFF', 0.7), far: mix(sc.bg, sc.fg, 0.14), forest: mix(sc.bg, sc.fg, 0.62), forest2: mix(sc.bg, sc.fg, 0.34), snow: '#FFFFFF', shadow: mix(sc.bg, sc.accent, 0.3), cap: 'rgba(255,255,255,0.92)', flake: mix(sc.accent, '#FFFFFF', 0.25) };
  };
  K.registerBg('echoSnowscape', '雪あかり', ['calm', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), tone = snowTone(sc), key = `snow-${seed}-${colourKey(sc)}`;
    const moonX = W * (0.18 + 0.6 * J.r(seed, 1)), moonY = H * 0.24;
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}-sky`, 0.25, (x, w, h) => {
      x.fillStyle = vertical(x, 0, h, [[0, tone.sky0], [0.6, tone.sky1], [1, tone.sky2]]); x.fillRect(0, 0, w, h);
      if (tone.dark) for (let i = 0; i < 90; i++) { x.fillStyle = rgba('#FFFFFF', 0.25 + 0.6 * J.r(seed, i, 3)); x.beginPath(); x.arc(J.r(seed, i, 1) * w, J.r(seed, i, 2) * h * 0.55, U * (0.0009 + 0.0012 * J.r(seed, i, 4)), 0, TAU); x.fill(); }
    }), 0, 0, W, H);
    if (tone.dark) {
      ctx.globalCompositeOperation = 'screen';
      [[sc.accent, 0.2, 0.26], [sc.accent2, 0.15, 0.38], [sc.ghostB, 0.12, 0.18]].forEach(([colour, alpha, level], band) => {
        const y = H * level, sway = Math.sin(t * 0.18 + band * 2 + seed) * H * 0.05, path = new Path2D();
        path.moveTo(0, y + sway);
        for (let x = 0; x <= W; x += W / 10) path.lineTo(x, y + sway + Math.sin(x / W * 5 + t * 0.25 + band) * H * 0.07);
        for (let x = W; x >= 0; x -= W / 10) path.lineTo(x, y + sway + H * 0.28 + Math.sin(x / W * 4 + t * 0.2 + band) * H * 0.05);
        path.closePath();
        const gradient = ctx.createLinearGradient(0, y - H * 0.1, 0, y + H * 0.4);
        gradient.addColorStop(0, rgba(colour, 0)); gradient.addColorStop(0.4, rgba(colour, alpha)); gradient.addColorStop(1, rgba(colour, 0));
        ctx.fillStyle = gradient; ctx.fill(path);
      });
      ctx.globalCompositeOperation = 'source-over';
    }
    // the moon (or a pale sun through mist) with halo rings
    ctx.globalCompositeOperation = tone.dark ? 'screen' : 'source-over';
    glowDisc(ctx, moonX, moonY, U * 0.55, tone.dark ? glowOf(sc) : '#FFFFFF', tone.dark ? 0.34 : 0.7);
    ctx.globalCompositeOperation = 'source-over';
    ctx.strokeStyle = rgba(tone.dark ? '#DCEBFF' : '#FFFFFF', tone.dark ? 0.12 : 0.55); ctx.lineWidth = Math.max(1, U * 0.002);
    for (let r = 1; r <= 2; r++) { ctx.beginPath(); ctx.arc(moonX, moonY, U * (0.075 + r * 0.05), 0, TAU); ctx.stroke(); }
    ctx.fillStyle = rgba(tone.dark ? '#F4F8FF' : '#FFF1D2', tone.dark ? 0.94 : 0.98); ctx.beginPath(); ctx.arc(moonX, moonY, U * 0.052, 0, TAU); ctx.fill();
    if (tone.dark) { ctx.fillStyle = rgba('#9FB6D6', 0.28); [[-0.012, -0.008, 0.012], [0.014, 0.01, 0.009], [0.002, 0.02, 0.006]].forEach(([dx, dy, r]) => { ctx.beginPath(); ctx.arc(moonX + U * dx, moonY + U * dy, U * r, 0, TAU); ctx.fill(); }); }
    // mountains, pines, hills: three planes with mist between them
    const farLayer = layer(env, `${key}-far`, 0.5, (x, w, h) => { ridge(x, w, h, 0.6, 0.17, seed, mix(tone.far, tone.sky2, 0.4), 0); ridge(x, w, h, 0.66, 0.13, seed + 3, tone.far, 1); });
    ctx.drawImage(farLayer, 0, 0, W, H);
    const mistGradient = (y0, alpha) => { const g = ctx.createLinearGradient(0, y0 - H * 0.12, 0, y0 + H * 0.06); g.addColorStop(0, rgba(tone.sky2, 0)); g.addColorStop(0.7, rgba(tone.sky2, alpha)); g.addColorStop(1, rgba(tone.sky2, 0)); ctx.fillStyle = g; ctx.fillRect(0, y0 - H * 0.12, W, H * 0.18); };
    mistGradient(H * 0.66, tone.dark ? 0.32 : 0.55);
    const forestRow = (name, dense, hMin, hMax, level, colour, alphaCap) => layer(env, `${key}-${name}`, 0.75, (x, w, h) => {
      const count = Math.round(w / (U * dense));
      for (let i = 0; i < count; i++) {
        const px = ((i + J.r(seed, i, 5, name.length) * 0.8) / count) * (w + 60) - 30, ph = U * (hMin + (hMax - hMin) * J.r(seed, i, 6, name.length)), pw = ph * (0.42 + 0.1 * J.r(seed, i, 7));
        pine(x, h * 0.82, px, h * level + U * 0.03 * J.r(seed, i, 8), ph, pw, colour, alphaCap, (J.r(seed, i, 9) - 0.5) * 0.06);
      }
    });
    const forestBack = forestRow('forestB', 0.06, 0.12, 0.2, 0.68, tone.forest2, tone.cap);
    ctx.drawImage(forestBack, 0, 0, W, H);
    mistGradient(H * 0.7, tone.dark ? 0.3 : 0.5);
    const forest = forestRow('forest', 0.085, 0.17, 0.3, 0.75, tone.forest, tone.cap);
    ctx.drawImage(forest, 0, 0, W, H);
    mistGradient(H * 0.76, tone.dark ? 0.4 : 0.7);
    const hills = layer(env, `${key}-hills`, 0.75, (x, w, h) => {
      x.fillStyle = tone.shadow; ridge(x, w, h, 0.84, 0.05, seed + 5, tone.shadow, 0);
      x.fillStyle = tone.snow; x.beginPath(); x.moveTo(-10, h + 10);
      for (let px = -10; px <= w + 10; px += 16) { const u = px / w; x.lineTo(px, h * 0.87 - h * 0.035 * (0.5 + 0.5 * Math.sin(u * 5.5 + seed)) - h * 0.012 * Math.sin(u * 17 + seed * 2)); }
      x.lineTo(w + 10, h + 10); x.closePath(); x.fill();
      // snow crust catching the light
      x.strokeStyle = tone.dark ? 'rgba(200,225,255,0.3)' : 'rgba(255,255,255,0.95)'; x.lineWidth = U * 0.004; x.beginPath();
      for (let px = -10; px <= w + 10; px += 16) { const u = px / w; const yy = h * 0.87 - h * 0.035 * (0.5 + 0.5 * Math.sin(u * 5.5 + seed)) - h * 0.012 * Math.sin(u * 17 + seed * 2); if (px < 0) x.moveTo(px, yy); else x.lineTo(px, yy); }
      x.stroke();
    });
    ctx.drawImage(hills, 0, 0, W, H);
    // a stone lantern, snow on its cap, warm light inside
    const lx = W * (moonX > W / 2 ? 0.12 : 0.88), ly = H * 0.86, s = U * 0.1, warm = tone.dark ? '#FFC87A' : '#F2A65A', flicker = 0.85 + 0.15 * Math.sin(t * 5.3) * Math.sin(t * 2.1);
    ctx.globalCompositeOperation = tone.dark ? 'screen' : 'source-over';
    glowDisc(ctx, lx, ly - s * 1.15, s * 3.2, warm, (tone.dark ? 0.5 : 0.28) * flicker);
    ctx.globalCompositeOperation = 'source-over';
    const stone = tone.dark ? '#1A2233' : mix(sc.bg, sc.fg, 0.55);
    ctx.fillStyle = stone; ctx.fillRect(lx - s * 0.16, ly - s * 0.9, s * 0.32, s * 0.9);
    ctx.fillRect(lx - s * 0.34, ly - s * 0.08, s * 0.68, s * 0.1);
    ctx.fillRect(lx - s * 0.5, ly - s * 1.02, s, s * 0.1);
    ctx.fillRect(lx - s * 0.36, ly - s * 1.6, s * 0.72, s * 0.58);
    ctx.fillStyle = rgba(warm, 0.95 * flicker); ctx.fillRect(lx - s * 0.24, ly - s * 1.5, s * 0.48, s * 0.38);
    ctx.fillStyle = stone; ctx.beginPath(); ctx.moveTo(lx - s * 0.66, ly - s * 1.6); ctx.lineTo(lx, ly - s * 2.02); ctx.lineTo(lx + s * 0.66, ly - s * 1.6); ctx.closePath(); ctx.fill();
    ctx.fillStyle = tone.dark ? '#DCEBFA' : '#FFFFFF'; ctx.beginPath(); ctx.moveTo(lx - s * 0.6, ly - s * 1.62); ctx.quadraticCurveTo(lx, ly - s * 2.16, lx + s * 0.6, ly - s * 1.62); ctx.quadraticCurveTo(lx, ly - s * 1.8, lx - s * 0.6, ly - s * 1.62); ctx.fill();
    ctx.fillRect(lx - s * 0.48, ly - s * 1.06, s * 0.96, s * 0.05);
    // snow in three depths
    [[100, 0.0058, 0.05, 0.012, 0.8], [46, 0.011, 0.09, 0.02, 0.85], [12, 0.024, 0.15, 0.03, 0.5]].forEach(([count, size, speed, sway, alpha], depth) => {
      const n = Math.round(count * Math.sqrt(W * H / (1920 * 1080))), vy = H * speed;
      ctx.fillStyle = tone.flake;
      for (let i = 0; i < n; i++) {
        const r = U * size * (0.7 + 0.6 * J.r(seed, depth, i, 1)), y = wrap(J.r(seed, depth, i, 3) * H * 1.2 + t * vy * (0.8 + 0.4 * J.r(seed, depth, i, 4)), H * 1.2) - H * 0.1;
        const x = wrap(J.r(seed, depth, i, 5) * W * 1.1 + t * vy * 0.12 + Math.sin(t * J.rr(0.5, 1.3, seed, depth, i, 2) + i) * U * sway, W * 1.1) - W * 0.05;
        ctx.globalAlpha = e * alpha * (0.6 + 0.4 * J.r(seed, depth, i, 6)); ctx.beginPath(); ctx.arc(x, y, r, 0, TAU); ctx.fill();
      }
    });
    ctx.globalAlpha = e;
    // frost glints on the drift
    ctx.globalCompositeOperation = tone.dark ? 'screen' : 'source-over';
    for (let i = 0; i < 18; i++) {
      const pulse = Math.pow(Math.max(0, Math.sin(t * (0.7 + J.r(seed, i, 81)) + i * 2.9)), 4);
      ctx.fillStyle = rgba(tone.dark ? '#E8F3FF' : '#FFFFFF', 0.95 * pulse); star(ctx, J.r(seed, i, 82) * W, H * (0.87 + 0.12 * J.r(seed, i, 83)), U * 0.012 * (0.4 + pulse), 4, 0.12); ctx.fill();
    }
  });
  /* in front: large soft flakes drifting past the lens */
  K.registerFront('echoSnowFront', '雪の粒', (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, U = Umin(env), seed = P.seed || 1, tone = snowTone(sc), e = fade(env, 1.2);
    ctx.globalAlpha = e;
    for (let i = 0; i < 16; i++) {
      const r = U * (0.012 + 0.03 * J.r(seed, i, 1)), speed = H * (0.07 + 0.09 * J.r(seed, i, 2));
      const y = wrap(J.r(seed, i, 3) * H * 1.3 + t * speed, H * 1.3) - H * 0.15, x = wrap(J.r(seed, i, 4) * W + Math.sin(t * 0.5 + i) * U * 0.04, W);
      const gradient = ctx.createRadialGradient(x, y, 0, x, y, r);
      gradient.addColorStop(0, rgba(tone.flake, 0.5)); gradient.addColorStop(0.6, rgba(tone.flake, 0.16)); gradient.addColorStop(1, rgba(tone.flake, 0));
      ctx.fillStyle = gradient; ctx.beginPath(); ctx.arc(x, y, r, 0, TAU); ctx.fill();
    }
    if (K.luxe) K.luxe(ctx, env, { seed: (P && P.seed) || 1, tint: ['#DDEBFF', sc.accent2, '#FFFFFF'], strength: 0.75, bokeh: 4, glitter: 8 });
  });

  /* ================= 純喫茶 ================= */
  K.registerBg('echoCafe', '喫茶店', ['calm', 'editorial', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), dark = isDark(sc.bg), portrait = H > W, key = `cafe-${seed}-${colourKey(sc)}`;
    const wall = sc.bg, ink = dark ? sc.fg : sc.fg, warm = dark ? mix(sc.bg, sc.accent2, 0.9) : '#FFF6D8';
    const winX = W * (J.r(seed, 2) > 0.5 ? 0.76 : 0.24), winW = U * (portrait ? 0.4 : 0.34), winH = H * (portrait ? 0.34 : 0.5), winY = H * 0.16;
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}-room`, 0.75, (x, w, h) => {
      x.fillStyle = vertical(x, 0, h, [[0, dark ? mix(wall, '#000000', 0.22) : mix(wall, sc.fg, 0.06)], [0.55, wall], [1, dark ? mix(wall, '#000000', 0.12) : mix(wall, sc.fg, 0.08)]]); x.fillRect(0, 0, w, h);
      // damask-ish wallpaper: fine stripes and small lozenges
      x.strokeStyle = rgba(dark ? '#FFFFFF' : sc.fg, dark ? 0.035 : 0.045); x.lineWidth = Math.max(1, U * 0.002);
      for (let px = 0; px < w; px += U * 0.05) { x.beginPath(); x.moveTo(px, 0); x.lineTo(px, h * 0.74); x.stroke(); }
      x.fillStyle = rgba(dark ? '#FFFFFF' : sc.accent, dark ? 0.05 : 0.09);
      for (let py = U * 0.06; py < h * 0.72; py += U * 0.11) for (let px = U * 0.025 + ((Math.round(py / (U * 0.11)) % 2) * U * 0.025); px < w; px += U * 0.05) { x.beginPath(); x.moveTo(px, py - U * 0.008); x.lineTo(px + U * 0.005, py); x.lineTo(px, py + U * 0.008); x.lineTo(px - U * 0.005, py); x.closePath(); x.fill(); }
      // wainscot panel and a picture rail
      const rail = h * 0.72;
      x.fillStyle = vertical(x, rail, h, [[0, mix(wall, sc.fg, dark ? 0.0 : 0.16)], [1, mix(wall, sc.fg, dark ? 0.0 : 0.3)]]); if (dark) x.fillStyle = vertical(x, rail, h, [[0, mix(wall, '#000000', 0.2)], [1, mix(wall, '#000000', 0.35)]]);
      x.fillRect(0, rail, w, h - rail);
      x.fillStyle = rgba(dark ? '#FFFFFF' : '#FFFFFF', dark ? 0.16 : 0.5); x.fillRect(0, rail - U * 0.004, w, U * 0.008);
      x.strokeStyle = rgba('#000000', 0.12); x.lineWidth = Math.max(1, U * 0.0022);
      for (let px = U * 0.06; px < w - U * 0.05; px += U * 0.24) x.strokeRect(px, rail + U * 0.03, U * 0.2, h - rail - U * 0.08);
    }), 0, 0, W, H);
    // the window: an arch onto a sunlit street, trees swaying
    const wx0 = winX - winW / 2, arch = winW / 2, topY = winY + arch;
    ctx.save();
    ctx.beginPath(); ctx.moveTo(wx0, winY + winH); ctx.lineTo(wx0, topY); ctx.arc(winX, topY, arch, Math.PI, 0); ctx.lineTo(wx0 + winW, winY + winH); ctx.closePath(); ctx.clip();
    ctx.fillStyle = vertical(ctx, winY, winY + winH, [[0, dark ? mix(sc.accent2, '#0B1020', 0.6) : mix('#BFE3FF', sc.accent2, 0.15)], [1, dark ? mix(sc.accent2, '#000000', 0.5) : '#FFF4D6']]); ctx.fillRect(wx0, winY, winW, winH);
    glowDisc(ctx, winX + winW * 0.15, winY + winH * 0.25, winW * 0.9, '#FFFFFF', dark ? 0.2 : 0.75);
    for (let i = 0; i < 5; i++) {
      const bx = wx0 + winW * (0.08 + 0.2 * i) + Math.sin(t * 0.7 + i * 1.6) * U * 0.004, by = winY + winH * (0.66 + 0.08 * (i % 2)), br = winW * (0.17 + 0.05 * J.r(seed, i, 7));
      ctx.fillStyle = dark ? '#0F2A22' : mix('#4C8B5A', '#B9E39A', 0.2 + 0.25 * J.r(seed, i, 8)); ctx.beginPath(); ctx.arc(bx, by, br, 0, TAU); ctx.arc(bx + br * 0.7, by + br * 0.2, br * 0.8, 0, TAU); ctx.arc(bx - br * 0.6, by + br * 0.3, br * 0.75, 0, TAU); ctx.fill();
    }
    ctx.fillStyle = dark ? '#151B1A' : '#8C7B62'; ctx.fillRect(wx0, winY + winH * 0.9, winW, winH * 0.1);
    ctx.font = `italic 700 ${Math.round(winW * 0.19)}px "Georgia", "Times New Roman", serif`; ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    ctx.fillStyle = rgba(dark ? '#FFD98A' : '#B78A2A', 0.85); ctx.fillText('COFFEE', winX, winY + winH * 0.4);
    ctx.font = `600 ${Math.round(winW * 0.07)}px "Georgia", serif`; ctx.fillStyle = rgba(dark ? '#FFD98A' : '#B78A2A', 0.7); ctx.fillText('— JUN KISSA —', winX, winY + winH * 0.4 + winW * 0.16);
    ctx.restore();
    ctx.strokeStyle = dark ? mix(sc.accent, '#000000', 0.2) : mix(sc.fg, '#000000', 0.15); ctx.lineWidth = Math.max(2, U * 0.011); ctx.lineJoin = 'round';
    ctx.beginPath(); ctx.moveTo(wx0, winY + winH); ctx.lineTo(wx0, topY); ctx.arc(winX, topY, arch, Math.PI, 0); ctx.lineTo(wx0 + winW, winY + winH); ctx.closePath(); ctx.stroke();
    ctx.lineWidth = Math.max(1.5, U * 0.006); ctx.beginPath(); ctx.moveTo(winX, winY); ctx.lineTo(winX, winY + winH); ctx.moveTo(wx0, topY + winH * 0.22); ctx.lineTo(wx0 + winW, topY + winH * 0.22); ctx.stroke();
    ctx.fillStyle = dark ? mix(sc.bg, '#FFFFFF', 0.12) : '#FFFFFF'; ctx.fillRect(wx0 - U * 0.02, winY + winH, winW + U * 0.04, U * 0.014);
    // light through the glass across the room
    ctx.globalCompositeOperation = dark ? 'screen' : 'source-over';
    const dir = winX > W / 2 ? -1 : 1;
    for (let i = 0; i < 3; i++) {
      const x0 = winX - dir * winW * 0.3 + (i - 1) * winW * 0.34 + Math.sin(t * 0.09 + i * 2) * U * 0.02, skew = -dir * H * 0.62, w = winW * 0.3;
      const gradient = ctx.createLinearGradient(x0, winY + winH * 0.4, x0 + skew, H);
      gradient.addColorStop(0, rgba(warm, dark ? 0.2 : 0.6)); gradient.addColorStop(1, rgba(warm, 0));
      ctx.fillStyle = gradient; ctx.beginPath(); ctx.moveTo(x0, winY + winH * 0.4); ctx.lineTo(x0 + w, winY + winH * 0.4); ctx.lineTo(x0 + w + skew, H); ctx.lineTo(x0 + skew, H); ctx.closePath(); ctx.fill();
    }
    ctx.globalCompositeOperation = 'source-over';
    // pendant lamps
    [0.16, 0.5, 0.84].forEach((u, i) => {
      if (Math.abs(u * W - winX) < winW * 0.55) return;
      const lx = W * u, sway = Math.sin(t * 0.8 + i * 1.7) * U * 0.006, cord = H * (0.13 + 0.06 * (i % 2)), ly = cord, r = U * (i === 1 ? 0.05 : 0.04);
      ctx.strokeStyle = rgba(ink, 0.45); ctx.lineWidth = Math.max(1, U * 0.002); ctx.beginPath(); ctx.moveTo(lx, 0); ctx.lineTo(lx + sway, ly - r * 0.4); ctx.stroke();
      ctx.globalCompositeOperation = dark ? 'screen' : 'source-over';
      glowDisc(ctx, lx + sway, ly + r * 0.5, r * 4.2, warm, dark ? 0.4 : 0.55);
      const cone = ctx.createLinearGradient(0, ly + r * 0.3, 0, ly + r * 4.4); cone.addColorStop(0, rgba(warm, dark ? 0.18 : 0.32)); cone.addColorStop(1, rgba(warm, 0));
      ctx.fillStyle = cone; ctx.beginPath(); ctx.moveTo(lx + sway - r * 0.7, ly + r * 0.3); ctx.lineTo(lx + sway + r * 0.7, ly + r * 0.3); ctx.lineTo(lx + sway + r * 2.6, ly + r * 4.4); ctx.lineTo(lx + sway - r * 2.6, ly + r * 4.4); ctx.closePath(); ctx.fill();
      ctx.globalCompositeOperation = 'source-over';
      const shade = ctx.createLinearGradient(lx - r, ly - r * 0.4, lx + r, ly + r * 0.5); shade.addColorStop(0, mix(sc.accent, '#FFFFFF', 0.25)); shade.addColorStop(1, mix(sc.accent, '#000000', 0.3));
      ctx.fillStyle = shade; ctx.beginPath(); ctx.moveTo(lx + sway - r * 1.15, ly + r * 0.5); ctx.quadraticCurveTo(lx + sway - r * 1.05, ly - r * 0.7, lx + sway, ly - r * 0.7); ctx.quadraticCurveTo(lx + sway + r * 1.05, ly - r * 0.7, lx + sway + r * 1.15, ly + r * 0.5); ctx.closePath(); ctx.fill();
      ctx.fillStyle = rgba('#FFF6D8', 0.95); ctx.beginPath(); ctx.ellipse(lx + sway, ly + r * 0.5, r * 1.15, r * 0.18, 0, 0, TAU); ctx.fill();
    });
    // checkered floor in perspective
    const horizon = H * 0.83, rows = 6, cols = 20;
    for (let row = 0; row < rows; row++) {
      const y0 = horizon + (H - horizon) * Math.pow(row / rows, 1.7), y1 = horizon + (H - horizon) * Math.pow((row + 1) / rows, 1.7);
      const w0 = W * (0.25 + 1.4 * (y0 - horizon) / (H - horizon)), w1 = W * (0.25 + 1.4 * (y1 - horizon) / (H - horizon));
      for (let col = 0; col < cols; col++) {
        if ((row + col) % 2) continue;
        const a0 = (col / cols - 0.5) * w0 + W / 2, b0 = ((col + 1) / cols - 0.5) * w0 + W / 2, a1 = (col / cols - 0.5) * w1 + W / 2, b1 = ((col + 1) / cols - 0.5) * w1 + W / 2;
        ctx.fillStyle = rgba(dark ? '#FFFFFF' : sc.fg, dark ? 0.06 : 0.1); ctx.beginPath(); ctx.moveTo(a0, y0); ctx.lineTo(b0, y0); ctx.lineTo(b1, y1); ctx.lineTo(a1, y1); ctx.closePath(); ctx.fill();
      }
    }
    // a cup and a curl of steam in the corner
    const cx = W * (winX > W / 2 ? 0.16 : 0.84), cy = H * 0.965, cr = U * 0.055;
    ctx.fillStyle = rgba('#000000', 0.16); ctx.beginPath(); ctx.ellipse(cx, cy + cr * 0.05, cr * 1.5, cr * 0.28, 0, 0, TAU); ctx.fill();
    ctx.fillStyle = '#FFF9EA'; ctx.beginPath(); ctx.ellipse(cx, cy - cr * 0.05, cr * 1.4, cr * 0.24, 0, 0, TAU); ctx.fill();
    ctx.fillStyle = '#FFFFFF'; ctx.beginPath(); ctx.moveTo(cx - cr * 0.85, cy - cr * 0.85); ctx.lineTo(cx + cr * 0.85, cy - cr * 0.85); ctx.quadraticCurveTo(cx + cr * 0.8, cy, cx, cy); ctx.quadraticCurveTo(cx - cr * 0.8, cy, cx - cr * 0.85, cy - cr * 0.85); ctx.fill();
    ctx.strokeStyle = '#FFFFFF'; ctx.lineWidth = cr * 0.16; ctx.beginPath(); ctx.arc(cx + cr * 0.95, cy - cr * 0.55, cr * 0.32, -Math.PI / 2, Math.PI / 2); ctx.stroke();
    ctx.fillStyle = mix('#5A2E1A', '#000000', 0.2); ctx.beginPath(); ctx.ellipse(cx, cy - cr * 0.85, cr * 0.85, cr * 0.15, 0, 0, TAU); ctx.fill();
    ctx.lineCap = 'round'; ctx.lineWidth = Math.max(2, cr * 0.13);
    for (let s = 0; s < 3; s++) {
      const phase = wrap(t * 0.42 + s / 3, 1), sy = cy - cr * 1.0 - phase * cr * 3.6, sx = cx + (s - 1) * cr * 0.4;
      ctx.strokeStyle = rgba('#FFFFFF', Math.sin(phase * Math.PI) * (dark ? 0.5 : 0.7)); ctx.beginPath();
      ctx.moveTo(sx, sy); ctx.bezierCurveTo(sx + Math.sin(phase * 7 + s) * cr * 0.7, sy - cr * 0.5, sx - Math.sin(phase * 7 + s) * cr * 0.7, sy - cr, sx + Math.sin(phase * 5 + s) * cr * 0.5, sy - cr * 1.5); ctx.stroke();
    }
    // the awning
    const stripe = U * 0.07, height = H * (portrait ? 0.05 : 0.075), count = Math.ceil(W / stripe) + 1, cream = dark ? mix(sc.bg, sc.fg, 0.9) : mix(sc.bg, '#FFFFFF', 0.75);
    const shadeStrip = ctx.createLinearGradient(0, height, 0, height + H * 0.06); shadeStrip.addColorStop(0, rgba('#000000', dark ? 0.32 : 0.16)); shadeStrip.addColorStop(1, rgba('#000000', 0));
    ctx.fillStyle = shadeStrip; ctx.fillRect(0, height, W, H * 0.06);
    for (let i = 0; i < count; i++) {
      const x = i * stripe - stripe * 0.5, sway = Math.sin(t * 0.7 + i * 0.5) * U * 0.0015;
      ctx.fillStyle = i % 2 ? cream : sc.accent; ctx.beginPath(); ctx.moveTo(x, 0); ctx.lineTo(x + stripe, 0); ctx.lineTo(x + stripe, height); ctx.arc(x + stripe / 2, height + sway, stripe / 2, 0, Math.PI); ctx.lineTo(x, height); ctx.closePath(); ctx.fill();
    }
    // dust turning in the light
    for (let i = 0; i < 40; i++) {
      const y = H - wrap(J.r(seed, i, 1) * H * 1.2 + t * H * (0.008 + 0.02 * J.r(seed, i, 2)), H * 1.2) + H * 0.1, x = J.r(seed, i, 3) * W + Math.sin(t * 0.4 + i) * U * 0.02;
      ctx.fillStyle = rgba(dark ? sc.accent2 : '#FFFFFF', 0.25 + 0.4 * (0.5 + 0.5 * Math.sin(t + i))); ctx.beginPath(); ctx.arc(x, y, U * (0.0025 + 0.004 * J.r(seed, i, 4)), 0, TAU); ctx.fill();
    }
  });
  /* in front: old-film warmth: a soft vignette, drifting specks and a slow flicker of light */
  K.registerFront('echoCafeFilm', '古い映画', (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, U = Umin(env), seed = P.seed || 1, dark = isDark(sc.bg), e = fade(env, 1);
    ctx.globalAlpha = e;
    const vignette = ctx.createRadialGradient(W / 2, H / 2, U * 0.4, W / 2, H / 2, Math.hypot(W, H) * 0.6);
    vignette.addColorStop(0, rgba('#3B1E0A', 0)); vignette.addColorStop(1, rgba('#3B1E0A', dark ? 0.32 : 0.2));
    ctx.fillStyle = vignette; ctx.fillRect(0, 0, W, H);
    const step = Math.floor(t * 12);
    ctx.fillStyle = rgba('#FFF3D6', dark ? 0.5 : 0.7);
    for (let i = 0; i < 14; i++) {
      if (J.r(seed, step, i, 1) > 0.5) continue;
      ctx.beginPath(); ctx.arc(J.r(seed, step, i, 2) * W, J.r(seed, step, i, 3) * H, U * (0.001 + 0.0025 * J.r(seed, step, i, 4)), 0, TAU); ctx.fill();
    }
    ctx.strokeStyle = rgba('#FFF3D6', 0.16); ctx.lineWidth = Math.max(1, U * 0.0012);
    if (J.r(seed, step, 90) > 0.82) { const sx = J.r(seed, step, 91) * W; ctx.beginPath(); ctx.moveTo(sx, 0); ctx.lineTo(sx + (J.r(seed, step, 92) - 0.5) * U * 0.02, H); ctx.stroke(); }
    ctx.globalCompositeOperation = 'screen'; ctx.fillStyle = rgba('#FFE6B0', 0.03 + 0.02 * Math.sin(t * 9) * Math.sin(t * 2.3)); ctx.fillRect(0, 0, W, H);
    if (K.luxe) K.luxe(ctx, env, { seed: (P && P.seed) || 1, tint: ['#FFD9A0', sc.accent, '#FFF3D8'], strength: 0.75, bokeh: 4, glitter: 8 });
  });
})();
