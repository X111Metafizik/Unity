import {modernTerrain,directedRelief} from './landforms.mjs';
import {clamp,smooth,fbm,localCoordinates} from './math.mjs';
import {stampedRelief,globalStamps} from './terrain-stamps.mjs';

// A continuous 3-D field: no face UVs, patch IDs or camera-dependent randomness.
function hash(x,y,z,s){let h=Math.imul(x,374761393)^Math.imul(y,668265263)^Math.imul(z,2147483647)^s;h=Math.imul(h^(h>>>13),1274126177);return ((h^(h>>>16))>>>0)/4294967296;}
export function noiseGradient(x,y,z,seed){
 const ix=Math.floor(x),iy=Math.floor(y),iz=Math.floor(z),fx=x-ix,fy=y-iy,fz=z-iz;
 const fade=t=>t*t*t*(t*(t*6-15)+10),slope=t=>30*t*t*(t-1)*(t-1),u=fade(fx),v=fade(fy),w=fade(fz);
 const a=hash(ix,iy,iz,seed),b=hash(ix+1,iy,iz,seed),c=hash(ix,iy+1,iz,seed),d=hash(ix+1,iy+1,iz,seed);
 const e=hash(ix,iy,iz+1,seed),f=hash(ix+1,iy,iz+1,seed),g=hash(ix,iy+1,iz+1,seed),h=hash(ix+1,iy+1,iz+1,seed);
 const ab=a+(b-a)*u,cd=c+(d-c)*u,ef=e+(f-e)*u,gh=g+(h-g)*u,low=ab+(cd-ab)*v,high=ef+(gh-ef)*v;
 const dx0=b-a+(d-c-b+a)*v,dx1=f-e+(h-g-f+e)*v;
 return [(low+(high-low)*w)*2-1,(dx0+(dx1-dx0)*w)*slope(fx)*2,((cd-ab)+(gh-ef-cd+ab)*w)*slope(fy)*2,(high-low)*slope(fz)*2];
}


// Smooth divides with bounded amplitude at each physical wavelength. The old
// gradient-directed oscillations rotated abruptly at stationary points and
// created needles; increasing tessellation could not repair that height field.
export function mountainRelief(world,d){
 if(modernTerrain(world)){const h=directedRelief(world,d),blend=globalStamps(world)?.65:.2;return h*(1-blend)+stampedRelief(world,d,h)*blend;}
 const R=world.radiusMeters,seed=world.terrainSeed+610;
 const province=smooth(-.32,.38,fbm(d,R/95000,seed+83,3));
 const n=fbm(d,R/21000,seed,3);
 const crest=clamp(1-(Math.sqrt(n*n+.0036)-.06)/.38);
 const secondary=fbm(d,R/7300,seed+41,3);
 const folds=clamp(1-(Math.sqrt(secondary*secondary+.0144)-.12)/.46);
 const gullies=1-smooth(.05,.30,Math.abs(fbm(d,R/4600,seed+191,2)));
 return stampedRelief(world,d,Math.max(0,province*650+crest**2.1*2900*(.28+.72*province)
  +folds*folds*380*(.3+.7*province)-gullies*130*(.4+world.generationConfig.erosion*.6)
  +fbm(d,R/1900,seed+93,3)*75+fbm(d,R/650,seed+117,2)*16));
}

// A broad rift province with a curved main trough and a joining side valley.
// Depth is measured from the adjacent plateau, not an artificial vertical scale.
// Independent, low-frequency rift belts also occur away from the named visit.
export function riftTerrain(world,d,height){
 const R=world.radiusMeters,s=world.terrainSeed,sea=world.generationConfig.seaLevel;
 if(height<=sea)return height;
 const [x,y,z]=localCoordinates(world,d),X=x*R+70000,Y=y*R-20000;
 const bend=6500*Math.sin(X/18000)+1700*Math.sin(X/8000);
 const along=1-smooth(48000,80000,Math.abs(X));
 const main=(1-smooth(600,5600,Math.abs(Y-bend)))*along;
 const branch=(1-smooth(350,3800,Math.abs(Y-bend-(15000-X)*.55)))*(1-smooth(30000,55000,Math.abs(X+10000)))*(1-smooth(12000,20000,X));
 const local=z>.9?Math.max(main,branch):0;
 const plateau=z>.9?1-smooth(.45,1.15,Math.hypot(X/100000,Y/37000)):0;
 const belt=1-smooth(.035,.14,Math.abs(fbm(d,R/85000,s+971,3)));
 const province=smooth(.02,.28,fbm(d,R/260000,s+973,3));
 const uplift=1400*plateau*smooth(sea,sea+450,height);
 const depth=Math.max(local*1950,belt*province*1800*smooth(sea+1600,sea+2800,height));
 return height+uplift-Math.min(depth,Math.max(0,height+uplift-sea-95));
}
