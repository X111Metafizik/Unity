using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace Ladir {
 [Serializable] public class RegionManifest {
  public string format, worldHash, seed, generatorVersion, generatorBuildHash, overviewSha256;
  public double radiusMeters; public float sizeMeters, tileMeters, fineStepMeters, overviewStepMeters;
  public int tilesPerAxis, fineCells; public TileRecord[] tiles; public CheckPoint[] checks;
 }
 [Serializable] public class TileRecord { public int id,x,z,bytes,rawBytes; public float min,max; public string file,sha256; }
 [Serializable] public class CheckPoint { public float x,z,height,water; }
 // Shared read-only height/material data owns CPU collision and every visual LOD.
 public sealed class RegionData : IDisposable {
  public int side; public float step,minX,minZ; public double radius;
  public NativeArray<float> heights,water; public NativeArray<Color32> colors;
  public long Bytes => (long)side*side*12;
  public static RegionData Read(string path,string hash,CancellationToken token=default) {
   token.ThrowIfCancellationRequested(); byte[] raw;
   using(var file=File.OpenRead(path)) using(var gzip=new GZipStream(file,CompressionMode.Decompress)) using(var output=new MemoryStream()) {
    var scratch=new byte[65536];int n;
    while((n=gzip.Read(scratch,0,scratch.Length))>0){token.ThrowIfCancellationRequested();output.Write(scratch,0,n);if(output.Length>16*1024*1024)throw new InvalidDataException("Region tile exceeds byte budget");}
    raw=output.ToArray();
   }
   using(var sha=SHA256.Create()) if(BitConverter.ToString(sha.ComputeHash(raw)).Replace("-","").ToLowerInvariant()!=hash)throw new InvalidDataException("Region checksum mismatch: "+Path.GetFileName(path));
   if(raw.Length<40||Encoding.ASCII.GetString(raw,0,8)!="LADIR001")throw new InvalidDataException("Region header");
   var d=new RegionData {side=BitConverter.ToInt32(raw,8),step=BitConverter.ToSingle(raw,12),minX=BitConverter.ToSingle(raw,16),minZ=BitConverter.ToSingle(raw,20),radius=BitConverter.ToDouble(raw,24)};
   if(d.side<2||d.side>513||raw.Length!=40+d.side*d.side*12||d.step<=0||d.radius!=4000000)throw new InvalidDataException("Region shape or world radius");
   int count=d.side*d.side;
   d.heights=new NativeArray<float>(count,Allocator.Persistent,NativeArrayOptions.UninitializedMemory);
   d.water=new NativeArray<float>(count,Allocator.Persistent,NativeArrayOptions.UninitializedMemory);
   d.colors=new NativeArray<Color32>(count,Allocator.Persistent,NativeArrayOptions.UninitializedMemory);
   unsafe {fixed(byte* p=raw){UnsafeUtility.MemCpy(d.heights.GetUnsafePtr(),p+40,count*4);UnsafeUtility.MemCpy(d.water.GetUnsafePtr(),p+40+count*4,count*4);UnsafeUtility.MemCpy(d.colors.GetUnsafePtr(),p+40+count*8,count*4);}}
   for(int i=0;i<count;i++)if(!float.IsFinite(d.heights[i])||!float.IsFinite(d.water[i])){d.Dispose();throw new InvalidDataException("Non-finite terrain sample");}
   return d;
  }
  public float Height(float x,float z)=>Sample(heights,side,step,minX,minZ,x,z);
  public float Water(float x,float z)=>Sample(water,side,step,minX,minZ,x,z);
  public static float Sample(NativeArray<float> data,int side,float step,float minX,float minZ,float x,float z) {
   float fx=math.clamp((x-minX)/step,0,side-1),fz=math.clamp((z-minZ)/step,0,side-1);int ix=math.min(side-2,(int)fx),iz=math.min(side-2,(int)fz);float tx=fx-ix,tz=fz-iz;int a=iz*side+ix;
   return math.lerp(math.lerp(data[a],data[a+1],tx),math.lerp(data[a+side],data[a+side+1],tx),tz);
  }
  public void Dispose(){if(heights.IsCreated)heights.Dispose();if(water.IsCreated)water.Dispose();if(colors.IsCreated)colors.Dispose();}
 }
}
