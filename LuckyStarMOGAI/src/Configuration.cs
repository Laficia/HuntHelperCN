using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Linq;
namespace LuckyStar;
[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version {get;set;} = 1;
    public int SelectedFarm = 0;
    public bool[][] Selections = [[true,true],[true,true,true],[true,true,true],[true,true,true],[true,true],[true,true,true],[true]];
    public bool DelayEnable;
    public bool[] RubyCaves = [true,true,true];
    public int DelayTime = 10;
    public RotationProvider Rotation = RotationProvider.AE;
    public bool AllowRotationStart = true;
    public bool MountBetweenTargets = true;
    public bool FlyBetweenTargets = true;
    [NonSerialized] private IDalamudPluginInterface? pluginInterface;
    public void Initialize(IDalamudPluginInterface pi)
    {
        pluginInterface=pi;
        if(Version<2)
        {
            // v1 defaulted to manual activation, which interrupted the one-click farm flow.
            AllowRotationStart=true;
            Version=2;
            Save();
        }
        SelectedFarm=Math.Clamp(SelectedFarm,0,FarmProfiles.Titles.Length-1);
        if(RubyCaves == null || RubyCaves.Length != 3) RubyCaves = [true,true,true];
        if(Selections == null || Selections.Length != FarmProfiles.Titles.Length) Selections = new bool[FarmProfiles.Titles.Length][];
        for(var i=0;i<Selections.Length;i++) if(Selections[i] == null || Selections[i].Length != FarmProfiles.NameIds[i].Length)
            Selections[i]=System.Linq.Enumerable.Repeat(true,FarmProfiles.NameIds[i].Length).ToArray();
        DelayTime=Math.Clamp(DelayTime,0,120);
        if(!Enum.IsDefined(Rotation)) Rotation=RotationProvider.AE;
    }
    public void Save()=>pluginInterface!.SavePluginConfig(this);
}

