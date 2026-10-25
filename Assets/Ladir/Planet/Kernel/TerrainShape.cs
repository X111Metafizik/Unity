using System;
using System.Threading;
using static Ladir.Planet.PM;

namespace Ladir.Planet
{
    public sealed partial class TerrainKernel : IDisposable
    {
        public readonly PlanetWorld World;
        readonly RiverField rivers;
        readonly ThreadLocal<StampField> stamps;
        int references=1,ownerReleased;
        PlanetConfig C=>World.generationConfig;
        double R=>World.radiusMeters;
        uint S=>World.terrainSeed;
        static readonly double[][] ridgeLines={
            new double[]{-15500,6500,-11000,10000,-6800,12400,-2600,15100},
            new double[]{-11000,10000,-8500,5500,-10500,1000,-13000,-2500},
            new double[]{-6800,12400,-4800,8200,-6500,3500},
            new double[]{2500,15100,5500,10500,10500,12500,15500,8500},
            new double[]{5500,10500,6200,6200,10000,3000,13000,0},
            new double[]{10500,12500,12000,7000,15500,4500}};
        public TerrainKernel(PlanetWorld w){World=w;stamps=new ThreadLocal<StampField>(()=>new StampField(w));rivers=new RiverField(w);}
        // Queued workers keep a lease so changing worlds releases the owner's
        // reference immediately without disposing a sampler still in use.
        public IDisposable Retain()
        {
            while(true)
            {
                int count=Volatile.Read(ref references);
                if(count==0||Volatile.Read(ref ownerReleased)!=0)throw new ObjectDisposedException(nameof(TerrainKernel));
                if(Interlocked.CompareExchange(ref references,count+1,count)==count)return new Lease(this);
            }
        }
        void Release(){if(Interlocked.Decrement(ref references)==0)stamps.Dispose();}
        public void Dispose(){if(Interlocked.Exchange(ref ownerReleased,1)==0)Release();}
        sealed class Lease:IDisposable
        {
            TerrainKernel owner;public Lease(TerrainKernel kernel){owner=kernel;}
            public void Dispose(){Interlocked.Exchange(ref owner,null)?.Release();}
        }
        public double ReferenceWeight(D3 d)
        {if(C.terrainModel!="watershed-2"||(C.worldType!="terran"&&C.worldType!="alien"))return 0;var p=World.Local(d);return p.z>.9?1-Smooth(16000,20500,Math.Max(Math.Abs(p.x*R+19200),Math.Abs(p.y*R-48000))):0;}
        double ReferenceHeight(D3 d,double original)
        {
            double weight=ReferenceWeight(d);if(weight==0)return original;var p=World.Local(d);double x=p.x*R+19200,y=p.y*R-48000,mountain=Smooth(-4000,10000,y),plain=(1-Smooth(-5000,1000,y))*(1-Smooth(-1000,5000,x));
            double h=1100+Clamp((y+19200)/35000)*1000+mountain*60+Fbm(d,R/4300,S+2801,3)*(12+mountain*130)*(1-plain*.85),uplift=0;
            for(int i=0;i<ridgeLines.Length;i++)
            {
                var line=ridgeLines[i];double dist=double.PositiveInfinity;for(int j=2;j<line.Length;j+=2){double ax=line[j-2],ay=line[j-1],dx=line[j]-ax,dy=line[j+1]-ay,t=Clamp(((x-ax)*dx+(y-ay)*dy)/(dx*dx+dy*dy));dist=Math.Min(dist,Hypot(x-ax-dx*t,y-ay-dy*t));}
                bool main=i==0||i==3;double width=main?2300:1450,peak=.73+.27*Sq(Math.Cos((x*.8+y*.55)/3300+i));uplift=Math.Max(uplift,Math.Exp(-Sq(dist/width))*(main?2800:1400)*peak);
            }
            h+=uplift*(.6+C.mountains*.6);double plateau=Smooth(3000,7000,x)*(1-Smooth(-2000,4000,y));h+=plateau*960;
            double axis=8400+1100*Math.Sin(y/3900)+400*Math.Sin(y/1450),across=Math.Abs(x-axis),end=1-Smooth(12500,16500,Math.Abs(y+3500));h-=870*(1-Smooth(150,1100,across))*end*plateau;return Mix(original,h,weight);
        }
        double DetailFactor(D3 d){double weight=ReferenceWeight(d);return weight==0?1:Mix(1,.025+.10*Smooth(-3000,8000,World.Local(d).y*R-48000),weight);}
        double Mountain(D3 d)
        {
            double h;
            if(C.terrainModel=="watershed-2")
            {
                var a=new D3(World.ridgeAxis);double along=D3.Dot(d,a)*R,across=(d.x*a.z-d.z*a.x)*R,province=Smooth(-.15,.22,Fbm(d,R/280000,S+2401,3)),fold=across+Fbm(d,R/48000,S+2402,3)*16000;
                double crest=Math.Exp(-Sq(Math.Sin(fold/14000)*14000/2700)),branches=Math.Exp(-Sq(Math.Sin((fold*.66+along*.5)/6300)*6300/1700)),peaks=.68+.32*Sq(Math.Sin(along/7900+Fbm(d,R/27000,S+2403,2)));
                h=550+province*(crest*3000*peaks+branches*620)+Fbm(d,R/2200,S+2404,3)*(14+province*95)-(1-province)*220;
                double blend=C.stampModel=="global-2"?.65:.2;return h*(1-blend)+stamps.Value.Relief(d,h)*blend;
            }
            uint seed=S+610;double pr=Smooth(-.32,.38,Fbm(d,R/95000,seed+83,3)),n=Fbm(d,R/21000,seed,3),cr=Clamp(1-(Math.Sqrt(n*n+.0036)-.06)/.38),secondary=Fbm(d,R/7300,seed+41,3),folds=Clamp(1-(Math.Sqrt(secondary*secondary+.0144)-.12)/.46),gullies=1-Smooth(.05,.30,Math.Abs(Fbm(d,R/4600,seed+191,2)));
            h=Math.Max(0,pr*650+Math.Pow(cr,2.1)*2900*(.28+.72*pr)+folds*folds*380*(.3+.7*pr)-gullies*130*(.4+C.erosion*.6)+Fbm(d,R/1900,seed+93,3)*75+Fbm(d,R/650,seed+117,2)*16);return stamps.Value.Relief(d,h);
        }
        double Rift(D3 d,double height)
        {
            if(height<=C.seaLevel)return height;var p=World.Local(d);double x=p.x*R+70000,y=p.y*R-20000,bend=6500*Math.Sin(x/18000)+1700*Math.Sin(x/8000);
            double main=(1-Smooth(600,5600,Math.Abs(y-bend)))*(1-Smooth(48000,80000,Math.Abs(x))),branch=(1-Smooth(350,3800,Math.Abs(y-bend-(15000-x)*.55)))*(1-Smooth(30000,55000,Math.Abs(x+10000)))*(1-Smooth(12000,20000,x));
            double local=p.z>.9?Math.Max(main,branch):0,plateau=p.z>.9?1-Smooth(.45,1.15,Hypot(x/100000,y/37000)):0,belt=1-Smooth(.035,.14,Math.Abs(Fbm(d,R/85000,S+971,3))),province=Smooth(.02,.28,Fbm(d,R/260000,S+973,3));
            double uplift=1400*plateau*Smooth(C.seaLevel,C.seaLevel+450,height),depth=Math.Max(local*1950,belt*province*1800*Smooth(C.seaLevel+1600,C.seaLevel+2800,height));return height+uplift-Math.Min(depth,Math.Max(0,height+uplift-C.seaLevel-95));
        }
        public double Base(D3 d)
        {
            if(World.Dry)return Geology(d).height;
            if(ReferenceWeight(d)==1)return ReferenceHeight(d,0);
            D3 q=d+new D3(Fbm(d,2.5,S+21,3),Fbm(d,2.5,S+31,3),Fbm(d,2.5,S+41,3))*.28,p=World.Local(d);
            double continental=Fbm(q,1.9,S,5)+.25*Fbm(q,4,S+8,3),home=p.z>0?Math.Exp(-(Sq(p.x+.10)/.36+Sq(p.y-.04)/.28)):0,island=p.z>0?Math.Exp(-(Sq(p.x-.83)/.18+Sq(p.y-.02)/.62)):0;
            double land=continental+C.landBalance*.58-.25+home*.67+island*.34,ridgeNoise=Fbm(q,7.2,S+53,5),belt=Math.Pow(1-Math.Abs(Math.Sin(D3.Dot(q,new D3(World.ridgeAxis))*5.7+Fbm(q,3,S+16,3)*4)),3),ridge=Math.Pow(Math.Max(0,1-Math.Abs(ridgeNoise*1.65)),3.2)*belt,plain=1-Smooth(.18,.68,Fbm(q,3.4,S+86,3)+.4),h=land*8500;
            if(h>0){h=h*.40*(1-plain*C.plains*.7)+ridge*C.mountains*2600*Smooth(0,.12,land);h+=Fbm(q,36,S+5,4)*450*(1-plain*.8)*(1-C.erosion*.5);h=6000*(1-Math.Exp(-Math.Max(0,h)/6000));h+=(Mountain(d)-1050)*C.mountains*(1-plain*C.plains*.86)*Smooth(0,900,h);}
            h=Rift(d,h);
            if(p.z>.3)
            {double scale=120000/R,x=p.x/scale,y=p.y/scale,a=Hypot((x-.68)/.28,(y-.44)/.26),b=Hypot((x-.68)/.30,(y+.42)/.31);h=Mix(h,-2100,1-Smooth(.72,1.08,Math.Min(a,b)));if(y>-.45&&y<.47){double width=.024+.05*Smooth(.02,.36,Math.Abs(y)),channel=1-Smooth(width,width+.075,Math.Abs(x-(.68+.018*Math.Sin(y*9))));h=Mix(h,C.seaLevel-180-700*Smooth(.02,.4,Math.Abs(y)),channel);}}
            if(World.Erosion!=null&&World.FaceNodes!=null)
            {
                FaceUV(d,out int face,out double u,out double v);int r=C.analysisResolution;double fx=(u+1)*r*.5,fy=(v+1)*r*.5;int ix=Math.Min(r-1,(int)Math.Floor(fx)),iy=Math.Min(r-1,(int)Math.Floor(fy)),a=face*(r+1)*(r+1)+iy*(r+1)+ix;double tx=fx-ix,ty=fy-iy;var ids=World.FaceNodes;var delta=World.Erosion;
                double sx=p.x*R/120000,sy=p.y*R/120000;bool protectedStrait=p.z>.3&&Math.Abs(sx-(.68+.018*Math.Sin(sy*9)))<.11&&Math.Abs(sy)<.6;
                if(!protectedStrait)h+=Mix(Mix(delta[ids[a]],delta[ids[a+1]],tx),Mix(delta[ids[a+r+1]],delta[ids[a+r+2]],tx),ty);
            }
            return ReferenceHeight(d,h);
        }
        (double height,double rim) Craters(D3 d,double spacing=45000)
        {
            double f=R/spacing;D3 p=d*f;int cx=(int)Math.Floor(p.x),cy=(int)Math.Floor(p.y),cz=(int)Math.Floor(p.z);double h=0,rim=0;
            for(int z=-1;z<=1;z++)for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)
            {int a=cx+x,b=cy+y,c=cz+z;D3 center=new D3(a+.15+Random(a,b,c,S)*.7,b+.15+Random(a,b,c,S+3)*.7,c+.15+Random(a,b,c,S+9)*.7);if(Math.Abs(center.Length-f)>.44)continue;double radius=.10+Random(a,b,c,S+7)*.28,dist=(p-center.Unit*f).Length/radius;if(dist>1.5)continue;double depth=spacing*radius*.075,lip=Math.Exp(-Sq((dist-1.03)/.12));h+=-depth*(1-Smooth(.12,1,dist))+depth*.30*lip;rim=Math.Max(rim,lip);}
            return(h,rim);
        }
        (double height,double lava,double albedo) Geology(D3 d)
        {
            var p=World.Local(d);double mx=p.x*R,my=p.y*R,broad=Fbm(d,5,S+300,4),rock=Fbm(d,R/17000,S+310,4),mesa=Smooth(-.025,.055,rock),detail=Fbm(d,R/1300,S+311,3)*22,h=650+broad*1100+mesa*850+detail,lava=0;
            if(C.worldType=="moon")
            {var a=Craters(d);var b=Craters(d,6500);double r=p.z>.9?Hypot(mx,my)/7500:100,hero=-460*(1-Smooth(.12,1,r))+145*Math.Exp(-Sq((r-1.03)/.1));return(650+broad*1500+rock*120+a.height+b.height+detail*.2+hero+(stamps.Value.Relief(d,1000)-1000)*.12*Smooth(1.3,2.2,r),0,.68+Smooth(-.18,.2,broad)*.35+a.rim*.20+b.rim*.12-.18*(1-Smooth(.65,1,r))+.18*Math.Exp(-Sq((r-1.03)/.09)));}
            if(C.worldType=="mars"){double canyon=1-Smooth(.025,.11,Math.Abs(Fbm(d,R/100000,S+337,3))),hero=p.z>.9?1-Smooth(1800,6500,Math.Abs(my-2200*Math.Sin(mx/17000))):0;h=900+broad*1300+mesa*550+Mountain(d)*.65-Math.Max(canyon,hero)*1150+Craters(d).height*.5+detail;}
            if(C.worldType=="desert"){double dune=1-Math.Abs(Math.Sin((d.x+d.z*.47)*R/240+Fbm(d,R/7000,S+340,3)*4)),channel=1-Smooth(.025,.14,Math.Abs(Fbm(d,R/29000,S+337,3)));h=550+broad*800+mesa*650+Mountain(d)*.48-channel*780+dune*dune*24*(1-mesa)*(1-channel)+detail*.35;}
            if(C.worldType=="volcanic")
            {double angle=Math.Atan2(my,mx),r=p.z>.9?Hypot(mx,my)/(1+.12*Math.Sin(angle*3)+.075*Math.Cos(angle*7)+.025*Math.Sin(angle*11)):1e9,province=1-Smooth(8500,25000,r),bowl=1-Smooth(2200,4600,r),rim=Math.Exp(-Sq((r-4900)/950)),basin=1-Smooth(.035,.17,Math.Abs(Fbm(d,R/90000,S+371,3))),relief=Mountain(d),fissure=1-Smooth(.006,.021,Math.Abs(Fbm(d,R/8500,S+377,3)));
             h=620+broad*750+relief*.8*(1-province*.85)-basin*380-province*470-bowl*800+rim*(460+rock*180+relief*.30)+detail*(1-bowl)+Fbm(d,R/1800,S+391,3)*260*province*(1-bowl);double pool=1-Smooth(1650,2250,r);lava=Math.Max(fissure*.78*basin*(1-Smooth(250,1050,h))*(1-province),pool);h=Mix(h,620+Fbm(World.Center,5,S+300,4)*750-380*(1-Smooth(.035,.17,Math.Abs(Fbm(World.Center,R/90000,S+371,3))))-1270,pool);}
            return(C.worldType=="mars"||C.worldType=="desert"?Rift(d,h):h,lava,1);
        }
    }
}
