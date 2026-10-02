/* ECHO first-party backdrops, sweet looks: 放課後 (after school) and 星のまほう (magic stars), plus a few round little
   shapes (heart, petal, sakura, puff cloud, sparkle) shared with the other cute looks through J.echo.shapes.
   Static art is painted once into cached layers; only the living parts are redrawn each frame. */
(() => {
  'use strict';
  const K = J.echo, E = K.E, TAU = K.TAU, clamp = K.clamp, mix = K.mix, rgba = K.rgba;
  const { U: Umin, isDark, wrap, hues, fade, vertical, glowDisc, star, layer, scroll } = K;
  const colourKey = sc => [sc.bg, sc.fg, sc.accent, sc.accent2, sc.ghostA, sc.ghostB].join('');

  /* ---------- shared round shapes: every path is built at (x, y) with size s, ready to fill or stroke ---------- */
  const shapes = {
    heart(ctx, x, y, s) {
      ctx.beginPath(); ctx.moveTo(x, y + s * 0.9);
      ctx.bezierCurveTo(x - s * 1.25, y + s * 0.1, x - s * 0.8, y - s * 0.95, x, y - s * 0.32);
      ctx.bezierCurveTo(x + s * 0.8, y - s * 0.95, x + s * 1.25, y + s * 0.1, x, y + s * 0.9); ctx.closePath();
    },
    petal(ctx, x, y, s) {                       /* a sakura petal with its little notch, pointing up */
      ctx.beginPath(); ctx.moveTo(x, y + s);
      ctx.bezierCurveTo(x - s * 0.95, y + s * 0.35, x - s * 0.75, y - s * 0.85, x - s * 0.12, y - s * 0.95);
      ctx.lineTo(x, y - s * 0.72); ctx.lineTo(x + s * 0.12, y - s * 0.95);
      ctx.bezierCurveTo(x + s * 0.75, y - s * 0.85, x + s * 0.95, y + s * 0.35, x, y + s); ctx.closePath();
    },
    sakura(ctx, x, y, r, rot, fill, heart) {
      ctx.save(); ctx.translate(x, y); ctx.rotate(rot);
      for (let i = 0; i < 5; i++) { ctx.save(); ctx.rotate(i * TAU / 5); ctx.translate(0, -r * 0.5); ctx.fillStyle = fill; shapes.petal(ctx, 0, 0, r * 0.55); ctx.fill(); ctx.restore(); }
      ctx.fillStyle = heart; ctx.beginPath(); ctx.arc(0, 0, r * 0.14, 0, TAU); ctx.fill(); ctx.restore();
    },
    puff(ctx, x, y, w, h) {                     /* a flat-bottomed cartoon cloud from round lobes */
      ctx.beginPath(); ctx.moveTo(x - w * 0.5, y);
      ctx.arc(x - w * 0.34, y - h * 0.22, h * 0.3, Math.PI * 0.5, Math.PI * 1.5);
      ctx.arc(x - w * 0.08, y - h * 0.5, h * 0.42, Math.PI * 0.95, Math.PI * 1.9);
      ctx.arc(x + w * 0.24, y - h * 0.3, h * 0.34, Math.PI * 1.25, Math.PI * 0.35);
      ctx.arc(x + w * 0.4, y - h * 0.1, h * 0.2, Math.PI * 1.5, Math.PI * 0.5);
      ctx.closePath();
    },
    sparkle(ctx, x, y, r) { star(ctx, x, y, r, 4, 0.16); },
    roundRect(ctx, x, y, w, h, r) { ctx.beginPath(); ctx.roundRect(x, y, w, h, r); },
  };
  K.shapes = shapes;

  /* A finishing veil for the front layers: a slow light leak, out-of-focus discs with brighter rims, and a little glitter.
     It is what turns a flat picture into a lit one; `tint` picks the colours, `k` the strength. */
  K.luxe = (ctx, env, o) => {
    const { W, H, sc } = env, t = env.t, U = Umin(env), dark = isDark(sc.bg), seed = o.seed || 1, tint = o.tint || [sc.accent, sc.accent2], k = o.strength == null ? 1 : o.strength, n = o.bokeh == null ? 6 : o.bokeh;
    ctx.save(); ctx.globalCompositeOperation = dark ? 'screen' : 'source-over';
    const a = (0.06 + 0.035 * Math.sin(t * 0.35 + seed)) * k * (dark ? 1 : 0.8), drift = Math.sin(t * 0.12 + seed) * W * 0.06;
    const leak = ctx.createLinearGradient(-W * 0.1 + drift, 0, W * 0.7 + drift, H);
    leak.addColorStop(0, rgba(tint[0], a * 1.7)); leak.addColorStop(0.4, rgba(tint[1 % tint.length], a * 0.7)); leak.addColorStop(0.7, rgba(tint[0], 0)); leak.addColorStop(1, rgba(tint[1 % tint.length], a * 0.5));
    ctx.fillStyle = leak; ctx.fillRect(0, 0, W, H);
    for (let i = 0; i < n; i++) {
      const r = U * (0.03 + 0.07 * J.r(seed, i, 91)), x = J.r(seed, i, 92) * W + Math.sin(t * 0.13 + i * 2) * U * 0.05, y = J.r(seed, i, 93) * H + Math.cos(t * 0.11 + i) * U * 0.04, c = tint[i % tint.length], pulse = 0.75 + 0.25 * Math.sin(t * 0.7 + i * 1.9);
      const g = ctx.createRadialGradient(x, y, r * 0.35, x, y, r); g.addColorStop(0, rgba(c, 0.015 * k)); g.addColorStop(0.82, rgba(c, 0.07 * k * pulse)); g.addColorStop(0.95, rgba(mix(c, '#FFFFFF', 0.45), 0.15 * k * pulse)); g.addColorStop(1, rgba(c, 0));
      ctx.fillStyle = g; ctx.beginPath(); ctx.arc(x, y, r, 0, TAU); ctx.fill();
    }
    for (let i = 0; i < (o.glitter == null ? 10 : o.glitter); i++) {
      const b = Math.pow(0.5 + 0.5 * Math.sin(t * (1.6 + 2.4 * J.r(seed, i, 95)) + i * 2.7), 4), x = J.r(seed, i, 96) * W, y = J.r(seed, i, 97) * H;
      if (b < 0.08) continue; ctx.fillStyle = rgba(dark ? '#FFFFFF' : mix(tint[0], '#FFFFFF', 0.3), (dark ? 0.85 : 0.7) * b * k); sparkle(ctx, x, y, U * (0.007 + 0.011 * J.r(seed, i, 98)) * (0.6 + b));  ctx.fill();
    }
    ctx.restore();
  };
  const { heart, petal, sakura, puff, sparkle, roundRect } = shapes;

  /* ================= 放課後 ================= */
  /* light grounds: a pastel classroom window with curtains, sunlight, notes and a desk.
     dark grounds: a blackboard scribbled with chalk hearts, stars and clouds. */
  const chalk = (x, kind, cx, cy, s) => {
    x.beginPath();
    if (kind === 'heart') { heart(x, cx, cy, s); }
    else if (kind === 'star') { star(x, cx, cy, s, 5, 0.42); }
    else if (kind === 'cloud') { puff(x, cx, cy + s * 0.3, s * 2.2, s * 1.5); }
    else if (kind === 'note') { x.arc(cx - s * 0.4, cy + s * 0.5, s * 0.4, 0, TAU); x.moveTo(cx, cy + s * 0.5); x.lineTo(cx, cy - s * 0.9); x.quadraticCurveTo(cx + s * 0.6, cy - s * 0.7, cx + s * 0.5, cy - s * 0.2); }
    else if (kind === 'cat') {
      x.arc(cx, cy, s * 0.8, 0, TAU); x.moveTo(cx - s * 0.68, cy - s * 0.4); x.lineTo(cx - s * 0.72, cy - s * 1.0); x.lineTo(cx - s * 0.2, cy - s * 0.72);
      x.moveTo(cx + s * 0.68, cy - s * 0.4); x.lineTo(cx + s * 0.72, cy - s * 1.0); x.lineTo(cx + s * 0.2, cy - s * 0.72);
      x.moveTo(cx - s * 0.3, cy - s * 0.05); x.arc(cx - s * 0.28, cy - s * 0.05, s * 0.05, 0, TAU); x.moveTo(cx + s * 0.33, cy - s * 0.05); x.arc(cx + s * 0.28, cy - s * 0.05, s * 0.05, 0, TAU);
      x.moveTo(cx - s * 0.12, cy + s * 0.2); x.quadraticCurveTo(cx, cy + s * 0.33, cx + s * 0.12, cy + s * 0.2);
    } else { x.arc(cx, cy, s * 0.8, 0, TAU); x.moveTo(cx - s * 0.35, cy - s * 0.1); x.arc(cx - s * 0.3, cy - s * 0.1, s * 0.06, 0, TAU); x.moveTo(cx + s * 0.35, cy - s * 0.1); x.arc(cx + s * 0.3, cy - s * 0.1, s * 0.06, 0, TAU); x.moveTo(cx - s * 0.4, cy + s * 0.2); x.quadraticCurveTo(cx, cy + s * 0.65, cx + s * 0.4, cy + s * 0.2); }
  };
  const CHALK = [['heart', 0.09, 0.2, 0.05], ['star', 0.3, 0.14, 0.045], ['cloud', 0.86, 0.16, 0.05], ['note', 0.94, 0.42, 0.05], ['cat', 0.1, 0.62, 0.06], ['smile', 0.88, 0.66, 0.05], ['star', 0.62, 0.1, 0.03], ['heart', 0.72, 0.78, 0.04], ['star', 0.05, 0.42, 0.03], ['note', 0.22, 0.78, 0.04]];

  K.registerBg('echoHokago', '放課後', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.8), dark = isDark(sc.bg), portrait = H > W;
    const key = `hokago|${seed}|${colourKey(sc)}`;
    ctx.globalAlpha = e;
    if (dark) {
      /* ---- blackboard ---- */
      const board = layer(env, `${key}|board`, 0.6, (x, w, h) => {
        x.fillStyle = vertical(x, 0, h, [[0, mix(sc.bg, '#000000', 0.15)], [0.5, sc.bg], [1, mix(sc.bg, '#000000', 0.1)]]); x.fillRect(0, 0, w, h);
        for (let i = 0; i < 26; i++) {             /* eraser smudges */
          const cx = w * J.r(seed, i, 1), cy = h * J.r(seed, i, 2), r = U * (0.12 + 0.3 * J.r(seed, i, 3));
          const g = x.createRadialGradient(cx, cy, 0, cx, cy, r); g.addColorStop(0, rgba(sc.fg, 0.05 + 0.04 * J.r(seed, i, 4))); g.addColorStop(1, rgba(sc.fg, 0));
          x.save(); x.translate(cx, cy); x.rotate(J.r(seed, i, 5) * 0.6 - 0.3); x.scale(1.9, 0.55); x.translate(-cx, -cy); x.fillStyle = g; x.fillRect(cx - r, cy - r, r * 2, r * 2); x.restore();
        }
        const rail = U * 0.028;                     /* wooden frame */
        x.fillStyle = '#C79A68'; x.fillRect(0, 0, w, rail); x.fillRect(0, h - rail, w, rail); x.fillRect(0, 0, rail, h); x.fillRect(w - rail, 0, rail, h);
        x.fillStyle = rgba('#000000', 0.14); x.fillRect(0, rail, w, rail * 0.25); x.fillRect(rail, rail, rail * 0.25, h - rail * 2);
        x.fillStyle = rgba('#FFFFFF', 0.2); x.fillRect(0, 0, w, rail * 0.22);
        const ledge = H * 0.9;                      /* chalk ledge with chalk and an eraser */
        x.fillStyle = '#B98A58'; x.fillRect(rail, ledge, w - rail * 2, h - ledge - rail);
        x.fillStyle = rgba('#FFFFFF', 0.18); x.fillRect(rail, ledge, w - rail * 2, U * 0.006);
        [[0.14, sc.fg], [0.17, sc.accent], [0.2, sc.accent2]].forEach(([u, c], i) => { x.fillStyle = c; x.beginPath(); x.roundRect(w * u, ledge - U * 0.018, U * 0.075, U * 0.018, U * 0.006); x.fill(); x.fillStyle = rgba('#000000', 0.12); x.fillRect(w * u + U * 0.02, ledge - U * 0.018, U * 0.006, U * 0.018); });
        x.fillStyle = '#D9C6A3'; x.beginPath(); x.roundRect(w * 0.72, ledge - U * 0.032, U * 0.11, U * 0.032, U * 0.006); x.fill();
        x.fillStyle = '#6E7E9A'; x.fillRect(w * 0.72, ledge - U * 0.012, U * 0.11, U * 0.012);
      });
      ctx.drawImage(board, 0, 0, W, H);
      /* chalk doodles that draw themselves on, one after another */
      const since = K.sinceStart(env);
      ctx.lineCap = 'round'; ctx.lineJoin = 'round'; ctx.lineWidth = Math.max(1.6, U * 0.005);
      CHALK.forEach(([kind, ux, uy, us], i) => {
        const colour = [sc.fg, sc.accent, sc.accent2, sc.fg][i % 4], p = E.outCubic(clamp((since - 0.2 - i * 0.28) / 1.1)), s = U * us * (portrait ? 0.9 : 1);
        if (p <= 0) return;
        const wob = Math.sin(t * 0.6 + i) * 0.006 * U;
        ctx.save(); ctx.translate(wob, Math.cos(t * 0.5 + i * 2) * 0.004 * U);
        ctx.strokeStyle = rgba(colour, 0.78); ctx.setLineDash([s * 14 * p, s * 14]); chalk(ctx, kind, W * ux, H * uy, s); ctx.stroke(); ctx.setLineDash([]);
        ctx.restore();
      });
    } else {
      /* ---- pastel classroom ---- */
      const wx = portrait ? W * 0.14 : W * 0.56, wy = portrait ? H * 0.06 : H * 0.11, ww = portrait ? W * 0.72 : W * 0.38, wh = portrait ? H * 0.3 : H * 0.55;
      const room = layer(env, `${key}|room`, 0.6, (x, w, h) => {
        x.fillStyle = vertical(x, 0, h, [[0, mix(sc.bg, '#FFFFFF', 0.35)], [0.7, sc.bg], [1, mix(sc.bg, sc.accent, 0.12)]]); x.fillRect(0, 0, w, h);
        x.strokeStyle = rgba(sc.ghostB, 0.22); x.lineWidth = Math.max(1, U * 0.0018);          /* notebook lines on the wall */
        for (let y = U * 0.06; y < h; y += U * 0.045) { x.beginPath(); x.moveTo(0, y); x.lineTo(w, y); x.stroke(); }
        x.strokeStyle = rgba(sc.accent, 0.3); x.beginPath(); x.moveTo(U * 0.09, 0); x.lineTo(U * 0.09, h); x.moveTo(U * 0.1, 0); x.lineTo(U * 0.1, h); x.stroke();
        /* window frame */
        x.fillStyle = rgba(sc.fg, 0.1); roundRect(x, wx - U * 0.018, wy - U * 0.018 + U * 0.01, ww + U * 0.036, wh + U * 0.04, U * 0.03); x.fill();
        x.fillStyle = '#FFFFFF'; roundRect(x, wx - U * 0.018, wy - U * 0.018, ww + U * 0.036, wh + U * 0.036, U * 0.03); x.fill();
        x.save(); roundRect(x, wx, wy, ww, wh, U * 0.016); x.clip();
        x.fillStyle = vertical(x, wy, wy + wh, [[0, '#9FD8FA'], [0.7, '#DDF1FF'], [1, mix('#FFE7F0', sc.accent, 0.15)]]); x.fillRect(wx, wy, ww, wh);
        x.fillStyle = mix('#FFFFFF', sc.accent, 0.12);   /* far hill */
        x.beginPath(); x.moveTo(wx, wy + wh); x.bezierCurveTo(wx + ww * 0.2, wy + wh * 0.62, wx + ww * 0.45, wy + wh * 0.86, wx + ww * 0.7, wy + wh * 0.66); x.bezierCurveTo(wx + ww * 0.85, wy + wh * 0.58, wx + ww * 0.95, wy + wh * 0.7, wx + ww, wy + wh * 0.64); x.lineTo(wx + ww, wy + wh); x.fill();
        /* sakura branch */
        x.strokeStyle = mix('#8A5A44', sc.bg, 0.15); x.lineWidth = U * 0.012; x.lineCap = 'round';
        x.beginPath(); x.moveTo(wx + ww * 1.02, wy + wh * 0.12); x.bezierCurveTo(wx + ww * 0.8, wy + wh * 0.16, wx + ww * 0.7, wy + wh * 0.3, wx + ww * 0.48, wy + wh * 0.28); x.stroke();
        x.lineWidth = U * 0.006; x.beginPath(); x.moveTo(wx + ww * 0.78, wy + wh * 0.18); x.quadraticCurveTo(wx + ww * 0.72, wy + wh * 0.06, wx + ww * 0.6, wy + wh * 0.04); x.stroke();
        for (let i = 0; i < 30; i++) {
          const u = J.r(seed, i, 61), bx = wx + ww * (0.44 + 0.58 * u), by = wy + wh * (0.05 + 0.3 * J.r(seed, i, 62)) + Math.sin(u * 6) * wh * 0.04, r = U * (0.02 + 0.022 * J.r(seed, i, 63));
          sakura(x, bx, by, r, J.r(seed, i, 64) * TAU, mix('#FFE3EC', sc.accent, 0.1 + 0.25 * J.r(seed, i, 65)), '#FFD34D');
        }
        x.restore();
        x.fillStyle = '#FFFFFF'; x.fillRect(wx + ww * 0.5 - U * 0.005, wy, U * 0.01, wh); x.fillRect(wx, wy + wh * 0.5 - U * 0.005, ww, U * 0.01);
        x.fillStyle = mix('#FFFFFF', sc.fg, 0.08); roundRect(x, wx - U * 0.04, wy + wh + U * 0.012, ww + U * 0.08, U * 0.022, U * 0.008); x.fill();
        /* desk */
        const desk = h * 0.88;
        x.fillStyle = mix('#F4D2A6', sc.bg, 0.25); x.fillRect(0, desk, w, h - desk);
        x.fillStyle = rgba('#FFFFFF', 0.4); x.fillRect(0, desk, w, U * 0.008);
        x.strokeStyle = rgba('#8A5A44', 0.14); x.lineWidth = Math.max(1, U * 0.002);
        for (let i = 0; i < 5; i++) { const y = desk + U * 0.02 + i * U * 0.02; x.beginPath(); x.moveTo(0, y); x.bezierCurveTo(w * 0.3, y + U * 0.005, w * 0.6, y - U * 0.005, w, y + U * 0.004); x.stroke(); }
        /* strawberry-milk carton, pencil and eraser */
        const bx = w * (portrait ? 0.78 : 0.86), by = desk + U * 0.004, bw = U * 0.07, bh = U * 0.1;
        x.fillStyle = '#FFF4F7'; roundRect(x, bx, by - bh, bw, bh, U * 0.006); x.fill();
        x.fillStyle = '#FFB1C8'; x.fillRect(bx, by - bh * 0.62, bw, bh * 0.4);
        x.fillStyle = '#FFF4F7'; x.beginPath(); x.moveTo(bx, by - bh); x.lineTo(bx + bw * 0.5, by - bh * 1.22); x.lineTo(bx + bw, by - bh); x.fill();
        x.fillStyle = '#E2456F'; heart(x, bx + bw * 0.5, by - bh * 0.42, bw * 0.16); x.fill();
        x.fillStyle = rgba('#000000', 0.06); x.fillRect(bx + bw * 0.74, by - bh, bw * 0.26, bh);
        x.save(); x.translate(w * (portrait ? 0.18 : 0.12), desk + U * 0.03); x.rotate(-0.12);
        x.fillStyle = '#FFD34D'; x.fillRect(0, -U * 0.008, U * 0.16, U * 0.016); x.fillStyle = '#F6C7A0'; x.beginPath(); x.moveTo(U * 0.16, -U * 0.008); x.lineTo(U * 0.19, 0); x.lineTo(U * 0.16, U * 0.008); x.fill();
        x.fillStyle = '#2E2E3A'; x.beginPath(); x.moveTo(U * 0.18, -U * 0.0025); x.lineTo(U * 0.19, 0); x.lineTo(U * 0.18, U * 0.0025); x.fill();
        x.fillStyle = '#FF7FA6'; x.fillRect(-U * 0.02, -U * 0.008, U * 0.02, U * 0.016); x.fillStyle = '#C9CED8'; x.fillRect(-U * 0.006, -U * 0.008, U * 0.006, U * 0.016);
        x.restore();
        /* notes stuck on the wall */
        [[0.06, 0.66, -0.1, '#FFF3A6', 'heart'], [0.16, 0.74, 0.08, '#BFEAFF', 'star'], [portrait ? 0.62 : 0.5, 0.7, 0.06, '#FFD6E4', 'heart']].forEach(([ux, uy, rot, colour, kind]) => {
          x.save(); x.translate(w * ux + U * 0.04, h * uy + U * 0.04); x.rotate(rot);
          x.fillStyle = rgba('#000000', 0.08); x.fillRect(-U * 0.04 + U * 0.004, -U * 0.04 + U * 0.006, U * 0.08, U * 0.08);
          x.fillStyle = colour; x.fillRect(-U * 0.04, -U * 0.04, U * 0.08, U * 0.08);
          x.fillStyle = rgba('#FFFFFF', 0.6); x.fillRect(-U * 0.02, -U * 0.048, U * 0.04, U * 0.014);
          x.fillStyle = kind === 'heart' ? '#FF7FA6' : '#FFB030'; if (kind === 'heart') heart(x, 0, 0, U * 0.02); else star(x, 0, 0, U * 0.024, 5, 0.42); x.fill();
          x.restore();
        });
      });
      ctx.drawImage(room, 0, 0, W, H);
      /* clouds drifting behind the glass */
      ctx.save(); roundRect(ctx, wx, wy, ww, wh, U * 0.016); ctx.clip();
      for (let i = 0; i < 4; i++) {
        const cw = ww * (0.28 + 0.16 * J.r(seed, i, 71)), cx = wx + wrap(J.r(seed, i, 72) * ww * 1.6 + t * U * (0.006 + 0.004 * J.r(seed, i, 73)), ww * 1.6) - cw * 0.3, cy = wy + wh * (0.22 + 0.36 * J.r(seed, i, 74));
        K.puffCloud(ctx, cx, cy, cw, cw * 0.5, '#FFFFFF', 0.96, i + 3, 1, false);
      }
      ctx.restore();
      /* sunlight through the glass, breathing */
      const lit = 0.16 + 0.05 * Math.sin(t * 0.5), lx = wx + ww * 0.05, ly = wy + wh;
      const beam = ctx.createLinearGradient(lx, ly, lx - U * 0.2, H);
      beam.addColorStop(0, `rgba(255,250,225,${lit * 1.4})`); beam.addColorStop(1, 'rgba(255,250,225,0)');
      ctx.fillStyle = beam; ctx.beginPath(); ctx.moveTo(wx, wy + wh); ctx.lineTo(wx + ww, wy + wh); ctx.lineTo(wx + ww - U * 0.25, H * 0.9); ctx.lineTo(wx - U * 0.3, H * 0.9); ctx.closePath(); ctx.fill();
      /* curtains breathing in the breeze */
      const sway = Math.sin(t * 0.7 + seed) * U * 0.012, cur = mix('#FFFFFF', sc.accent, 0.4);
      [[wx - U * 0.03, -1], [wx + ww + U * 0.03, 1]].forEach(([cx, dir]) => {
        const g = ctx.createLinearGradient(cx - dir * U * 0.12, 0, cx + dir * U * 0.06, 0); g.addColorStop(0, rgba(cur, 0.92)); g.addColorStop(0.5, rgba(mix(cur, '#FFFFFF', 0.3), 0.92)); g.addColorStop(1, rgba(mix(cur, '#000000', 0.06), 0.92));
        ctx.fillStyle = g; ctx.beginPath(); ctx.moveTo(cx, wy - U * 0.03); ctx.lineTo(cx - dir * U * 0.12, wy - U * 0.03);
        ctx.bezierCurveTo(cx - dir * (U * 0.14 + sway), wy + wh * 0.4, cx - dir * (U * 0.06 - sway), wy + wh * 0.7, cx - dir * (U * 0.1 + sway * 1.4), wy + wh + U * 0.04);
        ctx.lineTo(cx + dir * U * 0.005, wy + wh + U * 0.04); ctx.bezierCurveTo(cx + dir * U * 0.02, wy + wh * 0.6, cx - dir * U * 0.01, wy + wh * 0.3, cx, wy - U * 0.03); ctx.fill();
        ctx.strokeStyle = rgba(cur, 0.6); ctx.lineWidth = Math.max(1, U * 0.002);
        for (let f = 1; f < 4; f++) { ctx.beginPath(); ctx.moveTo(cx - dir * U * 0.03 * f, wy); ctx.bezierCurveTo(cx - dir * (U * 0.03 * f + sway), wy + wh * 0.4, cx - dir * (U * 0.02 * f), wy + wh * 0.7, cx - dir * (U * 0.028 * f + sway), wy + wh); ctx.stroke(); }
      });
    }
    /* bunting across the top: pastel pennants swaying on a string */
    {
      const flags = Math.round(W / (U * 0.11)), cols = dark ? [sc.accent, sc.accent2, '#9FE3F5', '#FFFFFF'] : ['#FFFFFF', sc.accent, sc.accent2, sc.ghostB], sag = U * 0.05, y0 = -U * 0.005;
      ctx.strokeStyle = rgba(dark ? sc.fg : mix(sc.fg, sc.bg, 0.4), 0.5); ctx.lineWidth = Math.max(1, U * 0.002); ctx.beginPath(); ctx.moveTo(-5, y0); ctx.quadraticCurveTo(W * 0.5, y0 + sag * 2, W + 5, y0); ctx.stroke();
      for (let i = 0; i < flags; i++) {
        const u = (i + 0.5) / flags, x0 = u * W, y = (1 - u) * (1 - u) * y0 + 2 * u * (1 - u) * (y0 + sag * 2) + u * u * y0, sw = Math.sin(t * 1.2 + i * 0.8) * U * 0.004, fw = U * 0.034, fh = U * 0.05;
        ctx.fillStyle = rgba(cols[i % cols.length], dark ? 0.92 : 0.95); ctx.beginPath(); ctx.moveTo(x0 - fw, y); ctx.lineTo(x0 + fw, y); ctx.lineTo(x0 + sw, y + fh); ctx.closePath(); ctx.fill();
        ctx.fillStyle = rgba('#FFFFFF', dark ? 0.5 : 0.7); if (i % 2) { heart(ctx, x0 + sw * 0.3, y + fh * 0.32, fw * 0.22); ctx.fill(); } else { star(ctx, x0 + sw * 0.3, y + fh * 0.32, fw * 0.3, 5, 0.45); ctx.fill(); }
      }
    }
    /* petals drifting through both rooms */
    for (let i = 0; i < 16; i++) {
      const speed = H * (0.03 + 0.05 * J.r(seed, i, 81)), y = wrap(J.r(seed, i, 82) * H * 1.2 + t * speed, H * 1.2) - H * 0.1, x = wrap(J.r(seed, i, 83) * W + Math.sin(t * 0.5 + i) * U * 0.05 - t * U * 0.01, W + 40) - 20;
      ctx.save(); ctx.translate(x, y); ctx.rotate(t * (0.4 + J.r(seed, i, 84)) + i); ctx.scale(1, 0.6 + 0.4 * Math.abs(Math.sin(t + i)));
      ctx.fillStyle = rgba(dark ? '#FFD3E0' : mix('#FFC4D6', sc.accent, 0.3), 0.8); petal(ctx, 0, 0, U * (0.008 + 0.008 * J.r(seed, i, 85))); ctx.fill(); ctx.restore();
    }
  });
  K.registerFront('echoHokagoPetals', '花びらの午後', (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, U = Umin(env), seed = P.seed || 1, e = fade(env, 1), dark = isDark(sc.bg);
    ctx.globalAlpha = e;
    const tint = dark ? '#FFD3E0' : mix('#FF9DBA', sc.accent, 0.4);
    for (let i = 0; i < 9; i++) {
      const speed = H * (0.05 + 0.06 * J.r(seed, i, 1)), y = wrap(J.r(seed, i, 2) * H * 1.3 + t * speed, H * 1.3) - H * 0.15, x = wrap(J.r(seed, i, 3) * W + Math.sin(t * 0.7 + i * 1.7) * U * 0.06 - t * U * 0.012, W + 60) - 30;
      const s = U * (0.013 + 0.012 * J.r(seed, i, 4)), turn = t * (0.8 + J.r(seed, i, 5)) + i;
      ctx.save(); ctx.translate(x, y); ctx.rotate(Math.sin(turn * 0.6) * 1.0 + J.r(seed, i, 6) * TAU); ctx.scale(Math.max(0.25, Math.abs(Math.cos(turn))), 1);
      ctx.fillStyle = rgba(tint, 0.72); petal(ctx, 0, 0, s); ctx.fill(); ctx.restore();
    }
    for (let i = 0; i < 6; i++) {                /* tiny hearts and sparkles blinking */
      const x = W * J.r(seed, i, 21), y = H * (0.12 + 0.76 * J.r(seed, i, 22)), b = Math.pow(0.5 + 0.5 * Math.sin(t * (1 + J.r(seed, i, 23)) + i * 2.2), 3), s = U * (0.008 + 0.01 * J.r(seed, i, 24));
      ctx.fillStyle = rgba(i % 2 ? sc.accent2 : tint, 0.15 + 0.6 * b);
      if (i % 2) { sparkle(ctx, x, y, s * 1.6); ctx.fill(); } else { heart(ctx, x, y, s); ctx.fill(); }
    }
    K.luxe(ctx, env, { seed, tint: dark ? ['#FFD3E0', sc.accent2, '#9FE3F5'] : [sc.accent, sc.accent2, '#FFFFFF'], strength: 0.9, bokeh: 6, glitter: 8 });
  });

  /* ================= 星のまほう ================= */
  const pentagram = (ctx, cx, cy, r, rot) => {
    ctx.beginPath();
    for (let i = 0; i <= 5; i++) { const a = rot + ((i * 2) % 5) / 5 * TAU - Math.PI / 2; if (i) ctx.lineTo(cx + Math.cos(a) * r, cy + Math.sin(a) * r); else ctx.moveTo(cx + Math.cos(a) * r, cy + Math.sin(a) * r); }
    ctx.closePath();
  };
  K.registerBg('echoMahou', '星のまほう', ['emotional', 'pop', 'calm'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 0.9), dark = isDark(sc.bg), portrait = H > W;
    const key = `mahou|${seed}|${colourKey(sc)}`, blend = dark ? 'screen' : 'source-over', palette = hues(sc);
    const spark = dark ? ['#FFFFFF', sc.accent, sc.accent2, sc.ghostB] : palette;
    ctx.globalAlpha = e;
    const sky = layer(env, `${key}|sky`, 0.5, (x, w, h) => {
      x.fillStyle = vertical(x, 0, h, [[0, dark ? mix(sc.bg, '#000000', 0.32) : mix(sc.bg, '#FFFFFF', 0.3)], [0.55, sc.bg], [1, mix(sc.bg, sc.accent, dark ? 0.3 : 0.2)]]); x.fillRect(0, 0, w, h);
      x.globalCompositeOperation = blend;         /* nebula: big soft colour clouds */
      for (let i = 0; i < 9; i++) {
        const cx = w * J.r(seed, i, 1), cy = h * J.r(seed, i, 2), r = U * (0.3 + 0.5 * J.r(seed, i, 3)), c = [sc.accent, sc.accent2, sc.ghostB, sc.ghostA][i % 4];
        const g = x.createRadialGradient(cx, cy, 0, cx, cy, r); g.addColorStop(0, rgba(c, dark ? 0.2 : 0.16)); g.addColorStop(1, rgba(c, 0));
        x.fillStyle = g; x.fillRect(cx - r, cy - r, r * 2, r * 2);
      }
      x.globalCompositeOperation = 'source-over';
      for (let i = 0; i < 160; i++) {              /* still stars */
        const c = spark[i % spark.length]; x.fillStyle = rgba(c, dark ? 0.25 + 0.6 * J.r(seed, i, 12) : 0.28 + 0.3 * J.r(seed, i, 12));
        x.beginPath(); x.arc(w * J.r(seed, i, 10), h * J.r(seed, i, 11) * 0.9, U * (0.0016 + 0.003 * J.r(seed, i, 13)), 0, TAU); x.fill();
      }
    });
    ctx.drawImage(sky, 0, 0, W, H);
    /* a rainbow arc, very pale, behind everything low down */
    const rx = W * (portrait ? 0.3 : 0.2), ry = H * 1.02, rr = U * 0.7;
    ['#FF8FA8', '#FFC58A', '#FFF09A', '#A8F0B8', '#9FDCFF', '#C7A8FF'].forEach((c, i) => { ctx.strokeStyle = rgba(c, dark ? 0.2 : 0.3); ctx.lineWidth = U * 0.02; ctx.beginPath(); ctx.arc(rx, ry, rr - i * U * 0.02, Math.PI * 1.05, Math.PI * 1.95); ctx.stroke(); });
    /* crescent moon */
    const mx = W * (portrait ? 0.78 : 0.12), my = H * (portrait ? 0.14 : 0.2), mr = U * 0.075;
    glowDisc(ctx, mx, my, mr * 4, dark ? sc.accent2 : sc.accent, dark ? 0.35 : 0.22);
    ctx.save(); ctx.translate(mx, my + Math.sin(t * 0.6) * U * 0.006); ctx.fillStyle = dark ? '#FFF6D0' : mix('#FFF3B0', sc.accent2, 0.3);
    ctx.beginPath(); ctx.rect(-mr * 3, -mr * 3, mr * 6, mr * 6); ctx.arc(mr * 0.5, -mr * 0.2, mr * 0.86, 0, TAU, true); ctx.clip('evenodd');
    ctx.beginPath(); ctx.arc(0, 0, mr, 0, TAU); ctx.fill(); ctx.restore();
    ctx.globalCompositeOperation = 'source-over';
    /* the magic circle: two rings, ticks, a pentagram and a slow counter-rotating hexagram */
    const cx = W * 0.5, cy = H * (portrait ? 0.4 : 0.5), R = U * (portrait ? 0.46 : 0.44), spin = t * 0.09, gold = dark ? sc.accent2 : sc.accent, pink = dark ? sc.accent : sc.accent2;
    ctx.globalCompositeOperation = blend; ctx.lineCap = 'round';
    const line = (c, a, w) => { ctx.strokeStyle = rgba(c, a * (dark ? 1 : 1.2)); ctx.lineWidth = Math.max(1, w); };
    line(gold, 0.42, U * 0.004); ctx.beginPath(); ctx.arc(cx, cy, R, 0, TAU); ctx.stroke();
    line(gold, 0.26, U * 0.002); ctx.beginPath(); ctx.arc(cx, cy, R * 0.93, 0, TAU); ctx.stroke(); ctx.beginPath(); ctx.arc(cx, cy, R * 0.62, 0, TAU); ctx.stroke();
    ctx.save(); ctx.translate(cx, cy); ctx.rotate(spin);
    line(pink, 0.4, U * 0.003); for (let i = 0; i < 48; i++) { const a = i / 48 * TAU, l = i % 4 === 0 ? 0.055 : 0.025; ctx.beginPath(); ctx.moveTo(Math.cos(a) * R * 0.93, Math.sin(a) * R * 0.93); ctx.lineTo(Math.cos(a) * R * (0.93 - l), Math.sin(a) * R * (0.93 - l)); ctx.stroke(); }
    for (let i = 0; i < 6; i++) { const a = i / 6 * TAU; ctx.fillStyle = rgba(pink, dark ? 0.7 : 0.8); heart(ctx, Math.cos(a) * R * 1.0, Math.sin(a) * R * 1.0, U * 0.012); ctx.fill(); }
    ctx.restore();
    line(gold, 0.36, U * 0.0032); pentagram(ctx, cx, cy, R * 0.62, -spin * 1.4); ctx.stroke();
    line(pink, 0.3, U * 0.0026); ctx.save(); ctx.translate(cx, cy); ctx.rotate(-spin * 0.8); for (let k = 0; k < 2; k++) { ctx.beginPath(); for (let i = 0; i < 3; i++) { const a = i / 3 * TAU + k * Math.PI / 3 - Math.PI / 2; const px = Math.cos(a) * R * 0.33, py = Math.sin(a) * R * 0.33; if (i) ctx.lineTo(px, py); else ctx.moveTo(px, py); } ctx.closePath(); ctx.stroke(); } ctx.restore();
    for (let i = 0; i < 5; i++) {                 /* little stars orbiting the ring */
      const a = -spin * 2.2 + i / 5 * TAU, b = 0.6 + 0.4 * Math.sin(t * 2 + i * 1.7);
      ctx.fillStyle = rgba(spark[i % spark.length], (dark ? 0.9 : 0.85) * b); sparkle(ctx, cx + Math.cos(a) * R * 1.08, cy + Math.sin(a) * R * 1.08, U * 0.02); ctx.fill();
    }
    ctx.globalCompositeOperation = 'source-over';
    /* candy planet with a ring */
    const px = W * (portrait ? 0.2 : 0.86), py = H * (portrait ? 0.7 : 0.26) + Math.sin(t * 0.5 + seed) * U * 0.012, pr = U * 0.055;
    ctx.save(); ctx.translate(px, py); ctx.rotate(-0.35);
    ctx.strokeStyle = rgba(sc.accent2, 0.75); ctx.lineWidth = pr * 0.16; ctx.beginPath(); ctx.ellipse(0, 0, pr * 1.7, pr * 0.45, 0, Math.PI, TAU); ctx.stroke();
    const pg = ctx.createRadialGradient(-pr * 0.3, -pr * 0.3, pr * 0.1, 0, 0, pr); pg.addColorStop(0, mix(sc.accent, '#FFFFFF', 0.5)); pg.addColorStop(1, mix(sc.accent, dark ? '#4B2A8A' : '#B36ABF', 0.35));
    ctx.fillStyle = pg; ctx.beginPath(); ctx.arc(0, 0, pr, 0, TAU); ctx.fill();
    ctx.strokeStyle = rgba('#FFFFFF', 0.35); ctx.lineWidth = pr * 0.1; ctx.beginPath(); ctx.arc(0, 0, pr * 0.7, Math.PI * 1.1, Math.PI * 1.6); ctx.stroke();
    ctx.strokeStyle = rgba(sc.accent2, 0.75); ctx.lineWidth = pr * 0.16; ctx.beginPath(); ctx.ellipse(0, 0, pr * 1.7, pr * 0.45, 0, 0, Math.PI); ctx.stroke(); ctx.restore();
    /* pastel cloud bank along the bottom, two speeds */
    const bank = layer(env, `${key}|bank`, 0.5, (x, w, h) => {
      for (let row = 0; row < 2; row++) for (let i = 0; i < 9; i++) {
        const cw = U * (0.34 + 0.3 * J.r(seed, row * 20 + i, 31)), cx0 = w * ((i + J.r(seed, row * 20 + i, 32) * 0.6) / 9), cy0 = h * (0.94 + row * 0.05 + 0.03 * J.r(seed, row * 20 + i, 33));
        const c = row ? mix(sc.bg, sc.accent, dark ? 0.35 : 0.25) : mix(sc.bg, '#FFFFFF', dark ? 0.32 : 0.55);
        for (const shift of [-w, 0, w]) { K.puffCloud(x, cx0 + shift, cy0, cw, cw * 0.5, c, dark ? 0.9 : 0.96, J.h(seed, row * 20 + i, 34), row ? -1 : 1, dark); }
      }
    });
    scroll(ctx, bank, env, t * U * 0.006, 1, 0, 0.9); scroll(ctx, bank, env, -t * U * 0.01 + W * 0.3, 1, U * 0.02, 1);
    /* twinkling four-point stars and a comet or two */
    ctx.globalCompositeOperation = blend;
    for (let i = 0; i < 24; i++) {
      const b = Math.pow(0.5 + 0.5 * Math.sin(t * (0.9 + 1.6 * J.r(seed, i, 41)) + i * 3.1), 2), s = U * (0.008 + 0.018 * J.r(seed, i, 42)) * (0.6 + b);
      ctx.fillStyle = rgba(spark[i % spark.length], (dark ? 0.35 : 0.45) + 0.55 * b); sparkle(ctx, W * J.r(seed, i, 43), H * J.r(seed, i, 44) * 0.8, s); ctx.fill();
    }
    for (let k = 0; k < 2; k++) {
      const period = 5.5 + k * 2.3, ph = (t + k * 2.1 + J.r(seed, k, 51) * 4) / period, n = Math.floor(ph), f = ph - n;
      if (f > 0.22) continue;
      const q = f / 0.22, sx0 = W * (0.15 + 0.7 * J.r(seed, n * 2 + k, 52)), sy0 = H * (0.05 + 0.3 * J.r(seed, n * 2 + k, 53)), dx = U * 0.5 * q, dy = U * 0.22 * q;
      const g = ctx.createLinearGradient(sx0 + dx, sy0 + dy, sx0 + dx - U * 0.24, sy0 + dy - U * 0.1); g.addColorStop(0, rgba('#FFFFFF', (1 - q) * 0.95)); g.addColorStop(1, rgba(sc.accent2, 0));
      ctx.strokeStyle = g; ctx.lineWidth = U * 0.005; ctx.beginPath(); ctx.moveTo(sx0 + dx, sy0 + dy); ctx.lineTo(sx0 + dx - U * 0.24, sy0 + dy - U * 0.1); ctx.stroke();
      ctx.fillStyle = rgba('#FFFFFF', 1 - q); sparkle(ctx, sx0 + dx, sy0 + dy, U * 0.02); ctx.fill();
    }
    ctx.globalCompositeOperation = 'source-over';
  });
  K.registerFront('echoMahouSparkle', '星屑', (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, U = Umin(env), seed = P.seed || 1, e = fade(env, 1), dark = isDark(sc.bg), palette = dark ? ['#FFFFFF', sc.accent, sc.accent2] : hues(sc);
    ctx.globalAlpha = e; ctx.globalCompositeOperation = dark ? 'screen' : 'source-over';
    for (let i = 0; i < 18; i++) {                 /* glitter falling slowly */
      const speed = H * (0.03 + 0.05 * J.r(seed, i, 1)), y = wrap(J.r(seed, i, 2) * H * 1.2 + t * speed, H * 1.2) - H * 0.1, x = J.r(seed, i, 3) * W + Math.sin(t * 0.6 + i) * U * 0.03;
      const b = 0.4 + 0.6 * Math.pow(0.5 + 0.5 * Math.sin(t * (2 + 3 * J.r(seed, i, 4)) + i * 2), 2), s = U * (0.005 + 0.008 * J.r(seed, i, 5));
      ctx.fillStyle = rgba(palette[i % palette.length], (dark ? 0.7 : 0.6) * b); sparkle(ctx, x, y, s * 1.8); ctx.fill();
    }
    for (let i = 0; i < 4; i++) {                  /* big sparkles that pop in and out near the corners */
      const cyc = 3.2 + i * 0.7, ph = wrap(t + i * 1.3, cyc) / cyc, b = Math.sin(Math.min(ph / 0.35, 1) * Math.PI) * (ph < 0.35 ? 1 : 0);
      if (b <= 0) continue;
      const n = Math.floor((t + i * 1.3) / cyc), x = W * (i % 2 ? 0.75 + 0.2 * J.r(seed, n, 11 + i) : 0.05 + 0.2 * J.r(seed, n, 11 + i)), y = H * (0.1 + 0.8 * J.r(seed, n, 21 + i));
      ctx.save(); ctx.translate(x, y); ctx.rotate(b * 0.6); ctx.fillStyle = rgba('#FFFFFF', 0.9 * b); sparkle(ctx, 0, 0, U * 0.04 * b); ctx.fill(); ctx.restore();
      glowDisc(ctx, x, y, U * 0.07 * b, palette[i % palette.length], 0.35 * b);
    }
    ctx.globalCompositeOperation = 'source-over';
    K.luxe(ctx, env, { seed, tint: dark ? [sc.accent, sc.accent2, '#9FE3F5', '#C7A8FF'] : [sc.accent, sc.accent2, '#FFFFFF'], strength: 1.1, bokeh: 7, glitter: 14 });
  });
})();
