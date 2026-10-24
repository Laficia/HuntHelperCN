using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using HuntHelper.Gui;
using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Utilities;

// Optional offscreen preview. Uses production widgets with explicitly simulated records.
// Never loads the game, player data, or a graphics device.
internal static unsafe class RosterPreviewRenderer
{
    private const int Width = 800, Height = 820;

    internal static void Render(bool showMissing)
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new(Width, Height);
        io.AddMousePosEvent(-100, -100);
        io.Fonts.Clear();
        ImGui.GetStyle().AntiAliasedLinesUseTex = false;
        io.Fonts.AddFontFromFileTTF("C:/Windows/Fonts/msyh.ttc", 18, default,
            io.Fonts.GetGlyphRangesChineseFull());
        io.Fonts.Build();
        for (var frame = 0; frame < 2; frame++) Draw(showMissing);
        byte* atlas;
        int tw, th;
        io.Fonts.GetTexDataAsRGBA32(0, &atlas, &tw, &th);
        var pixels = new byte[Width * Height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        { pixels[i] = 11; pixels[i + 1] = 17; pixels[i + 2] = 27; pixels[i + 3] = 255; }
        var data = ImGui.GetDrawData();
        for (var n = 0; n < data.CmdListsCount; n++)
        {
            var list = new ImDrawListPtr(data.CmdLists[n]);
            for (var c = 0; c < list.CmdBuffer.Size; c++)
            {
                var cmd = list.CmdBuffer.Data[c];
                if (cmd.UserCallback != null) continue;
                for (var i = 0u; i < cmd.ElemCount; i += 3)
                {
                    var a = list.VtxBuffer.Data[list.IdxBuffer.Data[cmd.IdxOffset + i] + cmd.VtxOffset];
                    var b = list.VtxBuffer.Data[list.IdxBuffer.Data[cmd.IdxOffset + i + 1] + cmd.VtxOffset];
                    var d = list.VtxBuffer.Data[list.IdxBuffer.Data[cmd.IdxOffset + i + 2] + cmd.VtxOffset];
                    Triangle(pixels, a, b, d, cmd.ClipRect, atlas, tw, th);
                }
            }
        }
        var path = Path.GetFullPath($"obj/release-staging/v1.0.0.6/roster-preview-{(showMissing ? "enabled" : "default")}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SavePng(path, pixels);
        Console.WriteLine($"PREVIEW (simulated records): {path}");
    }

    private static void Draw(bool showMissing)
    {
        ImGui.NewFrame();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(18, 14));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 8));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(.055f, .07f, .1f, .97f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(.1f, .17f, .26f, 1));
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, new Vector4(.11f, .16f, .23f, 1));
        ImGui.PushStyleColor(ImGuiCol.TableRowBg, new Vector4(.08f, .105f, .15f, .65f));
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, new Vector4(.105f, .135f, .18f, .65f));
        ImGui.SetNextWindowPos(new(16, 16));
        ImGui.SetNextWindowSize(new(768, 788));
        ImGui.Begin("狩猎车  /  Hunt Helper", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings);
        ImGui.Button("找怪 / 路线录制");
        ImGui.SameLine(); ImGui.TextDisabled("界面预览 · 模拟数据");
        ImGui.TextUnformatted("当前区服：银泪湖");
        var worlds = new WorldTabBar();
        worlds.Draw([new HuntTrainWorld(1, "银泪湖", "", 5), new HuntTrainWorld(2, "水晶塔", "", 12)], 1, new List<uint> { 1, 2 });
        var groups = new List<TrainVersionGroup> { new(7, "7A", 5, 12), new(4, "4A", 2, 12), new(null, "全部", 7) };
        if (showMissing) { groups.Insert(1,new(6,"6A",0,12));groups.Insert(2,new(5,"5A",0,12)); }
        new TrainVersionTabBar().Draw(groups, 7);
        HuntRosterPresentation.Summary("银泪湖", 7, 5, 14, 1);
        ImGui.TextDisabled("按地图分线计数 · 怪物种类 5/12");
        string[] names = ["女王蜂", "奥迦斯", "弗克施泰因", "悼骨灾蝎", "马赫沙", "泛光晶体", "鬼灵梦", "船幽灵", "象魔修罗", "安迦达", "硕蝎", "基里麦卡拉"];
        if (ImGui.BeginTable("PreviewRoster", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY, new(0, 380)))
        {
            ImGui.TableSetupColumn("怪物", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("状态", ImGuiTableColumnFlags.WidthFixed, 94);
            ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, 154);
            ImGui.TableSetupScrollFreeze(0, 1); ImGui.TableHeadersRow();
            for (var i = 0; i < (showMissing ? names.Length + 2 : 5); i++)
            {
                ImGui.PushID(i);
                ImGui.TableNextRow(ImGuiTableRowFlags.None, 31);
                ImGui.TableNextColumn();
                var name = names[i % names.Length] + (i is < 2 ? " 1线" : i >= 12 ? " 2线" : "");
                if (i < 5) ImGui.TextUnformatted(name); else ImGui.TextDisabled(name);
                ImGui.TableNextColumn(); HuntRosterPresentation.Status(i < 5, i == 4, 1);
                ImGui.TableNextColumn();
                if (i < 5) { ImGui.SmallButton("传送"); ImGui.SameLine(); ImGui.SmallButton("移动"); }
                else ImGui.TextDisabled("--");
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        ImGui.Spacing(); ImGui.Checkbox("显示未发现 A 怪占位（默认关闭）", ref showMissing);
        ImGui.TextDisabled("地图分线设置：自动识别 / 无分线 / 2～9 线");
        ImGui.Separator();
        ImGui.Button("开始记录"); ImGui.SameLine(); ImGui.Button("导出"); ImGui.SameLine(); ImGui.Button("导入");
        ImGui.End();
        ImGui.PopStyleColor(5); ImGui.PopStyleVar(3);
        ImGui.Render();
    }

    private static void Triangle(byte[] pixels, ImDrawVert a, ImDrawVert b, ImDrawVert c, Vector4 clip, byte* tex, int tw, int th)
    {
        static float Edge(Vector2 x, Vector2 y, Vector2 p) => (p.X - x.X) * (y.Y - x.Y) - (p.Y - x.Y) * (y.X - x.X);
        var area = Edge(a.Pos, b.Pos, c.Pos);
        if (MathF.Abs(area) < .0001f) return;
        var minX = Math.Max(0, (int)MathF.Floor(MathF.Max(clip.X, MathF.Min(a.Pos.X, MathF.Min(b.Pos.X, c.Pos.X)))));
        var minY = Math.Max(0, (int)MathF.Floor(MathF.Max(clip.Y, MathF.Min(a.Pos.Y, MathF.Min(b.Pos.Y, c.Pos.Y)))));
        var maxX = Math.Min(Width, (int)MathF.Ceiling(MathF.Min(clip.Z, MathF.Max(a.Pos.X, MathF.Max(b.Pos.X, c.Pos.X)))));
        var maxY = Math.Min(Height, (int)MathF.Ceiling(MathF.Min(clip.W, MathF.Max(a.Pos.Y, MathF.Max(b.Pos.Y, c.Pos.Y)))));
        for (var y = minY; y < maxY; y++) for (var x = minX; x < maxX; x++)
        {
            var p = new Vector2(x + .5f, y + .5f);
            var wa = Edge(b.Pos, c.Pos, p) / area;
            var wb = Edge(c.Pos, a.Pos, p) / area;
            var wc = 1 - wa - wb;
            if (wa < 0 || wb < 0 || wc < 0) continue;
            var uv = a.Uv * wa + b.Uv * wb + c.Uv * wc;
            var tx = Math.Clamp((int)(uv.X * tw), 0, tw - 1);
            var ty = Math.Clamp((int)(uv.Y * th), 0, th - 1);
            var ti = (ty * tw + tx) * 4;
            var alpha = (wa * (a.Col >> 24) + wb * (b.Col >> 24) + wc * (c.Col >> 24)) / 255f * tex[ti + 3] / 255f;
            var target = (y * Width + x) * 4;
            for (var channel = 0; channel < 3; channel++)
            {
                var shift = channel * 8;
                var color = wa * ((a.Col >> shift) & 255) + wb * ((b.Col >> shift) & 255) + wc * ((c.Col >> shift) & 255);
                color *= tex[ti + channel] / 255f;
                pixels[target + channel] = (byte)Math.Clamp(color * alpha + pixels[target + channel] * (1 - alpha), 0, 255);
            }
        }
    }

    private static void SavePng(string path, byte[] pixels)
    {
        using var file = File.Create(path);
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(), Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), Height);
        header[8] = 8; header[9] = 6; Chunk(file, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zip = new ZLibStream(compressed, CompressionLevel.Optimal, true))
            for (var y = 0; y < Height; y++) { zip.WriteByte(0); zip.Write(pixels, y * Width * 4, Width * 4); }
        Chunk(file, "IDAT", compressed.ToArray()); Chunk(file, "IEND", []);
    }

    private static void Chunk(Stream file, string name, byte[] bytes)
    {
        var type = System.Text.Encoding.ASCII.GetBytes(name);
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, bytes.Length); file.Write(number); file.Write(type); file.Write(bytes);
        uint crc = uint.MaxValue;
        foreach (var value in type.Concat(bytes))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1));
        }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); file.Write(number);
    }
}
