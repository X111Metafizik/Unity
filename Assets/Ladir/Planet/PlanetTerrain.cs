using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using static Ladir.Planet.PM;

namespace Ladir.Planet
{
    public static class Coordinates
    {
        // Web's right-handed geocentric frame -> Unity's left-handed render frame.
        // The reflection is confined to presentation; saved directions stay exact.
        public static Vector3 Float(D3 p)=>new Vector3((float)-p.x,(float)p.y,(float)p.z);
        public static D3 Double(Vector3 p)=>new D3(-p.x,p.y,p.z);
    }

    // A spherical six-face quadtree. Parents stay visible until all four child
    // buffers are uploaded. All jobs sample the same immutable canonical world.
    public sealed class PlanetTerrain : MonoBehaviour
    {
        public TerrainKernel Kernel {get;private set;}
        public D3 Origin,Observer,Forward,Predicted;
        public Material landMaterial,waterMaterial;
        public Camera viewCamera;
        public int visibleTiles,queued,running;
        public long residentBytes,meshBytes;
        public double lastUploadMs,lastBuildMs;
        public const long MemoryBudget=384L*1024*1024;
        public int grid=32,maxLevel=19;
        public float pixelError=2.5f;
        public event Action SurfaceReady;
        public uint Revision{get;private set;}
        readonly Node[] roots=new Node[6];
        readonly List<Node> all=new List<Node>();
        readonly List<Node> requests=new List<Node>();
        readonly ConcurrentQueue<Result> completed=new ConcurrentQueue<Result>();
        readonly Stopwatch budget=new Stopwatch();
        CancellationTokenSource lifetime;
        int epoch,planTick;double nextPlan;bool ready;
        public class Node
        {
            public int face,level,x,y;public double u,v,size,extent,priority;public D3 center,anchor;public Node parent;public Node[] children;
            public GameObject go;public Mesh land,water;public Vector3[] positions;public SurfaceSample[] samples;
            public bool wanted,pending,split,visible;public int wantedTick;public long bytes;public float touched;
            public CancellationTokenSource cancel;
            public string Key=>$"{face}/{level}/{x}/{y}";
        }
        sealed class Result
        {public Node node;public Vector3[] positions,normals;public Color[] colors;public Vector2[] uv;public Vector4[] climate;public int[] indices;public Vector3[] water;public int[] waterIndices;public SurfaceSample[] samples;public double buildMs;public int epoch;public Exception error;}
        public void Initialize(TerrainKernel kernel)
        {
            Clear();Kernel=kernel;lifetime=new CancellationTokenSource();epoch++;ready=false;
            for(int f=0;f<6;f++)roots[f]=Make(f,0,0,0,null);
            nextPlan=0;
        }
        Node Make(int f,int level,int x,int y,Node parent)
        {double size=2.0/(1<<level),u=-1+x*size,v=-1+y*size;var d=Face(f,u+size*.5,v+size*.5);var n=new Node{face=f,level=level,x=x,y=y,u=u,v=v,size=size,center=d,anchor=d*Kernel.World.radiusMeters,parent=parent,extent=size*Kernel.World.radiusMeters*.82};all.Add(n);return n;}
        void Update()
        {
            if(Kernel==null)return;
            if(Time.unscaledTimeAsDouble>=nextPlan){nextPlan=Time.unscaledTimeAsDouble+.10;Plan();}
            Upload();Dispatch();visibleTiles=0;meshBytes=0;residentBytes=0;
            foreach(var root in roots)if(root!=null)Draw(root,true);
            foreach(var n in all)if(n.go){n.go.transform.position=Coordinates.Float(n.anchor-Origin);meshBytes+=n.bytes;residentBytes+=n.bytes+(n.samples?.Length??0)*128L;}
            if(residentBytes>MemoryBudget)Evict();
        }
        void Plan()
        {
            planTick++;requests.Clear();foreach(var n in all){n.wanted=false;n.visible=false;}
            foreach(var root in roots)Select(root);
            // Predictive jobs use exactly the same surface, but never displace a
            // visible ancestor or compete ahead of missing foreground coverage.
            if((Predicted-Observer).Length>2000)foreach(var root in roots)Prefetch(root,Predicted,0);
            foreach(var n in all)if(n.pending&&!n.wanted&&n.level>2)n.cancel?.Cancel();
            requests.Sort((a,b)=>a.priority.CompareTo(b.priority));queued=requests.Count;
        }
        bool InView(Node n)
        {
            double R=Kernel.World.radiusMeters,r=Observer.Length,angular=n.size*.9;
            if(r>R+1&&D3.Dot(n.center,Observer/r)<R/r-angular-.012)return false;
            D3 to=n.anchor-Observer;double distance=to.Length;if(distance<n.extent+12000)return true;
            return D3.Dot(to/distance,Forward)>-.10-n.extent/distance;
        }
        void Want(Node n,double priority)
        {n.priority=n.wanted?Math.Min(n.priority,priority):priority;n.wanted=true;n.wantedTick=planTick;n.touched=Time.unscaledTime;if(!n.go&&!n.pending&&!requests.Contains(n))requests.Add(n);}
        void Select(Node n)
        {
            n.priority=double.MaxValue;
            if(n.level>1&&!InView(n)){n.split=false;return;}
            // Measure from the actual landform, not from the sea-level sphere.
            // Otherwise a ship 200 m over a 2 km mountain gets 2.2 km LOD.
            double height=n.samples!=null?n.samples[n.samples.Length/2].height:n.parent?.samples!=null?n.parent.samples[n.parent.samples.Length/2].height:0;
            double distance=Math.Max(25,(n.anchor+n.center*height-Observer).Length-n.extent*.4),focal=Screen.height/(2*Math.Tan(viewCamera.fieldOfView*Math.PI/360));
            // Geometric error and footprint, not just radial altitude. Narrow
            // river/shore/rift cells receive the same close-flight sampling.
            double error=Math.Min(3800,n.extent/grid*.19),pixels=error/distance*focal;
            double minimum=Kernel.World.radiusMeters*n.size/grid;
            bool split=n.level<maxLevel&&(n.level<2||pixels>pixelError*(n.split?.82:1.12))&&minimum>1.1;
            Want(n,distance+(n.level*50));n.split=split;
            if(!split)return;
            if(n.children==null){n.children=new Node[4];for(int i=0;i<4;i++)n.children[i]=Make(n.face,n.level+1,n.x*2+i%2,n.y*2+i/2,n);}
            foreach(var child in n.children)Select(child);
        }
        void Prefetch(Node n,D3 p,int depth)
        {
            double distance=(n.anchor-p).Length;if(distance>n.extent*2+Math.Max(20000,p.Length-Kernel.World.radiusMeters)&&n.level>1)return;
            double height=n.samples!=null?n.samples[n.samples.Length/2].height:n.parent?.samples!=null?n.parent.samples[n.parent.samples.Length/2].height:0;
            double surfaceDistance=Math.Max(25,(n.anchor+n.center*height-p).Length-n.extent*.4),focal=Screen.height/(2*Math.Tan(viewCamera.fieldOfView*Math.PI/360)),pixels=Math.Min(3800,n.extent/grid*.19)/surfaceDistance*focal;
            Want(n,1e8+surfaceDistance+n.level*50);
            if(n.level>=maxLevel||pixels<pixelError*1.3||Kernel.World.radiusMeters*n.size/grid<=1.1)return;
            if(n.children==null){n.children=new Node[4];for(int i=0;i<4;i++)n.children[i]=Make(n.face,n.level+1,n.x*2+i%2,n.y*2+i/2,n);}
            foreach(var c in n.children)Prefetch(c,p,depth+1);
        }
        void Draw(Node n,bool allowed)
        {
            bool childrenReady=n.split&&n.children!=null; if(childrenReady)foreach(var c in n.children)childrenReady&=c.go!=null||!c.wanted;
            bool show=allowed&&n.wanted&&n.go&&!childrenReady;n.visible=show;
            if(n.go&&n.go.activeSelf!=show){n.go.SetActive(show);Revision++;}if(show)visibleTiles++;
            if(n.children!=null)foreach(var child in n.children)Draw(child,allowed&&childrenReady);
        }
        public bool TrySurfaceHeight(D3 direction,out double height)
        {
            height=0;FaceUV(direction,out int face,out double u,out double v);var n=roots[face];
            while(n!=null)
            {
                if(n.visible&&n.positions!=null)
                {
                    int side=(int)Math.Round(Math.Sqrt(n.positions.Length+4)-2),N=side-1;double x=Clamp((u-n.u)/n.size)*N,y=Clamp((v-n.v)/n.size)*N;int ix=Math.Min(N-1,(int)x),iy=Math.Min(N-1,(int)y),a=iy*side+ix;double fx=x-ix,fy=y-iy;
                    int i0=fx+fy<=1?a:a+1,i1=fx+fy<=1?a+1:a+side+1,i2=a+side;D3 p0=n.anchor+Coordinates.Double(n.positions[i0]),p1=n.anchor+Coordinates.Double(n.positions[i1]),p2=n.anchor+Coordinates.Double(n.positions[i2]),normal=D3.Cross(p1-p0,p2-p0);double divisor=D3.Dot(normal,direction);if(Math.Abs(divisor)<1e-9)return false;height=D3.Dot(normal,p0)/divisor-Kernel.World.radiusMeters;return true;
                }
                if(n.children==null)return false;int child=(u>=n.u+n.size*.5?1:0)+(v>=n.v+n.size*.5?2:0);n=n.children[child];
            }return false;
        }
        void Dispatch()
        {
            int limit=Math.Max(1,Math.Min(4,Environment.ProcessorCount-2));
            for(int i=0;i<requests.Count&&running<limit;i++)
            {
                var node=requests[i];if(node.pending||node.go||!node.wanted)continue;
                // Start breadth-first: no fine patch can block its own fallback.
                if(node.parent!=null&&!node.parent.go)continue;
                node.pending=true;node.cancel=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var token=node.cancel.Token;int generation=epoch;var kernel=Kernel;int cells=node.level<2?16:grid;running++;
                var lease=kernel.Retain();Task.Run(()=>{using(lease){Result result;try{result=Build(node,kernel,cells,token);}catch(Exception e){result=new Result{node=node,error=e};}result.epoch=generation;completed.Enqueue(result);}});
            }
        }
        static Result Build(Node n,TerrainKernel kernel,int N,CancellationToken token)
        {
            var timer=Stopwatch.StartNew();int side=N+1,count=side*side,skirt=4*side;var p=new Vector3[count+skirt];var normal=new Vector3[p.Length];var colors=new Color[p.Length];var uv=new Vector2[p.Length];var climate=new Vector4[p.Length];var samples=new SurfaceSample[count];double R=kernel.World.radiusMeters;
            for(int y=0;y<=N;y++){token.ThrowIfCancellationRequested();for(int x=0;x<=N;x++){int i=y*side+x;var d=Face(n.face,n.u+n.size*x/N,n.v+n.size*y/N);var s=kernel.Sample(d);samples[i]=s;p[i]=Coordinates.Float(d*(R+s.height)-n.anchor);var c=TerrainKernel.Color(s);colors[i]=new Color((float)c.x,(float)c.y,(float)c.z,(float)s.lava);uv[i]=new Vector2((float)s.height,(float)s.biome);}}
            for(int y=0;y<=N;y++)for(int x=0;x<=N;x++){int i=y*side+x;var s=samples[i];climate[i]=new Vector4((float)s.snow,(float)s.moisture,(float)s.protection,(float)s.forest);Vector3 dx=p[y*side+Math.Min(N,x+1)]-p[y*side+Math.Max(0,x-1)],dy=p[Math.Min(N,y+1)*side+x]-p[Math.Max(0,y-1)*side+x];normal[i]=Vector3.Cross(dx,dy).normalized;}
            var triangles=new int[N*N*6+4*N*6];int at=0;for(int y=0;y<N;y++)for(int x=0;x<N;x++){int a=y*side+x;triangles[at++]=a;triangles[at++]=a+1;triangles[at++]=a+side;triangles[at++]=a+1;triangles[at++]=a+side+1;triangles[at++]=a+side;}
            for(int edge=0;edge<4;edge++)for(int k=0;k<=N;k++)
            {int src=edge==0?k:edge==1?k*side+N:edge==2?N*side+N-k:(N-k)*side;int dst=count+edge*side+k;var radial=Coordinates.Float((n.anchor+Coordinates.Double(p[src])).Unit);p[dst]=p[src]-radial*(float)Math.Max(8,n.extent/N*.8);normal[dst]=normal[src];colors[dst]=colors[src];uv[dst]=uv[src];if(k<N){int next=edge==0?src+1:edge==1?src+side:edge==2?src-1:src-side;triangles[at++]=src;triangles[at++]=dst;triangles[at++]=next;triangles[at++]=next;triangles[at++]=dst;triangles[at++]=dst+1;}}
            var wp=new List<Vector3>();var wi=new List<int>();
            // Clip each water triangle against the canonical bed; never fill a
            // dry river bank with a rectangle. Each wet vertex retains its reach's level.
            for(int y=0;y<N;y++)for(int x=0;x<N;x++){int a=y*side+x;WaterTriangle(a,a+1,a+side);WaterTriangle(a+1,a+side+1,a+side);}
            void WaterTriangle(int ia,int ib,int ic)
            {
                var sa=samples[ia];var sb=samples[ib];var sc=samples[ic];if(sa.Depth==0&&sb.Depth==0&&sc.Depth==0)return;
                double level=sa.Depth>0?sa.water:sb.Depth>0?sb.water:sc.water;
                // A dry vertex's sea datum must not lower a mountain lake edge.
                double wa=sa.Depth>0?sa.water:level,wb=sb.Depth>0?sb.water:level,wc=sc.Depth>0?sc.water:level;
                var pa=Coordinates.Double(p[ia])+n.anchor;var pb=Coordinates.Double(p[ib])+n.anchor;var pc=Coordinates.Double(p[ic])+n.anchor;
                Span<D3> ds=stackalloc D3[3]{pa.Unit,pb.Unit,pc.Unit};Span<double> levels=stackalloc double[3]{wa,wb,wc},depths=stackalloc double[3]{wa-sa.height,wb-sb.height,wc-sc.height};
                Span<Vector3> polygon=stackalloc Vector3[6];int length=0;
                for(int j=0;j<3;j++){int k=(j+1)%3;bool wet=depths[j]>0,next=depths[k]>0;if(wet)polygon[length++]=Coordinates.Float(ds[j]*(R+levels[j]+.08)-n.anchor);if(wet!=next){double t=depths[j]/(depths[j]-depths[k]);polygon[length++]=Coordinates.Float((ds[j]+(ds[k]-ds[j])*t).Unit*(R+Mix(levels[j],levels[k],t)+.08)-n.anchor);}}
                if(length<3)return;int start=wp.Count;for(int j=0;j<length;j++)wp.Add(polygon[j]);for(int j=1;j<length-1;j++){wi.Add(start);wi.Add(start+j);wi.Add(start+j+1);}
            }
            for(int edge=0;edge<4;edge++)for(int k=0;k<=N;k++){int src=edge==0?k:edge==1?k*side+N:edge==2?N*side+N-k:(N-k)*side;climate[count+edge*side+k]=climate[src];}
            for(int i=0;i<normal.Length;i++)normal[i]=-normal[i];
            for(int i=0;i<triangles.Length;i+=3){int t=triangles[i+1];triangles[i+1]=triangles[i+2];triangles[i+2]=t;}
            for(int i=0;i<wi.Count;i+=3){int t=wi[i+1];wi[i+1]=wi[i+2];wi[i+2]=t;}
            return new Result{node=n,positions=p,normals=normal,colors=colors,uv=uv,climate=climate,indices=triangles,water=wp.ToArray(),waterIndices=wi.ToArray(),samples=samples,buildMs=timer.Elapsed.TotalMilliseconds};
        }
        void Upload()
        {
            budget.Restart();int applied=0;
            while(completed.TryDequeue(out var r))
            {
                running=Math.Max(0,running-1);r.node.pending=false;r.node.cancel?.Dispose();r.node.cancel=null;
                if(r.epoch!=epoch||r.error is OperationCanceledException||!r.node.wanted)continue;
                if(r.error!=null){UnityEngine.Debug.LogException(r.error);continue;}
                var n=r.node;n.go=new GameObject("Terrain "+n.Key);n.go.transform.SetParent(transform,false);n.go.transform.position=Coordinates.Float(n.anchor-Origin);n.go.SetActive(false);
                n.land=new Mesh{name=n.Key,indexFormat=IndexFormat.UInt32};n.land.vertices=r.positions;n.land.normals=r.normals;n.land.colors=r.colors;n.land.uv=r.uv;n.land.SetUVs(1,r.climate);n.land.triangles=r.indices;n.land.RecalculateBounds();
                n.go.AddComponent<MeshFilter>().sharedMesh=n.land;var renderer=n.go.AddComponent<MeshRenderer>();renderer.sharedMaterial=landMaterial;renderer.shadowCastingMode=n.level>=10?ShadowCastingMode.On:ShadowCastingMode.Off;
                if(r.water.Length>0){var water=new GameObject("Water · clipped banks");water.transform.SetParent(n.go.transform,false);n.water=new Mesh{name="Water "+n.Key,indexFormat=IndexFormat.UInt32};n.water.vertices=r.water;n.water.triangles=r.waterIndices;var normals=new Vector3[r.water.Length];for(int i=0;i<normals.Length;i++)normals[i]=Coordinates.Float((n.anchor+Coordinates.Double(r.water[i])).Unit);n.water.normals=normals;n.water.RecalculateBounds();water.AddComponent<MeshFilter>().sharedMesh=n.water;var wr=water.AddComponent<MeshRenderer>();wr.sharedMaterial=waterMaterial;wr.shadowCastingMode=ShadowCastingMode.Off;}
                n.positions=r.positions;n.samples=r.samples;n.bytes=r.positions.Length*64L+r.indices.Length*4L+r.water.Length*24L+r.waterIndices.Length*4L;lastBuildMs=r.buildMs;applied++;
                if(!ready){ready=true;SurfaceReady?.Invoke();}
                if(budget.Elapsed.TotalMilliseconds>=2||applied>=3)break;
            }
            lastUploadMs=budget.Elapsed.TotalMilliseconds;
        }
        void Evict()
        {
            foreach(var n in all)
            {if(residentBytes<=MemoryBudget*.85)break;if(n.wanted||n.pending||n.level<2||!n.go||Time.unscaledTime-n.touched<4)continue;residentBytes-=n.bytes+(n.samples?.Length??0)*128L;Destroy(n.go);Destroy(n.land);if(n.water)Destroy(n.water);n.positions=null;n.samples=null;n.bytes=0;}
        }
        public void Clear()
        {lifetime?.Cancel();foreach(var n in all){n.cancel?.Cancel();if(n.go)Destroy(n.go);if(n.land)Destroy(n.land);if(n.water)Destroy(n.water);}all.Clear();requests.Clear();Array.Clear(roots,0,6);lifetime?.Dispose();lifetime=null;}
        void OnDestroy(){Clear();}
    }
}
