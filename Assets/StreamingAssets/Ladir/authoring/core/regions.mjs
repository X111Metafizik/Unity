import {normalize,dot,randomStream} from './math.mjs';
export function generateRegions(world,count){
  const rng=randomStream(`${world.terrainSeed}/regions`),phase=rng()*Math.PI*2;
  return Array.from({length:count},(_,id)=>{const y=1-2*(id+.5)/count,r=Math.sqrt(1-y*y),theta=id*2.399963229728653+phase;return {id,direction:[r*Math.sin(theta),y,r*Math.cos(theta)],name:`Bölge ${String(id+1).padStart(2,'0')}`};});
}
export function bindRegions(world){
  const n=world.buffers.terrainHeightMeters.length,regions=world.regionBindings,regionId=new Uint32Array(n),links=regions.map(()=>new Set());
  for(let i=0;i<n;i++){
    const d=world.buffers.directions.subarray(i*3,i*3+3);let best=-Infinity,index=0;
    for(let j=0;j<regions.length;j++){const s=dot(d,regions[j].direction);if(s>best){best=s;index=j;}}
    regionId[i]=index;
  }
  for(let i=0;i<n;i++)for(let k=world.buffers.offsets[i];k<world.buffers.offsets[i+1];k++){const a=regionId[i],b=regionId[world.buffers.neighbors[k]];if(a!==b){links[a].add(b);links[b].add(a);}}
  for(let i=0;i<regions.length;i++)regions[i].neighbors=[...links[i]].sort((a,b)=>a-b).map(j=>regions[j].id);
  world.buffers.regionIndex=regionId;
}
