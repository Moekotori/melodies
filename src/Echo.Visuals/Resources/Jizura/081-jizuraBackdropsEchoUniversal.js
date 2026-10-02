/* Palette-led ambient materials for every Afterglow look, including original styles.
   Fixed geometry painted directly: no image assets, offscreen layers, timers or growing caches. */
(() => {
  'use strict';
  const K = J.echo, { mix, rgba, TAU } = K;
  K.commonBackdrops = Object.freeze(['echoGlassLeaves', 'echoArmillary', 'echoRippleLight']);
  const ground = (env, ctx) => {
    const { W, H, sc } = env, U = K.U(env), dark = K.isDark(sc.bg);
    const T = { U, dark, a: sc.accent, b: sc.accent2, fg: sc.fg,
      bg: mix(sc.bg, dark ? '#000000' : '#FFFFFF', dark ? 0.24 : 0.12), rim: mix(sc.fg, sc.accent, 0.18) };
    ctx.globalAlpha = K.fade(env, 0.7);
    const g = ctx.createLinearGradient(0, 0, W, H);
    g.addColorStop(0, mix(T.bg, sc.accent, dark ? 0.06 : 0.04)); g.addColorStop(0.5, T.bg);
    g.addColorStop(1, mix(T.bg, sc.accent2, dark ? 0.09 : 0.05));
    ctx.fillStyle = g; ctx.fillRect(0, 0, W, H);
    return T;
  };
  const path = (ctx, points) => {
    ctx.beginPath(); points.forEach(([x, y], i) => { if (i) ctx.lineTo(x, y); else ctx.moveTo(x, y); }); ctx.closePath();
  };
  const gleam = (ctx, x, y, radius, colour, alpha) => K.glowDisc(ctx, x, y, radius, colour, alpha);

  K.registerBg('echoGlassLeaves', '折光薄片', ['calm', 'emotional', 'editorial'], (env, P, ctx) => {
    const { W, H } = env, T = ground(env, ctx), U = T.U, t = K.sinceStart(env);
    gleam(ctx, W * 0.87, H * 0.24, U * 0.8, T.a, T.dark ? 0.13 : 0.08);
    gleam(ctx, W * 0.1, H * 0.86, U * 0.65, T.b, T.dark ? 0.09 : 0.065);
    // Three bevelled optical leaves. Their reflections are sharp, their faces almost transparent.
    const leaves = [[0.12, 0.5, 0.23, 0.86, -0.31], [0.79, 0.51, 0.3, 1.04, 0.31], [0.95, 0.58, 0.18, 0.74, 0.44]];
    for (let i = 0; i < leaves.length; i++) {
      const [px, py, rw, rh, tilt] = leaves[i], w = U * rw, h = H * rh;
      ctx.save(); ctx.translate(W * px, H * py + Math.sin(t * 0.22 + i * 1.7) * U * 0.012);
      ctx.rotate(tilt + Math.sin(t * 0.15 + i) * 0.025);
      const points = [[-w * 0.5, -h * 0.38], [w * 0.13, -h * 0.5], [w * 0.5, -h * 0.39],
        [w * 0.5, h * 0.38], [-w * 0.13, h * 0.5], [-w * 0.5, h * 0.39]];
      const body = ctx.createLinearGradient(-w / 2, -h / 2, w / 2, h / 2);
      body.addColorStop(0, rgba(T.rim, T.dark ? 0.15 : 0.12)); body.addColorStop(0.18, rgba(T.a, 0.07));
      body.addColorStop(0.48, rgba(T.fg, 0.018)); body.addColorStop(0.8, rgba(T.b, T.dark ? 0.11 : 0.07)); body.addColorStop(1, rgba(T.rim, 0.18));
      path(ctx, points); ctx.fillStyle = body; ctx.fill();
      const edge = ctx.createLinearGradient(-w / 2, 0, w / 2, 0);
      edge.addColorStop(0, rgba(T.rim, 0.6)); edge.addColorStop(0.18, rgba(T.a, 0.18)); edge.addColorStop(0.73, rgba(T.b, 0.25)); edge.addColorStop(1, rgba(T.rim, 0.68));
      ctx.strokeStyle = edge; ctx.lineWidth = U * 0.0015; ctx.stroke();
      ctx.save(); ctx.clip();
      const reflection = ctx.createLinearGradient(-w * 0.45, 0, w * 0.3, 0);
      reflection.addColorStop(0, rgba(T.fg, 0)); reflection.addColorStop(0.45, rgba(T.fg, 0.04));
      reflection.addColorStop(0.49, rgba(T.rim, 0.22)); reflection.addColorStop(0.52, rgba(T.fg, 0.025)); reflection.addColorStop(1, rgba(T.a, 0));
      ctx.fillStyle = reflection; ctx.fillRect(-w / 2, -h / 2, w, h);
      // The bevel is a second plane, rather than a decorative outline around a card.
      path(ctx, [points[0], points[1], points[4], points[5]]); ctx.fillStyle = rgba(T.fg, 0.045); ctx.fill();
      ctx.strokeStyle = rgba(T.rim, 0.16); ctx.beginPath(); ctx.moveTo(points[1][0], points[1][1]); ctx.lineTo(points[4][0], points[4][1]); ctx.stroke();
      ctx.restore(); ctx.restore();
    }
    // Narrow caustics falling across the lower edge, kept out of the lyric's central band.
    ctx.save(); ctx.translate(W * 0.62, H * 1.12); ctx.rotate(-0.18);
    for (let i = 0; i < 5; i++) {
      const g = ctx.createLinearGradient(-U * 0.6, 0, U * 0.6, 0);
      g.addColorStop(0, rgba(T.a, 0)); g.addColorStop(0.52, rgba(i % 2 ? T.a : T.b, 0.13)); g.addColorStop(1, rgba(T.b, 0));
      ctx.strokeStyle = g; ctx.lineWidth = U * (0.003 + i * 0.001);
      ctx.beginPath(); ctx.ellipse(0, 0, U * (0.5 + i * 0.07), U * (0.14 + i * 0.02), 0, Math.PI, TAU); ctx.stroke();
    }
    ctx.restore();
  }, false);

  K.registerBg('echoArmillary', '悬浮星仪', ['calm', 'emotional', 'graphic'], (env, P, ctx) => {
    const { W, H } = env, T = ground(env, ctx), U = T.U, t = K.sinceStart(env);
    const cx = W * (H > W ? 0.63 : 0.76), cy = H * 0.55, radius = U * 0.4;
    gleam(ctx, cx, cy, U * 0.7, T.a, T.dark ? 0.16 : 0.055);
    const pointA = [0, 0, 0], pointB = [0, 0, 0];
    const project = (angle, ring, out = [0, 0, 0]) => {
      const x = Math.cos(angle) * radius, y = Math.sin(angle) * radius;
      const tilt = 0.48 + ring * 0.64, yaw = t * 0.09 + ring * 0.82;
      const yy = y * Math.cos(tilt), z = y * Math.sin(tilt);
      const xx = x * Math.cos(yaw) + z * Math.sin(yaw), zz = -x * Math.sin(yaw) + z * Math.cos(yaw);
      const perspective = U * 2.5 / (U * 2.5 + zz);
      out[0] = cx + xx * perspective; out[1] = cy + yy * perspective; out[2] = zz;
      return out;
    };
    const rings = front => {
      for (let ring = 0; ring < 3; ring++) {
        const colour = ring === 1 ? T.b : T.a;
        for (const rim of [false, true]) {
          ctx.strokeStyle = rgba(rim ? T.rim : colour, front ? rim ? 0.32 : 0.42 : rim ? 0.06 : 0.14);
          ctx.lineWidth = U * (rim ? 0.001 : 0.007); ctx.beginPath();
          for (let i = 0; i < 96; i++) {
            const a = project(i / 96 * TAU, ring, pointA), b = project((i + 1) / 96 * TAU, ring, pointB);
            if ((a[2] < 0) !== front) continue;
            ctx.moveTo(a[0], a[1] - (rim ? U * 0.002 : 0)); ctx.lineTo(b[0], b[1] - (rim ? U * 0.002 : 0));
          }
          ctx.stroke();
        }
      }
    };
    rings(false);
    // A satin metal core, lit through its own palette rather than fixed silver/gold assets.
    const core = U * 0.155, g = ctx.createRadialGradient(cx - core * 0.4, cy - core * 0.45, core * 0.03, cx, cy, core);
    g.addColorStop(0, mix(T.bg, T.fg, 0.73)); g.addColorStop(0.14, mix(T.bg, T.a, 0.42));
    g.addColorStop(0.48, mix(T.bg, T.a, 0.17)); g.addColorStop(0.85, mix(T.bg, T.b, 0.07)); g.addColorStop(1, T.bg);
    ctx.fillStyle = g; ctx.beginPath(); ctx.arc(cx, cy, core, 0, TAU); ctx.fill();
    ctx.strokeStyle = rgba(T.rim, 0.15); ctx.lineWidth = 1; ctx.stroke();
    rings(true);
    for (let ring = 0; ring < 3; ring++) {
      const p = project(t * (0.13 + ring * 0.03) + ring * 2.1, ring), rad = U * (ring === 1 ? 0.014 : 0.009);
      if (p[2] > 0 && Math.hypot(p[0] - cx, p[1] - cy) < core) continue;
      gleam(ctx, p[0], p[1], rad * 4, ring === 1 ? T.b : T.a, 0.12);
      ctx.fillStyle = mix(T.a, T.fg, 0.48); ctx.beginPath(); ctx.arc(p[0], p[1], rad, 0, TAU); ctx.fill();
    }
    const shadow = ctx.createRadialGradient(cx, H * 0.96, 0, cx, H * 0.96, U * 0.3);
    shadow.addColorStop(0, rgba(T.fg, T.dark ? 0.06 : 0.07)); shadow.addColorStop(1, rgba(T.fg, 0));
    ctx.save(); ctx.translate(0, H * 0.96); ctx.scale(1, 0.14); ctx.translate(0, -H * 0.96);
    ctx.fillStyle = shadow; ctx.fillRect(cx - U * 0.3, H * 0.96 - U * 0.3, U * 0.6, U * 0.6); ctx.restore();
  }, false);

  K.registerBg('echoRippleLight', '涟漪光场', ['calm', 'emotional', 'editorial'], (env, P, ctx) => {
    const { W, H } = env, T = ground(env, ctx), U = T.U, t = K.sinceStart(env);
    // Light skims two pools. Contours interfere at the edges; the lyric band remains quiet.
    const pools = [[W * 0.17, H * 0.84, -0.22, T.a], [W * 0.9, H * 0.14, -0.22, T.b]];
    for (let p = 0; p < pools.length; p++) {
      const [cx, cy, tilt, colour] = pools[p];
      gleam(ctx, cx, cy, U * 0.68, colour, T.dark ? 0.14 : 0.08);
      ctx.save(); ctx.translate(cx, cy); ctx.rotate(tilt);
      for (let ring = 0; ring < 13; ring++) {
        const phase = K.wrap(t * 0.07 + p * 0.3, 1), rr = U * (0.07 + (ring + phase) * 0.063);
        const alpha = Math.sin((ring + phase) / 14 * Math.PI) * (T.dark ? 0.36 : 0.28);
        const g = ctx.createLinearGradient(-rr, -rr * 0.22, rr, rr * 0.22);
        g.addColorStop(0, rgba(colour, 0)); g.addColorStop(0.22, rgba(colour, alpha * 0.45));
        g.addColorStop(0.55, rgba(T.rim, alpha)); g.addColorStop(0.72, rgba(p ? T.a : T.b, alpha * 0.5)); g.addColorStop(1, rgba(colour, 0));
        ctx.strokeStyle = g; ctx.lineWidth = U * (ring % 3 === 0 ? 0.003 : 0.0013); ctx.beginPath();
        for (let i = 0; i <= 72; i++) {
          const angle = i / 72 * TAU, bend = 1 + Math.sin(angle * 4 + t * 0.25 + ring * 0.28) * 0.026;
          const x = Math.cos(angle) * rr * bend, y = Math.sin(angle) * rr * 0.3 * bend;
          if (i) ctx.lineTo(x, y); else ctx.moveTo(x, y);
        }
        ctx.stroke();
      }
      ctx.restore();
    }
    // Broad reflected streaks replace a particle field: only six finite highlights.
    for (let i = 0; i < 6; i++) {
      const x = W * (0.05 + i * 0.18), y = H * (0.8 + Math.sin(t * 0.16 + i * 0.7) * 0.025), width = U * (0.06 + i % 3 * 0.03);
      const g = ctx.createLinearGradient(x - width, y, x + width, y);
      g.addColorStop(0, rgba(T.rim, 0)); g.addColorStop(0.5, rgba(T.rim, T.dark ? 0.12 : 0.08)); g.addColorStop(1, rgba(T.rim, 0));
      ctx.fillStyle = g; ctx.fillRect(x - width, y, width * 2, U * 0.0015);
    }
  }, false);
})();
