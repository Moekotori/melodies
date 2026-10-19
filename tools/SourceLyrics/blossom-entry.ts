import { BlossomEngine } from 'SOURCE/blossom/blossomEngine';
import { createBlossomPlan } from 'SOURCE/blossom/blossomPlan';
let stage: any, canvas: any, plan: any;
export function build(input: any, width: number, height: number) {
  canvas = document.createElement('canvas');
  stage = new BlossomEngine({canvas,onSeek:(ms:number)=>{(globalThis as any).__seek=ms},onShot:()=>{}});
  stage.resize(width,height);
  const lyrics = {kind:'synced',source:'local',offsetMs:0,lines:input.lines};
  plan=createBlossomPlan(lyrics,input.durationMs,input.title,input.artist);
  stage.setPlan(plan);
  stage.setFrameInterval(null);
  return {shots:plan.shots.length,kind:'blossom'};
}
export function draw(ms:number, motion:boolean, elapsed:number,playing:boolean) {
  const now=(globalThis as any).__now;
  stage.setMotion(motion);
  stage.setClock({positionMs:ms,updatedAtMs:now,playbackRate:1,playing,offsetMs:0});
  stage.render(plan,Math.min(.1,Math.max(.001,elapsed/1000)),now);
  return canvas.nativeId;
}
export function canvasId(){return canvas.nativeId;}
export function dispose(){stage?.dispose();stage=null;canvas?.dispose();canvas=null;}
export function pointer(x:number,y:number,press:boolean){
 const p=stage.s.ptr;p.hx=x;p.hy=y;p.x=x/stage.s.W;p.y=y/stage.s.H;p.over=true;
 if(press)stage.click(x,y);else stage.updateHover();
}
