using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace GraphDBViewerWeb.Code;

///<summary>The outcome of parsing a model's knowledge-graph JSON: the neutral graph plus every repair
///the parser made, or a clear error — never a silent empty or half graph.</summary>
public sealed class KgParseResult
{
    public GraphImport.Graph Graph { get; }
    public List<string> Warnings { get; }
    public string Error { get; }

    public bool IsError => Error != null;

    private KgParseResult(GraphImport.Graph graph, List<string> warnings, string error)
    {
        Graph = graph;
        Warnings = warnings;
        Error = error;
    }

    public static KgParseResult Success(GraphImport.Graph graph, List<string> warnings)
    {
        return new KgParseResult(graph, warnings, null);
    }

    public static KgParseResult Failure(string error)
    {
        return new KgParseResult(null, new List<string>(), error);
    }
}

///<summary>
///The outcome of folding a generated graph against the canvas for Merge mode: the delta script to
///append (addV for genuinely new entities, addE for new edges, property updates for folded entities
///that gained properties), the per-fold warnings, and the post-merge counts the preview shows.
///</summary>
public sealed class KgMergeResult
{
    public string DeltaGremlin { get; }
    public List<string> Warnings { get; }

    ///<summary>The genuinely new nodes the delta adds — what the preview's label breakdown is built
    ///from, so the modal never re-derives fold membership and drifts from the fold itself.</summary>
    public List<GraphImport.Node> AddedNodes { get; }

    public int NewNodes { get; }
    public int NewEdges { get; }
    public int FoldedNodes { get; }
    public int PropertyUpdates { get; }

    public KgMergeResult(string deltaGremlin, List<string> warnings, List<GraphImport.Node> addedNodes, int newEdges, int foldedNodes, int propertyUpdates)
    {
        DeltaGremlin = deltaGremlin;
        Warnings = warnings;
        AddedNodes = addedNodes;
        NewNodes = addedNodes.Count;
        NewEdges = newEdges;
        FoldedNodes = foldedNodes;
        PropertyUpdates = propertyUpdates;
    }
}

///<summary>
///Turns a model's strict-JSON knowledge-graph response into the neutral <see cref="GraphImport.Graph"/>,
///repairing what can be repaired and reporting every repair as a warning: fence stripping, id dedup,
///the entity fold, auto-created edge endpoints, name backfill and the node/edge caps. Also owns the
///fold's second scope — a generated graph against the canvas, for Merge mode — and the delta emission
///that goes with it, built from the same GremlinQueryBuilder pieces ToGremlin uses so the edit parser
///is known to accept every line. Pure and static; no IO.
///</summary>
public static class KgGraphParser
{
    ///<summary>
    ///Caps on one generation, sized so a full-size result fits the 8192 MaxTokens default rather than
    ///chosen — provisional until real documents are measured (see the spec's open questions). KgPrompt
    ///quotes these same constants to the model, so the ask and the enforcement can't drift.
    ///</summary>
    public const int MaxNodes = 100;
    public const int MaxEdges = 200;

    //Trailing company-form words dropped when normalizing an entity name, so "Acme, Inc." and "Acme"
    //fold. Abbreviations plus their safe full-word forms — but not "company", which is too often the
    //distinguishing token itself. Only ever stripped while at least one other token remains ("Co"
    //alone stays "co").
    private static readonly HashSet<string> CorporateSuffixes = new()
    {
        "inc", "incorporated", "ltd", "limited", "llc", "corp", "corporation", "co", "gmbh", "ag", "plc", "sa", "bv"
    };

