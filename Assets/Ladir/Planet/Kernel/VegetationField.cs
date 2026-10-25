using System;
using System.Collections.Generic;
using System.Threading;
namespace Ladir.Planet
{
    public struct TreeInstance {public D3 direction;public double height,elevation,rotation;public int species,id;}
    public static class VegetationField
    {
        // This is core/vegetation.mjs's stand-v3 address contract, in metres.
        public static TreeInstance[] Tile(TerrainKernel kernel,int face,int tx,int ty,int level,CancellationToken token=default)
        {
            const int grid=24;double size=2.0/(1<<level);uint seed=PM.HashString($"{kernel.World.vegetationSeed}/{face}/{level}/{tx}/{ty}/stand-v3");var items=new List<TreeInstance>();
            for(int y=0;y<grid;y++){token.ThrowIfCancellationRequested();for(int x=0;x<grid;x++)
            {
                int id=y*grid+x;double u=-1+(tx+(x+.15+PM.RandomAt(seed,id)*.7)/grid)*size,v=-1+(ty+(y+.15+PM.RandomAt((ulong)seed+1,id)*.7)/grid)*size;D3 d=PM.Face(face,u,v);var s=kernel.Sample(d);
                if(s.Depth>0||s.protection>.12||s.height>2800||s.temperature<0||s.forest<.04||PM.RandomAt((ulong)seed+2,id)>s.forest)continue;
                items.Add(new TreeInstance{id=id,direction=d,elevation=s.height,height=12+PM.RandomAt((ulong)seed+3,id)*19,species=s.biome==4?0:(int)Math.Floor(PM.RandomAt((ulong)seed+4,id)*3),rotation=PM.RandomAt((ulong)seed+5,id)*6.2831853});
            }}return items.ToArray();
        }
    }
}
