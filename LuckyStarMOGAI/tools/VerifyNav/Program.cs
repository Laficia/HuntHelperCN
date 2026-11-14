using System.Reflection;using System.Numerics;
if(args.Length!=2)throw new ArgumentException("Pass vnavmesh plugin directory and the territory-613 cache file.");
var folder=Path.GetFullPath(args[0]);
AppDomain.CurrentDomain.AssemblyResolve+=(_,e)=>{foreach(var dir in new[]{folder,Environment.ExpandEnvironmentVariables(@"%APPDATA%\XIVLauncherCN\addon\Hooks\dev")}){var p=Path.Combine(dir,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
var common=Assembly.LoadFrom(Path.Combine(folder,"vnavmesh.Common.dll"));var main=Assembly.LoadFrom(Path.Combine(folder,"vnavmesh.dll"));
var f=Path.GetFullPath(args[1]);
using var r=new BinaryReader(File.OpenRead(f));r.ReadUInt32();r.ReadInt32();var custom=r.ReadInt32();var signature=r.ReadString();r.BaseStream.Position=0;
var result=common.GetType("vnavmesh.Common.Build.Navmesh")!.GetMethod("Deserialize")!.Invoke(null,new object[]{r,custom,signature})!;
foreach(var t in main.GetTypes().Where(t=>t.Name=="Service")) foreach(var prop in t.GetProperties(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).Where(p=>p.PropertyType==typeof(Dalamud.Plugin.Services.IPluginLog))) prop.SetValue(null,DispatchProxy.Create<Dalamud.Plugin.Services.IPluginLog,SilentLog>());
var mesh=result.GetType().GetProperty("Navmesh")!.GetValue(result);
var type=main.GetType("vnavmesh.Query.NavmeshQuery")!;var ctor=type.GetConstructors().Single();var config=Activator.CreateInstance(ctor.GetParameters()[1].ParameterType);var q=ctor.Invoke(new[]{mesh,config});
var flags=BindingFlags.Instance|BindingFlags.NonPublic;
var floor=type.GetMethod("FindPointOnFloor",flags)!;
var plan=type.GetMethod("PlanMeshPathDetailed",flags)!;
Console.WriteLine(plan); Console.WriteLine(type.GetMethod("FindNearestMeshPoly",flags)); Console.WriteLine(type.GetMethod("FindReachableMeshPolys",flags));
var sources=new[]{new Vector3(-275.106f,-163.5312f,-291.4856f),new Vector3(-72.93321f,-187.0858f,169.2348f),new Vector3(209.334f,-165.4573f,127.385f)};
var maps=new[]{new Vector2(15,14.5f),new Vector2(21,26),new Vector2(25,25.5f)};
string Describe(object p)=>string.Join("; ",p.GetType().GetProperties().Where(v=>v.Name is "Status" or "FinalDestination" or "RequestedDestination").Select(v=>$"{v.Name}={v.GetValue(p)}"));
var caveType=typeof(LuckyStar.RubyCaves);
var flight=type.GetMethod("PlanVolumePathDetailed",flags)!;
var town=LuckyStar.RubyCaves.Tamamizu;
foreach(var leg in new[]{("town-exit",town.ScanPoint,town.Interior,false),("town-cave-0",town.Exterior,LuckyStar.RubyCaves.All[0].Exterior,true),("town-cave-1",town.Exterior,LuckyStar.RubyCaves.All[1].Exterior,true),("town-cave-2",town.Exterior,LuckyStar.RubyCaves.All[2].Exterior,true)}){
var method=leg.Item4?flight:plan;var callArgs=method.GetParameters().Select(p=>p.Name switch{"from"=>(object)leg.Item2,"to"=>leg.Item3,"range"=>0f,"cancel"=>CancellationToken.None,_=>p.HasDefaultValue?p.DefaultValue:Activator.CreateInstance(p.ParameterType)}).ToArray();
var path=method.Invoke(q,callArgs)!;Console.WriteLine($"LEG {leg.Item1}: {Describe(path)}");if(path.GetType().GetProperty("Status")!.GetValue(path)!.ToString()!="Complete")throw new Exception("INCOMPLETE TOWN NAV PATH");}
for(var i=0;i<3;i++){
var cave=LuckyStar.RubyCaves.All[i];var next=LuckyStar.RubyCaves.All[(i+1)%3];
foreach(var leg in new[]{("scan",cave.Interior,cave.ScanPoint,false),("exit-approach",cave.ScanPoint,cave.Interior,false),("cave-flight",cave.Interior,cave.ScanPoint,true),("cave-return-flight",cave.ScanPoint,cave.Interior,true),("transit",cave.Exterior,next.Exterior,true)}){
var method=leg.Item4?flight:plan;var callArgs=method.GetParameters().Select(p=>p.Name switch{"from"=>(object)leg.Item2,"to"=>leg.Item3,"range"=>0f,"cancel"=>CancellationToken.None,_=>p.HasDefaultValue?p.DefaultValue:Activator.CreateInstance(p.ParameterType)}).ToArray();
var path=method.Invoke(q,callArgs)!;Console.WriteLine($"LEG {i} {leg.Item1}: {Describe(path)}");if(path.GetType().GetProperty("Status")!.GetValue(path)!.ToString()!="Complete")throw new Exception("INCOMPLETE NAV PATH");}}public class SilentLog:DispatchProxy{protected override object? Invoke(MethodInfo? m,object?[]? args)=>m?.ReturnType==typeof(void)?null:m?.ReturnType.IsValueType==true?Activator.CreateInstance(m.ReturnType):null;}




