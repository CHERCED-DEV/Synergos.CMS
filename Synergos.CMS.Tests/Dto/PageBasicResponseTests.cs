using Synergos.CMS.Application.Dto.Responses;

namespace Synergos.CMS.Tests.Dto;

public class PageBasicResponseTests
{
    [Fact]
    public void Project_MapsSeoTitleAndBody_FromAccessor()
    {
        var values = new Dictionary<string, string?>
        {
            ["seoTitle"] = "About us",
            ["body"] = "<p>Hello.</p>",
        };

        var response = PageBasicResponse.Project(
            alias => values.GetValueOrDefault(alias));

        Assert.Equal("About us", response.SeoTitle);
        Assert.Equal("<p>Hello.</p>", response.BodyHtml);
    }

    [Fact]
    public void Project_ReturnsNullFields_WhenAccessorAlwaysReturnsNull()
    {
        var response = PageBasicResponse.Project(_ => null);

        Assert.Null(response.SeoTitle);
        Assert.Null(response.BodyHtml);
    }

    [Fact]
    public void Project_QueriesExactlyTheExpectedAliases()
    {
        var queried = new List<string>();

        PageBasicResponse.Project(alias =>
        {
            queried.Add(alias);
            return null;
        });

        Assert.Equal(2, queried.Count);
        Assert.Contains("seoTitle", queried);
        Assert.Contains("body", queried);
    }

    [Fact]
    public void Project_DoesNotQueryUnexpectedAliases()
    {
        var queried = new List<string>();

        PageBasicResponse.Project(alias =>
        {
            queried.Add(alias);
            return null;
        });

        // Guardrail: if a future refactor starts reading more aliases,
        // this test forces the author to update the ADR scope.
        Assert.DoesNotContain("title", queried);
        Assert.DoesNotContain("description", queried);
        Assert.DoesNotContain("name", queried);
    }
}
