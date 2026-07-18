using Bunit;
using GraphDBViewerWeb.Components;

namespace GraphDBViewerWeb.Tests;

//Component-level cover for the Import panel's gate: it validates the pasted text and only then raises
//the callback. Unlike the rest of this project's tests, these render real Razor markup.
public class JsonInputPanelTests : BunitContext
{
    //Located by text rather than position, so adding or reordering buttons can't silently point these
    //tests at the wrong control.
    private static AngleSharp.Dom.IElement VisualizeButton(IRenderedComponent<JsonInputPanel> cut)
    {
        return cut.FindAll("button").First(b => b.TextContent.Trim() == "Visualize");
    }

    [Fact]
    public void Visualize_UnrecognizedText_ShowsErrorAndDoesNotRaise()
    {
        string raised = null;
        var cut = Render<JsonInputPanel>(p => p.Add(c => c.OnVisualize, s => raised = s));

        cut.Find("textarea").Input("this is not a graph in any format");
        VisualizeButton(cut).Click();

        Assert.Null(raised);
        Assert.Contains("Paste GraphSON/JSON", cut.Markup);
    }

    [Fact]
    public void Visualize_Dot_RaisesWithThePastedText()
    {
        string raised = null;
        var cut = Render<JsonInputPanel>(p => p.Add(c => c.OnVisualize, s => raised = s));

        cut.Find("textarea").Input("digraph { a -> b [label=knows] }");
        VisualizeButton(cut).Click();

        Assert.Equal("digraph { a -> b [label=knows] }", raised);
    }

    [Fact]
    public void Visualize_ExportedGremlin_RaisesSoADrawingCanRoundTrip()
    {
        string raised = null;
        var cut = Render<JsonInputPanel>(p => p.Add(c => c.OnVisualize, s => raised = s));

        cut.Find("textarea").Input("g.addV('person').property(T.id, 'alice')");
        VisualizeButton(cut).Click();

        Assert.NotNull(raised);
    }

    [Fact]
    public void Buttons_AreDisabledUntilSomethingIsPasted()
    {
        var cut = Render<JsonInputPanel>();

        Assert.True(VisualizeButton(cut).HasAttribute("disabled"));

        cut.Find("textarea").Input("digraph { a -> b }");

        Assert.False(VisualizeButton(cut).HasAttribute("disabled"));
    }

}
