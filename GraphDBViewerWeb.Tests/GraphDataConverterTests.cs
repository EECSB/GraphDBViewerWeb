using System.Linq;
using System.Text.Json;
using GraphDBViewerWeb.Code;

namespace GraphDBViewerWeb.Tests;

public class GraphDataConverterTests
{
    #region Cytoscape (2D)

    [Fact]
    public void ToCytoscapeJson_TraversalTriples_ExtractsNodesAndEdges()
    {
        var json = """
        [
          {
            "v": { "id": "1", "label": "person", "properties": { "Name": [{ "value": "Alice" }] } },
            "e": { "id": "e1", "label": "knows" },
            "o": { "id": "2", "label": "person", "properties": { "Name": [{ "value": "Bob" }] } }
          },
          {
            "v": { "id": "2", "label": "person", "properties": { "Name": [{ "value": "Bob" }] } },
            "e": { "id": "e2", "label": "likes" },
            "o": { "id": "3", "label": "software", "properties": { "Name": [{ "value": "Gremlin" }] } }
          }
        ]
        """;

        var results = JsonDocument.Parse(json).RootElement;
        var output = GraphDataConverter.ToCytoscapeJson(results);
        var elements = JsonDocument.Parse(output).RootElement;

        var nodes = new List<JsonElement>();
        var edges = new List<JsonElement>();
        foreach (var el in elements.EnumerateArray())
        {
            var data = el.GetProperty("data");
            if (data.TryGetProperty("source", out _))
                edges.Add(data);
            else
                nodes.Add(data);
        }

        Assert.Equal(3, nodes.Count);
        Assert.Equal(2, edges.Count);

        Assert.Contains(nodes, n => n.GetProperty("id").GetString() == "1" && n.GetProperty("label").GetString() == "Alice");
        Assert.Contains(nodes, n => n.GetProperty("id").GetString() == "2" && n.GetProperty("label").GetString() == "Bob");
        Assert.Contains(nodes, n => n.GetProperty("id").GetString() == "3" && n.GetProperty("label").GetString() == "Gremlin");

        Assert.Contains(edges, e => e.GetProperty("source").GetString() == "1" && e.GetProperty("target").GetString() == "2" && e.GetProperty("label").GetString() == "knows");
        Assert.Contains(edges, e => e.GetProperty("source").GetString() == "2" && e.GetProperty("target").GetString() == "3" && e.GetProperty("label").GetString() == "likes");
    }

    [Fact]
    public void ToCytoscapeJson_EdgesCarryPaletteColorForTheirLabel()
    {
        var json = """
        [
          { "v": { "id": "1", "label": "person" }, "e": { "id": "e1", "label": "knows" }, "o": { "id": "2", "label": "person" } },
          { "v": { "id": "2", "label": "person" }, "e": { "id": "e2", "label": "knows" }, "o": { "id": "3", "label": "person" } },
          { "v": { "id": "1", "label": "person" }, "e": { "id": "e3", "label": "likes" }, "o": { "id": "3", "label": "person" } }
        ]
        """;

        var results = JsonDocument.Parse(json).RootElement;
        var elements = JsonDocument.Parse(GraphDataConverter.ToCytoscapeJson(results)).RootElement;

        var edges = elements.EnumerateArray().Select(e => e.GetProperty("data")).Where(d => d.TryGetProperty("source", out _)).ToList();

        var knows = edges.Where(e => e.GetProperty("label").GetString() == "knows").ToList();
        var likes = edges.Single(e => e.GetProperty("label").GetString() == "likes");

        //Every edge carries its label's palette color; same label → same color, matching GraphPalette.
        Assert.All(knows, e => Assert.Equal(GraphPalette.ColorForLabel("knows"), e.GetProperty("edgeColor").GetString()));
        Assert.Equal(GraphPalette.ColorForLabel("likes"), likes.GetProperty("edgeColor").GetString());
    }

    [Fact]
    public void ToForceGraphJson_LinksCarryPaletteColorForTheirLabel()
    {
        var json = """
        [ { "v": { "id": "1", "label": "person" }, "e": { "id": "e1", "label": "knows" }, "o": { "id": "2", "label": "person" } } ]
        """;

        var results = JsonDocument.Parse(json).RootElement;
        var root = JsonDocument.Parse(GraphDataConverter.ToForceGraphJson(results)).RootElement;

        var link = root.GetProperty("links").EnumerateArray().Single();
        Assert.Equal(GraphPalette.ColorForLabel("knows"), link.GetProperty("color").GetString());
    }

    //Two edge labels, used to exercise the edge-color modes below.
    private const string TwoEdgeLabelsJson = """
    [
      { "v": { "id": "1", "label": "person" }, "e": { "id": "e1", "label": "knows" }, "o": { "id": "2", "label": "person" } },
      { "v": { "id": "1", "label": "person" }, "e": { "id": "e2", "label": "likes" }, "o": { "id": "3", "label": "person" } }
    ]
    """;

    private static List<JsonElement> CytoscapeEdges(string output)
    {
        return JsonDocument.Parse(output).RootElement.EnumerateArray()
            .Select(e => e.GetProperty("data"))
            .Where(d => d.TryGetProperty("source", out _))
            .ToList();
    }

    [Fact]
    public void ToCytoscapeJson_EdgeColorModeOff_UsesDefaultGrayForEveryEdge()
    {
        var results = JsonDocument.Parse(TwoEdgeLabelsJson).RootElement;
        var edges = CytoscapeEdges(GraphDataConverter.ToCytoscapeJson(results, null, edgeColorMode: 1));

        //Off mode: every edge draws in the shared default gray, regardless of label.
        Assert.All(edges, e => Assert.Equal("#6c757d", e.GetProperty("edgeColor").GetString()));
    }

    [Fact]
    public void ToCytoscapeJson_EdgeColorModeOff_ColorsOnlyThePickedLabel()
    {
        var results = JsonDocument.Parse(TwoEdgeLabelsJson).RootElement;
        var custom = new Dictionary<string, string> { ["knows"] = "#00ff00" };
        var edges = CytoscapeEdges(GraphDataConverter.ToCytoscapeJson(results, null, edgeColorMode: 1, edgeColors: custom));

        var knows = edges.Single(e => e.GetProperty("label").GetString() == "knows");
        var likes = edges.Single(e => e.GetProperty("label").GetString() == "likes");

        //Off mode with a picked color: only that label is colored — an untouched label stays gray instead
        //of picking up an auto color.
        Assert.Equal("#00ff00", knows.GetProperty("edgeColor").GetString());
        Assert.Equal("#6c757d", likes.GetProperty("edgeColor").GetString());
    }

