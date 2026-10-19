// Host surface for the original first-party scene engines. It has no audio, I/O, storage or browser.
let __now=0;globalThis.__now=0;let __seek=-1;
performance.now=()=>__now;
window.devicePixelRatio=1;
window.requestIdleCallback=()=>0;window.cancelIdleCallback=()=>{};
window.setTimeout=()=>0;window.clearTimeout=()=>{};
document.fonts.addEventListener=()=>{};document.fonts.removeEventListener=()=>{};document.fonts.load=()=>Promise.resolve([]);
const getComputedStyle=()=>({getPropertyValue:name=>name==='--color-accent'?__accent:''});
NativeCanvas.prototype.getBoundingClientRect=function(){return {left:0,top:0,width:this.width,height:this.height};};
for(const dimension of ['width','height'])Object.defineProperty(NativeCanvas.prototype,dimension,{
 get(){return this['_'+dimension];},
 set(value){
  this['_'+dimension]=Math.max(0,value|0);
  if(!this._width||!this._height||this.__consumed&&value<=1){this.dispose();return;}
  if(!this.nativeId)this.nativeId=__canvas(0,'new');
  __canvas(this.nativeId,'resize',this._width,this._height);
  if(this.ctx){const reset=new NativeContext(this);Object.assign(this.ctx,reset);this.ctx.__lastStyle=null;}
 }
});
NativeContext.prototype.measureText=function(text){return JSON.parse(this.call('measureMetrics',String(text)));};
class Path2D {
 constructor(source){this.ops=source?source.ops.slice():[];}
}
for(const name of ['moveTo','lineTo','bezierCurveTo','quadraticCurveTo','rect','roundRect','arc','arcTo','ellipse','closePath'])
 Path2D.prototype[name]=function(...args){this.ops.push([name,args]);};
for(const name of ['fill','stroke','clip'])NativeContext.prototype[name]=function(path){
 if(path instanceof Path2D){this.beginPath();for(const [name,args]of path.ops)this[name](...args);}
 return this.call(name);
};
function __base64(value){
 const table='ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';const output=[];
 let bits=0,available=0;
 for(const ch of value){const v=table.indexOf(ch);if(v<0)continue;bits=bits*64+v;available+=6;if(available>=8){available-=8;output.push((bits>>available)&255);bits&=(1<<available)-1;}}
 return output;
}
function __sourceBuild(json,width,height){return JSON.stringify(SourceLyrics.build(JSON.parse(json),width,height));}
function __sourceDraw(ms,motion,now,elapsed,playing){__now=now;globalThis.__now=now;__frameEpoch++;__canvas(0,'beginFrame');return SourceLyrics.draw(ms,motion,elapsed,playing);}
function __sourceDispose(){SourceLyrics.dispose();}

function __sourceCanvas(){return SourceLyrics.canvasId();}
function __encode(bytes){
 const table='ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';const out=[];
 for(let i=0;i<bytes.length;i+=3){const a=bytes[i],b=bytes[i+1],c=bytes[i+2];out.push(table[a>>2],table[((a&3)<<4)|((b||0)>>4)],i+1<bytes.length?table[((b&15)<<2)|((c||0)>>6)]:'=',i+2<bytes.length?table[c&63]:'=');}
 return out.join('');
}
NativeContext.prototype.putImageData=function(data,x,y){__canvas(this.canvas.nativeId,'pixelBytes',__encode(data.data),data.width,data.height,x,y);};

class AbortController{constructor(){this.signal={aborted:false};}abort(){this.signal.aborted=true;}}
// Avoid re-sending identical paint state for every glyph/path; save/restore invalidates the single native state cache.
NativeContext.prototype.apply=function(){
 const values=[__token(this.fillStyle),__token(this.strokeStyle),this.globalAlpha,this.lineWidth,this.font,this.textAlign,this.textBaseline,this.globalCompositeOperation,this.lineCap,this.lineJoin,this.filter,this.shadowColor,this.shadowBlur,this.shadowOffsetX,this.shadowOffsetY,this.imageSmoothingEnabled];
 if(this.__lastStyle&&values.every((value,i)=>value===this.__lastStyle[i]))return;
 this.__lastStyle=values;__canvas(this.canvas.nativeId,'style',...values);
};
const __restore=NativeContext.prototype.restore;
NativeContext.prototype.restore=function(){__restore.call(this);this.__lastStyle=null;};
function __sourcePointer(x,y,press){globalThis.__seek=-1;SourceLyrics.pointer(x,y,press);return globalThis.__seek;}
