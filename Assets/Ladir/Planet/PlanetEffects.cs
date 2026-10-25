using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ladir.Planet
{
    public sealed class PlanetEffects : MonoBehaviour
    {
        PlanetStudio app;Transform ship;Transform[] enemies=new Transform[6],lights=new Transform[12];ParticleSystem[] engines=new ParticleSystem[2],bursts=new ParticleSystem[16];double[] burstLife=new double[16];Transform tube,wave;Mesh lines;Vector3[] positions=new Vector3[(112+128)*2];Color[] colors=new Color[(112+128)*2];int[] indices=new int[(112+128)*2];Material lineMaterial;AudioSource engineAudio,shotAudio;AudioClip gunClip,missileClip,explosionClip;float boostAmount;double travel;Volume volume;Bloom bloom;ChromaticAberration aberration;MotionBlur blur;
        public void Initialize(PlanetStudio studio,Transform player,Material thruster,Material light,Material warp)
        {
            app=studio;ship=player;
            for(int i=0;i<2;i++)
            {var obj=new GameObject("Ion plume · 1/4 scale");obj.transform.SetParent(ship,false);obj.transform.localPosition=new Vector3(i==0?-20.28125f:20.28125f,-1.71875f,i==0?-36.4f:-36.8f);obj.transform.localRotation=Quaternion.Euler(0,180,0);engines[i]=MakeParticles(obj,thruster,96,.16f,85,2.5f);var shape=engines[i].shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=5;shape.radius=1.15f;var render=obj.GetComponent<ParticleSystemRenderer>();render.renderMode=ParticleSystemRenderMode.Stretch;render.lengthScale=2;render.velocityScale=.02f;}
            var lightPositions=new[]{new Vector3(-31,7,-5),new Vector3(31,7,-5),new Vector3(-21,9,22),new Vector3(21,9,22),new Vector3(-6,15,-18),new Vector3(6,15,-18),new Vector3(-20,2,-34),new Vector3(20,2,-34),new Vector3(-12,9,3),new Vector3(12,9,3),new Vector3(-4,10,34),new Vector3(4,10,34)};
            for(int i=0;i<12;i++){var obj=GameObject.CreatePrimitive(PrimitiveType.Sphere);Destroy(obj.GetComponent<Collider>());lights[i]=obj.transform;lights[i].SetParent(ship,false);lights[i].localPosition=lightPositions[i];lights[i].localScale=Vector3.one*(i<2?1.1f:.65f);var mr=obj.GetComponent<MeshRenderer>();mr.sharedMaterial=light;var prop=new MaterialPropertyBlock();var color=i==0?new Color(5,.18f,.12f):i==1?new Color(.18f,5,1.4f):i<4?new Color(5,2.4f,.35f):new Color(.4f,4.8f,6);prop.SetColor("_EmissionColor",color);mr.SetPropertyBlock(prop);mr.shadowCastingMode=ShadowCastingMode.Off;}
            for(int i=0;i<enemies.Length;i++){var obj=new GameObject("Raider "+(i+1));obj.AddComponent<MeshFilter>().sharedMesh=app.shipMesh;var mr=obj.AddComponent<MeshRenderer>();mr.sharedMaterial=app.shipMaterial;var prop=new MaterialPropertyBlock();prop.SetColor("_BaseColor",new Color(.62f,.32f,.25f));mr.SetPropertyBlock(prop);enemies[i]=obj.transform;enemies[i].localScale=Vector3.one*.68f;obj.SetActive(false);}
            for(int i=0;i<bursts.Length;i++){var obj=new GameObject("Pooled explosion "+i);bursts[i]=MakeParticles(obj,thruster,96,.8f,120,12);var emission=bursts[i].emission;emission.rateOverTime=0;var main=bursts[i].main;main.loop=false;main.startColor=new Color(2,.65f,.12f,.9f);bursts[i].Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);}
            var lineObject=new GameObject("Streaks and projectile tracers · pooled");lines=new Mesh{name="Flight lines"};lines.MarkDynamic();for(int i=0;i<indices.Length;i++)indices[i]=i;lines.vertices=positions;lines.colors=colors;lines.SetIndices(indices,MeshTopology.Lines,0);lines.bounds=new Bounds(Vector3.zero,Vector3.one*20000000);lineObject.AddComponent<MeshFilter>().sharedMesh=lines;lineMaterial=new Material(Shader.Find("Ladir/FlightLine"));lineObject.AddComponent<MeshRenderer>().sharedMaterial=lineMaterial;
            tube=new GameObject("Transparent wormhole").transform;tube.gameObject.AddComponent<MeshFilter>().sharedMesh=TubeMesh();tube.gameObject.AddComponent<MeshRenderer>().sharedMaterial=warp;tube.gameObject.SetActive(false);
            wave=new GameObject("Warp charging wave").transform;wave.gameObject.AddComponent<MeshFilter>().sharedMesh=TubeMesh();wave.gameObject.AddComponent<MeshRenderer>().sharedMaterial=warp;wave.gameObject.SetActive(false);
            engineAudio=gameObject.AddComponent<AudioSource>();engineAudio.loop=true;engineAudio.spatialBlend=0;engineAudio.volume=.12f;engineAudio.clip=Synth(2,70,.4f);engineAudio.Play();shotAudio=gameObject.AddComponent<AudioSource>();shotAudio.spatialBlend=0;gunClip=Synth(.16f,380,.9f);missileClip=Synth(.6f,120,.9f);explosionClip=Synth(.9f,45,1);
            volume=new GameObject("LADİR grading and boost").AddComponent<Volume>();volume.isGlobal=true;volume.profile=ScriptableObject.CreateInstance<VolumeProfile>();bloom=volume.profile.Add<Bloom>();bloom.intensity.Override(.38f);bloom.threshold.Override(1.1f);bloom.scatter.Override(.65f);aberration=volume.profile.Add<ChromaticAberration>();aberration.intensity.Override(0);blur=volume.profile.Add<MotionBlur>();blur.intensity.Override(0);blur.quality.Override(MotionBlurQuality.Low);var tone=volume.profile.Add<Tonemapping>();tone.mode.Override(TonemappingMode.ACES);
        }
        static ParticleSystem MakeParticles(GameObject obj,Material mat,int max,float lifetime,float speed,float size)
        {
            var ps=obj.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var main=ps.main;main.simulationSpace=ParticleSystemSimulationSpace.Local;main.startLifetime=lifetime;main.startSpeed=speed;main.startSize=size;main.maxParticles=max;main.startColor=new Color(.4f,.8f,1,.8f);var emission=ps.emission;emission.rateOverTime=60;var col=ps.colorOverLifetime;col.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(.18f,.62f,1),1)},new[]{new GradientAlphaKey(.9f,0),new GradientAlphaKey(0,1)});col.color=gradient;obj.GetComponent<ParticleSystemRenderer>().sharedMaterial=mat;ps.Play();return ps;
        }
        static Mesh TubeMesh()
        {
            const int sides=48,rings=32;var vertices=new Vector3[(sides+1)*(rings+1)];var uv=new Vector2[vertices.Length];var tris=new int[sides*rings*6];int at=0;
            for(int j=0;j<=rings;j++)for(int i=0;i<=sides;i++){float a=i/(float)sides*Mathf.PI*2,z=j/(float)rings;int k=j*(sides+1)+i;float radius=45+Mathf.Pow(z,.7f)*250;vertices[k]=new Vector3(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius,z*4000-250);uv[k]=new Vector2(i/(float)sides,z);if(i<sides&&j<rings){tris[at++]=k;tris[at++]=k+sides+1;tris[at++]=k+1;tris[at++]=k+1;tris[at++]=k+sides+1;tris[at++]=k+sides+2;}}
            var mesh=new Mesh{name="Wormhole tube",vertices=vertices,uv=uv,triangles=tris};mesh.RecalculateBounds();return mesh;
        }
        static AudioClip Synth(float duration,float frequency,float noise)
        {
            int rate=22050,n=(int)(rate*duration);var data=new float[n];uint seed=13;for(int i=0;i<n;i++){seed=seed*1664525+1013904223;float t=i/(float)rate,envelope=duration>1?1:Mathf.Exp(-t*7);data[i]=(Mathf.Sin(t*frequency*Mathf.PI*2*(1-t*.2f))*.5f+((seed>>8)/16777216f-.5f)*noise)*envelope*.35f;}var clip=AudioClip.Create("LADIR synthesized audio",n,1,rate,false);clip.SetData(data,0);return clip;
        }
        public void PlayShot(bool missile){if(app.Sound)shotAudio.PlayOneShot(missile?missileClip:gunClip,missile?.28f:.12f);}
        public void ResetFlight(){travel=0;for(int i=0;i<burstLife.Length;i++)burstLife[i]=0;}
        void LateUpdate()
        {
            if(app==null)return;var f=app.Flight;bool active=app.Flying&&f!=null;engineAudio.mute=!app.Sound||!active;
            for(int i=0;i<enemies.Length;i++){enemies[i].gameObject.SetActive(active&&f.enemies[i].health>0&&(f.enemies[i].position-f.position).Length<15000);if(active){var e=f.enemies[i];enemies[i].position=Coordinates.Float(e.position-app.Origin);enemies[i].rotation=Quaternion.LookRotation(Coordinates.Float(e.forward),Coordinates.Float(e.position.Unit));}}
            if(!active){tube.gameObject.SetActive(false);wave.gameObject.SetActive(false);Array.Clear(positions,0,positions.Length);lines.vertices=positions;return;}
            float dt=Time.unscaledDeltaTime;boostAmount=Mathf.Lerp(boostAmount,f.boosting?1:0,1-Mathf.Exp(-dt*5));aberration.intensity.value=boostAmount*.22f;blur.intensity.value=boostAmount*.18f;app.Camera.fieldOfView=Mathf.Lerp(app.Camera.fieldOfView,64+boostAmount*9,1-Mathf.Exp(-dt*5));engineAudio.pitch=.7f+(float)Math.Min(1,f.speed/500)*.8f;engineAudio.volume=f.state=="landed"?.025f:.12f;
            foreach(var ps in engines){var emission=ps.emission;emission.enabled=f.state!="landed"&&f.state!="crashed";var main=ps.main;main.startSpeed=(float)(f.boosting?150:85);main.startSize=f.boosting?3:2.5f;}
            for(int i=0;i<lights.Length;i++)lights[i].gameObject.SetActive(f.health>0&&(i>1||Math.Sin(f.time*5+i)>-.7));
            if(!f.paused)travel+=f.speed*dt;D3 origin=f.position-app.Origin;
            for(int i=0;i<112;i++){double a=i*2.399963,r=85+(i*73)%149,z=((i*127.43-travel)%950+950)%950-400;D3 p=origin+f.right*(Math.Cos(a)*r)+f.up*(Math.Sin(a)*r)+f.forward*z;positions[i*2]=Coordinates.Float(p);positions[i*2+1]=Coordinates.Float(p-f.forward*(1.8+Math.Min(1,f.speed/500)*18));var c=new Color(.65f,.86f,.90f,(float)(f.state=="flying"?.055+Math.Min(1,f.speed/500)*.19:0));colors[i*2]=colors[i*2+1]=c;}
            for(int i=0;i<128;i++){var p=f.projectiles[i];int j=(112+i)*2;positions[j]=Coordinates.Float(p.position-app.Origin);positions[j+1]=Coordinates.Float(p.position-app.Origin-p.direction*(p.missile?24:38));colors[j]=colors[j+1]=p.life<=0?Color.clear:p.enemy?new Color(3,.35f,.12f,1):p.missile?new Color(3,1.2f,.25f,1):new Color(.4f,2,3,1);}
            lines.vertices=positions;lines.colors=colors;
            for(int i=0;i<bursts.Length;i++){var e=f.explosions[i];if(e.life>burstLife[i]){bursts[i].transform.position=Coordinates.Float(e.position-app.Origin);var main=bursts[i].main;main.startSpeed=e.impact?35:140;main.startSize=e.impact?4:22;bursts[i].Emit(e.impact?20:85);if(app.Sound)shotAudio.PlayOneShot(explosionClip,(float)(.4/(1+(e.position-f.position).Length/300)));}else if(e.life>0)bursts[i].transform.position=Coordinates.Float(e.position-app.Origin);burstLife[i]=e.life;}
            bool warping=f.state=="warping";tube.gameObject.SetActive(warping);wave.gameObject.SetActive(f.warpCharge>0);Quaternion rot=Quaternion.LookRotation(Coordinates.Float(f.forward),Coordinates.Float(f.up));tube.position=Coordinates.Float(origin);tube.rotation=rot;wave.position=tube.position;wave.rotation=rot;wave.localScale=new Vector3(1,1,.02f+(float)(1-f.warpCharge/3)*.08f);Shader.SetGlobalFloat("_LadirWarp",warping?1:0);Shader.SetGlobalFloat("_LadirWarpTime",(float)f.time);
        }
        void OnDestroy(){if(lines)Destroy(lines);if(lineMaterial)Destroy(lineMaterial);if(gunClip)Destroy(gunClip);if(missileClip)Destroy(missileClip);if(explosionClip)Destroy(explosionClip);}
    }
}
