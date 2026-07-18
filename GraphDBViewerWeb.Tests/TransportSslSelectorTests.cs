using Bunit;
using GraphDBViewerWeb.Code;
using GraphDBViewerWeb.Components;

namespace GraphDBViewerWeb.Tests;

//Markup cover for the Transport/SSL selector: its buttons write the "HTTP"/"WebSocket" literals that
//GremlinDB reads back when it builds the connection URI, and it mutates the passed connection object
//directly (by design — the caller reads the values back off the same reference).
public class TransportSslSelectorTests : BunitContext
{
    [Fact]
    public void ClickingHttp_WritesTheTransportTheClientReadsBack()
    {
        var conn = new GremlinDB.GremlinConnection { Transport = "WebSocket", UseSSL = true };
        var cut = Render<TransportSslSelector>(p => p.Add(c => c.Connection, conn));

        cut.FindAll("button").First(b => b.TextContent.Trim() == "HTTP").Click();

        Assert.Equal("HTTP", conn.Transport);
    }

    [Theory]
    [InlineData("WebSocket", true, "wss")]
    [InlineData("WebSocket", false, "ws")]
    [InlineData("HTTP", true, "https")]
    [InlineData("HTTP", false, "http")]
    public void Badge_ShowsTheSchemeTheConnectionWillUse(string transport, bool ssl, string expected)
    {
        var conn = new GremlinDB.GremlinConnection { Transport = transport, UseSSL = ssl };
        var cut = Render<TransportSslSelector>(p => p.Add(c => c.Connection, conn));

        Assert.Equal(expected, cut.Find(".badge").TextContent.Trim());
    }
}
