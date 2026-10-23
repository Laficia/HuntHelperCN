using Dalamud.Plugin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;

namespace HuntHelper.Utilities;

public static class LegacyDataMigration
{
    public static Configuration? TryLoadConfiguration(IDalamudPluginInterface pluginInterface)
    {
        if (pluginInterface.ConfigFile.Exists) return null;
        var parent = pluginInterface.ConfigFile.Directory;
        if (parent == null) return null;
        var oldPath = Path.Combine(parent.FullName, "HuntHelper.json");
        try
        {
            return File.Exists(oldPath) ? JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(oldPath),
                new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None }) : null;
        }
        catch (Exception ex) { PluginLog.Warning(ex, "Could not copy original HuntHelper configuration; original preserved"); return null; }
    }

    public static void CopyTrainIfMissing(string destinationPath, string currentAssemblyDirectory)
    {
        if (File.Exists(destinationPath)) return;
        var candidates = new System.Collections.Generic.List<string>
        {
            Path.Combine(currentAssemblyDirectory, "Data", "HuntTrain.json")
        };
        try
        {
            var configParent = Directory.GetParent(Path.GetDirectoryName(destinationPath)!);
            if (configParent != null) candidates.Add(Path.Combine(configParent.FullName, "HuntHelper", "HuntTrain.json"));
            var installedRoot = Directory.GetParent(currentAssemblyDirectory)?.Parent;
            if (installedRoot?.Name.Equals("installedPlugins", StringComparison.OrdinalIgnoreCase) == true)
            {
                var original = Path.Combine(installedRoot.FullName, "HuntHelper");
                if (Directory.Exists(original))
                    candidates.AddRange(Directory.EnumerateDirectories(original)
                        .OrderByDescending(p => Version.TryParse(Path.GetFileName(p), out var version) ? version : new Version())
                        .Select(p => Path.Combine(p, "Data", "HuntTrain.json")));
            }
            foreach (var candidate in candidates.Where(File.Exists))
            {
                try
                {
                    _ = JArray.Parse(File.ReadAllText(candidate));
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                    File.Copy(candidate, destinationPath, false);
                    return;
                }
                catch (Exception ex) { PluginLog.Warning(ex, "Could not copy legacy train data; original preserved"); }
            }
        }
        catch (Exception ex) { PluginLog.Warning(ex, "Could not locate legacy train data; original preserved"); }
    }
}
