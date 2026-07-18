using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace GraphDBViewerWeb.Code;

///<summary>
///Executes Gremlin queries against a Gremlin server using either:
///<list type="bullet">
///<item><b>HTTP</b>  — POST to the REST endpoint (TinkerPop with REST channel enabled)</item>
///<item><b>WebSocket</b> — ws:// GraphSON 3 framing (standard TinkerPop / CosmosDB default)</item>
///</list>
///No Gremlin.Net dependency — safe for Blazor WebAssembly.
///</summary>
public class GremlinDB : IGraphDb
{
    #region Constants

    private const string JsonContentType = "application/json";
    private const string GraphSON3MimeType = "application/vnd.gremlin-v3.0+json";

    #endregion


    #region Constructor

    public GremlinDB(HttpClient httpClient, GremlinConnection connection)
    {
        _http = httpClient;
        _connection = connection;

        bool useSsl = connection.UseSSL || connection.Port == 443;
        if (useSsl)
        {
            _httpScheme = "https";
            _wsScheme = "wss";
        }
        else
        {
            _httpScheme = "http";
            _wsScheme = "ws";
        }

        _baseHttpUri = new Uri($"{_httpScheme}://{connection.Hostname}:{connection.Port}/gremlin");
        _wsEndpoint = new Uri($"{_wsScheme}://{connection.Hostname}:{connection.Port}/gremlin");
    }

    public GremlinDB(HttpClient httpClient, string transport, string hostname, string authKey, int port, bool useSSL, string database, string collection)
        : this(httpClient, new GremlinConnection(transport, port, useSSL, hostname, authKey, database, collection))
    {}

    #endregion


    #region Fields

    private readonly HttpClient _http;
    private readonly GremlinConnection _connection;
    private readonly string _httpScheme;
    private readonly string _wsScheme;
    private readonly Uri _baseHttpUri;
    private readonly Uri _wsEndpoint;

    //WebSocket is kept open between queries for the lifetime of the connection
    private ClientWebSocket _ws;

    //Serializes WebSocket request/response exchanges: response frames are read until a terminal
    //status (not matched to senders), and ClientWebSocket forbids overlapping ReceiveAsync calls,
    //so concurrent queries — e.g. a double-click expand racing the selection's connection
    //queries — would otherwise consume each other's frames or fault.
    private readonly SemaphoreSlim _wsRequestLock = new(1, 1);
    private bool _disposed;

    #endregion


    #region Public Query API

