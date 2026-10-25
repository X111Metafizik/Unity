import {mkdir,readFile,writeFile,copyFile} from 'node:fs/promises';
import {resolve,dirname,join,relative} from 'node:path';
import {createHash} from 'node:crypto';
const source=resolve(process.argv[2]),target=resolve(import.meta.dirname,'../Assets/StreamingAssets/Ladir/authoring'),seen=new Set(),records=[];
async function copy(path){
 path=resolve(path);if(seen.has(path))return;seen.add(path);const rel=relative(source,path);if(rel.startsWith('..'))throw Error('Source escape');const data=await readFile(path),out=join(target,rel);await mkdir(dirname(out),{recursive:true});await writeFile(out,data);records.push({path:rel.replaceAll('\\','/'),sha256:createHash('sha256').update(data).digest('hex')});
 if(path.endsWith('.mjs'))for(const match of data.toString().matchAll(/(?:from\s*|import\s*\()\s*['"](\.[^'"]+)['"]/g))await copy(resolve(dirname(path),match[1]));
}
await copy(join(source,'core/world.mjs'));await copy(join(source,'io/bundle.mjs'));await copy(join(source,'assets/terrain-stamps/atlas.bin'));
await writeFile(join(target,'provenance.json'),JSON.stringify({source:'https://github.com/X111Metafizik/ladir',commit:'34841d6abc1c1c12251c2ace18ffb853895db16f',purpose:'Unmodified canonical authoring generator. Gameplay uses the independently parity-tested C# sampler.',files:records},null,2));
console.log(JSON.stringify({files:records.length,bytes:records.length,target}));
