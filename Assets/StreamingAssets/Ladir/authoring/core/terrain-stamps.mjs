import {clamp,dot,cross,normalize,localDirection} from './math.mjs';
import {STAMP_CATALOG} from './stamp-catalog.mjs';
import {StampMemo} from './stamp-cache.mjs';
export const globalStamps=world=>world.generationConfig.stampModel==='global-2';
const states=new WeakMap();
const hash=(x,y,z,s)=>{let h=Math.imul(x,374761393)^Math.imul(y,668265263)^Math.imul(z,2147483647)^s;h=Math.imul(h^(h>>>13),1274126177);return ((h^(h>>>16))>>>0)/4294967296;};
export const smoother=t=>{t=clamp(t);return t*t*t*(t*(t*6-15)+10);};
// Circular, C2-continuous support: height, slope and curvature reach the base
// together. No square texture boundary ever becomes a terrain boundary.
export const stampWeight=r=>1-smoother((r-.48)/.52);
function descriptor(world,d,index,angle,radius,stretch=1){
 const axis=Math.abs(d[1])>.95?[1,0,0]:[0,1,0],e=normalize(cross(axis,d)),n=cross(d,e),c=Math.cos(angle),s=Math.sin(angle);
 return {d,index,e:e.map((v,k)=>v*c+n[k]*s),n:n.map((v,k)=>v*c-e[k]*s),radius,radiusY:radius*stretch};
}
export function stampSites(world){
 if((world.generationConfig.stampLayers??1)>1&&world.buffers?.terrainHeightMeters)return distributedSites(world);
 const k=Math.min(1,world.radiusMeters/2000000),points=[[-28000,40000],[26000,40000],[-36000,-25000],[26000,-30000]];
 return points.map(([x,y],index)=>{const d=localDirection(world,x*k/world.radiusMeters,y*k/world.radiusMeters),a=hash(index,7,19,world.terrainSeed)*Math.PI*2,r=(index===2?12000:16000)*k;
  const t=descriptor(world,d,index,a,r,.9+index*.035),[u,v]=STAMP_CATALOG[index].focusUV;
  const direction=normalize(d.map((q,j)=>q+((u*2-1)*t.radius*t.e[j]+(v*2-1)*t.radiusY*t.n[j])/world.radiusMeters));
  return {...t,id:'stamp-'+index,name:STAMP_CATALOG[index].label+' · GLB',direction,rangeMeters:r*1.35,tilt:1.04};});
}
function state(world){
 let s=states.get(world);if(s)return s;const bytes=world.buffers?.stampHeights;if(!bytes?.length)return null;
 const data=new Uint16Array(bytes.buffer,bytes.byteOffset,bytes.byteLength/2);
 const layered=(world.generationConfig.stampLayers??1)>1;
 s={data,memoEnabled:true,cells:new Map(),sites:layered?[]:stampSites(world),spacing:Math.min(42000,world.radiusMeters*.025),bands:[220000,22000,700].map((meters,i)=>({spacing:Math.min(meters,world.radiusMeters*(globalStamps(world)?[.25,.08,.02]:[.02,.002,.0001])[i]),seed:world.terrainSeed+1709+i*173,cells:new Map(),descriptors:new Map(),memo:new StampMemo()}))};states.set(world,s);return s;
}
// A rounded 3-D lattice has a nearest center within sqrt(3)/2 cells.
// Support 1.08 is larger than that and smaller than the 1.5-cell search edge:
// every surface point is covered and entries outside the 27-cell neighborhood
// have exactly zero weight. Normalized C2 weights remove square/cell boundaries.
function bandCells(world,band,x,y,z){
 const key=x+','+y+','+z;let list=band.cells.get(key);if(list)return list;
 list=[];const R=world.radiusMeters/band.spacing;
 for(let k=-1;k<=1;k++)for(let j=-1;j<=1;j++)for(let i=-1;i<=1;i++){
  const a=x+i,b=y+j,c=z+k,len=Math.hypot(a,b,c);if(!len||Math.abs(len-R)>=1.08)continue;
  const index=Math.min(3,Math.floor(hash(a,b,c,band.seed)*4));
  const id=a+','+b+','+c;let t=band.descriptors.get(id);
  if(!t){t={...descriptor(world,[a/len,b/len,c/len],index,hash(a,b,c,band.seed+11)*Math.PI*2,band.spacing*1.08),x:a,y:b,z:c};band.descriptors.set(id,t);if(band.descriptors.size>2048)band.descriptors.delete(band.descriptors.keys().next().value);}list.push(t);
 }
 band.cells.set(key,list);if(band.cells.size>512)band.cells.delete(band.cells.keys().next().value);return list;
}
export function stampBand(world,d,index){
 const s=state(world);if(!s)return {value:0,weight:0};const band=s.bands[index],f=world.radiusMeters/band.spacing,x=d[0]*f,y=d[1]*f,z=d[2]*f;
 const cached=s.memoEnabled?band.memo.get(d):null;if(cached)return cached;
 const list=bandCells(world,band,Math.round(x),Math.round(y),Math.round(z));let sum=0,total=0;
 for(const t of list){const dx=x-t.x,dy=y-t.y,dz=z-t.z,r=Math.hypot(dx,dy,dz)/1.08;if(r>=1)continue;
  const weight=stampWeight(r),u=(dx*t.e[0]+dy*t.e[1]+dz*t.e[2])/2.16+.5,v=(dx*t.n[0]+dy*t.n[1]+dz*t.n[2])/2.16+.5;
  sum+=(sampleStamp(s.data,t.index,u,v)-STAMP_CATALOG[t.index].mean)*weight;total+=weight;
 }
 const value=total>1e-12?sum/total:0;return s.memoEnabled?band.memo.put(d,value,total):{value,weight:total};
}
export function stampDetail(world,d){
 if((world.generationConfig.stampLayers??1)<3||!(world.generationConfig.terrainStamps>0)||!world.buffers?.stampHeights)return 0;
 return stampBand(world,d,2).value*85*world.generationConfig.terrainStamps*Math.min(1,world.radiusMeters/2000000);
}
function distributedSites(world){
 const s=state(world),band=s.bands[1],R=world.radiusMeters,out=[],heights=world.buffers.terrainHeightMeters,dirs=world.buffers.directions;
 for(let index=0;index<4;index++){
  let best=null;
  for(let i=0;i<heights.length;i+=3){if(heights[i]<world.generationConfig.seaLevel+400)continue;
   const d=Array.from(dirs.subarray(i*3,i*3+3));if(out.some(t=>dot(t.direction,d)>.80))continue;
   const score=heights[i]+hash(i,index,31,world.terrainSeed)*150;if(best&&score<=best.score)continue;
   const f=R/band.spacing,list=bandCells(world,band,...d.map(v=>Math.round(v*f))),t=list.filter(t=>t.index===index).sort((a,b)=>dot(b.d,d)-dot(a.d,d))[0];if(!t)continue;
   const [u,v]=STAMP_CATALOG[index].focusUV,direction=normalize(t.d.map((q,k)=>q+((u*2-1)*t.radius*t.e[k]+(v*2-1)*t.radiusY*t.n[k])/R));
   best={...t,score,direction,id:'stamp-'+index,name:STAMP_CATALOG[index].label+' · GLB',rangeMeters:globalStamps(world)?band.spacing*1.35:18000,tilt:1.04};
  }
  if(best)out.push(best);
 }
 return out;
}
function cellStamps(world,s,x,y,z){
 const key=x+','+y+','+z;let list=s.cells.get(key);if(list)return list;
 list=[];const R=world.radiusMeters/s.spacing,seed=world.terrainSeed,type=world.generationConfig.worldType;
 for(let k=-1;k<=1;k++)for(let j=-1;j<=1;j++)for(let i=-1;i<=1;i++){
  const a=x+i,b=y+j,c=z+k,px=a+.25+hash(a,b,c,seed)*.5,py=b+.25+hash(a,b,c,seed+3)*.5,pz=c+.25+hash(a,b,c,seed+7)*.5,len=Math.hypot(px,py,pz);
  if(Math.abs(len-R)>.38)continue;
  const q=hash(a,b,c,seed+11);let index=Math.min(3,Math.floor(q*4));if(['mars','desert','volcanic','moon'].includes(type))index=q<.7?3:2;
  list.push(descriptor(world,[px/len,py/len,pz/len],index,hash(a,b,c,seed+17)*Math.PI*2,s.spacing*(.48+hash(a,b,c,seed+21)*.14),.86+hash(a,b,c,seed+29)*.14));
 }
 s.cells.set(key,list);if(s.cells.size>2048)s.cells.delete(s.cells.keys().next().value);return list;
}
// Positive cubic B-spline weights preserve C2 derivatives without Catmull-Rom
// overshoot/needle peaks. The 16-bit source is never sampled as a color texture.
export function sampleStamp(data,index,u,v){
 const m=STAMP_CATALOG[index],N=m.width,x=clamp(u)*(N-1),y=clamp(v)*(N-1),ix=Math.floor(x),iy=Math.floor(y),tx=x-ix,ty=y-iy;
 const a=1-tx,b=1-ty,w0=a*a*a/6,w1=(3*tx**3-6*tx*tx+4)/6,w2=(-3*tx**3+3*tx*tx+3*tx+1)/6,w3=tx**3/6;
 const x0=Math.max(0,ix-1),x1=ix,x2=Math.min(N-1,ix+1),x3=Math.min(N-1,ix+2);let height=0;
 for(let j=0;j<4;j++){const row=m.offset+clamp(iy+j-1,0,N-1)*N,w=j===0?b*b*b/6:j===1?(3*ty**3-6*ty*ty+4)/6:j===2?(-3*ty**3+3*ty*ty+3*ty+1)/6:ty**3/6;height+=(data[row+x0]*w0+data[row+x1]*w1+data[row+x2]*w2+data[row+x3]*w3)*w;}return height/65535;
}
function blend(world,s,d,list,base){
 let sum=0,total=0,uncovered=1;const R=world.radiusMeters;
 for(const t of list){if(dot(d,t.d)<.98)continue;const u=dot(d,t.e)*R/t.radius,v=dot(d,t.n)*R/t.radiusY,r=Math.hypot(u,v);if(r>=1)continue;
  const w=stampWeight(r),h=sampleStamp(s.data,t.index,u*.5+.5,v*.5+.5);sum+=(350+h*3400)*w;total+=w;uncovered*=1-w;
 }
 return total>1e-12?base+(sum/total-base)*(1-uncovered):base;
}
export function stampedRelief(world,d,base){
 const strength=world.generationConfig.terrainStamps??0;if(strength<=0||!world.buffers?.stampHeights)return base;
 if((world.generationConfig.stampLayers??1)>1){const k=Math.min(1,world.radiusMeters/2000000),relief=base*.40+1000+(stampBand(world,d,0).value*2800+stampBand(world,d,1).value*5600)*k;return base+(relief-base)*strength;}
 const s=state(world),f=world.radiusMeters/s.spacing;
 const list=cellStamps(world,s,Math.floor(d[0]*f),Math.floor(d[1]*f),Math.floor(d[2]*f));
 const global=blend(world,s,d,list,base),hero=blend(world,s,d,s.sites,global);return base+(hero-base)*strength;
}
export function setStampMemo(world,enabled){const s=state(world);if(s)s.memoEnabled=enabled;}
export function stampDiagnostics(world){const s=state(world);return {enabled:!!s,distribution:globalStamps(world)?'global-2':'legacy-1',bytes:world.buffers?.stampHeights?.byteLength??0,cachedCells:s?.cells.size??0,bands:s?.bands.map(b=>({spacingMeters:b.spacing,cells:b.cells.size,memoBytes:b.memo.bytes,hits:b.memo.hits,misses:b.memo.misses}))??[],maps:STAMP_CATALOG.map(m=>({id:m.id,width:m.width})),blend:'C2 circular support + positive cubic reconstruction; cube-face independent'};}
