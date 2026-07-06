using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace lunsyn;

public class ScreenCaptureManager : IDisposable
{
    public event Action<byte[]>? FrameCaptured;
    public bool IsCapturing { get; private set; }

    private CancellationTokenSource? _cts;
    private readonly int _targetFps = 30;
    private readonly long _jpegQuality = 75L;

    public async Task StartCaptureAsync()
    {
        if (IsCapturing) return;
        IsCapturing = true;
        _cts = new CancellationTokenSource();

        await Task.Run(() => CaptureLoop(_cts.Token));
    }

    private void CaptureLoop(CancellationToken ct)
    {
        var frameInterval = 1000 / _targetFps;
        var screen = Screen.PrimaryScreen!;
        var bounds = screen.Bounds;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var bitmap = new Bitmap(bounds.Width, bounds.Height);
                using var g = Graphics.FromImage(bitmap);
                g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);

                using var ms = new MemoryStream();
                var jpegEncoder = ImageCodecInfo.GetImageEncoders()
                    .First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(
                    System.Drawing.Imaging.Encoder.Quality, _jpegQuality);
                bitmap.Save(ms, jpegEncoder, encoderParams);

                FrameCaptured?.Invoke(ms.ToArray());
            }
            catch { break; }

            Thread.Sleep(frameInterval);
        }
    }

    public void StopCapture()
    {
        IsCapturing = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => StopCapture();
}
