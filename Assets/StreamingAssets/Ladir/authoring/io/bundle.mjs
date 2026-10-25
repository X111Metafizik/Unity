import {stableStringify} from '../core/math.mjs';
import {worldHash,validatePlanet,GENERATOR_VERSION} from '../core/world.mjs';
import {GENERATOR_BUILD_HASH} from '../core/build-id.mjs';
const TYPES={Float64Array,Float32Array,Uint32Array,Int32Array,Uint8Array};
const methods={Float64Array:['setFloat64','getFloat64'],Float32Array:['setFloat32','getFloat32'],Uint32Array:['setUint32','getUint32'],Int32Array:['setInt32','getInt32'],Uint8Array:['setUint8','getUint8']};
export function serializePlanet(world,appearanceConfig={}){
  const descriptors=[];let binaryBytes=0;
  for(const name of Object.keys(world.buffers).sort()){const a=world.buffers[name];binaryBytes=Math.ceil(binaryBytes/8)*8;descriptors.push({name,type:a.constructor.name,length:a.length,byteLength:a.byteLength,offset:binaryBytes});binaryBytes+=a.byteLength;}
  const metadata={...world,buffers:undefined,appearanceConfig,manifest:{...world.manifest,bufferDescriptors:descriptors}};
  delete metadata.buffers;
  const json=new TextEncoder().encode(stableStringify(metadata)),start=Math.ceil((16+json.length)/8)*8,out=new ArrayBuffer(start+binaryBytes),bytes=new Uint8Array(out),view=new DataView(out);
  bytes.set(new TextEncoder().encode('PLANET01'));view.setUint32(8,json.length,true);view.setUint32(12,start,true);bytes.set(json,16);
  for(const desc of descriptors){const a=world.buffers[desc.name];for(let i=0;i<a.length;i++)view[methods[desc.type][0]](start+desc.offset+i*a.BYTES_PER_ELEMENT,a[i],true);}
  return out;
}
export async function deserializePlanet(input){
  const buffer=input instanceof ArrayBuffer?input:input.buffer.slice(input.byteOffset,input.byteOffset+input.byteLength),bytes=new Uint8Array(buffer),view=new DataView(buffer);
  if(buffer.byteLength<16||new TextDecoder().decode(bytes.subarray(0,8))!=='PLANET01')throw Error('Geçerli bir .planet paketi değil.');
  const length=view.getUint32(8,true),start=view.getUint32(12,true);
  if(length>buffer.byteLength-16||start<16+length||start>buffer.byteLength)throw Error('Paket üstbilgisi bozuk.');
  const world=JSON.parse(new TextDecoder('utf-8',{fatal:true}).decode(bytes.subarray(16,16+length)));
  // Saved V32/V33 geography retains its own generator, radius and model. It uses
  // its explicit watershed model and metre radius; never reinterpret its data.
  const legacy=(world.manifest.generatorVersion==='1.8.0'&&world.manifest.generatorBuildHash==='264fe0af577151a24b686c4696567a787f6ec4002eb89431f27efe5f70e9310d')||(world.manifest.generatorVersion==='1.7.2'&&world.manifest.generatorBuildHash==='f770a1cdabf8b55d8c9ebeb922ea590bcbb51d2a0c46e6e83d6750987fe2806c')||(world.manifest.generatorVersion==='1.7.1'&&world.manifest.generatorBuildHash==='bdab88ca4754e370c7fa027035043cb08f44738d952a862212f646f893fd51d5')||(world.manifest.generatorVersion==='1.7.0'&&world.manifest.generatorBuildHash==='e075151f0fda0c102392633a29a698fc38a5a13269a9d639ad9bb3e2b14056b0')||(world.manifest.generatorVersion==='1.6.0'&&world.manifest.generatorBuildHash==='a6c081786ed381944e781378768124119231509be93da46f0345f130da3fa932')||(world.manifest.generatorVersion==='1.4.0'&&world.manifest.generatorBuildHash==='c47a6a06d6199052d60d9498da3da9397f3b28be52c23dd02d353487bdb67c25')||(world.manifest.generatorVersion==='1.5.0'&&world.manifest.generatorBuildHash==='340b2f28acec9002fc7981ddf10a5078245b8cf46b8c955da121fe708ccef071');
  if(world.manifest.schemaVersion!==1||(!legacy&&world.manifest.generatorVersion!==GENERATOR_VERSION))throw Error('Paket sürümü uyumsuz; açık dönüştürme gerekli.');
  if(!legacy&&world.manifest.generatorBuildHash!==GENERATOR_BUILD_HASH)throw Error('Üretim çekirdeği sürümü farklı. Paketi onu oluşturan Studio sürümüyle aç; sessizce yeniden üretilmedi.');
  world.buffers={};let lastEnd=0;
  for(const d of world.manifest.bufferDescriptors){
    const Type=TYPES[d.type];
    if(!Type||!Number.isInteger(d.length)||d.length<0||!Number.isInteger(d.offset)||d.offset<lastEnd||d.byteLength!==d.length*Type.BYTES_PER_ELEMENT||start+d.offset+d.byteLength>buffer.byteLength||['__proto__','constructor','prototype'].includes(d.name))throw Error('Eksik veya tutarsız sayısal alan.');
    const a=new Type(d.length);for(let i=0;i<a.length;i++)a[i]=view[methods[d.type][1]](start+d.offset+i*Type.BYTES_PER_ELEMENT,true);
    world.buffers[d.name]=a;lastEnd=d.offset+d.byteLength;
  }
  if(await worldHash(world)!==world.manifest.worldDataHash)throw Error('Dünya imzası eşleşmiyor; paket değiştirilmiş veya eksik.');
  const report=validatePlanet(world);if(!report.valid)throw Error(report.errors.join(' / '));
  world.generationReport=report;return world;
}
