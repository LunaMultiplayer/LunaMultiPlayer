using LmpClient.Systems.TimeSync;
using LmpClient.Systems.Warp;
using System.Text;

namespace LmpClient.Windows.Status
{
    /// <summary>
    /// This class is intended to store all the texts in the status window in the hope to reduce the GC allocations as otherwise the strings would be 
    /// created on every gui call
    /// </summary>
    public class StatusTexts
    {
        public const string DebugBtnTxt = "Debug";
        public const string SystemsBtnTxt = "Systems";
        public const string VesselsBtnTxt = "Vessels";

        public const string WarpingLabelTxt = "WARPING";
        public const string SpectateBtnTxt = "Spectate";
        public const string Debug1BtnTxt = "D1";
        public const string Debug2BtnTxt = "D2";
        public const string Debug3BtnTxt = "D3";
        public const string Debug4BtnTxt = "D4";
        public const string Debug5BtnTxt = "D5";
        public const string Debug6BtnTxt = "D6";
        public const string Debug7BtnTxt = "D7";
        public const string Debug8BtnTxt = "D8";
        public const string Debug9BtnTxt = "D9";

        private const string NegativeDeltaTimePrefix = " (-";
        private const string PositiveDeltaTimePrefix = " (+";
        private const string CloseDeltaTime = ")";

        private static readonly StringBuilder StringBuilder = new StringBuilder();

        public static string GetTimeLabel(SubspaceDisplayEntry currentEntry)
        {
            StringBuilder.Length = 0;

            var subspaceTime = WarpSystem.Singleton.GetSubspaceTime(currentEntry.SubspaceId);

            if (subspaceTime <= 0d)
                return "----";

            StringBuilder.Append(KSPUtil.PrintDateCompact(subspaceTime, true, true));

            if (WarpSystem.Singleton.CurrentSubspace != currentEntry.SubspaceId)
                AppendDeltaTime(subspaceTime);

            return StringBuilder.ToString();
        }

        private static void AppendDeltaTime(double subspaceTime)
        {
            var currentTime = TimeSyncSystem.UniversalTime;
            if (subspaceTime < currentTime)
            {
                StringBuilder.Append(NegativeDeltaTimePrefix).Append(KSPUtil.PrintTimeCompact(currentTime - subspaceTime, false));
            }
            else
            {
                StringBuilder.Append(PositiveDeltaTimePrefix).Append(KSPUtil.PrintTimeCompact(subspaceTime - currentTime, false));
            }

            StringBuilder.Append(CloseDeltaTime);
        }
    }
}
