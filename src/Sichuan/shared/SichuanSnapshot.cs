using System;
namespace BD2Sichuan
{
    // Wire v1 is private, local-only. Times are ISO UTC for Mono/.NET interoperability.
    public sealed class SichuanSnapshot
    {
        public string NetworkState { get; set; } = "";
        public bool InputReady { get; set; }
        public SichuanRunStatus Run { get; set; } = new SichuanRunStatus();
        public int ExecutionProtocol { get; set; }
        public int AnimationCount { get; set; }
        public SichuanActionReceipt LastAction { get; set; } = new SichuanActionReceipt();
        public int SchemaVersion { get; set; } = 1;
        public string SessionId { get; set; } = "";
        public string CapturedAtUtc { get; set; } = "";
        public string Runtime { get; set; } = "";
        public int ProcessId { get; set; }
        public string ClientMvid { get; set; } = "";
        public long Generation { get; set; }
        public long Revision { get; set; }
        public string State { get; set; } = "waiting_board";
        public string Error { get; set; } = "";
        public int LevelGroup { get; set; }
        public int Level { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int[] Cells { get; set; } = new int[0];
        public string BoardHash { get; set; } = "";
        public int SelectedIndex { get; set; } = -1;
        public float RemainingSeconds { get; set; }
        public float ElapsedSeconds { get; set; }
        public float ComboSeconds { get; set; }
        public float TimerBonusSeconds { get; set; }
        public int ComboCount { get; set; }
        public string[] ImageFiles { get; set; } = new string[0];
        public double CaptureMilliseconds { get; set; }
    }
}
