using System.Text.Json;
using GraphDBViewerWeb.Code;

namespace GraphDBViewerWeb.Tests;

///<summary>
///Pins SparqlDb's SparqlResult -> GraphDbResult mapping. This is where behavior had to be preserved
///when the client went behind IGraphDb: each SPARQL response shape has to land on the same pane it
///always did.
///</summary>
public class SparqlDbMappingTests
{
    [Fact]
    public void Select_BecomesATableAndLeavesTheGraphPayloadUnset()
    {
        var parsed = new SparqlResult
        {
            Kind = SparqlKind.Select,
            RawJson = "{\"head\":{}}",
            Vars = { "s", "p" },
            Rows = { new() { ["s"] = "subject", ["p"] = "predicate" } }
        };

        var result = SparqlDb.ToGraphDbResult(parsed);

        Assert.False(result.IsError);
        Assert.NotNull(result.Table);
        Assert.Equal(new[] { "s", "p" }, result.Table.Vars);
        Assert.Equal("subject", Assert.Single(result.Table.Rows)["s"]);
        Assert.Null(result.Table.Boolean);
        Assert.Equal(JsonValueKind.Undefined, result.Data.ValueKind);
        Assert.Equal("{\"head\":{}}", result.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Ask_BecomesABooleanTableWithNoRows(bool answer)
    {
        var parsed = new SparqlResult { Kind = SparqlKind.Ask, Boolean = answer, RawJson = "{}" };

        var result = SparqlDb.ToGraphDbResult(parsed);

        Assert.Equal(answer, result.Table.Boolean);
        Assert.Empty(result.Table.Vars);
        Assert.Empty(result.Table.Rows);
    }

    [Fact]
    public void Graph_JoinsTheNormalGraphPipelineWithNoTable()
    {
        var graph = JsonDocument.Parse("""[{"id":"http://x/1","label":"Thing"}]""").RootElement;
        var parsed = new SparqlResult { Kind = SparqlKind.Graph, GraphData = graph, RawJson = "{\"raw\":1}" };

        var result = SparqlDb.ToGraphDbResult(parsed);

        //A CONSTRUCT/DESCRIBE result must reach lastResultData exactly as a Gremlin result would.
        Assert.Null(result.Table);
        Assert.Equal(JsonValueKind.Array, result.Data.ValueKind);
        Assert.Equal("http://x/1", result.Data[0].GetProperty("id").GetString());
        Assert.Equal("{\"raw\":1}", result.ToString());
    }

    [Fact]
    public void Unknown_BecomesAnEmptyTable_SoItStillRendersAsBindingsZero()
    {
        //An unparseable body used to fall through the "is it a graph?" test and render as an empty
        //bindings table. Mapping it to an empty table keeps that, rather than drawing a blank canvas.
        var parsed = new SparqlResult { Kind = SparqlKind.Unknown, RawJson = "not-really-json" };

        var result = SparqlDb.ToGraphDbResult(parsed);

        Assert.NotNull(result.Table);
        Assert.Empty(result.Table.Rows);
        Assert.Null(result.Table.Boolean);
        Assert.Equal(JsonValueKind.Undefined, result.Data.ValueKind);
    }

    [Fact]
    public void Error_BecomesAFailure()
    {
        var parsed = new SparqlResult { IsError = true, Error = "HTTP 400: bad query" };

        var result = SparqlDb.ToGraphDbResult(parsed);

        Assert.True(result.IsError);
        Assert.Equal("HTTP 400: bad query", result.Error);
        Assert.Equal("HTTP 400: bad query", result.ToString());
        Assert.Null(result.Table);
    }
}
