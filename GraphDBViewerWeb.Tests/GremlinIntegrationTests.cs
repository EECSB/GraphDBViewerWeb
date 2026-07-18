using System.Text.Json;
using GraphDBViewerWeb.Code;

namespace GraphDBViewerWeb.Tests;

///<summary>
///Integration tests that connect to a live Gremlin server, seed a small test graph
///(if it doesn't already exist), and verify queries + data conversion.
///
///Configure via environment variables:
///GREMLIN_HOST      (default: 192.168.1.5)
///GREMLIN_PORT      (default: 8182)
///GREMLIN_TRANSPORT (default: WebSocket)
///
///Skip these tests when no server is available by setting:
///SKIP_GREMLIN_TESTS=true
///</summary>
[Collection("Gremlin")]
public class GremlinIntegrationTests : IAsyncLifetime
{
    private GremlinDB _gremlin = null!;
    private readonly HttpClient _http = new();

    private static string Host => Environment.GetEnvironmentVariable("GREMLIN_HOST") ?? "192.168.1.5";
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("GREMLIN_PORT"), out var p) ? p : 8182;
    private static string Transport => Environment.GetEnvironmentVariable("GREMLIN_TRANSPORT") ?? "WebSocket";

    private static bool ShouldSkip =>
        string.Equals(Environment.GetEnvironmentVariable("SKIP_GREMLIN_TESTS"), "true", StringComparison.OrdinalIgnoreCase);

    //Sentinel property value to identify our test vertices
    private const string TestMarker = "__graphdbviewer_test__";

    public async Task InitializeAsync()
    {
        if (ShouldSkip)
            return;

        var connection = new GremlinDB.GremlinConnection(Transport, Port, false, Host, string.Empty, string.Empty, string.Empty);
        _gremlin = new GremlinDB(_http, connection);

        await SeedTestGraphAsync();
    }

    public async Task DisposeAsync()
    {
        if (_gremlin is not null)
            await _gremlin.DisposeAsync();
        _http.Dispose();
    }

    private bool IsServerAvailable => !ShouldSkip && _gremlin is not null;

    //── Seed ──────────────────────────────────────────────────────────

    private async Task SeedTestGraphAsync()
    {
        //Check if test graph already exists
        var check = await _gremlin.ExecuteAsync(
            $"g.V().has('_testMarker', '{TestMarker}').count()");

        if (!check.IsError)
        {
            long count = ExtractCount(check.Data);
            if (count >= 4)
                return;//already seeded
        }

        //Drop any partial leftovers and recreate
        await _gremlin.ExecuteAsync($"g.V().has('_testMarker', '{TestMarker}').drop()");

        //Vertices: 4 people in a small social network
        var addVertices = new List<string>
        {
            $"g.addV('person').property('name', 'Alice').property('age', 30).property('_testMarker', '{TestMarker}')",
            $"g.addV('person').property('name', 'Bob').property('age', 27).property('_testMarker', '{TestMarker}')",
            $"g.addV('person').property('name', 'Charlie').property('age', 35).property('_testMarker', '{TestMarker}')",
            $"g.addV('software').property('name', 'GraphDB Viewer').property('lang', 'C#').property('_testMarker', '{TestMarker}')"
        };
        await _gremlin.ExecuteManyAsync(addVertices);

        //Edges: knows + created relationships
        var addEdges = new List<string>
        {
            $"g.V().has('name', 'Alice').has('_testMarker', '{TestMarker}').as('a').V().has('name', 'Bob').has('_testMarker', '{TestMarker}').addE('knows').from('a').property('since', 2020)",
            $"g.V().has('name', 'Alice').has('_testMarker', '{TestMarker}').as('a').V().has('name', 'Charlie').has('_testMarker', '{TestMarker}').addE('knows').from('a').property('since', 2019)",
            $"g.V().has('name', 'Bob').has('_testMarker', '{TestMarker}').as('b').V().has('name', 'Charlie').has('_testMarker', '{TestMarker}').addE('knows').from('b').property('since', 2021)",
            $"g.V().has('name', 'Alice').has('_testMarker', '{TestMarker}').as('a').V().has('name', 'GraphDB Viewer').has('_testMarker', '{TestMarker}').addE('created').from('a').property('weight', 1.0)",
            $"g.V().has('name', 'Bob').has('_testMarker', '{TestMarker}').as('b').V().has('name', 'GraphDB Viewer').has('_testMarker', '{TestMarker}').addE('created').from('b').property('weight', 0.5)"
        };
        await _gremlin.ExecuteManyAsync(addEdges);
    }

    private static long ExtractCount(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Number)
            return data.GetInt64();

        if (data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
        {
            var first = data[0];
            if (first.ValueKind == JsonValueKind.Number)
                return first.GetInt64();
            if (first.TryGetProperty("@value", out var inner) && inner.ValueKind == JsonValueKind.Number)
                return inner.GetInt64();
        }

        return 0;
    }

    //── Connection / Basic Queries ────────────────────────────────────

    [Fact]
    public async Task Connection_CanExecuteSimpleQuery()
    {
        if (!IsServerAvailable)
            return;

        var result = await _gremlin.ExecuteAsync("g.V().limit(1)");

        Assert.False(result.IsError, $"Expected success but got: {result.Error}");
    }

    [Fact]
    public async Task Query_VertexCount_ReturnsNumber()
    {
        if (!IsServerAvailable)
            return;

        var result = await _gremlin.ExecuteAsync(
            $"g.V().has('_testMarker', '{TestMarker}').count()");

        Assert.False(result.IsError, result.Error);
        long count = ExtractCount(result.Data);
        Assert.True(count >= 4, $"Expected at least 4 test vertices, got {count}");
    }

    [Fact]
    public async Task Query_EdgeCount_ReturnsNumber()
    {
        if (!IsServerAvailable)
            return;

        var result = await _gremlin.ExecuteAsync(
            $"g.V().has('_testMarker', '{TestMarker}').bothE().dedup().count()");

        Assert.False(result.IsError, result.Error);
        long count = ExtractCount(result.Data);
        Assert.True(count >= 5, $"Expected at least 5 test edges, got {count}");
    }

    //── Vertex Queries ────────────────────────────────────────────────

    [Fact]
    public async Task Query_GetVerticesByLabel_ReturnsPeople()
    {
        if (!IsServerAvailable)
            return;

        var result = await _gremlin.ExecuteAsync(
            $"g.V().has('_testMarker', '{TestMarker}').hasLabel('person')");

        Assert.False(result.IsError, result.Error);
        Assert.Equal(JsonValueKind.Array, result.Data.ValueKind);
        Assert.True(result.Data.GetArrayLength() >= 3, "Expected at least 3 person vertices");
    }

    [Fact]
    public async Task Query_GetVertexByName_FindsAlice()
    {
        if (!IsServerAvailable)
            return;

        var result = await _gremlin.ExecuteAsync(
            $"g.V().has('_testMarker', '{TestMarker}').has('name', 'Alice').valueMap(true)");

        Assert.False(result.IsError, result.Error);
        Assert.Equal(JsonValueKind.Array, result.Data.ValueKind);
        Assert.True(result.Data.GetArrayLength() >= 1, "Expected Alice vertex in results");
    }

    //── Edge / Traversal Queries ──────────────────────────────────────

    [Fact]
    public async Task Query_TraversalTriple_ReturnsVEO()
    {
        if (!IsServerAvailable)
            return;

        var result = await _gremlin.ExecuteAsync(
            $"g.V().has('_testMarker', '{TestMarker}').as('v').bothE().as('e').otherV().as('o').select('v','e','o')");

        Assert.False(result.IsError, result.Error);
        Assert.Equal(JsonValueKind.Array, result.Data.ValueKind);
        Assert.True(result.Data.GetArrayLength() >= 1, "Expected at least one traversal triple");
    }

    [Fact]
    public async Task Query_OutEdgesFromAlice_FindsKnowsAndCreated()
    {
        if (!IsServerAvailable)
            return;

        var result = await _gremlin.ExecuteAsync(
            $"g.V().has('_testMarker', '{TestMarker}').has('name', 'Alice').outE().label()");

        Assert.False(result.IsError, result.Error);
        Assert.Equal(JsonValueKind.Array, result.Data.ValueKind);

        var labels = new List<string>();
        foreach (var item in result.Data.EnumerateArray())
        {
            string? val;
            if (item.ValueKind == JsonValueKind.String)
                val = item.GetString();
            else if (item.TryGetProperty("@value", out var inner))
                val = inner.GetString();
            else
                val = item.ToString();

            if (val != null)
                labels.Add(val);
        }

        Assert.Contains("knows", labels);
        Assert.Contains("created", labels);
    }

    [Fact]
    public async Task Query_PathFromAliceToBob_ReturnsPath()
    {
        if (!IsServerAvailable)
            return;

        var result = await _gremlin.ExecuteAsync(
            $"g.V().has('_testMarker', '{TestMarker}').has('name', 'Alice').out('knows').has('name', 'Bob').path()");

        Assert.False(result.IsError, result.Error);
        Assert.Equal(JsonValueKind.Array, result.Data.ValueKind);
        Assert.True(result.Data.GetArrayLength() >= 1, "Expected a path from Alice to Bob");
    }

    //── Converter Integration (live data → Cytoscape / ForceGraph) ───

    [Fact]
    public async Task Converter_LiveTraversalTriples_ProducesValidCytoscapeJson()
    {
        if (!IsServerAvailable)
            return;

        var result = await _gremlin.ExecuteAsync(
            $"g.V().has('_testMarker', '{TestMarker}').as('v').bothE().as('e').otherV().as('o').select('v','e','o')");
        Assert.False(result.IsError, result.Error);

        var cytoscapeJson = GraphDataConverter.ToCytoscapeJson(result.Data);
        var elements = JsonDocument.Parse(cytoscapeJson).RootElement;

        Assert.Equal(JsonValueKind.Array, elements.ValueKind);
        Assert.True(elements.GetArrayLength() > 0, "Cytoscape output should have elements");

        int nodeCount = 0, edgeCount = 0;
        foreach (var el in elements.EnumerateArray())
        {
            var data = el.GetProperty("data");
            Assert.True(data.TryGetProperty("id", out _), "Every element needs an id");

            if (data.TryGetProperty("source", out _))
            {
                Assert.True(data.TryGetProperty("target", out _), "Edge needs target");
                Assert.True(data.TryGetProperty("label", out _), "Edge needs label");
                edgeCount++;
            }
            else
            {
                Assert.True(data.TryGetProperty("label", out _), "Node needs label");
                nodeCount++;
            }
        }

        Assert.True(nodeCount >= 2, $"Expected at least 2 nodes, got {nodeCount}");
        Assert.True(edgeCount >= 1, $"Expected at least 1 edge, got {edgeCount}");
    }

    [Fact]
    public async Task Converter_LiveTraversalTriples_ProducesValidForceGraphJson()
    {
        if (!IsServerAvailable)
            return;

        var result = await _gremlin.ExecuteAsync(
            $"g.V().has('_testMarker', '{TestMarker}').as('v').bothE().as('e').otherV().as('o').select('v','e','o')");
        Assert.False(result.IsError, result.Error);

        var forceJson = GraphDataConverter.ToForceGraphJson(result.Data);
        var graph = JsonDocument.Parse(forceJson).RootElement;

        var nodes = graph.GetProperty("nodes");
        var links = graph.GetProperty("links");

        Assert.True(nodes.GetArrayLength() >= 2, $"Expected at least 2 nodes, got {nodes.GetArrayLength()}");
        Assert.True(links.GetArrayLength() >= 1, $"Expected at least 1 link, got {links.GetArrayLength()}");

        foreach (var node in nodes.EnumerateArray())
        {
            Assert.True(node.TryGetProperty("id", out _), "Node needs id");
            Assert.True(node.TryGetProperty("label", out _), "Node needs label");
            Assert.True(node.TryGetProperty("group", out _), "Node needs group");
        }

        foreach (var link in links.EnumerateArray())
        {
            Assert.True(link.TryGetProperty("source", out _), "Link needs source");
            Assert.True(link.TryGetProperty("target", out _), "Link needs target");
            Assert.True(link.TryGetProperty("label", out _), "Link needs label");
        }
    }

    [Fact]
    public async Task Converter_LiveVertexOnly_ProducesNodesNoEdges()
    {
        if (!IsServerAvailable)
            return;

        var result = await _gremlin.ExecuteAsync(
            $"g.V().has('_testMarker', '{TestMarker}')");
        Assert.False(result.IsError, result.Error);

        var forceJson = GraphDataConverter.ToForceGraphJson(result.Data);
        var graph = JsonDocument.Parse(forceJson).RootElement;

        Assert.True(graph.GetProperty("nodes").GetArrayLength() >= 4);
        Assert.Equal(0, graph.GetProperty("links").GetArrayLength());
    }

    //── Error Handling ────────────────────────────────────────────────

    [Fact]
    public async Task Query_InvalidGremlin_ReturnsError()
    {
        if (!IsServerAvailable)
            return;

        var result = await _gremlin.ExecuteAsync("this is not valid gremlin");

        Assert.True(result.IsError, "Expected an error for invalid Gremlin syntax");
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public async Task Query_EmptyResult_ReturnsEmptyOrNoContent()
    {
        if (!IsServerAvailable)
            return;

        var result = await _gremlin.ExecuteAsync(
            "g.V().has('_nonexistent_property_xyz', 'impossible_value_abc')");

        //TinkerPop returns 204 No Content for empty results, which is treated as an error by GremlinDB.
        //Either an empty array or a 204-based error is acceptable.
        if (result.IsError)
        {
            Assert.Contains("204", result.Error);
        }
        else
        {
            Assert.Equal(JsonValueKind.Array, result.Data.ValueKind);
            Assert.Equal(0, result.Data.GetArrayLength());
        }
    }

    //── Batch Execution ──────────────────────────────────────────────

    [Fact]
    public async Task ExecuteMany_RunsMultipleQueries()
    {
        if (!IsServerAvailable)
            return;

        var queries = new Dictionary<string, string>
        {
            ["vertexCount"] = $"g.V().has('_testMarker', '{TestMarker}').count()",
            ["edgeCount"] = $"g.V().has('_testMarker', '{TestMarker}').bothE().dedup().count()",
            ["labels"] = $"g.V().has('_testMarker', '{TestMarker}').label().dedup()"
        };

        var results = await _gremlin.ExecuteManyAsync(queries);

        Assert.Equal(3, results.Count);
        Assert.False(results["vertexCount"].IsError, results["vertexCount"].Error);
        Assert.False(results["edgeCount"].IsError, results["edgeCount"].Error);
        Assert.False(results["labels"].IsError, results["labels"].Error);
    }
}
