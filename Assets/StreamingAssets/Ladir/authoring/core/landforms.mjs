import {clamp,mix,smooth,fbm,localCoordinates,localDirection} from './math.mjs';

export const LANDFORM_MODEL='watershed-2';
export const modernTerrain=w=>w.generationConfig.terrainModel===LANDFORM_MODEL;
export const REFERENCE={x:-19200,y:48000,sizeMeters:32000,blendMeters:4500};
const ridges=[
 [[-15500,6500],[-11000,10000],[-6800,12400],[-2600,15100]],
 [[-11000,10000],[-8500,5500],[-10500,1000],[-13000,-2500]],
 [[-6800,12400],[-4800,8200],[-6500,3500]],
 [[2500,15100],[5500,10500],[10500,12500],[15500,8500]],
 [[5500,10500],[6200,6200],[10000,3000],[13000,0]],
 [[10500,12500],[12000,7000],[15500,4500]]
];
function distanceToLine(x,y,points){let best=Infinity,along=0;for(let i=1;i<points.length;i++){const a=points[i-1],b=points[i],dx=b[0]-a[0],dy=b[1]-a[1],t=clamp(((x-a[0])*dx+(y-a[1])*dy)/(dx*dx+dy*dy)),d=Math.hypot(x-a[0]-dx*t,y-a[1]-dy*t);if(d<best){best=d;along=i-1+t;}}return [best,along];}
export function referenceCoordinates(world,d){const [x,y,z]=localCoordinates(world,d);return [(x*world.radiusMeters-REFERENCE.x),(y*world.radiusMeters-REFERENCE.y),z];}
export function referenceDirection(world,x,y){return localDirection(world,(REFERENCE.x+x)/world.radiusMeters,(REFERENCE.y+y)/world.radiusMeters);}
export function referenceWeight(world,d){if(!modernTerrain(world)||!['terran','alien'].includes(world.generationConfig.worldType))return 0;const [x,y,z]=referenceCoordinates(world,d);return z>.9?1-smooth(16000,20500,Math.max(Math.abs(x),Math.abs(y))):0;}

// Anisotropic kilometre-scale folds use one continuous 3-D domain on all cube
// faces. Noise modulates directed ridge belts; it does not define every peak.
export function directedRelief(world,d){
 const R=world.radiusMeters,s=world.terrainSeed,a=world.ridgeAxis;
 const along=(d[0]*a[0]+d[1]*a[1]+d[2]*a[2])*R;
 const across=(d[0]*a[2]-d[2]*a[0])*R;
 const province=smooth(-.15,.22,fbm(d,R/280000,s+2401,3));
 const fold=across+fbm(d,R/48000,s+2402,3)*16000;
 const crest=Math.exp(-Math.pow(Math.sin(fold/14000)*14000/2700,2));
 const branches=Math.exp(-Math.pow(Math.sin((fold*.66+along*.5)/6300)*6300/1700,2));
 const peaks=.68+.32*Math.sin(along/7900+fbm(d,R/27000,s+2403,2))**2;
 const plain=1-province;
 return 550+province*(crest*3000*peaks+branches*620)+fbm(d,R/2200,s+2404,3)*(14+province*95)-plain*220;
}

// A single, finite 32 km validation watershed. The outer 4.5 km blends C1
// continuously into the global provinces. No tiled copies of this region exist.
export function referenceHeight(world,d,original){
 const weight=referenceWeight(world,d);if(!weight)return original;
 const [x,y]=referenceCoordinates(world,d),s=world.terrainSeed;
 const mountain=smooth(-4000,10000,y),plain=(1-smooth(-5000,1000,y))*(1-smooth(-1000,5000,x));
 let h=1100+clamp((y+19200)/35000)*1000+mountain*60+fbm(d,world.radiusMeters/4300,s+2801,3)*(12+mountain*130)*(1-plain*.85);
 let uplift=0;
 for(let i=0;i<ridges.length;i++){
  const [dist]=distanceToLine(x,y,ridges[i]),width=i===0||i===3?2300:1450;
  // Along-segment indices jump across an inside bend's bisector. Vary peak
  // height in the continuous metre domain instead, avoiding a cut in a ridge.
  const peak=.73+.27*Math.cos((x*.8+y*.55)/3300+i)**2;
  uplift=Math.max(uplift,Math.exp(-((dist/width)**2))*(i===0||i===3?2800:1400)*peak);
 }
 h+=uplift*(.6+world.generationConfig.mountains*.6);
 const plateau=smooth(3000,7000,x)*(1-smooth(-2000,4000,y));
 h+=plateau*960;
 // Canyon has an independent 150 m floor, 1.1 km rim and 870 m depth.
 const axis=8400+1100*Math.sin(y/3900)+400*Math.sin(y/1450);
 const across=Math.abs(x-axis),end=(1-smooth(12500,16500,Math.abs(y+3500)));
 const wall=1-smooth(150,1100,across);
 h-=870*wall*end*plateau;
 // Stamps remain useful rock-scale variation, not the source of every ridge.
 return mix(original,h,weight);
}

