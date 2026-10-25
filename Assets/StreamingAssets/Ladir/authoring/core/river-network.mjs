import {modernTerrain} from './landforms.mjs';
import {normalize,mix,clamp,cross,dot,distance,smooth} from './math.mjs';
import {directionFace,faceDirection} from './cube.mjs';
const caches=new WeakMap(),BINS=1024;
function key(d){const [f,u,v]=directionFace(d);return `${f}/${Math.min(BINS-1,Math.floor((u+1)*BINS/2))}/${Math.min(BINS-1,Math.floor((v+1)*BINS/2))}`;}
export function networkIndex(world){
 if(caches.get(world)?.count===world.hydrology.rivers.length)return caches.get(world).index;
 const index=new Map(),R=world.radiusMeters;
 for(const river of world.hydrology.rivers)if(river.network)for(let i=0;i<river.points.length-1;i++){
  const a=river.points[i],b=river.points[i+1],ax=a.direction[0],ay=a.direction[1],az=a.direction[2],dx=b.direction[0]-ax,dy=b.direction[1]-ay,dz=b.direction[2]-az,pad=(3200+Math.max(a.widthMeters,b.widthMeters)/2)/R;
  const segment={a,b,river,ax,ay,az,dx,dy,dz,inv:1/(dx*dx+dy*dy+dz*dz),minX:Math.min(ax,b.direction[0])-pad,maxX:Math.max(ax,b.direction[0])+pad,minY:Math.min(ay,b.direction[1])-pad,maxY:Math.max(ay,b.direction[1])+pad,minZ:Math.min(az,b.direction[2])-pad,maxZ:Math.max(az,b.direction[2])+pad},mid=normalize(a.direction.map((v,k)=>v+b.direction[k])),[f,u,v]=directionFace(mid);
  const extent=distance(a.direction,b.direction)*1.8+6000/R,step=2/BINS,seen=new Set();
  for(let y=-extent-step;y<=extent+step;y+=step)for(let x=-extent-step;x<=extent+step;x+=step){const k=key(faceDirection(f,u+x,v+y));if(seen.has(k))continue;seen.add(k);if(!index.has(k))index.set(k,[]);index.get(k).push(segment);}
 }
 caches.set(world,{index,count:world.hydrology.rivers.length});return index;
}
export function sampleNetwork(world,d){
 const entries=networkIndex(world).get(key(d));if(!entries)return null;let best=null;
 const x=d[0],y=d[1],z=d[2],R=world.radiusMeters;
 for(const seg of entries){
  if(x<seg.minX||x>seg.maxX||y<seg.minY||y>seg.maxY||z<seg.minZ||z>seg.maxZ)continue;
  const {a,b,river,ax,ay,az,dx,dy,dz,inv}=seg,t=clamp(((x-ax)*dx+(y-ay)*dy+(z-az)*dz)*inv);
  let px=ax+dx*t,py=ay+dy*t,pz=az+dz*t;const il=1/Math.sqrt(px*px+py*py+pz*pz);px=x-px*il;py=y-py*il;pz=z-pz*il;
  const dist=Math.sqrt(px*px+py*py+pz*pz)*R,width=mix(a.widthMeters,b.widthMeters,t),edge=dist-width/2;
  if(edge<3000&&(!best||edge<best.edge))best={edge,distanceMeters:dist,widthMeters:width,waterLevelMeters:mix(a.waterLevelMeters,b.waterLevelMeters,t),depthMeters:mix(a.depthMeters,b.depthMeters,t),regional:!!river.regional,valleyWidthMeters:mix(a.valleyWidthMeters??2800,b.valleyWidthMeters??2800,t),id:river.id,flow:river.flow};
 }
 return best;
}
export function segmentsForPatch(world,p){
 const result=new Set(),index=networkIndex(world),step=2/BINS;
 for(let y=p.v-step;y<=p.v+p.size+step;y+=step)for(let x=p.u-step;x<=p.u+p.size+step;x+=step)for(const seg of index.get(key(faceDirection(p.face,x,y)))??[])result.add(seg);
 // Legacy showcase reaches use the same precise ribbon renderer.
 for(const river of world.hydrology.rivers)if(!river.network)for(let i=0;i<river.points.length-1;i++)result.add({a:river.points[i],b:river.points[i+1],river});
 return result;
}
export function buildRiverNetwork(world,graph,hydro,height,baseSample){
 const R=world.radiusMeters,n=height.length,mean=graph.areas.reduce((a,b)=>a+b,0)/n,sea=world.generationConfig.seaLevel,active=new Uint8Array(n),up=new Uint16Array(n),owner=new Map(),rivers=[];
 for(let i=0;i<n;i++)active[i]=!hydro.ocean[i]&&height[i]>sea+25&&hydro.accumulation[i]>mean*.9?1:0;
 for(let i=0;i<n;i++)if(active[i]&&hydro.receiver[i]>=0&&active[hydro.receiver[i]])up[hydro.receiver[i]]++;
 const starts=Array.from({length:n},(_,i)=>i).filter(i=>active[i]&&up[i]!==1).sort((a,b)=>hydro.accumulation[b]-hydro.accumulation[a]);
 for(const start of starts){
  if(rivers.length>=Math.min(220,world.generationConfig.analysisResolution*5))break;
  const ids=[start];let next=hydro.receiver[start];
  while(next>=0){ids.push(next);if(!active[next]||up[next]!==1)break;next=hydro.receiver[next];}
  if(ids.length<2)continue;
  const river={id:`drain-${start}`,name:`Nehir ${rivers.length+1}`,network:true,flow:Math.round(hydro.accumulation[start]/mean*20),recipientId:'ocean-0',points:[],endNode:ids.at(-1)};
  let level=Math.max(sea,Math.min(height[start],hydro.drainage[start])-4);
  for(let j=0;j<ids.length-1;j++){
   const a=Array.from(graph.directions.subarray(ids[j]*3,ids[j]*3+3)),b=Array.from(graph.directions.subarray(ids[j+1]*3,ids[j+1]*3+3)),len=distance(a,b)*R,width=clamp(130+Math.sqrt(hydro.accumulation[ids[j]]/mean)*170,180,1600),wavelength=16000+width*10,steps=Math.max(4,Math.ceil(len/(wavelength/18))),side=normalize(cross(a,b));
   for(let k=0;k<steps;k++){
    const t=k/steps,along=t*len,envelope=smooth(0,wavelength,along)*smooth(0,wavelength,len-along);
    const phase=along/wavelength*Math.PI*2+(start%97)*.17;
    const meander=envelope*(Math.sin(phase)*wavelength*.18+Math.sin(phase*1.83+.6)*wavelength*.035)/R;
    const d=normalize(a.map((v,i)=>mix(v,b[i],t)+side[i]*meander));
    level=Math.max(sea,Math.min(level,baseSample(d)-3));
    river.points.push({direction:d,waterLevelMeters:level,widthMeters:width,depthMeters:8+width*.015});
   }
  }
  const end=ids.at(-1),d=Array.from(graph.directions.subarray(end*3,end*3+3));level=Math.max(sea,Math.min(level,height[end]-3));
  river.points.push({direction:d,waterLevelMeters:level,widthMeters:river.points.at(-1).widthMeters,depthMeters:15});owner.set(start,river);rivers.push(river);
 }
 // Connected reaches share exactly the same junction water level.
 for(const river of rivers){const receiver=owner.get(river.endNode);river.recipientId=receiver?.id??'ocean-0';if(receiver){const limit=Math.min(river.points.at(-1).waterLevelMeters,receiver.points[0].waterLevelMeters);for(const p of receiver.points)p.waterLevelMeters=Math.min(p.waterLevelMeters,limit);river.points.at(-1).waterLevelMeters=limit;}delete river.endNode;}
 // Small tributaries branch from distributed reaches, not a single showcase tile.
 const streams=[];
 for(const river of rivers.filter(r=>r.points.length>8)){
  if(streams.length>=80)break;
  const join=river.points.find((p,i)=>i>2&&p.waterLevelMeters>sea+100&&baseSample(p.direction)>p.waterLevelMeters+2);if(!join)continue;
  if(modernTerrain(world)){
   const east=normalize(cross(join.direction,[0,1,0])),north=normalize(cross(join.direction,east));let candidate=null;
   for(let attempt=0;attempt<8&&!candidate;attempt++){
    const angle=(river.flow%17)*.37+attempt*Math.PI/4,length=8000+(river.flow%9)*650,points=[];let level=join.waterLevelMeters,valid=true;
    for(let i=96;i>=0;i--){const t=i/96,along=(1-t)*length,bend=Math.sin(t*16)*500*Math.sin(t*Math.PI),dx=Math.cos(angle)*along-Math.sin(angle)*bend,dy=Math.sin(angle)*along+Math.cos(angle)*bend,d=normalize(join.direction.map((v,k)=>v+(east[k]*dx+north[k]*dy)/R));
     const ground=baseSample(d);if(i<96){if(ground<level+2||ground<sea+100){valid=false;break;}level=Math.max(level+.02,Math.min(ground-2,level+2));}
     points.push({direction:d,waterLevelMeters:level,widthMeters:18+t*46,depthMeters:2+t*2});
    }
    if(valid)candidate={id:`stream-${river.id}`,name:`Dere ${streams.length+1}`,network:true,stream:true,flow:3,recipientId:river.id,points:points.reverse()};
   }
   if(candidate)streams.push(candidate);continue;
  }
  const tangent=normalize(cross(join.direction,[0,1,0])),side=normalize(cross(join.direction,tangent)),points=[],length=16000+(river.flow%13)*1000;
  for(let i=0;i<=96;i++){const t=i/96,d=normalize(join.direction.map((v,k)=>v+tangent[k]*(1-t)*length/R+side[k]*(Math.sin(t*19)*1500+Math.sin(t*8)*600)*Math.sin(t*Math.PI)/R));points.push({direction:d,waterLevelMeters:join.waterLevelMeters+(1-t)*105,widthMeters:18+t*46,depthMeters:2+t*2});}
  if(points.some(p=>baseSample(p.direction)<p.waterLevelMeters+5))continue;
  streams.push({id:`stream-${river.id}`,name:`Dere ${streams.length+1}`,network:true,stream:true,flow:3,recipientId:river.id,points});
 }
 return [...rivers,...streams];
}

