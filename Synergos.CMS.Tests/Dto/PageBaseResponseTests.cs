using Synergos.CMS.Application.Dto.Responses;

namespace Synergos.CMS.Tests.Dto;

public class PageBaseResponseTests
{
    [Fact]
    public void Project_MapsSeoTitleAndBody_FromAccessor()
    {
        var values = new Dictionary<string, string?>
        {
            ["seoTitle"] = "About",
            ["body"] = "<p>Hola.</p>",
        };

        var response = PageBaseResponse.Project(
            alias => values.GetValueOrDefault(alias));

        Assert.Equal("About", response.SeoTitle);
        Assert.Equal("<p>Hola.</p>", response.BodyHtml);
    }

    [Fact]
    public void Project_ReturnsNullFields_WhenAccessorAlwaysReturnsNull()
    {
        var response = PageBaseResponse.Project(_ => null);

        Assert.Null(response.SeoTitle);
        Assert.Null(response.BodyHtml);
    }

    [Fact]
    public void Project_QueriesExactlyTheExpectedAliases()
    {
        var queried = new List<string>();

        PageBaseResponse.Project(alias =>
        {
            queried.Add(alias);
            return null;
        });

        Assert.Equal(2, queried.Count);
        Assert.Contains("seoTitle", queried);
        Assert.Contains("body", queried);
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
    }
}
