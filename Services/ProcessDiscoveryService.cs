using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Timers;
using AetherLinkMonitor.Models;

namespace AetherLinkMonitor.Services
{
    public class ProcessDiscoveryService
    {
        private readonly Timer _scanTimer;
        private Dictionary<string, InstanceConfig> _knownInstances = new();
        private Dictionary<string, int> _instancePids = new();

        public event Action<List<InstanceConfig>>? InstancesDiscovered;
        public event Action<InstanceConfig, int>? ProcessFound;
        public event Action<InstanceConfig>? ProcessLost;

        public ProcessDiscoveryService(string prismLauncherPath)
        {
            _scanTimer = new Timer(5000);
            _scanTimer.Elapsed += OnScanTimerElapsed;
        }

        public void Start()
        {
            ScanForProcesses();
            _scanTimer.Start();
        }

        public void Stop()
        {
            _scanTimer.Stop();
        }

        public void DiscoverInstances()
        {
            // Force immediate scan
            ScanForProcesses();
        }

        private void OnScanTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            ScanForProcesses();
        }

        private void ScanForProcesses()
        {
            try
            {
                var currentInstances = new Dictionary<string, InstanceConfig>();
                var currentPids = new Dictionary<string, int>();

                // Find all java/javaw processes on the system
                var javaProcesses = GetAllJavaProcesses();

                // Removed verbose logging - only log when processes change

                foreach (var processInfo in javaProcesses)
                {
                    // Check if this is a Minecraft process
                    bool isMinecraft = !string.IsNullOrEmpty(processInfo.CommandLine) && IsMinecraftProcess(processInfo.CommandLine);
                    Console.WriteLine($"[ProcessDiscovery] PID {processInfo.ProcessId}: IS Minecraft process = {isMinecraft}");

                    if (!isMinecraft)
                    {
                        continue;
                    }

                    // Extract instance directory from command line
                    var instanceDir = ExtractInstanceDirectory(processInfo.CommandLine);
                    Console.WriteLine($"[ProcessDiscovery] PID {processInfo.ProcessId}: Instance dir = {instanceDir ?? "NULL"}");

                    if (string.IsNullOrEmpty(instanceDir) || !Directory.Exists(instanceDir))
                    {
                        Console.WriteLine($"[ProcessDiscovery] PID {processInfo.ProcessId}: Instance dir invalid or doesn't exist");
                        continue;
                    }

                    // Find the log file - try multiple possible locations
                    string? logPath = null;
                    string[] possiblePaths = new[]
                    {
                        Path.Combine(instanceDir, "logs", "latest.log"),                    // Direct: instance\logs\latest.log
                        Path.Combine(instanceDir, "minecraft", "logs", "latest.log"),       // PrismLauncher: instance\minecraft\logs\latest.log
                        Path.Combine(instanceDir, ".minecraft", "logs", "latest.log")       // Some launchers: instance\.minecraft\logs\latest.log
                    };

                    // Use the first path that exists
                    foreach (var path in possiblePaths)
                    {
                        if (File.Exists(path))
                        {
                            logPath = path;
                            Console.WriteLine($"[ProcessDiscovery] PID {processInfo.ProcessId}: Found log at {logPath}");
                            break;
                        }
                    }

                    if (logPath == null)
                    {
                        Console.WriteLine($"[ProcessDiscovery] PID {processInfo.ProcessId}: No log file found in any expected location");
                        continue;
                    }

                    // Get instance name
                    var instanceName = Path.GetFileName(instanceDir);
                    var instanceKey = instanceDir.ToLowerInvariant();

                    // Create or retrieve instance config
                    if (!currentInstances.ContainsKey(instanceKey))
                    {
                        var config = new InstanceConfig
                        {
                            Name = instanceName,
                            InstanceDirectory = instanceDir,
                            LogPath = logPath,
                            InstanceId = instanceName.Replace(" ", "_").ToLower(),
                            IsEnabled = true
                        };

                        currentInstances[instanceKey] = config;
                        currentPids[instanceKey] = processInfo.ProcessId;

                        // Track this instance
                        if (!_knownInstances.ContainsKey(instanceKey))
                        {
                            _knownInstances[instanceKey] = config;
                        }
                    }
                }

                // Notify about new/changed processes
                foreach (var kvp in currentInstances)
                {
                    var instanceKey = kvp.Key;
                    var config = kvp.Value;
                    var pid = currentPids[instanceKey];

                    bool pidChanged = !_instancePids.ContainsKey(instanceKey) || _instancePids[instanceKey] != pid;

                    if (pidChanged)
                    {
                        _instancePids[instanceKey] = pid;
                        ProcessFound?.Invoke(config, pid);
                    }
                }

                // Notify about lost processes
                var lostInstances = _instancePids.Keys.Except(currentInstances.Keys).ToList();
                foreach (var instanceKey in lostInstances)
                {
                    if (_knownInstances.TryGetValue(instanceKey, out var config))
                    {
                        ProcessLost?.Invoke(config);
                    }
                    _instancePids.Remove(instanceKey);
                }

                // Notify about all discovered instances
                var allInstances = _knownInstances.Values.ToList();
                InstancesDiscovered?.Invoke(allInstances);
            }
            catch
            {
                // Silent fail on scan errors
            }
        }

