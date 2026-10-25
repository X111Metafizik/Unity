using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug=UnityEngine.Debug;

namespace Ladir {
 // A local native proof of concept. There is no second terrain generator here:
 // all LODs, water and collision originate in the exported canonical samples.
 public sealed class RegionStreamer : MonoBehaviour {
  public Material terrainMaterial,waterMaterial;
  public Transform observer;
  public RegionManifest manifest {get;private set;}
  public string Error {get;private set;}="";
  public bool Ready {get;private set;}
  public bool Detailed {get;private set;}
  public float firstSurfaceMs,firstDetailMs,readTotalMs,jobTotalMs,uploadTotalMs,maxUploadMs,processAgeAtSceneStartMs;
  public int readCount,committedPlans,pendingReads,pendingMeshes,activeCells;
  public long CacheBytes {get;private set;} public long MeshBytes {get;private set;}
  public long PeakCacheBytes {get;private set;} public long PeakMeshBytes {get;private set;}
  public float uploadBudgetMs=2; public long cacheBudgetBytes=96L*1024*1024;
  const int Count=256;
  readonly CancellationTokenSource lifetime=new CancellationTokenSource();
  RegionData overview; Task<RegionData> overviewRead;
  readonly Dictionary<int,RegionData> cache=new Dictionary<int,RegionData>();
  readonly Dictionary<int,int> lastUsed=new Dictionary<int,int>();
  readonly Dictionary<int,ReadRequest> reads=new Dictionary<int,ReadRequest>();
  readonly Dictionary<int,int[]> indices=new Dictionary<int,int[]>();
  readonly List<Build> jobs=new List<Build>();
  readonly Queue<int> queue=new Queue<int>();
  readonly Patch[] visible=new Patch[Count],staged=new Patch[Count];
  readonly int[] wanted=new int[Count];
  readonly int4[] contracts=new int4[Count];
  string directory; bool planning; Vector3 planPosition; float nextPlan; long start;
  static readonly VertexAttributeDescriptor[] Layout={new VertexAttributeDescriptor(VertexAttribute.Position,VertexAttributeFormat.Float32,3),new VertexAttributeDescriptor(VertexAttribute.Normal,VertexAttributeFormat.Float32,3),new VertexAttributeDescriptor(VertexAttribute.Color,VertexAttributeFormat.UNorm8,4)};
  const MeshUpdateFlags Flags=MeshUpdateFlags.DontRecalculateBounds|MeshUpdateFlags.DontValidateIndices|MeshUpdateFlags.DontNotifyMeshUsers;
  sealed class ReadRequest {public Task<RegionData> task;public CancellationTokenSource cancel;public long started;}
  sealed class Build {public int id,cells;public int4 neighbors;public NativeArray<TerrainVertex> vertices;public NativeList<TerrainVertex> water;public JobHandle handle;public long started;}
  sealed class Patch {
   public int cells;public int4 neighbors;public GameObject go;public Mesh ground,water;public NativeArray<TerrainVertex> vertices;public long bytes;
   public void Dispose(){if(go)UnityEngine.Object.Destroy(go);if(ground)UnityEngine.Object.Destroy(ground);if(water)UnityEngine.Object.Destroy(water);if(vertices.IsCreated)vertices.Dispose();}
  }
  static double Ms(long since)=>(Stopwatch.GetTimestamp()-since)*1000.0/Stopwatch.Frequency;
  void Start(){start=Stopwatch.GetTimestamp();processAgeAtSceneStartMs=(float)(DateTime.UtcNow-Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalMilliseconds;directory=Path.Combine(Application.streamingAssetsPath,"Ladir/reference");
   try{manifest=JsonUtility.FromJson<RegionManifest>(File.ReadAllText(Path.Combine(directory,"manifest.json")));
    if(manifest.format!="ladir-unity-region-1"||manifest.radiusMeters!=4000000||manifest.tiles.Length!=Count||manifest.tileMeters!=2000||manifest.fineCells!=256)throw new InvalidDataException("Unsupported region manifest");
    overviewRead=Task.Run(()=>RegionData.Read(Path.Combine(directory,"overview.bin.gz"),manifest.overviewSha256,lifetime.Token),lifetime.Token);
   }catch(Exception e){Fail(e);}
  }
  void Fail(Exception e){Error=e.GetBaseException().Message;Debug.LogError("LADIR streaming: "+e);enabled=false;}
  void Update(){try{
   if(overview==null){if(overviewRead==null||!overviewRead.IsCompleted)return;overview=overviewRead.GetAwaiter().GetResult();CacheBytes=overview.Bytes;BeginPlan(true);}
   CollectReads();UploadFinished();
   if(planning){Schedule();if(queue.Count==0&&jobs.Count==0&&Enumerable.Range(0,Count).All(i=>Matches(visible[i],i)||staged[i]!=null))Commit();}
   else if(observer&&Time.unscaledTime>=nextPlan){nextPlan=Time.unscaledTime+.5f;BeginPlan(false);}
   Evict();pendingReads=reads.Count;pendingMeshes=queue.Count+jobs.Count;PeakCacheBytes=Math.Max(PeakCacheBytes,CacheBytes);PeakMeshBytes=Math.Max(PeakMeshBytes,MeshBytes);
  }catch(Exception e){Fail(e);}}
  int Desired(int id,Vector3 center){float x=(id%16)*2000-15000,z=(id/16)*2000-15000;float distance=Vector2.Distance(new Vector2(x,z),new Vector2(center.x,center.z));
   // Metric spacing is independent from planet radius; near detail is preserved
   // during boost. Only predictive loading moves in the velocity direction.
   int target=distance<3000?256:distance<6000?128:distance<10000?64:distance<18000?32:16;
   int previous=visible[id]?.cells??16;
   if(target!=previous){float boundary=target>previous?(previous==16?18000:previous==32?10000:previous==64?6000:3000):(previous==256?3000:previous==128?6000:previous==64?10000:18000);if(Mathf.Abs(distance-boundary)<350)return previous;}
   return target;
  }
  int4 Neighbors(int id)=>new int4(id>=16?wanted[id-16]:wanted[id],id%16<15?wanted[id+1]:wanted[id],id<240?wanted[id+16]:wanted[id],id%16>0?wanted[id-1]:wanted[id]);
  bool Matches(Patch p,int i)=>p!=null&&p.cells==wanted[i]&&math.all(p.neighbors==contracts[i]);
  void BeginPlan(bool initial){planPosition=observer?observer.position:Vector3.zero;for(int i=0;i<Count;i++)wanted[i]=initial?16:Desired(i,planPosition);
   for(int i=0;i<Count;i++)contracts[i]=Neighbors(i);var order=new List<int>();for(int i=0;i<Count;i++)if(!Matches(visible[i],i))order.Add(i);
   order.Sort((a,b)=>Distance(a,planPosition).CompareTo(Distance(b,planPosition)));foreach(int i in order)queue.Enqueue(i);planning=queue.Count>0;
   // Cancel files no longer useful before starting a new streaming plan.
   foreach(var pair in reads)if(wanted[pair.Key]<=32)pair.Value.cancel.Cancel();
  }
  static float Distance(int id,Vector3 p)=>Mathf.Abs(id%16*2000-15000-p.x)+Mathf.Abs(id/16*2000-15000-p.z);
  void Request(int id){if(cache.ContainsKey(id)||reads.ContainsKey(id)||reads.Count>=3)return;var tile=manifest.tiles[id];var cancel=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var token=cancel.Token;
   reads.Add(id,new ReadRequest {cancel=cancel,started=Stopwatch.GetTimestamp(),task=Task.Run(()=>RegionData.Read(Path.Combine(directory,tile.file),tile.sha256,token),token)});
  }
  void CollectReads(){if(reads.Count==0)return;foreach(int id in reads.Where(p=>p.Value.task.IsCompleted).Select(p=>p.Key).ToArray()){
   var r=reads[id];reads.Remove(id);r.cancel.Dispose();if(r.task.IsCanceled)continue;
   RegionData data=r.task.GetAwaiter().GetResult();cache.Add(id,data);lastUsed[id]=Time.frameCount;CacheBytes+=data.Bytes;readCount++;readTotalMs+=(float)Ms(r.started);
  }}
  void Schedule(){int attempts=queue.Count;
   while(attempts-->0&&queue.Count>0&&jobs.Count<4){int id=queue.Dequeue();RegionData data=overview;
    if(wanted[id]>32&&!cache.TryGetValue(id,out data)){Request(id);queue.Enqueue(id);continue;}
    if(wanted[id]>32)lastUsed[id]=Time.frameCount;
    int n=wanted[id],count=(n+1)*(n+1);var b=new Build {id=id,cells=n,neighbors=contracts[id],started=Stopwatch.GetTimestamp(),vertices=new NativeArray<TerrainVertex>(count,Allocator.Persistent,NativeArrayOptions.UninitializedMemory),water=new NativeList<TerrainVertex>(1024,Allocator.Persistent)};
    float x=id%16*2000-16000,z=id/16*2000-16000;
    var terrain=new TerrainJob {heights=data.heights,water=data.water,colors=data.colors,overview=overview.heights,side=data.side,cells=n,neighbors=b.neighbors,overviewSide=overview.side,step=data.step,minX=data.minX,minZ=data.minZ,tileX=x,tileZ=z,overviewStep=overview.step,radius=(float)manifest.radiusMeters,vertices=b.vertices};
    var water=new WaterJob {heights=data.heights,water=data.water,side=data.side,cells=n,step=data.step,minX=data.minX,minZ=data.minZ,tileX=x,tileZ=z,radius=(float)manifest.radiusMeters,vertices=b.water};
    b.handle=JobHandle.CombineDependencies(terrain.Schedule(count,128),water.Schedule());jobs.Add(b);
   }
   JobHandle.ScheduleBatchedJobs();
   // Predict two seconds of travel without lowering current terrain detail.
   if(observer&&observer.TryGetComponent<FlightController>(out var ship)&&reads.Count<3){Vector3 next=observer.position+ship.Velocity*2;int x=Mathf.Clamp(Mathf.FloorToInt((next.x+16000)/2000),0,15),z=Mathf.Clamp(Mathf.FloorToInt((next.z+16000)/2000),0,15);Request(z*16+x);}
  }
  int[] Indices(int cells){if(indices.TryGetValue(cells,out var result))return result;result=new int[cells*cells*6];int p=0;
   for(int z=0;z<cells;z++)for(int x=0;x<cells;x++){int a=z*(cells+1)+x,b=a+1,c=a+cells+1,d=c+1;result[p++]=a;result[p++]=c;result[p++]=b;result[p++]=b;result[p++]=c;result[p++]=d;}indices.Add(cells,result);return result;
  }
  Mesh MeshFrom(NativeArray<TerrainVertex> vertices,int[] triangles,Bounds bounds){var mesh=new Mesh {indexFormat=IndexFormat.UInt32,name="LADIR region mesh"};mesh.SetVertexBufferParams(vertices.Length,Layout);mesh.SetVertexBufferData(vertices,0,0,vertices.Length,0,Flags);mesh.SetIndexBufferParams(triangles.Length,IndexFormat.UInt32);mesh.SetIndexBufferData(triangles,0,0,triangles.Length,Flags);mesh.subMeshCount=1;mesh.SetSubMesh(0,new SubMeshDescriptor(0,triangles.Length){bounds=bounds,vertexCount=vertices.Length},Flags);mesh.bounds=bounds;mesh.UploadMeshData(true);return mesh;}
  void UploadFinished(){long frameStart=Stopwatch.GetTimestamp();int uploaded=0;
   for(int k=jobs.Count-1;k>=0;k--){var b=jobs[k];if(!b.handle.IsCompleted)continue;if(uploaded>0&&Ms(frameStart)>=uploadBudgetMs)break;b.handle.Complete();jobTotalMs+=(float)Ms(b.started);
    long uploadStart=Stopwatch.GetTimestamp();var record=manifest.tiles[b.id];var bounds=new Bounds(new Vector3(1000,(record.min+record.max)*.5f,1000),new Vector3(2002,record.max-record.min+300,2002));
    var p=new Patch {cells=b.cells,neighbors=b.neighbors,vertices=b.vertices};p.ground=MeshFrom(b.vertices,Indices(b.cells),bounds);p.go=new GameObject("Tile "+b.id+" / "+(2000f/b.cells).ToString("0.##")+"m");p.go.SetActive(false);p.go.transform.SetParent(transform,false);p.go.transform.localPosition=new Vector3(b.id%16*2000-16000,0,b.id/16*2000-16000);
    p.go.AddComponent<MeshFilter>().sharedMesh=p.ground;var renderer=p.go.AddComponent<MeshRenderer>();renderer.sharedMaterial=terrainMaterial;renderer.shadowCastingMode=ShadowCastingMode.On;
    p.bytes=b.vertices.Length*28L+Indices(b.cells).Length*4L;
    if(b.water.Length>0){var waterIndices=new int[b.water.Length];for(int i=0;i<waterIndices.Length;i++)waterIndices[i]=i;p.water=MeshFrom(b.water.AsArray(),waterIndices,bounds);var w=new GameObject("Water");w.transform.SetParent(p.go.transform,false);w.AddComponent<MeshFilter>().sharedMesh=p.water;var wr=w.AddComponent<MeshRenderer>();wr.sharedMaterial=waterMaterial;wr.shadowCastingMode=ShadowCastingMode.Off;p.bytes+=b.water.Length*32L;}
    b.water.Dispose();staged[b.id]=p;MeshBytes+=p.bytes;jobs.RemoveAt(k);uploaded++;float ms=(float)Ms(uploadStart);uploadTotalMs+=ms;maxUploadMs=Mathf.Max(maxUploadMs,ms);
   }
  }
  void Commit(){for(int i=0;i<Count;i++)if(staged[i]!=null){if(visible[i]!=null){MeshBytes-=visible[i].bytes;visible[i].Dispose();}visible[i]=staged[i];staged[i]=null;visible[i].go.SetActive(true);}planning=false;committedPlans++;activeCells=visible.Sum(p=>p.cells*p.cells);
   if(!Ready){Ready=true;firstSurfaceMs=(float)Ms(start);nextPlan=0;}else if(!Detailed){Detailed=true;firstDetailMs=(float)Ms(start);}Debug.Log("LADIR terrain plan "+committedPlans+" ready, "+activeCells+" cells");
  }
  void Evict(){if(CacheBytes<=cacheBudgetBytes)return;foreach(int id in lastUsed.OrderBy(p=>p.Value).Select(p=>p.Key).ToArray()){
   if(wanted[id]>32||jobs.Any(j=>j.id==id))continue;var data=cache[id];CacheBytes-=data.Bytes;data.Dispose();cache.Remove(id);lastUsed.Remove(id);if(CacheBytes<=cacheBudgetBytes)break;
  }}
  public float Surface(float x,float z){if(overview==null)return 0;int tx=Mathf.Clamp((int)((x+16000)/2000),0,15),tz=Mathf.Clamp((int)((z+16000)/2000),0,15),id=tz*16+tx;var p=visible[id];if(p==null)return overview.Height(x,z)-TerrainJob.Sag(x,z,4000000);
   float fx=Mathf.Clamp((x-(tx*2000-16000))*p.cells/2000,0,p.cells),fz=Mathf.Clamp((z-(tz*2000-16000))*p.cells/2000,0,p.cells);int ix=Mathf.Min((int)fx,p.cells-1),iz=Mathf.Min((int)fz,p.cells-1),a=iz*(p.cells+1)+ix;float u=fx-ix,v=fz-iz;var vs=p.vertices;
   // Exact rendered triangle, including the stitched edge, not a second noise query.
   return u+v<=1?vs[a].position.y*(1-u-v)+vs[a+1].position.y*u+vs[a+p.cells+1].position.y*v:vs[a+p.cells+2].position.y*(u+v-1)+vs[a+1].position.y*(1-v)+vs[a+p.cells+1].position.y*(1-u);
  }
  public float Water(float x,float z){if(overview==null)return 0;int id=Mathf.Clamp((int)((z+16000)/2000),0,15)*16+Mathf.Clamp((int)((x+16000)/2000),0,15);var d=cache.TryGetValue(id,out var fine)?fine:overview;return d.Water(x,z)-TerrainJob.Sag(x,z,4000000);}
  public int CellsAt(float x,float z){int id=Mathf.Clamp((int)((z+16000)/2000),0,15)*16+Mathf.Clamp((int)((x+16000)/2000),0,15);return visible[id]?.cells??0;}
  void OnDestroy(){lifetime.Cancel();foreach(var b in jobs){b.handle.Complete();b.vertices.Dispose();b.water.Dispose();}foreach(var p in visible)p?.Dispose();foreach(var p in staged)p?.Dispose();foreach(var d in cache.Values)d.Dispose();overview?.Dispose();
   // A cancelled Task can finish just before cancellation. Dispose its ownership
   // once, including shutdown before overview first becomes ready.
   foreach(var r in reads.Values){r.cancel.Cancel();r.task.ContinueWith(t=>{if(t.Status==TaskStatus.RanToCompletion)t.Result.Dispose();r.cancel.Dispose();},TaskScheduler.Default);}
   if(overview==null&&overviewRead!=null)overviewRead.ContinueWith(t=>{if(t.Status==TaskStatus.RanToCompletion)t.Result.Dispose();},TaskScheduler.Default);lifetime.Dispose();
  }
 }
}
