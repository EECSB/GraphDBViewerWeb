namespace GraphDBViewerWeb.Code;

///<summary>
///Persists the saved AI-model connections behind <see cref="IAppStorage"/>. Owns the
///graphdbviewer:llmConnections key, lifted out of NlQueryModal so every AI feature ("Ask AI" today,
///knowledge-graph generation next) shares one store and one config UI instead of growing a second
///copy of the CRUD. The stored shape is unchanged from when the modal owned the key, so existing
///saved models load with no migration.
///</summary>
public class LlmConnectionStore
{
    private const string StorageKey = "graphdbviewer:llmConnections";

    private readonly IAppStorage _storage;

    public LlmConnectionStore(IAppStorage storage)
    {
        _storage = storage;
    }

    ///<summary>The saved connections by name. Never null — no models yet reads as an empty dictionary.</summary>
    public async Task<Dictionary<string, LlmConnection>> LoadAsync()
    {
        var stored = await _storage.GetAsync<Dictionary<string, LlmConnection>>(StorageKey);

        if (stored == null)
            return new Dictionary<string, LlmConnection>();

        return stored;
    }

    public async Task SaveAsync(Dictionary<string, LlmConnection> connections)
    {
        await _storage.SetAsync(StorageKey, connections);
    }
}
