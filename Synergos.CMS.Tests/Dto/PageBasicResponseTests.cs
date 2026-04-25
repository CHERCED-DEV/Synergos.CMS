using Synergos.CMS.Application.Dto.Responses;

namespace Synergos.CMS.Tests.Dto;

public class PageBasicResponseTests
{
    [Fact]
    public void Project_MapsSeoTitle_FromAccessor()
    {
        var values = new Dictionary<string, string?>
        {
            ["seoTitle"] = "About us",
        };

        var response = PageBasicResponse.Project(
            alias => values.GetValueOrDefault(alias));

        Assert.Equal("About us", response.SeoTitle);
    }

    [Fact]
    public void Project_ReturnsNullSeoTitle_WhenAccessorAlwaysReturnsNull()
    {
        var response = PageBasicResponse.Project(_ => null);

        Assert.Null(response.SeoTitle);
    }

    [Fact]
    public void Project_QueriesOnlySeoTitle()
    {
        // Post-Ola 48: contenido editorial vive en sections.
        var queried = new List<string>();

        PageBasicResponse.Project(alias =>
        {
            queried.Add(alias);
            return null;
        });

        Assert.Single(queried);
        Assert.Contains("seoTitle", queried);
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

        Assert.DoesNotContain("body", queried);
        Assert.DoesNotContain("title", queried);
        Assert.DoesNotContain("description", queried);
        Assert.DoesNotContain("name", queried);
    }
}
