using LocalLlm.Api.Models;

namespace LocalLlm.Api.Services;

public interface IOllamaClient
{
    Task<GenerateResponse?> GenerateAsync(
        string prompt,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> GenerateStreamAsync(
    string prompt,
    CancellationToken cancellationToken = default);
}