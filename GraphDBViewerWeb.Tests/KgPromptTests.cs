using GraphDBViewerWeb.Code;

namespace GraphDBViewerWeb.Tests;

public class KgPromptTests
{
    [Fact]
    public void BuildSystemPrompt_PopulatedSchema_ConstrainsToTheListedLabels()
    {
        var schema = new SchemaVocabulary
        {
            VertexLabels = new() { "Person", "Company" },
            EdgeLabels = new() { "worksAt" },
            PropertyKeys = new() { "name" }
        };

        var prompt = KgPrompt.BuildSystemPrompt(schema);

        Assert.Contains("Use ONLY", prompt);
        Assert.Contains("Vertex labels: Person, Company", prompt);
        Assert.Contains("Edge labels: worksAt", prompt);
    }

    //The trap NlQueryPrompt used to have (fixed in e071c94): a constraint asserted over an empty
    //listing. A read-but-empty graph must say so instead.
    [Fact]
    public void BuildSystemPrompt_EmptySchema_DropsTheConstraintAndSaysTheGraphIsEmpty()
    {
        var prompt = KgPrompt.BuildSystemPrompt(new SchemaVocabulary());

        Assert.DoesNotContain("Use ONLY", prompt);
        Assert.Contains("empty", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_NullSchema_DropsTheConstraintWithoutClaimingEmpty()
    {
        var prompt = KgPrompt.BuildSystemPrompt(null);

        Assert.DoesNotContain("Use ONLY", prompt);
        Assert.DoesNotContain("empty", prompt);
        Assert.NotEqual(KgPrompt.BuildSystemPrompt(new SchemaVocabulary()), prompt);
    }

    //The fold is the safety net, not the mechanism — coreference is the model's job.
    [Fact]
    public void BuildSystemPrompt_AsksForCanonicalEntityNames()
    {
        Assert.Contains("canonical", KgPrompt.BuildSystemPrompt(null));
    }

    //The prompt quotes the parser's own constants, so the ask and the enforcement can't drift.
    [Fact]
    public void BuildSystemPrompt_QuotesTheParsersCaps()
    {
        var prompt = KgPrompt.BuildSystemPrompt(null);

        Assert.Contains($"{KgGraphParser.MaxNodes} nodes", prompt);
        Assert.Contains($"{KgGraphParser.MaxEdges} edges", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_AsksForStrictJsonWithoutFences()
    {
        var prompt = KgPrompt.BuildSystemPrompt(null);

        Assert.Contains("\"nodes\"", prompt);
        Assert.Contains("\"edges\"", prompt);
        Assert.Contains("no markdown code fences", prompt);
    }
}
