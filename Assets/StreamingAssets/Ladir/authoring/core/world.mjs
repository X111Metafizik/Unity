import {LANDFORM_MODEL,installReferenceWatershed,referenceDirection} from './landforms.mjs';
import {hash32,randomStream,randomAt,normalize,cross,dot,localDirection,distance,stableStringify,sha256} from './math.mjs';
import {cubeGraph} from './cube.mjs';
import {sampleTerrain,baseTerrain,makeFeatures} from './terrain.mjs';
import {priorityFlood} from './hydrology.mjs';
import {generateRegions,bindRegions} from './regions.mjs';
import {GENERATOR_BUILD_HASH} from './build-id.mjs';
import {queryStaticNavigation} from './navigation.mjs';
import {isDry,WORLD_TYPES,presetLandmarks} from './ecology.mjs';
import {mountainRelief} from './relief.mjs';
import {buildRiverNetwork} from './river-network.mjs';
import {BIOMES} from './terrain.mjs';
import {loadStampHeights} from './stamp-library.mjs';
import {stampSites} from './terrain-stamps.mjs';
export const GENERATOR_VERSION='1.8.1';
export const DEFAULT_CONFIG={seed:'LUMEN-742',worldType:'terran',profile:'starfront-terran',radiusMeters:4000000,terrainModel:'watershed-2',landBalance:.48,mountains:.65,plains:.58,erosion:.5,terrainStamps:1,stampLayers:3,stampModel:'global-2',seaLevel:0,temperature:30,rainfall:.55,forestDensity:.65,analysisResolution:48,regionCount:48,mode:'new-planet'};
export const DEFAULT_APPEARANCE={sunAzimuth:35,sunElevation:42,exposure:1.1,atmosphere:.45,clouds:.22,fog:.10,water:1,wind:.6,paused:false,tintMode:0,tintOpacity:.28};
export const DEFAULT_RENDER={detail:1,volumeSteps:32,pixelRatio:1,shadowResolution:2048};
export function canonicalConfig(input={}){
  const result={...DEFAULT_CONFIG};
  for(const key of Object.keys(result))if(input[key]!==undefined)result[key]=input[key];
  if(!Object.hasOwn(WORLD_TYPES,result.worldType))throw Error('Bilinmeyen dünya türü.');
  if(!['legacy-1','global-2'].includes(result.stampModel))throw Error('Unknown stamp distribution');
  if(!Number.isInteger(result.stampLayers)||result.stampLayers<1||result.stampLayers>3)throw Error('Arazi katman sayısı 1–3 olmalı.');
  if(!['legacy-1.6',LANDFORM_MODEL].includes(result.terrainModel))throw Error('Unknown terrain generator');
  result.seed=String(result.seed).normalize('NFC');
  for(const key of ['landBalance','mountains','plains','erosion','terrainStamps','rainfall','forestDensity'])if(!Number.isFinite(result[key])||result[key]<0||result[key]>1)throw Error(`Geçersiz üretim ayarı: ${key}`);
  if(!Number.isFinite(result.seaLevel)||result.seaLevel < -300||result.seaLevel>500)throw Error('Deniz seviyesi −300–500 m olmalı.');
  if(!Number.isFinite(result.radiusMeters)||result.radiusMeters<50000||result.radiusMeters>20000000)throw Error('Yarıçap 50–20.000 km olmalı.');
  if(!Number.isInteger(result.analysisResolution)||result.analysisResolution<16||result.analysisResolution>256)throw Error('Analiz çözünürlüğü 16–256 olmalı.');
  if(!Number.isFinite(result.temperature)||result.temperature<0||result.temperature>50)throw Error('Sıcaklık 0–50 °C olmalı.');
  if(!Number.isInteger(result.regionCount)||result.regionCount<4||result.regionCount>1024)throw Error('Bölge sayısı 4–1024 olmalı.');
  if(!['new-planet','existing-layout'].includes(result.mode))throw Error('Bilinmeyen üretim modu.');
  return result;
}
export async function worldHash(world){
  const manifest={...world.manifest};delete manifest.worldDataHash;delete manifest.bufferDescriptors;
  const metadata={...(world.landforms?{landforms:world.landforms}:{}),manifest,generationConfig:world.generationConfig,radiusMeters:world.radiusMeters,frame:world.frame,terrainSeed:world.terrainSeed,climateSeed:world.climateSeed,vegetationSeed:world.vegetationSeed,ridgeAxis:world.ridgeAxis,hydrology:world.hydrology,landmarks:world.landmarks,crossings:world.crossings,straits:world.straits,regionBindings:world.regionBindings,navigation:world.navigation};
  const chunks=[new TextEncoder().encode(stableStringify(metadata))];
  for(const key of Object.keys(world.buffers).sort()){
    const array=world.buffers[key],view=new DataView(new ArrayBuffer(array.byteLength));
    const setter={Float64Array:'setFloat64',Float32Array:'setFloat32',Int32Array:'setInt32',Uint32Array:'setUint32',Uint8Array:'setUint8'}[array.constructor.name];
    if(!setter)throw Error('Desteklenmeyen sayısal alan.');
    for(let i=0;i<array.length;i++)view[setter](i*array.BYTES_PER_ELEMENT,array[i],true);
    chunks.push(new TextEncoder().encode(`${key}:${array.constructor.name}:${array.length};`),new Uint8Array(view.buffer));
  }
  const all=new Uint8Array(chunks.reduce((a,b)=>a+b.length,0));let offset=0;for(const c of chunks){all.set(c,offset);offset+=c.length;}
  return sha256(all);
}
export async function generatePlanet(input={}, {signal,onProgress=()=>{},importedLayout=null}={}){
  const config=canonicalConfig(input);
  const checkpoint=(label,value)=>{if(signal?.aborted)throw new DOMException('Üretim iptal edildi.','AbortError');onProgress({label,value});};
  checkpoint('Tohum ve koordinatlar',.02);
  // Keep the existing continental seed namespace; the new generator adds relief.
  const root=hash32(`${config.seed}/1.4.0`),rng=randomStream(`${root}/geography-attempt/0`);
  const angle=(rng()-.5)*.6,center=[Math.sin(angle),.08,Math.cos(angle)],normal=normalize(center),east=normalize(cross([0,1,0],normal)),north=cross(normal,east);
  const configHash=await sha256(new TextEncoder().encode(stableStringify(config)));
  const world={radiusMeters:config.radiusMeters,generationConfig:config,frame:{center:normal,east,north,northAxis:[0,1,0],handedness:'right',metersPerGameUnit:config.radiusMeters},terrainSeed:hash32(`${root}/terrain/1`),climateSeed:hash32(`${root}/climate/1`),vegetationSeed:hash32(`${root}/vegetation/1`),ridgeAxis:normalize([rng(),rng()*.6,rng()-.5]),hydrology:{rivers:[],lakes:[]},crossings:[],straits:[],regionBindings:[],navigation:{portals:[]},buffers:{}};
  makeFeatures(world);
  installReferenceWatershed(world);
  if(config.terrainStamps>0){checkpoint('GLB arazi yükseklikleri',.06);world.buffers.stampHeights=await loadStampHeights();}
  checkpoint('Kıta, sıradağ ve ova alanları',.10);
  const graph=cubeGraph(config.analysisResolution,config.radiusMeters),n=graph.areas.length;
  const terrainHeightMeters=new Float64Array(n),rainfall=new Float32Array(n),moisture=new Float32Array(n),temperature=new Float32Array(n),biome=new Uint8Array(n),waterSurfaceMeters=new Float64Array(n),protectedFeatureMask=new Float32Array(n);
  for(let i=0;i<n;i++)terrainHeightMeters[i]=baseTerrain(world,Array.from(graph.directions.subarray(i*3,i*3+3)));
  checkpoint('Termal aşınma',.27);
  const original=terrainHeightMeters.slice();
  for(let iteration=0;iteration<5;iteration++){
    const delta=new Float64Array(n);
    for(let i=0;i<n;i++){
      if(terrainHeightMeters[i]<0)continue;
      let low=i;
      for(let k=graph.offsets[i];k<graph.offsets[i+1];k++){const j=graph.neighbors[k];if(terrainHeightMeters[j]<terrainHeightMeters[low])low=j;}
      const difference=terrainHeightMeters[i]-terrainHeightMeters[low],transfer=Math.max(0,difference-450)*config.erosion*.11;
      delta[i]-=transfer;delta[low]+=transfer*graph.areas[i]/graph.areas[low];
    }
    for(let i=0;i<n;i++)terrainHeightMeters[i]+=delta[i];
  }
  world.buffers={...world.buffers,...graph,erosionDelta:new Float32Array(Array.from(terrainHeightMeters,(h,i)=>h-original[i]))};
  checkpoint('Göl çanakları, nehir yatakları ve iklim',.40);
  for(let i=0;i<n;i++){
    const sample=sampleTerrain(world,Array.from(graph.directions.subarray(i*3,i*3+3)));
    terrainHeightMeters[i]=sample.elevationMeters;waterSurfaceMeters[i]=sample.waterSurfaceMeters;rainfall[i]=sample.moisture;
    moisture[i]=sample.moisture;temperature[i]=sample.temperature;biome[i]=sample.biome;protectedFeatureMask[i]=sample.protectedFeatureMask;
  }
  checkpoint('Küresel Priority-Flood ve akış birikimi',.57);
  const hydro=priorityFlood(graph,terrainHeightMeters,rainfall,config.seaLevel,isDry(world));
  Object.assign(world.buffers,{terrainHeightMeters,waterSurfaceMeters,rainfall,moisture,temperature,biome,protectedFeatureMask,drainageHeightMeters:hydro.drainage,receiver:hydro.receiver,floodOrder:hydro.order,accumulation:hydro.accumulation,basin:hydro.basin,ocean:hydro.ocean});
  if(!isDry(world))world.hydrology.rivers.push(...buildRiverNetwork(world,graph,hydro,terrainHeightMeters,d=>baseTerrain(world,d)));
  world.landmarks=presetLandmarks(world);
  if(world.landforms)world.landmarks.push({id:'reference',name:'32 km · Bağlantılı havza',direction:referenceDirection(world,0,0),rangeMeters:27000,tilt:.65},{id:'reference-canyon',name:'Referans kanyon',direction:referenceDirection(world,8400,-5000),rangeMeters:4500,tilt:.8});
  if(config.terrainStamps>0)world.landmarks.push(...stampSites(world).map(({id,name,direction,rangeMeters,tilt})=>({id,name,direction,rangeMeters,tilt})));
  if(['terran','alien','desert','mars'].includes(config.worldType))world.landmarks.push({id:'rift',name:'Büyük yarık · 1–2 km',direction:localDirection(world,-70000/world.radiusMeters,20000/world.radiusMeters),rangeMeters:22000,tilt:.55});
  const candidates=new Map();
  for(let i=0;i<n;i++){
    const d=Array.from(graph.directions.subarray(i*3,i*3+3)),s=sampleTerrain(world,d);
    terrainHeightMeters[i]=s.elevationMeters;waterSurfaceMeters[i]=s.waterSurfaceMeters;biome[i]=s.biome;protectedFeatureMask[i]=s.protectedFeatureMask;
    if(![0,1,8].includes(s.biome)&&(!candidates.has(s.biome)||dot(d,world.frame.center)>candidates.get(s.biome).score))candidates.set(s.biome,{id:'biome-'+s.biome,name:BIOMES[s.biome],direction:d,rangeMeters:9000,score:dot(d,world.frame.center)});
    if(s.forest>.50&&dot(d,world.frame.center)>.65){const old=world.landmarks.find(p=>p.id==='forest');if(!old)world.landmarks.push({id:'forest',name:'Orman ve açıklıklar',direction:d,rangeMeters:1400});else if(dot(d,world.frame.center)>dot(old.direction,world.frame.center))old.direction=d;}
  }
  // Locate actual contrasting ridges and valley floors at a local physical scale.
  if(config.worldType!=='moon'){
    let best=null;
    for(let y=-4;y<=4;y++)for(let x=-4;x<=4;x++){
      if(Math.hypot(x,y)<1.5)continue;
      const d=localDirection(world,x*17000/world.radiusMeters,y*17000/world.radiusMeters),h=sampleTerrain(world,d).elevationMeters,peak=mountainRelief(world,d);
      if(h<config.seaLevel+700)continue;
      let valley=null;
      for(let k=0;k<8;k++){const a=k*Math.PI/4,q=localDirection(world,(x*17000+Math.cos(a)*6000)/world.radiusMeters,(y*17000+Math.sin(a)*6000)/world.radiusMeters),v=sampleTerrain(world,q);if(v.waterDepthMeters===0&&v.elevationMeters>config.seaLevel+300&&(!valley||v.elevationMeters<valley.h))valley={d:q,h:v.elevationMeters,relief:mountainRelief(world,q)};}
      const score=valley?(peak-valley.relief)*.8+(h-valley.h)*.2:0;
      if(valley&&h-valley.h>300&&score>(best?.score??400))best={d,valley,score};
    }
    if(!best&&world.landforms){
      const d=referenceDirection(world,-11000,10000),h=sampleTerrain(world,d).elevationMeters;
      for(let k=0;k<16;k++){const a=k*Math.PI/8,q=referenceDirection(world,-11000+Math.cos(a)*6000,10000+Math.sin(a)*6000),v=sampleTerrain(world,q),score=h-v.elevationMeters;if(v.waterDepthMeters===0&&score>(best?.score??300))best={d,valley:{d:q,h:v.elevationMeters},score};}
    }
    if(best)world.landmarks.push({id:'ridges',name:'Ayrık dağ sırtları',direction:best.d,rangeMeters:8500,tilt:.95},{id:'valleys',name:'Derin vadiler',direction:best.valley.d,rangeMeters:7500,tilt:.45});
  }
  world.landmarks.push(...candidates.values());
  if(world.landforms){
    const finalDrainage=priorityFlood(graph,terrainHeightMeters,rainfall,config.seaLevel,isDry(world));
    Object.assign(world.buffers,{drainageHeightMeters:finalDrainage.drainage,receiver:finalDrainage.receiver,floodOrder:finalDrainage.order,accumulation:finalDrainage.accumulation,basin:finalDrainage.basin,ocean:finalDrainage.ocean});
  }
  const stream=world.hydrology.rivers.filter(r=>r.stream).sort((a,b)=>dot(b.points[16].direction,world.frame.center)-dot(a.points[16].direction,world.frame.center))[0],river=world.hydrology.rivers.find(r=>r.network&&!r.stream&&dot(r.points[Math.floor(r.points.length/2)].direction,world.frame.center)>.5);
  for(const r of [river,stream].filter(Boolean))world.landmarks.push({id:'visit-'+r.id,name:r.stream?'Dere vadisi':'Küresel nehir ağı',direction:r.points[Math.floor(r.points.length*.6)].direction,rangeMeters:r.stream?1800:12000});
  checkpoint('Bitki toplulukları ve kaya dağılımı',.71);
  const vegetation=[],species=[],rocks=[],vegetationIds=[];
  for(let i=0;i<n;i++){
    if(terrainHeightMeters[i]<=waterSurfaceMeters[i]||protectedFeatureMask[i]>.2)continue;
    const d=Array.from(graph.directions.subarray(i*3,i*3+3));
    for(let j=0;j<8;j++){
      const id=i*8+j,r=randomAt(world.vegetationSeed,id);
      if(isDry(world)||r>config.forestDensity*moisture[i]||![2,3,4,15,16].includes(biome[i]))continue;
      const tangent=normalize(cross(Math.abs(d[1])>.95?[1,0,0]:[0,1,0],d)),bitangent=cross(d,tangent),spread=1/config.analysisResolution*.5;
      const a=(randomAt(world.vegetationSeed+10,id)-.5)*spread,b=(randomAt(world.vegetationSeed+20,id)-.5)*spread,p=normalize(d.map((v,k)=>v+a*tangent[k]+b*bitangent[k])),sample=sampleTerrain(world,p);
      if(sample.waterDepthMeters>0||sample.protectedFeatureMask>.15||sample.forest<.15)continue;
      vegetation.push(...p,12+randomAt(world.vegetationSeed+40,id)*19);species.push(sample.biome===4?0:Math.floor(randomAt(world.vegetationSeed+30,id)*3));vegetationIds.push(id);
    }
    if(randomAt(world.vegetationSeed+900,i)<.15)rocks.push(...d,8+randomAt(world.vegetationSeed+1000,i)*32);
  }
  Object.assign(world.buffers,{vegetation:new Float32Array(vegetation),vegetationSpecies:new Uint8Array(species),vegetationIds:new Uint32Array(vegetationIds),rocks:new Float32Array(rocks)});
  world.navigation={portals:world.crossings.map(c=>({id:c.portalId,kind:c.kind,anchors:c.anchors,crossingId:c.id})),landMask:new Uint8Array(n),graphNodeDomain:'cube-vertex-index',riverBarriers:world.hydrology.rivers.map(r=>r.id)};
  for(let i=0;i<n;i++)world.navigation.landMask[i]=terrainHeightMeters[i]>waterSurfaceMeters[i]?1:0;
  world.buffers.landMask=world.navigation.landMask;delete world.navigation.landMask;
  if(config.mode==='existing-layout'){
    if(!importedLayout?.cells?.length)throw Error('Mevcut düzen için cells, frame ve korunan alanlar gerekli.');
    world.regionBindings=importedLayout.cells.map(c=>({id:c.id,direction:c.direction,polygon:c.polygon??[]}));
    for(const constraint of importedLayout.protectedSamples??[]){
      const s=sampleTerrain(world,constraint.direction);
      if((s.waterDepthMeters>0)!==constraint.water)throw Error(`Korunan kıyı çatışması: ${constraint.id}. Düzen değiştirilmedi.`);
    }
  }
  else world.regionBindings=generateRegions(world,config.regionCount);
  bindRegions(world);
  world.manifest={schemaVersion:1,generatorVersion:GENERATOR_VERSION,generatorBuildHash:GENERATOR_BUILD_HASH,normalizedSeed:config.seed,seed:config.seed,generationAttempt:0,configHash,layoutHash:await sha256(new TextEncoder().encode(stableStringify(importedLayout??null))),coordinateSystem:'planet-local-unit-direction',radiusMeters:config.radiusMeters,frame:world.frame,generationConfig:config,visualAssetVersion:1,numericEncoding:'IEEE754/integer little-endian; sorted buffer names; canonical JSON sorted keys; no timestamp/hash field'};
  checkpoint('Coğrafya doğrulaması ve dünya imzası',.90);
  world.generationReport=validatePlanet(world);
  if(!world.generationReport.valid)throw Error(world.generationReport.errors.join(' / '));
  world.manifest.worldDataHash=await worldHash(world);
  checkpoint('Gezegen hazır',1);
  return world;
}
export function validatePlanet(world){
  const errors=[],b=world.buffers,n=b.terrainHeightMeters.length,rank=new Int32Array(n);
  for(let i=0;i<n;i++)rank[b.floodOrder[i]]=i;
  for(let i=0;i<n;i++){
    if(!Number.isFinite(b.terrainHeightMeters[i])){errors.push('Sonlu olmayan yükseklik');break;}
    const r=b.receiver[i];if(r>=0&&(rank[r]>=rank[i]||b.drainageHeightMeters[r]>b.drainageHeightMeters[i]+1e-8)){errors.push('Drenaj döngüsü veya ters eğim');break;}
  }
  for(const river of world.hydrology.rivers){
    for(let i=1;i<river.points.length;i++)if(river.points[i].waterLevelMeters>river.points[i-1].waterLevelMeters+1e-6)errors.push(`${river.id}: ters su eğimi`);
    if(![world.hydrology.oceanId,...world.hydrology.lakes.map(l=>l.id),...world.hydrology.rivers.map(r=>r.id)].includes(river.recipientId))errors.push(`${river.id}: alıcı yok`);
    for(const p of river.points){const s=sampleTerrain(world,p.direction);if(s.elevationMeters>s.waterSurfaceMeters+.2) {errors.push(`${river.id}: kuru kanal (${s.elevationMeters.toFixed(1)} / ${s.waterSurfaceMeters.toFixed(1)} at ${river.points.indexOf(p)}; ${s.featureId})`);break;}}
  }
  for(const lake of world.hydrology.lakes){if(sampleTerrain(world,lake.direction).elevationMeters>=lake.waterLevelMeters)errors.push(`${lake.id}: Göl çanağı su kotunun üzerinde (${sampleTerrain(world,lake.direction).elevationMeters})`);}
  for(const strait of world.straits)for(const d of strait.centerline)if(sampleTerrain(world,d).waterDepthMeters<20){errors.push(`${strait.id}: kapalı deniz koridoru`);break;}
  for(const crossing of world.crossings)if(!queryStaticNavigation(world,{from:crossing.anchors[0],to:crossing.anchors[1],portalId:crossing.portalId}).passable)errors.push(`${crossing.id}: iki kıyı arasında geçiş yok`);
  if(!isDry(world)&&(!world.hydrology.lakes.length||!world.crossings.length||!world.straits.length))errors.push('Zorunlu coğrafya eksik');
  const areaTotal=b.areas.reduce((s,a)=>s+a,0),seaArea=b.areas.reduce((s,a,i)=>s+(b.terrainHeightMeters[i]<=b.waterSurfaceMeters[i]?a:0),0);
  return {valid:!errors.length,errors,nodeCount:n,waterFraction:seaArea/areaTotal,lakeCount:world.hydrology.lakes.length,riverCount:world.hydrology.rivers.length,crossingCount:world.crossings.length,straitCount:world.straits.length,treeCount:b.vegetation.length/4,checks:['Deterministik drenaj sırası','Azalan nehir su kotu','Geçerli su alıcıları','Göl çanağı','Açık boğaz koridoru','Alan ağırlıklı deniz oranı']};
}
