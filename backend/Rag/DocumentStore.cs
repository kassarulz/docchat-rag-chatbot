using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace RagChat.Api.Rag;

public sealed record DocumentInfo(string Id, string Name, int Chunks, int Characters, bool IsSample);

public sealed record Chunk(string DocumentId, string DocumentName, int Index, string Text);

public sealed record SearchHit(Chunk Chunk, double Score);

/// <summary>
/// In-memory document store with paragraph-aware chunking and BM25 keyword retrieval.
/// No embeddings service is needed, so the demo runs with a single Anthropic API key.
/// </summary>
public sealed partial class DocumentStore
{
    private const int TargetChunkChars = 900;
    private const int ChunkOverlapChars = 150;
    private const double K1 = 1.4;
    private const double B = 0.75;

    private static readonly HashSet<string> StopWords =
    [
        "a", "an", "and", "are", "as", "at", "be", "but", "by", "can", "do", "does", "for", "from",
        "how", "i", "if", "in", "is", "it", "its", "my", "of", "on", "or", "our", "so", "that", "the",
        "their", "then", "there", "these", "this", "to", "was", "we", "what", "when", "where", "which",
        "who", "why", "will", "with", "you", "your",
    ];

    private readonly ConcurrentDictionary<string, (DocumentInfo Info, List<Chunk> Chunks)> _documents = new();
    private readonly object _indexLock = new();
    private List<(Chunk Chunk, Dictionary<string, int> TermFreq, int Length)> _index = [];
    private Dictionary<string, int> _docFreq = [];
    private double _avgLength;

    public IReadOnlyList<DocumentInfo> List() =>
        _documents.Values.Select(d => d.Info).OrderBy(d => d.IsSample).ThenBy(d => d.Name).ToList();

    public DocumentInfo Add(string name, string text, bool isSample = false)
    {
        var id = Guid.NewGuid().ToString("N")[..12];
        var normalised = text.Replace("\r\n", "\n").Trim();
        var chunks = SplitIntoChunks(normalised)
            .Select((t, i) => new Chunk(id, name, i, t))
            .ToList();

        var info = new DocumentInfo(id, name, chunks.Count, normalised.Length, isSample);
        _documents[id] = (info, chunks);
        RebuildIndex();
        return info;
    }

    public bool Remove(string id)
    {
        var removed = _documents.TryRemove(id, out _);
        if (removed) RebuildIndex();
        return removed;
    }

    public IReadOnlyList<SearchHit> Search(string query, int topK)
    {
        var queryTerms = Tokenize(query).Distinct().ToList();
        if (queryTerms.Count == 0) return [];

        List<(Chunk Chunk, Dictionary<string, int> TermFreq, int Length)> index;
        Dictionary<string, int> docFreq;
        double avgLength;
        lock (_indexLock)
        {
            index = _index;
            docFreq = _docFreq;
            avgLength = _avgLength;
        }

        var n = index.Count;
        var hits = new List<SearchHit>();
        foreach (var entry in index)
        {
            double score = 0;
            foreach (var term in queryTerms)
            {
                if (!entry.TermFreq.TryGetValue(term, out var tf)) continue;
                var df = docFreq.GetValueOrDefault(term);
                var idf = Math.Log(1 + (n - df + 0.5) / (df + 0.5));
                score += idf * (tf * (K1 + 1)) / (tf + K1 * (1 - B + B * entry.Length / avgLength));
            }
            if (score > 0) hits.Add(new SearchHit(entry.Chunk, score));
        }

        return hits.OrderByDescending(h => h.Score).Take(topK).ToList();
    }

    private void RebuildIndex()
    {
        var index = _documents.Values
            .SelectMany(d => d.Chunks)
            .Select(c =>
            {
                var tokens = Tokenize(c.DocumentName + " " + c.Text);
                var tf = tokens.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
                return (Chunk: c, TermFreq: tf, Length: tokens.Count);
            })
            .ToList();

        var docFreq = new Dictionary<string, int>();
        foreach (var entry in index)
            foreach (var term in entry.TermFreq.Keys)
                docFreq[term] = docFreq.GetValueOrDefault(term) + 1;

        lock (_indexLock)
        {
            _index = index;
            _docFreq = docFreq;
            _avgLength = index.Count == 0 ? 1 : index.Average(e => e.Length);
        }
    }

    private static List<string> Tokenize(string text) =>
        WordRegex().Matches(text.ToLowerInvariant())
            .Select(m => Stem(m.Value))
            .Where(t => t.Length > 1 && !StopWords.Contains(t))
            .ToList();

    // Light suffix stripping so "harvests" matches "harvest" and "planting" matches "plant".
    private static string Stem(string word)
    {
        foreach (var suffix in new[] { "ing", "ies", "es", "ed", "s" })
        {
            if (word.Length > suffix.Length + 3 && word.EndsWith(suffix, StringComparison.Ordinal))
                return suffix == "ies" ? word[..^3] + "y" : word[..^suffix.Length];
        }
        return word;
    }

    // Packs paragraphs into ~900-character chunks with a small overlap so answers that span a
    // paragraph break still retrieve together. Headings stay attached to the text below them.
    private static IEnumerable<string> SplitIntoChunks(string text)
    {
        var paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = new List<string>();
        var length = 0;

        foreach (var paragraph in paragraphs)
        {
            if (length > 0 && length + paragraph.Length > TargetChunkChars)
            {
                yield return string.Join("\n\n", current);
                var tail = current[^1];
                current = tail.Length <= ChunkOverlapChars ? [tail] : [];
                length = current.Sum(p => p.Length);
            }
            current.Add(paragraph);
            length += paragraph.Length;
        }

        if (current.Count > 0) yield return string.Join("\n\n", current);
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordRegex();
}
