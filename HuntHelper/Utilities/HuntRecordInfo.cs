using HuntHelper.Managers.Hunts.Models;
using System;

namespace HuntHelper.Utilities;

public static class HuntRecordInfo
{
    public static string Describe(HuntTrainMob mob)
    {
        var source = mob.Source switch { HuntRecordSource.Scan => "现场扫描", HuntRecordSource.Sonar => "Sonar 情报",
            HuntRecordSource.Import => "分享码导入", _ => "历史记录（来源未知）" };
        return $"来源：{source}\n首次录入：{Time(mob.RecordedAtUtc)}\n最近现场发现：{Time(mob.LastObservedUtc, "尚无确认时间")}\n" +
            $"Sonar 击杀接收：{Time(mob.SonarKilledUtc)}\nSonar 接收：{Time(mob.SonarReceivedUtc)}";
    }

    private static string Time(DateTime? time, string missing = "无/未知") =>
        time is { } value && value != default ? value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : missing;
}
