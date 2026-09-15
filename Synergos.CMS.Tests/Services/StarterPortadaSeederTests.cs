using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Synergos.CMS.Web.Services;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StarterPortadaSeeder"/> — la herramienta dev-only (ADR 0013) que crea la
/// portada de arranque, el último hueco entre «los contenedores están sanos» y «el sitio
/// enseña algo» (#119).
/// </summary>
/// <remarks>
/// <para>Los cuatro casos de ADR 0075: empty (sin árbol, la crea) / happy (el cuerpo lleva las
/// tres bandas con sus obligatorias) / filter (un siteRoot que ya existe conserva su marca) /
/// idempotent (segunda corrida no toca nada).</para>
///
/// <para><b>El caso que de verdad cuesta es el idempotente, y no por duplicar.</b> Lo que hay
/// que impedir no es un segundo nodo: es que una segunda corrida <b>pise</b> la portada que
/// el arquitecto acaba de ajustar en el backoffice, justo antes de exportarla. Por eso el
/// test no comprueba «no se creó otro»: comprueba que <c>SaveAndPublish</c> <b>no se llamó</b>.
/// Un seeder que reescribe lo mismo pasa el primer criterio y falla el que importa.</para>
///
/// <para><b>Y el cuerpo se prueba sobre el JSON serializado, no sobre el builder.</b> Un
/// bloque colocado en un area que no existe se guarda igual, sin error, y la página publica
/// <b>vacía</b>: lo que hay que vigilar es la Key del area dentro del JSON, que es lo que lee
/// Umbraco.</para>
/// </remarks>
public sealed class StarterPortadaSeederTests
{
    private const string Culture = "es-CO";

    private static readonly string[] Requeridos =
    {
        "siteRoot", "elementLayoutSection", "elementSynHeroBanner",
        "elementSynFeatureGrid", "elementCorpMissionBlock",
    };

    private readonly IContentService _content = Substitute.For<IContentService>();
    private readonly IContentTypeService _types = Substitute.For<IContentTypeService>();
    private readonly Dictionary<string, Guid> _keys = new(StringComparer.Ordinal);

    public StarterPortadaSeederTests()
    {
        foreach (var alias in Requeridos) { RegistrarTipo(alias); }
        _content.GetRootContent().Returns(Array.Empty<IContent>());
    }

    private StarterPortadaSeeder Make() => new(
        _content, _types, new SchemaBlockDefaults(_types), NullLogger<StarterPortadaSeeder>.Instance);

    // NSubstitute: los substitutes se construyen ANTES, nunca dentro de otro Returns().
    private void RegistrarTipo(string alias)
    {
        var key = Guid.NewGuid();
        var tipo = Substitute.For<IContentType>();
        tipo.Key.Returns(key);
        tipo.Alias.Returns(alias);
        tipo.PropertyTypes.Returns(Array.Empty<IPropertyType>());
        tipo.CompositionPropertyTypes.Returns(Array.Empty<IPropertyType>());
        _types.Get(alias).Returns(tipo);
        _keys[alias] = key;
    }

    private void SinTipo(string alias) => _types.Get(alias).Returns((IContentType?)null);

    /// <summary>Un nodo de contenido falso, con sus SetValue/GetValue grabados.</summary>
    private static IContent Nodo(string alias, IDictionary<string, string> valores)
    {
        var simple = Substitute.For<ISimpleContentType>();
        simple.Alias.Returns(alias);
        var nodo = Substitute.For<IContent>();
        nodo.ContentType.Returns(simple);
        nodo.Id.Returns(42);
        nodo.Name.Returns("Inicio");
        nodo.HasProperty(Arg.Any<string>()).Returns(true);
        nodo.GetValue<string>(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool>())
            .Returns(ci => valores.TryGetValue(ci.ArgAt<string>(0), out var v) ? v : null);
        nodo.When(n => n.SetValue(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<string?>(), Arg.Any<string?>()))
            .Do(ci => valores[ci.ArgAt<string>(0)] = ci.ArgAt<object?>(1)?.ToString() ?? string.Empty);
        return nodo;
    }

    private IContent ArbolVacio(IDictionary<string, string> valores)
    {
        var creado = Nodo("siteRoot", valores);
        _content.Create(Arg.Any<string>(), Arg.Any<int>(), "siteRoot").Returns(creado);
        PublicaBien(creado);
        return creado;
    }

    private void PublicaBien(IContent nodo) => _content
        .SaveAndPublish(nodo, Arg.Any<string[]>(), Arg.Any<int>())
        .Returns(new PublishResult(PublishResultType.SuccessPublish, null, nodo));