    [Fact]
    public void ToCytoscapeJson_EdgeColorModeCustom_UsesPerLabelColor_AndFallsBackToAuto()
    {
        var results = JsonDocument.Parse(TwoEdgeLabelsJson).RootElement;
        var custom = new Dictionary<string, string> { ["knows"] = "#00ff00" };
        var edges = CytoscapeEdges(GraphDataConverter.ToCytoscapeJson(results, null, edgeColorMode: 2, edgeColors: custom));

        var knows = edges.Single(e => e.GetProperty("label").GetString() == "knows");
        var likes = edges.Single(e => e.GetProperty("label").GetString() == "likes");

        //Custom mode: the configured label uses its color; an unset label falls back to its auto color.
        Assert.Equal("#00ff00", knows.GetProperty("edgeColor").GetString());
        Assert.Equal(GraphPalette.ColorForLabel("likes"), likes.GetProperty("edgeColor").GetString());
    }

    [Fact]
    public void ToForceGraphJson_EdgeColorModeOff_UsesDefaultGray()
    {
        var results = JsonDocument.Parse(TwoEdgeLabelsJson).RootElement;
        var links = JsonDocument.Parse(GraphDataConverter.ToForceGraphJson(results, null, edgeColorMode: 1)).RootElement.GetProperty("links");

        Assert.All(links.EnumerateArray(), l => Assert.Equal("#6c757d", l.GetProperty("color").GetString()));
    }

    [Fact]
    public void ToForceGraphJson_EdgeColorModeOff_ColorsOnlyThePickedLabel()
    {
        var results = JsonDocument.Parse(TwoEdgeLabelsJson).RootElement;
        var custom = new Dictionary<string, string> { ["knows"] = "#00ff00" };
        var links = JsonDocument.Parse(GraphDataConverter.ToForceGraphJson(results, null, edgeColorMode: 1, edgeColors: custom)).RootElement.GetProperty("links");

        var knows = links.EnumerateArray().Single(l => l.GetProperty("label").GetString() == "knows");
        var likes = links.EnumerateArray().Single(l => l.GetProperty("label").GetString() == "likes");

        //Off mode with a picked color: only that label is colored; the untouched label stays gray.
        Assert.Equal("#00ff00", knows.GetProperty("color").GetString());
        Assert.Equal("#6c757d", likes.GetProperty("color").GetString());
    }

    [Fact]
    public void ToForceGraphJson_EdgeColorModeCustom_UsesPerLabelColor_AndFallsBackToAuto()
    {
        var results = JsonDocument.Parse(TwoEdgeLabelsJson).RootElement;
        var custom = new Dictionary<string, string> { ["knows"] = "#00ff00" };
        var links = JsonDocument.Parse(GraphDataConverter.ToForceGraphJson(results, null, edgeColorMode: 2, edgeColors: custom)).RootElement.GetProperty("links");

        var knows = links.EnumerateArray().Single(l => l.GetProperty("label").GetString() == "knows");
        var likes = links.EnumerateArray().Single(l => l.GetProperty("label").GetString() == "likes");

        Assert.Equal("#00ff00", knows.GetProperty("color").GetString());
        Assert.Equal(GraphPalette.ColorForLabel("likes"), likes.GetProperty("color").GetString());
    }

    [Fact]
    public void EdgeLabels_ReturnsDistinctEdgeLabels()
    {
        var results = JsonDocument.Parse(TwoEdgeLabelsJson).RootElement;

        var labels = GraphDataConverter.EdgeLabels(results);

        Assert.Equal(2, labels.Count);
        Assert.Contains("knows", labels);
        Assert.Contains("likes", labels);
    }

    [Fact]
    public void ToCytoscapeJson_StandaloneVertices_CreatesNodesOnly()
    {
        var json = """
        [
          { "id": "v1", "label": "city", "properties": { "name": [{ "value": "Seattle" }] } },
          { "id": "v2", "label": "city", "properties": { "name": [{ "value": "Portland" }] } }
        ]
        """;

        var results = JsonDocument.Parse(json).RootElement;
        var output = GraphDataConverter.ToCytoscapeJson(results);
        var elements = JsonDocument.Parse(output).RootElement;

        Assert.Equal(2, elements.GetArrayLength());
        foreach (var el in elements.EnumerateArray())
        {
            var data = el.GetProperty("data");
            Assert.False(data.TryGetProperty("source", out _));
        }

        Assert.Contains(elements.EnumerateArray().ToArray(),
            el => el.GetProperty("data").GetProperty("label").GetString() == "Seattle");
    }

    [Fact]
    public void ToCytoscapeJson_StandaloneEdges_CreatesImplicitNodesAndEdge()
    {
        var json = """
        [
          { "id": "e1", "label": "route", "outV": "v1", "inV": "v2" }
        ]
        """;

        var results = JsonDocument.Parse(json).RootElement;
        var output = GraphDataConverter.ToCytoscapeJson(results);
        var elements = JsonDocument.Parse(output).RootElement;

        int nodeCount = 0, edgeCount = 0;
        foreach (var el in elements.EnumerateArray())
        {
            if (el.GetProperty("data").TryGetProperty("source", out _))
                edgeCount++;
            else
                nodeCount++;
        }

        Assert.Equal(2, nodeCount);
        Assert.Equal(1, edgeCount);
    }

    [Fact]
    public void ToCytoscapeJson_VerticesThenEdges_IncludesIsolatedVertex()
    {
        //Mirrors the FullGraph union query output: all vertices first (one with no edges),
        //then all edges. The isolated vertex (id 9) must still appear as a node.
        var json = """
        [
          { "id": "1", "label": "Component", "properties": { "name": [{ "value": "Leg" }] } },
          { "id": "2", "label": "Assembly", "properties": { "name": [{ "value": "Table" }] } },
          { "id": "9", "label": "asdasd", "properties": {} },
          { "id": "e1", "label": "composes", "outV": "1", "inV": "2" }
        ]
        """;

        var results = JsonDocument.Parse(json).RootElement;
        var output = GraphDataConverter.ToCytoscapeJson(results);
        var elements = JsonDocument.Parse(output).RootElement;

        var nodes = elements.EnumerateArray().Select(e => e.GetProperty("data"))
            .Where(d => !d.TryGetProperty("source", out _)).ToList();
        var edges = elements.EnumerateArray().Select(e => e.GetProperty("data"))
            .Where(d => d.TryGetProperty("source", out _)).ToList();

        Assert.Equal(3, nodes.Count);
        Assert.Single(edges);
        Assert.Contains(nodes, n => n.GetProperty("id").GetString() == "9");//isolated vertex kept
    }

