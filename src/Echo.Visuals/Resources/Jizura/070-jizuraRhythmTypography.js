/* Rhythm lyrics occupy the quiet side of the cabinet, rather than obscuring notes. */
(() => {
    'use strict';
    J.register('cam', 'echoRhythmStill', { name: '固定', get: () => ({ s: 1 }) }, 'echo');
    J.register('layout', 'echoRhythmLyric', {
        name: 'リズムの歌詞', fits: () => true,
        plan: (rng, cut, st) => ({ font: st.fonts.display[0] }),
        render(env) {
            const { W, H, sc, cut } = env, portrait = H > W;
            const text = J.splitLines(cut.text, portrait ? 7 : 8);
            const width = W * (portrait ? 0.86 : 0.42);
            const height = H * (portrait ? 0.19 : 0.46);
            const size = Math.min(J.fitSize(text, cut.params.font, width, height, { track: 0.025, lead: 1.25 }), H * 0.12);
            const x = W * (portrait ? 0.5 : 0.28);
            const y = H * (portrait ? 0.17 : 0.48);
            return J.mainDraw(env, { text, font: cut.params.font, size, x, y, track: 0.025, lead: 1.25, color: sc.fg, ghost: false });
        },
    }, 'echo');
})();
