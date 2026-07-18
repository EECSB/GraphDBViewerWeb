using System.Text.Json;
using GraphDBViewerWeb.Code;

namespace GraphDBViewerWeb.Tests;

public class KgGraphParserTests
{
    //── Parse: validation and repair ────────────────────────────────────

    [Fact]
    public void Parse_FencedOutput_StillParses()
    {
        var fenced = """
            ```json
            {"nodes":[{"id":"a","label":"Person","properties":{"name":"Alice"}}],"edges":[]}
            ```
            """;

        var result = KgGraphParser.Parse(fenced);

        Assert.False(result.IsError);
        Assert.Single(result.Graph.Nodes);
    }

    [Fact]
    public void Parse_MalformedJson_FailsWithClearError()
    {
        var result = KgGraphParser.Parse("{nodes: [oops");

        Assert.True(result.IsError);
        Assert.Contains("JSON", result.Error);
    }

    [Fact]
    public void Parse_NonObject_Fails()
    {
        var result = KgGraphParser.Parse("[1, 2, 3]");

        Assert.True(result.IsError);
    }

    [Fact]
    public void Parse_MissingNodesArray_Fails()
    {
        var result = KgGraphParser.Parse("""{"edges":[]}""");

        Assert.True(result.IsError);
        Assert.Contains("nodes", result.Error);
    }

    [Fact]
    public void Parse_EmptyNodes_SucceedsWithAnEmptyGraph()
    {
        var result = KgGraphParser.Parse("""{"nodes":[],"edges":[]}""");

        Assert.False(result.IsError);
        Assert.Empty(result.Graph.Nodes);
        Assert.Empty(result.Graph.Edges);
    }

    [Fact]
    public void Parse_DuplicateIds_FirstWinsAndWarns()
    {
        var json = """
            {"nodes":[
                {"id":"a","label":"Person","properties":{"name":"Alice"}},
                {"id":"a","label":"Robot","properties":{"name":"Alice 2","role":"engineer"}}
            ],"edges":[]}
            """;

        var result = KgGraphParser.Parse(json);

        Assert.Single(result.Graph.Nodes);
        Assert.Equal("Person", result.Graph.Nodes[0].Label);
        Assert.Equal("Alice", result.Graph.Nodes[0].Properties["name"]);
        Assert.Equal("engineer", result.Graph.Nodes[0].Properties["role"]);
        Assert.Contains(result.Warnings, w => w.Contains("duplicate node id"));
    }

    [Fact]
    public void Parse_EdgeToUndeclaredNode_AutoCreatesItAndWarns()
    {
        var json = """
            {"nodes":[{"id":"a","label":"Person","properties":{"name":"Alice"}}],
             "edges":[{"source":"a","target":"ghost","label":"knows","properties":{}}]}
            """;

        var result = KgGraphParser.Parse(json);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Graph.Nodes.Count);

        var ghost = result.Graph.Nodes.First(n => n.Id == "ghost");

