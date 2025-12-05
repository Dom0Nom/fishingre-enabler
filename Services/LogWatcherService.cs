using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AetherLinkMonitor.Models;

namespace AetherLinkMonitor.Services
{
    public class LogWatcherService
    {
        private readonly Dictionary<string, CancellationTokenSource> _monitorTasks = new();
        private readonly Dictionary<string, long> _lastPositions = new();
        private readonly List<string> _eventPatterns = new()
        {
            "Failed to find or reach a new hotspot after 5 attempts",
            "Oops! Looks like we couldn't find optimal Return Route for current location!"
        };

        public event Action<InstanceConfig>? EventDetected;

        public void StartWatching(InstanceConfig instance)
        {
            if (!File.Exists(instance.LogPath))
            {
                Console.WriteLine($"[LogWatcher] Cannot start watching {instance.Name}: Log file does not exist at {instance.LogPath}");
                return;
            }

            if (_monitorTasks.ContainsKey(instance.Name))
            {
                Console.WriteLine($"[LogWatcher] Already watching {instance.Name}");
                return;
            }

            // Set initial position to end of file (only monitor new lines)
            var initialPos = new FileInfo(instance.LogPath).Length;
            _lastPositions[instance.Name] = initialPos;

            // Start polling task
            var cts = new CancellationTokenSource();
            _monitorTasks[instance.Name] = cts;

            Task.Run(() => MonitorLogAsync(instance, cts.Token));

            Console.WriteLine($"[LogWatcher] ========================================");
            Console.WriteLine($"[LogWatcher] Started watching: {instance.Name}");
            Console.WriteLine($"[LogWatcher] Log file: {instance.LogPath}");
            Console.WriteLine($"[LogWatcher] Initial position: {initialPos}");
            Console.WriteLine($"[LogWatcher] ========================================");
        }

        public void StopWatching(InstanceConfig instance)
        {
            if (_monitorTasks.TryGetValue(instance.Name, out var cts))
            {
                cts.Cancel();
                _monitorTasks.Remove(instance.Name);
                Console.WriteLine($"[LogWatcher] Stopped watching {instance.Name}");
            }

            _lastPositions.Remove(instance.Name);
        }

        private async Task MonitorLogAsync(InstanceConfig instance, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (File.Exists(instance.LogPath))
                    {
                        var fileInfo = new FileInfo(instance.LogPath);
                        long currentLength = fileInfo.Length;
                        long currentPosition = _lastPositions.GetValueOrDefault(instance.Name, 0);

                        // Check if file has grown
                        if (currentLength > currentPosition)
                        {
                            using (var fs = new FileStream(instance.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            {
                                // Seek to last read position
                                fs.Seek(currentPosition, SeekOrigin.Begin);

                                using (var reader = new StreamReader(fs))
                                {
                                    string? line;
                                    while ((line = await reader.ReadLineAsync()) != null)
                                    {
                                        // Check for event patterns (simple contains check)
                                        foreach (var pattern in _eventPatterns)
                                        {
                                            if (line.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                                            {
                                                Console.WriteLine($"[LogWatcher] ==================== EVENT DETECTED ====================");
                                                Console.WriteLine($"[LogWatcher] Instance: {instance.Name}");
                                                Console.WriteLine($"[LogWatcher] Log File: {instance.LogPath}");
                                                Console.WriteLine($"[LogWatcher] Pattern: {pattern}");
                                                Console.WriteLine($"[LogWatcher] ======================================================");
                                                EventDetected?.Invoke(instance);
                                                break;
                                            }
                                        }
                                    }
                                }

                                // Update position
                                _lastPositions[instance.Name] = currentLength;
                            }
                        }
                        else if (currentLength < currentPosition)
                        {
                            // Log file was reset/rotated, start from beginning
                            _lastPositions[instance.Name] = 0;
                            Console.WriteLine($"[LogWatcher] Log file reset detected for {instance.Name}, restarting from beginning");
                        }
                    }

                    // Check every 500ms
                    await Task.Delay(500, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LogWatcher] Error monitoring log for {instance.Name}: {ex.Message}");
                    await Task.Delay(1000, cancellationToken);
                }
            }
        }
    }
}
