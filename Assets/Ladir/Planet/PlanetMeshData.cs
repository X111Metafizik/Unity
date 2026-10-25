using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ladir.Planet
{
    // Immutable topology, shared by all worker results of a given grid size.
    // Boundary vertices and skirts are first in the GPU stream, so an LOD
    // stitch uploads one compact range, not every vertex/attribute of a tile.
    internal sealed class PlanetGridTopology
    {
        static readonly ConcurrentDictionary<int,PlanetGridTopology> cache=new ConcurrentDictionary<int,PlanetGridTopology>();
        public readonly int[] gpuToRow,rowToGpu,indices;
        public readonly int boundaryCount;
        public static PlanetGridTopology Get(int cells)=>cache.GetOrAdd(cells,n=>new PlanetGridTopology(n));
        PlanetGridTopology(int n)
        {
            if(n<2||n>256)throw new ArgumentOutOfRangeException(nameof(n));
            int side=n+1,count=side*side,total=count+4*side,at=0;
            gpuToRow=new int[total];rowToGpu=new int[total];
            for(int y=0;y<=n;y++)for(int x=0;x<=n;x++)if(x==0||y==0||x==n||y==n)gpuToRow[at++]=y*side+x;
            for(int i=count;i<total;i++)gpuToRow[at++]=i;boundaryCount=at;
            for(int y=1;y<n;y++)for(int x=1;x<n;x++)gpuToRow[at++]=y*side+x;
            for(int i=0;i<total;i++)rowToGpu[gpuToRow[i]]=i;
            indices=new int[n*n*6+4*n*6];at=0;
            void Tri(int a,int b,int c){indices[at++]=rowToGpu[a];indices[at++]=rowToGpu[c];indices[at++]=rowToGpu[b];}
            for(int y=0;y<n;y++)for(int x=0;x<n;x++){int a=y*side+x;Tri(a,a+1,a+side);Tri(a+1,a+side+1,a+side);}
            for(int edge=0;edge<4;edge++)for(int k=0;k<n;k++)
            {int src=edge==0?k:edge==1?k*side+n:edge==2?n*side+n-k:(n-k)*side,dst=count+edge*side+k,next=edge==0?src+1:edge==1?src+side:edge==2?src-1:src-side;Tri(src,dst,next);Tri(next,dst,dst+1);}
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct PlanetLandVertex
    {public Vector3 normal;public Color color;public Vector2 uv;public Vector4 climate;}
    [StructLayout(LayoutKind.Sequential)]
    internal struct PlanetWaterVertex
    {public Vector3 normal;public Vector2 uv;}
    internal static class PlanetMeshData
    {
        public const MeshUpdateFlags Flags=MeshUpdateFlags.DontRecalculateBounds|MeshUpdateFlags.DontValidateIndices;
        public static readonly VertexAttributeDescriptor[] LandLayout={
            new VertexAttributeDescriptor(VertexAttribute.Position,VertexAttributeFormat.Float32,3,0),
            new VertexAttributeDescriptor(VertexAttribute.Normal,VertexAttributeFormat.Float32,3,1),
            new VertexAttributeDescriptor(VertexAttribute.Color,VertexAttributeFormat.Float32,4,1),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0,VertexAttributeFormat.Float32,2,1),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1,VertexAttributeFormat.Float32,4,1)};
        public static readonly VertexAttributeDescriptor[] WaterLayout={
            new VertexAttributeDescriptor(VertexAttribute.Position,VertexAttributeFormat.Float32,3,0),
            new VertexAttributeDescriptor(VertexAttribute.Normal,VertexAttributeFormat.Float32,3,1),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0,VertexAttributeFormat.Float32,2,1)};
        // Pure worker-side calculation. The same bounds are assigned at publish
        // time; no main-thread RecalculateBounds or attribute format conversion.
        public static Bounds Bounds(Vector3[] vertices)
        {
            if(vertices.Length==0)return new Bounds();var lo=vertices[0];var hi=lo;
            for(int i=1;i<vertices.Length;i++){lo=Vector3.Min(lo,vertices[i]);hi=Vector3.Max(hi,vertices[i]);}
            return new Bounds((lo+hi)*.5f,hi-lo);
        }
        public static void Finish(Mesh mesh,int[] indices,int vertices,Bounds bounds)
        {
            mesh.SetIndexBufferData(indices,0,0,indices.Length,Flags);
            mesh.subMeshCount=1;
            mesh.SetSubMesh(0,new SubMeshDescriptor(0,indices.Length){bounds=bounds,vertexCount=vertices},Flags);
            mesh.bounds=bounds;
        }
    }
}