    [Fact]
    public void ToCytoscapeJson_DeduplicatesNodes()
    {
        var json = """
        [
          {
            "v": { "id": "1", "label": "person" },
            "e": { "id": "e1", "label": "knows" },
            "o": { "id": "2", "label": "person" }
          },
          {
            "v": { "id": "1", "label": "person" },
            "e": { "id": "e2", "label": "likes" },
            "o": { "id": "3", "label": "person" }
          }
        ]
        """;

        var results = JsonDocument.Parse(json).RootElement;
        var output = GraphDataConverter.ToCytoscapeJson(results);
        var elements = JsonDocument.Parse(output).RootElement;

        var nodeIds = elements.EnumerateArray()
            .Select(el => el.GetProperty("data"))
            .Where(d => !d.TryGetProperty("source", out _))
            .Select(d => d.GetProperty("id").GetString())
            .ToList();

        Assert.Equal(3, nodeIds.Count);
        Assert.Equal(nodeIds.Distinct().Count(), nodeIds.Count);
    }

    [Fact]
    public void ToCytoscapeJson_EmptyArray_ReturnsEmptyArray()
    {
        var results = JsonDocument.Parse("[]").RootElement;
        var output = GraphDataConverter.ToCytoscapeJson(results);
        var elements = JsonDocument.Parse(output).RootElement;

        Assert.Equal(0, elements.GetArrayLength());
    }

    [Fact]
    public void ToCytoscapeJson_GraphSONWrapped_UnwrapsCorrectly()
    {
        var json = """
        [
          {
            "@type": "g:Map",
            "@value": {
              "v": { "@type": "g:Vertex", "@value": { "id": "1", "label": "person" } },
              "e": { "@type": "g:Edge", "@value": { "id": "e1", "label": "created" } },
              "o": { "@type": "g:Vertex", "@value": { "id": "2", "label": "software" } }
            }
          }
        ]
        """;

        var results = JsonDocument.Parse(json).RootElement;
        var output = GraphDataConverter.ToCytoscapeJson(results);
        var elements = JsonDocument.Parse(output).RootElement;

        int nodeCount = 0, edgeCount = 0;
        foreach (var el in elements.EnumerateArray())
        {
            if (el.GetProperty("data").TryGetProperty("source", out _))
                edgeCount++;
            else
                nodeCount++;
        }

        Assert.Equal(2, nodeCount);
        Assert.Equal(1, edgeCount);
    }

    #endregion

    #region Force Graph (3D)

    [Fact]
    public void ToForceGraphJson_TraversalTriples_ProducesNodesAndLinks()
    {
        var json = """
        [
          {
            "v": { "id": "1", "label": "person", "properties": { "Name": [{ "value": "Alice" }] } },
            "e": { "id": "e1", "label": "knows" },
            "o": { "id": "2", "label": "person", "properties": { "Name": [{ "value": "Bob" }] } }
          }
        ]
        """;

        var results = JsonDocument.Parse(json).RootElement;
        var output = GraphDataConverter.ToForceGraphJson(results);
        var graph = JsonDocument.Parse(output).RootElement;

        var nodes = graph.GetProperty("nodes");
        var links = graph.GetProperty("links");

        Assert.Equal(2, nodes.GetArrayLength());
        Assert.Equal(1, links.GetArrayLength());

        var alice = nodes.EnumerateArray().First(n => n.GetProperty("id").GetString() == "1");
        Assert.Equal("Alice", alice.GetProperty("label").GetString());
        Assert.True(alice.TryGetProperty("group", out _));

        var link = links[0];
        Assert.Equal("1", link.GetProperty("source").GetString());
        Assert.Equal("2", link.GetProperty("target").GetString());
        Assert.Equal("knows", link.GetProperty("label").GetString());
    }

    [Fact]
    public void ToForceGraphJson_GroupsByVertexLabel()
    {
        var json = """
        [
          {
            "v": { "id": "1", "label": "person" },
            "e": { "id": "e1", "label": "created" },
            "o": { "id": "2", "label": "software" }
          },
          {
            "v": { "id": "3", "label": "person" },
            "e": { "id": "e2", "label": "created" },
            "o": { "id": "4", "label": "software" }
          }
        ]
        """;

        var results = JsonDocument.Parse(json).RootElement;
        var output = GraphDataConverter.ToForceGraphJson(results);
        var nodes = JsonDocument.Parse(output).RootElement.GetProperty("nodes");

        var groups = nodes.EnumerateArray()
            .Select(n => new { id = n.GetProperty("id").GetString(), group = n.GetProperty("group").GetInt32() })
            .ToList();

        var personGroup = groups.First(g => g.id == "1").group;
        var softwareGroup = groups.First(g => g.id == "2").group;

        Assert.NotEqual(personGroup, softwareGroup);
        Assert.Equal(personGroup, groups.First(g => g.id == "3").group);
        Assert.Equal(softwareGroup, groups.First(g => g.id == "4").group);
    }

    [Fact]
    public void ToForceGraphJson_EmptyArray_ReturnsEmptyGraph()
    {
        var results = JsonDocument.Parse("[]").RootElement;
        var output = GraphDataConverter.ToForceGraphJson(results);
        var graph = JsonDocument.Parse(output).RootElement;

        Assert.Equal(0, graph.GetProperty("nodes").GetArrayLength());
        Assert.Equal(0, graph.GetProperty("links").GetArrayLength());
    }

    [Fact]
    public void ToForceGraphJson_StandaloneVertices_CreatesNodesNoLinks()
    {
        var json = """
        [
          { "id": "v1", "label": "city" },
          { "id": "v2", "label": "city" }
        ]
        """;

        var results = JsonDocument.Parse(json).RootElement;
        var output = GraphDataConverter.ToForceGraphJson(results);
        var graph = JsonDocument.Parse(output).RootElement;

        Assert.Equal(2, graph.GetProperty("nodes").GetArrayLength());
        Assert.Equal(0, graph.GetProperty("links").GetArrayLength());
    }

    #endregion

    #region Table

    [Fact]
    public void ToTable_TraversalTriples_BuildsNodeAndEdgeRows()
    {
        var json = """
        [
          {
            "v": { "id": "1", "label": "Component", "properties": { "name": [{ "value": "Leg" }] } },
            "e": { "id": "e1", "label": "composes", "outV": "1", "inV": "2" },
            "o": { "id": "2", "label": "Assembly", "properties": { "name": [{ "value": "Table" }] } }
          }
        ]
        """;

        var table = GraphDataConverter.ToTable(JsonDocument.Parse(json).RootElement);

        Assert.Equal(2, table.Nodes.Count);
        Assert.Single(table.Edges);
        Assert.Contains("name", table.NodePropertyColumns);

        var leg = table.Nodes.First(n => n.Id == "1");
        Assert.Equal("Component", leg.Label);
        Assert.Equal("Leg", leg.Properties["name"]);

        var edge = table.Edges[0];
        Assert.Equal("composes", edge.Label);
        Assert.Equal("1", edge.Source);
        Assert.Equal("2", edge.Target);
    }

