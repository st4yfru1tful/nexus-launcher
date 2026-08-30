using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using NexusLauncher.App.Models;
using NexusLauncher.App.Services;

namespace NexusLauncher.UnitTests.App;

public sealed class OllamaLocalMetadataProviderTests
{
    private const string LocalModelDigest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task Process_manager_starts_owned_runtime_on_random_ipv4_loopback_with_cloud_disabled()
    {
        var handler = new RouteHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/version" => JsonResponse("""{ "version": "0.12.6" }"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var httpClient = new HttpClient(handler);
        var launcher = new RecordingProcessLauncher();
        await using var manager = new OllamaProcessManager(
            launcher,
            () => @"C:\Program Files\Ollama\ollama.exe",
            httpClient,
            TimeSpan.FromSeconds(1));

        var started = await manager.StartAsync();

        Assert.True(started);
        var startInfo = Assert.IsType<ProcessStartInfo>(launcher.StartInfo);
        Assert.Equal(@"C:\Program Files\Ollama\ollama.exe", startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(["serve"], startInfo.ArgumentList);
        Assert.Equal("1", startInfo.Environment["OLLAMA_NO_CLOUD"]);
        Assert.Equal("0", startInfo.Environment["OLLAMA_KEEP_ALIVE"]);
        Assert.StartsWith("127.0.0.1:", startInfo.Environment["OLLAMA_HOST"], StringComparison.Ordinal);
        Assert.NotNull(manager.Endpoint);
        Assert.Equal("127.0.0.1", manager.Endpoint.Host);
        Assert.False(manager.Endpoint.IsDefaultPort);

        await manager.DisposeAsync();

        Assert.True(launcher.Process.KillCalled);
        Assert.True(launcher.Process.KillEntireTree);
        Assert.True(launcher.Process.Disposed);
    }

    [Fact]
    public async Task Provider_uses_only_downloaded_local_model_and_sends_bounded_structured_request()
    {
        string? generateBody = null;
        var requestedPaths = new List<string>();
        var handler = new RouteHandler(async (request, cancellationToken) =>
        {
            requestedPaths.Add(request.RequestUri?.AbsolutePath ?? string.Empty);
            switch (request.RequestUri?.AbsolutePath)
            {
                case "/api/version":
                    return JsonResponse("""{ "version": "0.12.6" }""");
                case "/api/tags":
                    return JsonResponse($$"""
                        {
                          "models": [
                            { "name": "gpt-oss:120b-cloud", "model": "gpt-oss:120b-cloud", "size": 100, "digest": "{{LocalModelDigest}}" },
                            { "name": "gemma3:4b", "model": "gemma3:4b", "size": 3338801804, "digest": "{{LocalModelDigest}}" }
                          ]
                        }
                        """);
                case "/api/show":
                    return JsonResponse("""{ "capabilities": ["completion"] }""");
                case "/api/generate":
                    generateBody = request.Content is null
                        ? null
                        : await request.Content.ReadAsStringAsync(cancellationToken);
                    return JsonResponse("""
                        {
                          "done": true,
                          "response": "{\"canonicalTitle\":\"Hades\",\"description\":\"An action roguelike.\",\"genres\":[\"Action\"],\"tags\":[\"Roguelike\"],\"confidence\":0.93}"
                        }
                        """);
                default:
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        });
        using var httpClient = new HttpClient(handler);
        var launcher = new RecordingProcessLauncher();
        await using var manager = CreateManager(launcher, httpClient);
        await using var provider = new OllamaLocalMetadataProvider(manager, httpClient, "gemma3:4b");

        var response = await provider.LookupMetadataAsync(new AiMetadataLookupRequest
        {
            Title = "Hades",
            Provider = "Steam",
            Publisher = "Supergiant Games",
            ExecutableFileName = "Hades.exe",
            ParentFolderName = "x64"
        });

        Assert.True(response.Succeeded);
        Assert.Equal("Hades", response.Result!.CanonicalTitle);
        Assert.DoesNotContain("/api/pull", requestedPaths);
        Assert.NotNull(generateBody);
        using var body = JsonDocument.Parse(generateBody);
        var root = body.RootElement;
        Assert.Equal("gemma3:4b", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.False(root.GetProperty("think").GetBoolean());
        Assert.Equal(0, root.GetProperty("keep_alive").GetInt32());
        Assert.False(root.TryGetProperty("tools", out _));
        Assert.False(root.TryGetProperty("web_search", out _));
        Assert.False(root.GetProperty("format").GetProperty("additionalProperties").GetBoolean());
        var prompt = root.GetProperty("prompt").GetString();
        Assert.Contains("Hades.exe", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecutablePath", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InstallPath", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LaunchArguments", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Provider_rejects_cloud_model_before_starting_runtime()
    {
        var handler = new RouteHandler(_ => throw new InvalidOperationException("No HTTP request was expected."));
        using var httpClient = new HttpClient(handler);
        var launcher = new RecordingProcessLauncher();
        await using var manager = CreateManager(launcher, httpClient);
        await using var provider = new OllamaLocalMetadataProvider(manager, httpClient, "gpt-oss:120b-cloud");

        var availability = await provider.GetAvailabilityAsync();

        Assert.Equal(AiMetadataProviderState.NoLocalModel, availability.State);
        Assert.Equal(0, launcher.StartCount);
        Assert.True(OllamaLocalMetadataProvider.IsCloudModelName("registry.example/cloud/model"));
        Assert.True(OllamaLocalMetadataProvider.IsCloudModelName("model:CLOUD"));
    }

    [Fact]
    public async Task Provider_reports_no_model_when_runtime_has_no_downloaded_local_model()
    {
        var handler = new RouteHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/version" => JsonResponse("""{ "version": "0.12.6" }"""),
            "/api/tags" => JsonResponse($$"""
                { "models": [{ "model": "qwen:cloud", "size": 100, "digest": "{{LocalModelDigest}}" }] }
                """),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var httpClient = new HttpClient(handler);
        var launcher = new RecordingProcessLauncher();
        await using var manager = CreateManager(launcher, httpClient);
        await using var provider = new OllamaLocalMetadataProvider(manager, httpClient);

        var availability = await provider.GetAvailabilityAsync();
        var lookup = await provider.LookupMetadataAsync(new AiMetadataLookupRequest { Title = "Hades" });

        Assert.Equal(AiMetadataProviderState.NoLocalModel, availability.State);
        Assert.Equal(AiGatewayLookupStatus.LocalModelUnavailable, lookup.Status);
    }

    [Fact]
    public async Task Provider_rejects_unsafe_request_without_starting_runtime()
    {
        var handler = new RouteHandler(_ => throw new InvalidOperationException("No HTTP request was expected."));
        using var httpClient = new HttpClient(handler);
        var launcher = new RecordingProcessLauncher();
        await using var manager = CreateManager(launcher, httpClient);
        await using var provider = new OllamaLocalMetadataProvider(manager, httpClient);

        var response = await provider.LookupMetadataAsync(new AiMetadataLookupRequest { Title = new string('x', 257) });

        Assert.Equal(AiGatewayLookupStatus.RequestRejected, response.Status);
        Assert.Equal(0, launcher.StartCount);
    }

    [Fact]
    public async Task Provider_rejects_oversized_or_untrusted_generation_response()
    {
        var handler = new RouteHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/version" => JsonResponse("""{ "version": "0.12.6" }"""),
            "/api/tags" => JsonResponse($$"""
                { "models": [{ "model": "gemma3:4b", "size": 3338801804, "digest": "{{LocalModelDigest}}" }] }
                """),
            "/api/show" => JsonResponse("""{ "capabilities": ["completion"] }"""),
            "/api/generate" => JsonResponse(new string('x', 129 * 1024)),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var httpClient = new HttpClient(handler);
        var launcher = new RecordingProcessLauncher();
        await using var manager = CreateManager(launcher, httpClient);
        await using var provider = new OllamaLocalMetadataProvider(manager, httpClient);

        var response = await provider.LookupMetadataAsync(new AiMetadataLookupRequest { Title = "Hades" });

        Assert.Equal(AiGatewayLookupStatus.InvalidResponse, response.Status);
    }

    [Fact]
    public async Task Provider_propagates_cancellation_and_disposes_owned_process()
    {
        var generationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RouteHandler(async (request, cancellationToken) =>
        {
            return request.RequestUri?.AbsolutePath switch
            {
                "/api/version" => JsonResponse("""{ "version": "0.12.6" }"""),
                "/api/tags" => JsonResponse($$"""
                    { "models": [{ "model": "gemma3:4b", "size": 3338801804, "digest": "{{LocalModelDigest}}" }] }
                    """),
                "/api/show" => JsonResponse("""{ "capabilities": ["completion"] }"""),
                "/api/generate" => await WaitForCancellationAsync(generationStarted, cancellationToken),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });
        using var httpClient = new HttpClient(handler);
        var launcher = new RecordingProcessLauncher();
        await using var manager = CreateManager(launcher, httpClient);
        var provider = new OllamaLocalMetadataProvider(manager, httpClient);
        using var cancellation = new CancellationTokenSource();

        var lookup = provider.LookupMetadataAsync(new AiMetadataLookupRequest { Title = "Hades" }, cancellation.Token);
        await generationStarted.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lookup);
        await provider.DisposeAsync();
        Assert.True(launcher.Process.KillCalled);
    }

    [Fact]
    public void Installed_model_parser_filters_embedding_only_and_cloud_models()
    {
        var parsed = OllamaLocalMetadataProvider.TryParseInstalledModels($$"""
            {
              "models": [
                { "model": "nomic-embed-text:latest", "size": 274000000, "digest": "{{LocalModelDigest}}", "details": { "family": "nomic-bert" } },
                { "model": "custom-vectors:latest", "size": 275000000, "digest": "{{LocalModelDigest}}", "details": { "families": ["bert"] } },
                { "model": "qwen3:8b-cloud", "size": 100, "digest": "{{LocalModelDigest}}" },
                { "model": "llama3.2:3b", "size": 2010000000, "digest": "{{LocalModelDigest}}", "details": { "family": "llama" } }
              ]
            }
            """, out var models);

        Assert.True(parsed);
        Assert.Equal(["llama3.2:3b"], models);
        Assert.True(OllamaLocalMetadataProvider.IsLikelyEmbeddingOnlyModel("bge-m3:latest"));
        Assert.True(OllamaLocalMetadataProvider.IsLikelyEmbeddingOnlyModel("custom", ["bert"]));
    }

    [Fact]
    public async Task Provider_skips_models_without_generation_capability_and_selects_supported_chat_model()
    {
        var shownModels = new List<string>();
        var handler = new RouteHandler(async (request, cancellationToken) =>
        {
            switch (request.RequestUri?.AbsolutePath)
            {
                case "/api/version":
                    return JsonResponse("""{ "version": "0.12.6" }""");
                case "/api/tags":
                    return JsonResponse($$"""
                        {
                          "models": [
                            { "model": "zeta-chat:latest", "size": 500000000, "digest": "{{LocalModelDigest}}" },
                            { "model": "llama3.2:3b", "size": 2010000000, "digest": "{{LocalModelDigest}}", "details": { "family": "llama" } },
                            { "model": "gemma3:4b", "size": 3300000000, "digest": "{{LocalModelDigest}}", "details": { "family": "gemma3" } }
                          ]
                        }
                        """);
                case "/api/show":
                    var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                    using (var document = JsonDocument.Parse(body))
                    {
                        var model = document.RootElement.GetProperty("model").GetString()!;
                        shownModels.Add(model);
                        return model.StartsWith("gemma3", StringComparison.Ordinal)
                            ? JsonResponse("""{ "capabilities": ["embedding"] }""")
                            : JsonResponse("""{ "capabilities": ["completion", "tools"] }""");
                    }
                default:
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        });
        using var httpClient = new HttpClient(handler);
        var launcher = new RecordingProcessLauncher();
        await using var manager = CreateManager(launcher, httpClient);
        await using var provider = new OllamaLocalMetadataProvider(manager, httpClient);

        var availability = await provider.GetAvailabilityAsync();

        Assert.True(availability.IsReady);
        Assert.Equal("llama3.2:3b", availability.ModelName);
        Assert.Equal(["gemma3:4b", "llama3.2:3b"], shownModels);
    }

    [Fact]
    public async Task Provider_does_not_report_ready_when_only_model_is_embedding_capable()
    {
        var handler = new RouteHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/version" => JsonResponse("""{ "version": "0.12.6" }"""),
            "/api/tags" => JsonResponse($$"""
                { "models": [{ "model": "custom-vector-model", "size": 500000000, "digest": "{{LocalModelDigest}}" }] }
                """),
            "/api/show" => JsonResponse("""{ "capabilities": ["embedding"] }"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var httpClient = new HttpClient(handler);
        var launcher = new RecordingProcessLauncher();
        await using var manager = CreateManager(launcher, httpClient);
        await using var provider = new OllamaLocalMetadataProvider(manager, httpClient);

        var availability = await provider.GetAvailabilityAsync();

        Assert.Equal(AiMetadataProviderState.NoLocalModel, availability.State);
        Assert.Null(availability.ModelName);
        Assert.Contains("text generation", availability.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unsupported_generation_error_is_model_unavailable_not_runtime_unavailable()
    {
        var handler = new RouteHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/version" => JsonResponse("""{ "version": "0.12.6" }"""),
            "/api/tags" => JsonResponse($$"""
                { "models": [{ "model": "llama3.2:3b", "size": 2010000000, "digest": "{{LocalModelDigest}}" }] }
                """),
            "/api/show" => JsonResponse("""{ "capabilities": ["completion"] }"""),
            "/api/generate" => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    """{ "error": "model does not support generate" }""",
                    Encoding.UTF8,
                    "application/json")
            },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var httpClient = new HttpClient(handler);
        var launcher = new RecordingProcessLauncher();
        await using var manager = CreateManager(launcher, httpClient);
        await using var provider = new OllamaLocalMetadataProvider(manager, httpClient);

        var response = await provider.LookupMetadataAsync(new AiMetadataLookupRequest { Title = "Hades" });

        Assert.Equal(AiGatewayLookupStatus.LocalModelUnavailable, response.Status);
        Assert.NotEqual(AiGatewayLookupStatus.LocalRuntimeUnavailable, response.Status);
    }

    [Fact]
    public async Task Disposing_provider_cancels_active_request_and_immediately_kills_only_owned_process()
    {
        var generationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RouteHandler(async (request, cancellationToken) => request.RequestUri?.AbsolutePath switch
        {
            "/api/version" => JsonResponse("""{ "version": "0.12.6" }"""),
            "/api/tags" => JsonResponse($$"""
                { "models": [{ "model": "llama3.2:3b", "size": 2010000000, "digest": "{{LocalModelDigest}}" }] }
                """),
            "/api/show" => JsonResponse("""{ "capabilities": ["completion"] }"""),
            "/api/generate" => await WaitForCancellationAsync(generationStarted, cancellationToken),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var httpClient = new HttpClient(handler);
        var launcher = new RecordingProcessLauncher();
        await using var manager = CreateManager(launcher, httpClient);
        var provider = new OllamaLocalMetadataProvider(manager, httpClient);

        var lookup = provider.LookupMetadataAsync(new AiMetadataLookupRequest { Title = "Hades" });
        await generationStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await provider.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        await provider.DisposeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lookup);
        Assert.Equal(1, launcher.Process.KillCount);
        Assert.True(launcher.Process.KillEntireTree);
        Assert.Equal(1, launcher.Process.DisposeCount);
    }

    private static OllamaProcessManager CreateManager(RecordingProcessLauncher launcher, HttpClient httpClient) =>
        new(launcher, () => @"C:\Program Files\Ollama\ollama.exe", httpClient, TimeSpan.FromSeconds(1));

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static async Task<HttpResponseMessage> WaitForCancellationAsync(
        TaskCompletionSource generationStarted,
        CancellationToken cancellationToken)
    {
        generationStarted.SetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("Cancellation was not observed.");
    }

    private sealed class RouteHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        internal RouteHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this((request, _) => Task.FromResult(handler(request)))
        {
        }

        internal RouteHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }

    private sealed class RecordingProcessLauncher : IOllamaProcessLauncher
    {
        internal RecordingOwnedProcess Process { get; } = new();
        internal ProcessStartInfo? StartInfo { get; private set; }
        internal int StartCount { get; private set; }

        public IOllamaOwnedProcess Start(ProcessStartInfo startInfo)
        {
            StartInfo = startInfo;
            StartCount++;
            return Process;
        }
    }

    private sealed class RecordingOwnedProcess : IOllamaOwnedProcess
    {
        public bool HasExited { get; private set; }
        internal bool KillCalled { get; private set; }
        internal int KillCount { get; private set; }
        internal bool KillEntireTree { get; private set; }
        internal bool Disposed { get; private set; }
        internal int DisposeCount { get; private set; }

        public void Kill(bool entireProcessTree)
        {
            KillCalled = true;
            KillCount++;
            KillEntireTree = entireProcessTree;
            HasExited = true;
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HasExited = true;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Disposed = true;
            DisposeCount++;
        }
    }
}
