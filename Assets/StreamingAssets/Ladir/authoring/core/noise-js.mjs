// Reference implementation retained for compatibility and exact parity tests.
const mix=(a,b,t)=>a+(b-a)*t;
function lattice(x,y,z,seed){
  let h=Math.imul(x,374761393)^Math.imul(y,668265263)^Math.imul(z,2147483647)^seed;
  h=Math.imul(h^(h>>>13),1274126177); return ((h^(h>>>16))>>>0)/2147483647.5-1;
}
export function noise3JS(x,y,z,seed){
  const ix=Math.floor(x),iy=Math.floor(y),iz=Math.floor(z),fx=x-ix,fy=y-iy,fz=z-iz;
  const f=t=>t*t*t*(t*(t*6-15)+10),u=f(fx),v=f(fy),w=f(fz);
  return mix(mix(mix(lattice(ix,iy,iz,seed),lattice(ix+1,iy,iz,seed),u),mix(lattice(ix,iy+1,iz,seed),lattice(ix+1,iy+1,iz,seed),u),v),
    mix(mix(lattice(ix,iy,iz+1,seed),lattice(ix+1,iy,iz+1,seed),u),mix(lattice(ix,iy+1,iz+1,seed),lattice(ix+1,iy+1,iz+1,seed),u),v),w);
}
export function fbmJS(p,frequency,seed,octaves=5){
  let sum=0,amp=.5;
  for(let i=0;i<octaves;i++){sum+=amp*noise3JS(p[0]*frequency,p[1]*frequency,p[2]*frequency,seed+i*1013);frequency*=2.03;amp*=.49;}
  return sum;
}
