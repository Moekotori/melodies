import { CassetteEngine } from 'SOURCE/cassette/cassetteEngine';
import { cassettePhaseAssets, cassetteSharedAssets } from 'SOURCE/cassette/cassetteAssets';
let stage:any,canvas:any;
export function build(input:any,width:number,height:number){
 canvas=document.createElement('canvas');
 stage=new CassetteEngine({canvas,onSeek:(ms:number)=>{(globalThis as any).__seek=ms},onFailure:()=>{throw Error('Cassette scene failed')},onCoverReload:()=>{}});
 // Native assets are bundled and decoded synchronously by the serialized worker; no browser Image/URL loader.
 stage.ensureAssets=()=>{
  const wanted=new Set(cassetteSharedAssets);
  for(const phase of ["day","sunset","night"] as const){
   if(phase!==stage.target&&stage.weights[phase]<=0)continue;
   for(const id of [...cassettePhaseAssets[phase].essential,...cassettePhaseAssets[phase].extra])wanted.add(id);
  }
  for(const id of stage.renderer.assetIds())if(!wanted.has(id))stage.renderer.releaseAsset(id);
  for(const id of wanted)if(!stage.renderer.hasAsset(id)){
   const c=(globalThis as any).__assetCanvas(id);stage.renderer.uploadAsset(id,c);c.dispose();
  }
 };
 stage.resize(width,height);
 stage.setSong({kind:'synced',source:'local',offsetMs:0,lines:input.lines},input.durationMs,input.title);
 stage.setShowTranslation(input.translation!==false);
 stage.setFont(input.font||null,input.fontSize||40,input.secondarySize||22);
 stage.setPreferences({timeOfDay:input.phase||'auto',retro:input.retro!==false,frameCap:0,seed:input.seed||0});
 if(input.coverCanvas)stage.setCover(input.coverCanvas);
 return {lines:stage.timeline.length,kind:'cassette'};
}
export function draw(ms:number,motion:boolean,elapsed:number,playing:boolean){
 stage.setMotion(motion);stage.setClock({positionMs:ms,updatedAtMs:(globalThis as any).__now,playbackRate:1,playing,offsetMs:0});
 stage.frame(elapsed);
 return canvas.nativeId;
}
export function canvasId(){return canvas.nativeId;}
export function dispose(){stage?.dispose();stage=null;canvas?.dispose();canvas=null;}
export function pointer(x:number,y:number,press:boolean){if(press)stage.press(x,y);}

export function cover(canvas:any){stage.setCover(canvas);}
