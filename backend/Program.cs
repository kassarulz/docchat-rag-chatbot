using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using RagChat.Api.Rag;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<DocumentStore>();
builder.Services.AddSingleton(_ => new AnthropicClient()); // reads ANTHROPIC_API_KEY
builder.Services.AddSingleton<ChatService>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

var app = builder.Build();

const long MaxUploadBytes = 2 * 1024 * 1024;
string[] allowedExtensions = [".txt", ".md", ".markdown", ".csv", ".json"];
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

// Seed the knowledge base with the sample documents so the demo works out of the box.
var store = app.Services.GetRequiredService<DocumentStore>();
var sampleDir = Path.Combine(app.Environment.ContentRootPath, "SampleDocs");
if (Directory.Exists(sampleDir))
{
    foreach (var file in Directory.EnumerateFiles(sampleDir, "*.md").Order())
        store.Add(Path.GetFileName(file), await File.ReadAllTextAsync(file), isSample: true);
}

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    apiKeyConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")),
}));

app.MapGet("/api/documents", (DocumentStore docs) => docs.List());

// Retrieval only, no model call: handy for checking which chunks a question would pull in.
app.MapGet("/api/search", (string q, DocumentStore docs) =>
    docs.Search(q, 5).Select(h => new
    {
        document = h.Chunk.DocumentName,
        chunk = h.Chunk.Index + 1,
        score = Math.Round(h.Score, 2),
        excerpt = ChatService.Excerpt(h.Chunk.Text),
    }));

app.MapPost("/api/documents", async (IFormFileCollection files, DocumentStore docs) =>
{
    if (files.Count == 0) return Results.BadRequest(new { error = "No files were uploaded." });

    var added = new List<DocumentInfo>();
    foreach (var file in files)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(ext))
            return Results.BadRequest(new { error = $"{file.FileName}: only {string.Join(", ", allowedExtensions)} files are supported." });
        if (file.Length > MaxUploadBytes)
            return Results.BadRequest(new { error = $"{file.FileName} is larger than 2 MB." });

        using var reader = new StreamReader(file.OpenReadStream());
        var text = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(text))
            return Results.BadRequest(new { error = $"{file.FileName} is empty." });

        added.Add(docs.Add(Path.GetFileName(file.FileName), text));
    }
    return Results.Ok(added);
}).DisableAntiforgery();

app.MapDelete("/api/documents/{id}", (string id, DocumentStore docs) =>
    docs.Remove(id) ? Results.NoContent() : Results.NotFound());

// Streams the answer as Server-Sent Events: one "sources" event, then "delta" text chunks,
// optional "notice"/"error" events, and a final "done".
app.MapPost("/api/chat", async (ChatRequest request, ChatService chat, HttpContext http, ILogger<Program> logger) =>
{
    if (request.Messages is not { Count: > 0 } || request.Messages[^1].Role != "user" ||
        string.IsNullOrWhiteSpace(request.Messages[^1].Content))
    {
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        await http.Response.WriteAsJsonAsync(new { error = "The last message must be a non-empty user message." });
        return;
    }

    http.Response.ContentType = "text/event-stream";
    http.Response.Headers.CacheControl = "no-cache";
    var ct = http.RequestAborted;

    async Task Send(string eventName, object data)
    {
        await http.Response.WriteAsync($"event: {eventName}\ndata: {JsonSerializer.Serialize(data, jsonOptions)}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }

    try
    {
        await foreach (var evt in chat.StreamAnswerAsync(request.Messages, ct))
        {
            switch (evt)
            {
                case SourcesEvent s: await Send("sources", s.Sources); break;
                case DeltaEvent d: await Send("delta", new { text = d.Text }); break;
                case NoticeEvent n: await Send("notice", new { message = n.Message }); break;
            }
        }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        return; // the browser stopped the request
    }
    catch (AnthropicUnauthorizedException)
    {
        await Send("error", new { message = "The Anthropic API key is missing or invalid. Set ANTHROPIC_API_KEY and restart the backend." });
    }
    catch (AnthropicRateLimitException)
    {
        await Send("error", new { message = "Rate limit reached. Wait a moment and try again." });
    }
    catch (AnthropicApiException ex)
    {
        logger.LogError(ex, "Anthropic API error");
        await Send("error", new { message = "The AI service returned an error. Please try again." });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Chat request failed");
        await Send("error", new { message = "Something went wrong while generating the answer." });
    }

    await Send("done", new { });
});

app.Run();

public sealed record ChatRequest(List<ChatTurn> Messages);
