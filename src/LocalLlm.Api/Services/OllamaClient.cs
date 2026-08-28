using LocalLlm.Api.Configuration;
using LocalLlm.Api.Models;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using LocalLlm.Api.Exceptions;


namespace LocalLlm.Api.Services;

public sealed class OllamaClient(
    HttpClient httpClient,
    IOptions<OllamaOptions> options) : IOllamaClient
{
    private readonly OllamaOptions _options = options.Value;
    public async Task<GenerateResponse?> GenerateAsync(
    string prompt,
    CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new GenerateRequest(
                Model: _options.Model,
                Prompt: prompt);

            var response = await httpClient.PostAsJsonAsync(
                "/api/generate",
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new OllamaRequestException(
                    $"Ollama returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            return await response.Content.ReadFromJsonAsync<GenerateResponse>(
                cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OllamaTimeoutException(
                "The Ollama request timed out.");
        }
        catch (HttpRequestException ex)
        {
            throw new OllamaUnavailableException(
                "Ollama is unavailable.",
                ex);
        }
    }



    public async IAsyncEnumerable<string> GenerateStreamAsync(
    string prompt,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    var request = new GenerateRequest(
        Model: "qwen3:1.7b",
        Prompt: prompt,
        Stream: true);

    using var httpRequest = new HttpRequestMessage(
        HttpMethod.Post,
        "/api/generate")
    {
        Content = JsonContent.Create(request)
    };

        HttpResponseMessage response;

        try
        {
            response = await httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (TaskCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new OllamaTimeoutException(
                "The Ollama streaming request timed out.");
        }
        catch (HttpRequestException ex)
        {
            throw new OllamaUnavailableException(
                "Ollama is unavailable.",
                ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new OllamaRequestException(
                    $"Ollama returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            await using var stream =
                await response.Content.ReadAsStreamAsync(cancellationToken);

            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(cancellationToken);

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(line);

                if (document.RootElement.TryGetProperty(
                    "response",
                    out var responseElement))
                {
                    var chunk = responseElement.GetString();

                    if (!string.IsNullOrEmpty(chunk))
                    {
                        yield return chunk;
                    }
                }
            }
        }
    }
}