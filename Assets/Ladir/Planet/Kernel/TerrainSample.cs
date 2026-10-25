using System;
using static Ladir.Planet.PM;

namespace Ladir.Planet
{
    public sealed partial class TerrainKernel
    {
        public SurfaceSample Sample(D3 d,bool micro=true)
        {
            var geo=World.Dry?Geology(d):(0.0,0.0,1.0);double h=World.Dry?geo.Item1:Base(d),water=World.Dry?-100000:C.seaLevel,protect=0,flow=0,riverDistance=1e12;string feature=null;var p=World.Local(d);
            if(p.z>.3)
            {
                foreach(var river in World.hydrology.rivers)if(!river.network)
                {
                    if(p.y>river.startY+4800/R||p.y<river.endY-4800/R)continue;
                    var r=rivers.Local(river,p.x,p.y);double half=r.width/2;
                    if(r.distance<half+4200)
                    {
                        double valley=1-Smooth(half+100,half+4200,r.distance);h=Math.Min(h,Mix(h,r.water+30+(r.distance-half)*.18,valley));double carve=1-Smooth(half,half+130,r.distance);
                        bool ford=World.crossings.Length>0&&river.id==World.crossings[0].riverId&&Math.Abs(r.t-World.crossings[0].riverT)<.005;
                        double bed=r.water-(ford?.55:r.depth);h=Math.Min(h,Mix(h,bed+Math.Pow(Clamp(r.distance/half),4)*(ford?.25:r.depth*.45),carve));protect=Math.Max(protect,1-Smooth(half+130,half+260,r.distance));
                        if(r.distance<half){water=Math.Max(water,r.water);feature=r.id;flow=r.flow;}riverDistance=Math.Min(riverDistance,r.distance);
                    }
                }
                foreach(var lake in World.hydrology.lakes)
                {
                    double r=Hypot(p.x-lake.x,p.y-lake.y)*R,angle=Math.Atan2(p.y-lake.y,p.x-lake.x),phase=(S%997)/997.0*Math.PI*2,shore=lake.radiusMeters*(1+.12*Math.Sin(angle*3+phase)+.065*Math.Sin(angle*7-phase)+.035*Math.Cos(angle*11+phase));
                    if(lake.regional)
                    {double weight=1-Smooth(shore,shore+1800,r),target=lake.waterLevelMeters+(r<shore?-lake.depthMeters*(1-Sq(r/shore)):Math.Min(200,(r-shore)*.18));h=Mix(h,target,weight);protect=Math.Max(protect,weight);if(r<shore){water=Math.Max(water,lake.waterLevelMeters);feature=lake.id;}continue;}
                    double mask=1-Smooth(shore*.92,shore*1.55,r);if(mask>0){h=Mix(h,lake.waterLevelMeters-lake.depthMeters+Math.Pow(Clamp(r/shore),3)*lake.depthMeters*.65,mask);protect=Math.Max(protect,mask);if(h<lake.waterLevelMeters){water=Math.Max(water,lake.waterLevelMeters);feature=lake.id;}}
                }
                if(World.crossings.Length>0&&World.hydrology.rivers.Length>0)
                {
                    var ford=World.crossings[0];var main=World.hydrology.rivers[0];var point=main.points[(int)Math.Floor(ford.riverT*(main.points.Length-1)+.5)];double along=Math.Abs(p.y-point.y)*R,across=Math.Abs(p.x-point.x)*R;
                    if(along<160&&across<point.widthMeters*1.3){var r=rivers.Local(main,p.x,p.y);double half=r.width/2,weight=1-Smooth(70,160,along),bed=r.water+(r.distance<half?-.55:.16*(r.distance-half)+.3);h=Mix(h,bed,weight);protect=Math.Max(protect,weight);if(r.distance<half){water=r.water;feature=main.id;}}
                }
            }
            var net=rivers.Network(d);
            if(net.HasValue)
            {
                double prior=h;var r=net.Value;double half=r.width/2,bank=r.id.StartsWith("stream",StringComparison.Ordinal)?55:220,valley=1-Smooth(half+bank,half+2200,r.distance);
                h=Math.Min(h,Mix(h,r.water+20+Math.Max(0,r.distance-half)*.15,valley));double carve=1-Smooth(half,half+bank,r.distance);h=Math.Min(h,Mix(h,r.water-r.depth+Math.Pow(Clamp(r.distance/half),4)*r.depth*.4,carve));
                if(r.regional){double shaped=Mix(h,r.water+4+Math.Max(0,r.distance-half)*.24,1-Smooth(half+80,half+r.valley/2,r.distance));if(r.distance<half+100)shaped=Mix(shaped,r.water-r.depth+Math.Pow(Clamp(r.distance/half),4)*r.depth*.45,1-Smooth(half,half+100,r.distance));h=feature!=null?Math.Min(prior,shaped):shaped;}
                protect=Math.Max(protect,1-Smooth(half+bank,half+bank+80,r.distance));if(r.distance<half){water=Math.Max(water,r.water);feature=r.id;flow=r.flow;}riverDistance=Math.Min(riverDistance,r.distance);
            }
            double temperature=C.temperature-36*Math.Abs(d.y)-Math.Max(h,0)*.0055,dryBelt=Math.Exp(-Sq((Math.Abs(d.y)-.40)/.19)),moisture=Clamp(.40+Fbm(d,7.2,World.climateSeed,4)*1.1+C.rainfall*.32-dryBelt*.29+Math.Exp(-riverDistance/1400)*.28);
            if(micro&&h>water)h+=(Fbm(d,R/900,S+141,3)*7+stamps.Value.Detail(d)*Smooth(5,80,h-water)*(1-geo.Item2))*(1-protect)*DetailFactor(d);
            double snowLine=C.seaLevel+Math.Max(450,2450+(C.temperature-30)*40-Math.Abs(d.y)*1500)+Fbm(d,R/18000,World.climateSeed+811,3)*170,snow=!World.Dry&&h>water?Smooth(snowLine-140,snowLine+260,h):0;
            int biome=h<=water?(feature!=null?8:0):h<water+22?1:snow>.55?6:h>3600?10:moisture>.59?(temperature<10?4:3):moisture<.29?9:moisture<.40?7:2;
            if(biome==9&&micro){double dune=1-Math.Abs(Math.Sin((d.x+d.z*.47)*R/170+Fbm(d,R/5000,S+340,3)*3));h+=dune*dune*12*(1-protect)*DetailFactor(d);}
            double lava=0,albedo=1;
            switch(C.worldType)
            {case "moon":biome=14;albedo=geo.Item3;break;case "mars":biome=13;albedo=.65+.50*Smooth(-100,1700,h);break;case "desert":biome=Fbm(d,R/17000,S+310,4)>.03?10:9;break;case "volcanic":lava=geo.Item2;biome=lava>.45?12:11;break;case "alien":if(biome!=0&&biome!=8&&biome!=6&&biome!=1)biome=Fbm(d,18,World.climateSeed+76,3)>0?15:16;break;}
            var sample=new SurfaceSample{height=h,water=water,temperature=temperature,moisture=moisture,biome=biome,protection=protect,feature=feature,flow=flow,lava=lava,albedo=albedo,snow=snow,snowLine=snowLine};
            if(!World.Dry&&sample.Depth==0&&protect<=.12&&temperature>=1&&h<=2600&&(biome==2||biome==3||biome==4||biome==15||biome==16))
            {double region=Smooth(-.16,.23,Fbm(d,R/48000,World.vegetationSeed,3)),grove=Smooth(-.12,.20,Fbm(d,R/2300,World.vegetationSeed+29,3));sample.forest=Clamp(C.forestDensity*1.65*region*grove*Smooth(45,260,h-C.seaLevel)*Smooth(.34,.68,moisture));}
            return sample;
        }
        static readonly D3[] palette={new D3(.012,.052,.085),new D3(.48,.43,.29),new D3(.10,.17,.065),new D3(.065,.115,.036),new D3(.052,.095,.054),new D3(.30,.30,.275),new D3(.75,.83,.85),new D3(.40,.32,.17),new D3(.018,.17,.19),new D3(.57,.37,.17),new D3(.34,.28,.23),new D3(.045,.043,.048),new D3(.46,.065,.008),new D3(.39,.135,.065),new D3(.28,.285,.29),new D3(.24,.065,.33),new D3(.065,.34,.29),new D3(.72,.65,.49)};
        public static D3 Color(SurfaceSample s)
        {D3 c=palette[s.biome==6?5:s.biome];double t=Clamp((s.height-1800)/2700)*.6;if(s.biome>1&&s.biome<8&&s.biome!=6)c=c+(palette[5]-c)*t;c*=((1-s.forest*.48)*s.albedo);if(s.snow>0)c=c+(palette[6]-c)*s.snow;return c;}
        public D3 Normal(D3 d,double metres=10)
        {D3 axis=Math.Abs(d.y)>.95?new D3(1,0,0):new D3(0,1,0),e=D3.Cross(axis,d).Unit,n=D3.Cross(d,e),de=(d+e*(metres/R)).Unit,dn=(d+n*(metres/R)).Unit;var p=d*(R+Sample(d).height);return D3.Cross(de*(R+Sample(de).height)-p,dn*(R+Sample(dn).height)-p).Unit;}
    }
}
