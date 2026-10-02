/* Original heavy-light and prefix-tree artwork. Fixed 13-node tree and six short words.
   No input from the library, growing caches, timers or additional canvases. */
(() => {
  'use strict';
  const K = J.echo, A = K.icpcDiagram;
  const children = [[1, 2, 3], [4, 5], [6, 7], [8, 9], [10], [11], [], [12], [], [], [], [], []];
  const parent = Array(13).fill(-1), depth = Array(13).fill(0), size = Array(13).fill(1), heavy = Array(13).fill(-1);
  const head = Array(13), dfn = Array(13), linear = [], layout = Array(13);
  const firstDfs = u => {
    for (const v of children[u]) { parent[v] = u; depth[v] = depth[u] + 1; firstDfs(v); size[u] += size[v];
      if (heavy[u] < 0 || size[v] > size[heavy[u]]) heavy[u] = v; }
  };
  firstDfs(0);
  const split = (u, top) => {
    head[u] = top; dfn[u] = linear.length; linear.push(u);
    if (heavy[u] >= 0) split(heavy[u], top);
    for (const v of children[u]) if (v !== heavy[u]) split(v, v);
  };
  split(0, 0);
  let leaf = 0;
  const place = u => { layout[u] = children[u].length ? children[u].map(place).reduce((a, b) => a + b, 0) / children[u].length : leaf++; return layout[u]; };
  place(0);
  const ranges = [], HLD = []; let pathSum = 0, u = 11, v = 8;
  const save = (activeHead, status) => HLD.push({ activeHead, ranges: ranges.map(r => r.slice()), answer: pathSum, status });
  save(-1, 'QUERY PATH / 11 → 8');
  const take = (lo, hi, activeHead) => {
    ranges.push([lo, hi]); for (let i = lo; i <= hi; i++) pathSum += linear[i] + 1;
    save(activeHead, `CHAIN [${lo}, ${hi}] / PATH SUM ${pathSum}`);
  };
  while (head[u] !== head[v]) {
    if (depth[head[u]] < depth[head[v]]) [u, v] = [v, u];
    take(dfn[head[u]], dfn[u], head[u]); u = parent[head[u]];
  }
  if (depth[u] > depth[v]) [u, v] = [v, u];
  take(dfn[u], dfn[v], head[u]); save(-1, `PATH(11, 8) = ${pathSum} / O(log² n)`);
  K.registerBg('echoHeavyLight', 'Heavy-Light Decomposition / Chain Reactor', ['graphic', 'calm', 'glitch'], (env, P, ctx) => {
    const { s, phase, done } = A.at(env, HLD, 10), T = A.frame(env, ctx, 'HEAVY-LIGHT DECOMPOSITION', 'TREE PATH → A FEW CONTIGUOUS SEGMENT-TREE QUERIES', s.status), U = T.U;
    const rotation = 0.18 + Math.sin(env.t * 0.1) * 0.15;
    const pos = n => A.project(env, (layout[n] / (leaf - 1) - 0.5) * 3.5, 1.2 - depth[n] * 0.6, head[n] * 0.045,
      rotation, env.H > env.W ? 0.5 : 0.44, 0.53);
    const selected = n => s.ranges.some(([lo, hi]) => dfn[n] >= lo && dfn[n] <= hi);
    for (let d = 0; d < 4; d++) A.ring(env, ctx, T, 0.5 + d * 0.5, 1.2 - d * 0.6, rotation, 0.13);
    for (let n = 1; n < 13; n++) {
      const thick = heavy[parent[n]] === n, active = selected(n) && selected(parent[n]);
      A.edge(ctx, pos(parent[n]), pos(n), active ? T.b : thick ? K.rgba(T.a, 0.8) : T.faint,
        U * (active ? 0.004 : thick ? 0.003 : 0.0012), active ? K.wrap(phase + n * 0.1, 1) : thick ? K.wrap(env.t * 0.25 + n * 0.1, 1) : -1);
    }
    for (let n = 0; n < 13; n++) {
      const [x, y, z] = pos(n), active = head[n] === s.activeHead;
      A.node(ctx, x, y, U * 0.011 * z, selected(n) ? T.b : T.a, active);
      A.text(ctx, n, x, y - U * 0.026, U * 0.02, T.fg, 'center');
      A.text(ctx, `sz:${size[n]}`, x, y + U * 0.029, U * 0.012, T.dim, 'center');
    }
    const dx = env.W * 0.064, x0 = env.W * 0.09, y = env.H * 0.79;
    linear.forEach((n, i) => {
      const x = x0 + i * dx, active = selected(n);
      A.tile(ctx, x, y, dx * 0.39, U * 0.01, U * (active ? 0.04 : 0.015), active ? T.b : T.a, active);
      A.text(ctx, n, x, y - U * 0.022, U * 0.018, active ? T.b : T.fg, 'center');
      A.text(ctx, i, x, y + U * 0.044, U * 0.01, T.dim, 'center');
    });
    A.text(ctx, 'DFS ORDER / CONTIGUOUS HEAVY CHAINS', env.W * 0.055, env.H * 0.84, U * 0.012, T.dim);
    A.code(env, ctx, T, [
      'while (head[u] != head[v]) {',
      '  if (dep[head[u]] < dep[head[v]])',
      '    swap(u, v);',
      '  ans += query(dfn[head[u]],dfn[u]);',
      '  u = parent[head[u]];',
      '}',
      'if (dep[u] > dep[v]) swap(u,v);',
      'ans += query(dfn[u],dfn[v]);',
    ], done ? 7 : s.activeHead >= 0 ? 3 : 0);
  }, false);

  const words = ['TREE', 'TRIE', 'TRIAL', 'TRACE', 'TRACK', 'TRAP'];
  const trie = [{ letter: '∅', prefix: '', children: {}, count: 0, end: false }], PREFIX = [];
  const capture = (active, word, query, status) => PREFIX.push({ active, word, query, status,
    nodes: trie.map(n => ({ ...n, children: { ...n.children } })) });
  capture(0, '', false, 'INSERT / SIX WORDS');
  for (const word of words) {
    let current = 0; trie[0].count++;
    for (const letter of word) {
      if (trie[current].children[letter] == null) {
        trie[current].children[letter] = trie.length;
        trie.push({ letter, prefix: trie[current].prefix + letter, children: {}, count: 0, end: false });
      }
      current = trie[current].children[letter]; trie[current].count++;
      capture(current, word, false, `INSERT ${word} / PREFIX ${trie[current].prefix}`);
    }
    trie[current].end = true; capture(current, word, false, `WORD COMPLETE / ${word}`);
  }
  const prefix = 'TRA'; let current = 0;
  for (const letter of prefix) { current = trie[current].children[letter]; capture(current, prefix, true, `PREFIX LOOKUP / ${trie[current].prefix}`); }
  capture(current, prefix, true, `PREFIX TRA / ${trie[current].count} WORDS`);
  const terminalAngles = new Map(words.map((word, i) => [word, (i / words.length - 0.25) * K.TAU]));
  const crown = trie.map(node => {
    const leaves = words.filter(word => word.startsWith(node.prefix));
    const sx = leaves.reduce((a, word) => a + Math.cos(terminalAngles.get(word)), 0), sz = leaves.reduce((a, word) => a + Math.sin(terminalAngles.get(word)), 0);
    const angle = Math.atan2(sz, sx), radius = Math.max(0, node.prefix.length - 2) * 0.56;
    return [Math.cos(angle) * radius, 1.3 - node.prefix.length * 0.35, Math.sin(angle) * radius];
  });
  K.registerBg('echoTrieCrown', 'Trie / Prefix Crown', ['graphic', 'emotional', 'calm'], (env, P, ctx) => {
    const { s, phase, done } = A.at(env, PREFIX, 12), T = A.frame(env, ctx, 'TRIE / PREFIX CROWN', 'SHARED PREFIXES / BRANCH ON THE NEXT CHARACTER', s.status), U = T.U;
    const rotation = env.t * 0.055, pos = n => A.project(env, ...crown[n], rotation, env.H > env.W ? 0.5 : 0.44, 0.56);
    for (let d = 0; d < 6; d++) A.ring(env, ctx, T, Math.max(0.17, d - 2) * 0.56, 1.3 - d * 0.35, rotation, 0.13);
    for (let n = 0; n < s.nodes.length; n++) for (const child of Object.values(s.nodes[n].children)) {
      const active = s.nodes[s.active].prefix.startsWith(s.nodes[child].prefix);
      A.edge(ctx, pos(n), pos(child), active ? T.b : K.rgba(T.a, 0.3), U * (active ? 0.0035 : 0.0015), active ? K.wrap(phase + n * 0.13, 1) : -1);
    }
    const sorted = s.nodes.map((n, id) => ({ n, id, p: pos(id) })).sort((a, b) => a.p[2] - b.p[2]);
    for (const { n, id, p: [x, y, depth] } of sorted) {
      const active = id === s.active, w = U * 0.032 * depth;
      A.tile(ctx, x, y, w, w * 0.32, U * (n.end ? 0.035 : 0.015), active ? T.b : T.a, active);
      A.text(ctx, n.letter, x, y - U * 0.025, U * 0.034, active ? T.b : T.fg, 'center');
      A.text(ctx, `pass:${n.count}`, x, y + U * 0.041, U * 0.011, T.dim, 'center');
      if (active || n.end) A.node(ctx, x, y, U * 0.005, active ? T.b : T.a, active);
      if (n.end) A.text(ctx, n.prefix, x, y + U * 0.068, U * 0.013, T.a, 'center');
    }
    A.text(ctx, s.word || 'TR…', env.W * 0.055, env.H * (env.H > env.W ? 0.42 : 0.23), U * 0.055, T.b);
    A.code(env, ctx, T, s.query ? [
      'int prefix_count(string s) {',
      '  int u = 0;',
      '  for (char ch : s) {',
      "    int c = ch - 'A';",
      '    if (!next[u][c]) return 0;',
      '    u = next[u][c];',
      '  }',
      '  return pass[u];',
      '}',
    ] : [
      'int u = 0;',
      'for (char ch : word) {',
      "  int c = ch - 'A';",
      '  if (!next[u][c])',
      '    next[u][c] = new_node();',
      '  u = next[u][c];',
      '  ++pass[u];',
      '}',
      'terminal[u] = true;',
    ], s.query ? done ? 7 : 5 : s.nodes[s.active].end ? 8 : 6);
  }, false);
})();
