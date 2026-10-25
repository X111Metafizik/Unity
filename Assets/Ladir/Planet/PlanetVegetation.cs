using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ladir.Planet
{
    public sealed class PlanetVegetation : MonoBehaviour
    {
        struct Binding {public TreeInstance tree;public D3 point;public uint revision;public Matrix4x4 matrix;}
        sealed class Result {public string identity;public TreeInstance[] trees;}
        PlanetStudio app;Mesh[] meshes;Material material;Binding[] instances=Array.Empty<Binding>();
        readonly Matrix4x4[][] matrices={new Matrix4x4[1023],new Matrix4x4[1023],new Matrix4x4[1023],new Matrix4x4[1023],new Matrix4x4[1023],new Matrix4x4[1023]};
        readonly int[] counts=new int[6];int instanceCount;D3 matrixOrigin;
        readonly Dictionary<(int,int,int),TreeInstance[]> cache=new Dictionary<(int,int,int),TreeInstance[]>();
        readonly Queue<(int,int,int)> cacheOrder=new Queue<(int,int,int)>();string cacheWorld;
        Task<Result> job;CancellationTokenSource cancellation;D3 lastCenter;string identity;float nextCheck;
        public int InstanceCount=>instanceCount;
        public void Initialize(PlanetStudio studio)
        {
            app=studio;material=new Material(studio.vegetationMaterial){enableInstancing=true};
            using(var input=new BinaryReader(new MemoryStream(studio.vegetationGeometry.bytes)))
            {
                if(new string(input.ReadChars(8))!="LADIRVG1")throw new InvalidDataException("Vegetation asset header");meshes=new Mesh[input.ReadInt32()];
                for(int m=0;m<meshes.Length;m++){int n=input.ReadInt32();var vertices=new Vector3[n];var normals=new Vector3[n];var colors=new Color[n];var indices=new int[n];for(int i=0;i<n;i++){vertices[i]=new Vector3(input.ReadSingle(),input.ReadSingle(),input.ReadSingle());normals[i]=new Vector3(input.ReadSingle(),input.ReadSingle(),input.ReadSingle());colors[i]=new Color(input.ReadSingle(),input.ReadSingle(),input.ReadSingle(),input.ReadSingle());indices[i]=i;}meshes[m]=new Mesh{name="Web tree species / LOD "+m,vertices=vertices,normals=normals,colors=colors,triangles=indices};meshes[m].RecalculateBounds();}
            }
        }
        void Update()
        {
            if(app.Kernel==null)return;D3 point=app.Flying?app.Flight.position:app.CameraPosition;double altitude=point.Length-app.Kernel.World.radiusMeters;
            if(job!=null&&job.IsCompleted)
            {
                if(job.Status==TaskStatus.RanToCompletion&&job.Result.identity==app.Kernel.World.Identity)
                {
                    var trees=job.Result.trees;if(instances.Length<trees.Length)Array.Resize(ref instances,Mathf.NextPowerOfTwo(Math.Max(64,trees.Length)));instanceCount=trees.Length;
                    for(int i=0;i<instanceCount;i++){var t=trees[i];var root=t.direction*(app.Kernel.World.radiusMeters+t.elevation-.25);var rotation=Quaternion.FromToRotation(Vector3.up,Coordinates.Float(t.direction))*Quaternion.Euler(0,(float)(-t.rotation*180/Math.PI),0);instances[i]=new Binding{tree=t,point=root,revision=uint.MaxValue,matrix=Matrix4x4.TRS(Coordinates.Float(root-app.Origin),rotation,Vector3.one*(float)t.height)};}
                }
                else if(job.Exception!=null&&!(job.Exception.InnerException is OperationCanceledException))Debug.LogException(job.Exception);
                job=null;cancellation?.Dispose();cancellation=null;
            }
            if(altitude>12000||app.Kernel.World.Dry){cancellation?.Cancel();return;}
            if(Time.unscaledTime>nextCheck&&job==null&&((point-lastCenter).Length>450||identity!=app.Kernel.World.Identity))
            {nextCheck=Time.unscaledTime+.5f;lastCenter=point;identity=app.Kernel.World.Identity;cancellation=new CancellationTokenSource();var token=cancellation.Token;var kernel=app.Kernel;var lease=kernel.Retain();D3 d=point.Unit;job=Task.Run(()=>{using(lease)return Build(kernel,d,token);});}
            bool rebased=matrixOrigin.x!=app.Origin.x||matrixOrigin.y!=app.Origin.y||matrixOrigin.z!=app.Origin.z;matrixOrigin=app.Origin;Array.Clear(counts,0,counts.Length);
            // One traversal, one distance test. Rotation/scale are immutable;
            // only root height or a floating-origin change rewrites translation.
            for(int i=0;i<instanceCount;i++)
            {
                    ref var binding=ref instances[i];var t=binding.tree;double distance=(binding.point-point).Length;
                    // Bind roots to the actual currently displayed triangle.
                    bool moved=false;if(distance<=3150&&binding.revision!=app.Terrain.Revision&&app.Terrain.TrySurfaceHeight(t.direction,out double ground)){binding.point=t.direction*(app.Kernel.World.radiusMeters+ground-.25);binding.revision=app.Terrain.Revision;moved=true;}
                    // Rebase even out-of-range cached instances, which can come
                    // back into range without another origin transition.
                    if(rebased||moved){var p=Coordinates.Float(binding.point-app.Origin);binding.matrix.m03=p.x;binding.matrix.m13=p.y;binding.matrix.m23=p.z;}
                    if(distance>3150)continue;int type=t.species+(distance<1500?0:3);matrices[type][counts[type]++]=binding.matrix;
                    if(counts[type]==1023){Draw(type,counts[type]);counts[type]=0;}
            }
            for(int type=0;type<6;type++)if(counts[type]>0)Draw(type,counts[type]);
        }
        void Draw(int type,int count)=>Graphics.DrawMeshInstanced(meshes[type],0,material,matrices[type],count,null,type<3?ShadowCastingMode.On:ShadowCastingMode.Off,true,0,app.Camera);
        Result Build(TerrainKernel kernel,D3 center,CancellationToken token)
        {
            if(cacheWorld!=kernel.World.Identity){cache.Clear();cacheOrder.Clear();cacheWorld=kernel.World.Identity;}
            int level=(int)Math.Ceiling(Math.Log(2*kernel.World.radiusMeters/1400,2)),divisions=1<<level;double size=2.0/divisions;
            PM.FaceUV(center,out int face,out double u,out double v);int cx=(int)((u+1)/size),cy=(int)((v+1)/size);var keys=new HashSet<(int,int,int)>();var output=new List<TreeInstance>();
            for(int y=cy-4;y<=cy+4;y++)for(int x=cx-4;x<=cx+4;x++)
            {
                token.ThrowIfCancellationRequested();var d=PM.Face(face,-1+(x+.5)*size,-1+(y+.5)*size);PM.FaceUV(d,out int f,out double a,out double b);int tx=(int)PM.Clamp(Math.Floor((a+1)/size),0,divisions-1),ty=(int)PM.Clamp(Math.Floor((b+1)/size),0,divisions-1);var key=(f,tx,ty);if(!keys.Add(key))continue;
                if(!cache.TryGetValue(key,out var trees)){trees=VegetationField.Tile(kernel,f,tx,ty,level,token);cache[key]=trees;cacheOrder.Enqueue(key);if(cacheOrder.Count>96)cache.Remove(cacheOrder.Dequeue());}
                foreach(var t in trees)if((t.direction-center).Length*kernel.World.radiusMeters<3900)output.Add(t);
            }
            return new Result{identity=kernel.World.Identity,trees=output.ToArray()};
        }
        void OnDestroy(){cancellation?.Cancel();if(meshes!=null)foreach(var mesh in meshes)if(mesh)Destroy(mesh);if(material)Destroy(material);}
    }
}
