using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
namespace LuckyStar;
public static class FarmProfiles
{
    public static readonly string[] Titles = ["红玉海 · 巨大鳐", "萨维奈岛 · 颇胝迦", "叹息海 · 沉思之物", "拉凯提卡大森林 · 伊休妲", "基拉巴尼亚边区 · 优昙婆罗花", "魔大陆阿济兹拉 · 卢克洛塔", "北萨纳兰 · 蚓螈巨虫"];
    public static readonly uint[] Territories = [613, 957, 959, 817, 612, 402, 147];
    public static readonly uint[][] NameIds = [[5750,5751],[10701,10697,10698],[10463,10461,10462],[8598,8599,8789],[5671,5685],[3540,3580,3556],[113]];
    public static readonly string[][] Labels = [["观梦螺","无壳观梦螺"],["阿输陀花","毕舍遮","金刚尾"],["思考之物","彷徨之物","叹息之物"],["人偶","石蒺藜","器皿"],["莱西","狄亚卡"],["奇美拉","海德拉","薇薇尔飞龙"],["土元精"]];
    // Community hunt requirements: https://ff14.huijiwiki.com/wiki/怪物狩猎
    public static int KillGoal(int index) => index==5 ? 50 : 100;
    public static FarmProfile Profile(int index, bool[] selected) => new(Titles[index], Territories[index], NameIds[index].Where((_, i) => selected[i]).ToArray(), index == 0, index == 0 ? 5984u : 0)
    {
        Goals=NameIds[index].Where((_,i)=>selected[i]).ToDictionary(n=>n,_=>KillGoal(index)),
        PointNames=PresetTypes(index,selected)
    };
    public static List<Vector3> Preset(int index, bool[] selected)
    {
        return Groups(index).Where((_,i)=>selected[i]).SelectMany(g=>g).Select(p=>new Vector3(p.X,p.Y,p.Z)).ToList();
    }
    public static Dictionary<Vector3,uint> PresetTypes(int index,bool[] selected)=>Groups(index)
        .SelectMany((group,i)=>selected[i] ? group.Select(p=>(Point:new Vector3(p.X,p.Y,p.Z),Name:NameIds[index][i])) : [])
        .GroupBy(p=>p.Point).ToDictionary(g=>g.Key,g=>g.First().Name);
    private static List<(float X,float Y,float Z)>[] Groups(int index)=>index switch
        {
            1 => [MobsData.PoZhiJia.阿输陀花,MobsData.PoZhiJia.毕舍遮,MobsData.PoZhiJia.金刚尾],
            2 => [MobsData.TanXiZhiWu.思考之物,MobsData.TanXiZhiWu.彷徨之物,MobsData.TanXiZhiWu.叹息之物],
            3 => [MobsData.YiXiuDa.人偶,MobsData.YiXiuDa.石蒺藜,MobsData.YiXiuDa.器皿],
            4 => [MobsData.优昙婆罗花.莱西,MobsData.优昙婆罗花.狄亚卡],
            5 => [MobsData.卢克洛塔.奇美拉,MobsData.卢克洛塔.海德拉,MobsData.卢克洛塔.薇薇尔飞龙],
            6 => [MobsData.蚓螈巨虫], _ => []
        };
    public static string EorzeaDate(long unix)
    {
        var seconds = unix * 144 / 7;
        var day = (seconds / 86400) % 32 + 1;
        return $"ET 第 {day} 日 {seconds / 3600 % 24:00}:{seconds / 60 % 60:00}";
    }
}

