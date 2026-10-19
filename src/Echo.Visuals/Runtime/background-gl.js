// Fullscreen source-renderer adapter. No browser/GL context, audio device, network or filesystem.
let __glNext=0;
function __nativeGL(){
 let current=null,unit=1000;const bindings={};
 const gl={VERTEX_SHADER:1,FRAGMENT_SHADER:2,COMPILE_STATUS:3,LINK_STATUS:4,TEXTURE0:1000,TEXTURE1:1001,TEXTURE_2D:9,
 createShader:type=>({type,source:''}),shaderSource:(s,text)=>s.source=text,compileShader:()=>{},getShaderParameter:()=>true,deleteShader:()=>{},
 createProgram:()=>({id:++__glNext,uniforms:{},samplers:{}}),attachShader:(p,s)=>{if(s.type===2)p.source=s.source;},
 linkProgram:p=>__background('compile',p.id,p.source),getProgramParameter:()=>true,deleteProgram:p=>{if(p)__background('deleteProgram',p.id)},
 useProgram:p=>{current=p},getUniformLocation:(p,name)=>{name=name.replace(/\[0\]$/,'');return __background('uniform',p.id,name)?{p,name}:null;},
 uniform1f:(u,a)=>{if(u)u.p.uniforms[u.name]=[a]},uniform2f:(u,...v)=>{if(u)u.p.uniforms[u.name]=v},uniform4f:(u,...v)=>{if(u)u.p.uniforms[u.name]=v},
 uniform3fv:(u,v)=>{if(u)u.p.uniforms[u.name]=Array.from(v)},uniform1i:(u,value)=>{if(u)u.p.samplers[u.name]=1000+value},
 viewport:(x,y,w,h)=>{gl.w=w;gl.h=h},
 drawArrays:()=>{const textures={};for(const [name,slot]of Object.entries(current.samplers))if(bindings[slot])textures[name]=bindings[slot].id;
 __background('draw',current.id,gl.w,gl.h,JSON.stringify(current.uniforms),JSON.stringify(textures));},
 createTexture:()=>({id:++__glNext}),bindTexture:(target,texture)=>{bindings[unit]=texture},activeTexture:value=>{unit=value},
 texImage2D:(...args)=>{const image=args[args.length-1];if(image?.nativeId)__background('upload',bindings[unit].id,image.nativeId);},
 deleteTexture:t=>{if(t)__background('deleteTexture',t.id)},
 createVertexArray:()=>({}),createBuffer:()=>({}),getAttribLocation:()=>0,isContextLost:()=>false,getExtension:()=>null};
 for(const op of ['bindVertexArray','deleteVertexArray','bindBuffer','bufferData','deleteBuffer','enableVertexAttribArray','vertexAttribPointer','disable','clearColor','clear','flush','pixelStorei','texParameteri','generateMipmap'])gl[op]=()=>{};
 return gl;
}
const __canvasGetContext=NativeCanvas.prototype.getContext;
NativeCanvas.prototype.getContext=function(type){if(type==='webgl'||type==='webgl2')return this.gl||(this.gl=__nativeGL());return __canvasGetContext.call(this);};
NativeCanvas.prototype.close=NativeCanvas.prototype.dispose;
function __backgroundBuild(json,w,h){return JSON.stringify(SourceBackground.build(JSON.parse(json),w,h));}
let __spectrumJson=null,__spectrumValue=[];
function __backgroundDraw(ms,dt,spectrum,playing,telemetryTimestamp){__now=ms;globalThis.__now=ms;__frameEpoch++;__canvas(0,'beginFrame');if(spectrum!==__spectrumJson){__spectrumJson=spectrum;__spectrumValue=JSON.parse(spectrum);}SourceBackground.draw(ms,dt,__spectrumValue,playing,telemetryTimestamp);}
function __backgroundDispose(){SourceBackground.dispose();}
function __backgroundCover(id,w,h){const c=new NativeCanvas(id,w,h);try{SourceBackground.cover(c);}finally{c.dispose();}}
function __backgroundPointer(x,y,press){SourceBackground.pointer(x,y,press);}
function __backgroundCanvas(){return SourceBackground.canvasId();}
function __backgroundAsset(url,w,h){const id=__backgroundImage(String(url),w,h);const c=new NativeCanvas(id,w,h);c.naturalWidth=w;c.naturalHeight=h;return c;}

function __backgroundPointerLeave(){SourceBackground.pointerLeave();}

function __backgroundUpdate(json,w,h){SourceBackground.update(JSON.parse(json),w,h);}
function __backgroundClearCover(){SourceBackground.clearCover();}

function __backgroundFrameRate(fps){SourceBackground.frameRate(fps);}
