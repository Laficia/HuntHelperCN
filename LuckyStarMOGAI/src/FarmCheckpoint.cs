using System;
using System.Collections.Generic;
using System.Linq;

namespace LuckyStar;

/// <summary>A short-lived, scope-checked handoff for an explicitly requested developer reload.</summary>
public sealed record FarmCheckpoint(FarmScope Scope,int FarmIndex,uint[] Names,Dictionary<uint,int> Deaths,DateTimeOffset CapturedAt)
{
    public bool Matches(FarmScope scope,int farmIndex,uint[] names,DateTimeOffset now)=>
        Scope==scope && FarmIndex==farmIndex && Names!=null && Names.SequenceEqual(names) &&
        Deaths!=null && Deaths.Count<=16 && Deaths.All(p=>names.Contains(p.Key) && p.Value is >=0 and <=100000) &&
        now-CapturedAt>=TimeSpan.FromSeconds(-10) && now-CapturedAt<=TimeSpan.FromMinutes(10);
}
