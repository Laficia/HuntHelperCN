using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using ECommons;
using ECommons.DalamudServices;

namespace LuckyStar;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/luckystar";
    private const string LegacyCommandName = "/luckystarmogai";
    private readonly System.Collections.Generic.List<string> registeredCommands = [];
    public static Configuration Configuration = null!;
    public readonly WindowSystem WindowSystem = new("LuckyStarMOGAI");
    private MainWindow MainWindow { get; init; }
    private readonly ICallGateProvider<string> status;
    private readonly ICallGateProvider<string,bool> resume;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        ECommonsMain.Init(pluginInterface, this);

        Configuration = Svc.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(Svc.PluginInterface);

        MainWindow = new MainWindow();
        status=Svc.PluginInterface.GetIpcProvider<string>("LuckyStarMOGAI.GetStatus");
        status.RegisterFunc(MainWindow.StatusJson);
        resume=Svc.PluginInterface.GetIpcProvider<string,bool>("LuckyStarMOGAI.ResumeFromCheckpoint");
        resume.RegisterFunc(MainWindow.ResumeFromCheckpoint);
        WindowSystem.AddWindow(MainWindow);

        RegisterCommand(CommandName);
        RegisterCommand(LegacyCommandName);

        Svc.PluginInterface.UiBuilder.Draw += DrawUI;
        Svc.PluginInterface.UiBuilder.OpenConfigUi += ToggleMainUI;
        Svc.PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;
    }

    public void Dispose()
    {
        Svc.PluginInterface.UiBuilder.Draw -= DrawUI;
        Svc.PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainUI;
        Svc.PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUI;
        WindowSystem.RemoveAllWindows();
        status.UnregisterFunc();
        resume.UnregisterFunc();
        MainWindow.Dispose();
        foreach(var command in registeredCommands)Svc.Commands.RemoveHandler(command);
        registeredCommands.Clear();
        ECommonsMain.Dispose();
    }

    private void RegisterCommand(string command)
    {
        if(Svc.Commands.AddHandler(command,new CommandInfo(OnCommand)
        {HelpMessage="打开设置；start 开始农怪；stop 停止；status 查看运行状态"}))
            registeredCommands.Add(command);
        else Svc.Log.Warning($"LuckyStarMOGAI：{command} 已被其他插件占用；请停用原版 LuckyStar 后重载本插件。");
    }

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("stop", System.StringComparison.OrdinalIgnoreCase)) MainWindow.Stop();
        else if(args.Trim().Equals("start",System.StringComparison.OrdinalIgnoreCase))MainWindow.StartSelected();
        else if(args.Trim().Equals("status",System.StringComparison.OrdinalIgnoreCase))Svc.Chat.Print("[LuckyStarMOGAI] "+MainWindow.StatusJson());
        else ToggleMainUI();
    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleMainUI() => MainWindow.Toggle();
}