    [Fact]
    public void ToTable_IncludesIsolatedVertexWithNoEdges()
    {
        var json = """
        [
          { "id": "1", "label": "Component", "properties": { "name": [{ "value": "Leg" }] } },
          { "id": "9", "label": "asdasd", "properties": {} },
          { "id": "e1", "label": "composes", "outV": "1", "inV": "9" }
        ]
        """;

        var table = GraphDataConverter.ToTable(JsonDocument.Parse(json).RootElement);

        Assert.Equal(2, table.Nodes.Count);
        Assert.Contains(table.Nodes, n => n.Id == "9" && n.Label == "asdasd");
        Assert.Single(table.Edges);
    }

    [Fact]
    public void ToTable_CapturesGraphSonIdType()
    {
        var json = """
        [
          { "id": { "@type": "g:Int64", "@value": 1 }, "label": "person" },
          { "id": { "@type": "g:Int64", "@value": 2 }, "label": "person" },
          { "id": { "@type": "g:Int64", "@value": 727 }, "label": "knows", "outV": { "@type": "g:Int64", "@value": 1 }, "inV": { "@type": "g:Int64", "@value": 2 } }
        ]
        """;

        var table = GraphDataConverter.ToTable(JsonDocument.Parse(json).RootElement);

        var edge = table.Edges.Single();
        Assert.Equal("727", edge.Id);
        Assert.Equal("g:Int64", edge.IdType);
        Assert.All(table.Nodes, n => Assert.Equal("g:Int64", n.IdType));
    }

    [Fact]
    public void FindIdType_ReturnsEdgeAndVertexIdTypes()
    {
        var json = """
        [
          { "id": { "@type": "g:Int64", "@value": 1 }, "label": "person" },
          { "id": { "@type": "g:Int64", "@value": 727 }, "label": "knows", "outV": { "@type": "g:Int64", "@value": 1 }, "inV": { "@type": "g:Int64", "@value": 1 } }
        ]
        """;

        var data = JsonDocument.Parse(json).RootElement;

        Assert.Equal("g:Int64", GraphDataConverter.FindIdType(data, "727", isEdge: true));
        Assert.Equal("g:Int64", GraphDataConverter.FindIdType(data, "1", isEdge: false));
    }

    [Fact]
    public void FindIdType_UnknownId_ReturnsNull()
    {
        var json = """
        [
          { "id": { "@type": "g:Int64", "@value": 1 }, "label": "person" }
        ]
        """;

        var data = JsonDocument.Parse(json).RootElement;

        Assert.Null(GraphDataConverter.FindIdType(data, "999", isEdge: false));
    }

    [Fact]
    public void FindIdType_UntypedId_ReturnsNull()
    {
        //A bare id with no GraphSON @type yields null, so FormatId keeps its type-less heuristic.
        var json = """
        [
          { "id": "1", "label": "person" }
        ]
        """;

        var data = JsonDocument.Parse(json).RootElement;

        Assert.Null(GraphDataConverter.FindIdType(data, "1", isEdge: false));
    }

    #endregion

    #region Label Extraction

    [Fact]
    public void ToCytoscapeJson_ExtractsNameProperty_CaseInsensitive()
    {
        var json = """
        [
          { "id": "1", "label": "product", "properties": { "name": [{ "value": "Widget" }] } }
        ]
        """;

        var results = JsonDocument.Parse(json).RootElement;
        var output = GraphDataConverter.ToCytoscapeJson(results);
        var elements = JsonDocument.Parse(output).RootElement;

        var label = elements[0].GetProperty("data").GetProperty("label").GetString();
        Assert.Equal("Widget", label);
    }

    [Fact]
    public void ToCytoscapeJson_FallsBackToLabelAndId_WhenNoNameProperty()
    {
        var json = """
        [
          { "id": "42", "label": "device", "properties": {} }
        ]
        """;

        var results = JsonDocument.Parse(json).RootElement;
        var output = GraphDataConverter.ToCytoscapeJson(results);
        var elements = JsonDocument.Parse(output).RootElement;

        var label = elements[0].GetProperty("data").GetProperty("label").GetString();
        Assert.Equal("device (42)", label);
    }

    [Fact]
    public void VertexLabels_ReturnsDistinctTypeLabels()
    {
        var json = """
        [
          { "id": "1", "label": "person" },
          { "id": "2", "label": "person" },
          { "id": "3", "label": "product" }
        ]
        """;

        var labels = GraphDataConverter.VertexLabels(JsonDocument.Parse(json).RootElement);

        Assert.Equal(2, labels.Count);
        Assert.Contains("person", labels);
        Assert.Contains("product", labels);
    }

    [Fact]
    public void ToCytoscapeJson_DisplayPropertyOverride_UsesConfiguredProperty()
    {
        var json = """
        [ { "id": "1", "label": "person", "properties": { "name": "Alice", "email": "a@x.com" } } ]
        """;
        var styles = new Dictionary<string, LabelStyle> { ["person"] = new LabelStyle { DisplayProperty = "email" } };

        var withStyle = GraphDataConverter.ToCytoscapeJson(JsonDocument.Parse(json).RootElement, styles);
        var withoutStyle = GraphDataConverter.ToCytoscapeJson(JsonDocument.Parse(json).RootElement);

        Assert.Contains("\"label\":\"a@x.com\"", withStyle);
        Assert.Contains("\"label\":\"Alice\"", withoutStyle);
    }

    [Fact]
    public void ToCytoscapeJson_LabelStyle_EmitsColorAndSize()
    {
        var json = """
        [ { "id": "1", "label": "person", "properties": {} } ]
        """;
        var styles = new Dictionary<string, LabelStyle> { ["person"] = new LabelStyle { Color = "#ff0000", Size = 60 } };

        var result = GraphDataConverter.ToCytoscapeJson(JsonDocument.Parse(json).RootElement, styles);

        Assert.Contains("\"bgColor\":\"#ff0000\"", result);
        Assert.Contains("\"nodeSize\":60", result);
    }

    [Fact]
    public void ToForceGraphJson_LabelIcon_FillsImageWhenNoPerNodeImage()
    {
        var json = """
        [ { "id": "1", "label": "person", "properties": {} } ]
        """;
        var styles = new Dictionary<string, LabelStyle> { ["person"] = new LabelStyle { Icon = "http://x/i.png" } };

        var result = GraphDataConverter.ToForceGraphJson(JsonDocument.Parse(json).RootElement, styles);

        Assert.Contains("\"image\":\"http://x/i.png\"", result);
    }

