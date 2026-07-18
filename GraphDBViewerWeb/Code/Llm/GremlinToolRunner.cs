using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GraphDBViewerWeb.Code;

///<summary>
///Exposes a single read-only tool — <c>run_read_query</c> — that lets the model execute a Gremlin
///traversal against the connected graph and see the result, so it can explore the data and verify a query
///before answering. Mutating queries are refused (reusing the debugger's mutation guard).
///</summary>
public class GremlinToolRunner : ILlmToolRunner
{
    private const int MaxResultChars = 4000;

    //Takes the interface rather than the Gremlin client: the queries it runs are still Gremlin, but this
    //only needs "something that runs a query", which also makes it testable without a live database.
    private readonly IGraphDb _db;

    public GremlinToolRunner(IGraphDb db)
    {
        _db = db;
    }

    public IReadOnlyList<LlmTool> Tools { get; } = new List<LlmTool>
    {
        new LlmTool
        {
            Name = "run_read_query",
            Description = "Run a READ-ONLY Gremlin query against the connected graph and get the JSON result. "
                + "Use it to explore the data (labels, property values, counts) and to verify your query returns "
                + "what the user wants before giving your final answer. Mutations (addV/addE/drop/property/merge) are rejected.",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\",\"description\":\"The read-only Gremlin traversal to run, e.g. g.V().hasLabel('Product').limit(3).valueMap(true)\"}},\"required\":[\"query\"]}"
        }
    };

    public async Task<string> RunToolAsync(string name, string argumentsJson, CancellationToken ct)
    {
        if (name != "run_read_query")
            return $"Unknown tool: {name}";

        var query = ExtractQuery(argumentsJson);

        if (string.IsNullOrWhiteSpace(query))
            return "Error: no 'query' argument was provided.";

        if (GremlinStepParser.IsMutating(query))
            return "Error: that query mutates the graph, which is not allowed here. Only read-only queries can be run.";

        var result = await _db.ExecuteAsync(query, ct);

        if (result.IsError)
            return $"Query error: {result.Error}";

        var text = result.ToString();

        //Bound the tool output so a large result can't blow the model's context window.
        if (text.Length > MaxResultChars)
            text = text.Substring(0, MaxResultChars) + "\n…(truncated)";

        return text;
    }

    private static string ExtractQuery(string argumentsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);

            if (doc.RootElement.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String)
                return q.GetString();
        }
        catch { }

        return null;
    }
}
