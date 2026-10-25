import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {packTile,sourceRoot} from '../tools/export-from-web.mjs';
import {pathToFileURL} from 'node:url';
import {resolve} from 'node:path';

test('Unity region export preserves metric units, seed terrain and water at shared edges',{skip:!sourceRoot&&'Set LADIR_WEB_SOURCE to run the canonical web-generator parity test'},async()=>{
 const fromWeb=path=>import(pathToFileURL(resolve(sourceRoot,path)).href);
 const {deserializePlanet}=await fromWeb('io/bundle.mjs'),{sampleTerrain}=await fromWeb('core/terrain.mjs'),{referenceDirection}=await fromWeb('core/landforms.mjs');
 const world=await deserializePlanet(await readFile(resolve(sourceRoot,'fixtures/river-v42.planet')));
 const left=packTile(world,0,0,8,2000,-2000,-4000),right=packTile(world,1,0,8,2000,0,-4000),b=left.buffer,count=81;
 assert.equal(b.toString('ascii',0,8),'LADIR001');assert.equal(b.readDoubleLE(24),4000000);assert.equal(b.readFloatLE(12),250);assert.equal(b.length,40+count*12);
 for(let i=0;i<=8;i++){
  const li=i*9+8,ri=i*9;
  assert.equal(b.readFloatLE(40+li*4),right.buffer.readFloatLE(40+ri*4));
  assert.equal(b.readFloatLE(40+count*4+li*4),right.buffer.readFloatLE(40+count*4+ri*4));
  const canonical=sampleTerrain(world,referenceDirection(world,0,-4000+i*250));
  assert.equal(b.readFloatLE(40+li*4),Math.fround(canonical.elevationMeters));
  assert.equal(b.readFloatLE(40+count*4+li*4),Math.fround(canonical.waterSurfaceMeters));
 }
});
