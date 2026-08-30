using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace NexusLauncher.App.Services;

internal interface IOllamaOwnedProcess : IDisposable
{
    bool HasExited { get; }
    void Kill(bool entireProcessTree);
    Task WaitForExitAsync(CancellationToken cancellationToken);
}

internal interface IOllamaProcessLauncher
{
    IOllamaOwnedProcess Start(ProcessStartInfo startInfo);
}

internal sealed class SystemOllamaProcessLauncher : IOllamaProcessLauncher
{
    public IOllamaOwnedProcess Start(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (!Path.IsPathFullyQualified(startInfo.FileName) ||
            !string.Equals(Path.GetFileName(startInfo.FileName), "ollama.exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(startInfo.FileName))
        {
            throw new FileNotFoundException("A trusted Ollama installation was not found.", startInfo.FileName);
        }

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Ollama did not start.");
        return new SystemOllamaOwnedProcess(process);
    }

    private sealed class SystemOllamaOwnedProcess(Process process) : IOllamaOwnedProcess
    {
        public bool HasExited => process.HasExited;
        public void Kill(bool entireProcessTree) => process.Kill(entireProcessTree);
        public Task WaitForExitAsync(CancellationToken cancellationToken) => process.WaitForExitAsync(cancellationToken);
        public void Dispose() => process.Dispose();
    }
}

/// <summary>
/// Owns an isolated Ollama server process that can only listen on a randomly
/// selected IPv4 loopback port and has all Ollama cloud features disabled.
/// </summary>
public sealed class OllamaProcessManager : IAsyncDisposable
{
    private const int MaximumVersionResponseBytes = 4 * 1024;
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdownCancellation = new();
    private readonly object _disposeSync = new();
    private readonly IOllamaProcessLauncher _launcher;
    private readonly Func<string?> _executableResolver;
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _startupTimeout;
    private IOllamaOwnedProcess? _ownedProcess;
    private Uri? _endpoint;
    private Task? _disposeTask;
    private int _shutdownStarted;

    public OllamaProcessManager()
        : this(new SystemOllamaProcessLauncher(), ResolveInstalledExecutable, SharedHttpClient, TimeSpan.FromSeconds(12))
    {
    }

    internal OllamaProcessManager(
        IOllamaProcessLauncher launcher,
        Func<string?> executableResolver,
        HttpClient httpClient,
        TimeSpan startupTimeout)
    {
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _executableResolver = executableResolver ?? throw new ArgumentNullException(nameof(executableResolver));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(startupTimeout, TimeSpan.Zero);
        _startupTimeout = startupTimeout;
    }

    internal Uri? Endpoint => Volatile.Read(ref _endpoint);

    internal async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfShuttingDown();
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdownCancellation.Token);
        var operationToken = operationCancellation.Token;
        await _gate.WaitAsync(operationToken).ConfigureAwait(false);
        try
        {
            operationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _ownedProcess) is { HasExited: false } && Endpoint is not null) return true;
            await StopOwnedProcessAsync().ConfigureAwait(false);

            var executable = _executableResolver();
            if (string.IsNullOrWhiteSpace(executable) ||
                !Path.IsPathFullyQualified(executable) ||
                !string.Equals(Path.GetFileName(executable), "ollama.exe", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var port = SelectRandomLoopbackPort();
            var endpoint = new Uri($"http://127.0.0.1:{port}/", UriKind.Absolute);
            var startInfo = CreateStartInfo(Path.GetFullPath(executable), port);
            try
            {
                operationToken.ThrowIfCancellationRequested();
                var process = _launcher.Start(startInfo);
                Interlocked.Exchange(ref _ownedProcess, process);
                Volatile.Write(ref _endpoint, endpoint);
                operationToken.ThrowIfCancellationRequested();
                if (await WaitForReadyAsync(endpoint, operationToken).ConfigureAwait(false)) return true;
            }
            catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
            {
                await StopOwnedProcessAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception exception) when (exception is
                FileNotFoundException or
                InvalidOperationException or
                IOException or
                UnauthorizedAccessException or
                Win32Exception)
            {
                // A missing or failed local runtime is an availability state,
                // not an application failure.
            }

            await StopOwnedProcessAsync().ConfigureAwait(false);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Synchronously signals shutdown and terminates the process started by this
    /// manager. It never searches for or terminates any other Ollama process.
    /// </summary>
    internal void BeginShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) == 0)
        {
            _shutdownCancellation.Cancel();
        }

        TerminateOwnedProcessImmediately();
    }

    public ValueTask DisposeAsync()
    {
        Task disposeTask;
        lock (_disposeSync)
        {
            BeginShutdown();
            disposeTask = _disposeTask ??= DisposeCoreAsync();
        }

        return new ValueTask(disposeTask);
    }

    private async Task DisposeCoreAsync()
    {
        if (!await _gate.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false)) return;
        try
        {
            await StopOwnedProcessAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string executablePath, int port)
    {
        if (!Path.IsPathFullyQualified(executablePath) ||
            !string.Equals(Path.GetFileName(executablePath), "ollama.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The Ollama executable path is invalid.", nameof(executablePath));
        }

        if (port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort) throw new ArgumentOutOfRangeException(nameof(port));

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("serve");
        startInfo.Environment["OLLAMA_HOST"] = $"127.0.0.1:{port}";
        startInfo.Environment["OLLAMA_NO_CLOUD"] = "1";
        startInfo.Environment["OLLAMA_KEEP_ALIVE"] = "0";
        return startInfo;
    }

    private async Task<bool> WaitForReadyAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(_startupTimeout);
        while (DateTimeOffset.UtcNow < deadline && _ownedProcess is { HasExited: false })
        {
            using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptTimeout.CancelAfter(TimeSpan.FromMilliseconds(750));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, "api/version"));
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attemptTimeout.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode &&
                    response.Content.Headers.ContentLength is not > MaximumVersionResponseBytes &&
                    IsValidVersionPayload(await ReadLimitedStringAsync(response.Content, MaximumVersionResponseBytes, attemptTimeout.Token).ConfigureAwait(false)))
                {
                    return true;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            catch (HttpRequestException)
            {
            }
            catch (JsonException)
            {
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }

    private async Task StopOwnedProcessAsync()
    {
        var process = Interlocked.Exchange(ref _ownedProcess, null);
        Volatile.Write(ref _endpoint, null);
        if (process is null) return;

        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or
            NotSupportedException or
            OperationCanceledException or
            Win32Exception)
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    private void TerminateOwnedProcessImmediately()
    {
        var process = Interlocked.Exchange(ref _ownedProcess, null);
        Volatile.Write(ref _endpoint, null);
        if (process is null) return;

        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or
            NotSupportedException or
            Win32Exception)
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    private void ThrowIfShuttingDown() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _shutdownStarted) != 0, this);

    private static bool IsValidVersionPayload(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty("version", out var version) &&
            version.ValueKind == JsonValueKind.String &&
            version.GetString() is { Length: > 0 and <= 128 } value &&
            value.All(character => !char.IsControl(character));
    }

    private static int SelectRandomLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string? ResolveInstalledExecutable()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidates = new[]
        {
            Path.Combine(localAppData, "Programs", "Ollama", "ollama.exe"),
            Path.Combine(programFiles, "Ollama", "ollama.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<string> ReadLimitedStringAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[2048];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > maximumBytes) return string.Empty;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static HttpClient CreateHttpClient() => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false
    })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };
}