    ///<summary>
    ///Executes a single Gremlin query and returns a <see cref="GraphDbResult"/>.
    ///Routes to HTTP or WebSocket based on the transport setting.
    ///</summary>
    public async Task<GraphDbResult> ExecuteAsync(string gremlinQuery, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_connection.Transport == "WebSocket")
                return await ExecuteWebSocketAsync(gremlinQuery, cancellationToken);
            else
                return await ExecuteTinkerPopHttpAsync(gremlinQuery, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            //Let cancellation propagate so callers can distinguish it from a server error.
            throw;
        }
        catch (Exception ex)
        {
            return GraphDbResult.Failure($"{_connection.Transport} error: {ex.Message}");
        }
    }

    ///<summary>Executes a list of queries in order.</summary>
    public async Task<List<GraphDbResult>> ExecuteManyAsync(List<string> queries, CancellationToken cancellationToken = default)
    {
        var results = new List<GraphDbResult>(queries.Count);

        foreach (var q in queries)
            results.Add(await ExecuteAsync(q, cancellationToken));

        return results;
    }

    ///<summary>Executes a named set of queries in order.</summary>
    public async Task<Dictionary<string, GraphDbResult>> ExecuteManyAsync(Dictionary<string, string> queries, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, GraphDbResult>(queries.Count);

        foreach (var kv in queries)
            results[kv.Key] = await ExecuteAsync(kv.Value, cancellationToken);

        return results;
    }

    #endregion


    #region WebSocket Transport

    //── Protocol ──────────────────────────────────────────────────────
    //TinkerPop WebSocket framing (GraphSON 3):
    //
    //Request (UTF-8 JSON text frame):
    //{
    //"requestId": "<guid>",
    //"op": "eval",
    //"processor": "",
    //"args": { "gremlin": "<query>", "bindings": {}, "language": "gremlin-groovy", "aliases": {} }
    //}
    //
    //Response (one or more frames, status code in root):
    //{
    //"requestId": "<guid>",
    //"status": { "code": 200, "message": "" },
    //"result": { "data": { "@type": "g:List", "@value": [...] }, "meta": {} }
    //}
    //Status 200 = last/only frame, 204 = success but no content (empty result),
    //206 = partial (more frames follow). 200/204 are terminal; 206 means keep reading.

    private async Task<GraphDbResult> ExecuteWebSocketAsync(string query, CancellationToken cancellationToken)
    {
        //One exchange at a time (see _wsRequestLock). Acquired before try so the lock is only
        //released when it was actually taken.
        await _wsRequestLock.WaitAsync(cancellationToken);

        try
        {
            await EnsureWebSocketConnectedAsync(cancellationToken);

            var requestId = Guid.NewGuid().ToString();

            var request = new
            {
                requestId,
                op = "eval",
                processor = "",
                args = new
                {
                    gremlin = query,
                    bindings = new { },
                    language = "gremlin-groovy",
                    aliases = new { }
                }
            };

            var requestJson = JsonSerializer.Serialize(request);
            var requestBytes = Encoding.UTF8.GetBytes(requestJson);
            await _ws.SendAsync(requestBytes, WebSocketMessageType.Text, true, cancellationToken);

            //Collect all response frames (206 = partial, 200 = done, 204 = done/empty)
            var allValues = new List<JsonElement>();
            while (true)
            {
                var (json, closed) = await ReceiveFullMessageAsync(cancellationToken);
                if (closed)
                    return GraphDbResult.Failure("WebSocket closed unexpectedly.");

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                //A frame for a different request (e.g. left over from a canceled query whose
                //response was never fully read) — skip it and keep reading for ours.
                if (root.TryGetProperty("requestId", out var rid) && rid.GetString() != requestId)
                    continue;

                int statusCode = root.GetProperty("status").GetProperty("code").GetInt32();

                //204 (NO_CONTENT) is a success: query ran fine but returned no rows (e.g. empty graph).
                if (statusCode != 200 && statusCode != 204 && statusCode != 206)
                {
                    string msg;
                    if (root.GetProperty("status").TryGetProperty("message", out var m))
                        msg = m.GetString();
                    else
                        msg = "Unknown error";

                    return GraphDbResult.Failure($"Gremlin status {statusCode}: {msg}");
                }

                //Unwrap result.data (GraphSON: { "@type": "g:List", "@value": [...] }).
                //On 204 / empty results, data is JSON null — skip it (TryGetProperty throws on non-objects).
                if (root.TryGetProperty("result", out var result) && result.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                {
                    if (data.TryGetProperty("@value", out var values) && values.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in values.EnumerateArray())
                            allValues.Add(item.Clone());
                    }
                }

                if (statusCode == 200 || statusCode == 204)
                    break;//terminal frame (204 = no content)
            }

            //Wrap all collected values into a JSON array.
            var combined = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(allValues));

            return GraphDbResult.Success(combined);
        }
        finally
        {
            _wsRequestLock.Release();
        }
    }

    private async Task EnsureWebSocketConnectedAsync(CancellationToken cancellationToken)
    {
        if (_ws?.State == WebSocketState.Open)
            return;

        _ws?.Dispose();
        _ws = new ClientWebSocket();
        //_ws.Options.AddSubProtocol("graphson-v3.0");

        //Basic auth for TinkerPop servers that require it.
        if (!string.IsNullOrEmpty(_connection.AuthKey))
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($":{_connection.AuthKey}"));
            _ws.Options.SetRequestHeader("Authorization", $"Basic {credentials}");
        }

        await _ws.ConnectAsync(_wsEndpoint, cancellationToken);
    }

    ///<summary>Reads a complete WebSocket message, accumulating text frames.</summary>
    private async Task<(string json, bool closed)> ReceiveFullMessageAsync(CancellationToken cancellationToken)
    {
        var buffer = new ArraySegment<byte>(new byte[8192]);
        using var ms = new System.IO.MemoryStream();

        WebSocketReceiveResult result;
        do
        {
            result = await _ws.ReceiveAsync(buffer, cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
                return (null, true);

            ms.Write(buffer.Array!, buffer.Offset, result.Count);
        }
        while (!result.EndOfMessage);

        return (Encoding.UTF8.GetString(ms.ToArray()), false);
    }

    #endregion


    #region HTTP Transport — TinkerPop

    //POST http(s)://{host}:{port}/gremlin
    //Body:     { "gremlin": "<query>" }
    //Response: { "result": { "data": { "@type": "g:List", "@value": [...] } }, "status": { "code": 200 } }

    private async Task<GraphDbResult> ExecuteTinkerPopHttpAsync(string query, CancellationToken cancellationToken)
    {
        var endpoint = new Uri(_baseHttpUri, "/gremlin");
        var body = JsonSerializer.Serialize(new { gremlin = query });
        var content = new StringContent(body, Encoding.UTF8, JsonContentType);

        var response = await _http.PostAsync(endpoint, content, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            return GraphDbResult.Failure($"HTTP {(int)response.StatusCode}: {json}");

        return UnwrapTinkerPopResponse(json);
    }

    private static GraphDbResult UnwrapTinkerPopResponse(string json)
    {
        //Empty body (e.g. HTTP 204 No Content) = success with no rows.
        if (string.IsNullOrWhiteSpace(json))
            return GraphDbResult.Success(JsonSerializer.Deserialize<JsonElement>("[]"));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("result", out var result) && result.TryGetProperty("data", out var data))
        {
            //data is JSON null on empty results — don't call TryGetProperty on a non-object.
            if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("@value", out var value))
                return GraphDbResult.Success(value.Clone());
            else
                return GraphDbResult.Success(data.Clone());
        }

        return GraphDbResult.Success(root.Clone());
    }

    #endregion


    #region IAsyncDisposable

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_ws is not null)
        {
            if (_ws.State == WebSocketState.Open)
            {
                try
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disposing", CancellationToken.None);
                }
                catch 
                {
                    /* ignore errors on close */ 
                }
            }

            _ws.Dispose();
        }
        //HttpClient lifetime is owned by DI — do not dispose it here.
    }

    #endregion


    #region Models

    ///<summary>
    ///Holds all parameters needed to establish a connection to a Gremlin server.
    ///Persisted to localStorage (WASM) or a database (Server+Client variant).
    ///</summary>
    public class GremlinConnection
    {
        public GremlinConnection() { }

        public GremlinConnection(string transport, int port, bool useSSL, string hostname, string authKey, string database, string collection)
        {
            Transport = transport;
            Port = port;
            UseSSL = useSSL;
            Hostname = hostname;
            AuthKey = authKey;
            Database = database;
            Collection = collection;
        }

        public GremlinConnection(GremlinConnection copy)
        {
            Transport = copy.Transport;
            Port = copy.Port;
            UseSSL = copy.UseSSL;
            Hostname = copy.Hostname;
            AuthKey = copy.AuthKey;
            Database = copy.Database;
            Collection = copy.Collection;
            DatabaseType = copy.DatabaseType;
            Endpoint = copy.Endpoint;
            Username = copy.Username;
        }

        ///<summary>HTTP | WebSocket</summary>
        public string Transport { get; set; }

        public string Hostname { get; set; }
        public string AuthKey { get; set; }
        public int Port { get; set; }
        public bool UseSSL { get; set; }
        public string Database { get; set; }
        public string Collection { get; set; }

        ///<summary>Provider family: "ApacheTinkerPop" | "CosmosDb" | "Sparql". Defaults to TinkerPop for older saved connections.</summary>
        public string DatabaseType { get; set; } = "ApacheTinkerPop";

        ///<summary>SPARQL endpoint URL (e.g. https://query.wikidata.org/sparql). Used only when DatabaseType == "Sparql".</summary>
        public string Endpoint { get; set; }

        ///<summary>Optional HTTP basic-auth username (SPARQL); the password goes in AuthKey.</summary>
        public string Username { get; set; }
    }

    #endregion
}