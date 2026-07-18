using System.Text.Json;
using GraphDBViewerWeb.Code;

namespace GraphDBViewerWeb.Tests;

public class GremlinEditParserTests
{
    //── Typed-id normalization (canvas preview vs. Long ids) ────────────

    [Fact]
    public void Parse_DropEdge_LongLiteralId_YieldsPlainId()
    {
        //Regression: a typed drop g.E(727L).drop() must preview against the rendered edge id "727",
        //otherwise the edge stays on the canvas even though the DB commit removes it.
        var edits = GremlinEditParser.Parse("g.E(727L).drop()");

        Assert.Single(edits);
        Assert.Equal(GraphEditKind.RemoveEdge, edits[0].Kind);
        Assert.Equal("727", edits[0].Id);
    }

    [Fact]
    public void Parse_DropVertex_LongLiteralId_YieldsPlainId()
    {
        var edits = GremlinEditParser.Parse("g.V(775L).drop()");

        Assert.Single(edits);
        Assert.Equal(GraphEditKind.RemoveNode, edits[0].Kind);
        Assert.Equal("775", edits[0].Id);
    }

    [Fact]
    public void Parse_SetProperty_LongEdgeId_YieldsPlainId()
    {
        var edits = GremlinEditParser.Parse("g.E(727L).property('weight', '2')");

        Assert.Single(edits);
        Assert.Equal(GraphEditKind.SetProperty, edits[0].Kind);
        Assert.Equal("727", edits[0].Id);
        Assert.Equal("weight", edits[0].Key);
        Assert.Equal("2", edits[0].Value);
    }

    [Fact]
    public void Parse_QuotedStringIdEndingInL_IsPreserved()
    {
        //A string id that genuinely ends in 'L' must not be truncated.
        var edits = GremlinEditParser.Parse("g.E('727L').drop()");

        Assert.Single(edits);
        Assert.Equal(GraphEditKind.RemoveEdge, edits[0].Kind);
        Assert.Equal("727L", edits[0].Id);
    }

    [Fact]
    public void Parse_PlainNumericId_Unchanged()
    {
        var edits = GremlinEditParser.Parse("g.E(727).drop()");

        Assert.Single(edits);
        Assert.Equal(GraphEditKind.RemoveEdge, edits[0].Kind);
        Assert.Equal("727", edits[0].Id);
    }

    //── End-to-end preview (parse -> apply -> edge gone) ────────────────

    [Fact]
    public void BuildEffectiveGraphSON_DropTypedEdge_RemovesItFromPreview()
    {
        var json = """
        [
          { "id": { "@type": "g:Int64", "@value": 1 }, "label": "person" },
          { "id": { "@type": "g:Int64", "@value": 2 }, "label": "person" },
          { "id": { "@type": "g:Int64", "@value": 727 }, "label": "knows", "outV": { "@type": "g:Int64", "@value": 1 }, "inV": { "@type": "g:Int64", "@value": 2 } }
        ]
        """;
        var data = JsonDocument.Parse(json).RootElement;

        var edits = GremlinEditParser.Parse("g.E(727L).drop()");
        var effective = GraphDataConverter.BuildEffectiveGraphSON(data, edits);

        var table = GraphDataConverter.ToTable(effective);
        Assert.Empty(table.Edges);
        Assert.Equal(2, table.Nodes.Count);
    }

    //── Offline re-import: exported addV/addE queries → drawing ─────────

    [Fact]
    public void LooksLikeGremlin_DetectsAddVAddE_NotOtherFormats()
    {
        Assert.True(GraphImport.LooksLikeGremlin("g.addV('person').property('name','Alice')"));
        Assert.True(GraphImport.LooksLikeGremlin("g.V('1').addE('knows').to(__.V('2'))"));
        Assert.False(GraphImport.LooksLikeGremlin("digraph { a -> b }"));
        Assert.False(GraphImport.LooksLikeGremlin("""[{ "id": "1", "label": "x" }]"""));
        Assert.False(GraphImport.LooksLikeGremlin(""));
    }

