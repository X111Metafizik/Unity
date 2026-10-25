import {readFile,writeFile} from 'node:fs/promises';
import {resolve,join} from 'node:path';
import {pathToFileURL} from 'node:url';
import {createHash} from 'node:crypto';
const source=resolve(process.argv[2]),out=resolve(import.meta.dirname,'../Assets/Ladir/Source');
const THREE=await import(pathToFileURL(join(source,'node_modules/three/build/three.module.js')));
const {mergeGeometries}=await import(pathToFileURL(join(source,'node_modules/three/examples/jsm/utils/BufferGeometryUtils.js')));
const {volumeData}=await import(pathToFileURL(join(source,'render/volume-data.mjs')));
const chunks=[Buffer.from('LADIRVG1')];
function u32(n){const b=Buffer.alloc(4);b.writeUInt32LE(n);chunks.push(b);}
u32(6);
for(let lod=0;lod<2;lod++)for(let sp=0;sp<3;sp++){
 const parts=[];
 if(lod){const g=new THREE.IcosahedronGeometry(.36,1);g.scale(1,1.25,1);g.translate(0,.60,0);parts.push(g);}
 else if(sp===0){for(let i=0;i<3;i++){const g=new THREE.ConeGeometry(.25-i*.045,.53-i*.05,9);g.translate(0,.40+i*.20,0);parts.push(g);}}
 else for(const [x,y,z,r] of [[0,.68,0,.31],[-.19,.52,.08,.23],[.16,.60,.13,.25],[.04,.48,-.19,.23]]){const g=new THREE.IcosahedronGeometry(r,1);g.scale(1,1.12,1);g.translate(x,y,z);parts.push(g);}
 const trunk=new THREE.CylinderGeometry(.018,.032,.7,6);trunk.translate(0,.35,0);parts.push(trunk);
 const normalized=parts.map((g,i)=>{if(g.index)g=g.toNonIndexed();const color=new THREE.Color(i===parts.length-1?0x514333:[0x315f42,0x486a31,0x687e46][sp]),data=new Float32Array(g.attributes.position.count*4);for(let j=0;j<g.attributes.position.count;j++)data.set([color.r,color.g,color.b,i===parts.length-1?0:sp+1],j*4);g.setAttribute('color',new THREE.BufferAttribute(data,4));return g;});
 const mesh=mergeGeometries(normalized),p=mesh.attributes.position,n=mesh.attributes.normal,c=mesh.attributes.color;u32(p.count);
 const bytes=Buffer.alloc(p.count*40);let at=0;for(let i=0;i<p.count;i+=3)for(const j of [i,i+2,i+1])for(const v of [-p.getX(j),p.getY(j),p.getZ(j),-n.getX(j),n.getY(j),n.getZ(j),c.getX(j),c.getY(j),c.getZ(j),c.getW(j)]){bytes.writeFloatLE(v,at);at+=4;}chunks.push(bytes);
}
const trees=Buffer.concat(chunks);await writeFile(join(out,'Vegetation.bytes'),trees);
const volume=volumeData(4000);const noise=Buffer.from(volume.noise.image.data);await writeFile(join(out,'VolumeNoise.bytes'),noise);
const files={};for(const name of ['Vegetation.bytes','VolumeNoise.bytes'])files[name]={bytes:(await readFile(join(out,name))).length,sha256:createHash('sha256').update(await readFile(join(out,name))).digest('hex')};
await writeFile(join(out,'visual-provenance.json'),JSON.stringify({sourceCommit:'34841d6abc1c1c12251c2ace18ffb853895db16f',sourceFiles:['render/vegetation.mjs','render/volume-data.mjs'],files,notes:'Original geometries and colors. X reflection / reversed winding at the Unity boundary. 64 cubed RGBA volume field is unchanged.'},null,2));console.log(files);
