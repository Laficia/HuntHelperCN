using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using HuntHelper.Utilities;
using System.Numerics;

namespace HuntHelper.Managers.NewExpansion
{
    //todo make new config bool next expansion
    internal class SubmitDataPrompt
    {

        private Configuration _config;
        private bool _visible = false;
        public SubmitDataPrompt(Configuration config)
        {
            _config = config;
            if (!_config.DawntrailAlreadyPrompted) _visible = true;
        }

        public void Draw()
        {
            if (!Constants.NEW_EXPANSION) return;
            if (!_visible) return;

            ImGui.SetNextWindowSize(new Vector2(630 * ImGuiHelpers.GlobalScale, 220 * ImGuiHelpers.GlobalScale));
            if (ImGui.Begin("新版狩猎数据提交"))
            {
                ImGuiUtil.DoStuffWithMonoFont(() =>
                {
                    ImGui.TextWrapped("是否提交已发现的刷新点，协助完善新版狩猎地图？\n此提示来自狩猎助手。");

                    ImGui.NewLine();

                    if (ImGui.Button("参与提交，不再提示", new Vector2(333 * ImGuiHelpers.GlobalScale, 25 * ImGuiHelpers.GlobalScale)))
                    {
                        _config.DawntrailSubmitPositionsData = true;
                        _config.DawntrailAlreadyPrompted = true;
                        _visible = false;
                    }
                    if (ImGui.Button("暂不参与，不再提示", new Vector2(333 * ImGuiHelpers.GlobalScale, 25 * ImGuiHelpers.GlobalScale)))
                    {
                        _config.DawntrailAlreadyPrompted = true;
                        _visible = false;
                    }

                    ImGui.NewLine();
                    ImGui.Text("你可以随时在狩猎地图设置中开启或关闭数据提交。");
                });
            }
            ImGui.End();
        }
    }
}
