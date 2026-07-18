using Bunit;
using GraphDBViewerWeb.Code;
using GraphDBViewerWeb.Components;
using GraphDBViewerWeb.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace GraphDBViewerWeb.Tests;

//Markup and interaction cover for Home. Every other test here is pure xUnit over C#, which cannot see a
//.razor file at all — so 9972176 renamed the gremlin/sparql fields to db, the replace leaked into six
//string literals in the markup, and a 352-green suite said nothing while the Gremlin export silently
//wrote JSON and the editor's language picker offered the same value twice. These pin the literals that
//C# reads back, and the interactions (like the import confirm) that only exist at the component layer.
public class HomeMarkupTests : BunitContext
{
    private const string DotA = "digraph { Alice -> Bob [label=knows] }";
    private const string DotB = "digraph { Zeta -> Yara [label=knows] }";

    private IRenderedComponent<Home> RenderHome()
    {
        Services.AddSingleton<IAppStorage>(new NullStorage());
        Services.AddSingleton(new HttpClient());
        Services.AddScoped<WorkspaceStore>();
        Services.AddScoped<LlmConnectionStore>();

        //Home drives Monaco and Cytoscape on render. Loose mode returns default for anything unconfigured
        //rather than throwing, which is all these need — the assertions are about markup, not interop.
        JSInterop.Mode = JSRuntimeMode.Loose;

        return Render<Home>();
    }

    private List<string> OptionValues(IRenderedComponent<Home> cut)
    {
        return cut.FindAll("option").Select(o => o.GetAttribute("value")).ToList();
    }

    private void OpenImportPanel(IRenderedComponent<Home> cut)
    {
        //The panel is open at boot (showImportExport starts true), so an unconditional click would
        //toggle it shut — the e2e helper's retry loop hides the same trap. Click only when it's closed.
        if (cut.FindAll("textarea[placeholder^='GraphSON']").Count == 0)
            cut.FindAll("button").First(b => b.TextContent.Contains("Import / Export")).Click();
    }

    private void PasteAndGenerate(IRenderedComponent<Home> cut, string dot)
    {
        cut.Find("textarea[placeholder^='GraphSON']").Input(dot);
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Generate queries").Click();
    }

    //The staged Generated queries, read off the Generated tab's Monaco component — the markup itself
    //doesn't carry the text (Monaco draws it via JS), but the component's Value parameter is the buffer.
    private string GeneratedBuffer(IRenderedComponent<Home> cut)
    {
        return cut.FindComponent<MonacoEditor>().Instance.Value;
    }

    //RunExportChoiceAsync branches on exportChoice == "gremlin". When the option offered "db" instead, the
    //branch was simply unreachable and every export fell through to JSON, under the wrong filename.
    [Fact]
    public void GraphExport_OffersTheValueRunExportChoiceActuallyReads()
    {
        var values = OptionValues(RenderHome());

        Assert.Contains("gremlin", values);
        Assert.Contains("json", values);
    }

    //NormalizeEditorLanguage recognizes exactly these two and collapses anything else to "gremlin", so an
    //option carrying any other value is silently unselectable.
    [Fact]
    public void EditorLanguages_OfferOnlyValuesNormalizeEditorLanguageKeeps()
    {
        var cut = RenderHome();

        //The editor lives behind `@if (isConnected || offlineMode)`, so reveal the workspace first.
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Offline mode").Click();

        var values = OptionValues(cut);

        Assert.Contains("gremlin", values);
        Assert.Contains("sparql", values);
        Assert.DoesNotContain("db", values);
    }

