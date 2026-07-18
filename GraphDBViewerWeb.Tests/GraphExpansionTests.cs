using System.Linq;
using System.Text.Json;
using GraphDBViewerWeb.Code;

namespace GraphDBViewerWeb.Tests;

public class GraphExpansionTests
{
    [Fact]
    public void Neighbors_CapsToTheLimit()
    {
        Assert.Equal("g.V('a').bothE().limit(25).union(__.identity(), __.otherV())", GremlinQueryBuilder.Neighbors("a", 25));
    }

    [Fact]
    public void Neighbors_ZeroOrLess_IsUncapped()
    {
        Assert.Equal("g.V('a').union(__.bothE(), __.bothE().otherV())", GremlinQueryBuilder.Neighbors("a", 0));
    }

    [Fact]
    public void MergeGraphResults_DeduplicatesByIdAndCombines()
    {
        var existing = JsonSerializer.Deserialize<JsonElement>("""
        [ { "id": "1", "label": "person" }, { "id": "2", "label": "person" } ]
        """);

        var incoming = JsonSerializer.Deserialize<JsonElement>("""
        [
          { "id": "2", "label": "person" },
          { "id": "3", "label": "person" },
          { "id": "e1", "label": "knows", "outV": "1", "inV": "2" }
        ]
        """);

        var merged = GraphDataConverter.MergeGraphResults(existing, incoming);
        var table = GraphDataConverter.ToTable(merged);

        //Vertex "2" appears in both inputs but should be merged once.
        Assert.Equal(3, table.Nodes.Count);
        Assert.Single(table.Edges);
    }

    [Fact]
    public void ToForceGraphJson_EdgeListedBeforeVertices_UsesRealVertexLabel()
    {
        //Mirrors a neighbor-expansion result (edges first, then vertices). The named
        //vertices must win over the id-only placeholders the edge would otherwise create.
        var json = JsonSerializer.Deserialize<JsonElement>("""
        [
          { "id": "e1", "label": "composes", "outV": "288", "inV": "306" },
          { "id": "288", "label": "part", "properties": { "name": "Table Top" } },
          { "id": "306", "label": "part", "properties": { "name": "Leg" } }
        ]
        """);

        var fg = JsonSerializer.Deserialize<JsonElement>(GraphDataConverter.ToForceGraphJson(json));

        string label288 = null;
        foreach (var n in fg.GetProperty("nodes").EnumerateArray())
        {
            if (n.GetProperty("id").GetString() == "288")
                label288 = n.GetProperty("label").GetString();
        }

        Assert.Equal("Table Top", label288);
    }

    [Fact]
    public void BuildSchemaGraphJson_BuildsLabelNodesAndRelationshipEdges()
    {
        var vLabels = JsonDocument.Parse("""
        [ { "@type":"g:Map", "@value":[ "person", {"@type":"g:Int64","@value":3}, "product", {"@type":"g:Int64","@value":2} ] } ]
        """).RootElement;

        var eLabels = JsonDocument.Parse("""
        [ { "@type":"g:Map", "@value":[ "buys", {"@type":"g:Int64","@value":4} ] } ]
        """).RootElement;

        var vKeys = JsonDocument.Parse("""
        [ { "@type":"g:Map", "@value":[ "person", {"@type":"g:List","@value":["name","age"]}, "product", {"@type":"g:List","@value":["name"]} ] } ]
        """).RootElement;

        var triples = JsonDocument.Parse("""
        [ { "@type":"g:Map", "@value":[ "out","person","edge","buys","in","product" ] } ]
        """).RootElement;

        var json = SchemaBuilder.BuildSchemaGraphJson(vLabels, eLabels, vKeys, triples);
        var table = GraphDataConverter.ToTable(JsonSerializer.Deserialize<JsonElement>(json));

        Assert.Equal(2, table.Nodes.Count);
        Assert.Single(table.Edges);

        var person = table.Nodes.Single(n => n.Id == "person");
        Assert.Equal("3", person.Properties["count"]);
        Assert.Equal("name, age", person.Properties["keys"]);

        var edge = table.Edges.Single();
        Assert.Equal("buys", edge.Label);
        Assert.Equal("person", edge.Source);
        Assert.Equal("product", edge.Target);
    }

    [Fact]
    public void ExtractVocabulary_ReturnsSortedLabelsAndUnionedKeys()
    {
        var vLabels = JsonDocument.Parse("""
        [ { "@type":"g:Map", "@value":[ "product", {"@type":"g:Int64","@value":2}, "person", {"@type":"g:Int64","@value":3} ] } ]
        """).RootElement;

        var eLabels = JsonDocument.Parse("""
        [ { "@type":"g:Map", "@value":[ "knows", {"@type":"g:Int64","@value":1}, "buys", {"@type":"g:Int64","@value":4} ] } ]
        """).RootElement;

        var vKeys = JsonDocument.Parse("""
        [ { "@type":"g:Map", "@value":[ "person", {"@type":"g:List","@value":["name","age"]}, "product", {"@type":"g:List","@value":["name","price"]} ] } ]
        """).RootElement;

        var vocab = SchemaBuilder.ExtractVocabulary(vLabels, eLabels, vKeys);

        Assert.Equal(new[] { "person", "product" }, vocab.VertexLabels);
        Assert.Equal(new[] { "buys", "knows" }, vocab.EdgeLabels);
        Assert.Equal(new[] { "age", "name", "price" }, vocab.PropertyKeys);
    }
}
