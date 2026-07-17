using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests de la regla única de returnUrl seguro (<see cref="LocalReturnUrl"/>).
/// Cubre el open redirect que tenían las 3 copias previas: validar con
/// <c>Uri.TryCreate(raw, UriKind.Relative, out _)</c> deja pasar
/// <c>//evil.com</c> porque ES una URI relativa válida.
/// </summary>
public sealed class LocalReturnUrlTests
{
    [Theory]
    // El bug original: protocol-relative → otro origen.
    [InlineData("//evil.com")]
    [InlineData("//evil.com/x")]
    // Backslash variant — los navegadores lo tratan como "/" en el authority.
    [InlineData("/\\evil.com")]
    [InlineData("/\\/evil.com")]
    // Absolutas con esquema.
    [InlineData("https://evil.com")]
    [InlineData("http://evil.com/x")]
    [InlineData("javascript:alert(1)")]
    // Control chars: el navegador los descarta y "/\t/evil.com" → "//evil.com".
    [InlineData("/\t/evil.com")]
    [InlineData("/\n/evil.com")]
    [InlineData("/\r/evil.com")]
    // Tilde: Redirect() crudo no lo resuelve, no es un destino válido.
    [InlineData("~/x")]
    // Relativa sin "/" inicial: resolvería contra el path actual, no la queremos.
    [InlineData("evil.com")]
    // Vacíos.
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Rechaza_LoQueNoEsPathLocal(string? raw)
    {
        Assert.False(LocalReturnUrl.IsLocal(raw));
        Assert.Equal("/", LocalReturnUrl.Sanitize(raw));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/tramites")]
    [InlineData("/tramites#applications")]
    [InlineData("/account/profile?tab=x")]
    [InlineData("/a?b=c&d=e")]
    [InlineData("/tramites/sub/path")]
    // Un solo "/" seguido de "/" solo es malo en posición 1; aquí no.
    [InlineData("/a//b")]
    public void Acepta_PathsLocales_TalCual(string raw)
    {
        Assert.True(LocalReturnUrl.IsLocal(raw));
        Assert.Equal(raw, LocalReturnUrl.Sanitize(raw));
    }

    /// <summary>
    /// Test de NO-tautología: deja constancia de que el check viejo
    /// (<c>Uri.TryCreate(..., UriKind.Relative, ...)</c>) SÍ aprobaba el
    /// payload del ataque. Si alguien vuelve a esa regla, este test explica
    /// por qué no sirve.
    /// </summary>
    [Fact]
    public void ElCheckViejo_AprobabaElPayload_ElNuevoNo()
    {
        const string payload = "//evil.com/x";

        Assert.True(Uri.TryCreate(payload, UriKind.Relative, out _));
        Assert.False(LocalReturnUrl.IsLocal(payload));
    }
}
