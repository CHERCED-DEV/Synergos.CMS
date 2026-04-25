using Synergos.CMS.Application.Dto.Responses;

namespace Synergos.CMS.Tests.Dto;

public class PageBaseResponseTests
{
    [Fact]
    public void Project_MapsSeoTitle_FromAccessor()
    {
        var values = new Dictionary<string, string?>
        {
            ["seoTitle"] = "About",
        };

        var response = PageBaseResponse.Project(
            alias => values.GetValueOrDefault(alias));

        Assert.Equal("About", response.SeoTitle);
    }

    [Fact]
    public void Project_ReturnsNullSeoTitle_WhenAccessorAlwaysReturnsNull()
    {
        var response = PageBaseResponse.Project(_ => null);

        Assert.Null(response.SeoTitle);
    }

    [Fact]
    public void Project_QueriesOnlySeoTitle()
    {
        // Post-Ola 48: el contenido editorial vive en sections (Layout
        // Composer) leído por la view directo del IPublishedContent.
        // El DTO solo transporta seoTitle.
        var queried = new List<string>();

        PageBaseResponse.Project(alias =>
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

        PageBaseResponse.Project(alias =>
        {
            queried.Add(alias);
            return null;
        });

        Assert.DoesNotContain("internalNotes", queried);
        Assert.DoesNotContain("publishingNotes", queried);
        Assert.DoesNotContain("body", queried);
        Assert.DoesNotContain("bodyBlocks", queried);
    }
}
