using System;
using System.Collections.Generic;
using System.Numerics;

namespace HuntHelper.Managers.Automation;

public sealed record DRPreviewPoint(Vector3 Position, bool Covered, bool Passed = false);

/// <summary>A detached, read-only view of one active DR scan; it never records discoveries.</summary>
public sealed record DRPatrolPreview
{
    public uint World { get; init; }
    public uint Territory { get; init; }
    public uint Map { get; init; }
    public uint Instance { get; init; }
    public string WorldName { get; init; } = string.Empty;
    public string MapName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string PathStatus { get; init; } = string.Empty;
    public float MapScale { get; init; }
    public PatrolState State { get; init; }
    public Vector3 Player { get; init; }
    public Vector3? Target { get; init; }
    public IReadOnlyList<DRPreviewPoint> Points { get; init; } = Array.Empty<DRPreviewPoint>();
    public IReadOnlyList<Vector3> Path { get; init; } = Array.Empty<Vector3>();
    public IReadOnlyList<Vector3> Monsters { get; init; } = Array.Empty<Vector3>();
    public int FoundA { get; init; }
}
