namespace HuntHelper.Utilities;

public readonly record struct TrainVersionGroup(int? Version, string Label, int Count, int? Expected = null)
{
    public string DisplayLabel => Expected is { } expected ? $"{Label} ({Count}/{expected})" : $"{Label} ({Count})";
}
