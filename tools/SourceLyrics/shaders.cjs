const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
module.exports=async(source,target,esbuild)=>{
 const result=await esbuild.build({stdin:{contents:`export * from ${JSON.stringify(path.join(source,'src/renderer/components/lyrics/cassette/cassetteShaders.ts'))}`,resolveDir:source,loader:'ts'},bundle:true,format:'cjs',write:false});
 const box={exports:{},module:{exports:{}}};box.module.exports=box.exports;vm.runInNewContext(result.outputFiles[0].text,box);
 const shaders=box.module.exports;
 for(const [id,key]of Object.entries({layer:'cassetteLayerFragment',glyph:'cassetteGlyphFragment',particle:'cassetteParticleFragment',composite:'cassetteCompositeFragment'})){
  let code=shaders[key].replace(/#version[^\n]*\n/g,'').replace(/precision[^;]*;/g,'').replace(/out vec4 outColor;/,'');
  const children=[];
  code=code.replace(/uniform sampler2D (\w+);/g,(_,name)=>{children.push(name);return `uniform shader ${name};\nuniform float2 ${name}_size;`;});
  code=code.replace(/in (vec[234]) (\w+);/g,(_,type,name)=>name==='v_local'||name==='v_screen'||name==='v_uv'?'':`uniform ${type} ${name};`);
  code=code.replace(/texture\((\w+),/g,(_,name)=>`sample_${name}(`).replace(/\bvec([234])\b/g,'float$1');
  const sample=children.map(n=>`float4 sample_${n}(float2 uv){return ${n}.eval(${n==='u_scene'?'float2(uv.x,1.0-uv.y)':'uv'}*${n}_size);}`).join('\n');
  const prelude='uniform float2 draw_size;\nuniform float2 draw_center;\nuniform float draw_rotation;\nuniform float4 draw_uv;\n';
  const locals=`float4 outColor;\nfloat2 v_local=coord/draw_size;\nfloat2 v_uv=mix(draw_uv.xy,draw_uv.zw,v_local);\nfloat c=cos(draw_rotation),s=sin(draw_rotation);\nfloat2 q=(v_local-.5)*draw_size;\nfloat2 v_screen=draw_center+float2(q.x*c-q.y*s,q.x*s+q.y*c);\n`;
  // Sampling functions precede all callers, including the unmodified original ripple helper.
  const main=code.indexOf('void main()');
  code=code.slice(0,main)+code.slice(main).replace('void main() {',`half4 main(float2 coord) {\n${locals}`);
  code=code.replace('gl_FragCoord.xy','float2(coord.x,u_view.y-coord.y)');
  code=prelude+code;
  const firstFunction=code.search(/(?:float2 rippleOffset|float hash|half4 main)/);
  code=code.slice(0,firstFunction)+sample+'\n'+code.slice(firstFunction);
  const end=code.lastIndexOf('}');code=code.slice(0,end)+'return half4(outColor);\n'+code.slice(end);
  fs.writeFileSync(path.join(target,'cassette-'+id+'.sksl'),code);
 }
 const assetsText=fs.readFileSync(path.join(source,'src/renderer/components/lyrics/cassette/cassetteAssets.ts'),'utf8');
 const assets=Object.fromEntries([...assetsText.matchAll(/import (\w+) from '[^']*\/([^/']+\.webp)\?no-inline'/g)].map(m=>[m[1],m[2]]));
 fs.writeFileSync(path.join(target,'asset-index.json'),JSON.stringify(assets));
};
