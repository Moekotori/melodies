// Read the user's Electron checkout; all generated files and caches stay in this repository.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
if(!process.argv[2])throw new Error('Usage: node tools/Import-SourceLyrics.cjs <Electron repository>');
const source=path.resolve(process.argv[2]);
const backgroundOnly=process.argv.includes('--background-only');
const esbuild=require(path.join(source,'node_modules/esbuild'));
const root=path.resolve(__dirname,'..'),target=path.join(root,'src/Echo.Visuals/Resources/SourceLyrics');
fs.mkdirSync(target,{recursive:true});
(async()=>{
 const manifestPath=path.join(target,'manifest.json');
 const metadata=backgroundOnly?JSON.parse(fs.readFileSync(manifestPath,'utf8')).filter(x=>!['background','background-art'].includes(x.scene)):[];
 // Cassette's behavior is maintained in C#; only its artwork and shader resources are imported below.
 for(const mode of backgroundOnly?['background']:['blossom','background']) {
  const result=await esbuild.build({entryPoints:[path.join(__dirname,'SourceLyrics',mode+'-entry.ts')],bundle:true,format:'iife',globalName:mode==='background'?'SourceBackground':'SourceLyrics',target:'es2022',write:false,metafile:true,
   plugins:[{name:'native-lyrics',setup(build){
    build.onResolve({filter:/^SOURCE\//},a=>({path:path.join(source,'src/renderer/components/lyrics',a.path.slice(7)+'.ts')}));
    build.onResolve({filter:/lyricsResourceLifecycle$/},()=>({path:'resource-owner',namespace:'native'}));
    build.onResolve({filter:/appearancePreferences$/},()=>({path:'font-list',namespace:'native'}));
    build.onResolve({filter:/\.(?:webp\?no-inline|png)$/},a=>({path:a.path,namespace:'image'}));
    build.onLoad({filter:/.*/,namespace:'image'},a=>({contents:'export default '+JSON.stringify(path.basename(a.path).replace('?no-inline',''))+';',loader:'js'}));
    build.onLoad({filter:/geometricAmbienceRenderer\.ts$/},a=>{
     let code=fs.readFileSync(a.path,'utf8');const start=code.indexOf('  const load = async');const end=code.indexOf('  const preparePlate',start);
     code=code.slice(0,start)+"  const load = async (url: string,width: number,height: number): Promise<any> => (globalThis as any).__backgroundAsset(url,width,height);\n\n"+code.slice(end);
     return {contents:code,loader:'ts',resolveDir:path.dirname(a.path)};
    });
    build.onResolve({filter:/visualFrameClock$/},()=>({path:'frame-clock',namespace:'native'}));
    build.onLoad({filter:/.*/,namespace:'native'},()=>({contents:'export const subscribeVisualFrame=()=>()=>{};export const registerLyricsResourceOwner=()=>()=>{};export const serializeFontList=s=>s;',loader:'js'}));
   }}]});
  const output=result.outputFiles[0].contents;
  if(output.length>2*1024*1024)throw new Error('Source scene script budget exceeded');
  fs.writeFileSync(path.join(target,mode+'.js'),output);
  for(const filename of Object.keys(result.metafile.inputs)) {
   const file=path.resolve(root,filename),relative=path.relative(source,file);if(relative==='..'||relative.startsWith('..'+path.sep)||path.isAbsolute(relative))continue;
   const bytes=fs.readFileSync(file);metadata.push({scene:mode,source:path.relative(source,file).split(path.sep).join('/'),sha256:crypto.createHash('sha256').update(bytes).digest('hex')});
  }
 }
 if(!backgroundOnly){
 const images=path.join(source,'src/renderer/assets/lyrics-cassette');
 const nativeTarget=path.join(root,'src/Echo.Visuals.Native/Resources/SourceLyrics');
 const assets=path.join(nativeTarget,'Cassette');fs.mkdirSync(assets,{recursive:true});
 for(const name of fs.readdirSync(images).filter(n=>n.endsWith('.webp'))) {
  const file=path.join(images,name);fs.copyFileSync(file,path.join(assets,name));
  metadata.push({scene:'cassette-art',source:path.relative(source,file),sha256:crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex')});
 }
 await require('./SourceLyrics/shaders.cjs')(source,nativeTarget,esbuild);
 const shaderFile=path.join(source,'src/renderer/components/lyrics/cassette/cassetteShaders.ts');
 metadata.push({scene:'cassette-shaders',source:path.relative(source,shaderFile),sha256:crypto.createHash('sha256').update(fs.readFileSync(shaderFile)).digest('hex')});
 }
 const geometric=path.join(target,'Geometric');fs.mkdirSync(geometric,{recursive:true});
 const copyBackgroundAsset=(file,destination)=>{
  fs.copyFileSync(file,destination);
  metadata.push({scene:'background-art',source:path.relative(source,file).split(path.sep).join('/'),sha256:crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex')});
 };
 for(const name of ['ambient.png','geometry.png','README.md'])copyBackgroundAsset(path.join(source,'src/renderer/assets/geometric-ambience',name),path.join(geometric,name));
 const previews=path.join(source,'src/renderer/assets/lyrics-background-previews');
 const previewTarget=path.join(root,'src/Echo.Desktop/Resources/LyricsBackgrounds');fs.mkdirSync(previewTarget,{recursive:true});
 for(const name of fs.readdirSync(previews).filter(n=>n.endsWith('.webp')||n==='README.md'))copyBackgroundAsset(path.join(previews,name),path.join(previewTarget,name));
 fs.writeFileSync(manifestPath,JSON.stringify(metadata,null,2));
 console.log(JSON.stringify({sources:metadata.length}));
})().catch(e=>{console.error(e);process.exitCode=1});
