import {readFile,writeFile} from 'node:fs/promises';
import {generatePlanet} from './core/world.mjs';
import {serializePlanet} from './io/bundle.mjs';
const config=JSON.parse(await readFile(process.argv[2],'utf8'));
const world=await generatePlanet(config,{onProgress:p=>console.log(JSON.stringify(p))});
await writeFile(process.argv[3],new Uint8Array(serializePlanet(world)));
console.log(JSON.stringify({complete:true,worldDataHash:world.manifest.worldDataHash}));
