/* First-party ECHO look composition over JIZURA's rendering primitives. */
(() => {
  'use strict';
  const commonBackdrops = J.echo.commonBackdrops || [];
  const direction = {
    layout: ['center', 'mixed', 'vcols', 'huge', 'columnsBig', 'typeSpecimen', 'mirror', 'poster'],
    enter: ['cut', 'blur', 'fadeStagger', 'blurStagger', 'riseMask', 'trackIn', 'assemble'],
    exit: ['cut', 'blur', 'drift', 'dissolve', 'blurOutStagger'],
    hold: ['still', 'drift', 'breathe', 'float', 'sway', 'trackBreathe'],
    decor: ['brackets', 'rings', 'waveLine', 'bokeh', 'twinkle', 'leaders', 'dots'],
    bg: ['none', 'bokehBg', 'gradientSweep', 'spotlight', 'particlesBg'],
    cam: ['push', 'driftDiag', 'pullOut'],
    fx: ['lightSweep', 'whiteFrame'],
  };
  const originalDesignSize = J.designSize;
  J.designSize = aspect => aspect === 'echo-window'
    ? [Math.round(1080 * Math.max(0.45, Math.min(3, innerWidth / Math.max(1, innerHeight)))), 1080]
    : originalDesignSize(aspect);

  const copy = palette => ({ ...palette });
  for (const look of ECHO_JIZURA_LOOKS) {
    const base = J.STYLES[look.baseStyle];
    const own = look.style || {};
    const texture = own.texture || {};
    J.STYLES[`echo-${look.id}`] = { ...base, extra: false, set: null,
      fonts: { ...base.fonts, ...own.fonts }, bias: { ...base.bias, ...own.bias }, decor: own.decor || base.decor,
      glow: own.glow ?? base.glow, useGrad: own.useGrad ?? base.useGrad,
      schemes: [copy(look.palette)], ghost: own.ghost ?? 0.4, hud: false,
      texture: { grain: texture.grain ?? 0.22, paper: texture.paper ?? (look.palette.paper ? 0.25 : 0), scan: texture.scan ?? 0 },
    };
  }
  // Lines the planner left without a backdrop get the look's own, and every line gets its foreground layer,
  // so no page is a flat colour and every look carries its finish in front of the lyric too.
  const signatures = new Map(ECHO_JIZURA_LOOKS.filter(look => look.backdrop || look.front || look.scenes).map(look => [`echo-${look.id}`, look]));
  const planWithSignature = J.plan;
  J.plan = (project, audio) => {
    const result = planWithSignature(project, audio);
    const signature = signatures.get(project.style);
    if (!signature) return result;
    const { backdrop, front, scenes } = signature;
    const themeOnly = project.echoThemeScenesOnly === true;
    // A look with several scenes walks through them in order, changing every second line; the start is seeded.
    const rotation = (backdrop ? [backdrop, ...(scenes || []), ...(themeOnly || signature.id === 'rhythm' ? [] : commonBackdrops)] : []).filter(name => J.BG[name]);
    const start = J.h(project.seed, 3, 408);
    for (const cut of result.cuts) {
      if (cut.companion) continue;
      if (rotation.length > 1 && (themeOnly || cut.bg === 'none' || rotation.includes(cut.bg))) {
        const group = Math.floor(cut.line / 2);
        cut.bg = rotation[(start + group) % rotation.length]; cut.bgP = { seed: J.h(project.seed, group + 7, 405) };
      } else if (backdrop && J.BG[backdrop] && (themeOnly || cut.bg === 'none')) { cut.bg = backdrop; cut.bgP = { seed: J.h(project.seed, cut.line + 7, 405) }; }
      else if (themeOnly) { cut.bg = 'none'; cut.bgP = {}; }
      if (front && J.DECOR[front] && !(cut.decor || []).some(item => item.id === front)) {
        cut.decor = [...(cut.decor || []), { id: front, seed: J.h(project.seed, cut.line + 11, 406) }];
      }
    }
    return result;
  };
  J.applyEchoDirection = (project, preferences, paletteVariant = 0) => {
    project.aspect = preferences.fit === 'cinema' ? '16:9' : 'echo-window';
    // Random composition: no unified conventions, and (below) every technique is let into the plan.
    const random = preferences.randomComposition === true;
    const themeOnly = preferences.themeScenesOnly === true;
    project.echoThemeScenesOnly = themeOnly;
    project.unify = !random && preferences.unifiedComposition !== false;
    if (preferences.originalStyle) {
      project.style = J.STYLE_ORDER.includes(preferences.originalStyle) ? preferences.originalStyle : 'noir';
      const own = J.STYLES[project.style].bias?.bg || {};
      project.enabled.bg = Object.fromEntries(J.order('bg').map(id => [id, !themeOnly || id === 'none' || own[id] > 0]));
      return;
    }
    const look = ECHO_JIZURA_LOOKS.find(item => item.id === preferences.look) || ECHO_JIZURA_LOOKS[0];
    const ownScenes = new Set([look.backdrop, ...(look.scenes || [])]);
    project.style = `echo-${look.id}`;
    Object.assign(project.fx, look.fx, { flash: false, hud: 'off' });
    if (random) Object.assign(project.fx, { decor: Math.max(look.fx.decor, 0.6), density: Math.max(look.fx.density, 0.55), bgSwitch: Math.max(look.fx.bgSwitch, 0.55) });
    project.colors = { enabled: false };
    if (look.colorways) {
      // Hand-built colourways: every variant is a full set of schemes (a section change may flip the ground).
      J.STYLES[project.style].schemes = look.colorways[paletteVariant % look.colorways.length].map(copy);
    } else if (paletteVariant % 3 === 1) {
      const p = look.palette;
      project.colors = { enabled: true, ...p, bg: p.fg, fg: p.bg, ink: p.bg, dim: p.sub, sub: p.dim };
    } else if (paletteVariant % 3 === 2) {
      project.colors = { enabled: true, ...look.palette, accent: look.palette.accent2,
        ghostA: look.palette.ghostB, ghostB: look.palette.ghostA };
    }
    // Whitelists steer a look; random composition lifts them (the planner still honours the extra-pack switch).
    for (const [group, allowed] of Object.entries({ ...direction, ...look.direction })) {
      project.enabled[group] = Object.fromEntries(J.order(group).map(id => [id,
        group === 'bg' && themeOnly ? id === 'none' || ownScenes.has(id)
          : random || allowed.includes(id) || group === 'bg' && look.id !== 'rhythm' && commonBackdrops.includes(id)]));
    }
  };
})();
