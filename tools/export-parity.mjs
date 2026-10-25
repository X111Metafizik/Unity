import {readFile,writeFile,mkdir,copyFile} from 'node:fs/promises';
import {resolve,join} from 'node:path';
import {pathToFileURL} from 'node:url';
import {createHash} from 'node:crypto';
const root=resolve(import.meta.dirname,'..'),source=resolve(process.argv[2]??process.env.LADIR_WEB_SOURCE??'');
const {deserializePlanet}=await import(pathToFileURL(join(source,'io/bundle.mjs')));
const {sampleProceduralTerrain,biomeColor,baseTerrain}=await import(pathToFileURL(join(source,'core/terrain.mjs')));
const {faceDirection}=await import(pathToFileURL(join(source,'core/cube.mjs')));
const {localDirection}=await import(pathToFileURL(join(source,'core/math.mjs')));
const {referenceDirection}=await import(pathToFileURL(join(source,'core/landforms.mjs')));
const path=join(source,'fixtures/river-v42.planet'),bytes=await readFile(path),world=await deserializePlanet(bytes);
const directions=[];
for(let f=0;f<6;f++)for(let y=0;y<5;y++)for(let x=0;x<5;x++)directions.push({name:`face-${f}-${x}-${y}`,d:faceDirection(f,-1+x*.5,-1+y*.5)});
for(let y=-15000;y<=15000;y+=5000)for(let x=-15000;x<=15000;x+=5000)directions.push({name:`reference-${x}-${y}`,d:referenceDirection(world,x,y)});
for(const r of world.hydrology.rivers.slice(0,12))for(let i=0;i<r.points.length;i+=Math.max(1,Math.floor(r.points.length/9)))directions.push({name:r.id+'-'+i,d:r.points[i].direction});
for(const lake of world.hydrology.lakes)for(let a=0;a<8;a++)directions.push({name:lake.id+'-'+a,d:localDirection(world,lake.x+Math.cos(a*Math.PI/4)*lake.radiusMeters/world.radiusMeters,lake.y+Math.sin(a*Math.PI/4)*lake.radiusMeters/world.radiusMeters)});
for(const l of world.landmarks??[])if(l.direction)directions.push({name:l.id??l.name,d:l.direction});
const cases=[];
for(const type of ['terran','alien','desert','mars','moon','volcanic']){
 const w={...world,generationConfig:{...world.generationConfig,worldType:type}};
 for(const {name,d} of directions){const s=sampleProceduralTerrain(w,d);cases.push({type,name,d,base:baseTerrain(w,d),height:s.elevationMeters,water:s.waterSurfaceMeters,biome:s.biome,forest:s.forest,snow:s.snow,lava:s.lava,color:biomeColor(s)});}
}
const out=join(root,'evidence/planet-port');await mkdir(out,{recursive:true});
const assets=join(root,'Assets/StreamingAssets/Ladir/planet');await mkdir(assets,{recursive:true});await copyFile(path,join(assets,'river-v42.planet'));
const sha=createHash('sha256').update(bytes).digest('hex');await writeFile(join(assets,'river-v42.planet.sha256'),sha+'\n');
await writeFile(join(out,'parity-fixtures.json'),JSON.stringify({worldFileSha256:sha,worldDataHash:world.manifest.worldDataHash,radius:world.radiusMeters,cases}));
console.log(JSON.stringify({cases:cases.length,sha,radius:world.radiusMeters,worldDataHash:world.manifest.worldDataHash}));