        private List<ProcessInfo> GetAllJavaProcesses()
        {
            var processes = new List<ProcessInfo>();

            try
            {
                // Get all javaw and java processes
                var javaProcs = Process.GetProcessesByName("javaw")
                    .Concat(Process.GetProcessesByName("java"))
                    .ToArray();

                Console.WriteLine($"[ProcessDiscovery] Found {javaProcs.Length} Java processes");

                foreach (var proc in javaProcs)
                {
                    try
                    {
                        Console.WriteLine($"[ProcessDiscovery] PID {proc.Id}: Attempting to get command line...");

                        // Use Win32 API to get command line (more reliable than WMI)
                        var commandLine = ProcessCommandLineHelper.GetCommandLine(proc.Id);

                        if (!string.IsNullOrEmpty(commandLine))
                        {
                            Console.WriteLine($"[ProcessDiscovery] PID {proc.Id}: CmdLine length = {commandLine.Length}");
                            processes.Add(new ProcessInfo
                            {
                                ProcessId = proc.Id,
                                CommandLine = commandLine
                            });
                        }
                        else
                        {
                            Console.WriteLine($"[ProcessDiscovery] PID {proc.Id}: Failed to get command line");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ProcessDiscovery] PID {proc.Id}: Exception - {ex.Message}");
                        // Skip this process if we can't get its command line
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ProcessDiscovery] Exception in GetAllJavaProcesses: {ex.Message}");
            }

            return processes;
        }

        private bool IsMinecraftProcess(string commandLine)
        {
            if (string.IsNullOrEmpty(commandLine))
                return false;

            // Check for Minecraft/Forge indicators
            var indicators = new[]
            {
                "minecraft",
                "forge",
                "net.minecraft",
                "MinecraftForge",
                "launchwrapper",
                "PrismLauncher",
                ".minecraft",
                "minecraft-1.",
                "forge-1."
            };

            var cmdLower = commandLine.ToLowerInvariant();
            return indicators.Any(indicator => cmdLower.Contains(indicator.ToLowerInvariant()));
        }

        private string? ExtractInstanceDirectory(string commandLine)
        {
            try
            {
                // Try -Djava.library.path first (PrismLauncher format)
                // Format: -Djava.library.path=C:\...\instances\InstanceName\natives
                int libraryPathIndex = commandLine.IndexOf("-Djava.library.path=");
                if (libraryPathIndex != -1)
                {
                    int start = libraryPathIndex + "-Djava.library.path=".Length;
                    int end = commandLine.IndexOf(' ', start);
                    string libraryPath = end != -1 ? commandLine.Substring(start, end - start) : commandLine.Substring(start);

                    // Remove quotes if present
                    libraryPath = libraryPath.Trim('"').Replace("/", "\\");

                    // Navigate up from natives to instance root
                    // Path is typically: instances\Name\natives
                    string? instanceDir = Path.GetDirectoryName(libraryPath); // Gets instance directory
                    if (instanceDir != null && Directory.Exists(instanceDir))
                    {
                        Console.WriteLine($"[ProcessDiscovery] Extracted instance dir from library path: {instanceDir}");
                        return instanceDir;
                    }
                }

                // Try --gameDir as fallback
                int gameDirIndex = commandLine.IndexOf("--gameDir");
                if (gameDirIndex != -1)
                {
                    int start = gameDirIndex + "--gameDir".Length;
                    // Skip whitespace
                    while (start < commandLine.Length && char.IsWhiteSpace(commandLine[start]))
                        start++;

                    int end = commandLine.IndexOf(' ', start);
                    string gameDir = end != -1 ? commandLine.Substring(start, end - start) : commandLine.Substring(start);

                    // Remove quotes if present
                    gameDir = gameDir.Trim('"').Replace("/", "\\");

                    // gameDir typically points to minecraft folder, go up one level
                    string? instanceDir = Path.GetDirectoryName(gameDir);
                    if (instanceDir != null && Directory.Exists(instanceDir))
                    {
                        Console.WriteLine($"[ProcessDiscovery] Extracted instance dir from gameDir: {instanceDir}");
                        return instanceDir;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private class ProcessInfo
        {
            public int ProcessId { get; set; }
            public string CommandLine { get; set; } = "";
        }
    }
}
