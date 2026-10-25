using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Ladir {
 [StructLayout(LayoutKind.Sequential)] public struct TerrainVertex { public float3 position,normal; public Color32 color; }
 [BurstCompile(FloatPrecision.High,FloatMode.Strict)] public struct TerrainJob : IJobParallelFor {
  [ReadOnly] public NativeArray<float> heights,water,overview;
  [ReadOnly] public NativeArray<Color32> colors;
  public int side,cells,overviewSide; public int4 neighbors;
  public float step,minX,minZ,tileX,tileZ,overviewStep,radius;
  [WriteOnly] public NativeArray<TerrainVertex> vertices;
  public static float Sag(float x,float z,float radius)=> (x*x+z*z)/(2*radius);
  public float Height(float x,float z)=>RegionData.Sample(heights,side,step,minX,minZ,x,z)-Sag(x,z,radius);
  float OverviewHeight(float x,float z)=>RegionData.Sample(overview,overviewSide,overviewStep,-16000,-16000,x,z)-Sag(x,z,radius);
  public float3 Normal(float x,float z){float s=overviewStep;
   var shared=math.normalize(new float3(OverviewHeight(x-s,z)-OverviewHeight(x+s,z),2*s,OverviewHeight(x,z-s)-OverviewHeight(x,z+s)));
   if(side>257)return shared;
   float border=math.min(math.min(x-minX,minX+2000-x),math.min(z-minZ,minZ+2000-z));
   var fine=math.normalize(new float3(Height(x-step,z)-Height(x+step,z),2*step,Height(x,z-step)-Height(x,z+step)));
   return math.normalize(math.lerp(shared,fine,math.saturate(border/(2*step))));
  }
  public TerrainVertex At(float x,float z){int ix=(int)math.round(math.clamp((x-minX)/step,0,side-1)),iz=(int)math.round(math.clamp((z-minZ)/step,0,side-1));return new TerrainVertex {position=new float3(x,Height(x,z),z),normal=Normal(x,z),color=colors[iz*side+ix]};}
  public TerrainVertex Vertex(int i,int j){float x=tileX+i*2000f/cells,z=tileZ+j*2000f/cells;var v=At(x,z);
   // Both sides use exactly the coarser edge chord. Plans swap only after all
   // new edge contracts have been uploaded, retaining the old surface meanwhile.
   for(int edge=0;edge<4;edge++){bool on=edge==0?j==0:edge==1?i==cells:edge==2?j==cells:i==0;int n=neighbors[edge];if(!on||n>=cells||n==0)continue;float k=(edge%2==0?i:j)*(float)n/cells,a=math.floor(k),t=k-a;var va=At(edge%2==0?tileX+a*2000/n:x,edge%2==0?z:tileZ+a*2000/n);var vb=At(edge%2==0?tileX+(a+1)*2000/n:x,edge%2==0?z:tileZ+(a+1)*2000/n);v.position=math.lerp(va.position,vb.position,t);v.normal=math.normalize(math.lerp(va.normal,vb.normal,t));v.color=(Color32)Color.Lerp(va.color,vb.color,t);}
   v.position-=new float3(tileX,0,tileZ);return v;
  }
  public void Execute(int index)=>vertices[index]=Vertex(index%(cells+1),index/(cells+1));
 }
 [BurstCompile(FloatPrecision.High,FloatMode.Strict)] public struct WaterJob : IJob {
  [ReadOnly] public NativeArray<float> heights,water;
  public int side,cells; public float step,minX,minZ,tileX,tileZ,radius;
  public NativeList<TerrainVertex> vertices;
  struct Point {public float2 xz;public float bed,level;}
  Point At(int x,int z){float xx=tileX+x*2000f/cells,zz=tileZ+z*2000f/cells;return new Point {xz=new float2(xx,zz),bed=RegionData.Sample(heights,side,step,minX,minZ,xx,zz),level=RegionData.Sample(water,side,step,minX,minZ,xx,zz)};}
  public void Execute(){for(int bz=0;bz<cells;bz+=8)for(int bx=0;bx<cells;bx+=8){int ex=math.min(cells,bx+8),ez=math.min(cells,bz+8);Point first=At(bx,bz);bool flatWet=first.bed<first.level;
   for(int z=bz;z<=ez&&flatWet;z++)for(int x=bx;x<=ex;x++){Point p=At(x,z);if(p.bed>=p.level||math.abs(p.level-first.level)>.005f){flatWet=false;break;}}
   if(flatWet){Point a=At(bx,bz),b=At(ex,bz),c=At(bx,ez),d=At(ex,ez);Add(a);Add(c);Add(b);Add(b);Add(c);Add(d);continue;}
   for(int z=bz;z<ez;z++)for(int x=bx;x<ex;x++){Point a=At(x,z),b=At(x+1,z),c=At(x,z+1),d=At(x+1,z+1);Clip(a,c,b);Clip(b,c,d);}
  }}
  void Clip(Point a,Point b,Point c){bool wa=a.bed<a.level,wb=b.bed<b.level,wc=c.bed<c.level;if(!wa&&!wb&&!wc)return;
   // Dry bank samples outside a lake carry sea level. Extrapolate the wet
   // body's level for clipping instead of sloping the lake toward sea level.
   float level=math.max(wa?a.level:-100000,math.max(wb?b.level:-100000,wc?c.level:-100000));
   if(!wa)a.level=level;if(!wb)b.level=level;if(!wc)c.level=level;
   var input=new FixedList128Bytes<Point>();input.Add(a);input.Add(b);input.Add(c);var poly=new FixedList128Bytes<Point>();
   for(int i=0;i<3;i++){Point p=input[i],q=input[(i+1)%3];float dp=p.level-p.bed,dq=q.level-q.bed;if(dp>=0)poly.Add(p);if((dp>=0)!=(dq>=0)){float t=dp/(dp-dq);poly.Add(new Point {xz=math.lerp(p.xz,q.xz,t),bed=math.lerp(p.bed,q.bed,t),level=math.lerp(p.level,q.level,t)});}}
   for(int i=1;i+1<poly.Length;i++){Add(poly[0]);Add(poly[i]);Add(poly[i+1]);}
  }
  void Add(Point p){vertices.Add(new TerrainVertex {position=new float3(p.xz.x-tileX,p.level+.08f-TerrainJob.Sag(p.xz.x,p.xz.y,radius),p.xz.y-tileZ),normal=new float3(0,1,0),color=new Color32(15,75,86,255)});}
 }
}
