using System;

namespace AetherLinkMonitor.Models
{
    public enum LogLevel
    {
        Info,
        Warning,
        Error
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public LogLevel Level { get; set; }
        public string InstanceName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
