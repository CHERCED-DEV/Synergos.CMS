using Synergos.CMS.Application.Dto.Responses;

namespace Synergos.CMS.Tests.Dto;

public class PageBareResponseTests
{
    [Fact]
    public void Project_MapsSeoTitleAndBody_FromAccessor()
    {
        var values = new Dictionary<string, string?>
        {
            ["seoTitle"] = "Landing X",
            ["body"] = "<main>Raw.</main>",
        };

        var response = PageBareResponse.Project(
            alias => values.GetValueOrDefault(alias));

        Assert.Equal("Landing X", response.SeoTitle);
        Assert.Equal("<main>Raw.</main>", response.BodyHtml);
    }

    [Fact]
    public void Project_ReturnsNullFields_WhenAccessorAlwaysReturnsNull()
    {
        var response = PageBareResponse.Project(_ => null);

        Assert.Null(response.SeoTitle);
        Assert.Null(response.BodyHtml);
    }

    [Fact]
    public void Project_QueriesExactlyTheExpectedAliases()
    {
        var queried = new List<string>();

        PageBareResponse.Project(alias =>
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

        PageBareResponse.Project(alias =>
        {
            queried.Add(alias);
            return null;
        });

        // pageBare does not compose compCoreLifecycle, but enforces the same
        // contract: admin fields never leak into public DTOs.
        Assert.DoesNotContain("internalNotes", queried);
        Assert.DoesNotContain("publishingNotes", queried);
    }
}
