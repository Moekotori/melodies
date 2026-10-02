/* Decorated palette-led scenes: a lunar crescent, layered hills, drifting petals and light arcs.
   Direct Canvas geometry and a fixed particle count; no resources, simulations or extra caches. */
(() => {
  'use strict';
  const K = J.echo, { mix, rgba, TAU } = K;
  K.commonBackdrops = Object.freeze([...(K.commonBackdrops || []), 'echoLunarHalo', 'echoMistLayers', 'echoPetalDrift', 'echoLightArcs']);
  const ground = (env, ctx) => {
    const { W, H, sc } = env, U = K.U(env), dark = K.isDark(sc.bg);
    const T = { U, dark, bg: mix(sc.bg, dark ? '#000000' : '#FFFFFF', 0.12), fg: sc.fg, a: sc.accent, b: sc.accent2 };
    ctx.globalAlpha = K.fade(env, 0.65);
    const g = ctx.createLinearGradient(0, 0, W, H);
    g.addColorStop(0, mix(T.bg, T.a, 0.04)); g.addColorStop(0.5, T.bg); g.addColorStop(1, mix(T.bg, T.b, 0.06));
    ctx.fillStyle = g; ctx.fillRect(0, 0, W, H);
    return T;
  };
  const spark = (ctx, x, y, radius, colour, alpha, cross = false) => {
    ctx.fillStyle = rgba(colour, alpha); ctx.beginPath(); ctx.arc(x, y, radius, 0, TAU); ctx.fill();
    if (!cross) return;
    ctx.strokeStyle = rgba(colour, alpha * 0.6); ctx.lineWidth = radius * 0.4;
    ctx.beginPath(); ctx.moveTo(x - radius * 3.2, y); ctx.lineTo(x + radius * 3.2, y);
    ctx.moveTo(x, y - radius * 3.2); ctx.lineTo(x, y + radius * 3.2); ctx.stroke();
  };

  K.registerBg('echoLunarHalo', '月环', ['calm', 'emotional', 'editorial'], (env, P, ctx) => {
    const { W, H } = env, T = ground(env, ctx), U = T.U, t = K.sinceStart(env);
    const cx = W * 0.79, cy = H * 0.29 + Math.sin(t * 0.12) * U * 0.008, R = U * 0.235;
    K.glowDisc(ctx, cx, cy, R * 1.85, T.a, T.dark ? 0.13 : 0.06);
    const moon = ctx.createLinearGradient(cx, cy - R, cx + R, cy + R);
    moon.addColorStop(0, mix(T.a, T.fg, 0.7)); moon.addColorStop(0.5, mix(T.bg, T.fg, T.dark ? 0.5 : 0.26)); moon.addColorStop(1, mix(T.bg, T.b, 0.16));
    const bend = R * (1.08 + Math.sin(t * 0.09) * 0.025);
    ctx.fillStyle = moon; ctx.beginPath(); ctx.arc(cx, cy, R, -Math.PI / 2, Math.PI / 2);
    ctx.bezierCurveTo(cx + bend, cy + R * 0.55, cx + bend, cy - R * 0.55, cx, cy - R); ctx.closePath(); ctx.fill();
    for (let i = 0; i < 4; i++) {
      ctx.strokeStyle = rgba(i % 2 ? T.b : T.a, i % 2 ? 0.16 : 0.26); ctx.lineWidth = U * 0.001;
      ctx.beginPath(); ctx.arc(cx, cy, R * (1.2 + i * 0.14), -Math.PI * 0.83, Math.PI * 0.62); ctx.stroke();
    }
    // Sweeping orbits connect the crescent with the other half of the composition.
    for (let i = 0; i < 3; i++) {
      const g = ctx.createLinearGradient(0, H * 0.2, W, H * 0.8);
      g.addColorStop(0, rgba(T.a, 0.04)); g.addColorStop(0.38, rgba(T.a, 0.19)); g.addColorStop(0.7, rgba(T.b, 0.12)); g.addColorStop(1, rgba(T.b, 0));
      ctx.strokeStyle = g; ctx.lineWidth = U * (i === 1 ? 0.002 : 0.001);
      ctx.beginPath(); ctx.ellipse(W * 0.51, H * 0.54, W * (0.5 + i * 0.024), H * (0.26 + i * 0.022), -0.24, 0.15, Math.PI * 1.8); ctx.stroke();
    }
    const clusters = [[0.09, 0.13, 0.27, 0.2], [0.13, 0.74, 0.3, 0.19], [0.68, 0.72, 0.25, 0.18]];
    for (let c = 0; c < clusters.length; c++) {
      const [x0, y0, w, h] = clusters[c];
      ctx.beginPath();
      for (let i = 0; i < 5; i++) {
        const x = W * (x0 + i / 4 * w), y = H * (y0 + J.r(P.seed || 42, c * 8 + i, 8) * h);
        if (i) ctx.lineTo(x, y); else ctx.moveTo(x, y);
      }
      ctx.strokeStyle = rgba(T.fg, 0.12); ctx.lineWidth = U * 0.0008; ctx.stroke();
      for (let i = 0; i < 5; i++) {
        const x = W * (x0 + i / 4 * w), y = H * (y0 + J.r(P.seed || 42, c * 8 + i, 8) * h);
        spark(ctx, x, y, U * 0.003, T.fg, 0.48, i === 2);
      }
    }
    for (let i = 0; i < 58; i++) {
      const x = W * J.r(P.seed || 42, i, 20), y = H * J.r(P.seed || 42, i, 21), size = U * (0.0009 + J.r(P.seed || 42, i, 22) * 0.0018);
      const middle = y > H * 0.36 && y < H * 0.62;
      spark(ctx, x, y, size, i % 3 ? T.fg : T.b, middle ? 0.12 : 0.35 + Math.sin(t * 0.4 + i) * 0.12, i % 11 === 0);
    }
  }, false);

  K.registerBg('echoMistLayers', '层叠山岚', ['calm', 'emotional', 'editorial'], (env, P, ctx) => {
    const { W, H } = env, T = ground(env, ctx), t = K.sinceStart(env);
    K.glowDisc(ctx, W * 0.23, H * 0.32, T.U * 0.8, T.b, T.dark ? 0.14 : 0.11);
    const sunX = W * 0.22, sunY = H * 0.21, sunR = T.U * 0.075;
    const sun = ctx.createRadialGradient(sunX, sunY, 0, sunX, sunY, sunR);
    sun.addColorStop(0, rgba(T.b, 0.24)); sun.addColorStop(1, rgba(T.b, 0.05));
    ctx.fillStyle = sun; ctx.beginPath(); ctx.arc(sunX, sunY, sunR, 0, TAU); ctx.fill();
    ctx.strokeStyle = rgba(T.b, 0.18); ctx.lineWidth = T.U * 0.001;
    ctx.beginPath(); ctx.arc(sunX, sunY, sunR * 1.3, 0, TAU); ctx.stroke();
    // Long, translucent cloud ribbons occupy the sky without a solid panel behind lyrics.
    for (let i = 0; i < 5; i++) {
      const y = H * (0.11 + i * 0.061), bend = H * Math.sin(t * 0.08 + i * 0.8) * 0.025;
      ctx.fillStyle = rgba(i % 2 ? T.a : T.b, T.dark ? 0.065 : 0.07); ctx.beginPath(); ctx.moveTo(-W * 0.04, y);
      ctx.bezierCurveTo(W * 0.3, y - H * 0.08 + bend, W * 0.72, y + H * 0.08, W * 1.04, y - H * 0.016);
      ctx.lineTo(W * 1.04, y + H * 0.005);
      ctx.bezierCurveTo(W * 0.72, y + H * 0.11, W * 0.3, y - H * 0.048 + bend, -W * 0.04, y + H * 0.022); ctx.closePath(); ctx.fill();
    }
    for (let ridge = 0; ridge < 7; ridge++) {
      const base = H * (0.59 + ridge * 0.058), drift = t * 0.025 + ridge * 1.45;
      const colour = mix(T.bg, ridge % 2 ? T.a : T.b, (T.dark ? 0.095 : 0.11) + ridge * 0.043);
      const g = ctx.createLinearGradient(0, base - H * 0.1, 0, H);
      g.addColorStop(0, mix(colour, T.fg, T.dark ? 0.025 : 0.018)); g.addColorStop(1, colour);
      ctx.fillStyle = g; ctx.beginPath(); ctx.moveTo(-W * 0.04, H);
      for (let i = 0; i <= 72; i++) {
        const u = i / 72, x = W * (-0.04 + u * 1.08);
        const y = base + H * (Math.sin(u * 5.1 + drift) * 0.067 + Math.sin(u * 10.8 + ridge * 2) * 0.021);
        ctx.lineTo(x, y);
      }
      ctx.lineTo(W * 1.04, H); ctx.closePath(); ctx.fill();
    }
    for (let i = 0; i < 3; i++) {
      const y = H * (0.58 + i * 0.105), mist = ctx.createLinearGradient(0, y, 0, y + H * 0.11);
      mist.addColorStop(0, rgba(T.bg, 0)); mist.addColorStop(0.4, rgba(T.bg, 0.27)); mist.addColorStop(1, rgba(T.bg, 0));
      ctx.fillStyle = mist; ctx.fillRect(0, y, W, H * 0.11);
    }
    for (let i = 0; i < 24; i++) {
      const x = W * J.r(P.seed || 42, i, 30), y = H * (0.06 + J.r(P.seed || 42, i, 31) * 0.34);
      spark(ctx, x, y, T.U * (0.001 + J.r(P.seed || 42, i, 32) * 0.0016), T.fg, 0.2, i % 8 === 0);
    }
  }, false);

  K.registerBg('echoPetalDrift', '漂浮花瓣', ['calm', 'pop', 'emotional'], (env, P, ctx) => {
    const { W, H } = env, T = ground(env, ctx), U = T.U, t = K.sinceStart(env), seed = P.seed || 42;
    K.glowDisc(ctx, W * 0.12, H * 0.16, U * 0.74, T.a, T.dark ? 0.15 : 0.1);
    K.glowDisc(ctx, W * 0.88, H * 0.9, U * 0.6, T.b, T.dark ? 0.12 : 0.08);
    for (let i = 0; i < 4; i++) {
      const y = H * (0.13 + i * 0.21), g = ctx.createLinearGradient(0, y, W, y + H * 0.12);
      g.addColorStop(0, rgba(T.a, 0)); g.addColorStop(0.45, rgba(T.a, 0.14)); g.addColorStop(0.75, rgba(T.b, 0.1)); g.addColorStop(1, rgba(T.b, 0));
      ctx.strokeStyle = g; ctx.lineWidth = U * 0.001; ctx.beginPath(); ctx.moveTo(-W * 0.04, y);
      ctx.bezierCurveTo(W * 0.32, y - H * 0.16, W * 0.7, y + H * 0.17, W * 1.04, y + H * 0.018); ctx.stroke();
    }
    for (let i = 0; i < 66; i++) {
      const depth = J.r(seed, i, 1), x = W * J.r(seed, i, 2) + Math.sin(t * 0.2 + i * 1.7) * U * 0.025;
      const y = H * K.wrap(J.r(seed, i, 3) + t * (0.005 + depth * 0.004), 1), size = U * (i < 5 ? 0.06 : 0.013 + depth * 0.024);
      const inLyric = x > W * 0.2 && x < W * 0.76 && y > H * 0.37 && y < H * 0.61;
      const shade = mix(T.bg, i % 2 ? T.a : T.b, T.dark ? 0.52 : 0.4);
      ctx.save(); ctx.translate(x, y); ctx.rotate(J.r(seed, i, 4) * TAU + Math.sin(t * 0.18 + i) * 0.38);
      const g = ctx.createLinearGradient(-size, -size, size, size);
      g.addColorStop(0, mix(shade, T.fg, T.dark ? 0.2 : 0.025)); g.addColorStop(0.48, shade); g.addColorStop(1, mix(shade, T.bg, 0.45));
      ctx.globalAlpha *= (0.45 + depth * 0.5) * (inLyric ? 0.4 : 1);
      ctx.fillStyle = g; ctx.beginPath(); ctx.moveTo(-size, size * 0.17);
      ctx.bezierCurveTo(-size * 0.58, -size * 0.85, size * 0.57, -size * 0.77, size, -size * 0.17);
      ctx.bezierCurveTo(size * 0.5, size * 0.55, -size * 0.39, size * 0.66, -size, size * 0.17); ctx.fill();
      ctx.strokeStyle = rgba(T.fg, T.dark ? 0.13 : 0.08); ctx.lineWidth = U * 0.00065;
      ctx.beginPath(); ctx.moveTo(-size * 0.7, size * 0.13); ctx.quadraticCurveTo(0, -size * 0.12, size * 0.8, -size * 0.13); ctx.stroke();
      ctx.restore();
    }
    for (let i = 0; i < 24; i++) {
      spark(ctx, W * J.r(seed, i, 40), H * J.r(seed, i, 41), U * 0.0018, T.fg, 0.2, i % 7 === 0);
    }
  }, false);

  K.registerBg('echoLightArcs', '流光弧线', ['calm', 'graphic', 'editorial'], (env, P, ctx) => {
    const { W, H } = env, T = ground(env, ctx), U = T.U, t = K.sinceStart(env);
    const corners = [[W * 0.97, -H * 0.09, T.a, 0], [-W * 0.12, H * 1.05, T.b, Math.PI],
      [W * 0.14, H * 0.16, T.b, 0.6], [W * 0.82, H * 0.84, T.a, Math.PI * 1.36]];
    for (let group = 0; group < corners.length; group++) {
      const [cx, cy, colour, turn] = corners[group];
      K.glowDisc(ctx, cx, cy, U * 0.9, colour, T.dark ? 0.14 : 0.075);
      ctx.save(); ctx.translate(cx, cy); ctx.rotate(turn + t * 0.012);
      for (let ring = 0; ring < 9; ring++) {
        const radius = U * (group < 2 ? 0.25 + ring * 0.058 : 0.12 + ring * 0.033), g = ctx.createLinearGradient(-radius, 0, radius, radius * 0.7);
        g.addColorStop(0, rgba(colour, 0)); g.addColorStop(0.35, rgba(colour, group < 2 ? 0.29 : 0.2));
        g.addColorStop(0.61, rgba(T.fg, T.dark ? 0.4 : 0.29)); g.addColorStop(0.8, rgba(colour, 0.16)); g.addColorStop(1, rgba(colour, 0));
        ctx.strokeStyle = g; ctx.lineWidth = U * (ring === 3 ? 0.004 : 0.001);
        ctx.beginPath(); ctx.ellipse(0, 0, radius, radius * 0.78, 0.14, 0.2, Math.PI * 1.15); ctx.stroke();
        if (ring % 2 === 0) {
          const angle = 0.4 + ring * 0.19 + Math.sin(t * 0.09) * 0.1;
          const x = radius * Math.cos(angle), y = radius * 0.78 * Math.sin(angle);
          spark(ctx, x, y, U * 0.0025, colour, 0.62, ring === 4);
        }
      }
      ctx.restore();
    }
  }, false);
})();
