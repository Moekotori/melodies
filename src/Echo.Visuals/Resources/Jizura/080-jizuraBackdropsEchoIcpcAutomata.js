/* Original Aho–Corasick and exact modular-transform artwork.
   Ten automaton states and eight transform samples. BigInt work happens only at load time. */
(() => {
  'use strict';
  const K=J.echo,A=K.icpcDiagram,programs=K.icpcPrograms;
  const patterns=['HE','SHE','HIS','HERS'],nodes=[{prefix:'',children:{},own:[]}];
  patterns.forEach((word,id)=>{let u=0;for(const ch of word){if(nodes[u].children[ch]==null){nodes[u].children[ch]=nodes.length;nodes.push({prefix:nodes[u].prefix+ch,children:{},own:[]});}u=nodes[u].children[ch];}nodes[u].own.push(id);});
  const fail=Array(nodes.length).fill(0),out=nodes.map(n=>n.own.slice()),go=nodes.map(()=>Array(26).fill(0)),AC=[];
  const snap=(u,v,code,status,cursor=-1,matches=0)=>AC.push({u,v,code,status,cursor,matches,fail:fail.slice(),counts:out.map(a=>a.length)});
  const queue=[];for(const [ch,v] of Object.entries(nodes[0].children)){go[0][ch.charCodeAt(0)-65]=v;queue.push(v);}
  snap(0,-1,'link','BUILD / FAILURE LINKS');
  for(let index=0;index<queue.length;index++){
    const u=queue[index];out[u]=[...out[u],...out[fail[u]]];snap(u,fail[u],'inherit',`INHERIT OUTPUT / STATE ${u} / ${out[u].length} PATTERNS`);
    for(let c=0;c<26;c++){
      const ch=String.fromCharCode(65+c),v=nodes[u].children[ch];
      if(v!=null){go[u][c]=v;fail[v]=go[fail[u]][c];queue.push(v);snap(v,fail[v],'link',`FAIL[${v}] = ${fail[v]}`);}
      else go[u][c]=go[fail[u]][c];
    }
  }
  const text='USHERS';let state=0,matches=0;
  for(let i=0;i<text.length;i++){
    const ch=text[i];
    while(state&&nodes[state].children[ch]==null){const previous=state;state=fail[state];snap(previous,state,'scan',`FAILURE TRANSITION / ${previous} → ${state}`,i,matches);}
    state=nodes[state].children[ch]??0;snap(state,-1,'scan',`SCAN ${ch} / STATE ${state}`,i,matches);
    matches+=out[state].length;snap(state,-1,'match',out[state].length?`MATCH ${out[state].map(id=>patterns[id]).join(' + ')} / TOTAL ${matches}`:`NO OUTPUT / TOTAL ${matches}`,i,matches);
  }
  snap(state,-1,'done',`USHERS / ${matches} MATCHES / HE + SHE + HERS`,text.length-1,matches);
  const angles=patterns.map((_,i)=>(i/4-0.25)*K.TAU);
  const coords=nodes.map(n=>{
    const leaves=patterns.map((word,id)=>word.startsWith(n.prefix)?id:-1).filter(id=>id>=0),angle=Math.atan2(leaves.reduce((a,i)=>a+Math.sin(angles[i]),0),leaves.reduce((a,i)=>a+Math.cos(angles[i]),0));
    const radius=n.prefix.length*0.38;return [Math.cos(angle)*radius,0.95-n.prefix.length*0.4,Math.sin(angle)*radius];
  });
  K.registerBg('echoAhoCorasick','Aho–Corasick / Failure-Link Observatory',['graphic','glitch','calm'],(env,P,ctx)=>{
    const {s,phase}=A.at(env,AC,14),T=A.frame(env,ctx,'AHO–CORASICK AUTOMATON','MULTIPLE PATTERNS / FAILURE LINKS / SHARED OUTPUTS',s.status),U=T.U;
    const p=coords.map(([x,y,z])=>A.project(env,x,y,z,env.t*0.055,env.H>env.W?0.5:0.43,0.53));
    for(let d=0;d<5;d++)A.ring(env,ctx,T,Math.max(0.15,d*0.38),0.95-d*0.4,env.t*0.055,0.15);
    nodes.forEach((node,u)=>Object.values(node.children).forEach(v=>A.edge(ctx,p[u],p[v],v===s.u?T.b:K.rgba(T.a,0.5),U*0.0025,v===s.u?phase:-1,true)));
    ctx.setLineDash([U*0.006,U*0.004]);
    for(let u=1;u<nodes.length;u++){
      const v=s.fail[u],active=s.u===u&&s.v===v;
      const a=p[u],b=p[v];ctx.strokeStyle=active?T.b:K.rgba(T.b,0.2);ctx.lineWidth=U*(active?0.003:0.0012);
      ctx.beginPath();ctx.moveTo(a[0],a[1]);ctx.bezierCurveTo(a[0]+U*0.11,a[1]-U*0.08,b[0]+U*0.11,b[1]+U*0.08,b[0],b[1]);ctx.stroke();
    }ctx.setLineDash([]);
    p.forEach(([x,y,d],i)=>{const active=i===s.u;A.node(ctx,x,y,U*0.01*d,active?T.b:T.a,active);
      A.text(ctx,nodes[i].prefix||'ROOT',x,y-U*0.027,U*0.017,T.fg,'center');A.text(ctx,`out:${s.counts[i]}`,x,y+U*0.028,U*0.012,T.dim,'center');});
    const dx=env.W*0.075,x0=env.W*0.1,y=env.H*0.8;
    for(let i=0;i<text.length;i++){const x=x0+i*dx,active=i===s.cursor;A.tile(ctx,x,y,dx*0.4,U*0.012,U*0.025,active?T.b:T.a,active);A.text(ctx,text[i],x,y-U*0.027,U*0.036,active?T.b:T.fg,'center');}
    A.text(ctx,`OUTPUT ${s.matches}`,env.W*0.055,env.H*(env.H>env.W?0.45:0.23),U*0.048,T.b);
    const code=programs.ac;A.code(env,ctx,T,code.lines,code.at[s.code]);
  },false);

  const MOD=998244353n,ROOT=3n;
  const power=(a,k)=>{let result=1n;for(;k;k>>=1n,a=a*a%MOD)if(k&1n)result=result*a%MOD;return result;};
  const NTT=[];
  const transform=(input,inverse,record)=>{
    const a=input.slice(),columns=[];
    const save=(stage,u,v,code,factor=1n)=>{if(record)NTT.push({inverse,stage,u,v,code,factor,data:a.slice(),status:inverse?'INVERSE NTT':'FORWARD NTT'});};
    for(let i=1,j=0;i<8;i++){let bit=4;for(;j&bit;bit>>=1)j^=bit;j^=bit;if(i<j){[a[i],a[j]]=[a[j],a[i]];save(0,i,j,'reverse');}}
    columns.push(a.slice());
    for(let length=2,stage=1;length<=8;length*=2,stage++){
      let root=power(ROOT,(MOD-1n)/BigInt(length));if(inverse)root=power(root,MOD-2n);
      for(let i=0;i<8;i+=length){let w=1n;for(let j=0;j<length/2;j++){
        const u=i+j,v=u+length/2,x=a[u],y=a[v]*w%MOD;a[u]=(x+y)%MOD;a[v]=(x-y+MOD)%MOD;
        save(stage,u,v,'butterfly',w);w=w*root%MOD;
      }}columns.push(a.slice());
    }
    if(inverse){const norm=power(8n,MOD-2n);for(let i=0;i<8;i++){a[i]=a[i]*norm%MOD;save(3,i,-1,'normalize',norm);}}
    return {data:a,columns};
  };
  const forward=transform([1n,2n,3n,4n,0n,0n,0n,0n],false,true),other=transform([2n,1n,0n,0n,0n,0n,0n,0n],false,false);
  const product=forward.data.slice();for(let i=0;i<8;i++){product[i]=product[i]*other.data[i]%MOD;NTT.push({inverse:false,stage:3,u:i,v:-1,code:'product',factor:other.data[i],data:product.slice(),status:'POINTWISE MULTIPLICATION'});}
  const inverse=transform(product,true,true),result=inverse.data.slice(0,5).map(String);
  NTT.push({inverse:true,stage:3,u:-1,v:-1,code:'done',factor:1n,data:inverse.data.slice(),status:`CONVOLUTION = ${result.join(', ')}`});
  K.registerBg('echoNttConvolution','NTT / Modular Butterfly Accelerator',['graphic','glitch','emotional'],(env,P,ctx)=>{
    const {s,phase}=A.at(env,NTT,15),T=A.frame(env,ctx,'NTT / MODULAR CONVOLUTION','EXACT INTEGER BUTTERFLIES / FORWARD × FORWARD → INVERSE',s.status),U=T.U;
    const columns=s.inverse?inverse.columns:forward.columns,rotation=0.14+Math.sin(env.t*0.08)*0.1;
    const pos=(col,row)=>A.project(env,(col-1.5)*1.12,(3.5-row)*0.29,Math.sin(row*0.8+col)*0.12,rotation,env.H>env.W?0.5:0.4,0.55,0.86);
    for(let col=0;col<3;col++)for(let row=0;row<8;row++)for(const cross of [false,true]){
      const to=cross?row^(1<<col):row,active=col+1===s.stage&&(row===s.u||row===s.v),ready=col<s.stage;
      A.edge(ctx,pos(col,row),pos(col+1,to),active?T.b:ready?K.rgba(T.a,cross?0.5:0.3):T.faint,U*(active?0.0028:0.0012),active?phase:ready?K.wrap(env.t*0.18+row*0.11+col*0.2,1):-1);
    }
    for(let col=0;col<4;col++)for(let row=0;row<8;row++){
      const [x,y,d]=pos(col,row),ready=col<=s.stage,active=col===s.stage&&(row===s.u||row===s.v);
      A.node(ctx,x,y,U*0.0055*d,active?T.b:ready?T.a:T.dim,active);
      if(ready){const value=col===s.stage?s.data[row]:columns[col][row];A.text(ctx,String(value),x,y-U*0.018,U*0.013,active?T.b:T.dim,'center');}
    }
    A.text(ctx,'mod 998244353 / root 3',env.W*0.055,env.H*(env.H>env.W?0.45:0.23),U*0.017,T.a);
    A.text(ctx,`w = ${s.factor}`,env.W*0.055,env.H*0.83,U*0.014,T.dim);
    const code=programs.ntt;A.code(env,ctx,T,code.lines,code.at[s.code]);
  },false);
})();
