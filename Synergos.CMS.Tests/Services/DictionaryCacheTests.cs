using Synergos.CMS.Application.Services.Impl;

namespace Synergos.CMS.Tests.Services;

public class DictionaryCacheTests
{
    [Fact]
    public void Get_ReturnsNull_OnEmptyCache()
    {
        var sut = new DictionaryCache();

        Assert.Null(sut.Get("Nav.Main"));
    }

    [Fact]
    public void Get_ReturnsValue_AfterSet_WithoutCulture()
    {
        var sut = new DictionaryCache();
        sut.Set("Nav.Main", "Menú");

        Assert.Equal("Menú", sut.Get("Nav.Main"));
    }

    [Fact]
    public void Get_ReturnsCultureSpecificValue_WhenCultureProvided()
    {
        var sut = new DictionaryCache();
        sut.Set("Nav.Main", "Menú", culture: "es");
        sut.Set("Nav.Main", "Menu", culture: "en");

        Assert.Equal("Menú", sut.Get("Nav.Main", culture: "es"));
        Assert.Equal("Menu", sut.Get("Nav.Main", culture: "en"));
    }

    [Fact]
    public void Get_ReturnsNull_WhenCultureDoesNotMatch()
    {
        var sut = new DictionaryCache();
        sut.Set("Nav.Main", "Menú", culture: "es");

        Assert.Null(sut.Get("Nav.Main", culture: "en"));
    }

    [Fact]
    public void Invalidate_WithKey_RemovesAllCultures()
    {
        var sut = new DictionaryCache();
        sut.Set("Nav.Main", "Menú", culture: "es");
        sut.Set("Nav.Main", "Menu", culture: "en");
        sut.Set("Footer.Copy", "© 2026");

        sut.Invalidate("Nav.Main");

        Assert.Null(sut.Get("Nav.Main", culture: "es"));
        Assert.Null(sut.Get("Nav.Main", culture: "en"));
        Assert.Equal("© 2026", sut.Get("Footer.Copy"));
    }

    [Fact]
    public void Invalidate_WithoutKey_ClearsEverything()
    {
        var sut = new DictionaryCache();
        sut.Set("Nav.Main", "Menú", culture: "es");
        sut.Set("Footer.Copy", "© 2026");

        sut.Invalidate();

        Assert.Null(sut.Get("Nav.Main", culture: "es"));
        Assert.Null(sut.Get("Footer.Copy"));
    }

    [Fact]
    public void Invalidate_UnknownKey_DoesNotThrow()
    {
        var sut = new DictionaryCache();
        sut.Set("existing", "v");

        sut.Invalidate("unknown");

        Assert.Equal("v", sut.Get("existing"));
    }
}
