// Narrow Canvas host. No network, filesystem, CLR, storage, audio or browser APIs.
const window=globalThis; window.innerWidth=1280;window.innerHeight=720;
let __canvasMain=null;
let __frameEpoch=0;
function __token(value){return value&&value.nativeToken?'@'+value.nativeToken:String(value);}
class NativeGradient{
 constructor(canvas,kind,args){this.canvas=canvas;this.kind=kind;this.args=args;this.stops=[];this.epoch=-1;this.id=0;}
 get nativeToken(){if(this.epoch!==__frameEpoch){this.id=__canvas(this.canvas.nativeId,'gradient',this.kind,...this.args);for(const [offset,color]of this.stops)__canvas(this.canvas.nativeId,'stop',this.id,offset,color);this.epoch=__frameEpoch;}return this.id;}
 addColorStop(offset,color){this.stops.push([offset,String(color)]);this.epoch=-1;}
}
class NativePattern{constructor(canvas,source,repeat){this.canvas=canvas;this.source=source;this.repeat=repeat;this.epoch=-1;this.id=0;}get nativeToken(){if(this.epoch!==__frameEpoch){this.id=__canvas(this.canvas.nativeId,'pattern',this.source.nativeId,this.repeat);this.epoch=__frameEpoch;}return this.id;}}
class NativeCanvas{
 constructor(id=0,width=1,height=1){this.nativeId=id||__canvas(0,'new');this._width=width;this._height=height;this.style={};this.ctx=new NativeContext(this);}
 get width(){return this._width;} set width(value){this._width=Math.max(1,value|0);__canvas(this.nativeId,'resize',this._width,this._height);}
 get height(){return this._height;} set height(value){this._height=Math.max(1,value|0);__canvas(this.nativeId,'resize',this._width,this._height);}
 getContext(){return this.ctx;} addEventListener(){} removeEventListener(){}
 dispose(){if(this.nativeId){__canvas(this.nativeId,'dispose');this.nativeId=0;}}
}
class NativeContext{
 constructor(canvas){this.canvas=canvas;Object.assign(this,{fillStyle:'#000',strokeStyle:'#000',globalAlpha:1,lineWidth:1,font:'16px sans-serif',textAlign:'start',textBaseline:'alphabetic',globalCompositeOperation:'source-over',lineCap:'butt',lineJoin:'miter',filter:'none',shadowColor:'transparent',shadowBlur:0,shadowOffsetX:0,shadowOffsetY:0,imageSmoothingEnabled:true});this.stack=[];}
 apply(){__canvas(this.canvas.nativeId,'style',__token(this.fillStyle),__token(this.strokeStyle),this.globalAlpha,this.lineWidth,this.font,this.textAlign,this.textBaseline,this.globalCompositeOperation,this.lineCap,this.lineJoin,this.filter,this.shadowColor,this.shadowBlur,this.shadowOffsetX,this.shadowOffsetY);}
 call(op,...args){this.apply();return __canvas(this.canvas.nativeId,op,...args);}
 save(){this.stack.push({...this});__canvas(this.canvas.nativeId,'save');}
 restore(){if(!this.stack.length)return;const old=this.stack.pop();for(const key of Object.keys(old))if(key!=='stack'&&key!=='canvas')this[key]=old[key];__canvas(this.canvas.nativeId,'restore');}
 setTransform(...args){if(typeof args[0]==='object'){const m=args[0];args=[m.a,m.b,m.c,m.d,m.e,m.f];}__canvas(this.canvas.nativeId,'setTransform',...args);}
 resetTransform(){this.setTransform(1,0,0,1,0,0);}
 getTransform(){return JSON.parse(__canvas(this.canvas.nativeId,'getTransform'));}
 transform(...a){__canvas(this.canvas.nativeId,'transform',...a);}
 translate(...a){__canvas(this.canvas.nativeId,'translate',...a);}scale(...a){__canvas(this.canvas.nativeId,'scale',...a);}rotate(...a){__canvas(this.canvas.nativeId,'rotate',...a);}
 beginPath(){__canvas(this.canvas.nativeId,'beginPath');}closePath(){__canvas(this.canvas.nativeId,'closePath');}
 moveTo(...a){__canvas(this.canvas.nativeId,'moveTo',...a);}lineTo(...a){__canvas(this.canvas.nativeId,'lineTo',...a);}
 bezierCurveTo(...a){__canvas(this.canvas.nativeId,'bezierCurveTo',...a);}quadraticCurveTo(...a){__canvas(this.canvas.nativeId,'quadraticCurveTo',...a);}
 rect(...a){__canvas(this.canvas.nativeId,'rect',...a);}roundRect(x,y,w,h,r){__canvas(this.canvas.nativeId,'roundRect',x,y,w,h,Array.isArray(r)?r[0]:r);}
 arc(x,y,r,s,e,reverse=false){__canvas(this.canvas.nativeId,'arc',x,y,r,s,e,reverse?1:0);}
 arcTo(...a){__canvas(this.canvas.nativeId,'arcTo',...a);}ellipse(x,y,rx,ry,rot,s,e,reverse=false){__canvas(this.canvas.nativeId,'ellipse',x,y,rx,ry,rot,s,e,reverse?1:0);}
 fill(){this.call('fill');}stroke(){this.call('stroke');}clip(){this.call('clip');}
 fillRect(...a){this.call('fillRect',...a);}strokeRect(...a){this.call('strokeRect',...a);}clearRect(...a){this.call('clearRect',...a);}
 fillText(t,x,y){this.call('fillText',String(t),x,y);}strokeText(t,x,y){this.call('strokeText',String(t),x,y);}
 measureText(text){const width=this.call('measure',String(text));return {width,actualBoundingBoxLeft:0,actualBoundingBoxRight:width,actualBoundingBoxAscent:parseFloat(this.font)*.8||16,actualBoundingBoxDescent:parseFloat(this.font)*.2||4};}
 drawImage(image,...a){this.call('drawImage',image.nativeId,...a);}
 createLinearGradient(...a){return new NativeGradient(this.canvas,'linear',a);}
 createRadialGradient(...a){return new NativeGradient(this.canvas,'radial',a);}
 createConicGradient(...a){return new NativeGradient(this.canvas,'conic',a);}
 setLineDash(a){this.call('dash',a.join(','));}
 createImageData(w,h){return {width:w,height:h,data:new Uint8ClampedArray(w*h*4)};}
 getImageData(x,y,w,h){const data=this.createImageData(w,h);const raw=__canvas(this.canvas.nativeId,'readPixels',x,y,w,h);const bytes=__base64(raw);data.data.set(bytes);return data;}
 putImageData(data,x,y){__canvas(this.canvas.nativeId,'pixels',data.data,data.width,data.height,x,y);}
 createPattern(canvas,repeat){return new NativePattern(this.canvas,canvas,repeat);}
}
const HTMLCanvasElement=NativeCanvas;
const document={createElement(tag){if(tag==='canvas')return new NativeCanvas();throw new Error('Unsupported visual element: '+tag);},fonts:{check:()=>true,ready:Promise.resolve()},body:{style:{}},hidden:false};
const performance={now:()=>0,timeOrigin:0};
