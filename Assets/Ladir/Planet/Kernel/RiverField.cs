using System;
using System.Collections.Generic;
using static Ladir.Planet.PM;

namespace Ladir.Planet
{
    public struct RiverSample
    {public double distance,width,water,depth,valley,edge,flow,t;public string id;public bool regional;}
    public sealed class RiverField
    {
        const int Bins=1024;
        readonly PlanetWorld w;
        readonly Dictionary<int,List<Segment>> index=new Dictionary<int,List<Segment>>();
        struct Segment {public RiverPoint a,b;public River river;public D3 p,delta,min,max;public double inv;}
        static int Key(D3 d){FaceUV(d,out int f,out double u,out double v);return f*Bins*Bins+(int)Math.Min(Bins-1,Math.Floor((v+1)*Bins/2))*Bins+(int)Math.Min(Bins-1,Math.Floor((u+1)*Bins/2));}
        public RiverField(PlanetWorld world)
        {
            w=world;
            foreach(var river in w.hydrology.rivers)if(river.network)for(int i=0;i<river.points.Length-1;i++)
            {
                var a=river.points[i];var b=river.points[i+1];D3 p=a.D,q=b.D,delta=q-p;double pad=(3200+Math.Max(a.widthMeters,b.widthMeters)/2)/w.radiusMeters;
                var seg=new Segment{a=a,b=b,river=river,p=p,delta=delta,inv=1/D3.Dot(delta,delta),min=new D3(Math.Min(p.x,q.x)-pad,Math.Min(p.y,q.y)-pad,Math.Min(p.z,q.z)-pad),max=new D3(Math.Max(p.x,q.x)+pad,Math.Max(p.y,q.y)+pad,Math.Max(p.z,q.z)+pad)};
                FaceUV((p+q).Unit,out int f,out double u,out double v);double extent=Math.Acos(Clamp(D3.Dot(p,q),-1,1))*1.8+6000/w.radiusMeters,step=2.0/Bins;var seen=new HashSet<int>();
                for(double y=-extent-step;y<=extent+step;y+=step)for(double x=-extent-step;x<=extent+step;x+=step)
                {int key=Key(Face(f,u+x,v+y));if(!seen.Add(key))continue;if(!index.TryGetValue(key,out var list)){list=new List<Segment>();index.Add(key,list);}list.Add(seg);}
            }
        }
        public RiverSample? Network(D3 d)
        {
            if(!index.TryGetValue(Key(d),out var list))return null;RiverSample? best=null;
            foreach(var s in list)
            {
                if(d.x<s.min.x||d.x>s.max.x||d.y<s.min.y||d.y>s.max.y||d.z<s.min.z||d.z>s.max.z)continue;
                double t=Clamp(D3.Dot(d-s.p,s.delta)*s.inv),dist=(d-(s.p+s.delta*t).Unit).Length*w.radiusMeters,width=Mix(s.a.widthMeters,s.b.widthMeters,t),edge=dist-width/2;
                if(edge<3000&&(!best.HasValue||edge<best.Value.edge))best=new RiverSample{edge=edge,distance=dist,width=width,water=Mix(s.a.waterLevelMeters,s.b.waterLevelMeters,t),depth=Mix(s.a.depthMeters,s.b.depthMeters,t),valley=Mix(s.a.valleyWidthMeters,s.b.valleyWidthMeters,t),id=s.river.id,flow=s.river.flow,regional=s.river.regional,t=t};
            }
            return best;
        }
        public RiverSample Local(River river,double x,double y)
        {
            double t=Clamp((river.startY-y)/(river.startY-river.endY));int n=river.points.Length-1,k=Math.Min(n-1,(int)Math.Floor(t*n)),window=(int)Math.Ceiling(4800/(w.radiusMeters*(river.startY-river.endY))*n)+2;
            var best=new RiverSample{distance=double.PositiveInfinity};
            for(int i=Math.Max(0,k-window);i<=Math.Min(n-1,k+window);i++)
            {var a=river.points[i];var b=river.points[i+1];double vx=b.x-a.x,vy=b.y-a.y,f=Clamp(((x-a.x)*vx+(y-a.y)*vy)/(vx*vx+vy*vy)),dist=Hypot(x-Mix(a.x,b.x,f),y-Mix(a.y,b.y,f))*w.radiusMeters;
             if(dist<best.distance)best=new RiverSample{distance=dist,water=Mix(a.waterLevelMeters,b.waterLevelMeters,f),width=Mix(a.widthMeters,b.widthMeters,f),depth=Mix(a.depthMeters,b.depthMeters,f),flow=river.flow,id=river.id,t=(i+f)/n};}
            return best;
        }
    }
}
