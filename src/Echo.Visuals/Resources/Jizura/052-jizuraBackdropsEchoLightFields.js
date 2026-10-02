/* First-party cinematic light materials. Static detail uses the existing bounded
   layer cache; live drawing is capped and removed by the existing quality tier. */
(() => {
  'use strict';
  const K = J.echo, { mix, rgba, glowDisc, isDark } = K, TAU = Math.PI * 2;
  const scenes = new Set(['echoStarChart', 'echoChromaticFold', 'echoTidalLight', 'echoLuminousVeil']);
  Object.assign(J.FONTS, {
    echoLightDisplay: { label: 'Afterglow / Light Sans', family: '"Segoe UI Variable Display","Avenir Next","Helvetica Neue",Arial', weight: 300,
      kind: 'gothic', fb: '"Yu Gothic","Hiragino Sans","Microsoft YaHei",sans-serif' },
    echoLightSerif: { label: 'Afterglow / Lyrical Serif', family: '"Yu Mincho","Hiragino Mincho ProN","Songti SC","SimSun",Georgia', weight: 400,
      kind: 'mincho', fb: 'serif' },
  });
  const atmosphere = (ctx, W, H, sc, dark) => {
    ctx.fillStyle = K.vertical(ctx, 0, H, [[0, mix(sc.bg, dark ? '#000000' : '#FFFFFF', dark ? 0.68 : 0.65)],
      [0.6, dark ? mix(mix(sc.bg, '#000000', 0.38), sc.accent2, 0.04) : mix(sc.bg, sc.accent2, 0.12)],
      [1, mix(sc.bg, dark ? '#000000' : '#FFFFFF', dark ? 0.66 : 0.42)]]);
    ctx.fillRect(0, 0, W, H);
    glowDisc(ctx, W * 0.86, H * 0.25, W * 0.7, sc.accent2, dark ? 0.12 : 0.16);
  };
  const dust = (ctx, W, H, sc, seed, t, count, dark) => {
    const U = Math.min(W, H);
    for (let i = 0; i < count; i++) {
      const x = J.r(seed, i, 79) * W, y = J.r(seed, i, 81) * H;
      const s = U * (0.0007 + Math.pow(J.r(seed, i, 83), 6) * 0.004);
      ctx.fillStyle = rgba(dark ? sc.fg : sc.accent, (0.08 + J.r(seed, i, 85) * 0.32) * (0.8 + 0.2 * Math.sin(t * 0.22 + i)));
      ctx.beginPath(); ctx.arc(x, y, s, 0, TAU); ctx.fill();
    }
  };
  const eclipse = (ctx, W, H, sc, seed, dark, detail) => {
    const U = Math.min(W, H), port = H > W, cx = W * (port ? 0.86 : 0.78), cy = H * 0.42, R = U * (port ? 0.64 : 0.57);
    dust(ctx, W, H, sc, seed, 0, detail ? 180 : 72, dark);
    // A cropped occluding world; the corona, rather than an orbit icon, carries its volume.
    glowDisc(ctx, cx, cy, R * 1.6, sc.accent2, dark ? 0.32 : 0.16);
    glowDisc(ctx, cx - R * 0.52, cy - R * 0.51, R * 0.85, mix(sc.accent, sc.fg, 0.45), dark ? 0.32 : 0.18);
    for (let k = detail ? 11 : 6; k >= 0; k--) {
      const g = ctx.createLinearGradient(cx - R, cy - R, cx + R, cy + R);
      g.addColorStop(0, rgba(sc.fg, 0)); g.addColorStop(0.26, rgba(mix(sc.accent, sc.fg, 0.5), 0.05 + 0.18 / (k + 1)));
      g.addColorStop(0.52, rgba(sc.accent2, 0.14 / (k + 1))); g.addColorStop(1, rgba(sc.accent2, 0));
      ctx.strokeStyle = g; ctx.lineWidth = U * (0.002 + k * 0.003);
      ctx.beginPath(); ctx.arc(cx, cy, R + U * k * 0.003, -Math.PI * 1.1, Math.PI * 0.1); ctx.stroke();
    }
    const body = ctx.createRadialGradient(cx + R * 0.55, cy + R * 0.5, R * 0.08, cx, cy, R);
    body.addColorStop(0, mix(sc.bg, dark ? '#000000' : sc.accent2, dark ? 0.94 : 0.1));
    body.addColorStop(0.88, mix(sc.bg, dark ? '#000000' : sc.accent2, dark ? 0.84 : 0.15));
    body.addColorStop(1, mix(sc.bg, sc.accent2, dark ? 0.22 : 0.24));
    ctx.fillStyle = body; ctx.beginPath(); ctx.arc(cx, cy, R, 0, TAU); ctx.fill();
    ctx.strokeStyle = rgba(mix(sc.fg, sc.accent2, 0.3), dark ? 0.8 : 0.5); ctx.lineWidth = U * 0.0014;
    ctx.beginPath(); ctx.arc(cx, cy, R, -Math.PI * 0.96, -Math.PI * 0.28); ctx.stroke();
    glowDisc(ctx, cx - R * 0.68, cy - R * 0.73, U * 0.065, sc.fg, dark ? 0.7 : 0.22);
    const haze = ctx.createLinearGradient(0, 0, W, 0);
    haze.addColorStop(0, mix(sc.bg, dark ? '#000000' : '#FFFFFF', 0.58));
    haze.addColorStop(0.38, rgba(sc.bg, dark ? 0.45 : 0.2)); haze.addColorStop(0.76, rgba(sc.bg, 0));
    ctx.fillStyle = haze; ctx.fillRect(0, 0, W, H);
  };
  const fold = (ctx, W, H, sc, seed, dark, detail) => {
    const U = Math.min(W, H), n = detail ? 86 : 36;
    glowDisc(ctx, W * 0.7, H * 0.22, U * 0.6, sc.accent, dark ? 0.22 : 0.2);
    for (let i = 0; i < n; i++) {
      const v = i / (n - 1), glow = Math.pow(Math.sin(v * Math.PI), 0.6);
      const grad = ctx.createLinearGradient(W * 0.22, H * 0.65, W * 0.88, H * 0.05);
      grad.addColorStop(0, rgba(sc.accent2, 0)); grad.addColorStop(0.23, rgba(sc.accent2, glow * 0.4));
      grad.addColorStop(0.5, rgba(mix(sc.accent, sc.fg, 0.22), glow * 0.56));
      grad.addColorStop(0.68, rgba(mix(sc.accent2, sc.fg, 0.65), glow * 0.6)); grad.addColorStop(1, rgba(sc.accent, 0));
      ctx.strokeStyle = grad; ctx.lineWidth = U * (0.0016 + 0.002 * glow);
      ctx.beginPath(); ctx.moveTo(W * (0.12 + v * 0.2), H * 1.05);
      ctx.bezierCurveTo(W * (1.12 - v * 0.25), H * (0.58 - v * 0.58), W * (0.2 + v * 0.39), H * (-0.25 + v * 0.75), W * 1.12, H * (0.09 + v * 0.46)); ctx.stroke();
    }
    // Thin interference lines across the fold keep the material from reading as a flat gradient.
    for (let i = 0; i < (detail ? 24 : 10); i++) {
      const v = i / 24, g = ctx.createLinearGradient(W * 0.4, 0, W, H);
      g.addColorStop(0, rgba(sc.fg, 0)); g.addColorStop(0.55, rgba(sc.fg, dark ? 0.16 : 0.22)); g.addColorStop(1, rgba(sc.fg, 0));
      ctx.strokeStyle = g; ctx.lineWidth = Math.max(0.6, U * 0.0008);
      ctx.beginPath(); ctx.moveTo(W * 0.52, -H * 0.08); ctx.bezierCurveTo(W * (0.18 + v * 0.5), H * 0.6, W * 0.95, H * (0.35 + v * 0.2), W * 1.06, H * 0.9); ctx.stroke();
    }
  };
  const tide = (ctx, W, H, sc, seed, dark, detail) => {
    const U = Math.min(W, H), horizon = H * 0.46;
    glowDisc(ctx, W * 0.68, horizon, W * 0.52, sc.accent2, dark ? 0.3 : 0.2);
    glowDisc(ctx, W * 0.74, horizon, U * 0.35, mix(sc.fg, sc.accent, 0.5), dark ? 0.19 : 0.3);
    const sea = K.vertical(ctx, horizon, H, [[0, rgba(sc.accent2, 0)], [0.25, rgba(sc.accent2, 0.06)], [1, rgba(sc.bg, dark ? 0.8 : 0.3)]]);
    ctx.fillStyle = sea; ctx.fillRect(0, horizon, W, H - horizon);
    const count = detail ? 80 : 36;
    for (let i = 0; i < count; i++) {
      const v = (i + 1) / count, y = horizon + v * v * H * 0.64, amp = U * (0.002 + v * v * 0.046);
      const g = ctx.createLinearGradient(0, 0, W, 0);
      g.addColorStop(0, rgba(sc.accent2, 0)); g.addColorStop(0.52, rgba(dark ? sc.accent2 : mix(sc.accent2, sc.fg, 0.65), 0.24 * v));
      g.addColorStop(0.74, rgba(mix(sc.fg, sc.accent2, dark ? 0.35 : 0.22), 0.12 + (dark ? 0.65 : 0.34) * v)); g.addColorStop(1, rgba(sc.accent, 0));
      ctx.strokeStyle = g; ctx.lineWidth = U * (0.0009 + v * 0.0015); ctx.beginPath();
      for (let j = 0; j <= 48; j++) {
        const x = W * j / 48, yy = y + Math.sin(j * 0.22 + i * 0.26) * amp + Math.sin(j * 0.64 - i * 0.16) * amp * 0.24;
        j ? ctx.lineTo(x, yy) : ctx.moveTo(x, yy);
      } ctx.stroke();
    }
    dust(ctx, W, horizon, sc, seed, 0, detail ? 42 : 18, dark);
  };
  const veil = (ctx, W, H, sc, seed, dark, detail) => {
    const U = Math.min(W, H), n = detail ? 74 : 32;
    glowDisc(ctx, W * 0.68, H * 0.4, U * 0.9, sc.accent2, dark ? 0.24 : 0.26);
    for (let layer = 0; layer < 3; layer++) {
      for (let i = 0; i < n; i++) {
        const v = i / n, edge = Math.pow(Math.abs(Math.cos(v * Math.PI * 2)), 14);
        const g = ctx.createLinearGradient(W * 0.35, H, W * 0.9, 0);
        g.addColorStop(0, rgba(sc.accent, 0)); g.addColorStop(0.25, rgba(sc.accent, 0.1 + edge * 0.26));
        g.addColorStop(0.56, rgba(mix(sc.accent2, dark ? sc.fg : '#FFFFFF', dark ? 0.7 : 0.35), 0.22 + edge * 0.45));
        g.addColorStop(0.8, rgba(sc.accent2, 0.16)); g.addColorStop(1, rgba(sc.accent2, 0));
        ctx.strokeStyle = g; ctx.lineWidth = U * (0.002 + 0.003 * Math.sin(v * Math.PI));
        ctx.beginPath(); ctx.moveTo(W * (0.32 + layer * 0.09 + v * 0.16), H * 1.12);
        ctx.bezierCurveTo(W * (0.13 + v * 0.36), H * (0.7 - layer * 0.15), W * (1.24 - v * 0.35), H * (0.46 + layer * 0.05), W * (0.86 + v * 0.24), -H * 0.16); ctx.stroke();
      }
    }
    dust(ctx, W, H, sc, seed, 0, detail ? 56 : 20, dark);
  };
  const painters = { echoStarChart: eclipse, echoChromaticFold: fold, echoTidalLight: tide, echoLuminousVeil: veil };
  for (const [key, label] of [['echoStarChart', 'Eclipse field'], ['echoChromaticFold', 'Chromatic silk'], ['echoTidalLight', 'Tidal light'], ['echoLuminousVeil', 'Pearl veil']]) {
    K.registerBg(key, label, ['calm', 'emotional'], (env, P, ctx) => {
      const { W, H, sc } = env, seed = P.seed || 1, U = Math.min(W, H), dark = isDark(sc.bg), detail = !env.fast && (J.echoTier || 0) < 2;
      const keyWithScheme = `${key}|${seed}|${sc.bg}|${sc.fg}|${sc.accent}|${sc.accent2}|${detail}`;
      const art = K.layer(env, keyWithScheme, detail ? 0.6 : 0.45, (x, w, h) => {
        atmosphere(x, w, h, sc, dark); painters[key](x, w, h, sc, seed, dark, detail);
        const shade = x.createLinearGradient(0, 0, w * 0.9, 0);
        shade.addColorStop(0, rgba(dark ? '#000000' : '#FFFFFF', dark ? 0.25 : 0.15)); shade.addColorStop(1, rgba(sc.bg, 0));
        x.fillStyle = shade; x.fillRect(0, 0, w, h);
      });
      ctx.globalAlpha = J.echoMotion === false ? 1 : K.fade(env, 0.85); ctx.drawImage(art, 0, 0, W, H);
      if (J.echoMotion === false || (J.echoTier || 0) >= 2) return;
      // At most six translucent sweeps and twelve dust motes; no history or blur surfaces.
      ctx.globalAlpha *= 0.45;
      const t = env.t * 0.08;
      for (let k = 0; k < 6; k++) {
        if (key === 'echoStarChart') break;
        const v = k / 6, pulse = 0.25 + Math.sin(t + k * 0.8) * 0.15;
        ctx.strokeStyle = rgba(k % 2 ? sc.accent : sc.accent2, pulse); ctx.lineWidth = U * 0.001;
        ctx.beginPath();
        if (key === 'echoTidalLight') {
          const y = H * (0.56 + v * 0.35); ctx.moveTo(W * 0.25, y);
          ctx.bezierCurveTo(W * 0.45, y + Math.sin(t + k) * U * 0.015, W * 0.72, y - U * 0.025, W * 1.04, y);
        } else {
          ctx.moveTo(W * (0.5 + v * 0.08), H * 1.03);
          ctx.bezierCurveTo(W * 0.78, H * (0.7 + Math.sin(t + k) * 0.015), W * 0.5, H * 0.2, W * (0.86 + v * 0.07), -H * 0.1);
        } ctx.stroke();
      }
      dust(ctx, W, H, sc, seed + 91, env.t, 12, dark);
    }, false); // Fine material strands supply texture; coarse film grain would obscure them.
  }
  J.register('layout', 'echoLightFieldLyric', {
    name: 'Light field lyric', fits: () => true,
    tags: ['calm', 'emotional'], w: 1, treat: 'safe', ae: 'center',
    plan: () => ({}),
    render(env) {
      const { W, H, sc, cut } = env, U = Math.min(W, H), port = H > W;
      const pearl = cut.bg === 'echoLuminousVeil', tidal = cut.bg === 'echoTidalLight';
      const font = pearl || cut.bg === 'echoStarChart' ? 'echoLightSerif' : 'echoLightDisplay';
      const text = cut.text, x = W * (port ? 0.09 : 0.085), y = H * (tidal ? 0.31 : port ? 0.69 : pearl ? 0.58 : 0.68);
      const width = W * (port ? 0.82 : tidal ? 0.71 : 0.63), size = Math.min(U * (pearl ? 0.16 : 0.145), J.fitSize(text, font, width, H * 0.25));
      const bb = J.mainDraw(env, { text, font, size, x, y, align: 'left', color: sc.fg, track: pearl ? 0.035 : 0.015, lead: 1.25, noHold: true });
      if (cut.echoArtist) J.drawItem(env, { text: cut.echoArtist, font: 'echoIcpcBody', size: U * 0.019,
        x: x + U * 0.002, y: y - size * 0.85, align: 'left', color: sc.sub, alpha: 0.8, track: 0.12, plain: true });
      return bb;
    },
  }, 'echo');
  J.bindAfterglowLightFields = (plan, input) => {
    for (const cut of plan.cuts) {
      if (!scenes.has(cut.bg) || !cut.echoLyric || !cut.text?.trim()) continue;
      // Complex user-selected kinetic layouts keep their composition.
      if (['center', 'huge', 'lowerThird', 'subtitleBar', 'quote', 'tyMargin'].includes(cut.layout)) {
        cut.layout = 'echoLightFieldLyric'; cut.params = {}; cut.cam = 'none'; cut.camP = {};
        cut.treat = 'none'; cut.hold = 'still'; cut.echoArtist = input.artist?.slice(0, 128);
        cut.decor = (cut.decor || []).filter(d => !['echoStarfall', 'echoMistDrift', 'rings', 'brackets', 'leaders'].includes(d.id));
      }
    }
  };
})();
