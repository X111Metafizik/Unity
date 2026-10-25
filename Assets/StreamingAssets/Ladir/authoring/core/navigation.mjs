import {distance,normalize,dot} from './math.mjs';
import {sampleTerrain} from './terrain.mjs';
export function queryStaticNavigation(world,{from,to,mode='land',draftMeters=0,portalId=null,stepMeters=25}){
  if(!['land','water'].includes(mode))throw Error('Hareket türü land veya water olmalı.');
  const length=distance(from,to)*world.radiusMeters,steps=Math.max(1,Math.ceil(length/stepMeters));
  const crossing=world.crossings.find(c=>c.portalId===portalId);
  const route=[];
  for(let i=0;i<=steps;i++){
    const t=i/steps,d=normalize(from.map((v,k)=>v*(1-t)+to[k]*t)),s=sampleTerrain(world,d);
    const atFord=crossing&&distance(d,crossing.direction)*world.radiusMeters<crossing.widthMeters*1.2&&s.waterDepthMeters<1;
    if(mode==='land'&&s.waterDepthMeters>.1&&!atFord)return {passable:false,reason:'Su engeli',blockedAt:d,route};
    if(mode==='water'&&(s.waterDepthMeters<=0||s.waterDepthMeters<draftMeters))return {passable:false,reason:'Yetersiz su derinliği',blockedAt:d,route};
    route.push(d);
  }
  return {passable:true,lengthMeters:length,route,portalId:crossing?.portalId??null};
}
export function nearestRegion(world,d){
  let best=null,score=-Infinity;
  for(const r of world.regionBindings){const s=dot(d,r.direction);if(s>score){score=s;best=r.id;}}
  return best;
}
