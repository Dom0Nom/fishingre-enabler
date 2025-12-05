using System;

namespace AetherLinkMonitor.Models
{
    public class InstanceState
    {
        public int? ProcessId { get; set; }
        public IntPtr WindowHandle { get; set; }
        public DateTime? FirstEventTime { get; set; }
        public int EventCount { get; set; }
        public bool IsModConnected { get; set; }
        public string CurrentStatus { get; set; } = "Idle";
        public DateTime? LastEventTime { get; set; }
        public bool IsProcessFound => ProcessId.HasValue;
        public bool IsWindowFound => WindowHandle != IntPtr.Zero;
    }
}
