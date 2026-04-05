using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using CcMiniWpf.Models;

namespace CcMiniWpf.Services;

/// <summary>
/// 管理 cc-mini --stdio 子进程，提供异步事件流。
/// </summary>
public sealed class CcMiniClient : IDisposable
{
    private Process? _process;
    private StreamWriter? _stdin;
    private Channel<CcMiniEvent> _channel = Channel.CreateUnbounded<CcMiniEvent>();
    private CancellationTokenSource? _readCts;
    private int _requestId;
    private volatile bool _disposed;

    public string ExecutablePath { get; set; } = "cc-mini";
    public string ExtraArgs { get; set; } = "";
    public string? WorkingDirectory { get; set; }
    public bool IsRunning => !_disposed && _process is { HasExited: false };

    public event Action<string>? ProcessExited;
    public event Action<CcMiniEvent>? PermissionRequested;

    /// <summary>启动 cc-mini 子进程。</summary>
    public async Task StartAsync()
    {
        if (IsRunning) return;

        // 如果之前有旧进程，先清理
        Stop();
        _channel = Channel.CreateUnbounded<CcMiniEvent>();
        _disposed = false;

        var psi = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            Arguments = $"--stdio {ExtraArgs}".Trim(),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrEmpty(WorkingDirectory))
            psi.WorkingDirectory = WorkingDirectory;

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.Exited += (_, _) =>
        {
            if (_disposed) return;
            ProcessExited?.Invoke(
                _process.ExitCode == 0 ? "进程正常退出" : $"进程异常退出 (code={_process.ExitCode})");
        };

        _process.Start();
        _stdin = _process.StandardInput;
        _stdin.AutoFlush = true;

        _readCts = new CancellationTokenSource();
        _ = Task.Run(() => ReadStdoutLoop(_process.StandardOutput, _readCts.Token));

        // 等待 ready 事件（最多 30 秒）
        await WaitForReadyAsync(TimeSpan.FromSeconds(30));
    }

    /// <summary>发送 submit 请求，返回事件流。</summary>
    public async IAsyncEnumerable<CcMiniEvent> SubmitAsync(
        string prompt,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var id = NextId();
        await SendAsync(new { id, method = "submit", @params = new { prompt } });

        await foreach (var ev in _channel.Reader.ReadAllAsync(ct))
        {
            if (ev.Id != id && ev.Event != "permission_request") continue;
            yield return ev;
            if (ev.Event is "done" or "error" && ev.Id == id) yield break;
        }
    }

    /// <summary>发送中断请求。</summary>
    public async Task AbortAsync()
    {
        if (!IsRunning) return;
        await SendAsync(new { id = NextId(), method = "abort" });
    }

    /// <summary>回复权限请求。</summary>
    public async Task RespondPermissionAsync(bool allow, bool always = false)
    {
        await SendAsync(new
        {
            id = NextId(),
            method = "permission_response",
            @params = new { allow, always }
        });
    }

    /// <summary>获取费用信息。</summary>
    public async Task<CcMiniEvent?> GetCostAsync()
    {
        var id = NextId();
        await SendAsync(new { id, method = "get_cost" });
        await foreach (var ev in _channel.Reader.ReadAllAsync())
        {
            if (ev.Id == id) return ev;
        }
        return null;
    }

    /// <summary>停止子进程但不标记为 disposed。</summary>
    public void Stop()
    {
        _readCts?.Cancel();
        _readCts = null;
        _channel.Writer.TryComplete();

        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch { }
            _process.Dispose();
            _process = null;
        }
        _stdin = null;
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }

    // ── 内部方法 ──

    private string NextId() => Interlocked.Increment(ref _requestId).ToString();

    private async Task SendAsync(object request)
    {
        if (_stdin is null || _disposed) return;
        try
        {
            var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            await _stdin.WriteLineAsync(json);
        }
        catch (Exception) when (_disposed)
        {
            // 关闭时忽略写入错误
        }
    }

    private async Task ReadStdoutLoop(StreamReader reader, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var ev = JsonSerializer.Deserialize<CcMiniEvent>(line);
                    if (ev is null) continue;

                    if (ev.Event == "permission_request")
                    {
                        PermissionRequested?.Invoke(ev);
                        continue;
                    }

                    await _channel.Writer.WriteAsync(ev, ct);
                }
                catch (JsonException)
                {
                    // 忽略非 JSON 行
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) when (ct.IsCancellationRequested || _disposed) { }
        finally
        {
            _channel.Writer.TryComplete();
        }
    }

    private async Task WaitForReadyAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await foreach (var ev in _channel.Reader.ReadAllAsync(cts.Token))
            {
                if (ev.Event == "ready") return;
            }
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("cc-mini 未在规定时间内就绪");
        }
    }
}
