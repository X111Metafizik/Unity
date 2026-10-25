// Fixed-size, exact-coordinate memo. No quantized heights or camera-dependent
// fields: a collision only evicts a value, it never changes the surface.
export class StampMemo {
 constructor(size=8192){this.size=size;this.values=new Float64Array(size*5);this.valid=new Uint8Array(size);this.hits=0;this.misses=0;}
 slot(d){return (Math.imul(d[0]*1099511627776|0,73856093)^Math.imul(d[1]*1099511627776|0,19349663)^Math.imul(d[2]*1099511627776|0,83492791))&(this.size-1);}
 get(d){const slot=this.slot(d),i=slot*5,a=this.values;if(this.valid[slot]&&a[i]===d[0]&&a[i+1]===d[1]&&a[i+2]===d[2]){this.hits++;return {value:a[i+3],weight:a[i+4]};}this.misses++;return null;}
 put(d,value,weight){const s=this.slot(d),i=s*5,a=this.values;a[i]=d[0];a[i+1]=d[1];a[i+2]=d[2];a[i+3]=value;a[i+4]=weight;this.valid[s]=1;return {value,weight};}
 get bytes(){return this.values.byteLength+this.valid.byteLength;}
}
