using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Application.Blog;

public sealed record BlogPagedResult(
    IReadOnlyList<IPublishedContent> Posts,
    int CurrentPage,
    int TotalPages,
    int TotalItems);
