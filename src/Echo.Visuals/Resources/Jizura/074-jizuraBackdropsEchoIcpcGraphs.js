/* Actual shortest-path, spanning-tree and DAG traces on small fixed graphs.
   A few dozen snapshots, owned and released by the disposable JIZURA frame. */
(() => {
  'use strict';
  const K = J.echo, A = K.icpcDiagram;
  const POINTS = [[0, 0.6], [0.15, 0.1], [0.18, 0.9], [0.38, 0.4], [0.48, 0], [0.6, 0.85], [0.74, 0.3], [0.83, 0.95], [1, 0.5]];
  const EDGES = [[0, 1, 4], [0, 2, 2], [1, 3, 3], [1, 4, 5], [2, 3, 1], [2, 5, 6], [3, 4, 2], [3, 5, 4], [4, 6, 3], [5, 6, 2], [5, 7, 3], [6, 8, 4], [7, 8, 2]];
  const positions = (env, mode) => POINTS.map(([x, y], i) => {
    const angle = i / POINTS.length * K.TAU;
    return mode === 'mst' ? A.project(env, Math.cos(angle) * 1.7, Math.sin(angle) * 0.7, Math.sin(angle) * 1.1, env.t * 0.07) :
      A.project(env, (x - 0.5) * 3.5, (0.5 - y) * 1.6, Math.sin(i * 0.83) * 0.7, 0.2 + Math.sin(env.t * 0.12) * 0.25);
  });
  const drawGraph = (env, ctx, T, s, mode, phase) => {
    const p = positions(env, mode), U = T.U;
    A.ring(env, ctx, T, 2.05, -1.1, env.t * 0.07, 0.22);
    A.ring(env, ctx, T, 1.95, -1.1, env.t * 0.07, 0.1);
    EDGES.forEach(([u, v, w], i) => {
      const picked = mode === 'mst' ? s.chosen.includes(i) : s.parent[v] === u || s.parent[u] === v;
      const active = s.edge === i, color = active ? T.b : picked ? K.rgba(T.a, 0.82) : T.faint;
      const from = s.u === v ? v : u, to = from === u ? v : u;
      A.edge(ctx, p[from], p[to], color, Math.max(1, U * (picked || active ? 0.003 : 0.0012)), active ? phase : picked ? K.wrap(env.t * 0.35 + i * 0.17, 1) : -1);
      A.text(ctx, w, (p[u][0] + p[v][0]) / 2, (p[u][1] + p[v][1]) / 2 - U * 0.015, U * 0.017, active ? T.b : T.dim);
    });
    p.forEach(([x, y, depth], i) => {
      const active = s.u === i, settled = mode === 'mst' ? s.chosen.some(e => EDGES[e][0] === i || EDGES[e][1] === i) : s.visited[i];
      const rad = U * 0.013 * depth, color = active ? T.b : settled ? T.a : T.dim;
      A.node(ctx, x, y, rad, color, active);
      const wave = K.wrap(env.t * 0.4 + i / 9, 1);
      ctx.strokeStyle = K.rgba(settled ? T.a : T.b, (1 - wave) * (settled ? 0.35 : 0.09)); ctx.lineWidth = 1;
      ctx.beginPath(); ctx.arc(x, y, rad * (2 + wave * 4), 0, K.TAU); ctx.stroke();
      A.text(ctx, String.fromCharCode(65 + i), x, y + rad * 2.8, U * 0.019, T.fg, 'center');
      if (mode !== 'mst') A.text(ctx, Number.isFinite(s.dist[i]) ? s.dist[i] : '∞', x, y - rad * 2.8, U * 0.028, settled ? T.a : T.dim, 'center');
    });
  };
  const DIJKSTRA = [], dist = Array(9).fill(Infinity), visited = Array(9).fill(false), parent = Array(9).fill(-1);
  dist[0] = 0;
  const record = (u, edge, message) => DIJKSTRA.push({ u, edge, message, dist: dist.slice(), visited: visited.slice(), parent: parent.slice() });
  record(0, -1, 'SOURCE A / d[A] = 0');
  for (let step = 0; step < 9; step++) {
    let u = -1;
    for (let v = 0; v < 9; v++) if (!visited[v] && (u < 0 || dist[v] < dist[u])) u = v;
    if (u < 0 || !Number.isFinite(dist[u])) break;
    visited[u] = true; record(u, -1, `SETTLE ${String.fromCharCode(65 + u)} / MIN DISTANCE ${dist[u]}`);
    EDGES.forEach(([a, b, w], edge) => {
      const v = a === u ? b : b === u ? a : -1;
      if (v < 0 || visited[v] || dist[u] + w >= dist[v]) return;
      dist[v] = dist[u] + w; parent[v] = u;
      record(u, edge, `RELAX ${String.fromCharCode(65 + u)} → ${String.fromCharCode(65 + v)} / ${dist[v]}`);
    });
  }
  record(8, -1, `SHORTEST A → I = ${dist[8]}`);
  K.registerBg('echoDijkstraRoutes', 'Dijkstra / Shortest Paths', ['graphic', 'calm', 'glitch'], (env, P, ctx) => {
    const { s, phase } = A.at(env, DIJKSTRA), T = A.frame(env, ctx, 'DIJKSTRA', 'SETTLE THE NEAREST / RELAX EVERY EDGE', s.message);
    drawGraph(env, ctx, T, s, 'path', phase);
    A.code(env, ctx, T, [
      'd[source] = 0;',
      'for (int k = 0; k < n; ++k) {',
      '  int u = -1;',
      '  for (int v = 0; v < n; ++v)',
      '    if (!used[v] &&',
      '        (u < 0 || d[v] < d[u]))',
      '      u = v;',
      '  used[u] = true;',
      '  for (auto [v,w] : adj[u])',
      '    if (!used[v] && d[u]+w < d[v])',
      '      d[v] = d[u] + w;',
      '}',
    ], s.edge >= 0 ? 10 : 7);
  }, false);

  const MST = [], roots = Array.from({ length: 9 }, (_, i) => i), chosen = [];
  const root = v => { while (roots[v] !== v) v = roots[v]; return v; };
  let cost = 0;
  EDGES.map((edge, i) => ({ edge, i })).sort((a, b) => a.edge[2] - b.edge[2] || a.i - b.i).forEach(({ edge: [u, v, w], i }) => {
    const a = root(u), b = root(v), accepted = a !== b;
    if (accepted) { roots[a] = b; chosen.push(i); cost += w; }
    MST.push({ u, edge: i, chosen: chosen.slice(), cost, accepted });
  });
  MST.push({ u: -1, edge: -1, chosen: chosen.slice(), cost, accepted: true });
  K.registerBg('echoKruskalForest', 'Kruskal / Minimum Spanning Tree', ['graphic', 'emotional', 'calm'], (env, P, ctx) => {
    const { s, phase, done } = A.at(env, MST), T = A.frame(env, ctx, 'KRUSKAL', 'SORT THE EDGES / MERGE DISJOINT SETS', done ? `MST COMPLETE / WEIGHT ${s.cost}` : s.accepted ? `UNION / WEIGHT ${s.cost}` : 'CYCLE DETECTED / SKIP EDGE');
    drawGraph(env, ctx, T, s, 'mst', phase);
    A.text(ctx, `${s.chosen.length} / 8 EDGES`, env.W * 0.945, env.H * 0.8, T.U * 0.027, T.a, 'right');
    const y = env.H * 0.83;
    EDGES.map((e, i) => ({ w: e[2], i })).sort((a, b) => a.w - b.w || a.i - b.i).forEach((e, i) => {
      const x = env.W * (0.08 + i * 0.065);
      A.text(ctx, e.w, x, y, T.U * 0.014, s.chosen.includes(e.i) ? T.a : s.edge === e.i ? T.b : T.dim);
    });
    A.code(env, ctx, T, [
      '// edges stored as (weight,u,v)',
      'sort(edges.begin(), edges.end());',
      'for (auto [w,u,v] : edges) {',
      '  int a = find(u), b = find(v);',
      '  if (a == b) continue;',
      '  parent[a] = b;',
      '  total += w;',
      '  tree.push_back({u,v});',
      '}',
    ], done ? 7 : s.accepted ? 5 : 4);
  }, false);

  const DAG = [[0, 2], [0, 3], [1, 3], [1, 4], [2, 5], [3, 5], [3, 6], [4, 6], [5, 7], [6, 7]];
  const degree = Array(8).fill(0), order = [], TOPO = [];
  DAG.forEach(([, v]) => degree[v]++);
  const queue = degree.map((d, i) => d === 0 ? i : -1).filter(i => i >= 0);
  TOPO.push({ u: -1, degree: degree.slice(), order: [], queue: queue.slice() });
  while (queue.length) {
    const u = queue.shift(); order.push(u);
    for (const [a, v] of DAG) if (a === u && --degree[v] === 0) queue.push(v);
    TOPO.push({ u, degree: degree.slice(), order: order.slice(), queue: queue.slice() });
  }
  K.registerBg('echoTopoPipeline', 'Topological Sort / Dependency Pipeline', ['graphic', 'calm', 'editorial'], (env, P, ctx) => {
    const { s, phase, done } = A.at(env, TOPO), T = A.frame(env, ctx, 'TOPOLOGICAL SORT', 'ZERO IN-DEGREE / RELEASE THE NEXT DEPENDENCY', done ? 'DAG RESOLVED / O(V + E)' : `READY QUEUE / ${s.queue.map(i => String.fromCharCode(65 + i)).join(' ') || 'EMPTY'}`);
    const levels = [[0, 1], [2, 3, 4], [5, 6], [7]], p = Array(8), rotation = env.t * 0.09;
    levels.forEach((nodes, level) => {
      const y = 1.25 - level * 0.7, radius = 0.8 + Math.sin(level / 3 * Math.PI) * 0.65;
      A.ring(env, ctx, T, radius, y, rotation, level <= s.order.length / 2 ? 0.5 : 0.16);
      nodes.forEach((v, i) => { const angle = i / nodes.length * K.TAU + level * 0.65; p[v] = A.project(env, Math.cos(angle) * radius, y, Math.sin(angle) * radius, rotation); });
    });
    DAG.forEach(([u, v], i) => A.edge(ctx, p[u], p[v], u === s.u ? T.b : s.order.includes(u) ? K.rgba(T.a, 0.7) : T.faint, Math.max(1, T.U * 0.0025), u === s.u ? phase : s.order.includes(u) ? K.wrap(env.t * 0.3 + i * 0.15, 1) : -1, true));
    p.forEach(([x, y], i) => {
      A.node(ctx, x, y, T.U * 0.012, s.order.includes(i) ? T.a : s.queue.includes(i) ? T.b : T.dim, i === s.u);
      A.text(ctx, String.fromCharCode(65 + i), x, y - T.U * 0.031, T.U * 0.025, T.fg, 'center');
      A.text(ctx, `in:${s.degree[i]}`, x, y + T.U * 0.035, T.U * 0.015, T.dim, 'center');
    });
    A.text(ctx, s.order.map(i => String.fromCharCode(65 + i)).join(' → '), env.W * 0.945, env.H * 0.83, T.U * 0.021, T.a, 'right');
    A.code(env, ctx, T, [
      'for (int u = 0; u < n; ++u)',
      '  if (in[u] == 0) q.push(u);',
      'while (!q.empty()) {',
      '  int u = q.front(); q.pop();',
      '  order.push_back(u);',
      '  for (int v : adj[u])',
      '    if (--in[v] == 0)',
      '      q.push(v);',
      '}',
    ], done ? 8 : 6);
  }, false);
})();
