using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;
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
        public double lastPlanMs,lastSubmitMs,lastStitchMs;
        public int NodeCount=>all.Count;
        public const long MemoryBudget=384L*1024*1024;
        public int grid=32,maxLevel=19;
        public float pixelError=2.5f;
        public event Action SurfaceReady;
        public uint Revision{get;private set;}
        readonly Node[] roots=new Node[6];
        readonly List<Node> all=new List<Node>();
        readonly List<Node> requests=new List<Node>();
        readonly List<Node> seamWork=new List<Node>();
        readonly ConcurrentQueue<Result> completed=new ConcurrentQueue<Result>();
        readonly Stopwatch budget=new Stopwatch();
        static readonly ProfilerMarker planMarker=new ProfilerMarker("Ladir.Terrain.Plan"),uploadMarker=new ProfilerMarker("Ladir.Terrain.Upload"),submitMarker=new ProfilerMarker("Ladir.Terrain.Submit"),stitchMarker=new ProfilerMarker("Ladir.Terrain.Stitch");
        CancellationTokenSource lifetime;
        int epoch,planTick;double nextPlan;bool ready;uint lastSeamRevision=uint.MaxValue;
        int createdThisPlan;
        bool visibilityDirty=true,originValid,evicting;D3 submittedOrigin;int evictCursor;
        int refinementsLeft;bool transitionPending;
        Result uploading;int uploadStep;GameObject stagingObject;Mesh stagingLand,stagingWater;
        public int uploadsThisFrame,uploadStepsThisFrame,stitchedThisFrame,rebaseWritesThisFrame;
        public class Node
        {
            public int face,level,x,y;public double u,v,size,extent,priority;public double featureCell=double.NaN;public D3 center,anchor;public Node parent;public Node[] children;
            public GameObject go;public Mesh land,water;public Vector3[] positions,canonicalPositions;public SurfaceSample[] samples;
            internal PlanetGridTopology topology;internal Vector3[] boundaryPositions;
            public readonly Node[] neighbours=new Node[4];public readonly uint[] neighbourRevisions=new uint[4];public bool seamsInitialized;public uint seamRevision;public Bounds canonicalBounds;
            public bool wanted,viewWanted,pending,split,visible,expanded;public int wantedTick;public long bytes;public float touched;
            public CancellationTokenSource cancel;
            public string Key=>$"{face}/{level}/{x}/{y}";
        }
        sealed class Result
        {public Node node;public Vector3[] positions,displayPositions,gpuPositions,boundaryPositions;public PlanetLandVertex[] attributes;public PlanetWaterVertex[] waterAttributes;public PlanetGridTopology topology;public Bounds bounds,waterBounds;public Vector3[] water;public int[] waterIndices;public SurfaceSample[] samples;public double buildMs;public int epoch;public Exception error;}
        public void Initialize(TerrainKernel kernel)
        {
            Clear();Kernel=kernel;lifetime=new CancellationTokenSource();epoch++;ready=false;
            for(int f=0;f<6;f++)roots[f]=Make(f,0,0,0,null);
            nextPlan=0;visibilityDirty=true;originValid=false;residentBytes=meshBytes=0;
        }
        Node Make(int f,int level,int x,int y,Node parent)
        {double size=2.0/(1<<level),u=-1+x*size,v=-1+y*size;var d=Face(f,u+size*.5,v+size*.5);var n=new Node{face=f,level=level,x=x,y=y,u=u,v=v,size=size,center=d,anchor=d*Kernel.World.radiusMeters,parent=parent,extent=size*Kernel.World.radiusMeters*.82};all.Add(n);return n;}
        void Update()
        {
            if(Kernel==null)return;
            lastPlanMs=lastStitchMs=0;stitchedThisFrame=rebaseWritesThisFrame=0;
            if(Time.unscaledTimeAsDouble>=nextPlan){nextPlan=Time.unscaledTimeAsDouble+.10;long started=Stopwatch.GetTimestamp();using(planMarker.Auto())Plan();lastPlanMs=(Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency;visibilityDirty=true;}
            using(uploadMarker.Auto())Upload();long submit=Stopwatch.GetTimestamp();using(submitMarker.Auto()){
            Dispatch();
            if(visibilityDirty){visibleTiles=0;refinementsLeft=2;transitionPending=false;foreach(var root in roots)if(root!=null)Draw(root,true);using(stitchMarker.Auto())StitchVisibleEdges();visibilityDirty=transitionPending;}
            // Floating origin changes in 1024 m steps, not once per frame.
            // New uploads already have the current transform before publication.
            if(!originValid||Origin.x!=submittedOrigin.x||Origin.y!=submittedOrigin.y||Origin.z!=submittedOrigin.z)
            {foreach(var n in all)if(n.go){n.go.transform.position=Coordinates.Float(n.anchor-Origin);rebaseWritesThisFrame++;}submittedOrigin=Origin;originValid=true;}
            if(residentBytes>MemoryBudget)evicting=true;if(evicting)Evict();}
            lastSubmitMs=(Stopwatch.GetTimestamp()-submit)*1000.0/Stopwatch.Frequency;
        }
        void Plan()
        {
            planTick++;createdThisPlan=0;requests.Clear();foreach(var n in all){n.wanted=false;n.viewWanted=false;}
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
        {bool first=!n.wanted;n.priority=first?priority:Math.Min(n.priority,priority);n.wanted=true;n.wantedTick=planTick;n.touched=Time.unscaledTime;if(first&&!n.go&&!n.pending)requests.Add(n);}
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
            if(double.IsNaN(n.featureCell)&&n.level>=8)n.featureCell=Kernel.WaterFeatureCell(n.center,n.extent);
            // River width and shoreline curvature matter even when a flat
            // valley has a small height error. Do not discard a visible channel.
            double featureTarget=Math.Max(n.featureCell,distance/focal*1.5);
            bool feature=n.level>=8&&distance<60000&&minimum>featureTarget;
            double incidence=Math.Max(.25,Math.Abs(D3.Dot(n.center,n.anchor-Observer))/Math.Max(1,(n.anchor-Observer).Length)),projection=1/Math.Sqrt(1+Sq(n.u+n.size*.5)+Sq(n.v+n.size*.5));
            // Same orbital footprint rule as the web renderer; a small height
            // error alone cannot resolve categorical snow/shore material fields.
            bool orbital=Observer.Length-Kernel.World.radiusMeters>60000&&n.level>=3&&minimum*projection>Math.Max(150,distance*.0036)/Math.Sqrt(incidence);
            bool split=n.level<maxLevel&&(n.level<3||feature||orbital||pixels>pixelError*(n.split?.82:1.12))&&minimum>1.1;
            Want(n,distance+(n.level*50));n.viewWanted=true;n.split=split;
            if(!split)return;
            if(!EnsureChildren(n))return;
            foreach(var child in n.children)
            {
                Select(child);
                // Keep one ring of sibling coverage warm. A turn that exposes
                // a previously culled child must not collapse an entire ready
                // mountain subtree back to its distant ancestor. This requests
                // only the sibling mesh, never its culled descendants.
                if(!child.viewWanted)Want(child,1e7+distance+child.level*50);
            }
        }
        bool EnsureChildren(Node n)
        {
            if(n.children!=null)return true;
            // A cold start or teleport must not allocate/evaluate an entire new
            // quadtree in one Plan. Existing nodes and desired detail are kept.
            if(createdThisPlan>=128)return false;
            n.children=new Node[4];for(int i=0;i<4;i++)n.children[i]=Make(n.face,n.level+1,n.x*2+i%2,n.y*2+i/2,n);createdThisPlan+=4;return true;
        }
        void Prefetch(Node n,D3 p,int depth)
        {
            double distance=(n.anchor-p).Length;if(distance>n.extent*2+Math.Max(20000,p.Length-Kernel.World.radiusMeters)&&n.level>1)return;
            double height=n.samples!=null?n.samples[n.samples.Length/2].height:n.parent?.samples!=null?n.parent.samples[n.parent.samples.Length/2].height:0;
            double surfaceDistance=Math.Max(25,(n.anchor+n.center*height-p).Length-n.extent*.4),focal=Screen.height/(2*Math.Tan(viewCamera.fieldOfView*Math.PI/360)),pixels=Math.Min(3800,n.extent/grid*.19)/surfaceDistance*focal;
            Want(n,1e8+surfaceDistance+n.level*50);
            if(n.level>=maxLevel||pixels<pixelError*1.3||Kernel.World.radiusMeters*n.size/grid<=1.1)return;
            if(!EnsureChildren(n))return;
            foreach(var c in n.children)Prefetch(c,p,depth+1);
        }
        void Draw(Node n,bool allowed,bool allowRefine=true)
        {
            // Speculative flight/warp requests are cache demands, not current
            // camera coverage. They must never hold a visible parent hostage.
            bool childrenReady=n.split&&n.children!=null; if(childrenReady)foreach(var c in n.children)childrenReady&=c.go!=null||!c.viewWanted;
            bool previouslyExpanded=n.expanded;
            if(!allowed||!n.viewWanted||!childrenReady)n.expanded=false;
            else if(!n.expanded&&n.viewWanted)
            {
                // A previously hidden ready subtree can contain hundreds of
                // renderers. Reveal at most two sibling groups and one depth
                // per frame, retaining the valid parent for the other groups.
                if(allowRefine&&n.visible&&refinementsLeft>0){n.expanded=true;refinementsLeft--;}
                else transitionPending=true;
            }
            bool show=allowed&&n.viewWanted&&n.go&&!n.expanded;n.visible=show;
            if(n.go&&n.go.activeSelf!=show){n.go.SetActive(show);Revision++;}if(show)visibleTiles++;
            if(n.children!=null)foreach(var child in n.children)Draw(child,allowed&&n.expanded,previouslyExpanded&&allowRefine);
        }
        public bool TrySurfaceHeight(D3 direction,out double height)
        {
            var n=VisibleNode(direction);if(n!=null)return TriangleHeight(n,direction,out height);height=0;return false;
        }
        Node VisibleNode(D3 direction)
        {
            FaceUV(direction,out int face,out double u,out double v);var n=roots[face];
            while(n!=null)
            {
                if(n.visible&&n.positions!=null)return n;
                if(n.children==null)return null;int child=(u>=n.u+n.size*.5?1:0)+(v>=n.v+n.size*.5?2:0);n=n.children[child];
            }return null;
        }
        bool TriangleHeight(Node n,D3 direction,out double height)
        {
            // Face-local projection also works when the requested point lies
            // exactly on a cube-face boundary selected by another face's tie.
            FaceCoordinates(n.face,direction,out double u,out double v);
            int side=(int)Math.Round(Math.Sqrt(n.positions.Length+4)-2),N=side-1;double x=Clamp((u-n.u)/n.size)*N,y=Clamp((v-n.v)/n.size)*N;int ix=Math.Min(N-1,(int)x),iy=Math.Min(N-1,(int)y),a=iy*side+ix;double fx=x-ix,fy=y-iy;
            int i0=fx+fy<=1?a:a+1,i1=fx+fy<=1?a+1:a+side+1,i2=a+side;D3 p0=n.anchor+Coordinates.Double(n.positions[i0]),p1=n.anchor+Coordinates.Double(n.positions[i1]),p2=n.anchor+Coordinates.Double(n.positions[i2]),normal=D3.Cross(p1-p0,p2-p0);double divisor=D3.Dot(normal,direction);height=0;if(Math.Abs(divisor)<1e-9)return false;height=D3.Dot(normal,p0)/divisor-Kernel.World.radiusMeters;return true;
        }
        static void FaceCoordinates(int face,D3 d,out double u,out double v)
        {
            // Inverse of PM.Face, without choosing a different dominant face.
            switch(face){case 0:u=-d.z/d.x;v=d.y/d.x;break;case 1:u=-d.z/d.x;v=-d.y/d.x;break;case 2:u=d.x/d.y;v=-d.z/d.y;break;case 3:u=-d.x/d.y;v=-d.z/d.y;break;case 4:u=d.x/d.z;v=d.y/d.z;break;default:u=d.x/d.z;v=-d.y/d.z;break;}
        }
        Node Across(Node n,int edge)
        {
            double eps=n.size*1e-5,u=n.u+n.size*.5,v=n.v+n.size*.5;
            if(edge==0)v=n.v-eps;else if(edge==1)u=n.u+n.size+eps;else if(edge==2)v=n.v+n.size+eps;else u=n.u-eps;
            return VisibleNode(Face(n.face,u,v));
        }
        void StitchVisibleEdges()
        {
            lastStitchMs=0;if(lastSeamRevision==Revision)return;long start=Stopwatch.GetTimestamp();seamWork.Clear();foreach(var n in all)if(n.visible)seamWork.Add(n);seamWork.Sort((a,b)=>a.level.CompareTo(b.level));
            // Coarse edges are resolved first, including their own coarse
            // neighbours. Fine vertices then meet the displayed triangle plane.
            foreach(var n in seamWork){bool changed=!n.seamsInitialized;for(int e=0;e<4;e++){var neighbour=Across(n,e);if(neighbour!=null&&neighbour.level>=n.level)neighbour=null;uint revision=neighbour?.seamRevision??0;if(n.neighbours[e]!=neighbour||n.neighbourRevisions[e]!=revision){n.neighbours[e]=neighbour;n.neighbourRevisions[e]=revision;changed=true;}}if(!changed)continue;
                int side=(int)Math.Round(Math.Sqrt(n.positions.Length+4)-2),N=side-1,count=side*side;Array.Copy(n.canonicalPositions,n.positions,n.positions.Length);
                for(int e=0;e<4;e++){var coarse=n.neighbours[e];if(coarse==null)continue;for(int k=0;k<=N;k++){int src=e==0?k:e==1?k*side+N:e==2?N*side+N-k:(N-k)*side;var target=coarse;if(k==0||k==N){var other=n.neighbours[(e+(k==0?3:1))%4];if(other!=null&&other.level<target.level)target=other;}var point=n.anchor+Coordinates.Double(n.canonicalPositions[src]);var d=point.Unit;if(!TriangleHeight(target,d,out double h))continue;var moved=Coordinates.Float(d*(Kernel.World.radiusMeters+h)-n.anchor);var delta=moved-n.positions[src];n.positions[src]=moved;
                    // Keep both skirt copies at corners consistent with the top.
                    if(src<side)n.positions[count+src]+=delta;if(src%side==N)n.positions[count+side+src/side]+=delta;if(src>=N*side)n.positions[count+side*2+N-src%side]+=delta;if(src%side==0)n.positions[count+side*3+N-src/side]+=delta;
                }}var bounds=n.canonicalBounds;for(int i=0;i<n.boundaryPositions.Length;i++){n.boundaryPositions[i]=n.positions[n.topology.gpuToRow[i]];bounds.Encapsulate(n.boundaryPositions[i]);}
                n.land.SetVertexBufferData(n.boundaryPositions,0,0,n.boundaryPositions.Length,0,PlanetMeshData.Flags);n.land.bounds=bounds;n.seamsInitialized=true;n.seamRevision++;Revision++;stitchedThisFrame++;
            }lastSeamRevision=Revision;lastStitchMs=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
        }
        public (int vertices,double maxGapMeters) CheckVisibleSeams()
        {
            int count=0;double gap=0;foreach(var n in all){if(!n.visible)continue;int side=(int)Math.Round(Math.Sqrt(n.positions.Length+4)-2),N=side-1;for(int e=0;e<4;e++){var neighbour=n.neighbours[e];if(neighbour==null)continue;for(int k=1;k<N;k++){int src=e==0?k:e==1?k*side+N:e==2?N*side+N-k:(N-k)*side;var point=n.anchor+Coordinates.Double(n.positions[src]);if(TriangleHeight(neighbour,point.Unit,out double h)){gap=Math.Max(gap,Math.Abs(point.Length-Kernel.World.radiusMeters-h));count++;}}}}return(count,gap);
        }
        // Explicit diagnostic only: verifies the GPU stream reorder and partial
        // seam writes against the exact CPU surface used by vegetation binding.
        public (int vertices,float maxPositionErrorMeters,int invalidIndices) CheckUploadedSurface()
        {
            int vertices=0,invalid=0;float error=0;
            foreach(var n in all)if(n.visible&&n.land)
            {
                var gpu=n.land.vertices;var indices=n.land.GetIndices(0);
                if(gpu.Length!=n.positions.Length){invalid++;continue;}
                for(int i=0;i<gpu.Length;i++){vertices++;error=Mathf.Max(error,Vector3.Distance(gpu[i],n.positions[n.topology.gpuToRow[i]]));}
                if(indices.Length!=n.topology.indices.Length)invalid++;
                else for(int i=0;i<indices.Length;i++)if(indices[i]!=n.topology.indices[i])invalid++;
            }
            return(vertices,error,invalid);
        }
        public double RenderedCellSize(D3 direction)
        {
            FaceUV(direction,out int face,out double u,out double v);var n=roots[face];
            while(n!=null){if(n.visible&&n.positions!=null){int cells=(int)Math.Round(Math.Sqrt(n.positions.Length+4)-3);return Kernel.World.radiusMeters*n.size/cells;}if(n.children==null)break;n=n.children[(u>=n.u+n.size*.5?1:0)+(v>=n.v+n.size*.5?2:0)];}
            return double.PositiveInfinity;
        }
        void Dispatch()
        {
            int limit=Math.Max(1,Math.Min(4,Environment.ProcessorCount-2));
            // Build the children which can replace a DISPLAYED parent first.
            // Merely having a parent buffer is insufficient: the old scheduler
            // built deep hidden descendants while one distant sibling kept a
            // coarse ancestor on screen, then revealed the whole subtree at once.
            for(int pass=0;pass<2&&running<limit;pass++)
            for(int i=0;i<requests.Count&&running<limit;i++)
            {
                var node=requests[i];if(node.pending||node.go||!node.wanted)continue;
                bool frontier=node.viewWanted&&(node.parent==null||node.parent.visible);if(frontier!=(pass==0))continue;
                if(residentBytes>MemoryBudget&&node.priority>=1e8)continue;
                // Start breadth-first: no fine patch can block its own fallback.
                if(node.parent!=null&&!node.parent.go)continue;
                node.pending=true;node.cancel=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var token=node.cancel.Token;int generation=epoch;var kernel=Kernel;int cells=node.level<2?16:grid;running++;
                var lease=kernel.Retain();Task.Run(()=>{using(lease){Result result;try{result=Build(node,kernel,cells,token);}catch(Exception e){result=new Result{node=node,error=e};}result.epoch=generation;completed.Enqueue(result);}});
            }
        }
        static Result Build(Node n,TerrainKernel kernel,int N,CancellationToken token)
        {
            var timer=Stopwatch.StartNew();int side=N+1,count=side*side,skirt=4*side;var topology=PlanetGridTopology.Get(N);var p=new Vector3[count+skirt];var attributes=new PlanetLandVertex[p.Length];var samples=new SurfaceSample[count];double R=kernel.World.radiusMeters;
            for(int y=0;y<=N;y++){token.ThrowIfCancellationRequested();for(int x=0;x<=N;x++){int i=y*side+x;var d=Face(n.face,n.u+n.size*x/N,n.v+n.size*y/N);var s=kernel.Sample(d);samples[i]=s;p[i]=Coordinates.Float(d*(R+s.height)-n.anchor);}}
            for(int y=0;y<=N;y++)for(int x=0;x<=N;x++){int i=y*side+x;var s=samples[i];var c=TerrainKernel.Color(s);Vector3 dx=p[y*side+Math.Min(N,x+1)]-p[y*side+Math.Max(0,x-1)],dy=p[Math.Min(N,y+1)*side+x]-p[Math.Max(0,y-1)*side+x];attributes[topology.rowToGpu[i]]=new PlanetLandVertex{normal=-Vector3.Cross(dx,dy).normalized,color=new Color((float)c.x,(float)c.y,(float)c.z,(float)s.lava),uv=new Vector2((float)s.height,(float)s.biome),climate=new Vector4((float)s.snow,(float)s.moisture,(float)s.protection,(float)s.forest)};}
            for(int edge=0;edge<4;edge++)for(int k=0;k<=N;k++)
            {int src=edge==0?k:edge==1?k*side+N:edge==2?N*side+N-k:(N-k)*side;int dst=count+edge*side+k;var radial=Coordinates.Float((n.anchor+Coordinates.Double(p[src])).Unit);p[dst]=p[src]-radial*(float)Math.Max(8,n.extent/N*.8);attributes[topology.rowToGpu[dst]]=attributes[topology.rowToGpu[src]];}
            var wp=new List<Vector3>();var wu=new List<Vector2>();var wi=new List<int>();
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
                var wetSample=sa.Depth>0?sa:sb.Depth>0?sb:sc;int kind=wetSample.feature==null?0:wetSample.flow>0?1:2;
                Span<Vector3> polygon=stackalloc Vector3[6];Span<Vector2> waterData=stackalloc Vector2[6];int length=0;
                for(int j=0;j<3;j++){int k=(j+1)%3;bool wet=depths[j]>0,next=depths[k]>0;if(wet){waterData[length]=new Vector2(kind==0?(float)Math.Exp(-depths[j]/350):kind==1?.8f:.5f,kind);polygon[length++]=Coordinates.Float(ds[j]*(R+levels[j]+.08)-n.anchor);}if(wet!=next){double t=depths[j]/(depths[j]-depths[k]);waterData[length]=new Vector2(kind==0?1:kind==1?.8f:.5f,kind);polygon[length++]=Coordinates.Float((ds[j]+(ds[k]-ds[j])*t).Unit*(R+Mix(levels[j],levels[k],t)+.08)-n.anchor);}}
                if(length<3)return;int start=wp.Count;for(int j=0;j<length;j++){wp.Add(polygon[j]);wu.Add(waterData[j]);}for(int j=1;j<length-1;j++){wi.Add(start);wi.Add(start+j);wi.Add(start+j+1);}
            }
            for(int i=0;i<wi.Count;i+=3){int t=wi[i+1];wi[i+1]=wi[i+2];wi[i+2]=t;}
            var water=wp.ToArray();var waterAttributes=new PlanetWaterVertex[water.Length];for(int i=0;i<water.Length;i++)waterAttributes[i]=new PlanetWaterVertex{normal=Coordinates.Float((n.anchor+Coordinates.Double(water[i])).Unit),uv=wu[i]};
            var gpuPositions=new Vector3[p.Length];for(int i=0;i<p.Length;i++)gpuPositions[i]=p[topology.gpuToRow[i]];
            return new Result{node=n,positions=p,displayPositions=(Vector3[])p.Clone(),gpuPositions=gpuPositions,boundaryPositions=new Vector3[topology.boundaryCount],attributes=attributes,topology=topology,bounds=PlanetMeshData.Bounds(p),water=water,waterAttributes=waterAttributes,waterBounds=PlanetMeshData.Bounds(water),waterIndices=wi.ToArray(),samples=samples,buildMs=timer.Elapsed.TotalMilliseconds};
        }
        void Upload()
        {
            budget.Restart();uploadsThisFrame=uploadStepsThisFrame=0;
            // The budget is checked BETWEEN mesh stages, not only after an
            // entire land+water tile has allocated and copied all its buffers.
            while(budget.Elapsed.TotalMilliseconds<2&&uploadsThisFrame<3)
            {
                if(uploading==null)
                {
                    if(!completed.TryDequeue(out var r))break;
                    running=Math.Max(0,running-1);
                    if(r.epoch!=epoch||r.error!=null||!r.node.wanted){ReleaseJob(r.node);if(r.error!=null&&!(r.error is OperationCanceledException))UnityEngine.Debug.LogException(r.error);continue;}
                    uploading=r;uploadStep=0;
                }
                if(uploading.epoch!=epoch||!uploading.node.wanted){ReleaseJob(uploading.node);DiscardStaging();continue;}
                UploadStep();uploadStepsThisFrame++;
            }
            lastUploadMs=budget.Elapsed.TotalMilliseconds;
        }
        static void ReleaseJob(Node n){n.pending=false;n.cancel?.Dispose();n.cancel=null;}
        void UploadStep()
        {
            var r=uploading;var n=r.node;
            switch(uploadStep++)
            {
                case 0:
                    stagingObject=new GameObject("Terrain "+n.Key);stagingObject.SetActive(false);stagingObject.transform.SetParent(transform,false);
                    stagingLand=new Mesh{name=n.Key};stagingLand.MarkDynamic();stagingLand.SetVertexBufferParams(r.positions.Length,PlanetMeshData.LandLayout);stagingLand.SetIndexBufferParams(r.topology.indices.Length,IndexFormat.UInt32);break;
                case 1:stagingLand.SetVertexBufferData(r.gpuPositions,0,0,r.gpuPositions.Length,0,PlanetMeshData.Flags);stagingLand.SetVertexBufferData(r.attributes,0,0,r.attributes.Length,1,PlanetMeshData.Flags);break;
                case 2:
                    PlanetMeshData.Finish(stagingLand,r.topology.indices,r.positions.Length,r.bounds);stagingObject.AddComponent<MeshFilter>().sharedMesh=stagingLand;var renderer=stagingObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=landMaterial;renderer.shadowCastingMode=n.level>=10?ShadowCastingMode.On:ShadowCastingMode.Off;
                    if(r.water.Length==0)uploadStep=6;break;
                case 3:
                    stagingWater=new Mesh{name="Water "+n.Key};stagingWater.SetVertexBufferParams(r.water.Length,PlanetMeshData.WaterLayout);stagingWater.SetIndexBufferParams(r.waterIndices.Length,IndexFormat.UInt32);break;
                case 4:stagingWater.SetVertexBufferData(r.water,0,0,r.water.Length,0,PlanetMeshData.Flags);stagingWater.SetVertexBufferData(r.waterAttributes,0,0,r.waterAttributes.Length,1,PlanetMeshData.Flags);break;
                case 5:
                    PlanetMeshData.Finish(stagingWater,r.waterIndices,r.water.Length,r.waterBounds);var water=new GameObject("Water · clipped banks");water.transform.SetParent(stagingObject.transform,false);water.AddComponent<MeshFilter>().sharedMesh=stagingWater;var wr=water.AddComponent<MeshRenderer>();wr.sharedMaterial=waterMaterial;wr.shadowCastingMode=ShadowCastingMode.Off;break;
                case 6:
                    // Publish atomically: a parent cannot disappear while only
                    // half of a child's geometry is ready. Rebase may have moved.
                    stagingObject.transform.position=Coordinates.Float(n.anchor-Origin);n.go=stagingObject;n.land=stagingLand;n.water=stagingWater;n.canonicalBounds=r.bounds;
                    n.canonicalPositions=r.positions;n.positions=r.displayPositions;n.boundaryPositions=r.boundaryPositions;n.topology=r.topology;n.samples=r.samples;
                    n.bytes=r.positions.Length*76L+r.topology.indices.Length*4L+r.water.Length*32L+r.waterIndices.Length*4L;
                    meshBytes+=n.bytes;residentBytes+=n.bytes+r.samples.Length*128L;lastBuildMs=r.buildMs;uploadsThisFrame++;visibilityDirty=true;ReleaseJob(n);
                    uploading=null;stagingObject=null;stagingLand=stagingWater=null;
                    if(!ready){ready=true;SurfaceReady?.Invoke();}break;
            }
        }
        void DiscardStaging()
        {if(stagingObject)Destroy(stagingObject);if(stagingLand)Destroy(stagingLand);if(stagingWater)Destroy(stagingWater);stagingObject=null;stagingLand=stagingWater=null;uploading=null;}
        void Evict()
        {
            int removed=0,visited=0;long start=Stopwatch.GetTimestamp();
            while(visited++<all.Count&&removed<2&&(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency<.5)
            {if(residentBytes<=MemoryBudget*.85){evicting=false;break;}if(evictCursor>=all.Count)evictCursor=0;var n=all[evictCursor++];if(n.wanted||n.visible||n.pending||n.level<2||!n.go||Time.unscaledTime-n.touched<4)continue;residentBytes-=n.bytes+(n.samples?.Length??0)*128L;meshBytes-=n.bytes;Destroy(n.go);Destroy(n.land);if(n.water)Destroy(n.water);n.go=null;n.land=n.water=null;n.positions=n.canonicalPositions=n.boundaryPositions=null;n.samples=null;n.bytes=0;n.seamsInitialized=false;Array.Clear(n.neighbours,0,4);removed++;}
        }
        public void Clear()
        {lifetime?.Cancel();if(uploading!=null)ReleaseJob(uploading.node);DiscardStaging();foreach(var n in all){n.cancel?.Cancel();if(n.go)Destroy(n.go);if(n.land)Destroy(n.land);if(n.water)Destroy(n.water);}all.Clear();requests.Clear();Array.Clear(roots,0,6);lifetime?.Dispose();lifetime=null;residentBytes=meshBytes=0;visibleTiles=0;evicting=false;evictCursor=0;}
        void OnDestroy(){Clear();}
    }
}
