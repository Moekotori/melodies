const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm'),crypto=require('node:crypto');
const source=process.argv[2];if(!source)throw new Error('Provide the read-only old JIZURA directory.');
const target=path.resolve(__dirname,'../src/Echo.Visuals/Resources/Jizura');fs.mkdirSync(target,{recursive:true});
const text=fs.readFileSync(path.join(source,'jizuraDocument.ts'),'utf8');
const imports=new Map([...text.matchAll(/import (\w+) from '\.\/([^']+)\?url&no-inline'/g)].map(m=>[m[1],m[2]]));
const tail=text.match(/sort\(\)\.map\(key => sources\[key\]\),([\s\S]*?)\];/)[1];
const names=fs.readdirSync(path.join(source,'vendor')).filter(x=>x.endsWith('.js')).sort().map(x=>'vendor/'+x);
for(const name of tail.match(/\b\w+\b/g)||[]){if(['runtime','frameDriver','frameWake','menuShortcut'].includes(name))continue;if(imports.has(name))names.push(imports.get(name));}
let total=0;const manifest=[];
for(let i=0;i<names.length;i++){const bytes=fs.readFileSync(path.join(source,names[i]));total+=bytes.length;if(total>8*1024*1024)throw new Error('Script budget exceeded');const file=String(i).padStart(3,'0')+'-'+path.basename(names[i]);fs.writeFileSync(path.join(target,file),bytes);manifest.push({file,source:names[i],sha256:crypto.createHash('sha256').update(bytes).digest('hex')});}
const ts=require(path.resolve(source,'../../../../../node_modules/typescript'));
const loaded=new Map();
function load(name){const file=path.resolve(source,name+'.ts');if(!file.startsWith(path.resolve(source)+path.sep))throw new Error('Import outside visual source');if(loaded.has(file))return loaded.get(file);const script=ts.transpileModule(fs.readFileSync(file,'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText;const box={exports:{},require:n=>load(n)};loaded.set(file,box.exports);vm.runInNewContext(script,box,{timeout:1000});return box.exports;}
const looks=load('jizuraSignatureLooks').jizuraSignatureLooks;
fs.writeFileSync(path.join(target,'looks.json'),JSON.stringify(looks));
fs.copyFileSync(path.join(source,'vendor/LICENSE'),path.join(target,'LICENSE'));
fs.writeFileSync(path.join(target,'SOURCE.md'),'# JIZURA native visual resources\n\nUpstream: https://github.com/852wa/JIZURA\nRevision: 8da975fb362d966b065217618aedafd5a35a39e0\nMIT, Copyright (c) 2026 hakoniwa. Complete permission notice: LICENSE.\n\nImported in original build order from the old ECHO visual directory. manifest.json records each original source and its SHA-256. Includes ECHO first-party scene adapters and 23 look definitions. Browser runtime, animation driver and menu hooks are excluded.\n\nJint interprets local planning/drawing scripts; Skia implements the narrow Canvas host. No browser, Node, filesystem, network, audio decoder or external font loading is exposed. Known raster loops are translated explicitly in JizuraEngine; resource files preserve their original bytes. See docs/JIZURA_NATIVE_ENGINE.md for ownership, budgets and verification limits.\n');
fs.writeFileSync(path.join(target,'manifest.json'),JSON.stringify({revision:'8da975fb362d966b065217618aedafd5a35a39e0',scripts:manifest},null,2));
console.log(JSON.stringify({scripts:manifest.length,bytes:total,looks:looks.length}));
