/* ECHO first-party backdrops for the default look 夜航: 星海航行 (a small steamer under the Milky Way), オーロラ (curtains of light over a frozen lake),
   星図 (a rotating planisphere), 銀河鉄道 (a night train crossing a shallow sea). The light colourway turns each into its daybreak twin. */
(() => {
  'use strict';
  const K = J.echo, TAU = K.TAU, clamp = K.clamp, mix = K.mix, rgba = K.rgba, paint = K.paint;
  const { U: Umin, isDark, wrap, glowDisc, star, vertical, fade, layer, scroll } = K;
  const r = J.r, ck = sc => [sc.bg, sc.fg, sc.accent, sc.accent2].join('');

  const nightTone = sc => {
    const dark = isDark(sc.bg);
    return dark
      ? { dark, sky0: mix(sc.bg, '#010208', 0.6), sky1: sc.bg, sky2: mix(sc.bg, sc.accent2, 0.3), sea0: mix(sc.bg, '#010208', 0.35), sea1: mix(sc.bg, '#010208', 0.7), lamp: mix(sc.accent, '#FFE9B0', 0.45), glow: mix(sc.accent2, '#FFFFFF', 0.6), ink: mix(sc.bg, '#000000', 0.78), line: mix(sc.accent2, '#FFFFFF', 0.4) }
      : { dark, sky0: mix(sc.bg, sc.accent2, 0.42), sky1: mix(sc.bg, '#FFFFFF', 0.4), sky2: mix(sc.bg, sc.accent, 0.42), sea0: mix(sc.bg, sc.accent2, 0.35), sea1: mix(sc.bg, sc.accent2, 0.6), lamp: sc.accent, glow: '#FFF6DC', ink: mix(sc.fg, sc.bg, 0.12), line: mix(sc.fg, sc.bg, 0.4) };
  };
  /* sky with a Milky Way band of thousands of stars and a few nebulous blooms */
  const milkyWay = (x, w, h, seed, U, tone, sc, limit = 1) => {
    if (!tone.dark) { paint.stars(x, w, h, seed, U, 30, 0.4, 0.25); return; }
    const ang = -0.5 + 0.3 * r(seed, 90), cx = w * 0.5, cy = h * 0.32 * limit, nx = -Math.sin(ang), ny = Math.cos(ang), tx = Math.cos(ang), ty = Math.sin(ang);
    x.globalCompositeOperation = 'screen';
    for (let i = 0; i < 9; i++) {
      const at = (i / 8 - 0.5) * Math.hypot(w, h) * 0.9, px = cx + tx * at, py = cy + ty * at, rr = U * (0.28 + 0.2 * r(seed, i, 91)), g = x.createRadialGradient(px, py, 0, px, py, rr), c = i % 3 === 1 ? sc.accent : sc.accent2;
      g.addColorStop(0, rgba(c, 0.13)); g.addColorStop(0.5, rgba(mix(c, '#FFFFFF', 0.3), 0.05)); g.addColorStop(1, rgba(c, 0));
      x.fillStyle = g; x.beginPath(); x.arc(px, py, rr, 0, TAU); x.fill();
    }
    for (let i = 0; i < 1500; i++) {
      const at = (r(seed, i, 1) - 0.5) * Math.hypot(w, h) * 0.95, off = (r(seed, i, 2) + r(seed, i, 3) + r(seed, i, 4) - 1.5) * U * 0.32, px = cx + tx * at + nx * off, py = cy + ty * at + ny * off;
      if (py > h * 0.7 * limit) continue;
      x.fillStyle = rgba('#FFFFFF', (0.1 + 0.55 * Math.pow(r(seed, i, 5), 3)) * (1 - Math.abs(off) / (U * 0.5))); x.fillRect(px, py, Math.max(1, U * 0.0012), Math.max(1, U * 0.0012));
    }
    x.globalCompositeOperation = 'source-over';
    paint.stars(x, w, h, seed + 3, U, 140, 0.68 * limit);
    for (let i = 0; i < 26; i++) {
      const px = r(seed, i, 11) * w, py = r(seed, i, 12) * h * 0.55 * limit, rr = U * (0.006 + 0.012 * r(seed, i, 13));
      glowDisc(x, px, py, rr * 3, i % 3 ? '#FFFFFF' : sc.accent, 0.28);
      x.fillStyle = 'rgba(255,255,255,0.9)'; star(x, px, py, rr, 4, 0.1); x.fill();
    }
  };
  const twinkle = (ctx, t, W, H, U, seed, tone, count = 34, limit = 0.5) => {
    ctx.globalCompositeOperation = 'screen';
    for (let i = 0; i < count; i++) {
      const b = Math.pow(Math.max(0, Math.sin(t * (0.7 + r(seed, i, 61)) + i * 2.3)), 3), px = r(seed, i, 62) * W, py = r(seed, i, 63) * H * limit, s = U * (0.008 + 0.014 * r(seed, i, 64)) * (0.3 + b);
      ctx.fillStyle = rgba(tone.dark ? '#FFFFFF' : '#FFF6DC', (tone.dark ? 0.85 : 0.7) * b); star(ctx, px, py, s, 4, 0.12); ctx.fill();
    }
    ctx.globalCompositeOperation = 'source-over';
  };
  const shooting = (ctx, t, W, H, U, seed, tone, cycle = 7) => {
    const k = Math.floor((t + r(seed, 91) * cycle) / cycle), ph = wrap(t + r(seed, 91) * cycle, cycle) / 1.0;
    if (ph >= 1) return;
    const x0 = W * (0.1 + 0.6 * r(seed, k, 92)), y0 = H * (0.03 + 0.2 * r(seed, k, 93)), dx = U * 0.6 * ph, dy = U * 0.26 * ph;
    const g = ctx.createLinearGradient(x0 + dx, y0 + dy, x0 + dx - U * 0.3, y0 + dy - U * 0.13);
    g.addColorStop(0, rgba('#FFFFFF', 0.95 * (1 - ph))); g.addColorStop(1, rgba('#FFFFFF', 0));
    ctx.strokeStyle = g; ctx.lineWidth = Math.max(1.5, U * 0.003); ctx.lineCap = 'round'; ctx.beginPath(); ctx.moveTo(x0 + dx, y0 + dy); ctx.lineTo(x0 + dx - U * 0.3, y0 + dy - U * 0.13); ctx.stroke();
  };

  /* ================= 星海航行 ================= */
  const steamer = (x, s, hull, lit, lamp) => {
    x.fillStyle = hull;
    x.beginPath(); x.moveTo(-s * 0.5, -s * 0.13); x.lineTo(s * 0.56, -s * 0.13); x.lineTo(s * 0.46, 0); x.lineTo(-s * 0.4, 0); x.closePath(); x.fill();
    x.fillRect(-s * 0.3, -s * 0.25, s * 0.6, s * 0.12); x.fillRect(-s * 0.18, -s * 0.36, s * 0.34, s * 0.11); x.fillRect(-s * 0.02, -s * 0.43, s * 0.14, s * 0.07);
    x.beginPath(); x.moveTo(-s * 0.16, -s * 0.36); x.lineTo(-s * 0.1, -s * 0.36); x.lineTo(-s * 0.08, -s * 0.56); x.lineTo(-s * 0.15, -s * 0.56); x.fill();       /* funnel */
    x.fillRect(s * 0.34, -s * 0.66, s * 0.012, s * 0.53); x.fillRect(-s * 0.44, -s * 0.5, s * 0.01, s * 0.37);                                                    /* masts */
    x.strokeStyle = hull; x.lineWidth = Math.max(0.6, s * 0.004); x.beginPath(); x.moveTo(s * 0.34, -s * 0.64); x.lineTo(-s * 0.44, -s * 0.5); x.moveTo(s * 0.35, -s * 0.64); x.lineTo(s * 0.56, -s * 0.13); x.stroke();
    if (!lit) return;
    x.fillStyle = lamp;
    for (let i = 0; i < 11; i++) x.fillRect(-s * 0.27 + i * s * 0.05, -s * 0.22, s * 0.022, s * 0.032);
    for (let i = 0; i < 6; i++) x.fillRect(-s * 0.15 + i * s * 0.05, -s * 0.33, s * 0.02, s * 0.03);
    for (let i = 0; i < 10; i++) { x.beginPath(); x.arc(-s * 0.36 + i * s * 0.09, -s * 0.075, s * 0.011, 0, TAU); x.fill(); }
    x.fillStyle = '#FF6A5A'; x.beginPath(); x.arc(s * 0.35, -s * 0.66, s * 0.014, 0, TAU); x.fill();
  };
  K.registerBg('echoVoyage', '星海航行', ['calm', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), tone = nightTone(sc), key = `vy|${seed}|${ck(sc)}`, hz = H * 0.62, portrait = H > W;
    const mx = W * (portrait ? 0.7 : 0.26 + 0.18 * r(seed, 1)), my = H * (portrait ? 0.2 : 0.22);
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|sky`, 0.6, (x, w, h) => {
      x.fillStyle = vertical(x, 0, hz, [[0, tone.sky0], [0.62, tone.sky1], [1, tone.sky2]]); x.fillRect(0, 0, w, hz + 2);
      milkyWay(x, w, hz, seed, U, tone, sc, 1);
      glowDisc(x, mx, my, U * 0.85, tone.glow, tone.dark ? 0.42 : 0.8);
      x.fillStyle = tone.dark ? '#FBFAF0' : '#FFFFFF'; x.beginPath(); x.arc(mx, my, U * (tone.dark ? 0.06 : 0.07), 0, TAU); x.fill();
      if (tone.dark) { x.fillStyle = rgba('#B8B8CC', 0.4); [[-0.02, -0.012, 0.013], [0.018, 0.018, 0.01], [0.005, -0.03, 0.007], [-0.03, 0.02, 0.006]].forEach(([dx, dy, s]) => { x.beginPath(); x.arc(mx + dx * U, my + dy * U, U * s, 0, TAU); x.fill(); }); }
      /* far island and lighthouse */
      x.fillStyle = mix(tone.sky1, tone.ink, tone.dark ? 0.7 : 0.5); x.beginPath(); x.moveTo(w * 0.62, hz + 1); x.quadraticCurveTo(w * 0.74, hz - U * 0.055, w * 0.88, hz + 1); x.fill();
      x.fillStyle = tone.ink; x.fillRect(w * 0.775, hz - U * 0.1, U * 0.009, U * 0.052); x.fillRect(w * 0.7735, hz - U * 0.108, U * 0.012, U * 0.01);
    }), 0, 0, W, H);
    twinkle(ctx, t, W, H, U, seed, tone, 30, 0.5); shooting(ctx, t, W, H, U, seed, tone, 8);
    /* lighthouse beam swinging over the sea */
    if (tone.dark) { const bx = W * (portrait ? 0.6 : 0.775) + U * 0.004, by = hz - U * 0.104, a = Math.sin(t * 0.55) * 0.5 + Math.PI * (bx > W / 2 ? 1 : 0), beam = ctx.createLinearGradient(bx, by, bx + Math.cos(a) * W, by + Math.sin(a) * W * 0.05); ctx.globalCompositeOperation = 'screen'; beam.addColorStop(0, rgba(tone.lamp, 0.35)); beam.addColorStop(1, rgba(tone.lamp, 0)); ctx.fillStyle = beam; ctx.beginPath(); ctx.moveTo(bx, by); ctx.lineTo(bx + Math.cos(a) * W, by + Math.sin(a) * W * 0.05 - U * 0.03); ctx.lineTo(bx + Math.cos(a) * W, by + Math.sin(a) * W * 0.05 + U * 0.03); ctx.fill(); ctx.globalCompositeOperation = 'source-over'; glowDisc(ctx, bx, by, U * 0.03, tone.lamp, 0.8); }
    /* sea */
    ctx.fillStyle = vertical(ctx, hz, H, [[0, tone.sea0], [1, tone.sea1]]); ctx.fillRect(0, hz, W, H - hz);
    ctx.fillStyle = vertical(ctx, hz, hz + U * 0.06, [[0, rgba(tone.glow, tone.dark ? 0.16 : 0.4)], [1, rgba(tone.glow, 0)]]); ctx.fillRect(0, hz, W, U * 0.06);
    ctx.globalCompositeOperation = 'screen';
    for (let i = 0; i < 46; i++) {                                     /* the path of the moon */
      const v = i / 46, y = hz + (H - hz) * (0.02 + 0.9 * v), hw = U * (0.008 + 0.11 * v) * (0.4 + r(seed, i, 8)), b = 0.5 + 0.5 * Math.sin(t * (0.9 + r(seed, i, 9)) + i * 2.3);
      ctx.fillStyle = rgba(tone.glow, (tone.dark ? 0.2 : 0.34) * b * (1 - v * 0.45)); ctx.fillRect(mx - hw + (r(seed, i, 10) - 0.5) * U * 0.05, y, hw * 2, Math.max(1, U * (0.002 + 0.003 * v)));
    }
    ctx.globalCompositeOperation = 'source-over';
    ctx.lineCap = 'round';
    for (let i = 0; i < 52; i++) {
      const v = r(seed, i, 21), y = hz + (H - hz) * (0.03 + 0.95 * v * v), l = U * (0.03 + 0.16 * v), x0 = wrap(r(seed, i, 22) * W * 1.3 + t * U * (0.006 + 0.02 * v), W * 1.3) - W * 0.15;
      ctx.strokeStyle = rgba(tone.dark ? tone.glow : '#FFFFFF', (tone.dark ? 0.16 : 0.34) * (0.4 + v)); ctx.lineWidth = Math.max(1, U * (0.0014 + 0.0028 * v));
      ctx.beginPath(); ctx.moveTo(x0, y); ctx.quadraticCurveTo(x0 + l / 2, y - U * 0.004 * (1 + v), x0 + l, y); ctx.stroke();
    }
    /* the ship crosses very slowly: hull, lit windows, smoke, wake, and its lights doubled in the water */
    const sp = (t / 70 + r(seed, 3)) % 1, sx = W * (-0.15 + 1.3 * sp), sy = hz + U * 0.02 + Math.sin(t * 1.2) * U * 0.002, ss = U * 0.3;
    ctx.save(); ctx.translate(sx, sy); ctx.rotate(Math.sin(t * 0.9) * 0.008); steamer(ctx, ss, tone.ink, true, tone.lamp); ctx.restore();
    ctx.save(); ctx.translate(sx, sy + U * 0.005); ctx.scale(1, -0.5); ctx.globalAlpha = e * 0.28; steamer(ctx, ss, tone.ink, true, tone.lamp); ctx.restore(); ctx.globalAlpha = e;
    ctx.globalCompositeOperation = 'screen';
    for (let i = 0; i < 9; i++) { const lx = sx - ss * 0.3 + i * ss * 0.075, wob = Math.sin(t * 2 + i) * U * 0.004; ctx.fillStyle = rgba(tone.lamp, (tone.dark ? 0.32 : 0.18)); ctx.fillRect(lx + wob, sy + U * 0.006, U * 0.004, U * (0.03 + 0.03 * ((i * 7) % 5) / 5)); }
    glowDisc(ctx, sx - ss * 0.08, sy - ss * 0.2, ss * 0.5, tone.lamp, tone.dark ? 0.16 : 0.06);
    ctx.globalCompositeOperation = 'source-over';
    for (let i = 0; i < 16; i++) {                                      /* smoke */
      const age = wrap(t * 0.5 - i / 16, 1), px = sx - ss * 0.12 - age * ss * 1.1, py = sy - ss * 0.56 - age * ss * 0.18 + Math.sin(age * 5 + i) * ss * 0.01, rr = ss * (0.018 + 0.05 * age);
      ctx.fillStyle = rgba(tone.dark ? mix(tone.sky1, '#FFFFFF', 0.22) : mix(tone.sky1, tone.ink, 0.35), 0.4 * (1 - age)); ctx.beginPath(); ctx.arc(px, py, rr, 0, TAU); ctx.fill();
    }
    ctx.strokeStyle = rgba('#FFFFFF', 0.35); ctx.lineWidth = Math.max(1, U * 0.002);
    for (let k = 0; k < 2; k++) { ctx.beginPath(); ctx.moveTo(sx - ss * 0.42, sy + U * 0.006); ctx.quadraticCurveTo(sx - ss * 0.9, sy + U * (0.012 + k * 0.01), sx - ss * 1.7, sy + U * (0.006 + k * 0.014)); ctx.stroke(); }
    /* the near swell */
    ctx.fillStyle = tone.dark ? mix(tone.sea1, '#000000', 0.35) : mix(tone.sea1, tone.ink, 0.3);
    ctx.beginPath(); ctx.moveTo(0, H); for (let px = 0; px <= W + 8; px += 8) ctx.lineTo(px, H * 0.94 + Math.sin(px / (U * 0.14) + t * 0.7) * U * 0.012 + Math.sin(px / (U * 0.05) + t * 1.3) * U * 0.004); ctx.lineTo(W, H); ctx.fill();
    ctx.strokeStyle = rgba(tone.glow, tone.dark ? 0.22 : 0.4); ctx.lineWidth = Math.max(1, U * 0.002); ctx.beginPath(); for (let px = 0; px <= W + 8; px += 8) { const y = H * 0.94 + Math.sin(px / (U * 0.14) + t * 0.7) * U * 0.012 + Math.sin(px / (U * 0.05) + t * 1.3) * U * 0.004; px ? ctx.lineTo(px, y) : ctx.moveTo(px, y); } ctx.stroke();
  });

  /* ================= オーロラ ================= */
  const ribbonSprite = (ctx, c0, c1) => K.sprite(ctx, `au|${c0}|${c1}`, 4, 256, (g, w, h) => {
    const gr = g.createLinearGradient(0, 0, 0, h);
    gr.addColorStop(0, rgba(c1, 0)); gr.addColorStop(0.35, rgba(c1, 0.25)); gr.addColorStop(0.78, rgba(c0, 0.7)); gr.addColorStop(0.95, rgba(mix(c0, '#FFFFFF', 0.5), 0.95)); gr.addColorStop(1, rgba(c0, 0));
    g.fillStyle = gr; g.fillRect(0, 0, w, h);
  });
  let auroraBuf = null, auroraStamp = '', auroraSprite = null;
  const curtains = (ctx, t, W, H, U, seed, sprites, base, alphaK) => {
    const bw = Math.max(64, Math.round(W / 6)), bh = Math.max(48, Math.round(H / 6));
    if (!auroraBuf) auroraBuf = document.createElement('canvas');
    if (auroraBuf.width !== bw || auroraBuf.height !== bh) { auroraBuf.width = bw; auroraBuf.height = bh; auroraStamp = ''; }
    const stamp = `${t}|${seed}|${base}|${sprites[0].width}`;
    if (stamp !== auroraStamp || auroraSprite !== sprites[0]) {
      auroraSprite = sprites[0];            /* the reflection reuses the curtains already painted for this frame */
      auroraStamp = stamp;
      const g = auroraBuf.getContext('2d'); g.setTransform(1, 0, 0, 1, 0, 0); g.clearRect(0, 0, bw, bh); g.globalCompositeOperation = 'lighter';
      sprites.forEach((spr, k) => {
        const n = bw, sw = 1;
        for (let i = 0; i < n; i++) {
          const u = i / n, x = u * bw, w1 = Math.sin(u * 5 + t * 0.22 + k * 2 + r(seed, k, 1) * 6) + 0.6 * Math.sin(u * 11 - t * 0.31 + k), w2 = Math.sin(u * 3.2 - t * 0.16 + k * 1.7 + r(seed, k, 2) * 6);
          const y = (base + (k - 1) * 0.07 + w1 * 0.028) * bh, hgt = bh * (0.2 + 0.12 * (0.5 + 0.5 * w2) + 0.02 * Math.sin(u * 17 + t * 0.6)) * (1 - 0.14 * k);
          g.globalAlpha = clamp(0.55 + 0.5 * Math.sin(u * 4.7 + t * 0.3 + k * 3));
          g.drawImage(spr, 0, 0, spr.width, spr.height, x, y - hgt, sw + 0.6, hgt);
        }
      });
    }
    ctx.save(); ctx.globalCompositeOperation = 'screen'; ctx.globalAlpha *= alphaK; ctx.imageSmoothingEnabled = true; ctx.imageSmoothingQuality = 'low';
    ctx.drawImage(auroraBuf, 0, 0, bw, bh, 0, 0, W, H); ctx.restore();
  };
  K.registerBg('echoAurora', 'オーロラ', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), tone = nightTone(sc), key = `ar|${seed}|${ck(sc)}`, hz = H * 0.72;
    ctx.globalAlpha = e;
    const greens = tone.dark ? [mix(sc.accent2, '#4CFFB0', 0.55), mix(sc.accent, '#9A6BFF', 0.5), mix(sc.accent2, '#40C8FF', 0.4)] : [mix(sc.accent2, '#FFFFFF', 0.25), mix(sc.accent, '#FFFFFF', 0.2), mix(sc.accent, sc.accent2, 0.5)];
    ctx.drawImage(layer(env, `${key}|sky`, 0.6, (x, w, h) => {
      x.fillStyle = vertical(x, 0, hz, [[0, tone.sky0], [0.7, tone.sky1], [1, mix(tone.sky1, tone.sky2, 0.7)]]); x.fillRect(0, 0, w, hz + 2);
      if (tone.dark) paint.stars(x, w, hz, seed, U, 260, 0.9); else paint.stars(x, w, hz, seed, U, 50, 0.6, 0.35);
    }), 0, 0, W, H);
    const sprites = greens.map((c, i) => ribbonSprite(ctx, c, greens[(i + 1) % 3]));
    curtains(ctx, t, W, H, U, seed, sprites, 0.44, tone.dark ? 0.8 : 0.5);
    twinkle(ctx, t, W, H, U, seed, tone, 24, 0.5);
    /* the frozen lake mirrors the sky */
    ctx.fillStyle = vertical(ctx, hz, H, [[0, mix(tone.sea0, greens[0], tone.dark ? 0.16 : 0.2)], [1, tone.sea1]]); ctx.fillRect(0, hz, W, H - hz);
    ctx.save(); ctx.beginPath(); ctx.rect(0, hz, W, H - hz); ctx.clip(); ctx.translate(0, hz * 1.62 + U * 0.02); ctx.scale(1, -0.62);
    curtains(ctx, t, W, H, U, seed, sprites, 0.44, tone.dark ? 0.4 : 0.26); ctx.restore();
    ctx.strokeStyle = rgba('#FFFFFF', tone.dark ? 0.1 : 0.4); ctx.lineWidth = Math.max(1, U * 0.0016);
    for (let i = 0; i < 26; i++) { const y = hz + (H - hz) * (0.05 + 0.9 * r(seed, i, 31)), x0 = r(seed, i, 32) * W, l = U * (0.08 + 0.3 * r(seed, i, 33)); ctx.beginPath(); ctx.moveTo(x0, y); ctx.lineTo(x0 + l, y + U * 0.002 * Math.sin(i)); ctx.stroke(); }
    /* firs along the shore, a cabin with its window lit */
    ctx.drawImage(layer(env, `${key}|shore`, 0.6, (x, w, h) => {
      const dark = tone.ink;
      paint.ridge(x, w, hz + U * 0.012, hz + U * 0.012, U * 0.09, paint.profile(seed, 5, [[1, 0.5], [3, 0.3], [7, 0.2]]), mix(tone.sea1, dark, 0.5));
      for (let i = 0; i < 34; i++) { const px = w * (i + r(seed, i, 41) * 0.8) / 34, hh = U * (0.06 + 0.16 * Math.pow(r(seed, i, 42), 1.6)) * (px < w * 0.3 || px > w * 0.72 ? 1.25 : 0.6); paint.fir(x, px, hz + U * 0.012, hh, dark, 7); }
      const cx = w * 0.56, cy = hz + U * 0.006; x.fillStyle = dark; x.fillRect(cx - U * 0.035, cy - U * 0.03, U * 0.07, U * 0.03); x.beginPath(); x.moveTo(cx - U * 0.042, cy - U * 0.03); x.lineTo(cx, cy - U * 0.058); x.lineTo(cx + U * 0.042, cy - U * 0.03); x.fill();
      x.fillStyle = tone.lamp; x.fillRect(cx - U * 0.018, cy - U * 0.022, U * 0.012, U * 0.014); x.fillRect(cx + U * 0.006, cy - U * 0.022, U * 0.012, U * 0.014);
    }), 0, 0, W, H);
    ctx.globalCompositeOperation = 'screen'; glowDisc(ctx, W * 0.56, hz - U * 0.012, U * 0.09, tone.lamp, tone.dark ? 0.4 : 0.15); ctx.globalCompositeOperation = 'source-over';
    for (let i = 0; i < 18; i++) {                                     /* chimney smoke */
      const age = wrap(t * 0.18 + i / 18, 1), px = W * 0.56 + U * 0.02 + age * U * 0.05 + Math.sin(age * 6 + i) * U * 0.006, py = hz - U * 0.06 - age * U * 0.16;
      ctx.fillStyle = rgba(mix(tone.sky1, '#FFFFFF', 0.3), 0.3 * (1 - age)); ctx.beginPath(); ctx.arc(px, py, U * (0.004 + 0.012 * age), 0, TAU); ctx.fill();
    }
    shooting(ctx, t, W, H, U, seed, tone, 9);
  });

  /* Eclipse field is owned by jizuraBackdropsEchoLightFields.js. */

  /* ================= 銀河鉄道 ================= */
  const carriage = (x, w, h, body, lamp, lit, k) => {
    x.fillStyle = body; x.beginPath(); x.roundRect(0, -h, w, h, h * 0.12); x.fill();
    x.fillRect(w * 0.06, -h * 1.06, w * 0.88, h * 0.08);
    if (lit) { x.fillStyle = lamp; for (let i = 0; i < 6; i++) if ((i * 7 + k * 3) % 11 !== 4) x.fillRect(w * (0.08 + i * 0.15), -h * 0.78, w * 0.1, h * 0.36); }
  };
  K.registerBg('echoNightTrain', '銀河鉄道', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), tone = nightTone(sc), key = `nt|${seed}|${ck(sc)}`, hz = H * 0.6;
    ctx.globalAlpha = e;
    const world = layer(env, `${key}|world`, 0.6, (x, w, h) => {
      x.fillStyle = vertical(x, 0, hz, [[0, tone.sky0], [0.65, tone.sky1], [1, tone.sky2]]); x.fillRect(0, 0, w, hz + 2);
      milkyWay(x, w, hz, seed + 4, U, tone, sc, 1);
      x.fillStyle = mix(tone.sky1, tone.ink, tone.dark ? 0.65 : 0.4); paint.ridge(x, w, hz + 2, hz + 1, hz * 0.09, paint.profile(seed, 8, [[1, 0.4], [3, 0.3], [8, 0.2], [17, 0.1]]), x.fillStyle);
      /* the trestle: a low embankment with poles */
      x.fillStyle = tone.ink; x.fillRect(0, hz - U * 0.03, w, U * 0.03 + 2); x.fillStyle = rgba(tone.glow, tone.dark ? 0.35 : 0.5); x.fillRect(0, hz - U * 0.03, w, Math.max(1, U * 0.002));
      for (let i = 0; i < 10; i++) { x.fillStyle = tone.ink; x.fillRect(w * (i + 0.5) / 10, hz - U * 0.085, U * 0.003, U * 0.06); x.fillRect(w * (i + 0.5) / 10 - U * 0.012, hz - U * 0.085, U * 0.027, U * 0.002); }
    });
    ctx.drawImage(world, 0, 0, W, H);
    twinkle(ctx, t, W, H, U, seed, tone, 28, 0.5); shooting(ctx, t, W, H, U, seed, tone, 10);
    /* water */
    ctx.fillStyle = vertical(ctx, hz, H, [[0, tone.sea0], [1, tone.sea1]]); ctx.fillRect(0, hz, W, H - hz);
    ctx.save(); ctx.beginPath(); ctx.rect(0, hz, W, H - hz); ctx.clip(); ctx.translate(0, hz * 2); ctx.scale(1, -1); ctx.globalAlpha = e * (tone.dark ? 0.5 : 0.55);
    for (let strip = 0; strip < 12; strip++) { const y0 = hz * strip / 12, sy = y0 / H * world.height, sh = hz / 12 / H * world.height, wob = Math.sin(t * 1.1 + strip * 1.9) * U * 0.004 * (1 + (12 - strip) * 0.1); ctx.drawImage(world, 0, sy, world.width, sh + 1, wob, y0 - 0.5, W, hz / 12 + 1); }
    ctx.restore(); ctx.globalAlpha = e;
    ctx.fillStyle = vertical(ctx, hz, H, [[0, rgba(tone.sea0, 0.15)], [1, rgba(tone.sea1, tone.dark ? 0.7 : 0.35)]]); ctx.fillRect(0, hz, W, H - hz);
    /* the train, with a pause at the middle of every crossing */
    const cycle = 22, ph = wrap(t + r(seed, 4) * cycle, cycle) / cycle, run = ph < 0.5 ? ph / 0.5 : (ph - 0.5) / 0.5, dir = ph < 0.5 ? 1 : -1;
    const cars = 6, cw = U * 0.34, chh = U * 0.075, locoW = U * 0.15, gap = U * 0.008, total = locoW + cars * (cw + gap);
    const ease = run * run * (3 - 2 * run), front = dir > 0 ? -total * 0.02 + (W + total * 1.05) * ease : W + total * 0.02 - (W + total * 1.05) * ease;
    const train = (ctx2, alpha) => {
      ctx2.save(); ctx2.globalAlpha *= alpha;
      ctx2.fillStyle = tone.ink; ctx2.beginPath(); ctx2.roundRect(dir > 0 ? front - locoW : front, hz - U * 0.03 - chh * 1.25, locoW, chh * 1.25, chh * 0.2); ctx2.fill();
      ctx2.fillRect((dir > 0 ? front - locoW * 0.75 : front + locoW * 0.55), hz - U * 0.03 - chh * 1.7, locoW * 0.16, chh * 0.5);
      ctx2.fillStyle = tone.lamp; ctx2.fillRect(dir > 0 ? front - locoW * 0.6 : front + locoW * 0.1, hz - U * 0.03 - chh * 0.95, locoW * 0.5, chh * 0.35);
      for (let c = 0; c < cars; c++) {
        const off = c * (cw + gap), x0 = dir > 0 ? front - locoW - gap - off - cw : front + locoW + gap + off;
        ctx2.save(); ctx2.translate(x0, hz - U * 0.03); carriage(ctx2, cw, chh, tone.ink, tone.lamp, true, c); ctx2.restore();
      }
      ctx2.restore();
    };
    if (ph > 0.001 && ph < 0.999) {
      train(ctx, 1);
      ctx.save(); ctx.translate(0, hz * 2 - U * 0.06 + U * 0.006); ctx.scale(1, -1); train(ctx, 0.3); ctx.restore();
      ctx.globalCompositeOperation = 'screen';
      const gx = front - dir * total * 0.5; glowDisc(ctx, gx, hz - U * 0.05, total * 0.6, tone.lamp, tone.dark ? 0.12 : 0.05);
      for (let i = 0; i < 14; i++) { const age = wrap(t * 0.6 + i / 14, 1), sx = front - dir * (locoW * 0.7 + age * U * 0.4), sy = hz - U * 0.03 - chh * 1.75 - age * U * 0.12; ctx.fillStyle = rgba(tone.dark ? '#FFFFFF' : tone.ink, 0.25 * (1 - age)); ctx.beginPath(); ctx.arc(sx, sy, U * (0.004 + 0.012 * age), 0, TAU); ctx.fill(); }
      ctx.globalCompositeOperation = 'source-over';
    }
    /* spirits of light rising from the water, ripples, reeds */
    ctx.globalCompositeOperation = 'screen';
    for (let i = 0; i < 24; i++) { const age = wrap(t * (0.03 + 0.03 * r(seed, i, 81)) + r(seed, i, 82), 1), px = r(seed, i, 83) * W + Math.sin(t * 0.5 + i) * U * 0.03, py = H - age * (H - hz + U * 0.35), b = Math.sin(Math.PI * age); ctx.fillStyle = rgba(i % 3 ? tone.lamp : tone.glow, (tone.dark ? 0.85 : 0.5) * b); ctx.beginPath(); ctx.arc(px, py, U * (0.002 + 0.003 * r(seed, i, 84)), 0, TAU); ctx.fill(); glowDisc(ctx, px, py, U * 0.016, tone.lamp, 0.25 * b); }
    ctx.globalCompositeOperation = 'source-over';
    ctx.strokeStyle = rgba('#FFFFFF', tone.dark ? 0.12 : 0.4); ctx.lineWidth = Math.max(1, U * 0.002); ctx.lineCap = 'round';
    for (let i = 0; i < 30; i++) { const v = r(seed, i, 21), y = hz + (H - hz) * (0.08 + 0.9 * v * v), l = U * (0.03 + 0.14 * v), x0 = wrap(r(seed, i, 22) * W * 1.3 + t * U * (0.006 + 0.02 * v), W * 1.3) - W * 0.15; ctx.beginPath(); ctx.moveTo(x0, y); ctx.quadraticCurveTo(x0 + l / 2, y - U * 0.004, x0 + l, y); ctx.stroke(); }
    ctx.strokeStyle = tone.ink; ctx.lineWidth = Math.max(1.5, U * 0.004);
    for (let i = 0; i < 40; i++) { const left = i < 22, px = left ? W * 0.14 * r(seed, i, 61) : W * (0.86 + 0.14 * r(seed, i, 61)), h0 = U * (0.09 + 0.17 * r(seed, i, 62)), base = H + U * 0.01, lean = (left ? 1 : -1) * U * (0.02 + 0.03 * r(seed, i, 63)), sw = Math.sin(t * 0.9 + i * 1.3) * U * 0.012; ctx.beginPath(); ctx.moveTo(px, base); ctx.quadraticCurveTo(px + lean * 0.3, base - h0 * 0.6, px + lean + sw, base - h0); ctx.stroke(); }
  });

  /* in front of the lyric: rare falling stars, sparse dust; the text always reads */
  K.registerFront('echoStarfall', '星降り', (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, U = Umin(env), seed = P.seed || 1, e = fade(env, 1.2), tone = nightTone(sc);
    ctx.globalAlpha = e; shooting(ctx, t, W, H, U, seed + 7, tone, 6.5);
    ctx.globalCompositeOperation = 'screen';
    for (let i = 0; i < 22; i++) { const px = r(seed, i, 1) * W, py = H - wrap(r(seed, i, 2) * H + t * H * (0.008 + 0.016 * r(seed, i, 3)), H), b = 0.5 + 0.5 * Math.sin(t * 0.9 + i * 2); ctx.fillStyle = rgba(tone.dark ? '#FFFFFF' : sc.accent, 0.4 * b); ctx.beginPath(); ctx.arc(px + Math.sin(t * 0.4 + i) * U * 0.02, py, U * (0.0016 + 0.003 * r(seed, i, 4)), 0, TAU); ctx.fill(); }
    if (K.luxe) K.luxe(ctx, env, { seed: (P && P.seed) || 1, tint: ['#FFFFFF', sc.accent, sc.accent2], strength: 0.6, bokeh: 3, glitter: 10 });
  });
})();
