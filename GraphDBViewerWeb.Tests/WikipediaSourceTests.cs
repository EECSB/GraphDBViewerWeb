using GraphDBViewerWeb.Code;

namespace GraphDBViewerWeb.Tests;

public class WikipediaSourceTests
{
    [Fact]
    public void BuildExtractUrl_EncodesTheTitleAndStaysCorsOpen()
    {
        var url = WikipediaSource.BuildExtractUrl("Nikola Tesla");

        Assert.Contains("titles=Nikola%20Tesla", url);
        Assert.Contains("origin=*", url);
        Assert.Contains("explaintext=1", url);
    }

    [Fact]
    public void ParseExtract_ReadsThePageText()
    {
        var json = """
            {"query":{"pages":{"21473":{"pageid":21473,"title":"Nikola Tesla","extract":"Nikola Tesla was an inventor."}}}}
            """;

        Assert.Equal("Nikola Tesla was an inventor.", WikipediaSource.ParseExtract(json));
    }

    [Fact]
    public void ParseExtract_MissingPage_ReturnsNull()
    {
        var json = """
            {"query":{"pages":{"-1":{"title":"Nope Nope","missing":""}}}}
            """;

        Assert.Null(WikipediaSource.ParseExtract(json));
    }

    [Fact]
    public void ParseExtract_Garbage_ReturnsNull()
    {
        Assert.Null(WikipediaSource.ParseExtract("not json"));
    }
}
