// Transport adapter only. The original scene pass, layout, particles and glyph timing remain unchanged.
import { drawCassetteScene } from 'SOURCE/cassette/cassetteScenePass';
import { cassetteRepeatingAssets } from 'SOURCE/cassette/cassetteAssets';
const native=(...args:any[])=>(globalThis as any).__cassette(...args);
export class CassetteRenderer {
  private ratio=1; private w=1;private h=1;private assets=new Map<string,any>();
  private cover:any=null;private titles:any=null;private generated:any;
  readonly gl:any={isContextLost:()=>false};
  constructor(private canvas:any) {
    this.generated={glow:this.generatedSprite(false),streak:this.generatedSprite(true)};
  }
  get pixelRatio(){return this.ratio;}
  isContextLost(){return false;}
  resize(w:number,h:number,dpr:number,pressure:boolean){
    this.ratio=Math.min(Math.max(.5,dpr),pressure?1.25:2,Math.sqrt((pressure?1280*720:1920*1080)/(w*h)));
    this.w=Math.max(1,Math.floor(w*this.ratio));this.h=Math.max(1,Math.floor(h*this.ratio));
    this.canvas.width=this.w;this.canvas.height=this.h;
  }
  setPressure(){}
  backingBytes(){return native('bytes');}
  hasAsset(id:string){return this.assets.has(id);}
  uploadAsset(id:string,image:any){if(!this.assets.has(id))this.assets.set(id,this.upload(image,cassetteRepeatingAssets.has(id)));}
  releaseAsset(id:string){this.deleteTexture(this.assets.get(id));this.assets.delete(id);}
  assetIds(){return [...this.assets.keys()];}
  uploadAtlas(canvas:any){return this.upload(canvas);}
  deleteTexture(texture:any){if(texture)native('delete',texture.texture);}
  private upload(canvas:any,repeat=false){const id=native('upload',canvas.nativeId,repeat);canvas.__consumed=true;return {texture:id,width:canvas.width,height:canvas.height,bytes:canvas.width*canvas.height*4};}
  setCover(canvas:any){this.deleteTexture(this.cover);this.cover=canvas?this.upload(canvas):null;}
  hasCover(){return !!this.cover;}
  setTitles(ink:any,neon:any){if(this.titles){this.deleteTexture(this.titles.ink);this.deleteTexture(this.titles.neon);}this.titles=ink&&neon?{ink:this.upload(ink),neon:this.upload(neon)}:null;}
  render(frame:any){
    const pr=this.ratio;
    native('begin',this.w,this.h,frame.time,frame.fallback[0],frame.fallback[1],frame.fallback[2]);
    const noise=this.assets.get('waterNormal')??this.generated.glow;
    drawCassetteScene({
      has:(id:string)=>this.assets.has(id),
      layer:(id:string,cx:number,cy:number,w:number,h:number,options:any={})=>{
        const texture=id==='cover'?this.cover:id==='title'?this.titles?.ink:id==='titleNeon'?this.titles?.neon:this.assets.get(id);
        if(texture)native('layer',texture.texture,noise.texture,cx,cy,w,h,JSON.stringify(options));
      },
      particles:(id:string,emit:any)=>{
        const texture=id==='glow'?this.generated.glow:id==='streak'?this.generated.streak:this.assets.get(id);
        if(texture)emit((...v:number[])=>native('particle',texture.texture,...v));
      }
    },{...frame,pixelRatio:pr});
    native('composite',JSON.stringify({ripples:Array.from(frame.ripples),pixelRatio:pr,retro:frame.retro,reveal:frame.reveal,fallback:frame.fallback}));
    for(const batch of frame.glyphs)native('glyphs',batch.texture,noise.texture,JSON.stringify(Array.from(batch.data.subarray(0,batch.count*17))),batch.count,JSON.stringify(frame.palette));
    native('finish');
  }
  park(){} // The worker releases the whole scene when hidden instead of retaining a parked renderer.
  releaseArt(){for(const id of this.assetIds())this.releaseAsset(id);this.setCover(null);this.setTitles(null,null);}
  dispose(){this.releaseArt();this.deleteTexture(this.generated.glow);this.deleteTexture(this.generated.streak);}
  private generatedSprite(streak:boolean){
    const c=document.createElement('canvas');c.width=streak?128:64;c.height=streak?16:64;const x=c.getContext('2d')!;
    if(streak){const a=x.createLinearGradient(0,0,128,0);a.addColorStop(0,'rgba(255,255,255,0)');a.addColorStop(.85,'rgba(255,255,255,.7)');a.addColorStop(1,'white');x.fillStyle=a;x.fillRect(0,0,128,16);x.globalCompositeOperation='destination-in';const b=x.createLinearGradient(0,0,0,16);b.addColorStop(0,'transparent');b.addColorStop(.5,'black');b.addColorStop(1,'transparent');x.fillStyle=b;x.fillRect(0,0,128,16);}
    else{const a=x.createRadialGradient(32,32,0,32,32,32);a.addColorStop(0,'white');a.addColorStop(.18,'rgba(255,255,255,.75)');a.addColorStop(.5,'rgba(255,255,255,.18)');a.addColorStop(1,'transparent');x.fillStyle=a;x.fillRect(0,0,64,64);}
    const texture=this.upload(c);c.width=0;return texture;
  }
}
