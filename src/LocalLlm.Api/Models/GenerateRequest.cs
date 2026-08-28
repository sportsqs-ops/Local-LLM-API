namespace LocalLlm.Api.Models;

public sealed record GenerateRequest(
    string Model,
    string Prompt,
    bool Stream = false);   