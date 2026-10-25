using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ladir.Planet
{
    [Serializable] public class PlanetConfig
    {
        public string seed="LUMEN-742",worldType="terran",terrainModel="watershed-2",stampModel="global-2",profile="starfront-terran",mode="new-planet";
        public double radiusMeters=4000000,landBalance=.48,mountains=.65,plains=.58,erosion=.5,terrainStamps=1,seaLevel=0,temperature=30,rainfall=.55,forestDensity=.65;
        public int stampLayers=3,analysisResolution=48,regionCount=48;
    }
    [Serializable] public class PlanetFrame { public double[] center,east,north; }
    [Serializable] public class RiverPoint
    { public double x,y,waterLevelMeters,bedLevelMeters,widthMeters,depthMeters,flow;public double valleyWidthMeters=2800;public double[] direction; [JsonIgnore] public D3 D=>new D3(direction); }
    [Serializable] public class River
    { public string id,name,recipientId;public bool network,regional,stream;public double startY,endY,flow;public RiverPoint[] points; }
    [Serializable] public class Lake
    { public string id,name,outletId;public double x,y,radiusMeters,waterLevelMeters,spillLevelMeters,depthMeters;public bool regional,closedBasin;public double[] direction; }
    [Serializable] public class Hydrology { public River[] rivers=Array.Empty<River>();public Lake[] lakes=Array.Empty<Lake>();public string oceanId; }
    [Serializable] public class Crossing { public string id,name,riverId,portalId;public double riverT,widthMeters;public double[] direction;public double[][] anchors; }
    [Serializable] public class Strait {public string id,name;public double[] direction,entrance,exit;public double[][] centerline;public double minimumDepthMeters,minimumWidthMeters;}
    [Serializable] public class Landmark { public string id,name;public double[] direction;public double rangeMeters=10000,tilt=1; }
    [Serializable] public class PlanetWorld
    {
        public double radiusMeters;public uint terrainSeed,climateSeed,vegetationSeed;public double[] ridgeAxis;
        public PlanetConfig generationConfig;public PlanetFrame frame;public Hydrology hydrology;public Crossing[] crossings=Array.Empty<Crossing>();public Strait[] straits=Array.Empty<Strait>();public Landmark[] landmarks=Array.Empty<Landmark>();
        [JsonIgnore] public ushort[] Stamps;
        [JsonIgnore] public float[] Erosion;
        [JsonIgnore] public uint[] FaceNodes;
        [JsonIgnore] public JObject Metadata;
        [JsonIgnore] public Dictionary<string,byte[]> Buffers=new Dictionary<string,byte[]>();
        [JsonIgnore] public string Identity;
        [JsonIgnore] public D3 Center=>new D3(frame.center);
        [JsonIgnore] public D3 East=>new D3(frame.east);
        [JsonIgnore] public D3 North=>new D3(frame.north);
        [JsonIgnore] public bool Dry=>generationConfig.worldType=="desert"||generationConfig.worldType=="volcanic"||generationConfig.worldType=="mars"||generationConfig.worldType=="moon";
        public D3 Local(D3 d){double z=D3.Dot(d,Center);return z>.01?new D3(D3.Dot(d,East)/z,D3.Dot(d,North)/z,z):new D3(20,20,z);}
        public D3 Direction(double x,double y)=>(Center+East*x+North*y).Unit;

        public static PlanetWorld Load(string path)
        {
            var bytes=File.ReadAllBytes(path);if(bytes.Length<16||Encoding.ASCII.GetString(bytes,0,8)!="PLANET01")throw new InvalidDataException("Geçerli PLANET01 paketi gerekli.");
            int len=BitConverter.ToInt32(bytes,8),start=BitConverter.ToInt32(bytes,12);
            if(len<2||len>bytes.Length-16||start<16+len||start>bytes.Length)throw new InvalidDataException("Paket üstbilgisi bozuk.");
            var root=JObject.Parse(new UTF8Encoding(false,true).GetString(bytes,16,len));
            if((int?)root["manifest"]?["schemaVersion"]!=1)throw new InvalidDataException("Bilinmeyen dünya şeması.");
            var w=root.ToObject<PlanetWorld>();w.Metadata=root;
            if(w.radiusMeters<10000||w.radiusMeters>1e9||w.generationConfig==null||w.frame==null||w.hydrology==null)throw new InvalidDataException("Dünya alanları eksik.");
            // Never reinterpret a legacy world's terrain model or radius.
            var version=(string)root["manifest"]?["generatorVersion"];
            if(version!="1.8.1"&&version!="1.8.0")throw new InvalidDataException("Bu Unity portu yalnız 1.8.0 / 1.8.1 dünya paketlerini destekler; eski paket dönüştürülmedi.");
            var build=(string)root["manifest"]?["generatorBuildHash"];if(build!=(version=="1.8.1"?"c1e6340e175ecf40c8a03c74ab8f29e7f1a66c27520f6becb64f64667e97e0ca":"264fe0af577151a24b686c4696567a787f6ec4002eb89431f27efe5f70e9310d"))throw new InvalidDataException("Üretim çekirdeği imzası farklı; dünya sessizce değiştirilmedi.");
            if(w.generationConfig.terrainStamps>0&&w.generationConfig.stampLayers<2)throw new InvalidDataException("Tek katmanlı eski stamp dünyası için açık dönüştürme gerekli.");
            if(w.radiusMeters!=w.generationConfig.radiusMeters||(double?)root["manifest"]?["radiusMeters"]!=w.radiusMeters)throw new InvalidDataException("Dünya yarıçapı alanları uyuşmuyor.");
            long last=0;foreach(var a in (JArray)root["manifest"]["bufferDescriptors"])
            {
                string name=(string)a["name"],type=(string)a["type"];int n=(int)a["length"],count=(int)a["byteLength"],offset=(int)a["offset"];
                int size=type=="Float64Array"?8:type=="Uint8Array"?1:type=="Float32Array"||type=="Uint32Array"||type=="Int32Array"?4:0;
                if(size==0||n<0||count!=(long)n*size||offset<last||(long)start+offset+count>bytes.Length||w.Buffers.ContainsKey(name))throw new InvalidDataException("Paket sayısal alanı bozuk.");
                var data=new byte[count];Buffer.BlockCopy(bytes,start+offset,data,0,count);w.Buffers.Add(name,data);last=(long)offset+count;
            }
            if(w.Buffers.TryGetValue("stampHeights",out var stamp)){w.Stamps=new ushort[stamp.Length/2];Buffer.BlockCopy(stamp,0,w.Stamps,0,stamp.Length);}
            if(w.Buffers.TryGetValue("erosionDelta",out var erosion)){w.Erosion=new float[erosion.Length/4];Buffer.BlockCopy(erosion,0,w.Erosion,0,erosion.Length);}
            if(w.Buffers.TryGetValue("faceNodeIds",out var faces)){w.FaceNodes=new uint[faces.Length/4];Buffer.BlockCopy(faces,0,w.FaceNodes,0,faces.Length);}
            using(var sha=SHA256.Create())w.Identity=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
            if(File.Exists(path+".sha256")&&!string.Equals(File.ReadAllText(path+".sha256").Trim(),w.Identity,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Paket SHA-256 bütünlük kontrolü başarısız.");
            return w;
        }
        public void Save(string path,JObject appearance=null)
        {
            var root=(JObject)Metadata.DeepClone();if(appearance!=null)root["appearanceConfig"]=appearance;
            byte[] json=Encoding.UTF8.GetBytes(root.ToString(Formatting.None));int start=(16+json.Length+7)/8*8;
            long count=0;foreach(var d in (JArray)root["manifest"]["bufferDescriptors"])count=Math.Max(count,(long)d["offset"]+(long)d["byteLength"]);
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                using(var stream=File.Create(temporary))using(var writer=new BinaryWriter(stream))
                {writer.Write(Encoding.ASCII.GetBytes("PLANET01"));writer.Write(json.Length);writer.Write(start);writer.Write(json);while(stream.Position<start)writer.Write((byte)0);foreach(var d in (JArray)root["manifest"]["bufferDescriptors"]){stream.Position=start+(long)d["offset"];writer.Write(Buffers[(string)d["name"]]);}stream.SetLength(start+count);}
                string sha;using(var hash=SHA256.Create())using(var stream=File.OpenRead(temporary))sha=BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
                if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);File.WriteAllText(path+".sha256",sha+"\n");
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
    public struct SurfaceSample
    { public double height,water,temperature,moisture,protection,flow,lava,albedo,snow,snowLine,forest;public int biome;public string feature;public double Depth=>Math.Max(0,water-height); }
}
