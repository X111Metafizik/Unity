using System;
using System.Collections.Generic;
using System.Threading;
namespace Ladir.Planet
{
    public sealed class NavigationResult {public bool passable;public string reason;public D3[] route,surface;public double length;}
    public static class NavigationField
    {
        // Same sampled crossing/depth guard as core/navigation.mjs; not an
        // invented path through a mountain or an automatically open ford.
        public static NavigationResult Query(TerrainKernel terrain,D3 from,D3 to,bool water,double draft=0,Crossing crossing=null,CancellationToken token=default)
        {
            double R=terrain.World.radiusMeters,length=Math.Acos(PM.Clamp(D3.Dot(from,to),-1,1))*R;int steps=Math.Max(1,(int)Math.Ceiling(length/25));var points=new List<D3>(Math.Min(steps+1,100000));var surface=new List<D3>();
            if(steps>100000)throw new ArgumentOutOfRangeException(nameof(to),"Geçit önizlemesi 2.500 km ile sınırlı.");
            for(int i=0;i<=steps;i++)
            {
                token.ThrowIfCancellationRequested();var d=(from*(1-i/(double)steps)+to*(i/(double)steps)).Unit;var s=terrain.Sample(d);bool ford=crossing?.direction!=null&&Math.Acos(PM.Clamp(D3.Dot(d,new D3(crossing.direction)),-1,1))*R<crossing.widthMeters*1.2&&s.Depth<1;
                if((!water&&s.Depth>.1&&!ford)||(water&&(s.Depth<=0||s.Depth<draft)))return new NavigationResult{passable=false,reason=water?"Yetersiz su derinliği":"Su engeli",route=points.ToArray()};points.Add(d);surface.Add(d*(R+(water?s.water:s.height)+18));
            }
            return new NavigationResult{passable=true,length=length,route=points.ToArray(),surface=surface.ToArray()};
        }
    }
}