    [Fact] // empty: sin árbol, la portada se crea y se publica
    public void Seed_ArbolVacio_CreaLaPortada()
    {
        var valores = new Dictionary<string, string>(StringComparer.Ordinal);
        var creado = ArbolVacio(valores);

        var r = Make().Seed();

        Assert.True(r.Success);
        Assert.Equal(StarterPortadaSeeder.PortadaOutcome.Created, r.Outcome);
        _content.Received(1).Create("Inicio", Umbraco.Cms.Core.Constants.System.Root, "siteRoot");
        _content.Received(1).SaveAndPublish(creado, Arg.Any<string[]>(), Arg.Any<int>());
        // Las obligatorias del schema: sin ellas Umbraco rechaza el publish y la herramienta
        // contesta «no se pudo» sin que nadie sepa por qué.
        Assert.Equal("Inicio", valores["siteDisplayName"]);
        Assert.Equal("Inicio", valores["brandDisplayName"]);
        Assert.False(string.IsNullOrWhiteSpace(valores["brandKey"]));
        Assert.False(string.IsNullOrWhiteSpace(valores["sections"]));
    }

    [Fact] // happy: el cuerpo son TRES bandas, cada bloque dentro del area sectionContent
    public void BuildPortada_ComponeLasTresBandasEnSuArea()
    {
        var json = JsonDocument.Parse(Make().BuildPortada()).RootElement;

        var secciones = json.GetProperty("layout").GetProperty("Umbraco.BlockGrid");
        Assert.Equal(3, secciones.GetArrayLength());

        var area = LayoutComposerKeysArea();
        foreach (var seccion in secciones.EnumerateArray())
        {
            var areas = seccion.GetProperty("areas");
            Assert.Equal(1, areas.GetArrayLength());
            // La Key del area es lo que Umbraco lee. Una equivocada no da error: guarda el
            // bloque donde nadie lo busca y la página sale vacía.
            Assert.Equal(area, areas[0].GetProperty("key").GetString());
            Assert.Equal(1, areas[0].GetProperty("items").GetArrayLength());
        }

        var tipos = json.GetProperty("contentData").EnumerateArray()
            .Select(b => b.GetProperty("contentTypeKey").GetString()).ToList();
        foreach (var alias in new[]
                 {
                     "elementLayoutSection", "elementSynHeroBanner",
                     "elementSynFeatureGrid", "elementCorpMissionBlock",
                 })
        {
            Assert.Contains(_keys[alias].ToString(), tipos);
        }
    }

