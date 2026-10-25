import {normalize,dot,cross} from './math.mjs';
// A single 3D integer key welds all six faces, including their eight corners.
export const faceVector=(face,u,v)=>[
  [1,v,-u],[-1,v,u],[u,1,-v],[u,-1,v],[u,v,1],[-u,v,-1]
][face];
export const faceDirection=(face,u,v)=>normalize(faceVector(face,u,v));
export function directionFace(d){
  const [x,y,z]=d,a=[Math.abs(x),Math.abs(y),Math.abs(z)],m=Math.max(...a);
  if(m===a[0])return x>0?[0,-z/m,y/m]:[1,z/m,y/m];
  if(m===a[1])return y>0?[2,x/m,-z/m]:[3,x/m,z/m];
  return z>0?[4,x/m,y/m]:[5,-x/m,y/m];
}
// Project onto a specified face, even just beyond its seam. Ribbon clipping
// then shares the exact same crossing point on the neighboring face.
export function projectToFace(d,face){const [x,y,z]=d;const denominators=[x,-x,y,-y,z,-z],m=denominators[face];if(m<=0)return null;const uv=[[-z,y],[z,y],[x,-z],[x,z],[x,y],[-x,y]][face];return [face,uv[0]/m,uv[1]/m];}
export function cubeGraph(resolution,radius){
  const dirs=[],indices=[],keys=new Map(),neighbors=[],areas=[],faceNodeIds=[];
  for(let face=0;face<6;face++){
    const grid=[];
    for(let y=0;y<=resolution;y++)for(let x=0;x<=resolution;x++){
      // Face coordinates are generated at unit scale before canonical quantization.
      const p=faceVector(face,x/resolution*2-1,y/resolution*2-1),key=p.map(v=>Math.round(v*resolution)).join(',');
      let id=keys.get(key);
      if(id===undefined){id=dirs.length;keys.set(key,id);dirs.push(normalize(p));neighbors.push(new Set());areas.push(0);}
      grid.push(id);
    }
    faceNodeIds.push(...grid);
    for(let y=0;y<resolution;y++)for(let x=0;x<resolution;x++){
      const a=grid[y*(resolution+1)+x],b=grid[y*(resolution+1)+x+1],c=grid[(y+1)*(resolution+1)+x],d=grid[(y+1)*(resolution+1)+x+1];
      for(let tri of [[a,b,c],[b,d,c]]){
        const A=dirs[tri[0]],B=dirs[tri[1]],C=dirs[tri[2]];
        const normal=cross(B.map((v,i)=>v-A[i]),C.map((v,i)=>v-A[i]));
        if(dot(normal,A)<0)tri=[tri[0],tri[2],tri[1]];
        indices.push(...tri);
        const area=2*Math.atan2(Math.abs(dot(A,cross(B,C))),1+dot(A,B)+dot(B,C)+dot(C,A))*radius*radius;
        for(const id of tri)areas[id]+=area/3;
        for(let k=0;k<3;k++){neighbors[tri[k]].add(tri[(k+1)%3]);neighbors[tri[(k+1)%3]].add(tri[k]);}
      }
    }
  }
  const offsets=new Uint32Array(dirs.length+1),adj=[];
  neighbors.forEach((s,i)=>{offsets[i]=adj.length;adj.push(...[...s].sort((a,b)=>a-b));});offsets[dirs.length]=adj.length;
  return {directions:new Float64Array(dirs.flat()),indices:new Uint32Array(indices),offsets,neighbors:new Uint32Array(adj),areas:new Float64Array(areas),faceNodeIds:new Uint32Array(faceNodeIds)};
}
