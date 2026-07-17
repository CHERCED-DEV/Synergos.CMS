using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Tests para <see cref="BlogTagController"/> (Ola 230, ADR 0100) cubriendo
/// el wiring del seam IBlogQuery.GetByTag: empty / happy / filter (tag
/// distinto) / blank-tag (no consulta).
/// </summary>
public sealed class BlogTagControllerTests
{
    private static BlogTagController Build(FakeBlogQuery query) =>
        new(query, Options.Create(new BlogSettings { CategoryPageSize = 12 }));

    private static PostSummary Post(string title, params string[] tags) =>
        new(Url: "/" + title, Title: title, Excerpt: null, HeroImageUrl: null,
            PublishDate: null, ReadTimeMinutes: null, CategoryName: null, Tags: tags);

    [Fact]
    public void Index_NoMatchingPosts_ReturnsEmptyModel()
    {
        var controller = Build(new FakeBlogQuery()); // returns empty for any tag

        var result = controller.Index("inexistente");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<BlogTagViewModel>(view.Model);
        Assert.Equal("inexistente", model.Tag);
        Assert.Empty(model.Posts);
    }

    [Fact]
    public void Index_MatchingTag_ReturnsPostsForThatTag()
    {
        var query = new FakeBlogQuery
        {
            ["arquitectura"] = new[] { Post("a", "arquitectura"), Post("b", "arquitectura") },
        };
        var controller = Build(query);

        var result = controller.Index("arquitectura");

        var model = Assert.IsType<BlogTagViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(2, model.Posts.Count);
        Assert.Equal("arquitectura", query.LastTag);
    }

    [Fact]
    public void Index_TrimsTagBeforeQuery()
    {
        var query = new FakeBlogQuery
        {
            ["cdn"] = new[] { Post("x", "cdn") },
        };
        var controller = Build(query);

        var result = controller.Index("  cdn  ");

        var model = Assert.IsType<BlogTagViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Single(model.Posts);
        Assert.Equal("cdn", model.Tag);
        Assert.Equal("cdn", query.LastTag);
    }

    [Fact]
    public void Index_BlankTag_DoesNotQueryAndReturnsEmpty()
    {
        var query = new FakeBlogQuery();
        var controller = Build(query);

        var result = controller.Index("   ");

        var model = Assert.IsType<BlogTagViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Empty(model.Posts);
        Assert.Null(query.LastTag); // GetByTag nunca se invocó
    }

    private sealed class FakeBlogQuery : IBlogQuery
    {
        private readonly Dictionary<string, IReadOnlyList<PostSummary>> _byTag = new(StringComparer.OrdinalIgnoreCase);

        public string? LastTag { get; private set; }

        public IReadOnlyList<PostSummary> this[string tag]
        {
            set => _byTag[tag] = value;
        }

        public IReadOnlyList<PostSummary> GetByTag(string tag, int maxItems)
        {
            LastTag = tag;
            return _byTag.TryGetValue(tag, out var posts) ? posts : System.Array.Empty<PostSummary>();
        }

        public IReadOnlyList<PostSummary> GetPosts(BlogQueryRequest request) =>
            System.Array.Empty<PostSummary>();

        public IReadOnlyList<PostSummary> GetRelated(Guid postKey, int maxItems) =>
            System.Array.Empty<PostSummary>();
    }
}
