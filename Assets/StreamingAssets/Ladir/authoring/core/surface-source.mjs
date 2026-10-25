// Runtime-only samplers never become part of a serialized world or its hash.
const sources=new WeakMap();
export const terrainSource=world=>sources.get(world);
export function setTerrainSource(world,source){if(source)sources.set(world,source);else sources.delete(world);}
