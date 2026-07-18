using System.Text.Json;
using GraphDBViewerWeb.Code;

namespace GraphDBViewerWeb.Tests;

public class GraphDbResultTests
{
    private static JsonElement Json(string json)
    {
        return JsonDocument.Parse(json).RootElement;
    }

    [Fact]
    public void Success_ExposesDataAndNoError()
    {
        var result = GraphDbResult.Success(Json("""[{"id":"1"}]"""));

        Assert.False(result.IsError);
        Assert.Null(result.Error);
        Assert.Null(result.Table);
        Assert.Equal(JsonValueKind.Array, result.Data.ValueKind);
    }

    [Fact]
    public void Success_WithoutRaw_ToStringPrettyPrintsParseableJson()
    {
        var result = GraphDbResult.Success(Json("[1,2]"));

        //The Gremlin path: no verbatim body is kept, so the JSON view shows the re-serialized payload.
        var text = result.ToString();

        Assert.Contains("\n", text);

        var reparsed = JsonDocument.Parse(text).RootElement;
        Assert.Equal(JsonValueKind.Array, reparsed.ValueKind);
        Assert.Equal(2, reparsed.GetArrayLength());
    }

    [Fact]
    public void Success_WithRaw_ToStringReturnsTheEnginesOwnText()
    {
        var result = GraphDbResult.Success(Json("""[{"id":"1"}]"""), "the-verbatim-body");

        Assert.Equal("the-verbatim-body", result.ToString());
    }

    [Fact]
    public void Failure_ToStringReturnsTheError()
    {
        var result = GraphDbResult.Failure("boom");

        Assert.True(result.IsError);
        Assert.Equal("boom", result.Error);
        Assert.Equal("boom", result.ToString());
        Assert.Null(result.Table);
    }

    [Fact]
    public void Tabular_CarriesTheTableAndLeavesDataUndefined()
    {
        var table = new GraphDbTable { Vars = { "s" }, Rows = { new() { ["s"] = "x" } } };

        var result = GraphDbResult.Tabular(table, "{}");

        Assert.False(result.IsError);
        Assert.Same(table, result.Table);
        Assert.Equal(JsonValueKind.Undefined, result.Data.ValueKind);
        Assert.Equal("{}", result.ToString());
    }

    [Fact]
    public void Tabular_WithoutRaw_ToStringIsEmptyRatherThanThrowing()
    {
        //Data is default(JsonElement) here, which has no backing document — serializing it throws, so
        //ToString has to short-circuit on Undefined.
        var result = GraphDbResult.Tabular(new GraphDbTable(), null);

        Assert.Equal("", result.ToString());
    }

    [Fact]
    public void Tabular_BooleanShapedResultKeepsVarsAndRowsEmpty()
    {
        var result = GraphDbResult.Tabular(new GraphDbTable { Boolean = true }, "true");

        Assert.True(result.Table.Boolean);
        Assert.Empty(result.Table.Vars);
        Assert.Empty(result.Table.Rows);
    }
}
