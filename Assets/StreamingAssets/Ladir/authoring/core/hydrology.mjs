class Heap{
  data=[];
  less(a,b){return a.h<b.h||(a.h===b.h&&a.id<b.id);}
  push(v){let i=this.data.length;this.data.push(v);while(i){const p=(i-1)>>1;if(!this.less(v,this.data[p]))break;this.data[i]=this.data[p];i=p;}this.data[i]=v;}
  pop(){const result=this.data[0],v=this.data.pop();if(this.data.length){let i=0;while(true){let j=i*2+1;if(j>=this.data.length)break;if(j+1<this.data.length&&this.less(this.data[j+1],this.data[j]))j++;if(!this.less(this.data[j],v))break;this.data[i]=this.data[j];i=j;}this.data[i]=v;}return result;}
}
export function priorityFlood(graph,height,rainfall,seaLevel,allowDry=false,forcedOutlets=null){
  const n=height.length,drainage=new Float64Array(n),receiver=new Int32Array(n).fill(-1),order=new Uint32Array(n),visited=new Uint8Array(n),basin=new Int32Array(n).fill(-1),heap=new Heap(),ocean=new Uint8Array(n);
  // Classify connected below-sea components. The largest is the global ocean.
  let largest=[],component=[];
  for(let i=0;i<n;i++)if(!visited[i]&&height[i]<=seaLevel){
    component=[i];visited[i]=1;
    for(let q=0;q<component.length;q++){const a=component[q];for(let k=graph.offsets[a];k<graph.offsets[a+1];k++){const b=graph.neighbors[k];if(!visited[b]&&height[b]<=seaLevel){visited[b]=1;component.push(b);}}}
    if(component.length>largest.length)largest=component;
  }
  const dry=allowDry;
  if(dry){let low=0;for(let i=1;i<n;i++)if(height[i]<height[low])low=i;largest=[low];}
  if(forcedOutlets?.length)largest=[...forcedOutlets];
  if(!largest.length)throw Error('Küresel okyanus bulunamadı.');
  visited.fill(0);
  for(const id of largest){ocean[id]=dry?0:1;visited[id]=1;drainage[id]=dry?height[id]:seaLevel;basin[id]=id;heap.push({id,h:drainage[id]});}
  let count=0;
  while(heap.data.length){
    const {id,h}=heap.pop();order[count++]=id;
    for(let k=graph.offsets[id];k<graph.offsets[id+1];k++){
      const b=graph.neighbors[k];if(visited[b])continue;
      visited[b]=1;drainage[b]=Math.max(height[b],h);receiver[b]=id;basin[b]=basin[id];heap.push({id:b,h:drainage[b]});
    }
  }
  const accumulation=new Float64Array(n);
  for(let i=0;i<n;i++)accumulation[i]=rainfall[i]*graph.areas[i];
  for(let i=n-1;i>=0;i--){const id=order[i],r=receiver[id];if(r>=0)accumulation[r]+=accumulation[id];}
  return {drainage,receiver,order,accumulation,basin,ocean};
}
