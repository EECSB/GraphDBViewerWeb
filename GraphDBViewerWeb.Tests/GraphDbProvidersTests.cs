using GraphDBViewerWeb.Code;

namespace GraphDBViewerWeb.Tests;

public class GraphDbProvidersTests
{
    private static GremlinDB.GremlinConnection Sparql(string endpoint)
    {
        return new GremlinDB.GremlinConnection { DatabaseType = "Sparql", Endpoint = endpoint };
    }

    //── Lookup ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ApacheTinkerPop")]
    [InlineData("CosmosDb")]
    [InlineData("Sparql")]
    public void For_ReturnsTheMatchingProvider(string id)
    {
        Assert.Equal(id, GraphDbProviders.For(id).Id);
    }

    [Theory]
    [InlineData("Nonsense")]
    [InlineData("")]
    [InlineData(null)]
    public void For_UnknownOrMissingType_FallsBackToTinkerPop(string id)
    {
        //A saved or embed-URL connection can carry a type this build has never heard of. Anything that
        //isn't SPARQL has always taken the Gremlin path, and that has to keep holding.
        Assert.Equal(GraphDbProviders.TinkerPop, GraphDbProviders.For(id).Id);
    }

    //── Capabilities ────────────────────────────────────────────────────

    [Theory]
    [InlineData("ApacheTinkerPop")]
    [InlineData("CosmosDb")]
    public void GremlinProviders_CanDoEverything(string id)
    {
        var caps = GraphDbProviders.For(id).Capabilities;

        Assert.True(caps.BrowseGraph);
        Assert.True(caps.Traverse);
        Assert.True(caps.StageEdits);
        Assert.True(caps.MultiStatement);
        Assert.True(caps.Debug);
        Assert.True(caps.AiTools);
    }

    [Fact]
    public void SparqlProvider_HasNoneOfTheGremlinBuiltFeatures()
    {
        //Every one of these is built from GremlinQueryBuilder strings, so a plain RDF endpoint has none.
        //This is the truth table the UI gates used to spell as "!IsSparql".
        var caps = GraphDbProviders.For("Sparql").Capabilities;

        Assert.False(caps.BrowseGraph);
        Assert.False(caps.Traverse);
        Assert.False(caps.StageEdits);
        Assert.False(caps.MultiStatement);
        Assert.False(caps.Debug);
        Assert.False(caps.AiTools);
    }

    //── Editor language ─────────────────────────────────────────────────

    [Theory]
    [InlineData("ApacheTinkerPop")]
    [InlineData("CosmosDb")]
    public void GremlinProviders_LeaveTheEditorLanguageAlone(string id)
    {
        //Null means "don't touch it". Connecting to Gremlin must not reset a language the user picked.
        Assert.Null(GraphDbProviders.For(id).EditorLanguage);
    }

    [Fact]
    public void SparqlProvider_ForcesTheSparqlEditorLanguage()
    {
        Assert.Equal("sparql", GraphDbProviders.For("Sparql").EditorLanguage);
    }

    //── Display host / port ─────────────────────────────────────────────

    [Fact]
    public void Sparql_DisplayHostAndPort_ComeOutOfTheEndpointUrl()
    {
        var provider = GraphDbProviders.For("Sparql");
        var connection = Sparql("https://query.wikidata.org/sparql");

        Assert.Equal("query.wikidata.org", provider.DisplayHost(connection));
        Assert.Equal(443, provider.DisplayPort(connection));
    }

    [Fact]
    public void Sparql_DisplayHost_FallsBackToTheRawTextWhenTheUrlIsUnparseable()
    {
        var provider = GraphDbProviders.For("Sparql");
        var connection = Sparql("not a url");

        Assert.Equal("not a url", provider.DisplayHost(connection));
        Assert.Equal(0, provider.DisplayPort(connection));
    }

    [Fact]
    public void Gremlin_DisplayHostAndPort_ComeStraightOffTheConnection()
    {
        var provider = GraphDbProviders.For("ApacheTinkerPop");
        var connection = new GremlinDB.GremlinConnection("WebSocket", 8182, false, "192.168.1.5", "", "", "");

        Assert.Equal("192.168.1.5", provider.DisplayHost(connection));
        Assert.Equal(8182, provider.DisplayPort(connection));
    }

    //── IsConfigured ────────────────────────────────────────────────────

    [Fact]
    public void Sparql_IsConfigured_NeedsAnEndpoint()
    {
        var provider = GraphDbProviders.For("Sparql");

        Assert.False(provider.IsConfigured(Sparql("")));
        Assert.False(provider.IsConfigured(Sparql("   ")));
        Assert.True(provider.IsConfigured(Sparql("https://query.wikidata.org/sparql")));
    }

    [Fact]
    public void Gremlin_IsConfigured_HasNoEndpointRequirement()
    {
        var provider = GraphDbProviders.For("ApacheTinkerPop");

        Assert.True(provider.IsConfigured(new GremlinDB.GremlinConnection()));
    }

    //── Client factory ──────────────────────────────────────────────────

    [Fact]
    public void Create_BuildsTheRightClientForEachProvider()
    {
        var http = new HttpClient();
        //GremlinDB builds its endpoint Uri in the constructor, so this needs a real hostname.
        var gremlinConnection = new GremlinDB.GremlinConnection("WebSocket", 8182, false, "localhost", "", "", "");

        Assert.IsType<GremlinDB>(GraphDbProviders.For("ApacheTinkerPop").Create(http, gremlinConnection));
        Assert.IsType<GremlinDB>(GraphDbProviders.For("CosmosDb").Create(http, gremlinConnection));
        Assert.IsType<SparqlDb>(GraphDbProviders.For("Sparql").Create(http, Sparql("https://x/sparql")));
    }
}
