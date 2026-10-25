import {fbm,smooth,clamp,mix,localCoordinates,localDirection,normalize} from './math.mjs';
import {mountainRelief,riftTerrain} from './relief.mjs';
import {stampedRelief} from './terrain-stamps.mjs';
export const WORLD_TYPES={terran:'Terran · yaşayan dünya',desert:'Sahra · kum ve kayalık',volcanic:'Ignis · volkanik dünya',mars:'Ares · Mars esintisi',moon:'Selene · Ay esintisi',alien:'Prizma · mor ve turkuaz'};
export const isDry=world=>['desert','volcanic','mars','moon'].includes(world.generationConfig.worldType);
export const typeAppearance=type=>type==='moon'?{atmosphere:0,clouds:0,fog:0}:type==='mars'?{atmosphere:.12,clouds:0,fog:.035}:type==='volcanic'?{atmosphere:.28,clouds:.08,fog:.025}:type==='desert'?{atmosphere:.35,clouds:.05,fog:.04}:{atmosphere:.45,clouds:.22,fog:.10};
// Integer lattice, independent of mesh tiles and camera position.
function lattice(x,y,z,seed){let h=Math.imul(x,374761393)^Math.imul(y,668265263)^Math.imul(z,2147483647)^seed;h=Math.imul(h^(h>>>13),1274126177);return ((h^(h>>>16))>>>0)/4294967296;}
export function craterField(world,d,spacing=45000){
 const f=world.radiusMeters/spacing,p=d.map(v=>v*f),cell=p.map(Math.floor);let h=0,rim=0;
 for(let z=-1;z<=1;z++)for(let y=-1;y<=1;y++)for(let x=-1;x<=1;x++){
  const a=cell[0]+x,b=cell[1]+y,c=cell[2]+z,seed=world.terrainSeed;
  const center=[a+.15+lattice(a,b,c,seed)*.7,b+.15+lattice(a,b,c,seed+3)*.7,c+.15+lattice(a,b,c,seed+9)*.7],length=Math.hypot(...center);
  if(Math.abs(length-f)>.44)continue;
  const radius=.10+lattice(a,b,c,seed+7)*.28,dist=Math.hypot(...p.map((v,i)=>v-center[i]/length*f))/radius;
  if(dist>1.5)continue;
  const depth=spacing*radius*.075,lip=Math.exp(-(((dist-1.03)/.12)**2));
  h+=-depth*(1-smooth(.12,1,dist))+depth*.30*lip;rim=Math.max(rim,lip);
 }
 return {height:h,rim};
}
export function geology(world,d){
 const type=world.generationConfig.worldType,s=world.terrainSeed,R=world.radiusMeters,[x,y,z]=localCoordinates(world,d),mx=x*R,my=y*R;
 const broad=fbm(d,5,s+300,4),rock=fbm(d,R/17000,s+310,4),mesa=smooth(-.025,.055,rock),detail=fbm(d,R/1300,s+311,3)*22;
 let h=650+broad*1100+mesa*850+detail;
 if(type==='moon'){const a=craterField(world,d),b=craterField(world,d,6500),r=z>.9?Math.hypot(mx,my)/7500:100,hero=-460*(1-smooth(.12,1,r))+145*Math.exp(-(((r-1.03)/.1)**2));return {height:650+broad*1500+rock*120+a.height+b.height+detail*.2+hero+(stampedRelief(world,d,1000)-1000)*.12*smooth(1.3,2.2,r),lava:0,albedo:.68+smooth(-.18,.2,broad)*.35+a.rim*.20+b.rim*.12-.18*(1-smooth(.65,1,r))+.18*Math.exp(-(((r-1.03)/.09)**2))};}
 if(type==='mars'){
  const canyon=1-smooth(.025,.11,Math.abs(fbm(d,R/100000,s+337,3))),hero=z>.9?1-smooth(1800,6500,Math.abs(my-2200*Math.sin(mx/17000))):0;
  h=900+broad*1300+mesa*550+mountainRelief(world,d)*.65-Math.max(canyon,hero)*1150+craterField(world,d).height*.5+detail;
 }
 if(type==='desert'){
  const dune=1-Math.abs(Math.sin((d[0]+d[2]*.47)*R/240+fbm(d,R/7000,s+340,3)*4));
  const channel=1-smooth(.025,.14,Math.abs(fbm(d,R/29000,s+337,3)));
  h=550+broad*800+mesa*650+mountainRelief(world,d)*.48-channel*780+dune*dune*24*(1-mesa)*(1-channel)+detail*.35;
 }
 let lava=0;
 if(type==='volcanic'){
  const angle=Math.atan2(my,mx),r=z>.9?Math.hypot(mx,my)/(1+.12*Math.sin(angle*3)+.075*Math.cos(angle*7)+.025*Math.sin(angle*11)):1e9;
  const province=1-smooth(8500,25000,r),bowl=1-smooth(2200,4600,r),rim=Math.exp(-(((r-4900)/950)**2));
  const basin=1-smooth(.035,.17,Math.abs(fbm(d,R/90000,s+371,3)));
  const relief=mountainRelief(world,d),fissure=1-smooth(.006,.021,Math.abs(fbm(d,R/8500,s+377,3)));
  h=620+broad*750+relief*.8*(1-province*.85)-basin*380-province*470-bowl*800+rim*(460+rock*180+relief*.30)+detail*(1-bowl)+fbm(d,R/1800,s+391,3)*260*province*(1-bowl);
  const pool=1-smooth(1650,2250,r);
  lava=Math.max(fissure*.78*basin*(1-smooth(250,1050,h))*(1-province),pool);
  // A quiet lava floor sits below the surrounding rim and the outer province.
  h=mix(h,620+fbm(world.frame.center,5,s+300,4)*750-380*(1-smooth(.035,.17,Math.abs(fbm(world.frame.center,R/90000,s+371,3))))-1270,pool);
 }
 return {height:['mars','desert'].includes(type)?riftTerrain(world,d,h):h,lava};
}
export function forestCover(world,d,s){
 if(isDry(world)||s.waterDepthMeters>0||s.protectedFeatureMask>.12||s.temperature<1||s.elevationMeters>2600||![2,3,4,15,16].includes(s.biome))return 0;
 const c=world.generationConfig,R=world.radiusMeters,seed=world.vegetationSeed;
 const region=smooth(-.16,.23,fbm(d,R/48000,seed,3)),grove=smooth(-.12,.20,fbm(d,R/2300,seed+29,3));
 const shore=smooth(45,260,s.elevationMeters-c.seaLevel),climate=smooth(.34,.68,s.moisture);
 return clamp(c.forestDensity*1.65*region*grove*shore*climate);
}
export function presetLandmarks(world){
 const type=world.generationConfig.worldType,make=(id,name,x,y,rangeMeters)=>({id,name,direction:localDirection(world,x/world.radiusMeters,y/world.radiusMeters),rangeMeters});
 return type==='moon'?[make('crater','Selene Krateri',0,0,22000)]:type==='volcanic'?[make('caldera','Ateş Kalderası',0,0,13500)]:type==='mars'?[make('canyon','Kızıl Kanyon',0,0,18000)]:[];
}
