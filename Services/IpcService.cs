using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AetherLinkMonitor.Services
{
    public class IpcService
    {
        private TcpListener? _listener;
        private readonly int _port;
        private readonly Dictionary<string, TcpClient> _clients = new();
        private bool _isRunning;

        public event Action<string>? SequenceCompleted;
        public event Action<string, bool>? ClientConnectionChanged;

        public IpcService(int port)
        {
            _port = port;
        }

        public async Task StartAsync()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, _port);
                _listener.Start();
                _isRunning = true;
                Console.WriteLine($"[IPC] Server started on port {_port}");

                _ = Task.Run(AcceptClientsAsync);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IPC] Failed to start on port {_port}: {ex.Message}");
                // Try alternate port
                try
                {
                    _listener = new TcpListener(IPAddress.Loopback, _port + 1);
                    _listener.Start();
                    _isRunning = true;
                    Console.WriteLine($"[IPC] Server started on alternate port {_port + 1}");

                    _ = Task.Run(AcceptClientsAsync);
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"[IPC] Failed to start on alternate port {_port + 1}: {ex2.Message}");
                }
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _listener?.Stop();

            foreach (var client in _clients.Values)
            {
                client.Close();
            }

            _clients.Clear();
        }

        private async Task AcceptClientsAsync()
        {
            while (_isRunning)
            {
                try
                {
                    if (_listener == null)
                        break;

                    var client = await _listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleClientAsync(client));
                }
                catch
                {
                    // Silent fail
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            string? instanceId = null;

            try
            {
                using var stream = client.GetStream();
                var buffer = new byte[4096];

                while (client.Connected && _isRunning)
                {
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                        break;

                    var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    var responses = ProcessMessage(message, ref instanceId, client);

                    foreach (var response in responses)
                    {
                        var responseBytes = Encoding.UTF8.GetBytes(response);
                        await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IPC] Error: {ex.Message}");
            }
            finally
            {
                if (instanceId != null)
                {
                    _clients.Remove(instanceId);
                    ClientConnectionChanged?.Invoke(instanceId, false);
                    Console.WriteLine($"[IPC] Client disconnected: {instanceId}");
                }

                client.Close();
            }
        }

        private List<string> ProcessMessage(string message, ref string? instanceId, TcpClient client)
        {
            var responses = new List<string>();

            try
            {
                var json = JObject.Parse(message);
                var type = json["type"]?.ToString();
                var msgInstanceId = json["instanceId"]?.ToString();

                if (type == "status" && msgInstanceId != null)
                {
                    instanceId = msgInstanceId;

                    // If there's already a client with this instanceId, close it (old connection)
                    if (_clients.TryGetValue(instanceId, out var oldClient))
                    {
                        Console.WriteLine($"[IPC] Replacing old connection for {instanceId}");
                        oldClient.Close();
                    }

                    // Store the new client
                    _clients[instanceId] = client;
                    Console.WriteLine($"[IPC] Client connected: {instanceId}");
                    ClientConnectionChanged?.Invoke(instanceId, true);

                    responses.Add(JsonConvert.SerializeObject(new
                    {
                        type = "statusAck",
                        instanceId
                    }));
                }
                else if (type == "sequenceComplete" && msgInstanceId != null)
                {
                    SequenceCompleted?.Invoke(msgInstanceId);
                }
            }
            catch
            {
                // Silent fail
            }

            return responses;
        }

        public async Task SendRunSequenceCommandAsync(string instanceId)
        {
            if (!_clients.TryGetValue(instanceId, out var client))
                return;

            try
            {
                var message = JsonConvert.SerializeObject(new
                {
                    type = "runSpecialSequence",
                    instanceId,
                    afterHubSeconds = 10,
                    afterWarpSeconds = 5
                });

                var bytes = Encoding.UTF8.GetBytes(message);
                await client.GetStream().WriteAsync(bytes, 0, bytes.Length);
            }
            catch
            {
                _clients.Remove(instanceId);
                ClientConnectionChanged?.Invoke(instanceId, false);
            }
        }
    }
}
