using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Synergos.CMS.Web.Services;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="SynergosIdentitySeeder"/> — el andamio de la vitrina SynergosLabs, que
/// llevaba roto y contestando que sí (#119).
/// </summary>
/// <remarks>
/// <para>No tenía ni un test, y por eso sobrevivieron dos defectos a la vez: el publish del
/// <c>platformRoot</c> se caía por dos obligatorias de <c>compBranding</c> sin poner, y cada
/// llamada creaba <b>otro</b> árbol entero.</para>
///
/// <para><b>El de la marca no se prueba comprobando que se llamó a <c>SetValue</c>:</b> eso
/// afirma lo que el seeder decidió hacer, no lo que Umbraco exige. Se prueba contra los
/// aliases que el schema declara obligatorios, leídos de
/// <c>uSync/v9/ContentTypes/compbranding.config</c> — si mañana <c>compBranding</c> añade una
/// tercera, este test se pone rojo antes de que el seeder vuelva a caerse en silencio.</para>
/// </remarks>
public sealed class SynergosIdentitySeederTests
{
    private const string Culture = "es-CO";

    private readonly IContentService _content = Substitute.For<IContentService>();
    private readonly IContentTypeService _types = Substitute.For<IContentTypeService>();
    private readonly List<(string Alias, IDictionary<string, string> Valores)> _creados = new();
    private int _siguienteId = 100;

    public SynergosIdentitySeederTests()
    {
        foreach (var alias in new[] { "platformRoot", "siteRoot", "pageBase" })
        {
            var tipo = Substitute.For<IContentType>();
            tipo.Alias.Returns(alias);
            _types.Get(alias).Returns(tipo);
        }
        _content.GetRootContent().Returns(Array.Empty<IContent>());
        SinHijos();
    }

    private SynergosIdentitySeeder Make() =>
        new(_content, _types, NullLogger<SynergosIdentitySeeder>.Instance);

    private void SinHijos() => _content
        .GetPagedChildren(Arg.Any<int>(), Arg.Any<long>(), Arg.Any<int>(), out Arg.Any<long>())
        .Returns(Array.Empty<IContent>());

    private IContent Nodo(string alias, string nombre, IDictionary<string, string> valores, int? id = null)
    {
        var simple = Substitute.For<ISimpleContentType>();
        simple.Alias.Returns(alias);
        var nodo = Substitute.For<IContent>();
        nodo.ContentType.Returns(simple);
        nodo.Id.Returns(id ?? (_siguienteId++));
        nodo.Name.Returns(nombre);
        nodo.HasProperty(Arg.Any<string>()).Returns(true);
        nodo.GetValue<string>(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool>())
            .Returns(ci => valores.TryGetValue(ci.ArgAt<string>(0), out var v) ? v : null);
        nodo.When(n => n.SetValue(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<string?>(), Arg.Any<string?>()))
            .Do(ci => valores[ci.ArgAt<string>(0)] = ci.ArgAt<object?>(1)?.ToString() ?? string.Empty);
        _content.SaveAndPublish(nodo, Arg.Any<string[]>(), Arg.Any<int>())
            .Returns(new PublishResult(PublishResultType.SuccessPublish, null, nodo));
        return nodo;
    }

    /// <summary>Todo <c>Create</c> devuelve un nodo nuevo y anota qué se le puso.</summary>
    private void CreaLoQueLePidan() => _content
        .Create(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>())
        .Returns(ci =>
        {
            var alias = ci.ArgAt<string>(2);
            var valores = new Dictionary<string, string>(StringComparer.Ordinal);
            _creados.Add((alias, valores));
            return Nodo(alias, ci.ArgAt<string>(0), valores);
        });

    [Fact] // happy: se crean el platformRoot, el siteRoot y las tres páginas
    public void Seed_ArbolVacio_CreaElAndamioCompleto()
    {
        CreaLoQueLePidan();

        var r = Make().Seed();

        Assert.True(r.Success);
        Assert.Equal(3, r.PagesCreated);
        Assert.Equal(1, _creados.Count(c => c.Alias == "platformRoot"));
        Assert.Equal(1, _creados.Count(c => c.Alias == "siteRoot"));
        Assert.Equal(3, _creados.Count(c => c.Alias == "pageBase"));
    }

