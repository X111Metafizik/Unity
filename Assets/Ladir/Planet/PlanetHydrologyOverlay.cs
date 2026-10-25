using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace Ladir.Planet
{
    public sealed class PlanetHydrologyOverlay : MonoBehaviour
    {
        PlanetStudio app;string identity;D3[] points;Vector3[] positions;Mesh mesh;Material material;float next;
        public void Initialize(PlanetStudio studio){app=studio;material=new Material(studio.lineMaterial);}
        void Update()
        {
            if(app.Kernel==null)return;if(identity!=app.Kernel.World.Identity)Build();bool show=!app.Flying&&app.inspectLayer==4;
            if(!show||mesh==null)return;
            if(Time.unscaledTime>=next){next=Time.unscaledTime+.05f;for(int i=0;i<points.Length;i++)positions[i]=Coordinates.Float(points[i]-app.Origin);mesh.vertices=positions;mesh.bounds=new Bounds(Vector3.zero,Vector3.one*(float)app.Kernel.World.radiusMeters*6);}
            Graphics.DrawMesh(mesh,Matrix4x4.identity,material,0,app.Camera,0,null,ShadowCastingMode.Off,false);
        }
        void Build()
        {
            identity=app.Kernel.World.Identity;var w=app.Kernel.World;var list=new List<D3>();var colors=new List<Color>();
            foreach(var river in w.hydrology.rivers)for(int i=1;i<river.points.Length;i++)
            {var a=river.points[i-1];var b=river.points[i];D3 p=a.D*(w.radiusMeters+a.waterLevelMeters+8),q=b.D*(w.radiusMeters+b.waterLevelMeters+8);Color color=river.regional?new Color(.2f,2,2,1):river.stream?new Color(.3f,1.7f,1,1):new Color(.15f,.9f,2,1);list.Add(p);list.Add(q);colors.Add(color);colors.Add(color);
             if(i%12==0){D3 back=(p-q).Unit*100,side=D3.Cross(q.Unit,back).Unit*45;list.Add(q+back+side);list.Add(q);list.Add(q+back-side);list.Add(q);for(int j=0;j<4;j++)colors.Add(color);}}
            foreach(var lake in w.hydrology.lakes)for(int i=0;i<96;i++)for(int j=0;j<2;j++){double a=(i+j)*Math.PI*2/96;list.Add(w.Direction(lake.x+Math.Cos(a)*lake.radiusMeters/w.radiusMeters,lake.y+Math.Sin(a)*lake.radiusMeters/w.radiusMeters)*(w.radiusMeters+lake.waterLevelMeters+10));colors.Add(new Color(.3f,1.3f,2,.9f));}
            if(mesh)Destroy(mesh);points=list.ToArray();positions=new Vector3[points.Length];var indices=new int[points.Length];for(int i=0;i<indices.Length;i++)indices[i]=i;mesh=new Mesh{name="Canonical river flow and lake levels",indexFormat=IndexFormat.UInt32};mesh.MarkDynamic();mesh.vertices=positions;mesh.colors=colors.ToArray();mesh.SetIndices(indices,MeshTopology.Lines,0);
        }
        void OnDestroy(){if(mesh)Destroy(mesh);if(material)Destroy(material);}
    }
}
