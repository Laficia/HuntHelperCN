using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelper.Utilities;

public sealed record HuntMacroEntry(string Id, string Name, string Text, string Tab, bool BuiltIn);

/// <summary>A/S retain their existing fields. Custom pages and collection reference stable IDs.</summary>
public sealed class HuntMacroLibrary(HuntPointMacroSettings settings)
{
    public IReadOnlyList<HuntMacroEntry> Entries => new[]
    {
        new HuntMacroEntry("a", settings.AName, settings.A, "A预设", true),
        new HuntMacroEntry("s", settings.SName, settings.S, "S预设", true)
    }.Concat(settings.Custom.Select(m => new HuntMacroEntry(m.Id, m.Name, m.Text, m.Name, false))).ToArray();
    public IReadOnlyList<string> Collection => settings.Collection;
    public HuntMacroEntry? Find(string id)
    {
        if (id == "a") return new("a", settings.AName, settings.A, "A预设", true);
        if (id == "s") return new("s", settings.SName, settings.S, "S预设", true);
        var macro = settings.Custom.FirstOrDefault(m => m.Id == id);
        return macro == null ? null : new(macro.Id, macro.Name, macro.Text, macro.Name, false);
    }

    public bool Repair()
    {
        var changed = false;
        if (settings.Custom == null) { settings.Custom = new(); changed = true; }
        if (settings.Collection == null) { settings.Collection = new() { "a", "s" }; changed = true; }
        if (settings.A == null) { settings.A = ""; changed = true; }
        if (settings.S == null) { settings.S = ""; changed = true; }
        if (string.IsNullOrWhiteSpace(settings.AName)) { settings.AName = "A 报点"; changed = true; }
        if (string.IsNullOrWhiteSpace(settings.SName)) { settings.SName = "S 报点"; changed = true; }
        var ids = new HashSet<string> { "a", "s" };
        foreach (var macro in settings.Custom.ToArray())
        {
            if (macro == null) { settings.Custom.Remove(macro!); changed = true; continue; }
            if (string.IsNullOrWhiteSpace(macro.Id) || !ids.Add(macro.Id))
            { macro.Id = Guid.NewGuid().ToString("N"); ids.Add(macro.Id); changed = true; }
            if (string.IsNullOrWhiteSpace(macro.Name)) { macro.Name = "自定义宏"; changed = true; }
            if (macro.Text == null) { macro.Text = ""; changed = true; }
        }
        var pinned = settings.Collection.Where(id => id != null && ids.Contains(id)).Distinct().ToList();
        if (!pinned.SequenceEqual(settings.Collection)) { settings.Collection = pinned; changed = true; }
        return changed;
    }

    public HuntMacroEntry Add()
    {
        var number = 1;
        while (Entries.Any(m => m.Name == $"自定义 {number}")) number++;
        var item = new HuntCustomMacro { Id = Guid.NewGuid().ToString("N"), Name = $"自定义 {number}" };
        settings.Custom.Add(item);
        return Find(item.Id)!;
    }

    public bool Save(string id, string name, string text)
    {
        var entry = Find(id); if (entry == null) return false;
        name = string.IsNullOrWhiteSpace(name) ? entry.Name : name.Trim();
        if (entry.Name == name && entry.Text == text) return false;
        if (id == "a") { settings.AName = name; settings.A = text; }
        else if (id == "s") { settings.SName = name; settings.S = text; }
        else { var custom = settings.Custom.Single(m => m.Id == id); custom.Name = name; custom.Text = text; }
        return true;
    }

    public bool Pin(string id, string? target = null, bool after = true)
    {
        if (Find(id) == null || target == id || target != null && !settings.Collection.Contains(target)) return false;
        var order = settings.Collection.Where(m => m != id).ToList();
        var index = target == null ? order.Count : order.IndexOf(target) + (after ? 1 : 0);
        order.Insert(index, id);
        if (order.SequenceEqual(settings.Collection)) return false;
        settings.Collection = order;
        return true;
    }
    public bool Unpin(string id) => settings.Collection.Remove(id);
    public bool Delete(string id)
    {
        if (id is "a" or "s") return false;
        if (settings.Custom.RemoveAll(m => m.Id == id) == 0) return false;
        settings.Collection.RemoveAll(m => m == id);
        return true;
    }
}
