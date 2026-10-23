namespace HuntHelper.Managers.Hunts.Models;

/// <summary>A world tab and the number of records it currently contains.</summary>
public sealed record HuntTrainWorld(uint WorldID, string WorldName, string DataCenterName, int Count)
{
    public static string GetDisplayName(uint worldID, string name) =>
        worldID == 0 ? "未标注区服" : string.IsNullOrWhiteSpace(name) ? $"区服 {worldID}" : name;
}
