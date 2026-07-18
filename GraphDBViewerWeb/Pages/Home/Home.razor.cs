using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using GraphDBViewerWeb.Code;

namespace GraphDBViewerWeb.Pages;

//The viewer's single page. This root file holds the component lifecycle, the theme / layout
//preferences and the page-shell toggles; the feature areas live in the sibling Home.*.cs partials:
//  Home.State.cs          — the query tabs + the active-tab delegating properties
//  Home.Connections.cs    — saved connections + connect / disconnect
//  Home.Persistence.cs    — localStorage (tabs, history, saved queries, editor text)
//  Home.Query.cs          — query execution (Gremlin + SPARQL) and Load DB / Schema
//  Home.Debugger.cs       — the step-through query debugger
//  Home.Styling.cs        — the Style dialog + stylesheets
//  Home.ImportExport.cs   — clipboard / file exports and pasted-graph imports
//  Home.Tabs.cs           — tab add / rename / switch / close
//  Home.GraphView.cs      — 2D/3D canvas rendering, layouts, filters, saved positions
//  Home.ElementEditing.cs — graph-click selection, property editing, staged-query commit
public partial class Home : IAsyncDisposable
{

    [Inject]
    private NavigationManager Nav { get; set; }

    private const string DarkModeKey = "graphdbviewer:darkMode";
    private const string FullScreenKey = "graphdbviewer:fullScreen";
    private const string ExpansionLimitKey = "graphdbviewer:expansionLimit";

    //Theme
    private bool darkMode;

    //Whether the layout fills the viewport (side margins dropped). Persisted; toggled from the top bar.
    private bool fullScreen;

    //The most neighbors (incident edges) a double-click node-expansion pulls in, so a high-degree node
    //doesn't flood the canvas. Persisted; set from the Settings menu. 0 or less means uncapped.
    private const int DefaultExpansionLimit = 50;
    private int expansionLimit = DefaultExpansionLimit;

    //Page-shell toggles.
    private bool showConnectionCard = true;
    private bool showImportExport = true;
    private bool showAbout;
    private bool showShowcase;//full-screen showcase (the landing page) overlay
    private bool showNlModal;//"Ask in English" (natural-language query) popup
    private bool showKgModal;//"Generate with AI" (knowledge-graph generation) popup

    //The main workspace (results / canvas / query area) stays hidden on load until the user either
    //connects or enters offline mode (the Offline mode button, or importing/pasting a graph to draw).
    private bool offlineMode;

    //The split-row element, handed to the splitter interop so it can measure the container while dragging.
    private ElementReference splitRowRef;

    //One shared .NET reference handed to the JS interops (keyboard shortcuts + graph-click callbacks),
    //created on first use and disposed with the component.
    private DotNetObjectReference<Home> selfRef;

    private DotNetObjectReference<Home> SelfRef()
    {
        selfRef ??= DotNetObjectReference.Create(this);

        return selfRef;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await LoadConnectionsAsync();
            await LoadSavedQueriesAsync();
            await LoadHistoryAsync();
            await LoadTabsAsync();
            await LoadStylesheetsAsync();
            await LoadDarkModeAsync();
            fullScreen = await Storage.GetAsync<bool>(FullScreenKey);
            expansionLimit = await Storage.GetAsync<int?>(ExpansionLimitKey) ?? DefaultExpansionLimit;

            //Wire up the global keyboard shortcut listener (Delete key)
            await JS.InvokeVoidAsync("keyboardInterop.attach", SelfRef());

            StateHasChanged();

            //Redraw the restored active tab's graph now that its container is in the DOM.
            if ((visualizationMode == 2 || visualizationMode == 3) && HasGraphData)
                await RenderGraphAsync();

            //Apply any URL query-string settings last, so an embed's parameters win over restored state.
            await ApplyEmbedSettingsAsync();
        }

