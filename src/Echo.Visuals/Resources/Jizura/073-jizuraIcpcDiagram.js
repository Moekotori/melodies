/* Shared canvas vocabulary for the original ICPC algorithm studies. Frame-local only. */
(() => {
  'use strict';
  const K = J.echo, MONO = '"Cascadia Mono",Consolas,"SFMono-Regular",ui-monospace,monospace';
  const A = K.icpcDiagram = {};
  A.text = (ctx, value, x, y, size, color, align = 'left') => {
    ctx.font = `500 ${Math.max(9, size)}px ${MONO}`; ctx.textAlign = align;
    ctx.textBaseline = 'middle'; ctx.fillStyle = color; ctx.fillText(String(value), x, y);
  };
  A.line = (ctx, ax, ay, bx, by) => { ctx.beginPath(); ctx.moveTo(ax, ay); ctx.lineTo(bx, by); ctx.stroke(); };
  A.node = (ctx, x, y, radius, color, active = false) => {
    ctx.save();
    const halo = ctx.createRadialGradient(x, y, 0, x, y, radius * (active ? 5 : 3));
    halo.addColorStop(0, color); halo.addColorStop(1, 'rgba(0,0,0,0)');
    ctx.globalAlpha *= active ? 0.32 : 0.15; ctx.fillStyle = halo;
    ctx.beginPath(); ctx.arc(x, y, radius * (active ? 5 : 3), 0, K.TAU); ctx.fill();
    ctx.restore(); ctx.fillStyle = color; ctx.beginPath(); ctx.arc(x, y, radius, 0, K.TAU); ctx.fill();
    if (active) { ctx.save(); ctx.globalAlpha *= 0.55; ctx.strokeStyle = color; ctx.lineWidth = 1.5;
      ctx.beginPath(); ctx.arc(x, y, radius * 2, 0, K.TAU); ctx.stroke(); ctx.restore(); }
  };
  A.edge = (ctx, p, q, color, width, phase = -1, directed = false) => {
    ctx.save(); ctx.globalAlpha *= 0.13; ctx.strokeStyle = color; ctx.lineWidth = width * 5; A.line(ctx, p[0], p[1], q[0], q[1]); ctx.restore();
    ctx.strokeStyle = color; ctx.lineWidth = width; A.line(ctx, p[0], p[1], q[0], q[1]);
    if (phase >= 0) A.node(ctx, p[0] + (q[0] - p[0]) * phase, p[1] + (q[1] - p[1]) * phase, width * 2.2, color, true);
    if (directed) {
      const angle = Math.atan2(q[1] - p[1], q[0] - p[0]), x = p[0] + (q[0] - p[0]) * 0.78, y = p[1] + (q[1] - p[1]) * 0.78;
      A.line(ctx, x - Math.cos(angle - 0.5) * width * 6, y - Math.sin(angle - 0.5) * width * 6, x, y);
      A.line(ctx, x - Math.cos(angle + 0.5) * width * 6, y - Math.sin(angle + 0.5) * width * 6, x, y);
    }
  };
  A.project = (env, x, y, z, rotation = env.t * 0.09, cx = 0.5, cy = 0.55, zoom = 1) => {
    const co = Math.cos(rotation), si = Math.sin(rotation), xx = x * co + z * si, zz = z * co - x * si;
    const depth = 4.5 / (4.5 + zz), scale = Math.min(env.W * 0.22, env.H * 0.3) * zoom;
    return [env.W * cx + xx * scale * depth, env.H * cy + (-y + zz * 0.32) * scale * depth, depth];
  };
  A.ring = (env, ctx, T, radius, y, rotation, opacity = 0.3, cx = 0.5, cy = 0.55) => {
    ctx.save(); ctx.globalAlpha *= opacity; ctx.strokeStyle = T.a; ctx.lineWidth = 1;
    ctx.beginPath();
    for (let i = 0; i <= 64; i++) { const a = i / 64 * K.TAU, p = A.project(env, Math.cos(a) * radius, y, Math.sin(a) * radius, rotation, cx, cy); i ? ctx.lineTo(p[0], p[1]) : ctx.moveTo(p[0], p[1]); }
    ctx.stroke(); ctx.restore();
  };
  A.tile = (ctx, x, y, w, h, height, color, active = false) => {
    const face = (points, opacity) => { ctx.save(); ctx.globalAlpha *= opacity; ctx.fillStyle = color; ctx.beginPath(); points.forEach(([px, py], i) => i ? ctx.lineTo(px, py) : ctx.moveTo(px, py)); ctx.closePath(); ctx.fill(); ctx.strokeStyle = color; ctx.lineWidth = active ? 2 : 0.7; ctx.stroke(); ctx.restore(); };
    face([[x - w, y], [x, y + h], [x, y + h + height], [x - w, y + height]], 0.22);
    face([[x + w, y], [x, y + h], [x, y + h + height], [x + w, y + height]], 0.38);
    face([[x, y - h], [x + w, y], [x, y + h], [x - w, y]], active ? 0.65 : 0.22);
  };
  A.code = (env, ctx, T, lines, active) => {
    const portrait = env.H > env.W, x = env.W * (portrait ? 0.06 : 0.68), y = env.H * (portrait ? 0.185 : 0.19);
    const width = env.W * (portrait ? 0.88 : 0.285), fs = Math.max(9, Math.min(T.U * 0.017, width / 27)), lh = fs * 1.6;
    const rows = Math.min(lines.length, portrait ? 12 : 21);
    const first = Math.max(0, Math.min(lines.length - rows, active - Math.floor(rows * 0.45)));
    ctx.save();
    // A local fade keeps source legible over the sculpture without a boxed code card.
    const veil = ctx.createLinearGradient(x - fs * 2, 0, x + width, 0);
    veil.addColorStop(0, K.rgba(T.bg, 0)); veil.addColorStop(0.22, K.rgba(T.bg, 0.9)); veil.addColorStop(1, K.rgba(T.bg, 0.97));
    ctx.fillStyle = veil; ctx.fillRect(x - fs * 2, y - lh * 1.4, width + fs * 2, lh * (rows + 2));
    A.text(ctx, lines.length > rows ? `C++ / LINES ${first + 1}–${first + rows} / ${lines.length}` : 'C++ / CORE ROUTINE', x, y - lh, fs * 0.9, T.dim);
    for (let index = first; index < first + rows; index++) {
      const line = lines[index], yy = y + (index - first) * lh;
      if (index === active) { ctx.fillStyle = K.rgba(T.b, T.dark ? 0.12 : 0.09); ctx.fillRect(x - fs * 0.5, yy - lh * 0.48, width + fs, lh); }
      A.text(ctx, String(index + 1).padStart(2, '0'), x, yy, fs * 0.8, index === active ? T.b : T.dim);
      let cursor = x + fs * 2.4, offset = 0;
      const tokens = line.matchAll(/\/\/.*|\b(?:for|while|if|else|return|int|auto|void|const|bool|long|struct|using|char|continue|break)\b|\b\d+\b|[{}]/g);
      const draw = (value, color) => { A.text(ctx, value, cursor, yy, fs, color); cursor += ctx.measureText(value).width; };
      for (const match of tokens) { draw(line.slice(offset, match.index), index === active ? T.fg : T.dim); draw(match[0], match[0].startsWith('//') ? T.dim : /^\d/.test(match[0]) ? T.b : T.a); offset = match.index + match[0].length; }
      draw(line.slice(offset), index === active ? T.fg : T.dim);
    }
    ctx.restore();
  };
  A.frame = (env, ctx, title, detail, status) => {
    const { W, H, sc } = env, U = K.U(env), dark = K.isDark(sc.bg);
    const T = { U, dark, fg: sc.fg, a: sc.accent, b: sc.accent2, dim: K.rgba(sc.fg, dark ? 0.52 : 0.65),
      faint: K.rgba(sc.fg, 0.13), bg: K.mix(sc.bg, dark ? '#000000' : '#FFFFFF', 0.36) };
    ctx.globalAlpha = K.fade(env, 0.6); ctx.fillStyle = T.bg; ctx.fillRect(0, 0, W, H);
    const atmosphere = ctx.createRadialGradient(W * 0.5, H * 0.58, 0, W * 0.5, H * 0.58, U * 0.7);
    atmosphere.addColorStop(0, K.rgba(T.a, dark ? 0.09 : 0.055)); atmosphere.addColorStop(1, 'rgba(0,0,0,0)');
    ctx.fillStyle = atmosphere; ctx.fillRect(0, 0, W, H);
    // Perspective guides establish depth without another canvas or persistent particles.
    ctx.strokeStyle = K.rgba(T.a, 0.075); ctx.lineWidth = 1;
    for (let i = -8; i <= 8; i++) A.line(ctx, W * 0.5 + i * W * 0.025, H * 0.53, W * 0.5 + i * W * 0.15, H);
    for (let i = 0; i < 10; i++) { const y = H * (0.54 + Math.pow(i / 9, 2.3) * 0.46); A.line(ctx, 0, y, W, y); }
    A.text(ctx, 'ICPC / ALGORITHM LAB', W * 0.055, H * 0.055, U * 0.013, T.dim);
    ctx.font = `600 ${U * 0.045}px "Segoe UI Variable Display","Segoe UI","Helvetica Neue",Arial,sans-serif`;
    ctx.textAlign = 'left'; ctx.fillStyle = T.fg; ctx.fillText(title, W * 0.055, H * 0.115, W * 0.89);
    A.text(ctx, detail, W * 0.055, H * 0.875, U * 0.012, T.dim);
    A.text(ctx, status, W * 0.945, H * 0.955, U * 0.016, T.b, 'right');
    // Full-frame algorithm sculptures. Restrained central luminance lets lyrics sit in front.
    ctx.strokeStyle = T.faint; ctx.lineWidth = 1;
    A.line(ctx, W * 0.055, H * 0.91, W * 0.945, H * 0.91);
    return T;
  };
  A.at = (env, trace, seconds = 9) => {
    const progress = K.wrap(K.sinceStart(env), seconds) / (seconds * 0.76);
    const cursor = Math.min(trace.length - 1, progress * trace.length);
    const index = Math.floor(cursor);
    return { s: trace[index], index, phase: cursor - index, done: index === trace.length - 1 };
  };
})();
