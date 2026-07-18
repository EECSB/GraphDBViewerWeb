using GraphDBViewerWeb.Code;

namespace GraphDBViewerWeb.Tests;

///<summary>
///Verifies cancellation propagates out of the database clients (rather than being swallowed into a
///failed result). Both use an already-canceled token, so no live server is needed — the request throws
///before any network call.
///</summary>
public class GraphDbCancellationTests
{
    [Fact]
    public async Task GremlinExecuteAsync_HttpWithCanceledToken_ThrowsOperationCanceled()
    {
        var connection = new GremlinDB.GremlinConnection("HTTP", 8182, false, "localhost", "", "", "");
        await using var db = new GremlinDB(new HttpClient(), connection);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.ExecuteAsync("g.V()", cts.Token));
    }

    [Fact]
    public async Task SparqlExecuteAsync_WithCanceledToken_ThrowsOperationCanceled()
    {
        //A blank Endpoint short-circuits to a failed result before the try, where the token is never
        //observed — so this needs a real (unreachable is fine) URL to reach the cancellation path.
        var connection = new GremlinDB.GremlinConnection
        {
            DatabaseType = "Sparql",
            Endpoint = "http://localhost:1/sparql"
        };
        await using var db = new SparqlDb(new HttpClient(), connection);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.ExecuteAsync("SELECT * WHERE { ?s ?p ?o }", cts.Token));
    }
}
