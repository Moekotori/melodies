/* Original SCC and blocking-flow studies. Ten and six vertices respectively.
   Real fixed traces are computed once; all snapshots belong to the disposable frame. */
(() => {
  'use strict';
  const K = J.echo, A = K.icpcDiagram, programs = K.icpcPrograms;
  const directed = [[0,1],[1,2],[2,0],[2,3],[3,4],[4,5],[5,3],[5,6],[6,7],[7,6],[7,8],[8,9],[9,8],[1,4],[4,7],[0,9]];
  const adj = Array.from({length:10},()=>[]); directed.forEach(([u,v])=>adj[u].push(v));
  const dfn=Array(10).fill(0),low=Array(10).fill(0),comp=Array(10).fill(-1),onStack=Array(10).fill(false),stack=[],SCC=[];
  let timer=0,components=0;
  const snap=(u,v,code,status)=>SCC.push({u,v,code,status,dfn:dfn.slice(),low:low.slice(),comp:comp.slice(),stack:stack.slice()});
  const dfs=u=>{
    dfn[u]=low[u]=++timer;stack.push(u);onStack[u]=true;snap(u,-1,'discover',`DFS ${u} / dfn = low = ${timer}`);
    for(const v of adj[u]){
      if(!dfn[v]){snap(u,v,'descend',`TREE EDGE / ${u} → ${v}`);dfs(v);low[u]=Math.min(low[u],low[v]);snap(u,v,'return',`RETURN / low[${u}] = ${low[u]}`);}
      else if(onStack[v]){low[u]=Math.min(low[u],dfn[v]);snap(u,v,'back',`BACK EDGE / low[${u}] = ${low[u]}`);}
    }
    if(dfn[u]!==low[u])return;
    let v;do{v=stack.pop();onStack[v]=false;comp[v]=components;snap(u,v,'pop',`POP ${v} / SCC ${components}`);}while(v!==u);
    components++;snap(u,-1,'component',`COMPONENT CLOSED / ${components} SCCs`);
  };
  for(let u=0;u<10;u++)if(!dfn[u])dfs(u);
  snap(-1,-1,'done',`TARJAN COMPLETE / ${components} SCCs / O(V + E)`);
  const groups=[[0,1,2],[3,4,5],[6,7],[8,9]];
  K.registerBg('echoTarjanScc','Tarjan / Strongly Connected Orbits',['graphic','glitch','calm'],(env,P,ctx)=>{
    const {s,phase}=A.at(env,SCC,13),T=A.frame(env,ctx,'TARJAN / STRONGLY CONNECTED','DFS STACK / LOW-LINK VALUES / CONDENSATION DAG',s.status),U=T.U;
    const p=Array(10),rotation=0.18+Math.sin(env.t*0.09)*0.15;
    groups.forEach((nodes,g)=>nodes.forEach((n,i)=>{
      const angle=i/nodes.length*K.TAU+env.t*0.08;
      p[n]=A.project(env,(g-1.5)*1.06+Math.cos(angle)*0.4,Math.sin(angle)*0.45+Math.sin(g)*0.22,
        Math.sin(angle)*0.28+(g%2-0.5)*0.55,rotation,env.H>env.W?0.5:0.4,0.55,env.H>env.W?0.9:0.68);
    }));
    directed.forEach(([u,v],i)=>{
      const active=s.u===u&&s.v===v,closed=s.comp[u]>=0&&s.comp[u]===s.comp[v];
      A.edge(ctx,p[u],p[v],active?T.b:closed?K.mix(T.a,T.b,s.comp[u]/3):T.faint,U*(active?0.0035:closed?0.0025:0.0012),
        active?phase:closed?K.wrap(env.t*0.2+i*0.11,1):-1,true);
    });
    for(let n=0;n<10;n++){
      const [x,y,depth]=p[n],active=n===s.u||n===s.v,color=s.comp[n]>=0?K.mix(T.a,T.b,s.comp[n]/3):s.stack.includes(n)?T.b:T.dim;
      A.node(ctx,x,y,U*0.011*depth,color,active);
      A.text(ctx,n,x,y-U*0.03,U*0.028,T.fg,'center');
      A.text(ctx,`${s.dfn[n]} / ${s.low[n]}`,x,y+U*0.032,U*0.014,color,'center');
    }
    groups.forEach(nodes=>{const id=s.comp[nodes[0]];if(id<0)return;const [x,y]=p[nodes[0]];
      A.text(ctx,`SCC ${id}`,x,y+U*0.072,U*0.013,K.mix(T.a,T.b,id/3),'center');});
    A.text(ctx,'DFS STACK',env.W*0.055,env.H*0.77,U*0.013,T.dim);
    s.stack.forEach((n,i)=>{const x=env.W*(0.075+i*0.055),y=env.H*0.825;
      A.tile(ctx,x,y,U*0.022,U*0.01,U*0.023,i===s.stack.length-1?T.b:T.a,i===s.stack.length-1);
      A.text(ctx,n,x,y-U*0.022,U*0.02,T.fg,'center');});
    const code=programs.tarjan;A.code(env,ctx,T,code.lines,code.at[s.code]);
  },false);

  const network=[[0,1,10],[0,2,8],[1,2,2],[1,3,7],[2,4,10],[3,4,3],[3,5,6],[4,5,10]];
  const edges=[],links=Array.from({length:6},()=>[]),level=Array(6).fill(-1),cur=Array(6).fill(0),FLOW=[];
  network.forEach(([u,v,c])=>{links[u].push(edges.length);edges.push({u,v,cap:c});links[v].push(edges.length);edges.push({u:v,v:u,cap:0});});
  let flow=0;
  const record=(edge,code,status)=>FLOW.push({edge,code,status,flow,caps:edges.map(e=>e.cap),level:level.slice()});
  const bfs=()=>{
    level.fill(-1);level[0]=0;const queue=[0];record(-1,'layer','BUILD LEVEL GRAPH / SOURCE S');
    for(let i=0;i<queue.length;i++)for(const id of links[queue[i]]){const e=edges[id];
      if(e.cap>0&&level[e.v]<0){level[e.v]=level[e.u]+1;queue.push(e.v);record(id,'bfs',`BFS / level[${e.v}] = ${level[e.v]}`);}}
    return level[5]>=0;
  };
  const send=(u,pushed)=>{
    if(u===5)return pushed;
    for(;cur[u]<links[u].length;cur[u]++){
      const id=links[u][cur[u]],e=edges[id];if(!e.cap||level[e.v]!==level[u]+1)continue;
      record(id,'scan',`CURRENT ARC / ${e.u} → ${e.v}`);
      const delta=send(e.v,Math.min(pushed,e.cap));if(!delta)continue;
      e.cap-=delta;edges[id^1].cap+=delta;record(id,'push',`RESIDUAL UPDATE / PUSH ${delta}`);return delta;
    }return 0;
  };
  while(bfs()){
    cur.fill(0);let delta;while((delta=send(0,1e9))>0){flow+=delta;record(-1,'augment',`BLOCKING FLOW / TOTAL ${flow}`);}
  }
  record(-1,'done',`MAX FLOW = ${flow} / SINK UNREACHABLE`);
  const coords=[[-1.75,0,0],[-0.8,0.7,0.3],[-0.8,-0.7,-0.3],[0.65,0.7,0.3],[0.65,-0.7,-0.3],[1.7,0,0]];
  const names=['S','A','B','C','D','T'];
  K.registerBg('echoDinicFlow','Dinic / Residual Reactor',['graphic','glitch','emotional'],(env,P,ctx)=>{
    const {s,phase}=A.at(env,FLOW,14),T=A.frame(env,ctx,'DINIC / RESIDUAL REACTOR','LEVEL GRAPH / CURRENT ARCS / BLOCKING FLOW',s.status),U=T.U;
    const p=coords.map(([x,y,z])=>A.project(env,x,y,z,0.15+Math.sin(env.t*0.08)*0.12,env.H>env.W?0.5:0.4,0.57,env.H>env.W?0.9:0.74));
    network.forEach(([u,v,capacity],i)=>{
      const id=i*2,active=s.edge===id,carried=capacity-s.caps[id],a=p[u],b=p[v],dx=b[0]-a[0],dy=b[1]-a[1],len=Math.hypot(dx,dy)||1;
      A.edge(ctx,a,b,active?T.b:carried?T.a:T.faint,U*(0.002+capacity*0.00022),active?phase:carried?K.wrap(env.t*0.25+i*0.12,1):-1,true);
      if(carried){ctx.setLineDash([U*0.008,U*0.005]);const shift=U*0.015;
        A.edge(ctx,[b[0]-dy/len*shift,b[1]+dx/len*shift],[a[0]-dy/len*shift,a[1]+dx/len*shift],K.rgba(T.b,0.35),U*0.0015,s.edge===(id^1)?phase:-1,true);ctx.setLineDash([]);}
      A.text(ctx,`${carried}/${capacity}`,(a[0]+b[0])/2,(a[1]+b[1])/2-U*0.024,U*0.016,active?T.b:T.dim,'center');
    });
    p.forEach(([x,y,d],i)=>{const active=s.edge>=0&&(edges[s.edge].u===i||edges[s.edge].v===i);
      A.tile(ctx,x,y,U*0.048*d,U*0.018,U*0.04,active?T.b:T.a,active);
      A.text(ctx,names[i],x,y-U*0.039,U*0.04,T.fg,'center');
      A.text(ctx,`L${s.level[i]}`,x,y+U*0.06,U*0.016,s.level[i]>=0?T.a:T.dim,'center');
      if(active)A.node(ctx,x,y,U*0.009,T.b,true);});
    A.text(ctx,`FLOW ${s.flow}`,env.W*0.055,env.H*(env.H>env.W?0.45:0.22),U*0.06,T.b);
    const code=programs.dinic;A.code(env,ctx,T,code.lines,code.at[s.code]);
  },false);
})();