    [Fact] // happy: las obligatorias de cada bloque van rellenas — vacías, el publish se cae
    public void BuildPortada_RellenaLasObligatoriasDeCadaBloque()
    {
        var json = JsonDocument.Parse(Make().BuildPortada()).RootElement;
        var bloques = json.GetProperty("contentData").EnumerateArray().ToList();

        string Valor(string alias, string prop) => bloques
            .First(b => b.GetProperty("contentTypeKey").GetString() == _keys[alias].ToString())
            .GetProperty(prop).GetString()!;

        Assert.False(string.IsNullOrWhiteSpace(Valor("elementSynHeroBanner", "title")));
        Assert.False(string.IsNullOrWhiteSpace(Valor("elementCorpMissionBlock", "headingTitle")));
        Assert.False(string.IsNullOrWhiteSpace(Valor("elementCorpMissionBlock", "mediaAlt")));

        // itemsJson es obligatoria Y es texto libre: si no parsea, el fallback SSR no pinta
        // nada y la banda sale en blanco sin error.
        var items = JsonDocument.Parse(Valor("elementSynFeatureGrid", "itemsJson")).RootElement;
        Assert.Equal(3, items.GetArrayLength());
        foreach (var item in items.EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("heading").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("body").GetString()));
        }
    }

    [Fact] // idempotent: con portada puesta NO se publica nada — pisarla es el daño real
    public void Seed_ConPortadaPuesta_NoTocaNada()
    {
        var valores = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sections"] = "{\"layout\":{},\"contentData\":[{\"contentTypeKey\":\"x\"}]}",
            ["siteDisplayName"] = "Mi marca",
        };
        var existente = Nodo("siteRoot", valores);
        _content.GetRootContent().Returns(new[] { existente });

        var r = Make().Seed();

        Assert.True(r.Success);
        Assert.Equal(StarterPortadaSeeder.PortadaOutcome.AlreadyAuthored, r.Outcome);
        _content.DidNotReceive().SaveAndPublish(Arg.Any<IContent>(), Arg.Any<string[]>(), Arg.Any<int>());
        _content.DidNotReceive().Create(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>());
        Assert.Equal("Mi marca", valores["siteDisplayName"]);
    }

    [Fact] // filter: un siteRoot sin cuerpo se completa, y su marca NO se pisa
    public void Seed_SiteRootSinCuerpo_LoCompletaSinPisarLaMarca()
    {
        var valores = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["siteDisplayName"] = "Mi marca",
            ["brandKey"] = "miMarca",
        };
        var existente = Nodo("siteRoot", valores);
        _content.GetRootContent().Returns(new[] { existente });
        PublicaBien(existente);

        var r = Make().Seed();

        Assert.True(r.Success);
        Assert.Equal(StarterPortadaSeeder.PortadaOutcome.Filled, r.Outcome);
        _content.DidNotReceive().Create(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>());
        Assert.Equal("Mi marca", valores["siteDisplayName"]);
        Assert.Equal("miMarca", valores["brandKey"]);
        Assert.False(string.IsNullOrWhiteSpace(valores["sections"]));
    }

    [Fact] // filter: con la raíz ocupada NO se siembra — un siteRoot al lado no sale en /
    public void Seed_ConLaRaizOcupada_NoSiembraYLoDice()
    {
        // El andamio de la vitrina pone un platformRoot en la raíz. Umbraco resuelve `/` al
        // primer raíz, así que un siteRoot creado al lado quedaría en /inicio: la herramienta
        // habría contestado «creada» y el sitio seguiría enseñando otra cosa. Medido en vivo
        // antes de esta guarda — contestó Created con la portada invisible (#119).
        var platform = Nodo("platformRoot", new Dictionary<string, string>(StringComparer.Ordinal));
        _content.GetRootContent().Returns(new[] { platform });
        var valores = new Dictionary<string, string>(StringComparer.Ordinal);
        ArbolVacio(valores);

        var r = Make().Seed();

        Assert.Equal(StarterPortadaSeeder.PortadaOutcome.RootAlreadyTaken, r.Outcome);
        Assert.Contains("platformRoot", r.Detail, StringComparison.Ordinal);
        _content.DidNotReceive().Create(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>());
        _content.DidNotReceive().SaveAndPublish(Arg.Any<IContent>(), Arg.Any<string[]>(), Arg.Any<int>());
    }

    [Fact] // sin schema importado se DICE cuál falta, en vez de un OK vacío
    public void Seed_SinSchema_NoSiembraYNombraLoQueFalta()
    {
        SinTipo("elementSynFeatureGrid");

        var r = Make().Seed();

        Assert.False(r.Success);
        Assert.Equal(StarterPortadaSeeder.PortadaOutcome.MissingContentTypes, r.Outcome);
        Assert.Contains("elementSynFeatureGrid", r.Detail, StringComparison.Ordinal);
        _content.DidNotReceive().Create(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>());
    }

    [Fact] // un publish rechazado NO se reporta como éxito
    public void Seed_PublishRechazado_DevuelveFallo()
    {
        var valores = new Dictionary<string, string>(StringComparer.Ordinal);
        var creado = Nodo("siteRoot", valores);
        _content.Create(Arg.Any<string>(), Arg.Any<int>(), "siteRoot").Returns(creado);
        _content.SaveAndPublish(creado, Arg.Any<string[]>(), Arg.Any<int>())
            .Returns(new PublishResult(PublishResultType.FailedPublishContentInvalid, null, creado));

        var r = Make().Seed();

        Assert.False(r.Success);
        Assert.Equal(StarterPortadaSeeder.PortadaOutcome.SaveFailed, r.Outcome);
    }

    /// <summary>La Key del area <c>sectionContent</c>, leída del DataType y no de una constante.</summary>
    /// <remarks>
    /// El test no puede repetir el GUID: con el mismo número escrito a los dos lados, cambiarlo
    /// en el código cambiaría el test y el gate pasaría en verde. Se lee del schema, que es la
    /// fuente.
    /// </remarks>
    private static string LayoutComposerKeysArea()
    {
        var ruta = Path.Combine(RepoRoot(), "Synergos.CMS.Web", "uSync", "v9",
            "DataTypes", "DTBlockGridSections.config");
        var texto = System.IO.File.ReadAllText(ruta);
        var m = System.Text.RegularExpressions.Regex.Match(
            texto, "\"alias\":\\s*\"sectionContent\"[^}]*?\"key\":\\s*\"([0-9a-fA-F-]{36})\"");
        Assert.True(m.Success, "DTBlockGridSections ya no declara el area sectionContent.");
        return m.Groups[1].Value;
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
