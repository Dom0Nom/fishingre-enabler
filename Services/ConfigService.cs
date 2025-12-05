using System;
using System.IO;
using AetherLinkMonitor.Models;
using Newtonsoft.Json;

namespace AetherLinkMonitor.Services
{
    public class ConfigService
    {
        private readonly string _configPath;

        public ConfigService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appDataPath, "MinecraftMonitor");
            Directory.CreateDirectory(appFolder);
            _configPath = Path.Combine(appFolder, "config.json");
        }

        public AppConfig LoadConfig()
        {
            if (!File.Exists(_configPath))
            {
                var defaultConfig = new AppConfig();
                SaveConfig(defaultConfig);
                return defaultConfig;
            }

            try
            {
                var json = File.ReadAllText(_configPath);
                return JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }

        public void SaveConfig(AppConfig config)
        {
            try
            {
                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(_configPath, json);
            }
            catch
            {
                // Silent fail for now
            }
        }
    }
}