    ///<summary>Parses model output into a graph, running every repair and the in-run entity fold.</summary>
    public static KgParseResult Parse(string modelOutput)
    {
        if (string.IsNullOrWhiteSpace(modelOutput))
            return KgParseResult.Failure("The model returned nothing.");

        var text = LlmText.StripMarkdownFences(modelOutput);

        JsonDocument doc;

        try
        {
            doc = JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            return KgParseResult.Failure($"The model's output is not valid JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return KgParseResult.Failure("The model's output is not a JSON object.");

            if (!root.TryGetProperty("nodes", out var nodesEl) || nodesEl.ValueKind != JsonValueKind.Array)
                return KgParseResult.Failure("The model's output has no \"nodes\" array.");

            if (nodesEl.GetArrayLength() > MaxNodes)
                return KgParseResult.Failure($"The model produced {nodesEl.GetArrayLength()} nodes; the cap is {MaxNodes}. Narrow the source text, or extract it in parts.");

            var warnings = new List<string>();
            var graph = new GraphImport.Graph();
            var seenIds = new HashSet<string>();
            int duplicateIds = 0;
            int skippedNodes = 0;
            int missingLabels = 0;

            foreach (var el in nodesEl.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object)
                {
                    skippedNodes++;
                    continue;
                }

                var id = ReadString(el, "id");

                if (string.IsNullOrWhiteSpace(id))
                {
                    skippedNodes++;
                    continue;
                }

                var node = graph.GetOrAdd(id);

                if (!seenIds.Add(id))
                {
                    //Same id twice: first occurrence wins; later ones only contribute missing properties.
                    duplicateIds++;
                    MergeMissingProperties(node, el);
                    continue;
                }

                var label = ReadString(el, "label");

                if (string.IsNullOrWhiteSpace(label))
                {
                    missingLabels++;
                    label = "Entity";
                }

                node.Label = label;
                ReadProperties(el, node.Properties);
            }

            int autoCreated = 0;
            int skippedEdges = 0;

            if (root.TryGetProperty("edges", out var edgesEl) && edgesEl.ValueKind == JsonValueKind.Array)
            {
                if (edgesEl.GetArrayLength() > MaxEdges)
                    return KgParseResult.Failure($"The model produced {edgesEl.GetArrayLength()} edges; the cap is {MaxEdges}. Narrow the source text, or extract it in parts.");

                foreach (var el in edgesEl.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object)
                    {
                        skippedEdges++;
                        continue;
                    }

                    var source = ReadString(el, "source");
                    var target = ReadString(el, "target");

                    if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
                    {
                        skippedEdges++;
                        continue;
                    }

                    //An edge naming an undeclared node auto-creates it (GremlinFromJson would silently
                    //drop the edge — the wrong default for model output).
                    autoCreated += EnsureEndpoint(graph, seenIds, source);
                    autoCreated += EnsureEndpoint(graph, seenIds, target);

                    var label = ReadString(el, "label");

                    if (string.IsNullOrWhiteSpace(label))
                        label = "relatedTo";

                    var edge = new GraphImport.Edge { Source = source, Target = target, Label = label };
                    ReadProperties(el, edge.Properties);
                    graph.Edges.Add(edge);
                }
            }

            if (duplicateIds > 0)
                warnings.Add($"Merged {duplicateIds} duplicate node id(s).");

            if (skippedNodes > 0)
                warnings.Add($"Skipped {skippedNodes} node(s) with no id.");

            if (missingLabels > 0)
                warnings.Add($"Defaulted {missingLabels} node(s) without a label to \"Entity\".");

            if (autoCreated > 0)
                warnings.Add($"Auto-created {autoCreated} node(s) that only appeared as edge endpoints.");

            if (skippedEdges > 0)
                warnings.Add($"Skipped {skippedEdges} edge(s) missing a source or target.");

            //The in-run entity fold: different ids that mean the same thing ("Acme" / "Acme Inc.").
            graph = FoldWithinGraph(graph, warnings);

            //Backfill display names last, so a name contributed by a folded duplicate survives.
            int backfilled = 0;

            foreach (var node in graph.Nodes)
            {
                if (!node.Properties.ContainsKey("name") && !node.Properties.ContainsKey("title"))
                {
                    node.Properties["name"] = node.Id;
                    backfilled++;
                }
            }

            if (backfilled > 0)
                warnings.Add($"Backfilled a display name from the id for {backfilled} node(s).");

            return KgParseResult.Success(graph, warnings);
        }
    }

    ///<summary>
    ///Folds a parsed generation against the canvas (Merge mode): an entity whose label and normalized
    ///name match one already on the canvas collapses onto the existing id — the T.id collision resolved
    ///before emission — and the delta script contains addV only for genuinely new entities, addE only
    ///for triples the canvas doesn't have, and property updates for folded entities that gained keys
    ///(the canvas value wins a conflict, and the discarded value is reported).
    ///</summary>
    public static KgMergeResult FoldAgainstCanvas(GraphImport.Graph generated, GraphImport.Graph canvas)
    {
        var warnings = new List<string>();
        var canvasNodes = new HashSet<GraphImport.Node>(canvas.Nodes);

        //The canvas is authoritative: its nodes seed the fold map and its ids win.
        var authoritative = new Dictionary<(string Label, string Key), GraphImport.Node>();

        foreach (var node in canvas.Nodes)
        {
            var key = FoldKey(node);

            if (key != null && !authoritative.ContainsKey(key.Value))
                authoritative[key.Value] = node;
        }

        var idMap = new Dictionary<string, string>();
        var newNodes = new List<GraphImport.Node>();
        var propertyLines = new List<string>();
        int propertyUpdates = 0;
        int foldedCount = 0;

        foreach (var node in generated.Nodes)
        {
            var key = FoldKey(node);

            if (key != null && authoritative.TryGetValue(key.Value, out var survivor))
            {
                if (ReferenceEquals(survivor, node))
                    continue;

                foldedCount++;
                idMap[node.Id] = survivor.Id;
                warnings.Add($"Merged '{DisplayName(node)}' into existing '{DisplayName(survivor)}' ({survivor.Label}).");

                foreach (var kv in node.Properties)
                {
                    if (!survivor.Properties.TryGetValue(kv.Key, out var existing))
                    {
                        if (canvasNodes.Contains(survivor))
                        {
                            //The canvas node itself is not mutated — the gain is emitted as a staged
                            //property update, which the preview and the commit both see.
                            propertyLines.Add(GremlinQueryBuilder.SetProperty("node", survivor.Id, kv.Key, kv.Value));
                            propertyUpdates++;
                        }
                        else
                            survivor.Properties[kv.Key] = kv.Value;
                    }
                    else if (existing != kv.Value)
                        warnings.Add($"Kept '{DisplayName(survivor)}' {kv.Key}='{existing}'; discarded conflicting '{kv.Value}'.");
                }
            }
            else
            {
                if (key != null)
                    authoritative[key.Value] = node;

                newNodes.Add(node);
            }
        }

        //Edges: repoint folded endpoints, skip triples the canvas already has, dedupe within the delta.
        var canvasTriples = new HashSet<string>(canvas.Edges.Select(TripleKey));
        var deltaTriples = new HashSet<string>();
        var edgeLines = new List<string>();
        int existingEdges = 0;

        foreach (var edge in generated.Edges)
        {
            var source = Remap(idMap, edge.Source);
            var target = Remap(idMap, edge.Target);
            var remapped = new GraphImport.Edge { Source = source, Target = target, Label = edge.Label };

            foreach (var kv in edge.Properties)
                remapped.Properties[kv.Key] = kv.Value;

            var triple = TripleKey(remapped);

            if (canvasTriples.Contains(triple))
            {
                existingEdges++;
                continue;
            }

            if (!deltaTriples.Add(triple))
                continue;

            edgeLines.Add(GremlinQueryBuilder.AddEdgeWithProperties(source, remapped.Label, target, remapped.Properties));
        }

        if (existingEdges > 0)
            warnings.Add($"Skipped {existingEdges} edge(s) already on the canvas.");

        var lines = new List<string>();

        foreach (var node in newNodes)
            lines.Add(GremlinQueryBuilder.AddVertexWithProperties(node.Label, node.Id, node.Properties));

        lines.AddRange(edgeLines);
        lines.AddRange(propertyLines);

        return new KgMergeResult(string.Join("\n", lines), warnings, newNodes, edgeLines.Count, foldedCount, propertyUpdates);
    }

    ///<summary>
    ///The render JSON the canvas holds (EffectiveData) as a <see cref="GraphImport.Graph"/>, so Merge
    ///can fold against it. GraphDataConverter.ToTable already yields the same shape, so this is a copy
    ///loop, not a parser.
    ///</summary>
    public static GraphImport.Graph FromRenderJson(JsonElement data)
    {
        var table = GraphDataConverter.ToTable(data);
        var graph = new GraphImport.Graph();

        foreach (var row in table.Nodes)
        {
            if (string.IsNullOrEmpty(row.Id))
                continue;

            var node = graph.GetOrAdd(row.Id);

            if (!string.IsNullOrEmpty(row.Label))
                node.Label = row.Label;

            foreach (var kv in row.Properties)
                node.Properties[kv.Key] = kv.Value;
        }

        foreach (var row in table.Edges)
        {
            if (string.IsNullOrEmpty(row.Source) || string.IsNullOrEmpty(row.Target))
                continue;

            var edge = new GraphImport.Edge { Source = row.Source, Target = row.Target };

            if (!string.IsNullOrEmpty(row.Label))
                edge.Label = row.Label;

            foreach (var kv in row.Properties)
                edge.Properties[kv.Key] = kv.Value;

            graph.Edges.Add(edge);
        }

        return graph;
    }

    ///<summary>
    ///Normalizes an entity name for fold matching: lowercase, punctuation dropped, whitespace collapsed,
    ///and trailing corporate suffixes stripped while another token remains. Empty means "not foldable".
    ///</summary>
    public static string NormalizeEntityKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);

        foreach (var ch in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            else if (char.IsWhiteSpace(ch))
                sb.Append(' ');
        }

        var tokens = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        while (tokens.Count > 1 && CorporateSuffixes.Contains(tokens[^1]))
            tokens.RemoveAt(tokens.Count - 1);

        return string.Join(' ', tokens);
    }

    //── In-run fold ─────────────────────────────────────────────────────

    //Folds same-label nodes whose normalized names match into the first occurrence, unions properties
    //(the survivor wins a conflict, and the discarded value is reported), repoints edges and dedupes
    //ones that collapse onto the same source→target:label triple. Deliberately conservative: exact
    //normalized match within one label only — over-merging destroys information silently, a missed
    //duplicate is visible and fixable by hand.
    private static GraphImport.Graph FoldWithinGraph(GraphImport.Graph graph, List<string> warnings)
    {
        var authoritative = new Dictionary<(string Label, string Key), GraphImport.Node>();
        var idMap = new Dictionary<string, string>();
        var survivors = new List<GraphImport.Node>();

        foreach (var node in graph.Nodes)
        {
            var key = FoldKey(node);

            if (key != null && authoritative.TryGetValue(key.Value, out var survivor))
            {
                idMap[node.Id] = survivor.Id;
                warnings.Add($"Merged '{DisplayName(node)}' into '{DisplayName(survivor)}' ({survivor.Label}).");

                foreach (var kv in node.Properties)
                {
                    if (!survivor.Properties.TryGetValue(kv.Key, out var existing))
                        survivor.Properties[kv.Key] = kv.Value;
                    else if (existing != kv.Value)
                        warnings.Add($"Kept '{DisplayName(survivor)}' {kv.Key}='{existing}'; discarded conflicting '{kv.Value}'.");
                }

                continue;
            }

            if (key != null)
                authoritative[key.Value] = node;

            survivors.Add(node);
        }

        if (idMap.Count == 0)
            return graph;

        //Rebuild so the graph's id index holds only survivors — a stale index entry for a folded id
        //would hand later GetOrAdd callers a node that is no longer in the graph.
        var rebuilt = new GraphImport.Graph();

        foreach (var node in survivors)
        {
            var copy = rebuilt.GetOrAdd(node.Id);
            copy.Label = node.Label;

            foreach (var kv in node.Properties)
                copy.Properties[kv.Key] = kv.Value;
        }

        var triples = new HashSet<string>();
        int collapsed = 0;

        foreach (var edge in graph.Edges)
        {
            var remapped = new GraphImport.Edge
            {
                Source = Remap(idMap, edge.Source),
                Target = Remap(idMap, edge.Target),
                Label = edge.Label
            };

            foreach (var kv in edge.Properties)
                remapped.Properties[kv.Key] = kv.Value;

            if (!triples.Add(TripleKey(remapped)))
            {
                collapsed++;
                continue;
            }

            rebuilt.Edges.Add(remapped);
        }

        if (collapsed > 0)
            warnings.Add($"Removed {collapsed} edge(s) that became duplicates after merging.");

        return rebuilt;
    }

    //── Helpers ─────────────────────────────────────────────────────────

    //The fold key for a node — its label plus the normalized name (else id) — or null when the
    //normalized form is empty, which means "never fold this one".
    private static (string Label, string Key)? FoldKey(GraphImport.Node node)
    {
        string candidate;

        if (node.Properties.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name))
            candidate = name;
        else
            candidate = node.Id;

        var normalized = NormalizeEntityKey(candidate);

        if (normalized.Length == 0)
            return null;

        return (node.Label, normalized);
    }

    private static string DisplayName(GraphImport.Node node)
    {
        if (node.Properties.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name))
            return name;

        return node.Id;
    }

    private static string Remap(Dictionary<string, string> idMap, string id)
    {
        if (idMap.TryGetValue(id, out var mapped))
            return mapped;

        return id;
    }

    private static string TripleKey(GraphImport.Edge edge)
    {
        return $"{edge.Source}\u0001{edge.Target}\u0001{edge.Label}";
    }

    private static int EnsureEndpoint(GraphImport.Graph graph, HashSet<string> seenIds, string id)
    {
        if (seenIds.Contains(id))
            return 0;

        seenIds.Add(id);
        var node = graph.GetOrAdd(id);
        node.Label = "Entity";

        return 1;
    }

    private static string ReadString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.String)
            return value.GetString();

        if (value.ValueKind == JsonValueKind.Number)
            return value.GetRawText();

        return null;
    }

    private static void ReadProperties(JsonElement el, Dictionary<string, string> into)
    {
        if (!el.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object)
            return;

        foreach (var p in props.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.Null)
                continue;

            if (p.Value.ValueKind == JsonValueKind.String)
                into[p.Name] = p.Value.GetString();
            else
                into[p.Name] = p.Value.GetRawText();
        }
    }

    private static void MergeMissingProperties(GraphImport.Node node, JsonElement el)
    {
        var extra = new Dictionary<string, string>();
        ReadProperties(el, extra);

        foreach (var kv in extra)
        {
            if (!node.Properties.ContainsKey(kv.Key))
                node.Properties[kv.Key] = kv.Value;
        }
    }
}