    [Fact]
    public void GremlinImport_RebuildsDrawingOverEmptyBaseline()
    {
        //The offline round-trip: re-importing exported addV/addE queries reconstructs the drawn graph by
        //applying them as edits over an empty baseline (no database).
        var gremlin = """
        g.addV('person').property(T.id, '1').property('name', 'Alice')
        g.addV('person').property(T.id, '2').property('name', 'Bob')
        g.V('1').addE('knows').to(__.V('2'))
        """;

        var empty = JsonDocument.Parse("[]").RootElement;
        var effective = GraphDataConverter.BuildEffectiveGraphSON(empty, GremlinEditParser.Parse(gremlin));
        var table = GraphDataConverter.ToTable(effective);

        Assert.Equal(2, table.Nodes.Count);
        Assert.Single(table.Edges);
    }

    //── MergePropertyEdits (multi-element staged edits accumulate) ──────

    [Fact]
    public void MergePropertyEdits_DifferentElement_Accumulates()
    {
        //The reported bug: editing a property on a second node must add a line, not replace the first.
        var buffer = "g.V(1).property('gdbvModel', 'u1')";
        var result = GremlinEditParser.MergePropertyEdits(buffer, "node", "2", new[] { "gdbvModel" },
            new[] { "g.V(2).property('gdbvModel', 'u2')" });

        var lines = result.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Contains("g.V(1).property('gdbvModel', 'u1')", lines);
        Assert.Contains("g.V(2).property('gdbvModel', 'u2')", lines);
    }

    [Fact]
    public void MergePropertyEdits_SameElementSameKey_Replaces()
    {
        var buffer = "g.V(1).property('gdbvModel', 'u1')";
        var result = GremlinEditParser.MergePropertyEdits(buffer, "node", "1", new[] { "gdbvModel" },
            new[] { "g.V(1).property('gdbvModel', 'u2')" });

        Assert.Equal("g.V(1).property('gdbvModel', 'u2')", result);
    }

    [Fact]
    public void MergePropertyEdits_SameElementUntouchedKey_Preserved()
    {
        //Re-selecting an element and editing a different key must not drop its earlier staged edit.
        var buffer = "g.V(1).property('gdbvModel', 'u1')";
        var result = GremlinEditParser.MergePropertyEdits(buffer, "node", "1", new[] { "gdbvColor" },
            new[] { "g.V(1).property('gdbvColor', '#f00')" });

        var lines = result.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Contains("g.V(1).property('gdbvModel', 'u1')", lines);
        Assert.Contains("g.V(1).property('gdbvColor', '#f00')", lines);
    }

    [Fact]
    public void MergePropertyEdits_RevertedKey_LineRemoved()
    {
        //Key touched this session but with no regenerated line (value reverted to original) -> line dropped.
        var buffer = "g.V(1).property('gdbvColor', '#f00')";
        var result = GremlinEditParser.MergePropertyEdits(buffer, "node", "1", new[] { "gdbvColor" },
            new string[0]);

        Assert.Equal("", result);
    }

    [Fact]
    public void MergePropertyEdits_NonPropertyQueries_Preserved()
    {
        var buffer = "g.V(9).drop()\ng.V(1).addE('knows').to(__.V(2))";
        var result = GremlinEditParser.MergePropertyEdits(buffer, "node", "1", new[] { "gdbvColor" },
            new[] { "g.V(1).property('gdbvColor', '#f00')" });

        var lines = result.Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Contains("g.V(9).drop()", lines);
        Assert.Contains("g.V(1).addE('knows').to(__.V(2))", lines);
        Assert.Contains("g.V(1).property('gdbvColor', '#f00')", lines);
    }

    [Fact]
    public void MergePropertyEdits_LongIdElement_MatchesAndReplaces()
    {
        //The staged line uses a Long literal (g.V(775L)); the element id is the plain "775".
        var buffer = "g.V(775L).property('gdbvModel', 'u1')";
        var result = GremlinEditParser.MergePropertyEdits(buffer, "node", "775", new[] { "gdbvModel" },
            new[] { "g.V(775L).property('gdbvModel', 'u2')" });

        Assert.Equal("g.V(775L).property('gdbvModel', 'u2')", result);
    }
}
