using System;
using System.Collections.Generic;
using static Ladir.Planet.PM;

namespace Ladir.Planet
{
    // Each terrain worker owns this small cache; immutable source samples are shared.
    public sealed class StampField
    {
        readonly PlanetWorld w;
        readonly Dictionary<(int,int,int,int),Descriptor[]> cells=new Dictionary<(int,int,int,int),Descriptor[]>();
        readonly Queue<(int,int,int,int)> order=new Queue<(int,int,int,int)>();
        static readonly int[] widths={512,512,1024,512},offsets={0,262144,524288,1572864};
        static readonly double[] means={.3332221806049347,.23085933923721313,.46916377544403076,.27031996846199036};
        static readonly double[] spacings={220000,22000,700},globalScale={.25,.08,.02},legacyScale={.02,.002,.0001};
        struct Descriptor { public D3 p,e,n;public int index; }
        public StampField(PlanetWorld world){w=world;}
        public double Sample(int index,double u,double v)
        {
            int N=widths[index],off=offsets[index];double x=Clamp(u)*(N-1),y=Clamp(v)*(N-1);int ix=(int)Math.Floor(x),iy=(int)Math.Floor(y);
            double tx=x-ix,ty=y-iy,a=1-tx,b=1-ty,w0=a*a*a/6,w1=(3*tx*tx*tx-6*tx*tx+4)/6,w2=(-3*tx*tx*tx+3*tx*tx+3*tx+1)/6,w3=tx*tx*tx/6,h=0;
            int x0=Math.Max(0,ix-1),x2=Math.Min(N-1,ix+1),x3=Math.Min(N-1,ix+2);
            for(int j=0;j<4;j++) {int row=off+(int)Clamp(iy+j-1,0,N-1)*N;double weight=j==0?b*b*b/6:j==1?(3*ty*ty*ty-6*ty*ty+4)/6:j==2?(-3*ty*ty*ty+3*ty*ty+3*ty+1)/6:ty*ty*ty/6;h+=(w.Stamps[row+x0]*w0+w.Stamps[row+ix]*w1+w.Stamps[row+x2]*w2+w.Stamps[row+x3]*w3)*weight;}
            return h/65535;
        }
        public double Band(D3 d,int band)
        {
            if(w.Stamps==null)return 0;
            double spacing=Math.Min(spacings[band],w.radiusMeters*(w.generationConfig.stampModel=="global-2"?globalScale:legacyScale)[band]);
            double f=w.radiusMeters/spacing;D3 p=d*f;int x=(int)Math.Floor(p.x+.5),y=(int)Math.Floor(p.y+.5),z=(int)Math.Floor(p.z+.5);var key=(band,x,y,z);
            if(!cells.TryGetValue(key,out var list))
            {
                var build=new List<Descriptor>(16);uint seed=unchecked(w.terrainSeed+(uint)(1709+band*173));
                for(int k=-1;k<=1;k++)for(int j=-1;j<=1;j++)for(int i=-1;i<=1;i++)
                {
                    int ax=x+i,ay=y+j,az=z+k;D3 c=new D3(ax,ay,az);double len=c.Length;if(len==0||Math.Abs(len-f)>=1.08)continue;
                    D3 q=c/len,axis=Math.Abs(q.y)>.95?new D3(1,0,0):new D3(0,1,0),e=D3.Cross(axis,q).Unit,n=D3.Cross(q,e);
                    double a=Random(ax,ay,az,seed+11)*Math.PI*2,cs=Math.Cos(a),sn=Math.Sin(a);
                    build.Add(new Descriptor{p=c,e=e*cs+n*sn,n=n*cs-e*sn,index=Math.Min(3,(int)Math.Floor(Random(ax,ay,az,seed)*4))});
                }
                list=build.ToArray();cells.Add(key,list);order.Enqueue(key);if(order.Count>1536)cells.Remove(order.Dequeue());
            }
            double sum=0,total=0;foreach(var t in list)
            {D3 delta=p-t.p;double r=delta.Length/1.08;if(r>=1)continue;double weight=1-Fade(Clamp((r-.48)/.52));sum+=(Sample(t.index,D3.Dot(delta,t.e)/2.16+.5,D3.Dot(delta,t.n)/2.16+.5)-means[t.index])*weight;total+=weight;}
            return total>1e-12?sum/total:0;
        }
        public double Relief(D3 d,double original)
        {
            if(w.Stamps==null||w.generationConfig.terrainStamps<=0)return original;
            if(w.generationConfig.stampLayers<2)throw new NotSupportedException("Tek katmanlı eski stamp dünyası için açık dönüştürme gerekli.");
            double relief=original*.40+1000+(Band(d,0)*2800+Band(d,1)*5600)*Math.Min(1,w.radiusMeters/2000000);
            return Mix(original,relief,w.generationConfig.terrainStamps);
        }
        public double Detail(D3 d)=>w.generationConfig.stampLayers>=3?Band(d,2)*85*w.generationConfig.terrainStamps*Math.Min(1,w.radiusMeters/2000000):0;
    }
}
