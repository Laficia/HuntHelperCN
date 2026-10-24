using System.Collections.Generic;
using System.Numerics;
namespace HuntHelper;
public class HuntTrainSettings
{
        #region Hunt Train Window
        public Vector2 HuntTrainWindowSize { get; set; } = new Vector2(520, 300);
        public Vector2 HuntTrainWindowPos { get; set; } = new Vector2(150, 150);
        public bool HuntTrainShowLastSeen { get; set; } = false;
        public bool HuntTrainUseBorder { get; set; } = false;
        public bool HuntTrainNextOpensMap { get; set; } = false;
        public bool HuntTrainNextTeleportMe { get; set; } = false;
        public bool HuntTrainNextTeleportMeOnCommand { get; set; } = false;
        public bool HuntTrainShowTeleportButtons { get; set; } = true;
        public bool HuntTrainTeleportToAetheryte { get; set; } = false;
        public bool HuntTrainShowFlagInChat { get; set; } = true;
        public bool HuntTrainShowUIDuringIPCImport { get; set; } = true;

        public bool HuntTrainShowMapName { get; set; } = false;
        public bool HuntTrainShowMissingRoster { get; set; } = false;
        public Dictionary<uint, Dictionary<uint, int>> HuntTrainMapInstanceCounts { get; set; } = new();
        public Dictionary<uint, List<uint>> HuntTrainVersionTabOrders { get; set; } = new();
        public List<uint> HuntTrainWorldTabOrder { get; set; } = new();

        #endregion
    public bool HuntTrainCaptureSonar { get; set; } = true;
}
