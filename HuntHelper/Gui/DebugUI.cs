using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using HuntHelper.Managers.Hunts;
using HuntHelper.Utilities;
using System;
using System.Numerics;

namespace HuntHelper.Gui
{
    internal class DebugUI
    {
        private readonly IAetheryteList _aeth;
        private readonly IDataManager _dataManager;
        private readonly IClientState _clientState;
        private readonly IObjectTable _objectTable;
        private readonly HuntManager _huntManager;

        private string _territoryName;
        private string _worldName;
        private uint _territoryId;
        private float _mapZoneScale;
        public float MapZoneMaxCoord => MapHelpers.MapScaleToMaxCoord(_mapZoneScale);
        public float SingleCoordSize => ImGui.GetWindowSize().X / MapZoneMaxCoord;

        public bool RandomDebugWindowVisisble = false;
        private uint Instance => MapHelpers.CurrentInstance;

        public DebugUI(IAetheryteList aeth, IDataManager dataManager, IClientState clientState, HuntManager huntManager, IObjectTable objectTable)
        {
            _aeth = aeth;
            _dataManager = dataManager;
            _clientState = clientState;
            _huntManager = huntManager;
            _objectTable = objectTable;

        }

        public unsafe void Draw()
        {
            if (!RandomDebugWindowVisisble)
            {
                return;
            }

            UpdateLocalStuff();

            ImGui.SetNextWindowSize(new Vector2(375, 330), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSizeConstraints(new Vector2(375, 330), new Vector2(float.MaxValue, float.MaxValue));
            if (ImGui.Begin("调试信息###Debug stuff", ref RandomDebugWindowVisisble))
            {

                LocalObjects();
                PositionalInfo();
                Aetherytes();
            }
            ImGui.End();
        }

        private void LocalObjects()
        {
            ImGui.Text($"当前地区： {_territoryName}{Instance}");
            ImGui.Text($"地区编号： {_clientState.TerritoryType}");

            //PLAYER POS
            var v3 = _objectTable.LocalPlayer?.Position ?? new Vector3(0, 0, 0);
            ImGui.Text($"位置 X： ({ConvertPosToCoordinate(v3.X)}, Y: {ConvertPosToCoordinate(v3.X)})\n");
            ImGui.NewLine();

            if (ImGui.CollapsingHeader("附近狩猎怪与对象列表调试"))
            {
                ImGui.BeginGroup();
                var nearby = _huntManager.GetCurrentMobs();
                ImGui.Text("附近怪物");
                nearby.ForEach(m => ImGui.TextUnformatted($"{m.Name} - {m.NameId}"));
                ImGui.Text("============");

                var (rank, mob) = _huntManager.GetPriorityMob();
                ImGui.Text("优先目标：");
                ImGui.TextUnformatted($"{rank} - {mob?.Name}");
                ImGui.Text("============");
                ImGui.Text("");

                var hunt = "";
                foreach (var obj in _objectTable)
                {
                    if (obj is not IBattleNpc bobj) continue;
                    if (bobj.MaxHp < 10000) continue; //not really needed if subkind is enemy, once matching to id / name
                    if (bobj.BattleNpcKind != BattleNpcSubKind.Combatant) continue; //not really needed if matching to 'nameID'


                    hunt += $"{obj.Name} \n" +
                            $"类型： {bobj.BattleNpcKind}\n" +
                            $"名称编号： {bobj.NameId}\n" +
                            $"|生命值： {bobj.CurrentHp}\n" +
                            $"|生命百分比： {bobj.CurrentHp * 1.0 / bobj.MaxHp * 100}%%\n" +
                            //$"|object ID: {obj.ObjectId}\n| Data ID: {obj.DataId} \n| OwnerID: {obj.OwnerId}\n" +
                            $"X: {ConvertPosToCoordinate(obj.Position.X)};\n" +
                            $"Y: {ConvertPosToCoordinate(obj.Position.Y)}\n" +
                            $"  --------------  \n";
                }
                ImGui.Text($"已发现： {hunt}");
                ImGui.NewLine();
                ImGui.EndGroup();
            }

        }
        private void PositionalInfo()
        {
            if (ImGui.CollapsingHeader("位置调试"))
            {
                ImGui.BeginGroup();
                ImGui.TextUnformatted("其他调试信息：");
                ImGui.Columns(2);

                ImGui.TextUnformatted($"内容区域： {ImGui.GetContentRegionAvail()}");
                ImGui.TextUnformatted($"窗口尺寸： {ImGui.GetWindowSize()}");
                ImGui.TextUnformatted($"窗口位置： {ImGui.GetWindowPos()}");
                if (_objectTable?.LocalPlayer?.Position != null)
                {
                    ImGui.TextUnformatted($"朝向： {_objectTable!.LocalPlayer.Rotation}");
                    var rotation = Math.Abs(_objectTable.LocalPlayer.Rotation - Math.PI);
                    ImGui.TextUnformatted($"朝向弧度： {Math.Round(rotation, 2):0.00}");
                    ImGui.TextUnformatted($"朝向正弦： {Math.Round(Math.Sin(rotation), 2):0.00}");
                    ImGui.TextUnformatted($"朝向余弦： {Math.Round(Math.Cos(rotation), 2):0.00}");
                    ImGui.TextUnformatted($"玩家位置： (" +
                                       $"{MapHelpers.ConvertToMapCoordinate(_objectTable.LocalPlayer.Position.X, _mapZoneScale):0.00}" +
                                       $"," +
                                       $" {MapHelpers.ConvertToMapCoordinate(_objectTable.LocalPlayer.Position.Z, _mapZoneScale):0.00}" +
                                       $")");
                    var playerPos = CoordinateToPositionInWindow(
                        new Vector2(ConvertPosToCoordinate(_objectTable.LocalPlayer.Position.X),
                            ConvertPosToCoordinate(_objectTable.LocalPlayer.Position.Z)));
                    ImGui.TextUnformatted($"玩家位置： {playerPos}");
                    ImGui.NextColumn();
                    ImGuiUtil.ImGui_RightAlignText($"地图： {_territoryName}{Instance} - {_territoryId}");
                    ImGuiUtil.ImGui_RightAlignText($"坐标比例： {_mapZoneScale} ");

                    //priority mob stuff
                    ImGuiUtil.ImGui_RightAlignText("优先怪物：");
                    var priorityMob = _huntManager.GetPriorityMob().Mob;
                    if (priorityMob != null)
                    {
                        ImGuiUtil.ImGui_RightAlignText($"等级 {_huntManager.GetPriorityMob().Rank} " +
                                                            $"{priorityMob.Name} " +
                                                            $"{Math.Round(1.0 * priorityMob.CurrentHp / priorityMob.MaxHp * 100, 2):0.00}% | " +
                                                            $"({ConvertPosToCoordinate(priorityMob.Position.X):0.00}, " +
                                                            $"{ConvertPosToCoordinate(priorityMob.Position.Z):0.00}) ");
                    }

                    var currentMobs = _huntManager.GetAllCurrentMobsWithRank();
                    ImGuiUtil.ImGui_RightAlignText("--");
                    ImGuiUtil.ImGui_RightAlignText("其他附近怪物：");
                    foreach (var (rank, mob) in currentMobs)
                    {
                        ImGuiUtil.ImGui_RightAlignText("--");
                        ImGuiUtil.ImGui_RightAlignText($"等级： {rank} | " +
                                                            $"{mob.Name} {Math.Round(1.0 * mob.CurrentHp / mob.MaxHp * 100, 2):0.00}% | " +
                                                            $"({ConvertPosToCoordinate(mob.Position.X):0.00}, " +
                                                            $"{ConvertPosToCoordinate(mob.Position.Z):0.00})");
                    }
                }
                else
                {
                    ImGui.Text("未找到当前玩家");
                }
                ImGui.Columns(1);
                ImGui.NewLine();
                ImGui.EndGroup();
            }
        }

        /// <summary>
        /// sets to clipboard a new aetheryteData object line (used in TeleportManager.cs)
        /// </summary>
        private unsafe void Aetherytes()
        {
            var tele = Telepo.Instance();
            if (ImGui.CollapsingHeader("以太水晶"))
            {
                ImGui.BeginGroup();
                foreach (var a in _aeth)
                {
                    var pn = a.AetheryteData.ValueNullable?.PlaceName.Value;
                    ImGui.PushFont(UiBuilder.MonoFont);
                    ImGui.Text($"{pn?.Name,26} | {"水晶编号：",6}{a.AetheryteId,4} | {"子编号：",6}{a.SubIndex,4} | {"地区：",6}{a.TerritoryId,4}");
                    ImGui.PopFont();

                    if (ImGui.Button($"复制##{a.AetheryteId}"))
                    {
                        ImGui.SetClipboardText($"new AetheryteData() {{ AetheryteID = {a.AetheryteId}, SubIndex = {a.SubIndex}, TerritoryID = {a.TerritoryId}, Position = new Vector2()}}, //{pn?.Name}");
                    }
                    ImGui.Separator();
                }
                ImGui.BeginGroup();
            }
        }




        private unsafe void UpdateLocalStuff()
        {
            _territoryName = MapHelpers.GetMapName(_clientState.TerritoryType);
            _worldName = _objectTable.LocalPlayer?.CurrentWorld.ValueNullable?.Name.ToString() ?? "未找到";
            _territoryId = _clientState.TerritoryType;
            _mapZoneScale = _huntManager.GetMapZoneScale(_territoryId);
        }

        private float ConvertPosToCoordinate(float coord) { return MapHelpers.ConvertToMapCoordinate(coord, _mapZoneScale); }

        private Vector2 CoordinateToPositionInWindow(Vector2 pos)
        {
            var x = (pos.X - 1) * SingleCoordSize + ImGui.GetWindowPos().X;
            var y = (pos.Y - 1) * SingleCoordSize + ImGui.GetWindowPos().Y;
            return new Vector2(x, y);
        }
    }
}
