using System;
using System.IO;
using System.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ladir.Planet;

class Program
{
    static int Main(string[] args)
    {
        string root=Path.GetFullPath(args.Length>0?args[0]:".");var fixtures=JObject.Parse(File.ReadAllText(Path.Combine(root,"evidence/planet-port/parity-fixtures.json")));
        var w=PlanetWorld.Load(Path.Combine(root,"Assets/StreamingAssets/Ladir/planet/river-v42.planet"));
        if(w.Identity!=(string)fixtures["worldFileSha256"])throw new Exception("World integrity mismatch");
        double maxHeight=0,maxWater=0,maxColor=0;int failed=0,count=0;var watch=Stopwatch.StartNew();TerrainKernel kernel=null;string type=null;
        foreach(var c in fixtures["cases"])
        {
            if(type!=(string)c["type"]){kernel?.Dispose();type=(string)c["type"];w.generationConfig.worldType=type;kernel=new TerrainKernel(w);}
            var d=new D3(c["d"].ToObject<double[]>());var s=kernel.Sample(d);var color=TerrainKernel.Color(s);var expected=new D3(c["color"].ToObject<double[]>());
            double height=Math.Abs(s.height-(double)c["height"]),water=Math.Abs(s.water-(double)c["water"]),colorError=(color-expected).Length;
            maxHeight=Math.Max(height,maxHeight);maxWater=Math.Max(water,maxWater);maxColor=Math.Max(colorError,maxColor);count++;
            if(height>.001||water>.001||colorError>.00001||s.biome!=(int)c["biome"]){failed++;if(failed<12)Console.WriteLine($"MISMATCH {type}/{c["name"]}: height {height:R}, water {water:R}, color {colorError:R}, biome {s.biome}/{c["biome"]}, base {kernel.Base(d)-(double)c["base"]:R}");}
        }
        kernel?.Dispose();watch.Stop();var result=new{count,failed,maxHeightErrorMeters=maxHeight,maxWaterErrorMeters=maxWater,maxColorError=maxColor,elapsedMs=watch.Elapsed.TotalMilliseconds,radius=w.radiusMeters,worldFileSha256=w.Identity};
        string json=JsonConvert.SerializeObject(result,Formatting.Indented);File.WriteAllText(Path.Combine(root,"evidence/planet-port/parity-results.json"),json);Console.WriteLine(json);
        w.generationConfig.worldType="terran";using(var physics=new TerrainKernel(w))
        {
            int checks=0;void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
            var vegetation=JArray.Parse(File.ReadAllText(Path.Combine(root,"evidence/planet-port/vegetation-fixtures.json")));int trees=0;double rootError=0;
            foreach(var test in vegetation)
            {
                var actual=VegetationField.Tile(physics,(int)test["face"],(int)test["tx"],(int)test["ty"],(int)test["level"]);var expected=(JArray)test["trees"];Check(actual.Length==expected.Count,"Canonical tree count");
                for(int i=0;i<actual.Length;i++){var actualTree=actual[i];var e=expected[i];double error=Math.Abs(actualTree.elevation-(double)e["elevationMeters"]);rootError=Math.Max(rootError,error);Check(error<.001&&(actualTree.direction-new D3(e["direction"].ToObject<double[]>())).Length<1e-12&&Math.Abs(actualTree.height-(double)e["heightMeters"])<1e-10&&actualTree.species==(int)e["species"]&&Math.Abs(actualTree.rotation-(double)e["rotation"])<1e-10,"Canonical tree root / height / species / rotation");trees++;}
            }
            File.WriteAllText(Path.Combine(root,"evidence/planet-port/vegetation-results.json"),JsonConvert.SerializeObject(new{passed=true,tiles=vegetation.Count,trees,maxRootErrorMeters=rootError},Formatting.Indented));
            checks=0;
            FlightModel landing=null;for(int y=-12000;y<=-4000&&landing==null;y+=4000)for(int x=-12000;x<=12000;x+=4000){var d=w.Direction((x-19200)/w.radiusMeters,(y+48000)/w.radiusMeters);var candidate=new FlightModel(physics,d,FlightModel.North(d));if(candidate.Landing()){landing=candidate;break;}}
            Check(landing!=null,"A dry landing site exists in the reference plain");foreach(var e in landing.enemies){e.health=0;e.respawn=double.PositiveInfinity;}for(int i=0;i<3600&&landing.state!="landed";i++)landing.Step(1.0/60);Check(landing.state=="landed"&&landing.agl>=FlightModel.Clearance-.01,"Landing stays above the canonical terrain");Check(landing.Landing(),"Takeoff accepted");for(int i=0;i<1800&&landing.state!="flying";i++)landing.Step(1.0/60);Check(landing.state=="flying"&&landing.agl>120,"Takeoff clearance");
            landing.paused=true;var paused=landing.position;landing.Step(1);Check((landing.position-paused).Length==0,"Pause freezes physics");landing.paused=false;landing.position=landing.up*(w.radiusMeters+10000);landing.pitch=0;landing.input=new FlightModel.FlightInput{boost=true};for(int i=0;i<180;i++)landing.Step(1.0/60);Check(landing.speed>490&&landing.boostEnergy<100,"Boost approaches 1800 km/h and consumes energy");landing.input=new FlightModel.FlightInput();for(int i=0;i<420;i++)landing.Step(1.0/60);Check(Math.Abs(landing.speed-FlightModel.Cruise)<.1,"Release returns smoothly to 700 km/h");
            if(w.crossings.Length>0&&w.crossings[0].anchors!=null){var crossing=w.crossings[0];var navigation=NavigationField.Query(physics,new D3(crossing.anchors[0]),new D3(crossing.anchors[1]),false,0,crossing);Check(navigation.passable&&navigation.surface.Length==navigation.route.Length,"Canonical ford route");}
            bool waterRouteTested=false;foreach(var lake in w.hydrology.lakes){var fromLake=w.Direction(lake.x,lake.y);var toLake=w.Direction(lake.x+25/w.radiusMeters,lake.y);if(physics.Sample(fromLake).Depth<2||physics.Sample(toLake).Depth<2)continue;var route=NavigationField.Query(physics,fromLake,toLake,true,1);Check(route.passable&&route.surface.Length==route.route.Length,"Water route follows canonical lake level");Check(!NavigationField.Query(physics,fromLake,toLake,true,1000000).passable,"Draft exceeding water depth rejects route");waterRouteTested=true;break;}Check(waterRouteTested,"A wet lake route was exercised");
            foreach(double distance in FlightModel.WarpDistances)
            {double duration=Math.Max(3,12*distance/12000000),previous=0;for(int i=0;i<=300;i++){var m=FlightModel.WarpMotion(duration*i/300,duration,distance,FlightModel.Cruise);Check(m.distance>=previous-1e-6,"Monotonic warp");previous=m.distance;}var last=FlightModel.WarpMotion(duration,duration,distance,FlightModel.Cruise);Check(Math.Abs(last.distance-distance)<1e-6&&Math.Abs(last.speed-FlightModel.Cruise)<1e-6,"Exact warp arrival");}
            var f=new FlightModel(physics,w.Center,FlightModel.North(w.Center));foreach(var e in f.enemies){e.health=0;e.respawn=double.PositiveInfinity;}f.position=w.Center*(w.radiusMeters+100000);f.previousPosition=f.position;f.forward=w.Center;f.pitch=1.55;f.Activate(1);
            for(int i=0;i<181;i++)f.Step(1.0/60);Check(f.state=="warping","Three second charge");var from=f.warpFrom;for(int i=0;i<721;i++)f.Step(1.0/60);Check(f.state=="flying"&&f.warpCooldown>19,"Warp completes and starts cooldown");Check((f.position-from).Length>=11999999,"Twelve thousand km outward warp");
            D3 a=w.Center*(w.radiusMeters+100000),b=-a;Check(f.Sweep(a,b,out var impact,out var normal),"Through-planet warp collision");Check(impact.Length>w.radiusMeters-10000,"Collision remains on surface");Check(Math.Abs(normal.Length-1)<1e-8,"Collision normal normalized");
            f.health=40;f.repairCooldown=0;f.Activate(2);for(int i=0;i<300;i++)f.Step(1.0/60);Check(Math.Abs(f.health-90)<1e-7&&Math.Abs(f.repairCooldown-25)<1e-7,"50 percent repair over five seconds; 30 second cooldown");
            var saved=Path.Combine(root,"tools/terrain-parity/bin/roundtrip.planet");w.Save(saved);Check(PlanetWorld.Load(saved).radiusMeters==4000000,"Package round trip radius");
            w.Save(saved,new JObject{{"clouds",.3}});var restored=PlanetWorld.Load(saved);Check(restored.radiusMeters==4000000,"Atomic overwrite updates checksum");
            Check((double)restored.appearanceConfig["clouds"]==.3,"Typed appearance matches the preserved package header");
            var originalMetadata=(JObject)w.Metadata.DeepClone();originalMetadata["appearanceConfig"]=new JObject{{"clouds",.3}};
            Check(JToken.DeepEquals(originalMetadata,restored.Metadata),"Lazy metadata round trip preserves every unknown header field");
            Check(restored.Buffers.Count==w.Buffers.Count,"All binary buffer descriptors survive typed loading");
            foreach(var pair in w.Buffers)Check(Convert.ToBase64String(pair.Value)==Convert.ToBase64String(restored.Buffers[pair.Key]),"Binary buffer remains byte-identical: "+pair.Key);
            string validChecksum=File.ReadAllText(saved+".sha256");File.WriteAllText(saved+".sha256",new string('0',64));bool corruptRejected=false;try{PlanetWorld.Load(saved);}catch(InvalidDataException){corruptRejected=true;}finally{File.WriteAllText(saved+".sha256",validChecksum);}Check(corruptRejected,"Corrupt package checksum rejected");
            var retained=new TerrainKernel(w);var lease=retained.Retain();double beforeRelease=retained.Sample(w.Center).height;retained.Dispose();Check(retained.Sample(w.Center).height==beforeRelease,"Worker lease survives world replacement");bool lateLeaseRejected=false;try{retained.Retain();}catch(ObjectDisposedException){lateLeaseRejected=true;}Check(lateLeaseRejected,"Closed world rejects new jobs");lease.Dispose();lease.Dispose();retained.Dispose();bool released=false;try{retained.Sample(w.Center);}catch(ObjectDisposedException){released=true;}Check(released,"Last worker releases the sampler; dispose is idempotent");
            File.WriteAllText(Path.Combine(root,"evidence/planet-port/physics-results.json"),JsonConvert.SerializeObject(new{passed=true,checks,cruiseKmh=FlightModel.Cruise*3.6,boostKmh=FlightModel.Boost*3.6,warpDistances=FlightModel.WarpDistances,roundtrip=Path.GetRelativePath(root,saved).Replace('\\','/')},Formatting.Indented));Console.WriteLine($"Flight checks passed: {checks}");
        }
        using(var referenceKernel=new TerrainKernel(w)){
            var expected=JObject.Parse(File.ReadAllText(Path.Combine(root,"evidence/planet-port/reference-fixtures.json")));var actual=ReferenceAnalysis.Run(referenceKernel,32);double maxHeightDifference=0,maxFlowDifference=0;int edgeDifferences=0;
            for(int i=0;i<actual.heights.Length;i++){maxHeightDifference=Math.Max(maxHeightDifference,Math.Abs(actual.heights[i]-(double)expected["heights"][i]));maxFlowDifference=Math.Max(maxFlowDifference,Math.Abs(actual.accumulation[i]-(double)expected["accumulation"][i]));if(actual.receiver[i]!=(int)expected["receiver"][i]||actual.basin[i]!=(int)expected["basin"][i])edgeDifferences++;}
            if(maxHeightDifference>.001||maxFlowDifference>.001||edgeDifferences!=0||actual.downhillErrors!=0||actual.massError>1e-10)throw new Exception("Reference diagnostic drainage differs from the web tool");
            File.WriteAllText(Path.Combine(root,"evidence/planet-port/reference-results.json"),JsonConvert.SerializeObject(new{passed=true,nodes=actual.heights.Length,maxHeightDifference,maxFlowDifference,edgeDifferences,actual.downhillErrors,actual.massError},Formatting.Indented));Console.WriteLine("Reference drainage parity passed: "+actual.heights.Length+" nodes.");
        }
        var oceanTests=JObject.Parse(File.ReadAllText(Path.Combine(root,"evidence/planet-port/ocean-fixtures.json")));int oceanChecks=0;double maxSpectrum=0,maxWave=0;
        foreach(var test in oceanTests["cases"]){var ocean=new OceanSpectrum((uint)test["seed"]);foreach(var s in test["spectrum"]){int i=(int)s["i"];for(int j=0;j<4;j++)maxSpectrum=Math.Max(maxSpectrum,Math.Abs(ocean.packed[i*4+j]-(double)s["p"][j]));maxSpectrum=Math.Max(maxSpectrum,Math.Abs(ocean.frequency[i]-(double)s["frequency"]));}var heights=new float[4096];double clock=-1;foreach(var s in test["samples"]){double time=(double)s["time"];if(clock!=time){ocean.Evaluate(time,heights);clock=time;}double error=Math.Abs(heights[(int)s["i"]]-(double)s["height"]);maxWave=Math.Max(maxWave,error);oceanChecks++;}}
        if(maxSpectrum>1e-5||maxWave>1e-5)throw new Exception($"Ocean parity failed: spectrum {maxSpectrum:R}, height {maxWave:R}");
        File.WriteAllText(Path.Combine(root,"evidence/planet-port/ocean-results.json"),JsonConvert.SerializeObject(new{passed=true,checks=oceanChecks,maxSpectrumError=maxSpectrum,maxWaveErrorMeters=maxWave},Formatting.Indented));Console.WriteLine($"Ocean DFT checks passed: {oceanChecks}, max wave error {maxWave:R} m");
        return failed==0?0:1;
    }
}
