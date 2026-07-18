using System.Text.Json;
using GraphDBViewerWeb.Code;

namespace GraphDBViewerWeb.Tests;

///<summary>
///Covers the read-only query tool handed to a tool-using model. Only testable because the runner takes
///IGraphDb — a fake stands in for the database, so the mutation guard and the output bounds can be
///checked without a live server.
///</summary>
public class GremlinToolRunnerTests
{
    private sealed class FakeGraphDb : IGraphDb
    {
        private readonly GraphDbResult _result;

        public FakeGraphDb(GraphDbResult result)
        {
            _result = result;
        }

        public string LastQuery { get; private set; }
        public int Calls { get; private set; }

        public Task<GraphDbResult> ExecuteAsync(string query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            Calls++;

            return Task.FromResult(_result);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private static FakeGraphDb Returning(string json)
    {
        return new FakeGraphDb(GraphDbResult.Success(JsonDocument.Parse(json).RootElement));
    }

    private static string Args(string query)
    {
        return JsonSerializer.Serialize(new { query });
    }

    [Fact]
    public async Task RunsAReadOnlyQueryAndReturnsItsResult()
    {
        var db = Returning("""[{"id":"1"}]""");
        var runner = new GremlinToolRunner(db);

        var output = await runner.RunToolAsync("run_read_query", Args("g.V().limit(1)"), default);

        Assert.Equal("g.V().limit(1)", db.LastQuery);
        Assert.Contains("\"id\"", output);
    }

    [Fact]
    public async Task RefusesAMutatingQueryWithoutTouchingTheDatabase()
    {
        //The guard has to run before the query does — the point is that the model can't mutate the graph.
        var db = Returning("[]");
        var runner = new GremlinToolRunner(db);

        var output = await runner.RunToolAsync("run_read_query", Args("g.addV('person')"), default);

        Assert.Equal(0, db.Calls);
        Assert.Contains("mutates the graph", output);
    }

    [Fact]
    public async Task UnknownToolNameIsReported()
    {
        var db = Returning("[]");
        var runner = new GremlinToolRunner(db);

        var output = await runner.RunToolAsync("delete_everything", Args("g.V()"), default);

        Assert.Equal(0, db.Calls);
        Assert.Contains("Unknown tool", output);
    }

    [Fact]
    public async Task MissingQueryArgumentIsReported()
    {
        var db = Returning("[]");
        var runner = new GremlinToolRunner(db);

        var output = await runner.RunToolAsync("run_read_query", "{}", default);

        Assert.Equal(0, db.Calls);
        Assert.Contains("no 'query' argument", output);
    }

    [Fact]
    public async Task MalformedArgumentsAreReportedRatherThanThrowing()
    {
        var db = Returning("[]");
        var runner = new GremlinToolRunner(db);

        var output = await runner.RunToolAsync("run_read_query", "not json at all", default);

        Assert.Equal(0, db.Calls);
        Assert.Contains("no 'query' argument", output);
    }

    [Fact]
    public async Task AFailedQueryComesBackAsAnError()
    {
        var db = new FakeGraphDb(GraphDbResult.Failure("syntax error at line 1"));
        var runner = new GremlinToolRunner(db);

        var output = await runner.RunToolAsync("run_read_query", Args("g.V(("), default);

        Assert.Equal("Query error: syntax error at line 1", output);
    }

    [Fact]
    public async Task ALargeResultIsTruncatedSoItCantBlowTheModelsContext()
    {
        var big = "[" + string.Join(",", Enumerable.Range(0, 2000).Select(i => $"\"item-{i}\"")) + "]";
        var db = Returning(big);
        var runner = new GremlinToolRunner(db);

        var output = await runner.RunToolAsync("run_read_query", Args("g.V()"), default);

        Assert.EndsWith("…(truncated)", output);
        Assert.True(output.Length < big.Length);
    }

    [Fact]
    public void TheToolIsDeclaredWithAQueryArgument()
    {
        var runner = new GremlinToolRunner(Returning("[]"));

        var tool = Assert.Single(runner.Tools);
        Assert.Equal("run_read_query", tool.Name);
        Assert.Contains("\"query\"", tool.InputSchemaJson);
    }
}