    //Entering offline mode used to unmount its own exit button: the Offline/Exit toggle lives inside the
    //connection card, and StartBlankCanvasAsync closes that card — leaving the top bar claiming
    //"Disconnected" with no visible way back out. The top bar now names the mode and reopens the card,
    //which is the path back; this walks the whole round trip that used to dead-end.
    [Fact]
    public void OfflineMode_ExitStaysReachableThroughTheTopBar()
    {
        var cut = RenderHome();

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Offline mode").Click();

        //The card closed with entry, taking the toggle with it — the top bar carries the state instead.
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Exit offline mode");
        cut.FindAll("button").First(b => b.TextContent.Contains("Offline mode")).Click();

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Exit offline mode").Click();

        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Contains("Disconnected"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Exit offline mode");
    }

    //The DatabaseType buttons write literals that GraphDbProviders.For reads back — and For(unknown)
    //silently falls back to TinkerPop, so a drifted value wouldn't error, it would quietly change the
    //form shape and the capability gates. The form shape is the observable end of that chain.
    [Fact]
    public void SparqlButton_SwapsTheFormToAnEndpointUrl()
    {
        var cut = RenderHome();

        //TinkerPop is the default shape: host/port, no endpoint URL.
        Assert.Contains(cut.FindAll("label"), l => l.TextContent.StartsWith("Hostname"));

        cut.FindAll("button").First(b => b.TextContent.Trim() == "SPARQL / RDF").Click();

        //The endpoint input replaces host/port, and its example URL is a real SPARQL endpoint (the
        //rename leak had turned it into wikidata.org/db).
        Assert.NotNull(cut.Find("input[placeholder='https://query.wikidata.org/sparql']"));
        Assert.DoesNotContain(cut.FindAll("label"), l => l.TextContent.StartsWith("Hostname"));
    }

    [Fact]
    public void CosmosButton_RevealsDatabaseAndCollection()
    {
        var cut = RenderHome();

        Assert.DoesNotContain(cut.FindAll("label"), l => l.TextContent.Trim() == "Collection");

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Cosmos DB (Gremlin)").Click();

        Assert.Contains(cut.FindAll("label"), l => l.TextContent.Trim() == "Database");
        Assert.Contains(cut.FindAll("label"), l => l.TextContent.Trim() == "Collection");
    }

    //The ✨ entry point lives in the Import panel and opens the knowledge-graph modal.
    [Fact]
    public void GenerateWithAi_OpensTheKgModal()
    {
        var cut = RenderHome();

        OpenImportPanel(cut);
        cut.FindAll("button").First(b => b.TextContent.Contains("Generate with AI")).Click();

        Assert.Contains("Generate a graph from text", cut.Markup);
    }

    //AC 12's worry — the Import panel closing under an in-flight generation — is designed out by the
    //modal: it isn't the panel's child, so the panel closing doesn't touch it. In the browser the
    //backdrop even blocks every user path to close the panel mid-generation (the e2e spec notes this);
    //bUnit dispatches the occluded click anyway, so this is where the survival property is provable.
    [Fact]
    public void KgModal_SurvivesTheImportPanelClosing()
    {
        var cut = RenderHome();

        OpenImportPanel(cut);
        cut.FindAll("button").First(b => b.TextContent.Contains("Generate with AI")).Click();

        //Close the panel underneath the open modal.
        cut.FindAll("button").First(b => b.TextContent.Contains("Import / Export")).Click();

        Assert.Empty(cut.FindAll("textarea[placeholder^='GraphSON']"));
        Assert.Contains("Generate a graph from text", cut.Markup);
    }

    //The top-bar Feature showcase button opens the full-screen showcase overlay (the bundled landing page
    //in an iframe). It moved out of the Settings menu onto the top bar, so it's clickable without opening a menu.
    [Fact]
    public void FeatureShowcaseButton_OpensTheShowcaseOverlay()
    {
        var cut = RenderHome();

        cut.FindAll("button").First(b => b.TextContent.Contains("Feature showcase")).Click();

        Assert.EndsWith("showcase/index.html", cut.Find("iframe").GetAttribute("src"));
    }

    //The Import panel's two buttons both overwrite the staged Generated queries. The first import has
    //nothing to lose and must not ask; importing over staged work must. Visualize is covered end to end
    //by e2e/import-confirm.spec.js; these cover Generate queries, whose miss shipped once already.
    [Fact]
    public void GenerateQueries_FirstImport_DoesNotAsk()
    {
        var cut = RenderHome();
        var confirm = JSInterop.Setup<bool>("confirm", _ => true);
        confirm.SetResult(true);

        OpenImportPanel(cut);
        PasteAndGenerate(cut, DotA);

        Assert.Empty(confirm.Invocations);
        Assert.Contains("Alice", GeneratedBuffer(cut));
    }

    [Fact]
    public void GenerateQueries_DecliningTheConfirm_KeepsTheStagedQueries()
    {
        var cut = RenderHome();
        var confirm = JSInterop.Setup<bool>("confirm", _ => true);
        confirm.SetResult(false);

        OpenImportPanel(cut);
        PasteAndGenerate(cut, DotA);
        PasteAndGenerate(cut, DotB);

        Assert.Single(confirm.Invocations);

        var buffer = GeneratedBuffer(cut);

        Assert.Contains("Alice", buffer);
        Assert.DoesNotContain("Zeta", buffer);
    }

    [Fact]
    public void GenerateQueries_AcceptingTheConfirm_ReplacesTheStagedQueries()
    {
        var cut = RenderHome();
        var confirm = JSInterop.Setup<bool>("confirm", _ => true);
        confirm.SetResult(true);

        OpenImportPanel(cut);
        PasteAndGenerate(cut, DotA);
        PasteAndGenerate(cut, DotB);

        Assert.Single(confirm.Invocations);

        var buffer = GeneratedBuffer(cut);

        Assert.Contains("Zeta", buffer);
        Assert.DoesNotContain("Alice", buffer);
    }

    //Committing staged queries with no connection used to silently no-op. It now warns and pulses the
    //connect button, exactly like running a query without a connection — the message lives in the stats bar.
    //Generating from an import enters offline mode (db == null) and reveals the workspace, so this is the
    //exact "offline, staged edits, no connection" state the user hits.
    [Fact]
    public void CommitOffline_WarnsToConnect_InsteadOfSilentlyDoingNothing()
    {
        var cut = RenderHome();
        var confirm = JSInterop.Setup<bool>("confirm", _ => true);
        confirm.SetResult(true);

        OpenImportPanel(cut);
        PasteAndGenerate(cut, DotA);

        Assert.DoesNotContain("No active connection", cut.Markup);

        cut.FindAll("button").First(b => b.TextContent.Contains("Commit Changes")).Click();

        Assert.Contains("No active connection — connect to a database first.", cut.Markup);
    }

    //Offline mode is a drawing surface, not a query — entering it seeds an empty base, but the "no results
    //to visualize" / "query ran — no graph to display" banner must not appear (there was no query). The
    //companion behavior — showing that banner for a real empty result even while previewing edits with
    //nothing staged — needs a live database, so it's covered by manual / e2e verification, not here.
    [Fact]
    public void OfflineMode_DoesNotShowTheNoResultsBanner()
    {
        var cut = RenderHome();

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Offline mode").Click();

        Assert.DoesNotContain("No results could be visualized", cut.Markup);
        Assert.DoesNotContain("no graph to display", cut.Markup);
    }
}
