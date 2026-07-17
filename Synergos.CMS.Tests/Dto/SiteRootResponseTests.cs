using Synergos.CMS.Application.Dto.Responses;

namespace Synergos.CMS.Tests.Dto;

public class SiteRootResponseTests
{
    [Fact]
    public void Project_MapsAllFields_FromAccessor()
    {
        var values = new Dictionary<string, string?>
        {
            ["seoTitle"] = "Acme Colombia",
            ["siteDisplayName"] = "Acme",
            ["canonicalHostname"] = "www.acme.co",
        };

        var response = SiteRootResponse.Project(
            alias => values.GetValueOrDefault(alias));

        Assert.Equal("Acme Colombia", response.SeoTitle);
        Assert.Equal("Acme", response.SiteDisplayName);
        Assert.Equal("www.acme.co", response.CanonicalHostname);
    }

    [Fact]
    public void Project_ReturnsNullFields_WhenAccessorAlwaysReturnsNull()
    {
        var response = SiteRootResponse.Project(_ => null);

        Assert.Null(response.SeoTitle);
        Assert.Null(response.SiteDisplayName);
        Assert.Null(response.CanonicalHostname);
    }

    [Fact]
    public void Project_QueriesExactlyTheExpectedAliases()
    {
        var queried = new List<string>();

        SiteRootResponse.Project(alias =>
        {
            queried.Add(alias);
            return null;
        });

        Assert.Equal(3, queried.Count);
        Assert.Contains("seoTitle", queried);
        Assert.Contains("siteDisplayName", queried);
        Assert.Contains("canonicalHostname", queried);
    }

    [Fact]
    public void Project_DoesNotQueryAdminOnlyAliases()
    {
        var queried = new List<string>();

        SiteRootResponse.Project(alias =>
        {
            queried.Add(alias);
            return null;
        });

        // internalNotes is admin-only (compCoreBase) — never projected to public DTO.
        Assert.DoesNotContain("internalNotes", queried);
        Assert.DoesNotContain("publishingNotes", queried);
    }
}
