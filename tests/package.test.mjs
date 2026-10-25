import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {gunzipSync} from 'node:zlib';
import {createHash} from 'node:crypto';

const root=new URL('../Assets/StreamingAssets/Ladir/reference/',import.meta.url);
const manifest=JSON.parse(await readFile(new URL('manifest.json',root),'utf8'));
const sha=bytes=>createHash('sha256').update(bytes).digest('hex');

test('regional package has complete, intact metric tiles and a matching overview',async()=>{
 assert.equal(manifest.format,'ladir-unity-region-1');
 assert.equal(manifest.radiusMeters,4000000);
 assert.equal(manifest.sizeMeters,32000);
 assert.equal(manifest.tiles.length,256);
 assert.equal(manifest.fineStepMeters,7.8125);
 const overviewGz=await readFile(new URL('overview.bin.gz',root));
 assert.equal(sha(overviewGz),manifest.overviewCompressedSha256);
 const overview=gunzipSync(overviewGz);
 assert.equal(sha(overview),manifest.overviewSha256);
 const seen=new Set(),edges=new Map();
 for(const tile of manifest.tiles){
  assert.ok(!seen.has(tile.file));seen.add(tile.file);
  assert.equal(tile.file,`tile-${tile.x}-${tile.z}.bin.gz`);
  assert.ok(tile.x>=0&&tile.x<16&&tile.z>=0&&tile.z<16);
  const compressed=await readFile(new URL(tile.file,root));
  assert.equal(sha(compressed),tile.compressedSha256,tile.file);
  const data=gunzipSync(compressed),side=257,count=side*side;
  assert.equal(sha(data),tile.sha256,tile.file);
  assert.equal(data.toString('ascii',0,8),'LADIR001');
  assert.equal(data.length,40+count*12);assert.equal(data.readInt32LE(8),side);
  assert.equal(data.readDoubleLE(24),manifest.radiusMeters);
  assert.equal(data.readFloatLE(12),7.8125);
  assert.equal(data.readFloatLE(16),-16000+tile.x*2000);
  assert.equal(data.readFloatLE(20),-16000+tile.z*2000);
  assert.equal(data.readInt32LE(32),tile.x);assert.equal(data.readInt32LE(36),tile.z);
  const edge={left:[],right:[],top:[],bottom:[]};
  for(let i=0;i<side;i++)for(const [name,index]of [['left',i*side],['right',i*side+256],['top',i],['bottom',256*side+i]]){
   edge[name].push(data.readFloatLE(40+index*4),data.readFloatLE(40+count*4+index*4));
  }
  edges.set(`${tile.x}:${tile.z}`,edge);
  // Every eighth tile sample coincides with the overview's global grid.
  for(let z=0;z<side;z+=8)for(let x=0;x<side;x+=8){
   const index=z*side+x,overviewIndex=(tile.z*32+z/8)*513+tile.x*32+x/8;
   for(let channel=0;channel<2;channel++){
    assert.equal(data.readFloatLE(40+channel*count*4+index*4),overview.readFloatLE(40+channel*513*513*4+overviewIndex*4));
   }
  }
 }
 for(let z=0;z<16;z++)for(let x=0;x<16;x++){
  const edge=edges.get(`${x}:${z}`);
  if(x<15)assert.deepEqual(edge.right,edges.get(`${x+1}:${z}`).left);
  if(z<15)assert.deepEqual(edge.bottom,edges.get(`${x}:${z+1}`).top);
 }
});

test('ship package preserves supplied mesh dimensions and index validity',async()=>{
 const data=await readFile(new URL('../Assets/Ladir/Source/Voyager.bytes',import.meta.url));
 assert.equal(data.toString('ascii',0,8),'LADIRMS1');
 const vertices=data.readInt32LE(8),indices=data.readInt32LE(12);
 assert.equal(data.length,16+vertices*32+indices*4);
 let min=Infinity,max=-Infinity;
 for(let i=0;i<vertices;i++){const z=data.readFloatLE(16+i*32+8);min=Math.min(min,z);max=Math.max(max,z);}
 assert.ok(Math.abs(max-min-110)<0.001);
 for(let i=0;i<indices;i++)assert.ok(data.readUInt32LE(16+vertices*32+i*4)<vertices);
});
