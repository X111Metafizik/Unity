using System;
using System.Collections.Generic;
using System.Threading;

namespace Ladir.Planet
{
    // Diagnostic Priority-Flood over the final surface, with the same eight
    // neighbours, open boundary and deterministic tie-break as the web tool.
    // It never fills or changes the canonical terrain or authored lake levels.
    public sealed class ReferenceAnalysis
    {
        public int resolution,downhillErrors;public double spacing,massError,maxAccumulation;
        public double[] heights,drainage,accumulation;public int[] receiver,basin;
        public static ReferenceAnalysis Run(TerrainKernel kernel,int resolution=256,CancellationToken token=default)
        {
            if(resolution<8||resolution>512)throw new ArgumentOutOfRangeException(nameof(resolution));int side=resolution+1,count=side*side;double spacing=32000.0/resolution;
            var result=new ReferenceAnalysis{resolution=resolution,spacing=spacing,heights=new double[count],drainage=new double[count],accumulation=new double[count],receiver=new int[count],basin=new int[count]};
            var rain=new float[count];var visited=new bool[count];var order=new int[count];var heap=new List<int>();
            bool Less(int a,int b)=>result.drainage[a]<result.drainage[b]||result.drainage[a]==result.drainage[b]&&a<b;
            void Push(int value){int i=heap.Count;heap.Add(value);while(i>0){int parent=(i-1)/2;if(!Less(value,heap[parent]))break;heap[i]=heap[parent];i=parent;}heap[i]=value;}
            int Pop(){int first=heap[0],value=heap[heap.Count-1];heap.RemoveAt(heap.Count-1);if(heap.Count>0){int i=0;while(true){int j=i*2+1;if(j>=heap.Count)break;if(j+1<heap.Count&&Less(heap[j+1],heap[j]))j++;if(!Less(heap[j],value))break;heap[i]=heap[j];i=j;}heap[i]=value;}return first;}
            for(int y=0;y<side;y++){token.ThrowIfCancellationRequested();for(int x=0;x<side;x++){
                int id=y*side+x;var d=kernel.World.Direction((-19200+x*spacing-16000)/kernel.World.radiusMeters,(48000+y*spacing-16000)/kernel.World.radiusMeters);var sample=kernel.Sample(d);result.heights[id]=sample.height;rain[id]=(float)sample.moisture;result.receiver[id]=-1;result.basin[id]=-1;
                if(x==0||y==0||x==resolution||y==resolution){visited[id]=true;result.drainage[id]=sample.height;result.basin[id]=id;Push(id);}
            }}
            int[] dx={-1,1,0,0,-1,1,-1,1},dy={0,0,-1,1,-1,-1,1,1};int at=0;
            while(heap.Count>0){if((at&1023)==0)token.ThrowIfCancellationRequested();int id=Pop();order[at++]=id;int x=id%side,y=id/side;for(int j=0;j<8;j++){int nx=x+dx[j],ny=y+dy[j];if(nx<0||ny<0||nx>=side||ny>=side)continue;int b=ny*side+nx;if(visited[b])continue;visited[b]=true;result.drainage[b]=Math.Max(result.heights[b],result.drainage[id]);result.receiver[b]=id;result.basin[b]=result.basin[id];Push(b);}}
            double input=0,output=0;var rank=new int[count];for(int i=0;i<count;i++){rank[order[i]]=i;input+=rain[i]*spacing*spacing;result.accumulation[i]=rain[i]*spacing*spacing;}
            for(int i=count-1;i>=0;i--){int id=order[i],r=result.receiver[id];if(r>=0)result.accumulation[r]+=result.accumulation[id];}
            for(int i=0;i<count;i++){result.maxAccumulation=Math.Max(result.maxAccumulation,result.accumulation[i]);int r=result.receiver[i];if(r<0)output+=result.accumulation[i];else if(rank[r]>=rank[i]||result.drainage[r]>result.drainage[i])result.downhillErrors++;}
            result.massError=input==0?0:Math.Abs(input-output)/input;return result;
        }
    }
}
