namespace LocalLlm.Api.Exceptions;

public sealed class OllamaTimeoutException(string message)
    : Exception(message);