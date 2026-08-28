namespace LocalLlm.Api.Exceptions;

public sealed class OllamaUnavailableException(
    string message,
    Exception? innerException = null)
    : Exception(message, innerException);