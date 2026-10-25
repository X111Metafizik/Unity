import {readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {pathToFileURL} from 'node:url';
const root=resolve(import.meta.dirname,'..');
const web=resolve(process.argv[2]??'../2026-10-20/files-mentioned-by-the-user-starfront/outputs/planet-studio');
const {randomStream}=await import(pathToFileURL(resolve(web,'core/math.mjs')));
const text=await readFile(resolve(web,'render/water.mjs'),'utf8');
const initialize=new Function('seed','randomStream',text.slice(text.indexOf('  const N=64'),text.indexOf('  const spectrum=storage'))+'; return {packed,frequencies};');
const reverse=n=>{let r=0;for(let i=0;i<6;i++){r=r*2+(n&1);n>>=1;}return r;};
const cases=[];
for(const seed of [0,123456789,4294967295]){
 const {packed,frequencies}=initialize(seed,randomStream);
 const spectrum=Array.from({length:128},(_,j)=>{const i=(j*313)%4096;return {i,p:Array.from(packed.slice(i*4,i*4+4)),frequency:frequencies[i]};});
 // Direct O(N^2) inverse DFT at selected spatial positions. No butterfly code
 // is shared with the C# or compute shader implementation being tested.
 const samples=[];
 for(const time of [0,.73,57.2])for(let sample=0;sample<24;sample++){
  const x=sample*17%64,y=sample*29%64;let total=0;
  for(let ky=0;ky<64;ky++)for(let kx=0;kx<64;kx++){
   const i=reverse(ky)*64+reverse(kx),q=i*4,a=frequencies[i]*time,c=Math.cos(a),s=Math.sin(a);
   const re=packed[q]*c-packed[q+1]*s+packed[q+2]*c+packed[q+3]*s,im=packed[q]*s+packed[q+1]*c-packed[q+2]*s+packed[q+3]*c;
   const phase=2*Math.PI*(x*kx+y*ky)/64;total+=re*Math.cos(phase)-im*Math.sin(phase);
  }
  samples.push({time,i:y*64+x,height:total/4096*22});
 }
 cases.push({seed,spectrum,samples});
}
await writeFile(resolve(root,'evidence/planet-port/ocean-fixtures.json'),JSON.stringify({source:'Original render/water.mjs initialization + independent direct inverse DFT',cases}));
console.log('Original ocean spectrum and 216 independent DFT samples exported.');