    [Fact] // el defecto: sin las obligatorias de compBranding el publish se cae y no se crea nada
    public void Seed_ElPlatformRoot_LlevaLasObligatoriasDeCompBranding()
    {
        CreaLoQueLePidan();

        Make().Seed();

        var platform = _creados.Single(c => c.Alias == "platformRoot").Valores;
        foreach (var alias in ObligatoriasDeCompBranding())
        {
            Assert.True(platform.TryGetValue(alias, out var v) && !string.IsNullOrWhiteSpace(v),
                $"El platformRoot no lleva '{alias}', que compBranding declara obligatoria: el "
                + "publish se cae con FailedPublishContentInvalid y el seeder no crea NADA (#119).");
        }
    }

    [Fact] // idempotent: con el árbol ya puesto no se crea un segundo de nada
    public void Seed_ConElArbolPuesto_NoDuplicaNada()
    {
        var platform = Nodo("platformRoot", "Synergos Platform", new Dictionary<string, string>(StringComparer.Ordinal), id: 10);
        var site = Nodo("siteRoot", "Synergos", new Dictionary<string, string>(StringComparer.Ordinal), id: 20);
        var paginas = new[] { "Home", "Identidad", "Contacto" }
            .Select((n, i) => Nodo("pageBase", n, new Dictionary<string, string>(StringComparer.Ordinal), id: 30 + i))
            .ToArray();

        _content.GetRootContent().Returns(new[] { platform });
        _content.GetPagedChildren(platform.Id, Arg.Any<long>(), Arg.Any<int>(), out Arg.Any<long>())
            .Returns(new[] { site });
        _content.GetPagedChildren(site.Id, Arg.Any<long>(), Arg.Any<int>(), out Arg.Any<long>())
            .Returns(paginas);
        CreaLoQueLePidan();

        var r = Make().Seed();

        Assert.True(r.Success);
        Assert.Equal(0, r.PagesCreated);
        // Un segundo árbol no da un error: da DOS raíces, y desde ahí `/` resuelve a una y
        // DevContentFiller —que toma la primera que encuentra— llena la otra.
        Assert.Empty(_creados);
        _content.DidNotReceive().Create(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>());
    }

    [Fact] // sin schema no se siembra, y se dice qué falta
    public void Seed_SinSchema_NoSiembra()
    {
        _types.Get("pageBase").Returns((IContentType?)null);
        CreaLoQueLePidan();

        var r = Make().Seed();

        Assert.False(r.Success);
        Assert.Contains("pageBase", r.Detail, StringComparison.Ordinal);
        Assert.Empty(_creados);
    }

    /// <summary>Los aliases que <c>compBranding</c> declara obligatorios, leídos del schema.</summary>
    private static IReadOnlyList<string> ObligatoriasDeCompBranding()
    {
        var ruta = Path.Combine(RepoRoot(), "Synergos.CMS.Web", "uSync", "v9",
            "ContentTypes", "compbranding.config");
        var texto = System.IO.File.ReadAllText(ruta);
        var propiedades = System.Text.RegularExpressions.Regex.Matches(
            texto, @"<GenericProperty>[\s\S]*?</GenericProperty>");

        var obligatorias = propiedades
            .Select(m => m.Value)
            .Where(p => p.Contains("<Mandatory>true</Mandatory>", StringComparison.OrdinalIgnoreCase))
            .Select(p => System.Text.RegularExpressions.Regex.Match(p, @"<Alias>([^<]+)</Alias>").Groups[1].Value)
            .Where(a => a.Length > 0)
            .ToList();

        Assert.True(obligatorias.Count > 0,
            "compbranding.config no declara ninguna obligatoria: el schema cambió de forma y este "
            + "test dejó de mirar lo que cree que mira.");
        return obligatorias;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir.FullName;
    }
}
