using HuntHelper.Managers.Hunts.Models;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelper.Managers.Automation;

public readonly record struct HuntObservation(uint NameId, Vector3 Position, uint Hp, uint MaxHp);

public sealed class HuntDeathMonitor
{
    private sealed class Track
    {
        public bool Seen, Damaged, AutomaticDeath, SuppressCorpse;
        public long MissingSince = -1;
    }
    private readonly Dictionary<HuntTrainMob, Track> _tracks = new();
    private (uint World, uint Territory, uint Map, uint Instance)? _stay;
    public void ManualChange(HuntTrainMob mob) => _tracks[mob] = new Track { SuppressCorpse = !mob.Dead };
    public void Reset() { _tracks.Clear(); _stay = null; }

    public bool Update(PatrolContext context, IReadOnlyList<HuntTrainMob> records, IReadOnlyList<HuntObservation> seen, long now)
    {
        if (!context.LoggedIn) { Reset(); return false; }
        if (!context.Ready)
        {
            foreach (var track in _tracks.Values) { track.Seen = false; track.Damaged = false; track.MissingSince = -1; }
            _stay = null;
            return false;
        }
        var stay = (context.World, context.Territory, context.Map, context.Instance);
        if (_stay != stay)
        {
            foreach (var track in _tracks.Values) { track.Seen = false; track.Damaged = false; track.MissingSince = -1; }
            _stay = stay;
        }
        var existing = records.ToHashSet();
        foreach (var removed in _tracks.Keys.Where(m => !existing.Contains(m)).ToList()) _tracks.Remove(removed);
        var candidates = seen.GroupBy(m => m.NameId).ToDictionary(g => g.Key, g => g.ToList());
        var changed = false;
        foreach (var mob in records)
        {
            if (mob.WorldID != context.World || mob.WorldID == 0 || mob.TerritoryID != context.Territory ||
                mob.MapID != context.Map || mob.Instance != context.Instance) continue;
            if (!_tracks.TryGetValue(mob, out var track)) _tracks[mob] = track = new();
            HuntObservation? match = null;
            if (candidates.TryGetValue(mob.MobID, out var found))
            {
                var nearby = found.Where(o => !mob.WorldPosition.HasValue || Vector3.DistanceSquared(o.Position, mob.WorldPosition.Value) < 200 * 200);
                if (nearby.Any()) match = nearby.MinBy(o => Vector3.DistanceSquared(o.Position, mob.WorldPosition ?? context.Position));
            }
            if (match is { } observation)
            {
                track.Seen = true; track.MissingSince = -1;
                if (observation.MaxHp > 0 && (double)observation.Hp / observation.MaxHp < .8) track.Damaged = true;
                if (observation.Hp > 0)
                {
                    track.SuppressCorpse = false;
                    if (track.AutomaticDeath && mob.Dead) { mob.Dead = false; track.AutomaticDeath = false; changed = true; }
                }
                else if (!track.SuppressCorpse && !mob.Dead)
                { mob.Dead = true; track.AutomaticDeath = true; changed = true; }
            }
            else if (track.Seen && track.Damaged && !track.SuppressCorpse && !context.Dead &&
                mob.WorldPosition is { } position && Vector3.DistanceSquared(context.Position, position) <= 80 * 80)
            {
                if (track.MissingSince < 0) track.MissingSince = now;
                else if (now - track.MissingSince >= 2000 && !mob.Dead)
                { mob.Dead = true; track.AutomaticDeath = true; changed = true; }
            }
            else track.MissingSince = -1;
        }
        return changed;
    }
}
