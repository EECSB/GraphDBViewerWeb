using System.Text.Json;
using GraphDBViewerWeb.Code;

namespace GraphDBViewerWeb.Tests;

///<summary>
///Unit tests for the non-network models in GremlinDB. The result type moved out to GraphDbResult when
///the client was put behind IGraphDb — see GraphDbResultTests.
///</summary>
public class GremlinDbModelsTests
{
    //── GremlinConnection ───────────────────────────────────────────────

    [Fact]
    public void GremlinConnection_FullConstructor_SetsAllProperties()
    {
        var c = new GremlinDB.GremlinConnection("WebSocket", 8182, false, "host", "key", "db", "coll");

        Assert.Equal("WebSocket", c.Transport);
        Assert.Equal(8182, c.Port);
        Assert.False(c.UseSSL);
        Assert.Equal("host", c.Hostname);
        Assert.Equal("key", c.AuthKey);
        Assert.Equal("db", c.Database);
        Assert.Equal("coll", c.Collection);
    }

    [Fact]
    public void GremlinConnection_CopyConstructor_CopiesAllProperties()
    {
        var original = new GremlinDB.GremlinConnection("HTTP", 443, true, "h", "k", "d", "c");
        var copy = new GremlinDB.GremlinConnection(original);

        Assert.Equal(original.Transport, copy.Transport);
        Assert.Equal(original.Port, copy.Port);
        Assert.Equal(original.UseSSL, copy.UseSSL);
        Assert.Equal(original.Hostname, copy.Hostname);
        Assert.Equal(original.AuthKey, copy.AuthKey);
        Assert.Equal(original.Database, copy.Database);
        Assert.Equal(original.Collection, copy.Collection);
    }

    [Fact]
    public void GremlinConnection_Copy_IsIndependentOfOriginal()
    {
        var original = new GremlinDB.GremlinConnection("HTTP", 443, true, "h", "k", "d", "c");
        var copy = new GremlinDB.GremlinConnection(original);

        copy.Hostname = "changed";

        Assert.Equal("h", original.Hostname);
    }
}
