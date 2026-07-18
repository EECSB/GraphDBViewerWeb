using Blazored.LocalStorage;

namespace GraphDBViewerWeb.Code;

///<summary>
///<see cref="IAppStorage"/> backed by the browser's localStorage (via Blazored.LocalStorage).
///This is the only persistence layer in the WebAssembly build — everything is kept on the user's
///machine, so state does not survive clearing site data and is not shared between devices.
///</summary>
public class LocalAppStorage : IAppStorage
{
    private readonly ILocalStorageService _localStorage;

    public LocalAppStorage(ILocalStorageService localStorage)
    {
        _localStorage = localStorage;
    }

    public async Task<T> GetAsync<T>(string key)
    {
        return await _localStorage.GetItemAsync<T>(key);
    }

    public async Task SetAsync<T>(string key, T value)
    {
        await _localStorage.SetItemAsync(key, value);
    }

    public async Task<string> GetStringAsync(string key)
    {
        return await _localStorage.GetItemAsStringAsync(key);
    }

    public async Task SetStringAsync(string key, string value)
    {
        await _localStorage.SetItemAsStringAsync(key, value);
    }

    public async Task RemoveAsync(string key)
    {
        await _localStorage.RemoveItemAsync(key);
    }
}
