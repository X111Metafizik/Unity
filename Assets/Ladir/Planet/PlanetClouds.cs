using UnityEngine;
using System.Threading.Tasks;
namespace Ladir.Planet
{
    public sealed class PlanetClouds : MonoBehaviour
    {
        PlanetStudio app;Texture3D noise;Color32[] densityBytes;Task<Color32[]> job;double skyTime,nextFlash=5,flashUntil;D3 storm;LineRenderer lightning;Material boltMaterial;AudioSource thunderAudio;AudioClip thunder;
        public void Initialize(PlanetStudio studio,Material unused)
        {
            app=studio;var bytes=studio.volumeNoise.bytes;job=Task.Run(()=>{const int N=64;var data=new Color32[N*N*N];for(int i=0;i<data.Length;i++)data[i]=new Color32(bytes[i*4],bytes[i*4+1],bytes[i*4+2],bytes[i*4+3]);return data;});
            lightning=new GameObject("Storm lightning · geometry, no billboards").AddComponent<LineRenderer>();lightning.positionCount=0;lightning.startWidth=8;lightning.endWidth=3;lightning.useWorldSpace=true;boltMaterial=new Material(Shader.Find("Ladir/FlightLine"));lightning.sharedMaterial=boltMaterial;lightning.startColor=new Color(3,4,6,1);lightning.endColor=new Color(1,2,4,.6f);thunderAudio=gameObject.AddComponent<AudioSource>();thunderAudio.spatialBlend=0;var samples=new float[44100];uint seed=33;float low=0;for(int i=0;i<samples.Length;i++){seed=seed*1664525+1013904223;low=Mathf.Lerp(low,(seed>>8)/16777216f-.5f,.04f);samples[i]=low*Mathf.Exp(-i/12000f)*2;}thunder=AudioClip.Create("Storm thunder",samples.Length,1,22050,false);thunder.SetData(samples,0);
        }
        void Update()
        {
            if(job!=null&&job.IsCompleted){densityBytes=job.Result;noise=new Texture3D(64,64,64,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear};noise.SetPixels32(densityBytes);noise.Apply(false,true);Shader.SetGlobalTexture("_LadirNoise3D",noise);job=null;}
            if(app.Kernel==null||!noise)return;if(!app.pausedSky)skyTime+=Time.unscaledDeltaTime;
            double altitude=app.CameraPosition.Length-app.Kernel.World.radiusMeters;float fade=1-(float)PM.Smooth(30000,38000,altitude);Shader.SetGlobalVector("_LadirWeather",new Vector4(app.clouds*fade,(float)skyTime,app.atmosphere,(float)app.Kernel.World.radiusMeters));
            if(skyTime>nextFlash&&app.clouds*fade>.15f&&altitude<25000)
            {
                nextFlash=skyTime+8+PM.Random((int)skyTime,2,7,93)*12;D3 center=app.CameraPosition.Unit,north=FlightModel.North(center),east=D3.Cross(north,center).Unit;double angle=PM.Random((int)skyTime,3,7,93)*System.Math.PI*2;
                // Select the storm from the very same low-frequency density field.
                D3 best=new D3();double density=0;for(int i=0;i<48;i++){double a=angle+i*2.399963,r=3500+(i*1543)%11500;D3 d=(center+(north*System.Math.Cos(a)+east*System.Math.Sin(a))*r/app.Kernel.World.radiusMeters).Unit;double sample=Density(d*(app.Kernel.World.radiusMeters+4200),app.clouds*fade);if(sample>density){best=d;density=sample;}}
                if(density>.55){storm=best*(app.Kernel.World.radiusMeters+4200);flashUntil=skyTime+.35;if(app.Sound)thunderAudio.PlayOneShot(thunder,.3f);}
            }
            bool flashing=skyTime<flashUntil;Shader.SetGlobalVector("_LadirLightning",new Vector4((float)-storm.x,(float)storm.y,(float)storm.z,flashing?(float)(.55+.45*System.Math.Sin(skyTime*130)):0));
            lightning.positionCount=flashing?12:0;if(flashing){D3 up=storm.Unit,north=FlightModel.North(up);for(int i=0;i<12;i++){double t=i/11.0;D3 p=storm-up*(t*2200)+north*(System.Math.Sin(i*3.9+flashUntil)*90*System.Math.Sin(t*System.Math.PI));lightning.SetPosition(i,Coordinates.Float(p-app.Origin));}}
        }
        // Match normalized, repeating hardware trilinear sampling, including the
        // half-texel offset. Storm positions and the raymarch use one density.
        double Noise(D3 p,bool fine=false)
        {
            double x=p.x*64-.5,y=p.y*64-.5,z=p.z*64-.5;int ix=(int)System.Math.Floor(x),iy=(int)System.Math.Floor(y),iz=(int)System.Math.Floor(z);double sum=0;
            double fx=x-ix,fy=y-iy,fz=z-iz;for(int k=0;k<2;k++)for(int j=0;j<2;j++)for(int i=0;i<2;i++){var color=densityBytes[(((iz+k)&63)*64+((iy+j)&63))*64+((ix+i)&63)];sum+=(fine?color.b:color.r)/255.0*(i==0?1-fx:fx)*(j==0?1-fy:fy)*(k==0?1-fz:fz);}return sum;
        }
        double Density(D3 physical,float coverage)
        {
            var p=physical/1000;double h=physical.Length-app.Kernel.World.radiusMeters;double layer=PM.Smooth(2000,3000,h)*(1-PM.Smooth(9000,12000,h));D3 drift=new D3(skyTime*.0001,0,skyTime*.00006),shape=p*.022+drift;
            return PM.Smooth(.68-coverage*.30,.82-coverage*.30,Noise(p*.001+drift*.1)*.35+Noise(shape)*.90-Noise(shape,true)*.12)*layer;
        }
        void OnDestroy(){if(noise)Destroy(noise);if(boltMaterial)Destroy(boltMaterial);if(thunder)Destroy(thunder);}
    }
}
