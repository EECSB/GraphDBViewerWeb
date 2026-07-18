using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using GraphDBViewerWeb.Code;

namespace GraphDBViewerWeb.Pages;

//Connection management: the active connection and its lifecycle (connect / disconnect), plus the
//saved-connections list and its add / edit / delete form.
public partial class Home
{

    private const string LocalStorageKey = "graphdbviewer:connections";

    #region Active connection state

    //Database provider — chosen per connection. Apache TinkerPop / Cosmos DB speak Gremlin; SPARQL is a
    //plain HTTP RDF endpoint. Database/Collection inputs apply only to Cosmos DB.
    private bool RequiresDatabaseCollection => connection.DatabaseType == "CosmosDb";
    private bool showSupportedDbs;

    //The current database's provider and what it can do. Both read off the connection rather than a live
    //client, because the UI gates on them before there is one — the Load DB row, for instance, renders
    //while disconnected so that clicking it can prompt you to connect.
    private GraphDbProvider Provider => GraphDbProviders.For(connection.DatabaseType);
    private GraphDbCapabilities Caps => Provider.Capabilities;

    //True when this database is addressed by an endpoint URL rather than host/port, which is the one
    //thing that still changes the shape of the connection form.
    private bool UsesEndpointUrl => connection.DatabaseType == "Sparql";

    //Host/port shown in the top bar — an endpoint-URL database parses them back out of the URL.
    private string DisplayHost => Provider.DisplayHost(connection);
    private int DisplayPort => Provider.DisplayPort(connection);

    private GremlinDB.GremlinConnection connection = new("WebSocket", 443, true, string.Empty, string.Empty, string.Empty, string.Empty);

    //The live database, whichever kind it is. Null until Connect succeeds.
    private IGraphDb db;
    private bool isConnected;
    private bool isConnecting;
    private CancellationTokenSource connectCts;
    private string statusMessage;
    private string statusClass;

    #endregion

    #region Saved connections
    private Dictionary<string, GremlinDB.GremlinConnection> savedConnections = new();
    private string selectedConnectionKey;
    #endregion

    #region Add-connection form state
    //The name the fields below are saved under when the + button is clicked; the connection itself is the
    //active `connection`, so there's no separate form — the + just captures what's already entered.
    private string connectionName;
    private bool connectionNameError;
    private string connectionNameErrorMessage;
    #endregion


    #region Saved-connections persistence

    private async Task LoadConnectionsAsync()
    {
        var stored = await Storage.GetAsync<Dictionary<string, GremlinDB.GremlinConnection>>(LocalStorageKey);

        if (stored is { Count: > 0 })
        {
            savedConnections = stored;
            SelectConnection(savedConnections.Keys.First());
        }
    }

    private async Task PersistConnectionsAsync()
    {
        await Storage.SetAsync(LocalStorageKey, savedConnections);
    }

    #endregion


    #region Add / Delete

    //Saves the connection currently entered in the fields below to the saved-connections list under the
    //name box's value — adding a new one, or overwriting the existing one when the name already matches
    //(so selecting a connection, tweaking the fields and clicking + updates it). No separate form.
    private async Task AddCurrentConnectionAsync()
    {
        connectionNameError = false;

        var name = connectionName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            connectionNameError = true;
            connectionNameErrorMessage = "Connection name is required.";

            return;
        }

        savedConnections[name] = new GremlinDB.GremlinConnection(connection);
        await PersistConnectionsAsync();

        selectedConnectionKey = name;
        connectionName = name;

