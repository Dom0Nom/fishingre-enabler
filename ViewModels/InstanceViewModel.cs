using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MinecraftMonitor.Models;

namespace MinecraftMonitor.ViewModels
{
    public class InstanceViewModel : INotifyPropertyChanged
    {
        private readonly InstanceConfig _config;
        private readonly InstanceState _state;

        public InstanceViewModel(InstanceConfig config, InstanceState state)
        {
            _config = config;
            _state = state;
        }

        public InstanceConfig Config => _config;
        public InstanceState State => _state;

        public string Name
        {
            get => _config.Name;
            set
            {
                _config.Name = value;
                OnPropertyChanged();
            }
        }

        public bool IsEnabled
        {
            get => _config.IsEnabled;
            set
            {
                _config.IsEnabled = value;
                OnPropertyChanged();
            }
        }

        public int? ProcessId
        {
            get => _state.ProcessId;
            set
            {
                _state.ProcessId = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProcessStatus));
            }
        }

        public IntPtr WindowHandle
        {
            get => _state.WindowHandle;
            set
            {
                _state.WindowHandle = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WindowStatus));
            }
        }

        public int EventCount
        {
            get => _state.EventCount;
            set
            {
                _state.EventCount = value;
                OnPropertyChanged();
            }
        }

        public bool IsModConnected
        {
            get => _state.IsModConnected;
            set
            {
                _state.IsModConnected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IpcStatus));
            }
        }

        public string CurrentStatus
        {
            get => _state.CurrentStatus;
            set
            {
                _state.CurrentStatus = value;
                OnPropertyChanged();
            }
        }

        public DateTime? LastEventTime
        {
            get => _state.LastEventTime;
            set
            {
                _state.LastEventTime = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LastEventTimeDisplay));
            }
        }

        public string ProcessStatus => _state.IsProcessFound ? $"PID: {_state.ProcessId}" : "Not Found";
        public string WindowStatus => _state.IsWindowFound ? "Found" : "Not Found";
        public string IpcStatus => _state.IsModConnected ? "Connected" : "Disconnected";
        public string LastEventTimeDisplay => _state.LastEventTime?.ToString("HH:mm:ss") ?? "Never";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
