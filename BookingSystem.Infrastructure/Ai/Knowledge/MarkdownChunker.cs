using System.Text;
using System.Text.RegularExpressions;

namespace BookingSystem.Ai.Knowledge;

public sealed record MarkdownDocument(string DocumentId, string Category, string Title, IReadOnlyList<MarkdownChunk> Chunks);

/// <param name="Index">1-based position in the document (part of the stable key <c>doc#index</c>).</param>
public sealed record MarkdownChunk(int Index, string Title, string Content, int TokenCount);

/// <summary>
/// Splits a knowledge-base markdown file into retrieval chunks:
/// one chunk per "##" section; sections longer than the token limit are split by paragraphs
/// with a one-sentence overlap between consecutive parts.
/// Expected format: YAML-like frontmatter with <c>category</c> (and optional <c>title</c>), then "# Title".
/// </summary>
public static partial class MarkdownChunker
{
    public static MarkdownDocument Parse(string documentId, string markdown, int maxTokens = 350)
    {
        var text = markdown.Replace("\r\n", "\n").TrimStart('﻿');
        var (meta, body) = SplitFrontmatter(text);

        var lines = body.Split('\n');
        var docTitle = meta.GetValueOrDefault("title")
            ?? lines.FirstOrDefault(l => l.StartsWith("# "))?[2..].Trim()
            ?? documentId;
        var category = meta.GetValueOrDefault("category") ?? documentId;

        var sections = new List<(string Title, string Content)>();
        string? currentTitle = null;
        var buffer = new StringBuilder();

        void Flush()
        {
            var content = buffer.ToString().Trim();
            if (content.Length > 0)
                sections.Add((currentTitle ?? docTitle, content));
            buffer.Clear();
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("## "))
            {
                Flush();
                currentTitle = line[3..].Trim();
            }
            else if (!line.StartsWith("# "))
            {
                buffer.AppendLine(line);
            }
        }
        Flush();

        var chunks = new List<MarkdownChunk>();
        foreach (var (title, content) in sections)
            foreach (var part in SplitLongSection(content, maxTokens))
                chunks.Add(new MarkdownChunk(chunks.Count + 1, title, part, EstimateTokens(title + " " + part)));

        return new MarkdownDocument(documentId, category, docTitle, chunks);
    }

    /// <summary>
    /// Rough token estimate for Ukrainian text with BPE tokenizers (~2 tokens per word).
    /// Only used to decide where to split.
    /// </summary>
    public static int EstimateTokens(string text) =>
        (int)Math.Ceiling(WordRegex().Matches(text).Count * 2.0);

    private static IEnumerable<string> SplitLongSection(string content, int maxTokens)
    {
        if (EstimateTokens(content) <= maxTokens)
        {
            yield return content;
            yield break;
        }

        var paragraphs = Regex.Split(content, @"\n\s*\n").Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        var current = new List<string>();

        foreach (var paragraph in paragraphs)
        {
            if (current.Count > 0 && EstimateTokens(string.Join("\n\n", current.Append(paragraph))) > maxTokens)
            {
                var part = string.Join("\n\n", current);
                yield return part;
                // Overlap: the next part starts with the last sentence of the previous one.
                current = [LastSentence(part)];
            }
            current.Add(paragraph);
        }

        if (current.Count > 0)
            yield return string.Join("\n\n", current);
    }

    private static string LastSentence(string text)
    {
        var sentences = SentenceRegex().Split(text.Trim());
        return sentences.LastOrDefault(s => s.Trim().Length > 0)?.Trim() ?? string.Empty;
    }

    private static (Dictionary<string, string> Meta, string Body) SplitFrontmatter(string text)
    {
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!text.StartsWith("---\n")) return (meta, text);

        var end = text.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (end < 0) return (meta, text);

        foreach (var line in text[4..end].Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon > 0)
                meta[line[..colon].Trim()] = line[(colon + 1)..].Trim().Trim('"');
        }

        var bodyStart = text.IndexOf('\n', end + 1);
        return (meta, bodyStart < 0 ? string.Empty : text[(bodyStart + 1)..]);
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"(?<=[.!?…])\s+")]
    private static partial Regex SentenceRegex();
}