        //Focus the tab-name input right after a double-click puts it on screen.
        if (focusTabInput)
        {
            focusTabInput = false;

            try
            {
                await tabNameInput.FocusAsync();
            }
            catch { }
        }
    }

    //Applies settings passed in the page URL's query string, used when the viewer is embedded (e.g. in an
    //iframe): connection details, an initial query, and the initial view mode. Runs once on first render,
    //after the persisted state has loaded, so the URL settings take precedence over the restored tab.
    private async Task ApplyEmbedSettingsAsync()
    {
        EmbedSettings settings;

        try
        {
            settings = EmbedSettings.Parse(new Uri(Nav.Uri).Query);
        }
        catch
        {
            return;
        }

        if (!settings.HasAny)
            return;

        //Initial view (JSON / 2D / 3D / Table).
        if (settings.View.HasValue)
            visualizationMode = settings.View.Value;

        //Initial query text + editor language, set before connecting so LoadQueryAsync can't overwrite it.
        if (settings.Query != null)
            queryText = settings.Query;

        if (settings.Language != null)
            editorLanguage = WorkspaceStore.NormalizeEditorLanguage(settings.Language);
        else if (settings.DatabaseType == "Sparql" || settings.Endpoint != null)
            editorLanguage = "sparql";

        //Flush the new view/query into the DOM before anything renders into it.
        StateHasChanged();

        //Connection details — open the connection unless connect=false.
        if (settings.HasConnection)
        {
            connection = settings.BuildConnection();
            selectedConnectionKey = null;

            if (settings.Connect != false)
                await ConnectAsync();
        }

        //Run the initial query once connected (unless run=false); otherwise just draw any restored graph.
        if (isConnected && !string.IsNullOrEmpty(queryText) && settings.AutoRun != false)
            await RunQueryAsync();
        else if ((visualizationMode == 2 || visualizationMode == 3) && HasGraphData)
            await RenderGraphAsync();

        StateHasChanged();
    }

    #region Theme & layout preferences

    private async Task LoadDarkModeAsync()
    {
        //Dark mode is the default; a stored preference (set via the theme toggle) overrides it.
        darkMode = await Storage.GetAsync<bool?>(DarkModeKey) ?? true;
        await ApplyThemeAsync();
    }

    private async Task ToggleDarkModeAsync()
    {
        darkMode = !darkMode;
        await Storage.SetAsync(DarkModeKey, darkMode);
        await ApplyThemeAsync();
    }

    private async Task ToggleFullScreenAsync()
    {
        fullScreen = !fullScreen;
        await Storage.SetAsync(FullScreenKey, fullScreen);
    }

    //Opening the showcase from the About dialog closes About first, so the two overlays don't stack.
    private void OpenShowcaseFromAbout()
    {
        showAbout = false;
        showShowcase = true;
    }

    //Sets the node-expansion neighbor cap (from the Settings menu), clamped to a sane range and persisted.
    private async Task SetExpansionLimitAsync(int value)
    {
        expansionLimit = Math.Clamp(value, 1, 5000);
        await Storage.SetAsync(ExpansionLimitKey, expansionLimit);
    }

    //Sets the query / Load DB result limit from the Settings menu — the same per-tab value the sidebar's
    //query-limit box binds (activeTab.LoadDbLimit), so the two controls stay in sync; persisted with the tab.
    private async Task SetQueryLimitAsync(int value)
    {
        loadDbLimit = Math.Max(1, value);
        await SaveTabsAsync();
    }

    private async Task ApplyThemeAsync()
    {
        string theme;
        if (darkMode)
            theme = "dark";
        else
            theme = "light";

        await JS.InvokeVoidAsync("document.documentElement.setAttribute", "data-bs-theme", theme);

        try
        {
            await JS.InvokeVoidAsync("monacoInterop.setTheme", darkMode);
        }
        catch { }
    }

    #endregion


    //Blazor calls this when the component is torn down: stop any in-flight work, release the JS-held
    //.NET reference, and close the database connections.
    public async ValueTask DisposeAsync()
    {
        textSaveCts?.Cancel();
        textSaveCts?.Dispose();
        queryCts?.Cancel();
        queryCts?.Dispose();
        connectCts?.Cancel();
        connectCts?.Dispose();
        selfRef?.Dispose();

        try
        {
            await DisposeDbAsync();
        }
        catch { }
    }

    #region Split view

    //Starts dragging the divider between the graph view and the sidebar; the interop handles the
    //pointer tracking, the min-width clamp, and snapping back to the default ratio.
    private async Task StartResizeAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("splitterInterop.beginResize", splitRowRef);
        }
        catch { }
    }

    //Resets the split back to the default ratio (double-click the divider).
    private async Task ResetSplitAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("splitterInterop.resetSplit");
        }
        catch { }
    }

    #endregion
}
