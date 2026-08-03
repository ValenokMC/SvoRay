namespace ServiceLib.Models.Dto;

/// <summary>
/// Text returned by a download together with the response headers that accompanied it.
/// </summary>
public sealed class DownloadStringResult(string content, IReadOnlyDictionary<string, string> headers)
{
    public string Content { get; } = content;

    public IReadOnlyDictionary<string, string> Headers { get; } = headers;

    public string? GetHeaderValue(string name)
    {
        return Headers.TryGetValue(name, out var value) ? value : null;
    }

    public static async Task<DownloadStringResult> FromHttpResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var headers = response.Headers
            .Concat(response.Content.Headers)
            .GroupBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => string.Join(",", group.SelectMany(header => header.Value)),
                StringComparer.OrdinalIgnoreCase);

        return new DownloadStringResult(content, headers);
    }
}
