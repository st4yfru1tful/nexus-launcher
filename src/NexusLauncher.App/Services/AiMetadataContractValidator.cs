using NexusLauncher.App.Models;

namespace NexusLauncher.App.Services;

internal static class AiMetadataContractValidator
{
    internal static bool IsSafeRequest(AiMetadataLookupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return IsSafeValue(request.Title, 256, required: true) &&
            IsSafeValue(request.Provider, 256) &&
            IsSafeValue(request.Publisher, 256) &&
            IsSafeValue(request.Version, 128) &&
            IsSafeValue(request.ExecutableFileName, 260) &&
            IsSafeValue(request.ParentFolderName, 260);
    }

    internal static bool TryNormalizeResult(AiMetadataLookupResult? result, out AiMetadataLookupResult? normalized)
    {
        normalized = null;
        if (result is null ||
            !IsSafeValue(result.CanonicalTitle, 256) ||
            !IsSafeValue(result.Description, 4 * 1024) ||
            result.Confidence is < 0 or > 1)
        {
            return false;
        }

        var genres = NormalizeValues(result.Genres, 12, 64);
        var tags = NormalizeValues(result.Tags, 20, 64);
        if (genres is null || tags is null) return false;

        normalized = new AiMetadataLookupResult
        {
            CanonicalTitle = NormalizeOptional(result.CanonicalTitle),
            Description = NormalizeOptional(result.Description),
            Genres = genres,
            Tags = tags,
            Confidence = result.Confidence
        };
        return normalized.CanonicalTitle is not null ||
            normalized.Description is not null ||
            normalized.Genres.Count > 0 ||
            normalized.Tags.Count > 0;
    }

    private static List<string>? NormalizeValues(IReadOnlyList<string>? values, int maximumCount, int maximumLength)
    {
        if (values is null || values.Count > maximumCount) return null;

        var normalized = new List<string>();
        foreach (var item in values)
        {
            if (!IsSafeValue(item, maximumLength, required: true)) return null;
            var value = item.Trim();
            if (!normalized.Contains(value, StringComparer.OrdinalIgnoreCase)) normalized.Add(value);
        }

        return normalized;
    }

    private static bool IsSafeValue(string? value, int maximumLength, bool required = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return !required;
        return value.Length <= maximumLength && value.All(character => !char.IsControl(character));
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
