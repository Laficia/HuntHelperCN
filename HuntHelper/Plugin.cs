using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using HuntHelper.Gui;
using HuntHelper.Gui.Resource;
using HuntHelper.Managers;
using HuntHelper.Managers.Automation;
using HuntHelper.Managers.Hunts;
using HuntHelper.Managers.MapData;
using HuntHelper.Managers.NewExpansion;
using HuntHelper.Utilities;
using System;
using System.IO;
using System.Linq;

namespace HuntHelper
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "狩猎助手Hunt Helper";

        private const string MapWindowCommand = "/hh";
        private const string MapWindowPresetOne = "/hh1";
        private const string MapWindowPresetTwo = "/hh2";
        private const string MapWindowPresetOneSave = "/hh1save";
        private const string MapWindowPresetTwoSave = "/hh2save";
        private const string HuntTrainWindowCommand = "/hht";
        private const string SRankWindowCommand = "/hhs";
        private const string NextHuntInTrainCommand = "/hhn";
        private const string NextHuntInTrainAetheryteCommand = "/hhna";
        private const string CounterCommand = "/hhc";
        private const string SpawnPointCommand = "/hhr";
        private const string DebugCommand = "/hhdebug";
        [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
        private ICommandManager CommandManager { get; init; }
        private IDalamudPluginInterface PluginInterface { get; init; }
        private IClientState ClientState { get; init; }
        private IObjectTable ObjectTable { get; init; }
        private long _nextTrainScan;
        private string? _lastTrainScanError;
        private Configuration Configuration { get; init; }
        private MapUI MapUi { get; init; }
        private HuntTrainUI HuntTrainUI { get; init; }
        private HuntTrainUI SRankUI { get; init; }
        private TrainManager SRankManager { get; init; }
        private SonarChatRecorder SonarRecorder { get; init; }
        private HuntAutomationManager Automation { get; init; }
        private HuntFinderUI HuntFinderUI { get; init; }
        private CounterUI CounterUI { get; init; }
        private PointerUI PointerUI { get; init; }
        private SpawnPointFinderUI SpawnPointFinderUI { get; init; }

        private HuntManager HuntManager { get; init; }
        private TrainManager TrainManager { get; init; }
        private MapDataManager MapDataManager { get; init; }
        private IpcSystem IpcSystem { get; init; }
        private NavigationManager NavigationManager { get; init; }

        public static ICallGateSubscriber<uint, byte, bool> TeleportIpc { get; private set; }
        public static string PluginDir { get; set; } = string.Empty;

        private SubmitDataPrompt SubmitDataPrompt { get; set; }
        private DebugUI DebugUI { get; set; }


        public Plugin(
            IDalamudPluginInterface pluginInterface,
            IFramework framework,
            ICommandManager commandManager,
            IClientState clientState,
            IPlayerState playerState,
            ICondition condition,
            IObjectTable objectTable,
            IDataManager dataManager,
            IChatGui chatGui,
            IFlyTextGui flyTextGui,
            IGameGui gameGui,
            IFateTable fateTable,
            IPluginLog pluginLog,
            IAetheryteList aeth)
        {
            this.CommandManager = commandManager;
            PluginInterface = pluginInterface;
            ClientState = clientState;
            ObjectTable = objectTable;

            PluginLog.Logger = pluginLog;

            PluginDir = pluginInterface.AssemblyLocation.Directory?.FullName!;

            if (!GuiResources.LoadGuiText(clientState.ClientLanguage))
            {
                PluginLog.Error("Unable to find localisation file. What did you do?! gonna crash ok");
            }

            MapHelpers.SetUp(dataManager, clientState);

            Constants.SetCounterLanguage(clientState.ClientLanguage);

            Configuration = pluginInterface.GetPluginConfig() as Configuration ?? LegacyDataMigration.TryLoadConfiguration(pluginInterface) ?? new Configuration();
            Configuration.Initialize(pluginInterface);
            if (Configuration.RepairLegacyMapImageVisibility()) Configuration.Save();
            if (!Configuration.AutoMapImagesInitialized)
            {
                Configuration.UseMapImages = true;
                Configuration.HuntTrainShowTeleportButtons = true;
                Configuration.AutoMapImagesInitialized = true;
                Configuration.Save();
            }

            var trainPath = Path.Combine(pluginInterface.ConfigDirectory.FullName, "HuntTrain.json");
            LegacyDataMigration.CopyTrainIfMissing(trainPath, PluginDir);
            TrainManager = new TrainManager(chatGui, gameGui, trainPath, playerState, defaultARankMapOrder: true);
            MapDataManager = new MapDataManager(Path.Combine(pluginInterface.AssemblyLocation.Directory?.FullName!, @"Data\SpawnPointData.json"));
            HuntManager = new HuntManager(pluginInterface, TrainManager, chatGui, flyTextGui, Configuration.TTSVolume,
                MapHelpers.GetHuntTerritories(MapDataManager.SpawnPointsList.Select(m => (uint)m.MapID)));
            SRankManager = new TrainManager(chatGui, gameGui, Path.Combine(pluginInterface.ConfigDirectory.FullName,"SRankTrain.json"),
                playerState, mob => HuntManager.GetSRankVersion(mob.MobID).HasValue);
            SonarRecorder = new SonarChatRecorder(chatGui,clientState,dataManager,HuntManager,TrainManager,SRankManager,Configuration);

            MapUi = new MapUI(this.Configuration, clientState, playerState, objectTable, HuntManager, MapDataManager, gameGui);
            NavigationManager = new NavigationManager(pluginInterface, clientState, playerState, objectTable, dataManager, framework,
                () => condition[ConditionFlag.InFlight]);
            Automation = new HuntAutomationManager(clientState, playerState, objectTable, condition, aeth, dataManager,
                TrainManager, HuntManager, NavigationManager, MapDataManager, Configuration, pluginInterface.ConfigDirectory.FullName, SRankManager);
            HuntFinderUI = new HuntFinderUI(Automation, Configuration);
            HuntTrainUI = new HuntTrainUI(TrainManager, Configuration, NavigationManager, HuntFinderUI.Open, () => Automation.Session.Active,
                () => Automation.Session.Stop(), mob => HuntManager.GetARankVersion(mob.MobID), HuntManager.ARankRoster,
                () => { var c = Automation.Context; return (c.World, c.Territory, c.Instance, c.Ready && c.LoggedIn); },
                relayStatus:()=>SonarRecorder.AStatus,openOther:()=>{if(SRankUI is { } ui) ui.HuntTrainWindowVisible=true;});
            SRankUI = new HuntTrainUI(SRankManager,Configuration,NavigationManager,()=>{},()=>Automation.Session.Active,
                ()=>Automation.Session.Stop(),mob=>HuntManager.GetSRankVersion(mob.MobID),HuntManager.SRankRoster,
                ()=>{var c=Automation.Context;return(c.World,c.Territory,c.Instance,c.Ready&&c.LoggedIn);},"S",
                ()=>SonarRecorder.SStatus,()=>HuntTrainUI.HuntTrainWindowVisible=true);
            CounterUI = new CounterUI(clientState, chatGui, gameGui, Configuration, objectTable, fateTable);
            SpawnPointFinderUI = new SpawnPointFinderUI(MapDataManager, Configuration);
            PointerUI = new PointerUI(HuntManager, Configuration, gameGui);
            SubmitDataPrompt = new SubmitDataPrompt(Configuration);
#if DEBUG
            DebugUI = new DebugUI(aeth, dataManager, clientState, HuntManager, objectTable);
#endif

            IpcSystem = new IpcSystem(pluginInterface, framework, TrainManager, HuntManager);
            TeleportIpc = pluginInterface.GetIpcSubscriber<uint, byte, bool>("Teleport");

            CommandManager.AddHandler(MapWindowCommand, new CommandInfo(HuntMapCommand) { HelpMessage = GuiResources.PluginText["/hh helpmessage"] });
            CommandManager.AddHandler(MapWindowPresetOne, new CommandInfo(ApplyPresetOneCommand) { HelpMessage = GuiResources.PluginText["/hh1 helpmessage"] });
            CommandManager.AddHandler(MapWindowPresetTwo, new CommandInfo(ApplyPresetTwoCommand) { HelpMessage = GuiResources.PluginText["/hh2 helpmessage"] });
            CommandManager.AddHandler(MapWindowPresetOneSave, new CommandInfo(SavePresetOneCommand) { HelpMessage = GuiResources.PluginText["/hh1save helpmessage"] });
            CommandManager.AddHandler(MapWindowPresetTwoSave, new CommandInfo(SavePresetTwoCommand) { HelpMessage = GuiResources.PluginText["/hh2save helpmessage"] });
            CommandManager.AddHandler(HuntTrainWindowCommand, new CommandInfo(HuntTrainCommand) { HelpMessage = GuiResources.PluginText["/hht helpmessage"] });
            CommandManager.AddHandler(SRankWindowCommand,new CommandInfo((_,__)=>SRankUI.HuntTrainWindowVisible=!SRankUI.HuntTrainWindowVisible){HelpMessage="打开独立S怪记录窗口"});
            CommandManager.AddHandler("/hhsn",new CommandInfo((_,__)=>SRankUI.GetNextMobCommand()){HelpMessage="发送当前S怪标签中的下一只坐标到聊天"});
            CommandManager.AddHandler("/hhsna",new CommandInfo((_,__)=>SRankUI.GetNextMobNearestAetheryte()){HelpMessage="发送下一只S怪的最近水晶坐标到聊天"});
            CommandManager.AddHandler(NextHuntInTrainCommand, new CommandInfo(GetNextMobCommand) { HelpMessage = GuiResources.PluginText["/hhn helpmessage"] });
            CommandManager.AddHandler(NextHuntInTrainAetheryteCommand, new CommandInfo(GetNextMobAetheryteCommand) { HelpMessage = GuiResources.PluginText["/hhna helpmessage"] }); //todo replace msg
#if DEBUG
            CommandManager.AddHandler(DebugCommand, new CommandInfo(DebugWindowCommand) { HelpMessage = "显示调试数据" });
#endif
            CommandManager.AddHandler(CounterCommand, new CommandInfo(CounterWindowCommand) { HelpMessage = GuiResources.PluginText["/hhc helpmessage"] });
            CommandManager.AddHandler(SpawnPointCommand, new CommandInfo(SpawnPointWindowCommand) { HelpMessage = GuiResources.PluginText["/hhr helpmessage"] });

            pluginInterface.UiBuilder.Draw += DrawUI;
            pluginInterface.UiBuilder.OpenConfigUi += OpenMapUI;
            pluginInterface.UiBuilder.OpenMainUi += OpenHuntTrainUI;
        }

        public void Dispose()
        {
            PluginInterface.UiBuilder.Draw -= DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi -= OpenMapUI;
            PluginInterface.UiBuilder.OpenMainUi -= OpenHuntTrainUI;
            //save hunttrainui config first
            HuntTrainUI.SaveSettings();
            SRankUI.SaveSettings();
            CounterUI.SaveSettings();
            SpawnPointFinderUI.SaveSettings();

            IpcSystem.Dispose();
            SonarRecorder.Dispose();
            Automation.Dispose();
            NavigationManager.Dispose();
            HuntTrainUI.Dispose();
            SRankUI.Dispose();
            SRankManager.Dispose();
            SpawnPointFinderUI.Dispose();
            CounterUI.Dispose();
            MapUi.Dispose();
#if DEBUG
            CommandManager.RemoveHandler(DebugCommand);
#endif
            CommandManager.RemoveHandler(MapWindowCommand);
            CommandManager.RemoveHandler(MapWindowPresetOne);
            CommandManager.RemoveHandler(MapWindowPresetTwo);
            CommandManager.RemoveHandler(MapWindowPresetOneSave);
            CommandManager.RemoveHandler(MapWindowPresetTwoSave);
            CommandManager.RemoveHandler(HuntTrainWindowCommand);
            CommandManager.RemoveHandler(SRankWindowCommand);
            CommandManager.RemoveHandler("/hhsn");
            CommandManager.RemoveHandler("/hhsna");
            CommandManager.RemoveHandler(NextHuntInTrainCommand);
            CommandManager.RemoveHandler(CounterCommand);
            CommandManager.RemoveHandler(SpawnPointCommand);

            HuntManager.Dispose();
            TrainManager.Dispose();
        }

        private void DebugWindowCommand(string command, string args) => DebugUI.RandomDebugWindowVisisble = !DebugUI.RandomDebugWindowVisisble;
        private void HuntMapCommand(string command, string args)
        {
            MapUi.MapVisible = !MapUi.MapVisible;
            Configuration.MapWindowVisible = MapUi.MapVisible;

            //todo idk delete this or comment out after dawntrail maps done
            if (MapUi.MapVisible) MapUi.CheckMapImageVer();
        }
        private void OpenMapUI()
        {
            MapUi.MapVisible = true;
            Configuration.MapWindowVisible = true;
            MapUi.CheckMapImageVer();
        }

        private void OpenHuntTrainUI() => HuntTrainUI.HuntTrainWindowVisible = true;

        private void ScanHuntTrain()
        {
            var now = Environment.TickCount64;
            if (now < _nextTrainScan) return;
            _nextTrainScan = now + 250;
            if (!ClientState.IsLoggedIn || ClientState.TerritoryType == 0 || ClientState.MapId == 0 || ObjectTable.LocalPlayer == null) return;
            try
            {
                var territory = (uint)ClientState.TerritoryType;
                var scale = HuntManager.GetMapZoneScale(territory);
                var name = MapHelpers.GetMapName(territory);
                var instance = MapHelpers.CurrentInstance;
                foreach (var obj in ObjectTable)
                    if (obj is IBattleNpc mob && HuntManager.IsHunt(mob.NameId))
                    {
                        HuntManager.AddToTrain(mob, territory, ClientState.MapId, instance, name, scale);
                        if(mob.CurrentHp>0 && HuntManager.GetSRankVersion(mob.NameId).HasValue)
                            SRankManager.ObserveMob(mob,true,territory,ClientState.MapId,instance,name,scale);
                    }
                _lastTrainScanError = null;
            }
            catch (Exception ex)
            {
                if (_lastTrainScanError != ex.Message) PluginLog.Warning(ex, "Could not scan nearby hunts for train recording");
                _lastTrainScanError = ex.Message;
            }
        }

        private void ApplyPresetOneCommand(string _, string __) => MapUi.ApplyPreset(1);
        private void ApplyPresetTwoCommand(string _, string __) => MapUi.ApplyPreset(2);
        private void SavePresetOneCommand(string _, string __) => MapUi.SavePresetByCommand(1);
        private void SavePresetTwoCommand(string _, string __) => MapUi.SavePresetByCommand(2);
        private void HuntTrainCommand(string _, string __) => HuntTrainUI.HuntTrainWindowVisible = !HuntTrainUI.HuntTrainWindowVisible;
        //gets next available hunt in the recorded train
        private void GetNextMobCommand(string _, string __) => HuntTrainUI.GetNextMobCommand();
        private void GetNextMobAetheryteCommand(string _, string __) => HuntTrainUI.GetNextMobNearestAetheryte();
        private void CounterWindowCommand(string _, string __) => CounterUI.WindowVisible = !CounterUI.WindowVisible;
        private void SpawnPointWindowCommand(string _, string __) => SpawnPointFinderUI.WindowVisible = !SpawnPointFinderUI.WindowVisible;

        private void DrawUI()
        {
            // Recording runs even when the map is closed, collapsed, or fails to draw.
            // Keep train mutations on the same UI tick as row/menu operations.
            ScanHuntTrain();
            Automation.Tick();
            try
            {
                MapUi.Draw();
                HuntTrainUI.Draw();
                SRankUI.Draw();
                HuntFinderUI.Draw();
                CounterUI.Draw();
                SpawnPointFinderUI.Draw();
                PointerUI.Draw();
#if DEBUG
                DebugUI.Draw();
#endif

                //uhh keeping this around for future expansions
                if (Constants.NEW_EXPANSION) SubmitDataPrompt.Draw();
            }
            catch (Exception e)
            {
                PluginLog.Error(e.Message);
                if (e.StackTrace != null) PluginLog.Error(e.StackTrace);
                MapUi.MapVisible = false;
                Configuration.MapWindowVisible = false;
                HuntTrainUI.HuntTrainWindowVisible = false;
                CounterUI.WindowVisible = false;
                SpawnPointFinderUI.WindowVisible = false;
            }
        }

    }
}
