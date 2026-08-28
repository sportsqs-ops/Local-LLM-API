namespace LocalLlm.Api.Exceptions;

public sealed class OllamaRequestException(string message)
    : Exception(message);