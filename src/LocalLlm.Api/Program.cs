using LocalLlm.Api.Configuration;
using LocalLlm.Api.Exceptions;
using LocalLlm.Api.Models;
using LocalLlm.Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services
    .AddOptions<OllamaOptions>()
    .Bind(builder.Configuration.GetSection(OllamaOptions.SectionName))
    .Validate(options => Uri.TryCreate(
        options.BaseUrl,
        UriKind.Absolute,
        out _),
        "Ollama:BaseUrl must be a valid absolute URL.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Model),
        "Ollama:Model must be configured.")
    .ValidateOnStart();

builder.Services.AddHttpClient<IOllamaClient, OllamaClient>((services, client) =>
{
    var options = services
        .GetRequiredService<IOptions<OllamaOptions>>()
        .Value;

    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var exception = context.Features
            .Get<IExceptionHandlerFeature>()?
            .Error;

        context.Response.ContentType = "application/problem+json";

        var result = exception switch
        {
            OllamaUnavailableException => Results.Problem(
                title: "AI service unavailable",
                detail: "The local Ollama service could not be reached.",
                statusCode: StatusCodes.Status503ServiceUnavailable),

            OllamaTimeoutException => Results.Problem(
                title: "AI request timed out",
                detail: "The local model took too long to respond.",
                statusCode: StatusCodes.Status504GatewayTimeout),

            OllamaRequestException => Results.Problem(
                title: "AI service error",
                detail: exception.Message,
                statusCode: StatusCodes.Status502BadGateway),

            _ => Results.Problem(
                title: "Unexpected error",
                statusCode: StatusCodes.Status500InternalServerError)
        };

        await result.ExecuteAsync(context);
    });
});



app.MapPost("/api/generate", async (
    PromptRequest request,
    IOllamaClient ollamaClient,
    CancellationToken cancellationToken) =>
{
    var result = await ollamaClient.GenerateAsync(
        request.Prompt,
        cancellationToken);

    return result is null
        ? Results.Problem("Ollama returned no response.")
        : Results.Ok(result);
});
app.MapPost("/api/generate/stream", async (
    PromptRequest request,
    IOllamaClient ollamaClient,
    HttpResponse response,
    CancellationToken cancellationToken) =>
{
    response.ContentType = "text/plain; charset=utf-8";

    try
    {
        await foreach (var chunk in ollamaClient.GenerateStreamAsync(
            request.Prompt,
            cancellationToken))
        {
            await response.WriteAsync(chunk, cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }
    }
    catch (OperationCanceledException)
        when (cancellationToken.IsCancellationRequested)
    {
        // The client disconnected or cancelled the request.
        // Nothing further should be written to the response.
    }
});

app.MapGet("/stream-test", () => Results.Content("""
<!DOCTYPE html>
<html>
<head>
    <title>LLM Streaming Test</title>
</head>
<body>
    <h2>LLM Streaming Test</h2>

    <textarea id="prompt" rows="4" cols="70">Explain dependency injection in three short sentences.</textarea>
    <br><br>

    <button onclick="generate()">Generate</button>

    <pre id="output"></pre>

    <script>
        async function generate() {
            const output = document.getElementById("output");
            output.textContent = "";

            const response = await fetch("/api/generate/stream", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({
                    prompt: document.getElementById("prompt").value
                })
            });

            const reader = response.body.getReader();
            const decoder = new TextDecoder();

            while (true) {
                const { value, done } = await reader.read();

                if (done) {
                    break;
                }

                output.textContent += decoder.decode(value, {
                    stream: true
                });
            }
        }
    </script>
</body>
</html>
""", "text/html"));


app.Run();

