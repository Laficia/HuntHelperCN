using LuckyStar;

// Minimal persistence boundary for the game-independent regression executable.
namespace Dalamud.Configuration
{
    public interface IPluginConfiguration { int Version { get; set; } }
}
namespace Dalamud.Plugin
{
    public interface IDalamudPluginInterface
    {
        void SavePluginConfig(Dalamud.Configuration.IPluginConfiguration config);
    }
}
internal sealed class ConfigurationStore : Dalamud.Plugin.IDalamudPluginInterface
{
    public int Saves;
    public bool SavedAllowStart;
    public int SavedVersion;
    public void SavePluginConfig(Dalamud.Configuration.IPluginConfiguration config)
    {
        Saves++;
        SavedAllowStart=((Configuration)config).AllowRotationStart;
        SavedVersion=config.Version;
    }
}
