using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Zeroconf;

namespace lunsyn;

public class ScreenShareService : IDisposable
{
    public enum ConnectionStateEnum { Disconnected, Connecting, Connected }
    public ConnectionStateEnum ConnectionState { get; private set; } = ConnectionStateEnum.Disconnected;
    public event Action<ConnectionStateEnum>? ConnectionStateChanged;
    public event Action<byte[]>? FrameReceived;

    private TcpListener? _listener;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private bool _running;
    private const int Port = 18889;

    public async Task StartAsync()
    {
        if (ConnectionState != ConnectionStateEnum.Disconnected) return;
        SetState(ConnectionStateEnum.Connecting);
        _running = true;

        var results = await ZeroconfResolver.ResolveAsync("_lunsyn._tcp", TimeSpan.FromSeconds(3));
        var host = results.FirstOrDefault();

        if (host != null)
        {
            await ConnectAsClientAsync(host.IPAddress);
        }
        else
        {
            await StartServerAsync();
        }
    }

    private async Task StartServerAsync()
    {
        _listener = new TcpListener(IPAddress.Any, Port);
        _listener.Start();
        System.Console.WriteLine($"[望月] 屏幕共享服务端已启动 (端口 {Port})");
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

    public async Task SendFrameAsync(byte[] data)
    {
        if (_stream == null || ConnectionState != ConnectionStateEnum.Connected) return;
        try
        {
            var header = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(data.Length));
            await _stream.WriteAsync(header, 0, 4);
            await _stream.WriteAsync(data, 0, data.Length);
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
                if (length <= 0 || length > 10_000_000) break;

                var buffer = new byte[length];
                int total = 0;
                while (total < length)
                {
                    read = await _stream.ReadAsync(buffer, total, length - total);
                    if (read == 0) break;
                    total += read;
                }
                if (total < length) break;
                FrameReceived?.Invoke(buffer);
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
