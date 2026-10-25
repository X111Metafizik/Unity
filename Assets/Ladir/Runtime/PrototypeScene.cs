using UnityEngine;
using UnityEngine.Rendering;

namespace Ladir {
 public sealed class PrototypeScene : MonoBehaviour {
  public Mesh shipMesh;public Material shipMaterial,terrainMaterial,waterMaterial,thrusterMaterial,lightMaterial;
  void Awake(){QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;Application.runInBackground=true;
   RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.46f,.57f,.69f);RenderSettings.ambientEquatorColor=new Color(.28f,.32f,.36f);RenderSettings.ambientGroundColor=new Color(.18f,.2f,.21f);
   RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogColor=new Color(.53f,.68f,.78f);RenderSettings.fogDensity=.000035f;
   var sunObject=new GameObject("Sun");var sun=sunObject.AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.6f;sun.color=new Color(1,.94f,.85f);sun.shadows=LightShadows.Soft;sun.shadowBias=.03f;sun.shadowNormalBias=.2f;sunObject.transform.rotation=Quaternion.Euler(34,-35,0);RenderSettings.sun=sun;
   var skyShader=Shader.Find("Skybox/Procedural");if(skyShader){var sky=new Material(skyShader);sky.SetFloat("_SunSize",.035f);sky.SetFloat("_AtmosphereThickness",.8f);sky.SetColor("_SkyTint",new Color(.52f,.55f,.6f));sky.SetColor("_GroundColor",new Color(.3f,.35f,.38f));RenderSettings.skybox=sky;}
   var cameraObject=new GameObject("Flight Camera");cameraObject.tag="MainCamera";var camera=cameraObject.AddComponent<Camera>();camera.nearClipPlane=.5f;camera.farClipPlane=60000;camera.fieldOfView=64;camera.allowHDR=true;camera.clearFlags=CameraClearFlags.Skybox;cameraObject.AddComponent<AudioListener>();
   var terrainObject=new GameObject("Canonical Region");var terrain=terrainObject.AddComponent<RegionStreamer>();terrain.terrainMaterial=terrainMaterial;terrain.waterMaterial=waterMaterial;
   var shipObject=new GameObject("Starlight Voyager · 110 m");shipObject.AddComponent<MeshFilter>().sharedMesh=shipMesh;shipObject.AddComponent<MeshRenderer>().sharedMaterial=shipMaterial;var flight=shipObject.AddComponent<FlightController>();flight.terrain=terrain;flight.flightCamera=camera;terrain.observer=shipObject.transform;
   shipObject.transform.position=new Vector3(800,1900,-6700);cameraObject.transform.position=shipObject.transform.position+new Vector3(0,80,-255);cameraObject.transform.LookAt(shipObject.transform);
   flight.thrusters=new ParticleSystem[2];for(int i=0;i<2;i++){var obj=new GameObject("Ion thruster "+i);obj.transform.SetParent(shipObject.transform,false);obj.transform.localPosition=new Vector3(i==0?-20.28125f:20.28125f,-1.71875f,i==0?-36.4f:-36.8f);obj.transform.localRotation=Quaternion.Euler(0,180,0);var ps=obj.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var main=ps.main;main.simulationSpace=ParticleSystemSimulationSpace.Local;main.startLifetime=.16f;main.startSpeed=100;main.startSize=4.5f;main.maxParticles=64;main.startColor=new Color(.25f,.85f,1,.85f);var emission=ps.emission;emission.rateOverTime=50;var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=6;shape.radius=1.8f;var color=ps.colorOverLifetime;color.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(.15f,.65f,1),1)},new[]{new GradientAlphaKey(.9f,0),new GradientAlphaKey(0,1)});color.color=gradient;var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=thrusterMaterial;renderer.renderMode=ParticleSystemRenderMode.Stretch;renderer.lengthScale=2;renderer.velocityScale=.02f;ps.Play();flight.thrusters[i]=ps;
    var lamp=GameObject.CreatePrimitive(PrimitiveType.Sphere);Destroy(lamp.GetComponent<Collider>());lamp.name="Navigation light";lamp.transform.SetParent(shipObject.transform,false);lamp.transform.localPosition=new Vector3(i==0?-31:31,7,-5);lamp.transform.localScale=Vector3.one*1.2f;lamp.GetComponent<MeshRenderer>().sharedMaterial=lightMaterial;
   }
   var benchmark=gameObject.AddComponent<PrototypeBenchmark>();benchmark.terrain=terrain;benchmark.ship=flight;
   var controls=gameObject.AddComponent<ControlSmoke>();controls.terrain=terrain;controls.ship=flight;
  }
 }
}