function reach(world,id,points,recipientId,{width=70,valley=1400,depth=7,flow=30}={}){
 const out=[];
 for(let i=1;i<points.length;i++){
  const a=points[i-1],b=points[i],length=Math.hypot(b[0]-a[0],b[1]-a[1]),n=Math.ceil(length/180);
  for(let j=0;j<n;j++){const t=j/n,previous=points[Math.max(0,i-2)],next=points[Math.min(points.length-1,i+1)],curve=k=>.5*(2*a[k]+(-previous[k]+b[k])*t+(2*previous[k]-5*a[k]+4*b[k]-next[k])*t*t+(-previous[k]+3*a[k]-3*b[k]+next[k])*t*t*t),x=curve(0),y=curve(1),level=mix(a[2],b[2],t);out.push({direction:referenceDirection(world,x,y),waterLevelMeters:level,bedLevelMeters:level-depth,widthMeters:width,depthMeters:depth,valleyWidthMeters:valley});}
 }
 const b=points.at(-1);out.push({direction:referenceDirection(world,b[0],b[1]),waterLevelMeters:b[2],bedLevelMeters:b[2]-depth,widthMeters:width,depthMeters:depth,valleyWidthMeters:valley});
 return {id,name:id,network:true,regional:true,flow,recipientId,points:out};
}
export function installReferenceWatershed(world){
 if(!modernTerrain(world)||!['terran','alien'].includes(world.generationConfig.worldType))return;
 for(const lake of world.hydrology.lakes)lake.spillLevelMeters=lake.waterLevelMeters;
 const lake={id:'lake-reference',name:'Referans havza gölü',x:(REFERENCE.x+800)/world.radiusMeters,y:(REFERENCE.y-3500)/world.radiusMeters,direction:referenceDirection(world,800,-3500),radiusMeters:2000,waterLevelMeters:1380,spillLevelMeters:1380,depthMeters:115,outletId:'reference-outlet',closedBasin:false,regional:true};
 world.hydrology.lakes.push(lake);
 const inlet=reach(world,'reference-inlet',[[0,15500,2060],[-1400,13000,1940],[-800,10500,1830],[-2500,8000,1740],[-1800,6000,1650],[-900,4000,1550],[-1400,2000,1480],[300,0,1420],[800,-1700,1380]],lake.id,{width:110,valley:1800});
 const side=reach(world,'reference-tributary',[[-14500,3500,1760],[-12000,2000,1700],[-9000,2500,1680],[-6500,1000,1600],[-3800,2400,1550],[-1400,2000,1480]],inlet.id,{width:55,valley:1100,depth:5,flow:14});
 const outlet=reach(world,'reference-outlet',[[800,-3500,1380],[800,-5500,1380],[2000,-7200,1350],[1200,-9000,1310],[-1400,-10500,1270],[-2600,-12800,1220],[-800,-15000,1160],[1000,-16900,1100],[0,-19200,1050]],'river-aurora',{width:160,valley:3200,depth:10,flow:60});
 world.hydrology.rivers.push(inlet,side,outlet);
 world.landforms={model:LANDFORM_MODEL,reference:{...REFERENCE,seed:world.generationConfig.seed,lakeId:lake.id,rivers:[inlet.id,side.id,outlet.id],closedBasin:false},canyon:{floorWidthMeters:300,rimWidthMeters:2200,depthMeters:870}};
}

// Shape and hydraulic constraints are reapplied after erosion/detail. The
// same function feeds meshes, collision, tree roots and water classification.
export function regionalChannelHeight(h,r){
 const half=r.widthMeters/2,valley=(r.valleyWidthMeters??2800)/2,dist=r.distanceMeters;
 const bankHeight=r.waterLevelMeters+4+Math.max(0,dist-half)*.24;
 h=mix(h,bankHeight,1-smooth(half+80,half+valley,dist));
 if(dist<half+100)h=mix(h,r.waterLevelMeters-r.depthMeters+clamp(dist/half)**4*r.depthMeters*.45,1-smooth(half,half+100,dist));
 return h;
}

export function referenceDetailFactor(world,d){const weight=referenceWeight(world,d);if(!weight)return 1;const [x,y]=referenceCoordinates(world,d);return mix(1,.025+.10*smooth(-3000,8000,y),weight);}
