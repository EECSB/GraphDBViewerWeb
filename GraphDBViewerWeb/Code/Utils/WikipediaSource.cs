using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GraphDBViewerWeb.Code;

///<summary>
///Fetches a Wikipedia article's plain text for knowledge-graph generation, via the MediaWiki extracts
///API — the endpoint that serves article prose (SPARQL endpoints like Wikidata serve structured
///triples, not text). Browser-direct like everything else: origin=* makes the API CORS-open. The
///request building and response parsing are pure and unit-tested; the send is thin — the same split as
///the LLM providers.
///</summary>
public static class WikipediaSource
{
    public const string ApiBase = "https://en.wikipedia.org/w/api.php";

    ///<summary>The extracts query for an article title: plain text, redirects followed, CORS-open.</summary>
    public static string BuildExtractUrl(string title)
    {
        return $"{ApiBase}?action=query&prop=extracts&explaintext=1&redirects=1&format=json&origin=*&titles={Uri.EscapeDataString(title.Trim())}";
    }

    ///<summary>The article's extract, or null when the page is missing or empty.</summary>
    public static string ParseExtract(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages))
                return null;

            foreach (var page in pages.EnumerateObject())
            {
                if (page.Value.TryGetProperty("missing", out _))
                    return null;

                if (page.Value.TryGetProperty("extract", out var extract))
                    return extract.GetString();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    ///<summary>Fetches and parses in one step, returning the text or a clear error. Never both.</summary>
    public static async Task<(string Text, string Error)> FetchExtractAsync(HttpClient http, string title, CancellationToken ct)
    {
        try
        {
            var json = await http.GetStringAsync(BuildExtractUrl(title), ct);
            var text = ParseExtract(json);

            if (string.IsNullOrWhiteSpace(text))
                return (null, $"No Wikipedia article found for \"{title.Trim()}\".");

            return (text.Trim(), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (null, $"Wikipedia fetch failed: {ex.Message}");
        }
    }
}
