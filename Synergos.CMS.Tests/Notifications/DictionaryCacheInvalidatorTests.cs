using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Notifications;

namespace Synergos.CMS.Tests.Notifications;

public class DictionaryCacheInvalidatorTests
{
    private sealed class RecordingCache : IDictionaryCache
    {
        public List<string?> Invalidations { get; } = new();
        public string? Get(string key, string? culture = null) => null;
        public void Invalidate(string? key = null) => Invalidations.Add(key);
    }

    [Fact]
    public void InvalidateKeys_CallsCacheForEachNonBlankKey()
    {
        var cache = new RecordingCache();
        var sut = new DictionaryCacheInvalidator(cache);

        sut.InvalidateKeys(new[] { "Nav.Main", "Footer.Copy" });

        Assert.Equal(new[] { "Nav.Main", "Footer.Copy" }, cache.Invalidations);
    }

    [Fact]
    public void InvalidateKeys_SkipsBlankAndNullKeys()
    {
        var cache = new RecordingCache();
        var sut = new DictionaryCacheInvalidator(cache);

        sut.InvalidateKeys(new[] { "", "   ", null!, "Nav.Main" });

        Assert.Equal(new[] { "Nav.Main" }, cache.Invalidations);
    }

    [Fact]
    public void InvalidateKeys_NoOp_WhenCollectionEmpty()
    {
        var cache = new RecordingCache();
        var sut = new DictionaryCacheInvalidator(cache);

        sut.InvalidateKeys(Array.Empty<string>());

        Assert.Empty(cache.Invalidations);
    }
}
