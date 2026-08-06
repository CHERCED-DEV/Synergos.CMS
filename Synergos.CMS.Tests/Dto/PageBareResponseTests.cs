using Synergos.CMS.Application.Dto.Responses;

namespace Synergos.CMS.Tests.Dto;

public class PageBareResponseTests
{
    [Fact]
    public void Project_MapsSeoTitle_FromAccessor()
    {
        var values = new Dictionary<string, string?>
        {
            ["seoTitle"] = "Landing X",
        };

        var response = PageBareResponse.Project(
            alias => values.GetValueOrDefault(alias));

        Assert.Equal("Landing X", response.SeoTitle);
    }

    [Fact]
    public void Project_ReturnsNullSeoTitle_WhenAccessorAlwaysReturnsNull()
    {
        var response = PageBareResponse.Project(_ => null);

        Assert.Null(response.SeoTitle);
    }

    [Fact]
    public void Project_QueriesOnlySeoTitle()
    {
        // Post-Ola 48: contenido editorial vive en sections.
        var queried = new List<string>();

        PageBareResponse.Project(alias =>
        {
            queried.Add(alias);
            return null;
        });

        Assert.Single(queried);
        Assert.Contains("seoTitle", queried);
    }

    [Fact]
    public void Project_DoesNotQueryAdminOnlyAliases()
    {
        var queried = new List<string>();

        PageBareResponse.Project(alias =>
        {
            queried.Add(alias);
            return null;
        });

        Assert.DoesNotContain("body", queried);
        Assert.DoesNotContain("internalNotes", queried);
        Assert.DoesNotContain("publishingNotes", queried);
    }
}