        Assert.Equal("Entity", ghost.Label);
        Assert.Equal("ghost", ghost.Properties["name"]);
        Assert.Contains(result.Warnings, w => w.Contains("Auto-created 1"));
    }

    [Fact]
    public void Parse_MissingName_BackfilledFromTheId()
    {
        var result = KgGraphParser.Parse("""{"nodes":[{"id":"acme","label":"Company","properties":{}}],"edges":[]}""");

        Assert.Equal("acme", result.Graph.Nodes[0].Properties["name"]);
        Assert.Contains(result.Warnings, w => w.Contains("Backfilled"));
    }

    [Fact]
    public void Parse_MissingLabel_DefaultsToEntityAndWarns()
    {
        var result = KgGraphParser.Parse("""{"nodes":[{"id":"x","properties":{"name":"X"}}],"edges":[]}""");

        Assert.Equal("Entity", result.Graph.Nodes[0].Label);
        Assert.Contains(result.Warnings, w => w.Contains("Entity"));
    }

    [Fact]
    public void Parse_NodeCapOverflow_FailsLoudly()
    {
        var nodes = string.Join(",", Enumerable.Range(0, KgGraphParser.MaxNodes + 1)
            .Select(i => $$$"""{"id":"n{{{i}}}","label":"Entity","properties":{}}"""));

        var result = KgGraphParser.Parse($$$"""{"nodes":[{{{nodes}}}],"edges":[]}""");

        Assert.True(result.IsError);
        Assert.Contains($"{KgGraphParser.MaxNodes}", result.Error);
    }

    [Fact]
    public void Parse_EdgeCapOverflow_FailsLoudly()
    {
        var edges = string.Join(",", Enumerable.Range(0, KgGraphParser.MaxEdges + 1)
            .Select(i => $$$"""{"source":"a","target":"b","label":"e{{{i}}}","properties":{}}"""));

        var result = KgGraphParser.Parse($$$"""{"nodes":[{"id":"a","properties":{}},{"id":"b","properties":{}}],"edges":[{{{edges}}}]}""");

        Assert.True(result.IsError);
        Assert.Contains($"{KgGraphParser.MaxEdges}", result.Error);
    }

    //── The in-run entity fold (AC 5) ───────────────────────────────────

    [Fact]
    public void Parse_TwoSurfaceForms_FoldToOneNodeWithAWarning()
    {
        var json = """
            {"nodes":[
                {"id":"a1","label":"Company","properties":{"name":"Acme"}},
                {"id":"a2","label":"Company","properties":{"name":"Acme Inc."}},
                {"id":"b","label":"Person","properties":{"name":"Bob"}}
            ],"edges":[{"source":"b","target":"a2","label":"worksAt","properties":{}}]}
            """;

        var result = KgGraphParser.Parse(json);

        Assert.Equal(2, result.Graph.Nodes.Count);
        Assert.DoesNotContain(result.Graph.Nodes, n => n.Id == "a2");

        //The edge that named the folded id now points at the survivor.
        Assert.Equal("a1", result.Graph.Edges.Single().Target);
        Assert.Contains(result.Warnings, w => w.Contains("Merged 'Acme Inc.' into 'Acme' (Company)"));
    }

    [Fact]
    public void Parse_SameName_DifferentLabels_DoNotMerge()
    {
        var json = """
            {"nodes":[
                {"id":"c","label":"Company","properties":{"name":"Acme"}},
                {"id":"p","label":"Product","properties":{"name":"Acme"}}
            ],"edges":[]}
            """;

        var result = KgGraphParser.Parse(json);

        Assert.Equal(2, result.Graph.Nodes.Count);
    }

    [Fact]
    public void Parse_FoldConflict_SurvivorWinsAndDiscardIsReported()
    {
        var json = """
            {"nodes":[
                {"id":"a1","label":"Company","properties":{"name":"Acme","industry":"software"}},
                {"id":"a2","label":"Company","properties":{"name":"Acme Inc.","industry":"consulting","founded":"1999"}}
            ],"edges":[]}
            """;

        var result = KgGraphParser.Parse(json);
        var survivor = result.Graph.Nodes.Single();

        Assert.Equal("software", survivor.Properties["industry"]);
        Assert.Equal("1999", survivor.Properties["founded"]);
        Assert.Contains(result.Warnings, w => w.Contains("discarded conflicting 'consulting'"));
    }

    [Fact]
    public void Parse_EdgesCollapsingOntoOneTriple_AreDeduplicated()
    {
        var json = """
            {"nodes":[
                {"id":"a1","label":"Company","properties":{"name":"Acme"}},
                {"id":"a2","label":"Company","properties":{"name":"Acme Inc."}},
                {"id":"b","label":"Person","properties":{"name":"Bob"}}
            ],"edges":[
                {"source":"b","target":"a1","label":"worksAt","properties":{}},
                {"source":"b","target":"a2","label":"worksAt","properties":{}}
            ]}
            """;

        var result = KgGraphParser.Parse(json);

        Assert.Single(result.Graph.Edges);
        Assert.Contains(result.Warnings, w => w.Contains("duplicates after merging"));
    }

    //── The merge-scope fold (AC 5 + AC 9) ──────────────────────────────

    private static GraphImport.Graph Canvas(params (string Id, string Label, string Name)[] nodes)
    {
        var graph = new GraphImport.Graph();

        foreach (var n in nodes)
        {
            var node = graph.GetOrAdd(n.Id);
            node.Label = n.Label;
            node.Properties["name"] = n.Name;
        }

        return graph;
    }

    //The unit-level proof of the T.id argument: a repeat entity collapses onto the existing canvas id,
    //so the colliding addV is never written.
    [Fact]
    public void FoldAgainstCanvas_RepeatEntity_CollapsesOntoTheCanvasId_NoAddV()
    {
        var canvas = Canvas(("acme-1", "Company", "Acme"));
        var generated = KgGraphParser.Parse("""
            {"nodes":[
                {"id":"acme","label":"Company","properties":{"name":"Acme Inc."}},
                {"id":"alice","label":"Person","properties":{"name":"Alice"}}
            ],"edges":[{"source":"alice","target":"acme","label":"worksAt","properties":{}}]}
            """).Graph;

        var merge = KgGraphParser.FoldAgainstCanvas(generated, canvas);

        Assert.Equal(1, merge.NewNodes);
        Assert.Equal(1, merge.FoldedNodes);
        Assert.DoesNotContain("addV('Company')", merge.DeltaGremlin);
        Assert.Contains("addV('Person')", merge.DeltaGremlin);

        //The new edge lands on the existing canvas id, not a second Acme.
        Assert.Contains("'acme-1'", merge.DeltaGremlin);
        Assert.Contains(merge.Warnings, w => w.Contains("into existing 'Acme'"));
    }

    [Fact]
    public void FoldAgainstCanvas_GainedProperty_EmittedAsAPropertyUpdate_CanvasWinsConflicts()
    {
        var canvas = Canvas(("acme-1", "Company", "Acme"));
        var generated = KgGraphParser.Parse("""
            {"nodes":[{"id":"acme","label":"Company","properties":{"name":"Acme Incorporated","founded":"1999"}}],"edges":[]}
            """).Graph;

        var merge = KgGraphParser.FoldAgainstCanvas(generated, canvas);

        Assert.Equal(1, merge.PropertyUpdates);
        Assert.Contains(".property('founded', '1999')", merge.DeltaGremlin);

        //The canvas's name survives; the generated variant is reported, not applied.
        Assert.DoesNotContain("Acme Incorporated", merge.DeltaGremlin);
        Assert.Contains(merge.Warnings, w => w.Contains("discarded conflicting 'Acme Incorporated'"));
    }

    [Fact]
    public void FoldAgainstCanvas_TripleAlreadyOnCanvas_IsNotReEmitted()
    {
        var canvas = Canvas(("alice-1", "Person", "Alice"), ("acme-1", "Company", "Acme"));
        canvas.Edges.Add(new GraphImport.Edge { Source = "alice-1", Target = "acme-1", Label = "worksAt" });

        var generated = KgGraphParser.Parse("""
            {"nodes":[
                {"id":"alice","label":"Person","properties":{"name":"Alice"}},
                {"id":"acme","label":"Company","properties":{"name":"Acme"}}
            ],"edges":[{"source":"alice","target":"acme","label":"worksAt","properties":{}}]}
            """).Graph;

        var merge = KgGraphParser.FoldAgainstCanvas(generated, canvas);

        Assert.Equal(0, merge.NewNodes);
        Assert.Equal(0, merge.NewEdges);
        Assert.Equal("", merge.DeltaGremlin);
        Assert.Contains(merge.Warnings, w => w.Contains("already on the canvas"));
    }

    //The delta is built from the same GremlinQueryBuilder pieces ToGremlin uses, so the edit parser
    //must recognize every line — a skipped line would silently vanish from the staged preview.
    [Fact]
    public void FoldAgainstCanvas_Delta_RoundTripsThroughGremlinEditParser()
    {
        var canvas = Canvas(("acme-1", "Company", "Acme"));
        var generated = KgGraphParser.Parse("""
            {"nodes":[
                {"id":"acme","label":"Company","properties":{"name":"Acme","founded":"1999"}},
                {"id":"alice","label":"Person","properties":{"name":"Alice"}}
            ],"edges":[{"source":"alice","target":"acme","label":"worksAt","properties":{}}]}
            """).Graph;

        var merge = KgGraphParser.FoldAgainstCanvas(generated, canvas);
        var edits = GremlinEditParser.Parse(merge.DeltaGremlin);

        Assert.Equal(3, edits.Count);
        Assert.Contains(edits, e => e.Kind == GraphEditKind.AddNode && e.Id == "alice");
        Assert.Contains(edits, e => e.Kind == GraphEditKind.AddEdge && e.Source == "alice" && e.Target == "acme-1");
        Assert.Contains(edits, e => e.Kind == GraphEditKind.SetProperty && e.Id == "acme-1" && e.Key == "founded");
    }

    //── The render-JSON adapter ─────────────────────────────────────────

    [Fact]
    public void FromRenderJson_ReadsTheCanvasShapeBack()
    {
        var json = """
            [
                {"id":"a","label":"Person","properties":{"name":"Alice"}},
                {"id":"b","label":"Company","properties":{"name":"Acme"}},
                {"id":"a->b:worksAt","label":"worksAt","outV":"a","inV":"b","properties":{}}
            ]
            """;

        var graph = KgGraphParser.FromRenderJson(JsonDocument.Parse(json).RootElement);

        Assert.Equal(2, graph.Nodes.Count);
        Assert.Equal("Alice", graph.Nodes.First(n => n.Id == "a").Properties["name"]);
        Assert.Single(graph.Edges);
        Assert.Equal("a", graph.Edges[0].Source);
        Assert.Equal("b", graph.Edges[0].Target);
        Assert.Equal("worksAt", graph.Edges[0].Label);
    }

    //── Normalization ───────────────────────────────────────────────────

    [Theory]
    [InlineData("Acme, Inc.", "acme")]
    [InlineData("Acme Incorporated", "acme")]
    [InlineData("ACME", "acme")]
    [InlineData("Acme   Corp", "acme")]
    [InlineData("Acme GmbH", "acme")]
    [InlineData("Co", "co")]
    [InlineData("the company", "the company")]
    public void NormalizeEntityKey_NormalizesSurfaceForms(string input, string expected)
    {
        Assert.Equal(expected, KgGraphParser.NormalizeEntityKey(input));
    }
}
