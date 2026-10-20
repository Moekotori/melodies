import {createShaderSceneRenderer} from 'SOURCE/shader-scenes/shaderSceneRenderer';
import {isLyricsShaderSceneMusicReactive} from 'SOURCE/../../../shared/constants/lyricsShaderScenes';
import {resolveShaderScenePalette} from 'SOURCE/shader-scenes/shaderScenePalettes';
import {createFlowingLightRenderer} from 'SOURCE/flowingLightRenderer';
import {createFlowingLightDrive,createFlowingLightDriver} from 'SOURCE/flowingLightModel';
import {createLyricsBackdropPalette} from 'SOURCE/lyricsBackdropPalette';
import {createCoverFlowRenderer} from 'SOURCE/cover-flow/coverFlowRenderer';
import {defaultCoverFlowPalette,extractCoverFlowPalette} from 'SOURCE/cover-flow/coverFlowPalette';
import {defaultGeometricAmbienceSettings} from 'SOURCE/../../../shared/constants/geometricAmbienceSettings';
import {createGeometricAmbienceRenderer} from 'SOURCE/geometricAmbienceRenderer';
let renderer:any,canvas:any,mode:string,light:boolean,driver:any,elapsed=0,coverRevision=0,lastTelemetry=-1,lastPlaying=false;
export function build(input:any,w:number,h:number){
 mode=input.mode;light=!input.dark;canvas=document.createElement('canvas');driver=createFlowingLightDriver();elapsed=0;lastTelemetry=-1;
 const palette=createLyricsBackdropPalette(input.sample||null,input.dark?'dark':'light').flowingLight;
 if(mode==='coverFlow'){renderer=createCoverFlowRenderer(canvas,defaultCoverFlowPalette);renderer?.resize(w,h);}
 else if(mode==='flowingLight'){renderer=createFlowingLightRenderer(canvas,palette,input.trackKey||'');renderer?.resize(w,h,1);}
 else if(mode==='geometricAmbience'){renderer=createGeometricAmbienceRenderer(canvas,light);renderer?.resize(w,h);renderer?.configure(defaultGeometricAmbienceSettings,palette,true);}
 else{renderer=createShaderSceneRenderer(canvas,mode as any,resolveShaderScenePalette({palette:'cover',customColor:'#7c9cff',coverPalette:palette,mode:mode as any}));renderer?.resize(w,h,1);}
 renderer?.setTargetFrameInterval?.(1000/30);
 if(!renderer)throw Error('Original background renderer unavailable: '+mode);
 return {mode};
}
export function update(input:any,w:number,h:number){
 light=!input.dark;
 const palette=createLyricsBackdropPalette(input.sample||null,input.dark?'dark':'light').flowingLight;
 if(mode==='coverFlow'){renderer.resize(w,h);}
 else if(mode==='flowingLight'){renderer.resize(w,h,1);renderer.setPalette(palette);renderer.setTrackKey(input.trackKey||'');}
 else if(mode==='geometricAmbience'){renderer.resize(w,h);renderer.setTheme(light);renderer.configure(defaultGeometricAmbienceSettings,palette,false);}
 else{if(!renderer.setScene(input.mode))throw Error('Original background scene switch failed');renderer.resize(w,h,1);renderer.setPalette(resolveShaderScenePalette({palette:'cover',customColor:'#7c9cff',coverPalette:palette,mode:input.mode}));}
 mode=input.mode;
}
export function frameRate(fps:number){renderer.setTargetFrameInterval?.(1000/fps);}
export function clearCover(){if(mode==='coverFlow')renderer.setPalette(defaultCoverFlowPalette);else renderer.setCover?.(null);}
export function draw(ms:number,dt:number,spectrum:number[],playing:boolean,telemetryTimestamp:number){
 const energy=spectrum.length?spectrum.reduce((a,b)=>a+b,0)/spectrum.length:0;
 if(lastTelemetry!==telemetryTimestamp||lastPlaying!==playing){driver.ingest({state:playing?'playing':'paused',audioLevels:{visualEnergy:energy,visualSpectrum:spectrum}},ms);lastTelemetry=telemetryTimestamp;lastPlaying=playing;}
 driver.advance(dt);elapsed+=Math.min(100,Math.max(0,dt));
 if(mode==='coverFlow')renderer.draw(elapsed/1000,light);
 else if(mode==='geometricAmbience')renderer.draw(dt,driver.drive);
 else renderer.draw(ms,dt,mode==='flowingLight'||isLyricsShaderSceneMusicReactive(mode)?driver.drive:null);
}
export function cover(c:any){
 if(mode==='coverFlow'){const probe=document.createElement('canvas');probe.width=64;probe.height=64;const g=probe.getContext('2d');g.drawImage(c,0,0,64,64);const p=extractCoverFlowPalette(g.getImageData(0,0,64,64).data);renderer.setPalette(p,coverRevision===0);coverRevision++;probe.dispose();}
 else renderer.setCover?.(c,'cover-'+(++coverRevision));
}
export function pointer(x:number,y:number,press:boolean){renderer.pointerMove?.(x,1-y);if(press)renderer.pointerPress?.(x,1-y);}
export function pointerLeave(){renderer.pointerLeave?.();}
export function canvasId(){return canvas.nativeId;}
export function dispose(){renderer?.dispose();renderer=null;canvas?.dispose();canvas=null;}