    [Fact]
    public void ToCytoscapeJson_ImageShown_SetsImageData()
    {
        //gdbvImage renders only when the node is set to show it (its gdbvShow list names gdbvImage).
        var json = """
        [
          { "id": "1", "label": "person", "properties": { "gdbvImage": "https://x/i.png", "gdbvShow": "gdbvImage" } }
        ]
        """;

        var results = JsonDocument.Parse(json).RootElement;
        var elements = JsonDocument.Parse(GraphDataConverter.ToCytoscapeJson(results)).RootElement;

        var data = elements[0].GetProperty("data");
        Assert.Equal("https://x/i.png", data.GetProperty("image").GetString());
    }

    [Fact]
    public void ToCytoscapeJson_ImageNotShown_OmitsImageData()
    {
        //gdbvImage present but not in gdbvShow — the "show" toggle is off, so no image is drawn.
        var json = """
        [
          { "id": "1", "label": "person", "properties": { "gdbvImage": "https://x/i.png" } }
        ]
        """;

        var elements = JsonDocument.Parse(GraphDataConverter.ToCytoscapeJson(JsonDocument.Parse(json).RootElement)).RootElement;

        Assert.False(elements[0].GetProperty("data").TryGetProperty("image", out _));
    }

    [Fact]
    public void ToForceGraphJson_ModelShown_SetsModelData()
    {
        //A per-node gdbvModel maps to data["model"] (which the 3D view renders) only when shown.
        var json = """
        [ { "id": "1", "label": "part", "properties": { "gdbvModel": "https://x/m.obj", "gdbvShow": "gdbvModel" } } ]
        """;

        var node = JsonDocument.Parse(GraphDataConverter.ToForceGraphJson(JsonDocument.Parse(json).RootElement)).RootElement.GetProperty("nodes")[0];

        Assert.Equal("https://x/m.obj", node.GetProperty("model").GetString());
    }

    [Fact]
    public void ToForceGraphJson_ModelNotShown_OmitsModelData()
    {
        //gdbvModel present but not in gdbvShow — the model is hidden, so the 3D node stays a sphere.
        var json = """
        [ { "id": "1", "label": "part", "properties": { "gdbvModel": "https://x/m.obj" } } ]
        """;

        var node = JsonDocument.Parse(GraphDataConverter.ToForceGraphJson(JsonDocument.Parse(json).RootElement)).RootElement.GetProperty("nodes")[0];

        Assert.False(node.TryGetProperty("model", out _));
    }

    #endregion

    #region GraphSON unwrapping & property extraction

    [Fact]
    public void ToForceGraphJson_StandaloneEdge_CreatesNodesAndLink()
    {
        var json = """
        [
          { "id": "e1", "label": "route", "outV": "a", "inV": "b" }
        ]
        """;

        var graph = JsonDocument.Parse(GraphDataConverter.ToForceGraphJson(JsonDocument.Parse(json).RootElement)).RootElement;

        Assert.Equal(2, graph.GetProperty("nodes").GetArrayLength());
        Assert.Equal(1, graph.GetProperty("links").GetArrayLength());

        var link = graph.GetProperty("links")[0];
        Assert.Equal("a", link.GetProperty("source").GetString());
        Assert.Equal("b", link.GetProperty("target").GetString());
    }

    [Fact]
    public void ToCytoscapeJson_GraphSONInt64Ids_AreUnwrapped()
    {
        //Mirrors real TinkerPop output: ids are { "@type": "g:Int64", "@value": 2 }.
        var json = """
        [
          {
            "@type": "g:Vertex",
            "@value": {
              "id": { "@type": "g:Int64", "@value": 2 },
              "label": "Component",
              "properties": {
                "name": [ { "@type": "g:VertexProperty", "@value": { "value": "M3 Screw", "label": "name" } } ]
              }
            }
          }
        ]
        """;

        var elements = JsonDocument.Parse(GraphDataConverter.ToCytoscapeJson(JsonDocument.Parse(json).RootElement)).RootElement;
        var data = elements[0].GetProperty("data");

        Assert.Equal("2", data.GetProperty("id").GetString());
        Assert.Equal("M3 Screw", data.GetProperty("label").GetString());
    }

    [Fact]
    public void ToTable_ExtractsEdgeProperties()
    {
        var json = """
        [
          { "id": "1", "label": "person" },
          { "id": "2", "label": "person" },
          { "id": "e1", "label": "knows", "outV": "1", "inV": "2", "properties": { "weight": { "value": "0.5" } } }
        ]
        """;

        var table = GraphDataConverter.ToTable(JsonDocument.Parse(json).RootElement);

        Assert.Equal(2, table.Nodes.Count);
        Assert.Single(table.Edges);
        Assert.Contains("weight", table.EdgePropertyColumns);
        Assert.Equal("0.5", table.Edges[0].Properties["weight"]);
    }

    [Fact]
    public void UnwrapElement_UnwrapsGraphSONVertex()
    {
        var wrapped = JsonDocument.Parse(
            """{ "@type": "g:Vertex", "@value": { "id": "1", "label": "person" } }""").RootElement;

        var unwrapped = GraphDataConverter.UnwrapElement(wrapped);

        Assert.Equal("person", unwrapped.GetProperty("label").GetString());
    }

    [Fact]
    public void UnwrapElement_GMap_BecomesObject()
    {
        var wrapped = JsonDocument.Parse(
            """{ "@type": "g:Map", "@value": [ "k", "v" ] }""").RootElement;

        var unwrapped = GraphDataConverter.UnwrapElement(wrapped);

        Assert.Equal("v", unwrapped.GetProperty("k").GetString());
    }

    [Fact]
    public void ExtractProperties_UnwrapsArrayValues_AndSkipsMetadata()
    {
        var el = JsonDocument.Parse(
            """{ "id": "1", "label": "person", "properties": { "name": [ { "value": "Bob" } ], "age": [ { "value": "30" } ] } }""").RootElement;

        var props = GraphDataConverter.ExtractProperties(el);

        Assert.Equal("Bob", props["name"]);
        Assert.Equal("30", props["age"]);
        Assert.False(props.ContainsKey("id"));
        Assert.False(props.ContainsKey("label"));
    }

    #endregion

    #region CSV export

