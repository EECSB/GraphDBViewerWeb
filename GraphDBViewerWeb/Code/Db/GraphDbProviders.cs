using System;
using System.Collections.Generic;
using System.Net.Http;

namespace GraphDBViewerWeb.Code;

///<summary>
///What a database can do, beyond running a query. The UI asks these instead of asking which engine it
///is talking to, so a new provider turns features on as it grows them rather than the page learning its
///name. They describe the user-facing feature, not the dialect it happens to be built from today.
///</summary>
public sealed class GraphDbCapabilities
{
    ///<summary>Load DB, DB schema, the query limit — the whole graph can be enumerated and its schema reported.</summary>
    public bool BrowseGraph { get; init; }

    ///<summary>Expand-on-double-click and a selected element's in/out edge lists — traversal from a known id.</summary>
    public bool Traverse { get; init; }

    ///<summary>The Generated tab, Commit, Save positions, Database cleanup — edits can be staged as queries and run.</summary>
    public bool StageEdits { get; init; }

    ///<summary>Run each line — the editor's text can be split into independently executed statements.</summary>
    public bool MultiStatement { get; init; }

    ///<summary>The step debugger: truncated-prefix counts, profile and explain.</summary>
    public bool Debug { get; init; }

    ///<summary>Ask AI may hand the model a read-only query tool bound to this database.</summary>
    public bool AiTools { get; init; }
}

///<summary>
///Everything the app needs to know about one kind of database before it connects: what it can do, how
///to build a client for it, and how to describe it. Keyed by <see cref="GremlinDB.GremlinConnection.DatabaseType"/>.
///</summary>
public sealed class GraphDbProvider
{
    public string Id { get; init; }
    public string DisplayName { get; init; }

    ///<summary>Monaco language to force on connect, or null to leave the user's own choice alone.</summary>
    public string EditorLanguage { get; init; }

    ///<summary>A trivial query run on connect to prove the endpoint is reachable.</summary>
    public string ProbeQuery { get; init; }

    public GraphDbCapabilities Capabilities { get; init; }
    public Func<HttpClient, GremlinDB.GremlinConnection, IGraphDb> Create { get; init; }

    ///<summary>Host shown in the top bar — for an endpoint-URL database it's parsed back out of the URL.</summary>
    public Func<GremlinDB.GremlinConnection, string> DisplayHost { get; init; }
    public Func<GremlinDB.GremlinConnection, int> DisplayPort { get; init; }

    ///<summary>Whether the connection form holds enough to attempt a connection.</summary>
    public Func<GremlinDB.GremlinConnection, bool> IsConfigured { get; init; }

    public Func<GremlinDB.GremlinConnection, string> ConnectedMessage { get; init; }

    ///<summary>Extra hint appended when a connection attempt fails, or null.</summary>
    public string ConnectFailedHint { get; init; }
}

///<summary>The databases the app knows how to talk to, looked up by a connection's DatabaseType.</summary>
public static class GraphDbProviders
{
    private static readonly GraphDbCapabilities GremlinCapabilities = new()
    {
        BrowseGraph = true,
        Traverse = true,
        StageEdits = true,
        MultiStatement = true,
        Debug = true,
        AiTools = true
    };

    //Every Gremlin-backed feature is built from GremlinQueryBuilder strings, so a plain RDF endpoint has
    //none of them. Not a judgement about SPARQL — a Cypher provider will switch them on one at a time.
    private static readonly GraphDbCapabilities SparqlCapabilities = new();

    public const string TinkerPop = "ApacheTinkerPop";
    public const string CosmosDb = "CosmosDb";
    public const string Sparql = "Sparql";

    private static readonly GraphDbProvider TinkerPopProvider = new()
    {
        Id = TinkerPop,
        DisplayName = "Apache TinkerPop (Gremlin)",
        EditorLanguage = null,//leave the editor on whatever the user picked
        ProbeQuery = GremlinQueryBuilder.TestConnection,
        Capabilities = GremlinCapabilities,
        Create = (http, connection) => new GremlinDB(http, connection),
        DisplayHost = connection => connection.Hostname,
        DisplayPort = connection => connection.Port,
        IsConfigured = connection => true,
        ConnectedMessage = connection => $"Connected OK via {connection.Transport}.",
        ConnectFailedHint = null
    };

    private static readonly GraphDbProvider CosmosDbProvider = new()
    {
        Id = CosmosDb,
        DisplayName = "Cosmos DB (Gremlin)",
        EditorLanguage = null,
        ProbeQuery = GremlinQueryBuilder.TestConnection,
        //Cosmos speaks Gremlin but has no OLAP, so it gets its own entry rather than aliasing TinkerPop —
        //that difference is expected to show up here as capabilities are split finer.
        Capabilities = GremlinCapabilities,
        Create = (http, connection) => new GremlinDB(http, connection),
        DisplayHost = connection => connection.Hostname,
        DisplayPort = connection => connection.Port,
        IsConfigured = connection => true,
        ConnectedMessage = connection => $"Connected OK via {connection.Transport}.",
        ConnectFailedHint = null
    };

    private static readonly GraphDbProvider SparqlProvider = new()
    {
        Id = Sparql,
        DisplayName = "SPARQL / RDF",
        EditorLanguage = "sparql",
        ProbeQuery = "SELECT * WHERE { ?s ?p ?o } LIMIT 1",
        Capabilities = SparqlCapabilities,
        Create = (http, connection) => new SparqlDb(http, connection),
        DisplayHost = EndpointHost,
        DisplayPort = EndpointPort,
        IsConfigured = connection => !string.IsNullOrWhiteSpace(connection.Endpoint),
        ConnectedMessage = connection => "Connected to SPARQL endpoint.",
        ConnectFailedHint = "(if this is a CORS error, the endpoint must allow this origin)"
    };

    private static readonly Dictionary<string, GraphDbProvider> ById = new(StringComparer.OrdinalIgnoreCase)
    {
        [TinkerPop] = TinkerPopProvider,
        [CosmosDb] = CosmosDbProvider,
        [Sparql] = SparqlProvider
    };

    public static IEnumerable<GraphDbProvider> All => ById.Values;

    ///<summary>
    ///The provider for a connection's DatabaseType. An unknown or missing type falls back to TinkerPop,
    ///matching the long-standing behavior that anything which isn't SPARQL takes the Gremlin path — an
    ///embed URL can carry a database type this build has never heard of.
    ///</summary>
    public static GraphDbProvider For(string databaseType)
    {
        if (databaseType != null && ById.TryGetValue(databaseType, out var provider))
            return provider;

        return TinkerPopProvider;
    }

    private static string EndpointHost(GremlinDB.GremlinConnection connection)
    {
        if (Uri.TryCreate(connection.Endpoint, UriKind.Absolute, out var uri))
            return uri.Host;

        return connection.Endpoint;
    }

    private static int EndpointPort(GremlinDB.GremlinConnection connection)
    {
        if (Uri.TryCreate(connection.Endpoint, UriKind.Absolute, out var uri))
            return uri.Port;

        return 0;
    }
}
