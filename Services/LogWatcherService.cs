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
        private readonly Dictionary<string, InstanceState> _instanceStates = new();

        private readonly List<string> _eventPatterns = new()
        {
            "Failed to find or reach a new hotspot after 5 attempts",
            "Oops! Looks like we couldn't find optimal Return Route for current location!"
        };

        // Blue Ringed Octopus sequence patterns
        private const string BRO_STEP1 = "You were killed by Blue Ringed Octopus";
        private const string BRO_STEP2 = "Disabling Route Executor";
        private const string BRO_STEP3 = "Fishing: §cDisabled";
        private const int BRO_SEQUENCE_WINDOW_SECONDS = 10;

        // Hotspot Mismatch pattern
        private const string HOTSPOT_MISMATCH = "Predicted hotspot doesn't seem to match the actual hotspot";
        private const int HOTSPOT_MISMATCH_THRESHOLD = 5;
        private const int HOTSPOT_MISMATCH_WINDOW_SECONDS = 5;

        public event Action<InstanceConfig>? EventDetected;
        public event Action<InstanceConfig>? BlueRingedOctopusDetected;
        public event Action<InstanceConfig>? HotspotMismatchDetected;

        public void StartWatching(InstanceConfig instance, InstanceState state)
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
            _instanceStates[instance.Name] = state;

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

                                        // Check for Blue Ringed Octopus sequence patterns
                                        CheckBlueRingedOctopusSequence(instance, line);

                                        // Check for Hotspot Mismatch pattern
                                        CheckHotspotMismatch(instance, line);
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

        private void CheckBlueRingedOctopusSequence(InstanceConfig instance, string line)
        {
            if (!_instanceStates.TryGetValue(instance.Name, out var state))
                return;

            var now = DateTime.Now;

            // Check for Step 1: "You were killed by Blue Ringed Octopus"
            if (line.Contains(BRO_STEP1, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[LogWatcher] [BRO] Step 1/3 detected for {instance.Name}: Killed by Blue Ringed Octopus");
                state.BroStep1Time = now;
                state.BroSequenceStep = 1;
                state.BroStep2Time = null;
                state.BroStep3Time = null;
                return;
            }

            // Check for Step 2: "Disabling Route Executor" (only if step 1 was detected)
            if (state.BroSequenceStep >= 1 && line.Contains(BRO_STEP2, StringComparison.OrdinalIgnoreCase))
            {
                // Check if within window
                if (state.BroStep1Time.HasValue && (now - state.BroStep1Time.Value).TotalSeconds <= BRO_SEQUENCE_WINDOW_SECONDS)
                {
                    Console.WriteLine($"[LogWatcher] [BRO] Step 2/3 detected for {instance.Name}: Route Executor disabled");
                    state.BroStep2Time = now;
                    state.BroSequenceStep = 2;
                }
                else
                {
                    Console.WriteLine($"[LogWatcher] [BRO] Step 2 detected but outside window, resetting sequence for {instance.Name}");
                    state.ResetBroSequence();
                }
                return;
            }

            // Check for Step 3: "Fishing: Disabled" (only if steps 1 and 2 were detected)
            if (state.BroSequenceStep >= 2 && line.Contains(BRO_STEP3, StringComparison.Ordinal))
            {
                // Check if within window from step 1
                if (state.BroStep1Time.HasValue && (now - state.BroStep1Time.Value).TotalSeconds <= BRO_SEQUENCE_WINDOW_SECONDS)
                {
                    Console.WriteLine($"[LogWatcher] [BRO] ==================== BLUE RINGED OCTOPUS SEQUENCE COMPLETE ====================");
                    Console.WriteLine($"[LogWatcher] [BRO] Instance: {instance.Name}");
                    Console.WriteLine($"[LogWatcher] [BRO] All 3 steps detected within {BRO_SEQUENCE_WINDOW_SECONDS} seconds");
                    Console.WriteLine($"[LogWatcher] [BRO] =============================================================================");

                    state.BroStep3Time = now;
                    state.BroSequenceStep = 3;

                    // Trigger the Blue Ringed Octopus event
                    BlueRingedOctopusDetected?.Invoke(instance);

                    // Reset sequence after completion
                    state.ResetBroSequence();
                }
                else
                {
                    Console.WriteLine($"[LogWatcher] [BRO] Step 3 detected but outside window, resetting sequence for {instance.Name}");
                    state.ResetBroSequence();
                }
                return;
            }
        }

        private void CheckHotspotMismatch(InstanceConfig instance, string line)
        {
            if (!_instanceStates.TryGetValue(instance.Name, out var state))
                return;

            // Check if line contains the hotspot mismatch pattern
            if (!line.Contains(HOTSPOT_MISMATCH, StringComparison.OrdinalIgnoreCase))
                return;

            var now = DateTime.Now;

            // If this is the first occurrence or window expired, reset
            if (!state.HotspotMismatchFirstTime.HasValue ||
                (now - state.HotspotMismatchFirstTime.Value).TotalSeconds > HOTSPOT_MISMATCH_WINDOW_SECONDS)
            {
                state.HotspotMismatchFirstTime = now;
                state.HotspotMismatchCount = 1;
                Console.WriteLine($"[LogWatcher] [Hotspot] First mismatch detected for {instance.Name} (1/{HOTSPOT_MISMATCH_THRESHOLD})");
                return;
            }

            // Increment count
            state.HotspotMismatchCount++;
            Console.WriteLine($"[LogWatcher] [Hotspot] Mismatch detected for {instance.Name} ({state.HotspotMismatchCount}/{HOTSPOT_MISMATCH_THRESHOLD})");

            // Check if threshold reached
            if (state.HotspotMismatchCount >= HOTSPOT_MISMATCH_THRESHOLD)
            {
                Console.WriteLine($"[LogWatcher] [Hotspot] ==================== HOTSPOT MISMATCH THRESHOLD REACHED ====================");
                Console.WriteLine($"[LogWatcher] [Hotspot] Instance: {instance.Name}");
                Console.WriteLine($"[LogWatcher] [Hotspot] {state.HotspotMismatchCount} occurrences in {HOTSPOT_MISMATCH_WINDOW_SECONDS} seconds");
                Console.WriteLine($"[LogWatcher] [Hotspot] ============================================================================");

                // Trigger the Hotspot Mismatch event
                HotspotMismatchDetected?.Invoke(instance);

                // Reset counter after triggering
                state.ResetHotspotMismatch();
            }
        }
    }
}