    [Fact]
    public void ToCsv_ProducesHeaderVerticesEdges_AndQuotesCommas()
    {
        var json = """
        [
          { "id": "1", "label": "person", "properties": { "name": [ { "value": "Bob" } ] } },
          { "id": "2", "label": "person", "properties": { "name": [ { "value": "Al,ice" } ] } },
          { "id": "e1", "label": "knows", "outV": "1", "inV": "2" }
        ]
        """;

        var table = GraphDataConverter.ToTable(JsonDocument.Parse(json).RootElement);
        var csv = GraphDataConverter.ToCsv(table);
        var lines = csv.Replace("\r\n", "\n").Trim().Split('\n');

        Assert.Equal("kind,id,label,source,target,name", lines[0]);
        Assert.Contains(lines, l => l.StartsWith("vertex,1,person,,,Bob"));
        Assert.Contains(lines, l => l.StartsWith("edge,e1,knows,1,2"));
        Assert.Contains(lines, l => l.Contains("\"Al,ice\""));
    }

    #endregion

    #region DOT export

    [Fact]
    public void ToDot_ProducesDigraphWithNodeNamesAndEdgeLabels()
    {
        var json = """
        [
          { "id": "1", "label": "person", "properties": { "name": [ { "value": "Bob" } ] } },
          { "id": "2", "label": "person", "properties": { "name": [ { "value": "Alice" } ] } },
          { "id": "e1", "label": "knows", "outV": "1", "inV": "2" }
        ]
        """;

        var table = GraphDataConverter.ToTable(JsonDocument.Parse(json).RootElement);
        var dot = GraphDataConverter.ToDot(table).Replace("\r\n", "\n");

        Assert.StartsWith("digraph G {", dot);
        Assert.EndsWith("}", dot.TrimEnd());
        Assert.Contains("\"1\" [label=\"Bob\"];", dot);
        Assert.Contains("\"2\" [label=\"Alice\"];", dot);
        Assert.Contains("\"1\" -> \"2\" [label=\"knows\"];", dot);
    }

    [Fact]
    public void ToDot_EscapesQuotes_AndFallsBackToTypeLabel()
    {
        var json = """
        [
          { "id": "n1", "label": "device", "properties": { "title": [ { "value": "a \"quoted\" name" } ] } },
          { "id": "n2", "label": "device" }
        ]
        """;

        var table = GraphDataConverter.ToTable(JsonDocument.Parse(json).RootElement);
        var dot = GraphDataConverter.ToDot(table);

        //Quotes inside a label are backslash-escaped so the DOT stays valid.
        Assert.Contains("a \\\"quoted\\\" name", dot);
        //A vertex with no name property falls back to its type label.
        Assert.Contains("\"n2\" [label=\"device\"];", dot);
    }

    #endregion

    #region Schema (data model)

    [Fact]
    public void BuildSchemaFromData_AggregatesLabelsKeysAndRelationships()
    {
        var json = """
        [
          { "id": "1", "label": "person", "properties": { "name": [{"value":"Alice"}], "age": [{"value":"30"}] } },
          { "id": "2", "label": "person", "properties": { "name": [{"value":"Bob"}] } },
          { "id": "3", "label": "company", "properties": { "name": [{"value":"Acme"}] } },
          { "id": "e1", "label": "knows", "outV": "1", "inV": "2" },
          { "id": "e2", "label": "worksAt", "outV": "1", "inV": "3" },
          { "id": "e3", "label": "worksAt", "outV": "2", "inV": "3" }
        ]
        """;

        var schemaJson = GraphDataConverter.BuildSchemaFromData(JsonDocument.Parse(json).RootElement);
        var schema = JsonDocument.Parse(schemaJson).RootElement;

        var nodes = new List<JsonElement>();
        var edges = new List<JsonElement>();
        foreach (var el in schema.EnumerateArray())
        {
            if (el.TryGetProperty("outV", out _))
                edges.Add(el);
            else
                nodes.Add(el);
        }

        //One node per vertex label; one edge per distinct (srcLabel, edgeLabel, tgtLabel).
        Assert.Equal(2, nodes.Count);
        Assert.Equal(2, edges.Count);

        var person = nodes.Single(n => n.GetProperty("id").GetString() == "person");
        Assert.Equal("2", person.GetProperty("properties").GetProperty("count").GetString());
        Assert.Equal("age, name", person.GetProperty("properties").GetProperty("keys").GetString());

        var company = nodes.Single(n => n.GetProperty("id").GetString() == "company");
        Assert.Equal("1", company.GetProperty("properties").GetProperty("count").GetString());

        Assert.Contains(edges, e => e.GetProperty("label").GetString() == "knows" && e.GetProperty("outV").GetString() == "person" && e.GetProperty("inV").GetString() == "person" && e.GetProperty("properties").GetProperty("count").GetString() == "1");
        Assert.Contains(edges, e => e.GetProperty("label").GetString() == "worksAt" && e.GetProperty("outV").GetString() == "person" && e.GetProperty("inV").GetString() == "company" && e.GetProperty("properties").GetProperty("count").GetString() == "2");
    }

    #endregion

    #region Pinned positions

    [Fact]
    public void ToForceGraphJson_EmitsFixedPositionsFrom3dReservedProps()
    {
        var json = """
        [
          { "id": "1", "label": "person", "properties": {
              "name": [{"value":"Alice"}],
              "gdbvX3d": [{"value":"100"}], "gdbvY3d": [{"value":"-50"}], "gdbvZ3d": [{"value":"25"}] } }
        ]
        """;

        var output = GraphDataConverter.ToForceGraphJson(JsonDocument.Parse(json).RootElement);
        var node = JsonDocument.Parse(output).RootElement.GetProperty("nodes")[0];

        Assert.Equal(100, node.GetProperty("fx").GetDouble());
        Assert.Equal(-50, node.GetProperty("fy").GetDouble());
        Assert.Equal(25, node.GetProperty("fz").GetDouble());
    }

    [Fact]
    public void PinnedPositions_2dAnd3dAreIndependent()
    {
        //Node 1 is pinned only in 2D (gdbvX/Y); node 2 only in 3D (gdbvX3d/Y3d/Z3d). Neither viewer
        //should pick up the other viewer's keys.
        var json = """
        [
          { "id": "1", "label": "person", "properties": {
              "gdbvX": [{"value":"12"}], "gdbvY": [{"value":"34"}] } },
          { "id": "2", "label": "person", "properties": {
              "gdbvX3d": [{"value":"5"}], "gdbvY3d": [{"value":"6"}], "gdbvZ3d": [{"value":"7"}] } }
        ]
        """;
        var data = JsonDocument.Parse(json).RootElement;

        var force = JsonDocument.Parse(GraphDataConverter.ToForceGraphJson(data)).RootElement.GetProperty("nodes");
        var n1Force = force.EnumerateArray().First(n => n.GetProperty("id").GetString() == "1");
        var n2Force = force.EnumerateArray().First(n => n.GetProperty("id").GetString() == "2");
        Assert.False(n1Force.TryGetProperty("fx", out _));//2D-only pin ignored in 3D
        Assert.Equal(5, n2Force.GetProperty("fx").GetDouble());

        var cyto = JsonDocument.Parse(GraphDataConverter.ToCytoscapeJson(data)).RootElement;
        var n1Cyto = cyto.EnumerateArray().First(e => e.GetProperty("data").GetProperty("id").GetString() == "1");
        var n2Cyto = cyto.EnumerateArray().First(e => e.GetProperty("data").GetProperty("id").GetString() == "2");
        Assert.True(n1Cyto.TryGetProperty("position", out _));
        Assert.False(n2Cyto.TryGetProperty("position", out _));//3D-only pin ignored in 2D
    }

