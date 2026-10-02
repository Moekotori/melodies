function __base64(text){const abc='ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';const result=[];let value=0,bits=0;for(const c of text){const n=abc.indexOf(c);if(n<0)continue;value=(value<<6)|n;bits+=6;if(bits>=8){bits-=8;result.push((value>>bits)&255);}}return result;}
let __renderer=null,__plan=null,__framePlan=null,__continuity=null,__caches=null;
function __initialize(){
 const release=value=>{if(value instanceof NativeCanvas)value.dispose();else if(value?.pieces)for(const piece of value.pieces)piece.cv?.dispose();};
 const remove=JizuraLruMap.prototype.delete;
 JizuraLruMap.prototype.delete=function(key){const entry=Map.prototype.get.call(this,key);const result=remove.call(this,key);if(entry)release(entry.value);return result;};
 JizuraLruMap.prototype.clear=function(){for(const value of this.values())release(value);Map.prototype.clear.call(this);this.bytes=0;};
 __canvasMain=new NativeCanvas();__canvasMain.width=1280;__canvasMain.height=720;
 J.ensureFonts=()=>Promise.resolve();J.glyphs.maxRes=128;
 __renderer=new J.Renderer();__caches=installJizuraFrameCaches(__renderer);
}
function __build(inputJson){
 const input=JSON.parse(inputJson);const project=J.defaultProject();
 project.title=input.title;project.artist=input.artist;project.lyrics=input.lines.map(l=>l.text).join('\n');project.seed=input.seed>>>0;
 project.style=J.STYLES[input.style]?input.style:'noir';
 J.applyEchoDirection(project,{look:input.look,fit:'window',unifiedComposition:true,randomComposition:false,originalStyle:false,themeScenesOnly:false,extraExpressions:false},0);
 project.fps=30;project.extra=false;project.fx.koma=0;project.fx.onTwos=false;project.typeset=false;project.timing.snap=false;project.timing.tail=0;
 const parse=J.parseLyrics,timing=J.computeTiming;
 J.parseLyrics=()=>({meta:{},lines:input.lines.map((l,i)=>({text:l.text,lrc:l.start,src:i,note:null,impact:false,emph:[],manual:null,gapBefore:false}))});
 J.computeTiming=()=>({starts:input.lines.map(l=>l.start),ends:input.lines.map(l=>l.end),duration:Math.max(input.duration,...input.lines.map(l=>l.end))});
 try{__plan=J.plan(project,null);}finally{J.parseLyrics=parse;J.computeTiming=timing;}
 J.bindAfterglowWordEffects(__plan,input.lines);J.bindAfterglowLightMotion(__plan);J.bindAfterglowLightFields(__plan,input);
 __framePlan=createJizuraEffectWindow(__plan);__continuity=createJizuraBackdropContinuity(__plan);
 return JSON.stringify({cuts:__plan.cuts.length,style:project.style,width:__plan.W,height:__plan.H});
}
function __draw(seconds,motion){
 __frameEpoch++;__canvas(0,'beginFrame');
 J.echoTier=0;J.echoMinRes=0;J.echoMotion=motion;J.echoPresentationTime=seconds;J.echoDissolve=motion;
 const cut=J.cutAt(__plan,seconds);const time=!motion&&cut?cut.start+Math.min((cut.end-cut.start)/2,Math.max(.1,cut.inDur||.3)):seconds;
 __renderer.frame(__canvasMain.ctx,__framePlan(time),time,{scale:Math.min(1280/__plan.W,720/__plan.H),fast:!motion,noGhost:!motion});
 __continuity?.draw(__renderer,__canvasMain.ctx,time,{scale:Math.min(1280/__plan.W,720/__plan.H),fast:!motion});
 return __canvasMain.nativeId;
}
