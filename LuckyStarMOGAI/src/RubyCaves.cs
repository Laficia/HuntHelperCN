// Coordinates: local CN territory 613 planmap.lgb, LVD_WaterRange_01.
// ExitRange/PopRange IDs identify normal game cave loading transitions, never coordinate teleportation.
using System.Numerics;
namespace LuckyStar;
public sealed record RubyCave(string Name, Vector2 Map, Vector3 Interior, Vector3 Exterior, Vector3 ScanPoint,
    Vector3 EnterTrigger, Vector3 ExitTrigger, uint EnterId, uint ExitId);
public static class RubyCaves
{
    // Native Tamamizu west exit: 6903733 -> outside PopRange 6903734.
    public static readonly RubyCave Tamamizu = new("玉水村",new(28.5f,16),
        new(337.0081f,-120.3756f,-296.0493f),new(308.9224f,-103.0847f,-331.1247f),new(359.1639f,-119.1982f,-272.394f),
        new(322.373f,-117.657f,-313.739f),new(324.685f,-116.836f,-310.965f),6903732,6903733);
    public static bool InTamamizu(Vector3 position)=>Vector3.Distance(position,Tamamizu.ScanPoint)<95 &&
        position.Y is > -135 and < -105;
    public static readonly RubyCave[] All =
    [
        new("采贝洞",new(15,14.5f),new(-275.106f,-163.5312f,-291.4856f),new(-244.1555f,-146.1256f,-263.9228f),new(-316.75f,-167.75f,-342.25f),
            new(-257.3593f,-160.0303f,-276.6963f),new(-260.0331f,-160.342f,-278.6002f),6903392,6903393),
        new("石牡丹岩窟",new(21,26),new(-72.93321f,-187.0858f,169.2348f),new(-99.53331f,-171.6407f,153.1095f),new(-36.5f,-193f,218.5f),
            new(-89.38416f,-182.9717f,158.5168f),new(-86.74059f,-183.2422f,160.0189f),6903380,6903381),
        new("镜仙洞",new(25,25.5f),new(209.334f,-165.4573f,127.385f),new(190.0265f,-150.6815f,108.3638f),new(166.75f,-171.75f,188.25f),
            new(198.923f,-161.5932f,115.7771f),new(201.0679f,-161.8877f,118.2227f),6890619,6890628)
    ];
    public static Vector3 CrossTrigger(RubyCave cave, bool enter)
    {
        var toward = enter ? cave.Interior - cave.Exterior : cave.Exterior - cave.Interior;
        toward.Y = 0; toward = Vector3.Normalize(toward);
        return (enter ? cave.EnterTrigger : cave.ExitTrigger) + toward * 2;
    }
}
