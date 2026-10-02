/* Data-only Afterglow pack interpreter. No author code, URLs, assets or host capabilities. */
(() => {
  'use strict';
  let current = null, ids = [];
  const colour = (sc, token) => token === 'transparent' || token?.startsWith('#') ? token : sc[token] || sc.accent;
  const clear = () => {
    for (const id of ids) {
      delete J.BG[id];
      const index = J.BG_ORDER.indexOf(id); if (index >= 0) J.BG_ORDER.splice(index, 1);
    }
    ids = []; current = null;
  };
  J.setAfterglowScenePack = pack => {
    if (!pack) { clear(); return; }
    const encoded = JSON.stringify(pack);
    if (current && encoded === JSON.stringify(current)) return;
    clear();
    if (encoded.length > 128 * 1024 || pack.schemaVersion !== 1 || !Array.isArray(pack.scenes) || !pack.scenes.length || pack.scenes.length > 12) throw new Error('Invalid Afterglow pack');
    for (const scene of pack.scenes) {
      if (!/^[a-z][a-z0-9_-]{0,31}$/.test(scene.id) || !Array.isArray(scene.layers) || scene.layers.length > 64) throw new Error('Invalid Afterglow scene');
    }
    current = pack;
    ids = pack.scenes.map(scene => {
      const id = `echoImported_${scene.id}`;
      J.echo.registerBg(id, scene.title, ['calm', 'graphic', 'emotional'], (env, P, ctx) => {
        if (J.echoTier >= 3) return;
        const { W, H, sc } = env, U = Math.min(W, H), t = J.echo.sinceStart(env);
        ctx.fillStyle = sc.bg; ctx.fillRect(0, 0, W, H);
        for (let li = 0; li < scene.layers.length; li++) {
          const layer = scene.layers[li], motion = layer.motion;
          const phase = t * (motion?.speed || 0) + (motion?.phase || 0);
          const x = W * (layer.x + Math.sin(phase) * (motion?.floatX || 0));
          const y = H * (layer.y + Math.cos(phase) * (motion?.floatY || 0));
          ctx.save(); ctx.translate(x, y); ctx.rotate(layer.rotation + t * (motion?.spin || 0));
          ctx.globalAlpha = layer.opacity * J.echo.fade(env, 0.6);
          ctx.fillStyle = colour(sc, layer.color); ctx.strokeStyle = colour(sc, layer.stroke || layer.color); ctx.lineWidth = layer.strokeWidth * U;
          ctx.beginPath();
          if (layer.type === 'circle') ctx.arc(0, 0, layer.radius * U, 0, Math.PI * 2);
          else if (layer.type === 'ellipse') ctx.ellipse(0, 0, layer.width * W / 2, layer.height * H / 2, 0, 0, Math.PI * 2);
          else if (layer.type === 'rect') ctx.rect(-layer.width * W / 2, -layer.height * H / 2, layer.width * W, layer.height * H);
          else if (layer.type === 'line') { ctx.moveTo(0, 0); ctx.lineTo(layer.width * W, layer.height * H); }
          else if (layer.type === 'polygon') {
            layer.points.forEach(([px, py], index) => { if (index) ctx.lineTo(px * W, py * H); else ctx.moveTo(px * W, py * H); }); ctx.closePath();
          } else if (layer.type === 'text') {
            ctx.font = `${layer.fontSize * U}px ui-monospace,Consolas,sans-serif`; ctx.textAlign = 'center'; ctx.fillText(layer.text, 0, 0);
          } else if (layer.type === 'particles') {
            for (let i = 0; i < layer.count; i++) {
              const px = (J.r(P.seed || 42, li * 100 + i, 1) - 0.5) * layer.width * W;
              const py = (J.r(P.seed || 42, li * 100 + i, 2) - 0.5) * layer.height * H;
              ctx.moveTo(px + layer.radius * U, py); ctx.arc(px, py, layer.radius * U, 0, Math.PI * 2);
            }
          }
          if (!['line', 'text'].includes(layer.type)) ctx.fill();
          if (layer.stroke || layer.type === 'line') ctx.stroke();
          ctx.restore();
        }
      }, false);
      return id;
    });
  };
  const plan = J.plan;
  J.plan = (project, audio) => {
    const result = plan(project, audio);
    if (!current || !ids.length || project.echoThemeScenesOnly === true) return result;
    for (const cut of result.cuts) {
      if (cut.companion || !Number.isInteger(cut.line)) continue;
      const group = Math.max(0, Math.floor(cut.line / 2));
      if (current.mode === 'only' || group % 3 === project.seed % 3) {
        cut.bg = ids[(J.h(project.seed, 408) + group) % ids.length];
        cut.bgP = { seed: J.h(project.seed, group + 7, 405) };
      }
    }
    return result;
  };
})();