    [Fact]
    public void ToCytoscapeJson_EmitsPresetPositionFromReservedProps()
    {
        var json = """
        [
          { "id": "1", "label": "person", "properties": {
              "gdbvX": [{"value":"12"}], "gdbvY": [{"value":"34"}] } }
        ]
        """;

        var output = GraphDataConverter.ToCytoscapeJson(JsonDocument.Parse(json).RootElement);
        var node = JsonDocument.Parse(output).RootElement[0];

        Assert.Equal(12, node.GetProperty("position").GetProperty("x").GetDouble());
        Assert.Equal(34, node.GetProperty("position").GetProperty("y").GetDouble());
        Assert.Equal(12, node.GetProperty("data").GetProperty("px").GetDouble());
    }

    [Fact]
    public void ToCytoscapeJson_NoReservedProps_OmitsPosition()
    {
        var json = """
        [ { "id": "1", "label": "person", "properties": { "name": [{"value":"Alice"}] } } ]
        """;

        var output = GraphDataConverter.ToCytoscapeJson(JsonDocument.Parse(json).RootElement);
        var node = JsonDocument.Parse(output).RootElement[0];

        Assert.False(node.TryGetProperty("position", out _));
    }

    #endregion

    #region Per-node styling (database)

    [Fact]
    public void ToCytoscapeJson_EmitsColorAndSizeFromPerNodeProps()
    {
        var json = """
        [ { "id": "1", "label": "person", "properties": {
            "gdbvColor": [{"value":"#e67e22"}], "gdbvSize": [{"value":"48"}] } } ]
        """;

        var result = GraphDataConverter.ToCytoscapeJson(JsonDocument.Parse(json).RootElement);

        Assert.Contains("\"bgColor\":\"#e67e22\"", result);
        Assert.Contains("\"nodeSize\":48", result);
    }

    [Fact]
    public void ToForceGraphJson_EmitsColorAndSizeFromPerNodeProps()
    {
        var json = """
        [ { "id": "1", "label": "person", "properties": {
            "gdbvColor": [{"value":"#123456"}], "gdbvSize": [{"value":"30"}] } } ]
        """;

        var result = GraphDataConverter.ToForceGraphJson(JsonDocument.Parse(json).RootElement);

        Assert.Contains("\"bgColor\":\"#123456\"", result);
        Assert.Contains("\"nodeSize\":30", result);
    }

    [Fact]
    public void ToCytoscapeJson_PerNodeColor_OverridesLabelStyle()
    {
        var json = """
        [ { "id": "1", "label": "person", "properties": { "gdbvColor": [{"value":"#e67e22"}] } } ]
        """;
        var styles = new Dictionary<string, LabelStyle> { ["person"] = new LabelStyle { Color = "#0000ff" } };

        var result = GraphDataConverter.ToCytoscapeJson(JsonDocument.Parse(json).RootElement, styles);

        Assert.Contains("\"bgColor\":\"#e67e22\"", result);
        Assert.DoesNotContain("#0000ff", result);
    }

    [Fact]
    public void ToCytoscapeJson_PerNodeDisplay_PicksThatPropertyAsLabel()
    {
        var json = """
        [ { "id": "1", "label": "person", "properties": {
            "name": [{"value":"Alice"}], "code": [{"value":"A-42"}],
            "gdbvDisplay": [{"value":"code"}] } } ]
        """;

        var output = GraphDataConverter.ToCytoscapeJson(JsonDocument.Parse(json).RootElement);
        var node = JsonDocument.Parse(output).RootElement[0];

        Assert.Equal("A-42", node.GetProperty("data").GetProperty("label").GetString());
    }

    #endregion

    #region Search suggestions

    [Fact]
    public void SearchSuggestions_ReturnsDistinctSortedDisplayLabels()
    {
        var json = """
        [
          {
            "v": { "id": "1", "label": "person", "properties": { "Name": [{ "value": "Bob" }] } },
            "e": { "id": "e1", "label": "knows" },
            "o": { "id": "2", "label": "person", "properties": { "Name": [{ "value": "Alice" }] } }
          },
          {
            "v": { "id": "2", "label": "person", "properties": { "Name": [{ "value": "Alice" }] } },
            "e": { "id": "e2", "label": "made" },
            "o": { "id": "3", "label": "software", "properties": { "Name": [{ "value": "Gremlin" }] } }
          }
        ]
        """;

        var suggestions = GraphDataConverter.SearchSuggestions(JsonDocument.Parse(json).RootElement);

        //Distinct (Alice appears in both triples) and sorted case-insensitively.
        Assert.Equal(new[] { "Alice", "Bob", "Gremlin" }, suggestions);
    }

    [Fact]
    public void SearchSuggestions_StandaloneVertices_UsesDisplayLabels()
    {
        var json = """
        [
          { "id": "v1", "label": "city", "properties": { "name": [{ "value": "Seattle" }] } },
          { "id": "v2", "label": "city", "properties": { "name": [{ "value": "Portland" }] } }
        ]
        """;

        var suggestions = GraphDataConverter.SearchSuggestions(JsonDocument.Parse(json).RootElement);

        Assert.Equal(new[] { "Portland", "Seattle" }, suggestions);
    }

    [Fact]
    public void SearchSuggestions_DedupesByText_AcrossDifferentVertices()
    {
        var json = """
        [
          { "id": "v1", "label": "material", "properties": { "name": [{ "value": "Paint" }] } },
          { "id": "v2", "label": "material", "properties": { "name": [{ "value": "Paint" }] } }
        ]
        """;

        var suggestions = GraphDataConverter.SearchSuggestions(JsonDocument.Parse(json).RootElement);

        Assert.Equal(new[] { "Paint" }, suggestions);
    }

    [Fact]
    public void SearchSuggestions_NonArray_ReturnsEmpty()
    {
        var el = JsonDocument.Parse("{}").RootElement;

        Assert.Empty(GraphDataConverter.SearchSuggestions(el));
    }

