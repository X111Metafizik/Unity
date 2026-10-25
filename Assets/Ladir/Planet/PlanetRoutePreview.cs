using System;
using System.Threading.Tasks;
using UnityEngine;
namespace Ladir.Planet
{
    public sealed class PlanetRoutePreview : MonoBehaviour
    {
        PlanetStudio app;LineRenderer line;Transform unit;D3[] route,surface;Vector3[] points;float time;bool busy;string identity;
        public void Initialize(PlanetStudio studio){app=studio;line=new GameObject("Validated crossing preview").AddComponent<LineRenderer>();line.sharedMaterial=studio.lineMaterial;line.startColor=line.endColor=new Color(2,1.2f,.4f,1);line.startWidth=line.endWidth=4;line.positionCount=0;var obj=GameObject.CreatePrimitive(PrimitiveType.Capsule);Destroy(obj.GetComponent<Collider>());unit=obj.transform;unit.localScale=new Vector3(9,14,9);obj.GetComponent<MeshRenderer>().sharedMaterial=studio.lightMaterial;obj.SetActive(false);}
        public async void Preview()
        {
            if(busy||app.Kernel==null||app.Kernel.World.crossings.Length==0)return;var crossing=app.Kernel.World.crossings[0];if(crossing.anchors==null||crossing.anchors.Length<2){app.Status="Bu dünyada doğrulanabilir geçit bağlantısı yok.";return;}
            busy=true;var kernel=app.Kernel;string requested=kernel.World.Identity;app.Status="Geçit ve su derinliği doğrulanıyor…";
            var lease=kernel.Retain();
            try{var result=await Task.Run(()=>{using(lease)return NavigationField.Query(kernel,new D3(crossing.anchors[0]),new D3(crossing.anchors[1]),false,0,crossing);});if(!this||app.Kernel.World.Identity!=requested)return;if(!result.passable){app.Status="Geçiş kapalı: "+result.reason;return;}identity=requested;route=result.route;surface=result.surface;points=new Vector3[route.Length];time=0;line.positionCount=points.Length;unit.gameObject.SetActive(true);app.Visit(new D3(crossing.direction),1600);app.Status=$"Geçiş açık · {result.length:F0} m · rota canlandırılıyor";}
            catch(Exception e){app.Status="Rota doğrulanamadı: "+e.Message;}finally{busy=false;}
        }
        void LateUpdate()
        {
            if(route==null)return;if(app.Kernel.World.Identity!=identity){route=null;line.positionCount=0;unit.gameObject.SetActive(false);return;}
            bool visible=!app.Flying;line.enabled=visible;unit.gameObject.SetActive(visible&&time<18);if(!visible)return;
            // Heights are canonical; the direction route was already validated
            // off the render thread. Only the moving marker is resampled here.
            for(int i=0;i<points.Length;i++)points[i]=Coordinates.Float(surface[i]-app.Origin);line.SetPositions(points);
            if(time>=18)return;time+=Time.deltaTime;double t=Math.Min(1,time/18.0)*(route.Length-1);int a=Math.Min(route.Length-2,(int)t);D3 d=(route[a]*(1-(t-a))+route[a+1]*(t-a)).Unit;unit.position=Coordinates.Float(d*(app.Kernel.World.radiusMeters+app.Kernel.Sample(d).height+15)-app.Origin);unit.rotation=Quaternion.FromToRotation(Vector3.up,Coordinates.Float(d));
        }
    }
}
