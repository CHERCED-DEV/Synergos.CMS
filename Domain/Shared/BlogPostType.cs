namespace Synergos.CMS.Domain.Shared;

/// <summary>
/// Editorial classification of a blog post. Drives list filtering, badge
/// rendering, and sometimes template variation (e.g. tutorials may get a
/// "Difficulty" sidebar while news posts get a compact timeline).
///
/// Stored value in Umbraco: lowercase camelCase alias wrapped in the single-
/// element JSON array that DropDown.Flexible uses (e.g. <c>["tutorial"]</c>).
/// Use <see cref="BlogPostTypeExtensions.ToAlias"/> to obtain the stored form
/// and <see cref="BlogPostTypeExtensions.FromAlias"/> to parse it.
/// </summary>
public enum BlogPostType
{
    /// <summary>Default. Long-form editorial piece.</summary>
    Article,

    /// <summary>Short news item or announcement.</summary>
    News,

    /// <summary>Step-by-step how-to with code / screenshots.</summary>
    Tutorial,

    /// <summary>Customer or project showcase.</summary>
    CaseStudy,

    /// <summary>Q&amp;A with a person (employee, customer, partner).</summary>
    Interview,

    /// <summary>Opinion / editorial by an author.</summary>
    Opinion,

    /// <summary>Release notes / changelog entry.</summary>
    Release
}

/// <summary>
/// Conversion between <see cref="BlogPostType"/> and the Umbraco dropdown
/// string aliases stored against the <c>postType</c> property. Centralising
/// the mapping means adding a new type requires updating exactly one place.
/// </summary>
public static class BlogPostTypeExtensions
{
    public static string ToAlias(this BlogPostType type) => type switch
    {
        BlogPostType.Article   => "article",
        BlogPostType.News      => "news",
        BlogPostType.Tutorial  => "tutorial",
        BlogPostType.CaseStudy => "caseStudy",
        BlogPostType.Interview => "interview",
        BlogPostType.Opinion   => "opinion",
        BlogPostType.Release   => "release",
        _                       => "article"
    };

    /// <summary>
    /// Parses a stored alias back to <see cref="BlogPostType"/>. Unknown or
    /// null values collapse to <see cref="BlogPostType.Article"/> — article
    /// is the safe default that preserves the previous "plain post" behaviour
    /// for older content created before this enum existed.
    /// </summary>
    public static BlogPostType FromAlias(string? alias) => alias switch
    {
        "article"   => BlogPostType.Article,
        "news"      => BlogPostType.News,
        "tutorial"  => BlogPostType.Tutorial,
        "caseStudy" => BlogPostType.CaseStudy,
        "interview" => BlogPostType.Interview,
        "opinion"   => BlogPostType.Opinion,
        "release"   => BlogPostType.Release,
        _           => BlogPostType.Article
    };
}
