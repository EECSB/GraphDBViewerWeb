using AngleSharp.Dom;
using Bunit;
using GraphDBViewerWeb.Code;
using GraphDBViewerWeb.Components;
using Microsoft.Extensions.DependencyInjection;

namespace GraphDBViewerWeb.Tests;

//Markup cover for the AI-model form. Its provider buttons write the literals LlmProviderFactory.Create
//and OpenAiProvider.DefaultBaseUrlFor read back, and its per-provider fields (Max tokens, placeholders)
//encode decisions that are pinned in C# tests but were invisible at the markup layer until now.
public class NlQueryModalTests : BunitContext
{
    private IRenderedComponent<NlQueryModal> RenderModalWithOpenForm()
    {
        Services.AddSingleton<IAppStorage>(new NullStorage());
        Services.AddSingleton(new HttpClient());
        Services.AddScoped<LlmConnectionStore>();

        var cut = Render<NlQueryModal>(p => p.Add(c => c.Visible, true));

        //"+" opens the add-model form; a fresh LlmConnection defaults to Anthropic.
        cut.FindAll("button").First(b => b.TextContent.Trim() == "+").Click();

        return cut;
    }

    //Only the Anthropic adapter sends an output cap, so the box would silently do nothing elsewhere.
    [Fact]
    public void MaxTokens_ShownOnlyForAnthropic()
    {
        var cut = RenderModalWithOpenForm();

        Assert.Contains(cut.FindAll("label"), l => l.TextContent.Trim() == "Max tokens");

        cut.FindAll("button").First(b => b.TextContent.Trim() == "OpenAI").Click();

        Assert.DoesNotContain(cut.FindAll("label"), l => l.TextContent.Trim() == "Max tokens");
    }

    //Anthropic's model field is the one optional one, so its placeholder must name the model a blank
    //field actually runs — derived from the provider constant so the offer and the behavior can't drift
    //apart again (they did once: the placeholder said haiku while a blank field ran opus).
    [Fact]
    public void AnthropicModelPlaceholder_NamesTheDefaultThatActuallyRuns()
    {
        var cut = RenderModalWithOpenForm();

        Assert.NotNull(cut.Find($"input[placeholder='{AnthropicProvider.DefaultModel} (default)']"));
    }

    [Fact]
    public void GeminiButton_OffersGeminisEndpointAsTheBaseUrl()
    {
        var cut = RenderModalWithOpenForm();

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Gemini").Click();

        Assert.NotNull(cut.Find($"input[placeholder='{OpenAiProvider.GeminiBaseUrl}']"));
    }

    //The four provider families the factory routes on. "Other" is the label; its value is "Custom".
    [Fact]
    public void ProviderGroup_OffersAllFourFamilies()
    {
        var cut = RenderModalWithOpenForm();

        var group = cut.Find("[aria-label='Provider']");
        var texts = group.QuerySelectorAll("button").Select(b => b.TextContent.Trim()).ToList();

        Assert.Equal(new[] { "Anthropic", "OpenAI", "Gemini", "Other" }, texts);
    }
}
