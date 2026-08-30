using NexusLauncher.App.Models;

namespace NexusLauncher.App.Services;

public sealed record AiMetadataSuggestionOutcome(AiMetadataLookupResult? Suggestion, string Message)
{
    public bool Succeeded => Suggestion is not null;
}

/// <summary>
/// Applies privacy, consent, and local quota rules before any optional Nexus AI
/// gateway call. It never updates a library entry itself; callers must ask the
/// user to review and approve suggestions first.
/// </summary>
public sealed class AiMetadataCoordinator
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly NexusAiGatewayClient _gateway;

    public AiMetadataCoordinator(AppSettings settings, SettingsService settingsService, NexusAiGatewayClient gateway)
    {
        _settings = settings;
        _settingsService = settingsService;
        _gateway = gateway;
    }

    public bool CanRequest => _settings.EnableAiMetadata && _gateway.IsConfigured;
    public event Action? UsageChanged;

    public async Task<AiMetadataSuggestionOutcome> SuggestAsync(LibraryItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!_settings.EnableAiMetadata)
        {
            return new AiMetadataSuggestionOutcome(null, "AI metadata suggestions are turned off in Settings.");
        }

        if (!_gateway.IsConfigured)
        {
            return new AiMetadataSuggestionOutcome(null, "Nexus AI is not configured in this build. No library metadata was sent.");
        }

        NormalizeMonthlyUsage();
        if (_settings.AiRequestsThisMonth >= _settings.AiMonthlyRequestLimit)
        {
            return new AiMetadataSuggestionOutcome(null, $"Your local AI request limit of {_settings.AiMonthlyRequestLimit} for this month has been reached.");
        }

        var request = AiMetadataRequestFactory.Create(item);
        var response = await _gateway.LookupMetadataAsync(request, cancellationToken);
        if (!response.Succeeded)
        {
            return new AiMetadataSuggestionOutcome(null, DescribeFailure(response.Status));
        }

        _settings.AiRequestsThisMonth++;
        await _settingsService.SaveAsync(_settings);
        UsageChanged?.Invoke();
        return new AiMetadataSuggestionOutcome(response.Result, "Nexus AI returned reviewable metadata suggestions.");
    }

    /// <summary>
    /// Updates only descriptive fields after an explicit user approval. Launch
    /// paths, URIs, arguments, provider identity, and user-authored description
    /// are never overwritten by this method.
    /// </summary>
    public static int ApplyApprovedSuggestion(LibraryItem item, AiMetadataLookupResult suggestion)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(suggestion);

        var updates = 0;
        if (string.IsNullOrWhiteSpace(item.Description) && !string.IsNullOrWhiteSpace(suggestion.Description))
        {
            item.Description = suggestion.Description.Trim();
            updates++;
        }

        var mergedTags = item.Tags.ToList();
        foreach (var suggestionTag in suggestion.Genres.Concat(suggestion.Tags))
        {
            var tag = suggestionTag?.Trim();
            if (string.IsNullOrWhiteSpace(tag) || mergedTags.Contains(tag, StringComparer.OrdinalIgnoreCase)) continue;
            if (mergedTags.Count >= 30) break;
            mergedTags.Add(tag);
            updates++;
        }

        if (updates > 0) item.Tags = mergedTags;
        return updates;
    }

    private void NormalizeMonthlyUsage()
    {
        var month = DateTimeOffset.UtcNow.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        if (string.Equals(_settings.AiUsageMonth, month, StringComparison.Ordinal)) return;
        _settings.AiUsageMonth = month;
        _settings.AiRequestsThisMonth = 0;
    }

    private static string DescribeFailure(AiGatewayLookupStatus status) => status switch
    {
        AiGatewayLookupStatus.NotConnected => "Sign in to Nexus AI in Settings before requesting metadata suggestions.",
        AiGatewayLookupStatus.RateLimited => "Nexus AI is temporarily rate limited. Try again later.",
        AiGatewayLookupStatus.RequestRejected => "Nexus did not send this metadata request because its local safety checks rejected it.",
        AiGatewayLookupStatus.InvalidResponse => "Nexus AI returned data Nexus could not safely use.",
        AiGatewayLookupStatus.Unavailable => "Nexus AI is unavailable right now. Your local library was not changed.",
        _ => "Nexus AI metadata suggestions are unavailable."
    };
}
