using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using AetherLinkMonitor.Helpers;
using AetherLinkMonitor.Models;
using AetherLinkMonitor.Services;

namespace AetherLinkMonitor.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ConfigService _configService;
        private readonly ProcessDiscoveryService _processDiscoveryService;
        private readonly LogWatcherService _logWatcherService;
        private readonly WindowService _windowService;
        private readonly IpcService _ipcService;

        private AppConfig _config;
        private string _prismLauncherPath;
        private string _keyToSend;
        private int _ipcPort;
        private int _lastInstanceCount = -1;

        public ObservableCollection<InstanceViewModel> Instances { get; } = new();
        public ObservableCollection<LogEntry> LogEntries { get; } = new();

        public ICommand RescanCommand { get; }
        public ICommand SaveSettingsCommand { get; }

        public string PrismLauncherPath
        {
            get => _prismLauncherPath;
            set
            {
                _prismLauncherPath = value;
                OnPropertyChanged();
            }
        }

        public string KeyToSend
        {
            get => _keyToSend;
            set
            {
                _keyToSend = value;
                OnPropertyChanged();
            }
        }

        public int IpcPort
        {
            get => _ipcPort;
            set
            {
                _ipcPort = value;
                OnPropertyChanged();
            }
        }

        public MainViewModel()
        {
            _configService = new ConfigService();
            _config = _configService.LoadConfig();

            _prismLauncherPath = _config.PrismLauncherPath;
            _keyToSend = _config.KeyToSend;
            _ipcPort = _config.IpcPort;

            _processDiscoveryService = new ProcessDiscoveryService(_config.PrismLauncherPath);
            _logWatcherService = new LogWatcherService();
            _windowService = new WindowService();
            _ipcService = new IpcService(_config.IpcPort);

            _processDiscoveryService.InstancesDiscovered += OnInstancesDiscovered;
            _processDiscoveryService.ProcessFound += OnProcessFound;
            _processDiscoveryService.ProcessLost += OnProcessLost;

            _logWatcherService.EventDetected += OnEventDetected;
            _logWatcherService.BlueRingedOctopusDetected += OnBlueRingedOctopusDetected;

            _ipcService.SequenceCompleted += OnSequenceCompleted;
            _ipcService.ClientConnectionChanged += OnClientConnectionChanged;

            RescanCommand = new RelayCommand(Rescan);
            SaveSettingsCommand = new RelayCommand(SaveSettings);

            StartServices();
        }

        private async void StartServices()
        {
            await _ipcService.StartAsync();
            _processDiscoveryService.Start();

            AddLog(LogLevel.Info, "System", "Services started");
        }

        private void Rescan()
        {
            _processDiscoveryService.DiscoverInstances();
            AddLog(LogLevel.Info, "System", "Rescanning instances");
        }

        private void SaveSettings()
        {
            _config.PrismLauncherPath = _prismLauncherPath;
            _config.KeyToSend = _keyToSend;
            _config.IpcPort = _ipcPort;

            _configService.SaveConfig(_config);
            AddLog(LogLevel.Info, "System", "Settings saved");
        }

        private void OnInstancesDiscovered(System.Collections.Generic.List<InstanceConfig> instances)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var existingInstances = Instances.ToDictionary(i => i.Name, i => i);

                Instances.Clear();

                foreach (var config in instances)
                {
                    InstanceViewModel vm;
                    if (existingInstances.TryGetValue(config.Name, out var existing))
                    {
                        vm = existing;
                    }
                    else
                    {
                        var state = new InstanceState();
                        vm = new InstanceViewModel(config, state);
                        _logWatcherService.StartWatching(config, state);
                    }

                    Instances.Add(vm);
                }

                // Only log when instance count changes
                if (_lastInstanceCount != instances.Count)
                {
                    _lastInstanceCount = instances.Count;
                    AddLog(LogLevel.Info, "System", $"Discovered {instances.Count} instance(s)");
                }
            });
        }

        private void OnProcessFound(InstanceConfig instance, int pid)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var vm = Instances.FirstOrDefault(i => i.Name == instance.Name);
                if (vm == null)
                {
                    var state = new InstanceState();
                    vm = new InstanceViewModel(instance, state);
                    Instances.Add(vm);
                    _logWatcherService.StartWatching(instance, state);
                }

                var hadProcess = vm.ProcessId.HasValue;
                vm.ProcessId = pid;

                if (_windowService.TryFindWindowByPid(pid, out var hwnd))
                {
                    vm.WindowHandle = hwnd;
                }

                if (!hadProcess)
                {
                    vm.EventCount = 0;
                    vm.State.FirstEventTime = null;
                    AddLog(LogLevel.Info, instance.Name, $"Process found: PID {pid}");
                }
            });
        }

        private void OnProcessLost(InstanceConfig instance)
        {
            var vm = Instances.FirstOrDefault(i => i.Name == instance.Name);
            if (vm == null) return;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (vm.ProcessId.HasValue)
                {
                    vm.ProcessId = null;
                    vm.WindowHandle = IntPtr.Zero;
                    vm.EventCount = 0;
                    vm.State.FirstEventTime = null;
                    AddLog(LogLevel.Warning, instance.Name, "Process lost");
                }
            });
        }

        private async void OnEventDetected(InstanceConfig instance)
        {
            var vm = Instances.FirstOrDefault(i => i.Name == instance.Name);
            if (vm == null || !vm.IsEnabled) return;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                vm.LastEventTime = DateTime.Now;
                var now = DateTime.Now;

                if (vm.State.FirstEventTime == null || (now - vm.State.FirstEventTime.Value).TotalSeconds > instance.EventWindowSeconds)
                {
                    vm.State.FirstEventTime = now;
                    vm.EventCount = 1;

                    AddLog(LogLevel.Info, instance.Name, $"First event detected, waiting {instance.FirstEventDelaySeconds}s then sending key");

                    await Task.Delay(instance.FirstEventDelaySeconds * 1000);

                    if (vm.WindowHandle != IntPtr.Zero)
                    {
                        _windowService.SendKey(vm.WindowHandle, _config.KeyToSend);
                        AddLog(LogLevel.Info, instance.Name, $"Sent key: {_config.KeyToSend}");
                    }
                }
                else if (vm.EventCount == 1)
                {
                    vm.EventCount = 2;
                    AddLog(LogLevel.Info, instance.Name, "Second event detected, running special sequence");

                    await _ipcService.SendRunSequenceCommandAsync(instance.InstanceId);
                }
                else
                {
                    vm.State.FirstEventTime = now;
                    vm.EventCount = 1;
                    AddLog(LogLevel.Info, instance.Name, "Third+ event detected, resetting to first event");
                }
            });
        }

        private async void OnBlueRingedOctopusDetected(InstanceConfig instance)
        {
            var vm = Instances.FirstOrDefault(i => i.Name == instance.Name);
            if (vm == null || !vm.IsEnabled) return;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                vm.LastEventTime = DateTime.Now;
                AddLog(LogLevel.Info, instance.Name, "[BRO] Blue Ringed Octopus sequence detected, sending key");

                if (vm.WindowHandle != IntPtr.Zero)
                {
                    _windowService.SendKey(vm.WindowHandle, _config.KeyToSend);
                    AddLog(LogLevel.Info, instance.Name, $"[BRO] Sent key: {_config.KeyToSend}");
                }
                else
                {
                    AddLog(LogLevel.Warning, instance.Name, "[BRO] Window handle not found, cannot send key");
                }
            });
        }

        private async void OnSequenceCompleted(string instanceId)
        {
            var vm = Instances.FirstOrDefault(i => i.Config.InstanceId == instanceId);
            if (vm == null) return;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                AddLog(LogLevel.Info, vm.Name, "Sequence completed, sending final key");

                if (vm.WindowHandle != IntPtr.Zero)
                {
                    _windowService.SendKey(vm.WindowHandle, _config.KeyToSend);
                    AddLog(LogLevel.Info, vm.Name, $"Sent final key: {_config.KeyToSend}");
                }

                vm.EventCount = 0;
                vm.State.FirstEventTime = null;
            });
        }

        private void OnClientConnectionChanged(string instanceId, bool connected)
        {
            var vm = Instances.FirstOrDefault(i => i.Config.InstanceId == instanceId);
            if (vm == null) return;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                vm.IsModConnected = connected;
                AddLog(LogLevel.Info, vm.Name, connected ? "Mod connected" : "Mod disconnected");
            });
        }

        private void AddLog(LogLevel level, string instanceName, string message)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                LogEntries.Insert(0, new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = level,
                    InstanceName = instanceName,
                    Message = message
                });

                while (LogEntries.Count > 500)
                {
                    LogEntries.RemoveAt(LogEntries.Count - 1);
                }
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
