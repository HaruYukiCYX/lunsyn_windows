using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Zeroconf;

namespace lunsyn;

public class ActivityPayload
{
    public string ForegroundApp { get; set; } = "";
    public string BrowserURL { get; set; } = "";
    public string BrowserTitle { get; set; } = "";
    public string MusicApp { get; set; } = "";
    public string MusicTitle { get; set; } = "";
    public string MusicArtist { get; set; } = "";
    public bool IsPlaying { get; set; }
}

public class ActivitySyncService : IDisposable
{
    public enum ConnectionStateEnum { Disconnected, Connecting, Connected }
    public ConnectionStateEnum ConnectionState { get; private set; } = ConnectionStateEnum.Disconnected;
    public event Action<ConnectionStateEnum>? ConnectionStateChanged;
    public event Action<ActivityPayload>? PayloadReceived;

    private TcpListener? _listener;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private bool _running;
    private const int Port = 18888;
    private readonly JsonSerializerOptions _jsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task StartAsync()
    {
        if (ConnectionState != ConnectionStateEnum.Disconnected) return;
        SetState(ConnectionStateEnum.Connecting);

        _running = true;

        // 先尝试作为客户端搜索 Bonjour
        var results = await ZeroconfResolver.ResolveAsync("_lunsynchat._tcp", TimeSpan.FromSeconds(3));
        var host = results.FirstOrDefault();
        
        if (host != null)
        {
            // 找到服务端，作为客户端连接
            System.Console.WriteLine($"[望月] 找到服务端: {host.IPAddress}");
            await ConnectAsClientAsync(host.IPAddress);
        }
        else
        {
            // 没找到，自己作为服务端
            System.Console.WriteLine("[望月] 启动活动同步服务端...");
            await StartServerAsync();
        }
    }

    private async Task StartServerAsync()
    {
        _listener = new TcpListener(IPAddress.Any, Port);
        _listener.Start();
        System.Console.WriteLine($"[望月] 活动同步服务端已启动 (端口 {Port})");

        // 注册 Bonjour
        _ = Task.Run(() => RegisterBonjourAsync());

        var client = await _listener.AcceptTcpClientAsync();
        _client = client;
        _stream = client.GetStream();
        SetState(ConnectionStateEnum.Connected);
        _ = Task.Run(ReceiveLoop);
    }

    private async Task ConnectAsClientAsync(string ip)
    {
        _client = new TcpClient();
        await _client.ConnectAsync(ip, Port);
        _stream = _client.GetStream();
        SetState(ConnectionStateEnum.Connected);
        _ = Task.Run(ReceiveLoop);
    }

    private async Task RegisterBonjourAsync()
    {
        try
        {
            var service = new ZeroconfService
            {
                Name = "LunsynActivitySync",
                Type = "_lunsynchat._tcp",
                Port = (ushort)Port
            };
            await service.PublishAsync();
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[望月] Bonjour 注册失败: {ex.Message}");
        }
    }

    public async Task SendPayloadAsync(ActivityPayload payload)
    {
        if (_stream == null || ConnectionState != ConnectionStateEnum.Connected) return;
        try
        {
            var json = JsonSerializer.Serialize(payload, _jsonOpts);
            var bytes = Encoding.UTF8.GetBytes(json);
            var header = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(bytes.Length));
            await _stream.WriteAsync(header, 0, 4);
            await _stream.WriteAsync(bytes, 0, bytes.Length);
            await _stream.FlushAsync();
        }
        catch { SetState(ConnectionStateEnum.Disconnected); }
    }

    private async Task ReceiveLoop()
    {
        var header = new byte[4];
        while (_running && _stream != null)
        {
            try
            {
                int read = await _stream.ReadAsync(header, 0, 4);
                if (read < 4) break;
                int length = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(header, 0));
                if (length <= 0 || length > 65536) break;

                var buffer = new byte[length];
                int total = 0;
                while (total < length)
                {
                    read = await _stream.ReadAsync(buffer, total, length - total);
                    if (read == 0) break;
                    total += read;
                }
                if (total < length) break;

                var json = Encoding.UTF8.GetString(buffer, 0, total);
                var payload = JsonSerializer.Deserialize<ActivityPayload>(json, _jsonOpts);
                if (payload != null) PayloadReceived?.Invoke(payload);
            }
            catch { break; }
        }
        SetState(ConnectionStateEnum.Disconnected);
    }

    private void SetState(ConnectionStateEnum state)
    {
        ConnectionState = state;
        ConnectionStateChanged?.Invoke(state);
    }

    public void Stop()
    {
        _running = false;
        _stream?.Close();
        _client?.Close();
        _listener?.Stop();
        SetState(ConnectionStateEnum.Disconnected);
    }

    public void Dispose() => Stop();
}