    [Fact]
    public void SearchSuggestions_RespectsMaxCap()
    {
        var items = Enumerable.Range(0, 10)
            .Select(i => $$"""{ "id": "v{{i}}", "label": "n", "properties": { "name": [{ "value": "node{{i}}" }] } }""");
        var json = "[" + string.Join(",", items) + "]";

        var suggestions = GraphDataConverter.SearchSuggestions(JsonDocument.Parse(json).RootElement, max: 3);

        Assert.Equal(3, suggestions.Count);
    }

    #endregion

    #region Node shape (2D / 3D)

    [Fact]
    public void ToCytoscapeJson_DefaultShape_IsRectangle()
    {
        var json = """[ { "id": "1", "label": "person", "properties": {} } ]""";

        var node = JsonDocument.Parse(GraphDataConverter.ToCytoscapeJson(JsonDocument.Parse(json).RootElement)).RootElement[0];

        Assert.Equal("rectangle", node.GetProperty("data").GetProperty("shape").GetString());
    }

    [Fact]
    public void ToForceGraphJson_DefaultShape3d_IsSphere()
    {
        var json = """[ { "id": "1", "label": "person", "properties": {} } ]""";

        var node = JsonDocument.Parse(GraphDataConverter.ToForceGraphJson(JsonDocument.Parse(json).RootElement)).RootElement.GetProperty("nodes")[0];

        Assert.Equal("sphere", node.GetProperty("shape3d").GetString());
    }

    [Fact]
    public void ToCytoscapeJson_LabelStyleShape_EmitsShape()
    {
        var json = """[ { "id": "1", "label": "person", "properties": {} } ]""";
        var styles = new Dictionary<string, LabelStyle> { ["person"] = new LabelStyle { Shape = "hexagon" } };

        var node = JsonDocument.Parse(GraphDataConverter.ToCytoscapeJson(JsonDocument.Parse(json).RootElement, styles)).RootElement[0];

        Assert.Equal("hexagon", node.GetProperty("data").GetProperty("shape").GetString());
    }

    [Fact]
    public void ToCytoscapeJson_PerNodeShape_OverridesLabelShape()
    {
        var json = """[ { "id": "1", "label": "person", "properties": { "gdbvShape": [{"value":"circle"}] } } ]""";
        var styles = new Dictionary<string, LabelStyle> { ["person"] = new LabelStyle { Shape = "hexagon" } };

        var node = JsonDocument.Parse(GraphDataConverter.ToCytoscapeJson(JsonDocument.Parse(json).RootElement, styles)).RootElement[0];

        Assert.Equal("circle", node.GetProperty("data").GetProperty("shape").GetString());
    }

    [Fact]
    public void ToForceGraphJson_Shape3d_FromPerNodeProperty()
    {
        var json = """[ { "id": "1", "label": "person", "properties": { "gdbvShape3d": [{"value":"pyramid"}] } } ]""";

        var result = GraphDataConverter.ToForceGraphJson(JsonDocument.Parse(json).RootElement);

        Assert.Contains("\"shape3d\":\"pyramid\"", result);
    }

    [Fact]
    public void ToForceGraphJson_2dAnd3dShapes_AreIndependent()
    {
        //The 2D canvas shape and the 3D solid are separate settings on the label style.
        var json = """[ { "id": "1", "label": "person", "properties": {} } ]""";
        var styles = new Dictionary<string, LabelStyle> { ["person"] = new LabelStyle { Shape = "circle", Shape3d = "cube" } };

        var node = JsonDocument.Parse(GraphDataConverter.ToForceGraphJson(JsonDocument.Parse(json).RootElement, styles)).RootElement.GetProperty("nodes")[0];

        Assert.Equal("circle", node.GetProperty("shape").GetString());
        Assert.Equal("cube", node.GetProperty("shape3d").GetString());
    }

    #endregion

    #region Shown properties (gdbvShow)

    [Fact]
    public void ToCytoscapeJson_ShownProperties_EmitsSelectedKeyValueLines()
    {
        var json = """
        [ { "id": "1", "label": "person", "properties": {
            "name": [{"value":"Bob"}], "age": [{"value":"27"}], "city": [{"value":"Paris"}],
            "gdbvShow": [{"value":"name,age"}] } } ]
        """;

        var node = JsonDocument.Parse(GraphDataConverter.ToCytoscapeJson(JsonDocument.Parse(json).RootElement)).RootElement[0];
        var shown = node.GetProperty("data").GetProperty("showProps").EnumerateArray().Select(e => e.GetString()).ToList();

        //Only the keys named in gdbvShow, in that order — "city" is excluded.
        Assert.Equal(new[] { "name: Bob", "age: 27" }, shown);
    }

    [Fact]
    public void ToCytoscapeJson_NoShowList_OmitsShowProps()
    {
        var json = """[ { "id": "1", "label": "person", "properties": { "name": [{"value":"Bob"}] } } ]""";

        var node = JsonDocument.Parse(GraphDataConverter.ToCytoscapeJson(JsonDocument.Parse(json).RootElement)).RootElement[0];

        Assert.False(node.GetProperty("data").TryGetProperty("showProps", out _));
    }

    [Fact]
    public void ToForceGraphJson_ShownProperties_EmitsShowProps()
    {
        var json = """
        [ { "id": "1", "label": "person", "properties": { "name": [{"value":"Bob"}], "gdbvShow": [{"value":"name"}] } } ]
        """;

        var node = JsonDocument.Parse(GraphDataConverter.ToForceGraphJson(JsonDocument.Parse(json).RootElement)).RootElement.GetProperty("nodes")[0];
        var shown = node.GetProperty("showProps").EnumerateArray().Select(e => e.GetString()).ToList();

        Assert.Equal(new[] { "name: Bob" }, shown);
    }

    [Fact]
    public void ShowList_WithImageAndModel_ExcludesThemFromShowProps()
    {
        //gdbvImage / gdbvModel share the gdbvShow list but render as the image / model itself, so they
        //must never appear in the text lines drawn beneath the node — only the real property does.
        var json = """
        [ { "id": "1", "label": "part", "properties": {
            "name": [{"value":"Bolt"}], "gdbvImage": "https://x/i.png", "gdbvModel": "https://x/m.obj",
            "gdbvShow": "name,gdbvImage,gdbvModel" } } ]
        """;

        var node = JsonDocument.Parse(GraphDataConverter.ToCytoscapeJson(JsonDocument.Parse(json).RootElement)).RootElement[0];
        var shown = node.GetProperty("data").GetProperty("showProps").EnumerateArray().Select(e => e.GetString()).ToList();

        Assert.Equal(new[] { "name: Bolt" }, shown);
    }

    #endregion
}
