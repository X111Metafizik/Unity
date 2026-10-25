// Export the existing canonical generator, never a second terrain algorithm.
import {readFile,writeFile,mkdir,copyFile} from 'node:fs/promises';
import {gzipSync} from 'node:zlib';
import {createHash} from 'node:crypto';
import {Worker,isMainThread,parentPort,workerData} from 'node:worker_threads';
import {createRequire} from 'node:module';
import {pathToFileURL,fileURLToPath} from 'node:url';
import {resolve,join} from 'node:path';
const sourceIndex=process.argv.indexOf('--source');
export const sourceRoot=workerData?.sourceRoot??process.env.LADIR_WEB_SOURCE??(sourceIndex>=0?process.argv[sourceIndex+1]:null);
const projectRoot=fileURLToPath(new URL('../',import.meta.url));
let deserializePlanet,sampleTerrain,biomeColor,referenceDirection,REFERENCE,Vector3,Matrix4,Matrix3;
if(sourceRoot){
 const fromWeb=path=>import(pathToFileURL(resolve(sourceRoot,path)).href);
 ({deserializePlanet}=await fromWeb('io/bundle.mjs'));
 ({sampleTerrain,biomeColor}=await fromWeb('core/terrain.mjs'));
 ({referenceDirection,REFERENCE}=await fromWeb('core/landforms.mjs'));
 const requireWeb=createRequire(resolve(sourceRoot,'package.json'));
 ({Vector3,Matrix4,Matrix3}=await import(new URL('./three.module.js',pathToFileURL(requireWeb.resolve('three'))).href));
}
const sha=b=>createHash('sha256').update(b).digest('hex');
const out=join(projectRoot,'Assets/StreamingAssets/Ladir/reference');
const fixture=sourceRoot?resolve(sourceRoot,'fixtures/river-v42.planet'):null;
const shipRoot=join(projectRoot,'Assets/Ladir/Source');
const evidenceRoot=join(projectRoot,'evidence/unity-v1');
export function packTile(world,x,z,cells,size,minX,minZ){
 if(!sourceRoot)throw new Error('Provide --source <LADIR web checkout> or LADIR_WEB_SOURCE. The committed Unity package needs neither Node nor the web checkout to play.');
 const side=cells+1,count=side*side,step=size/cells,buffer=Buffer.alloc(40+count*12);
 buffer.write('LADIR001');buffer.writeInt32LE(side,8);buffer.writeFloatLE(step,12);buffer.writeFloatLE(minX,16);buffer.writeFloatLE(minZ,20);buffer.writeDoubleLE(world.radiusMeters,24);buffer.writeInt32LE(x,32);buffer.writeInt32LE(z,36);
 const heights=new Float32Array(buffer.buffer,buffer.byteOffset+40,count),water=new Float32Array(buffer.buffer,buffer.byteOffset+40+count*4,count),colors=new Uint8Array(buffer.buffer,buffer.byteOffset+40+count*8,count*4);
 let min=Infinity,max=-Infinity;
 for(let j=0;j<side;j++)for(let i=0;i<side;i++){
  const index=j*side+i,s=sampleTerrain(world,referenceDirection(world,minX+i*step,minZ+j*step)),color=biomeColor(s);
  heights[index]=s.elevationMeters;water[index]=s.waterSurfaceMeters;
  min=Math.min(min,s.elevationMeters);max=Math.max(max,s.elevationMeters);
  for(let k=0;k<3;k++)colors[index*4+k]=Math.round(Math.max(0,Math.min(1,color[k]))*255);
  colors[index*4+3]=Math.round((s.forest??0)*255);
 }
 return {buffer,min,max,side,step};
}
if(!isMainThread){
 const world=await deserializePlanet(await readFile(fixture));
 parentPort.on('message',async id=>{
  try{const x=id%16,z=Math.floor(id/16),start=performance.now(),tile=packTile(world,x,z,256,2000,-16000+x*2000,-16000+z*2000),bytes=gzipSync(tile.buffer,{level:3}),file=`tile-${x}-${z}.bin.gz`;
   await writeFile(out+'/'+file,bytes);parentPort.postMessage({id,x,z,file,sha256:sha(tile.buffer),compressedSha256:sha(bytes),bytes:bytes.length,rawBytes:tile.buffer.length,min:tile.min,max:tile.max,ms:performance.now()-start});
  }catch(e){parentPort.postMessage({error:e.stack});}
 });
}else if(process.argv[1]&&pathToFileURL(resolve(process.argv[1])).href===import.meta.url){
 if(!sourceRoot)throw new Error('Usage: node tools/export-from-web.mjs --source <LADIR web checkout>');
 await mkdir(out,{recursive:true});await mkdir(shipRoot,{recursive:true});await mkdir(evidenceRoot,{recursive:true});
 const world=await deserializePlanet(await readFile(fixture)),start=performance.now();
 const overview=packTile(world,-1,-1,512,32000,-16000,-16000),overviewGz=gzipSync(overview.buffer,{level:3});await writeFile(out+'/overview.bin.gz',overviewGz);
 const tiles=[];let next=0,done=0;
 await Promise.all(Array.from({length:3},()=>new Promise((finish,reject)=>{
  const worker=new Worker(new URL(import.meta.url),{workerData:{sourceRoot:resolve(sourceRoot)}});worker.on('error',reject);
  worker.on('message',r=>{if(r.error){reject(Error(r.error));worker.terminate();return;}tiles.push(r);done++;if(done%16===0)console.log(`Export ${done}/256 tiles, ${((performance.now()-start)/1000).toFixed(1)} s`);if(next<256)worker.postMessage(next++);else worker.terminate().then(finish);});worker.postMessage(next++);
 })));
 tiles.sort((a,b)=>a.id-b.id);
 if(tiles.length!==256||tiles.some((tile,id)=>tile.id!==id))throw new Error('Incomplete tile export; manifest was not replaced.');
 const checks=[];for(const [x,z]of [[800,-3500],[-11000,10000],[8400,-3500],[0,0],[-9000,2500]]){const s=sampleTerrain(world,referenceDirection(world,x,z));checks.push({x,z,height:s.elevationMeters,water:s.waterSurfaceMeters});}
 const manifest={format:'ladir-unity-region-1',worldHash:world.manifest.worldDataHash,seed:world.manifest.normalizedSeed,generatorVersion:world.manifest.generatorVersion,generatorBuildHash:world.manifest.generatorBuildHash,radiusMeters:world.radiusMeters,sizeMeters:32000,tileMeters:2000,tilesPerAxis:16,fineCells:256,fineStepMeters:7.8125,overviewStepMeters:62.5,coordinates:'reference gnomonic metres; Unity X=reference X, Z=reference Y; rendered Y=ASL minus spherical sag; finite local prototype',reference:REFERENCE,frame:world.frame,overviewSha256:sha(overview.buffer),overviewCompressedSha256:sha(overviewGz),overviewBytes:overviewGz.length,tiles,checks,limitations:['7.8125 m sampled height field; not equal to every browser adaptive mesh.','32 km local region; spherical global quadtree, global warp and all visual effects are not yet ported.'],exportMs:performance.now()-start};
 await writeFile(out+'/manifest.json',JSON.stringify(manifest,null,2));
 await exportShip();
 await writeFile(join(evidenceRoot,'export.json'),JSON.stringify({worldHash:manifest.worldHash,tiles:tiles.length,stepMeters:7.8125,rawBytes:tiles.reduce((a,t)=>a+t.rawBytes,overview.buffer.length),compressedBytes:tiles.reduce((a,t)=>a+t.bytes,overviewGz.length),milliseconds:manifest.exportMs},null,2));
 console.log(JSON.stringify({complete:true,ms:manifest.exportMs,compressedMiB:tiles.reduce((a,t)=>a+t.bytes,overviewGz.length)/1048576}));
}
async function exportShip(){
 const b=await readFile(resolve(sourceRoot,'assets/starlight-voyager.glb')),jsonLength=b.readUInt32LE(12),g=JSON.parse(b.subarray(20,20+jsonLength).toString()),bin=b.subarray(20+jsonLength+8),root=shipRoot;
 const prim=g.meshes[0].primitives[0];
 function access(id){const a=g.accessors[id],v=g.bufferViews[a.bufferView],stride=v.byteStride??({SCALAR:1,VEC2:2,VEC3:3,VEC4:4}[a.type])*({5126:4,5125:4,5123:2,5121:1}[a.componentType]),components={SCALAR:1,VEC2:2,VEC3:3,VEC4:4}[a.type],arr=[];for(let i=0;i<a.count;i++)for(let k=0;k<components;k++){const offset=(v.byteOffset??0)+(a.byteOffset??0)+i*stride+k*({5126:4,5125:4,5123:2,5121:1}[a.componentType]);arr.push(a.componentType===5126?bin.readFloatLE(offset):a.componentType===5125?bin.readUInt32LE(offset):a.componentType===5123?bin.readUInt16LE(offset):bin.readUInt8(offset));}return arr;}
 const p=access(prim.attributes.POSITION),n=access(prim.attributes.NORMAL),uv=access(prim.attributes.TEXCOORD_0),indices=access(prim.indices),vertices=p.length/3;
 // Supplied ship points toward -X; rotate into Unity +Z and preserve 110 m length.
 const rot=new Matrix4().makeRotationY(Math.PI/2),v=new Vector3(),normalMatrix=new Matrix3().getNormalMatrix(rot),lo=new Vector3(Infinity,Infinity,Infinity),hi=new Vector3(-Infinity,-Infinity,-Infinity);
 for(let i=0;i<vertices;i++){v.fromArray(p,i*3).applyMatrix4(rot);v.toArray(p,i*3);lo.min(v);hi.max(v);v.fromArray(n,i*3).applyNormalMatrix(normalMatrix).toArray(n,i*3);}
 const center=lo.clone().add(hi).multiplyScalar(.5),factor=110/(hi.z-lo.z),buffer=Buffer.alloc(16+vertices*32+indices.length*4);buffer.write('LADIRMS1');buffer.writeInt32LE(vertices,8);buffer.writeInt32LE(indices.length,12);
 for(let i=0;i<vertices;i++){v.fromArray(p,i*3).sub(center).multiplyScalar(factor);const values=[-v.x,v.y,v.z,-n[i*3],n[i*3+1],n[i*3+2],uv[i*2],1-uv[i*2+1]];values.forEach((x,k)=>buffer.writeFloatLE(x,16+i*32+k*4));}
 for(let i=0;i<indices.length;i+=3)for(let k=0;k<3;k++)buffer.writeUInt32LE(indices[i+[0,2,1][k]],16+vertices*32+(i+k)*4);
 await writeFile(root+'/Voyager.bytes',buffer);
 for(const [name,index]of [['VoyagerAlbedo',g.textures[g.materials[0].pbrMetallicRoughness.baseColorTexture.index].source],['VoyagerNormal',g.textures[g.materials[0].normalTexture.index].source]]){const image=g.images[index],view=g.bufferViews[image.bufferView];await writeFile(root+'/'+name+'.png',bin.subarray(view.byteOffset,view.byteOffset+view.byteLength));}
 await copyFile(resolve(sourceRoot,'assets/fx/ion-thruster-v24.png'),root+'/IonThruster.png');
 await writeFile(root+'/model-provenance.json',JSON.stringify({source:'assets/starlight-voyager.glb',sha256:sha(b),vertices,triangles:indices.length/3,lengthMeters:110},null,2));
}
