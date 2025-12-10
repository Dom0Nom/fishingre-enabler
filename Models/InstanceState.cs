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

        // Blue Ringed Octopus event tracking
        public DateTime? BroStep1Time { get; set; }  // "You were killed by Blue Ringed Octopus"
        public DateTime? BroStep2Time { get; set; }  // "Disabling Route Executor"
        public DateTime? BroStep3Time { get; set; }  // "Fishing: Disabled"
        public int BroSequenceStep { get; set; }     // 0 = none, 1 = step1, 2 = step2, 3 = complete

        public void ResetBroSequence()
        {
            BroStep1Time = null;
            BroStep2Time = null;
            BroStep3Time = null;
            BroSequenceStep = 0;
        }
    }
}