        statusMessage = $"Connection \"{name}\" saved.";
        statusClass = "text-success";
    }

    private async Task DeleteConnectionAsync()
    {
        if (string.IsNullOrEmpty(selectedConnectionKey))
            return;

        if (isConnected)
            Disconnect("Disconnected because the active connection was deleted.");

        savedConnections.Remove(selectedConnectionKey);
        selectedConnectionKey = null;

        await PersistConnectionsAsync();
    }

    #endregion


    #region Connect / disconnect

    private void OnConnectionSelected(ChangeEventArgs e)
    {
        if (isConnected)
            Disconnect("Disconnected due to connection change.");

        SelectConnection(e.Value?.ToString());
    }

    //Loads the saved connection with the given key into the active fields.
    private void SelectConnection(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            selectedConnectionKey = null;
            connectionName = null;
            return;
        }

        if (savedConnections.TryGetValue(key, out var saved))
        {
            connection = new GremlinDB.GremlinConnection(saved);
            selectedConnectionKey = key;
            connectionName = key;
        }
    }

    private void OnDatabaseChanged(ChangeEventArgs e)
    {
        if (isConnected)
            Disconnect("Disconnected due to database change.");

        connection.Database = e.Value?.ToString();
    }

    private async Task ToggleConnectionAsync()
    {
        if (isConnected)
        {
            Disconnect("Disconnected by user.");
            return;
        }

        await ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        isConnecting = true;
        statusMessage = null;
        connectCts?.Dispose();
        connectCts = new CancellationTokenSource();

        try
        {
            var provider = Provider;
            db = provider.Create(Http, connection);

            //A trivial query surfaces reachability problems immediately (CORS being the #1 SPARQL gotcha).
            var test = await db.ExecuteAsync(provider.ProbeQuery, connectCts.Token);

            if (test.IsError)
            {
                statusMessage = $"Connection failed: {test.Error}{FailedHint(provider)}";
                statusClass = "text-danger";
                isConnected = false;
                await DisposeDbAsync();
            }
            else
            {
                statusMessage = provider.ConnectedMessage(connection);
                statusClass = "text-success";
                isConnected = true;
                offlineMode = false;//connecting and offline mode are mutually exclusive
                showConnectionCard = false;
                showImportExport = false;

                //Only when the database dictates one — otherwise the user's own choice stands.
                if (provider.EditorLanguage != null)
                    editorLanguage = provider.EditorLanguage;

                await LoadQueryAsync();

                if (provider.Capabilities.BrowseGraph)
                    await RefreshSchemaVocabularyAsync();
            }
        }
        catch (OperationCanceledException)
        {
            statusMessage = "Connection canceled.";
            statusClass = "text-muted";
            isConnected = false;
            await DisposeDbAsync();
        }
        catch (Exception ex)
        {
            statusMessage = ex.Message;
            statusClass = "text-danger";
            isConnected = false;
            await DisposeDbAsync();
        }
        finally
        {
            isConnecting = false;
            connectCts?.Dispose();
            connectCts = null;
        }
    }

    //Cancels an in-progress connection attempt.
    private void CancelConnect()
    {
        connectCts?.Cancel();
    }

    private void Disconnect(string reason = null)
    {
        _ = DisposeDbAsync();//fire-and-forget; closes WebSocket gracefully
        isConnected = false;
        queryResults = null;
        queryText = null;
        statusMessage = reason ?? "Disconnected.";
        statusClass = "text-muted";
        showConnectionCard = true;

        //Drop the schema autocomplete so it doesn't suggest the disconnected DB's labels/keys.
        _ = ClearSchemaAsync();
    }

    private async Task ClearSchemaAsync()
    {
        schemaVocab = null;//disconnected: the schema is unknown again, which isn't the same as empty

        try
        {
            await JS.InvokeVoidAsync("monacoInterop.setSchema", new SchemaVocabulary());
        }
        catch { }
    }

    private async Task DisposeDbAsync()
    {
        if (db is not null)
        {
            await db.DisposeAsync();
            db = null;
        }
    }

    //Trailing note on a failed connection, for databases whose usual failure has a known cause. Empty
    //for the rest, which end on the database's own message.
    private static string FailedHint(GraphDbProvider provider)
    {
        if (provider.ConnectFailedHint == null)
            return "";

        return $" {provider.ConnectFailedHint}.";
    }

    #endregion
}
