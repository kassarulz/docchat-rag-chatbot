using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Anthropic;
using Anthropic.Models.Beta.Messages;

namespace RagChat.Api.Rag;

public sealed record ChatTurn(string Role, string Content);

public sealed record SourceRef(int Number, string DocumentId, string DocumentName, int ChunkIndex, string Excerpt, double Score);

/// <summary>Events streamed back to the browser over Server-Sent Events.</summary>
public abstract record ChatEvent;
public sealed record SourcesEvent(IReadOnlyList<SourceRef> Sources) : ChatEvent;
public sealed record DeltaEvent(string Text) : ChatEvent;
public sealed record NoticeEvent(string Message) : ChatEvent;

public sealed class ChatService(AnthropicClient client, DocumentStore store, ILogger<ChatService> logger)
{
    private const string Model = "claude-opus-5-5";
    private const int TopK = 5;

    // Kept byte-stable across requests so it stays cacheable; per-request sources go in the user turn.
    private const string SystemPrompt = """
        You are a helpful assistant that answers questions using the user's own documents.

        Each question comes with numbered sources retrieved from those documents. Base your answer on
        the sources. After each claim, cite the source it came from in square brackets, like [1] or [2][3].
        If the sources don't contain the answer, say so plainly and suggest what document might help,
        rather than guessing or filling gaps from general knowledge.

        Keep answers concise and well structured. Use short paragraphs or bullet points, and Markdown
        where it helps readability.
        """;

    public async IAsyncEnumerable<ChatEvent> StreamAnswerAsync(
        IReadOnlyList<ChatTurn> history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var question = history.LastOrDefault(t => t.Role == "user")?.Content ?? "";

        // Retrieve on the latest question plus the previous user turn, so follow-ups like
        // "what about in winter?" still find the right chunks.
        var previousQuestion = history.Where(t => t.Role == "user").SkipLast(1).LastOrDefault()?.Content ?? "";
        var hits = store.Search($"{question} {previousQuestion}", TopK);

        var sources = hits
            .Select((h, i) => new SourceRef(i + 1, h.Chunk.DocumentId, h.Chunk.DocumentName, h.Chunk.Index,
                Excerpt(h.Chunk.Text), Math.Round(h.Score, 2)))
            .ToList();
        yield return new SourcesEvent(sources);

        var messages = history
            .SkipLast(1)
            .Select(t => t.Role == "assistant"
                ? (BetaMessageParam)new BetaAssistantMessageParam { Content = t.Content }
                : new BetaUserMessageParam { Content = t.Content })
            .ToList();
        messages.Add(new BetaUserMessageParam { Content = BuildGroundedQuestion(question, hits) });

        var parameters = new MessageCreateParams
        {
            Model = Model,
            MaxTokens = 16000,
            System = SystemPrompt,
            OutputConfig = new BetaOutputConfig { Effort = Effort.Low },
            // Server-side fallback: if a safety classifier declines, the request is re-served
            // by a fallback model inside the same call instead of simply stopping.
            Betas = ["server-side-fallback-2026-07-01"],
            Fallbacks = new Default(),
            Messages = messages,
        };

        string? stopReason = null;
        await foreach (var streamEvent in client.Beta.Messages.CreateStreaming(parameters, cancellationToken))
        {
            if (streamEvent.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var text))
            {
                yield return new DeltaEvent(text.Text);
            }
            else if (streamEvent.TryPickDelta(out var messageDelta) && messageDelta.Delta.StopReason is { } reason)
            {
                stopReason = reason.ToString();
            }
        }

        logger.LogInformation("Answered with {SourceCount} sources, stop reason {StopReason}", sources.Count, stopReason);

        if (stopReason is not null && stopReason.Contains("refusal", StringComparison.OrdinalIgnoreCase))
            yield return new NoticeEvent("The model declined to answer this request.");
        else if (stopReason is not null && stopReason.Contains("max_tokens", StringComparison.OrdinalIgnoreCase))
            yield return new NoticeEvent("The answer was cut off because it reached the length limit.");
    }

    private static string BuildGroundedQuestion(string question, IReadOnlyList<SearchHit> hits)
    {
        var sb = new StringBuilder();
        if (hits.Count == 0)
        {
            sb.AppendLine("<sources>No relevant passages were found in the uploaded documents.</sources>");
        }
        else
        {
            sb.AppendLine("<sources>");
            for (var i = 0; i < hits.Count; i++)
            {
                var c = hits[i].Chunk;
                sb.AppendLine($"<source number=\"{i + 1}\" document=\"{c.DocumentName}\">");
                sb.AppendLine(c.Text);
                sb.AppendLine("</source>");
            }
            sb.AppendLine("</sources>");
        }
        sb.AppendLine();
        sb.Append("Question: ").Append(question);
        return sb.ToString();
    }

    // Plain-text preview for the sources panel: drops Markdown headings, emphasis and table rules.
    public static string Excerpt(string text)
    {
        var plain = Regex.Replace(text, @"^#{1,6}\s*|\*\*|__|^\|?[-:| ]+\|?$", "", RegexOptions.Multiline);
        plain = Regex.Replace(plain.Replace('|', ' '), @"\s+", " ").Trim();
        return plain.Length <= 240 ? plain : plain[..240].TrimEnd() + "…";
    }
}
