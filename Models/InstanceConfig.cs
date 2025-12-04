namespace MinecraftMonitor.Models
{
    public class InstanceConfig
    {
        public string Name { get; set; } = string.Empty;
        public string LogPath { get; set; } = string.Empty;
        public string InstanceDirectory { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public int EventWindowSeconds { get; set; } = 30;
        public int FirstEventDelaySeconds { get; set; } = 10;
        public int AfterHubDelaySeconds { get; set; } = 10;
        public int AfterWarpDelaySeconds { get; set; } = 5;
        public string InstanceId { get; set; } = string.Empty;
    }
}
