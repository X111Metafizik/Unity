import {terrainSource} from './surface-source.mjs';
import {modernTerrain,referenceHeight,referenceWeight,regionalChannelHeight,referenceDetailFactor} from './landforms.mjs';
import {clamp,mix,smooth,fbm,dot,localCoordinates,localDirection} from './math.mjs';
import {directionFace} from './cube.mjs';
import {isDry,geology,forestCover} from './ecology.mjs';
import {mountainRelief,riftTerrain} from './relief.mjs';
import {sampleNetwork} from './river-network.mjs';
import {stampDetail} from './terrain-stamps.mjs';
export const BIOMES=['Okyanus','Kumsal','Çayır','Ilıman orman','İğne yapraklı orman','Kaya','Kar','Kurak bozkır','Göl / akarsu','Kum çölü','Sarp kayalık','Bazalt','Lav','Kızıl regolit','Ay regoliti','Mor biyom','Turkuaz biyom','Tuz düzlüğü'];
const localRiverCache=new WeakMap();
function localRivers(world){
  const source=world.hydrology.rivers;let cache=localRiverCache.get(world);
  if(!cache||cache.source!==source||cache.length!==source.length){cache={source,length:source.length,rivers:source.filter(r=>!r.network)};localRiverCache.set(world,cache);}
  return cache.rivers;
}
export function baseTerrain(world,d){
  if(isDry(world))return geology(world,d).height;
  // The fully constrained reference basin replaces this value exactly. Avoid
  // evaluating continental noise, stamps and erosion only to discard them.
  if(referenceWeight(world,d)===1)return referenceHeight(world,d,0);
  const c=world.generationConfig,s=world.terrainSeed,p=d;
  const warp=[fbm(p,2.5,s+21,3),fbm(p,2.5,s+31,3),fbm(p,2.5,s+41,3)];
  const q=p.map((v,i)=>v+warp[i]*.28);
  const continental=fbm(q,1.9,s,5)+.25*fbm(q,4,s+8,3);
  const [x,y,z]=localCoordinates(world,d);
  const home=z>0?Math.exp(-((x+.10)**2/.36+(y-.04)**2/.28)):0;
  const island=z>0?Math.exp(-((x-.83)**2/.18+(y-.02)**2/.62)):0;
  let land=continental+c.landBalance*.58-.25+home*.67+island*.34;
  const ridgeNoise=fbm(q,7.2,s+53,5);
  const belt=Math.pow(1-Math.abs(Math.sin(dot(q,world.ridgeAxis)*5.7+fbm(q,3,s+16,3)*4)),3.0);
  const ridge=Math.pow(Math.max(0,1-Math.abs(ridgeNoise*1.65)),3.2)*belt;
  const plain=1-smooth(.18,.68,fbm(q,3.4,s+86,3)+.4);
  let h=land*8500;
  if(h>0){
    h=h*.40*(1-plain*c.plains*.7)+ridge*c.mountains*2600*smooth(0,.12,land);
    h+=fbm(q,36,s+5,4)*450*(1-plain*.8)*(1-c.erosion*.5);
    h=6000*(1-Math.exp(-Math.max(0,h)/6000));
    // Preserve local relief instead of compressing every ridge into the same
    // elevated plateau. Valleys descend as well as summits rising.
    h+=(mountainRelief(world,d)-1050)*c.mountains*(1-plain*c.plains*.86)*smooth(0,900,h);
  }
  h=riftTerrain(world,d,h);
  // Two broad water basins and a measured narrow marine corridor.
  if(z>.3){
    const featureScale=120000/world.radiusMeters,fx=x/featureScale,fy=y/featureScale;
    const seaA=Math.hypot((fx-.68)/.28,(fy-.44)/.26);
    const seaB=Math.hypot((fx-.68)/.30,(fy+.42)/.31);
    const seaMask=1-smooth(.72,1.08,Math.min(seaA,seaB));
    h=mix(h,-2100,seaMask);
    if(fy>-.45&&fy<.47){
      const width=.024+.05*smooth(.02,.36,Math.abs(fy));
      const channel=1-smooth(width,width+.075,Math.abs(fx-(.68+.018*Math.sin(fy*9))));
      h=mix(h,c.seaLevel-180-700*smooth(.02,.4,Math.abs(fy)),channel);
    }
  }
  if(world.buffers?.erosionDelta){
    const [face,u,v]=directionFace(d),r=c.analysisResolution,fx=(u+1)*r*.5,fy=(v+1)*r*.5,ix=Math.min(r-1,Math.floor(fx)),iy=Math.min(r-1,Math.floor(fy)),tx=fx-ix,ty=fy-iy;
    const ids=world.buffers.faceNodeIds,a=face*(r+1)**2+iy*(r+1)+ix,delta=world.buffers.erosionDelta;
    const sx=x*world.radiusMeters/120000,sy=y*world.radiusMeters/120000;
    const protectedStrait=z>.3&&Math.abs(sx-(.68+.018*Math.sin(sy*9)))<.11&&Math.abs(sy)<.6;
    h+=mix(mix(delta[ids[a]],delta[ids[a+1]],tx),mix(delta[ids[a+r+1]],delta[ids[a+r+2]],tx),ty)*(protectedStrait?0:1);
  }
  return referenceHeight(world,d,h);
}
function channelSample(river,x,y,radius){
  const t=clamp((river.startY-y)/(river.startY-river.endY)),n=river.points.length-1,k=Math.min(n-1,Math.floor(t*n));
  let best=null;
  // Search the entire physical valley width. A fixed two-segment window made
  // the selected bank segment jump at every point on a meandering reach.
  const window=Math.ceil(4800/(radius*(river.startY-river.endY))*n)+2;
  for(let i=Math.max(0,k-window);i<=Math.min(n-1,k+window);i++){
    const a=river.points[i],b=river.points[i+1],vx=b.x-a.x,vy=b.y-a.y;
    const f=clamp(((x-a.x)*vx+(y-a.y)*vy)/(vx*vx+vy*vy));
    const dist=Math.hypot(x-mix(a.x,b.x,f),y-mix(a.y,b.y,f))*radius;
    if(!best||dist<best.distanceMeters)best={distanceMeters:dist,waterLevelMeters:mix(a.waterLevelMeters,b.waterLevelMeters,f),widthMeters:mix(a.widthMeters,b.widthMeters,f),depthMeters:mix(a.depthMeters,b.depthMeters,f),flow:river.flow,id:river.id,t:(i+f)/n};
  }
  return best;
}
export function nearestRiver(world,d){
  let best=sampleNetwork(world,d);const [x,y,z]=localCoordinates(world,d),R=world.radiusMeters;
  if(z>.3)for(const river of localRivers(world)){
    if(y>river.startY+4800/R||y<river.endY-4800/R)continue;
    const sample=channelSample(river,x,y,R);
    if(sample.distanceMeters<6000&&(!best||sample.distanceMeters<best.distanceMeters))best=sample;
  }
  return best;
}
export function lakeShoreRadius(world,lake,angle){
  const phase=(world.terrainSeed%997)/997*Math.PI*2;
  return lake.radiusMeters*(1+.12*Math.sin(angle*3+phase)+.065*Math.sin(angle*7-phase)+.035*Math.cos(angle*11+phase));
}
export function sampleTerrain(world,d,options={}){return terrainSource(world)?.(d,options)??sampleProceduralTerrain(world,d,options);}
export function sampleProceduralTerrain(world,d,{micro=true,microWeight=1}={}){
  const geo=isDry(world)?geology(world,d):null;
  let h=geo?geo.height:baseTerrain(world,d),water=isDry(world)?-100000:world.generationConfig.seaLevel,protectedMask=0,featureId=null,flow=0,riverDistance=1e12;
  const [x,y,z]=localCoordinates(world,d),radius=world.radiusMeters;
  if(z>.3){
    for(const river of localRivers(world)){
      if(y>river.startY+4800/radius||y<river.endY-4800/radius)continue;
      const r=channelSample(river,x,y,radius),half=r.widthMeters/2;
      if(r.distanceMeters<half+4200){
        const valley=1-smooth(half+100,half+4200,r.distanceMeters);
        h=Math.min(h,mix(h,r.waterLevelMeters+30+(r.distanceMeters-half)*.18,valley));
        const carve=1-smooth(half,half+130,r.distanceMeters);
        const isFord=river.id===world.crossings[0]?.riverId&&Math.abs(r.t-world.crossings[0].riverT)<.005;
        const bed=r.waterLevelMeters-(isFord?.55:r.depthMeters);
        h=Math.min(h,mix(h,bed+Math.pow(clamp(r.distanceMeters/half),4)*(isFord?.25:r.depthMeters*.45),carve));
        protectedMask=Math.max(protectedMask,1-smooth(half+130,half+260,r.distanceMeters));
        if(r.distanceMeters<half){water=Math.max(water,r.waterLevelMeters);featureId=r.id;flow=r.flow;}
        riverDistance=Math.min(riverDistance,r.distanceMeters);
      }
    }
    for(const lake of world.hydrology.lakes){
      const r=Math.hypot(x-lake.x,y-lake.y)*radius,shore=lakeShoreRadius(world,lake,Math.atan2(y-lake.y,x-lake.x));
      if(lake.regional){
        const weight=1-smooth(shore,shore+1800,r),target=lake.waterLevelMeters+ (r<shore?-lake.depthMeters*(1-(r/shore)**2):Math.min(200,(r-shore)*.18));
        h=mix(h,target,weight);protectedMask=Math.max(protectedMask,weight);
        if(r<shore){water=Math.max(water,lake.waterLevelMeters);featureId=lake.id;}
        continue;
      }
      const mask=1-smooth(shore*.92,shore*1.55,r);
      if(mask>0){
        h=mix(h,lake.waterLevelMeters-lake.depthMeters+Math.pow(clamp(r/shore),3)*lake.depthMeters*.65,mask);
        protectedMask=Math.max(protectedMask,mask);
        if(h<lake.waterLevelMeters){water=Math.max(water,lake.waterLevelMeters);featureId=lake.id;}
      }
    }
    const ford=world.crossings[0],main=world.hydrology.rivers[0];
    if(ford&&main){
      const p=main.points[Math.round(ford.riverT*(main.points.length-1))],along=Math.abs(y-p.y)*radius,across=Math.abs(x-p.x)*radius;
      if(along<160&&across<p.widthMeters*1.3){
        const r=channelSample(main,x,y,radius),half=r.widthMeters/2,weight=1-smooth(70,160,along);
        const fordBed=r.waterLevelMeters+(r.distanceMeters<half?-.55:.16*(r.distanceMeters-half)+.3);
        h=mix(h,fordBed,weight);protectedMask=Math.max(protectedMask,weight);
        if(r.distanceMeters<half){water=r.waterLevelMeters;featureId=main.id;}
      }
    }
  }
  const network=sampleNetwork(world,d);
  if(network){
    const priorHeight=h;
    const r=network,half=r.widthMeters/2,bank=r.id.startsWith('stream')?55:220;
    const valley=1-smooth(half+bank,half+2200,r.distanceMeters);
    h=Math.min(h,mix(h,r.waterLevelMeters+20+Math.max(0,r.distanceMeters-half)*.15,valley));
    const carve=1-smooth(half,half+bank,r.distanceMeters);
    h=Math.min(h,mix(h,r.waterLevelMeters-r.depthMeters+Math.pow(clamp(r.distanceMeters/half),4)*r.depthMeters*.4,carve));
    if(r.regional){const shaped=regionalChannelHeight(h,r);h=featureId?Math.min(priorHeight,shaped):shaped;}
    protectedMask=Math.max(protectedMask,1-smooth(half+bank,half+bank+80,r.distanceMeters));
    if(r.distanceMeters<half){water=Math.max(water,r.waterLevelMeters);featureId=r.id;flow=r.flow;}
    riverDistance=Math.min(riverDistance,r.distanceMeters);
  }
  const temperature=world.generationConfig.temperature-36*Math.abs(d[1])-Math.max(h,0)*.0055;
  const latitude=Math.abs(d[1]),dryBelt=Math.exp(-(((latitude-.40)/.19)**2));
  const moisture=clamp(.40+fbm(d,7.2,world.climateSeed,4)*1.1+world.generationConfig.rainfall*.32-dryBelt*.29+Math.exp(-riverDistance/1400)*.28);
  if(micro&&h>water)h+=(fbm(d,world.radiusMeters/900,world.terrainSeed+141,3)*7+stampDetail(world,d)*smooth(5,80,h-water)*(1-(geo?.lava??0)))*(1-protectedMask)*referenceDetailFactor(world,d)*microWeight;
  const snowLine=snowElevation(world,d);
  const snow=!isDry(world)&&h>water?smooth(snowLine-140,snowLine+260,h):0;
  let biome=h<=water?(featureId?8:0):h<water+22?1:snow>.55?6:h>3600?10:moisture>.59?(temperature<10?4:3):moisture<.29?9:moisture<.40?7:2;
  if(biome===9&&micro){const dune=1-Math.abs(Math.sin((d[0]+d[2]*.47)*radius/170+fbm(d,radius/5000,world.terrainSeed+340,3)*3));h+=dune*dune*12*(1-protectedMask)*microWeight*referenceDetailFactor(world,d);}
  let lava=0,albedo=1;
  const type=world.generationConfig.worldType;
  if(type==='moon'){biome=14;albedo=geo.albedo;}
  if(type==='mars'){biome=13;albedo=.65+.50*smooth(-100,1700,h);}
  if(type==='desert')biome=fbm(d,radius/17000,world.terrainSeed+310,4)>.03?10:9;
  if(type==='volcanic'){lava=geo.lava;biome=lava>.45?12:11;}
  if(type==='alien'&&biome!==0&&biome!==8&&biome!==6&&biome!==1)biome=fbm(d,18,world.climateSeed+76,3)>0?15:16;
  const sample={elevationMeters:h,waterSurfaceMeters:water,waterDepthMeters:Math.max(0,water-h),temperature,moisture,biome,protectedFeatureMask:protectedMask,featureId,flow,lava,albedo,snow,snowLineMeters:snowLine};
  sample.forest=forestCover(world,d,sample);return sample;
}
// A temperate snowline around 2.4 km, lower toward the poles and in cold
// presets. A 400 m transition and regional variation avoid a flat white stripe.
export function snowElevation(world,d){
 const c=world.generationConfig;
 return c.seaLevel+Math.max(450,2450+(c.temperature-30)*40-Math.abs(d[1])*1500)
  +fbm(d,world.radiusMeters/18000,world.climateSeed+811,3)*170;
}
export function surfaceNormal(world,d,epsilon=10/world.radiusMeters){
  const axis=Math.abs(d[1])>.95?[1,0,0]:[0,1,0];
  const cross=(a,b)=>[a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
  const norm=a=>{const l=Math.hypot(...a);return a.map(v=>v/l);};
  const e=norm(cross(axis,d)),n=cross(d,e);
  const pe=norm(d.map((v,i)=>v+e[i]*epsilon)),pn=norm(d.map((v,i)=>v+n[i]*epsilon));
  const h=world.radiusMeters+sampleTerrain(world,d).elevationMeters;
  const he=world.radiusMeters+sampleTerrain(world,pe).elevationMeters,hn=world.radiusMeters+sampleTerrain(world,pn).elevationMeters;
  return norm(cross(pe.map((v,i)=>v*he-d[i]*h),pn.map((v,i)=>v*hn-d[i]*h)));
}
const BIOME_COLORS=[[.012,.052,.085],[.48,.43,.29],[.10,.17,.065],[.065,.115,.036],[.052,.095,.054],[.30,.30,.275],[.75,.83,.85],[.40,.32,.17],[.018,.17,.19],[.57,.37,.17],[.34,.28,.23],[.045,.043,.048],[.46,.065,.008],[.39,.135,.065],[.28,.285,.29],[.24,.065,.33],[.065,.34,.29],[.72,.65,.49]];
export function biomeColor(s){
  const colors=BIOME_COLORS;
  const c=colors[s.biome===6&&s.snow!==undefined?5:s.biome].slice(),t=clamp((s.elevationMeters-1800)/2700)*.6;
  if(s.biome>1&&s.biome<8&&s.biome!==6)for(let i=0;i<3;i++)c[i]=mix(c[i],colors[5][i],t);
  for(let i=0;i<3;i++)c[i]*=(1-(s.forest??0)*.48)*(s.albedo??1);
  if(s.snow>0)for(let i=0;i<3;i++)c[i]=mix(c[i],colors[6][i],s.snow);
  return c;
}
export function makeFeatures(world){
  if(isDry(world)){world.hydrology={rivers:[],lakes:[],oceanId:null};world.crossings=[];world.straits=[];return;}
  const seedShift=(world.terrainSeed%1000)/1000,f=120000/world.radiusMeters;
  const river={id:'river-aurora',name:'Aurora Nehri',startY:.24*f,endY:-.69*f,flow:42,recipientId:'ocean-0',points:[]};
  for(let i=0;i<=512;i++){
    const t=i/512,x=(-.16+Math.sin(t*36+seedShift)*.026*Math.sin(t*Math.PI)+Math.sin(t*13+seedShift)*.045*t+.035*t)*f,y=mix(.24,-.69,t)*f,level=mix(1050,world.generationConfig.seaLevel-2,t);
    river.points.push({x,y,direction:localDirection(world,x,y),waterLevelMeters:level,bedLevelMeters:level-7-t*6,widthMeters:210+t*350,depthMeters:7+t*6,flow:42+t*80});
  }
  const join=river.points[228],tributary={id:'river-silver',name:'Gümüş Kol',startY:.36*f,endY:join.y,flow:18,recipientId:river.id,points:[]};
  for(let i=0;i<=192;i++){const t=i/192,x=mix(-.43*f,join.x,t)+Math.sin(t*25)*.022*f*Math.sin(t*Math.PI),y=mix(.36*f,join.y,t),level=mix(1800,join.waterLevelMeters,t);tributary.points.push({x,y,direction:localDirection(world,x,y),waterLevelMeters:level,bedLevelMeters:level-5,widthMeters:100+t*120,depthMeters:5,flow:18});}
  const source=river.points[0];
  world.hydrology={rivers:[river,tributary],lakes:[{id:'lake-lumen',name:'Lumen Gölü',x:source.x,y:source.y+.016*f,direction:localDirection(world,source.x,source.y+.016*f),radiusMeters:2450,waterLevelMeters:1050,depthMeters:130,outletId:river.id,closedBasin:false}],oceanId:'ocean-0'};
  const riverT=83/128,p=river.points[332],offset=p.widthMeters/world.radiusMeters*.95;
  world.crossings=[{id:'ford-aurora',name:'Aurora Sığ Geçidi',kind:'ford',riverId:river.id,riverT,direction:p.direction,waterLevelMeters:p.waterLevelMeters,maxDepthMeters:.55,widthMeters:p.widthMeters,approachSlope:.18,anchors:[localDirection(world,p.x-offset,p.y),localDirection(world,p.x+offset,p.y)],portalId:'portal-aurora',builtBridge:false}];
  world.straits=[{id:'strait-iris',name:'İris Boğazı',direction:localDirection(world,.68*f,0),minimumWidthMeters:120000*.048,minimumDepthMeters:180,entrance:localDirection(world,.68*f,.44*f),exit:localDirection(world,.68*f,-.42*f),centerline:Array.from({length:33},(_,i)=>localDirection(world,(.68+.018*Math.sin((i/32*.88-.44)*9))*f,(i/32*.88-.44)*f))}];
}
