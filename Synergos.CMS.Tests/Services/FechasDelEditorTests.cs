using System.Text.RegularExpressions;
using NSubstitute;
using Synergos.CMS.Web.Services;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// <see cref="FechasDelEditor"/>: la fecha que el editor dejó vacía es <c>null</c>, no el año 1
/// (#185, #188). Y es la ÚNICA lectura de fechas del CMS.
/// </summary>
/// <remarks>
/// <para><b>La ausencia se fabrica como la entrega Umbraco</b>: la propiedad sin valor y su
/// conversor devolviendo <see cref="DateTime.MinValue"/>. Con un doble que devuelve <c>null</c>
/// la lectura cruda <c>Value&lt;DateTime?&gt;</c> también pasa, y el test saldría verde sobre el
/// código que pintaba «1/01/0001» en el timeline.</para>
/// </remarks>
public sealed class FechasDelEditorTests
{
    private static IPublishedElement Elemento(string alias, object? valor, bool tieneValor)
    {
        var propiedad = Substitute.For<IPublishedProperty>();
        propiedad.Alias.Returns(alias);
        propiedad.HasValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(tieneValor);
        propiedad.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(valor);

        var elemento = Substitute.For<IPublishedElement>();
        elemento.GetProperty(alias).Returns(propiedad);
        return elemento;
    }

    private static IPublishedValueFallback SinRespaldo() => Substitute.For<IPublishedValueFallback>();

    [Fact]
    public void Un_date_picker_vacio_es_null_y_no_el_ano_uno()
    {
        // Lo que hace el DatePickerValueConverter de Umbraco 13 con un campo vacío.
        var hito = Elemento("contentDate", DateTime.MinValue, tieneValor: false);

        Assert.Equal(new DateTime(1, 1, 1), hito.Value<DateTime?>(SinRespaldo(), "contentDate"));
        Assert.Null(hito.FechaDelEditor(SinRespaldo(), "contentDate"));
    }

    [Fact]
    public void Una_fecha_puesta_llega_tal_cual()
    {
        var fecha = new DateTime(2026, 3, 15);
        var hito = Elemento("contentDate", fecha, tieneValor: true);

        Assert.Equal(fecha, hito.FechaDelEditor(SinRespaldo(), "contentDate"));
    }

    [Fact]
    public void Un_texto_que_no_es_fecha_es_null()
    {
        // postPage.publishDate es un TextBox: lo que no se puede leer como fecha no se inventa.
        var post = Elemento("publishDate", "2026-13-45", tieneValor: true);

        Assert.Null(post.FechaDelEditor(SinRespaldo(), "publishDate"));
    }

    /// <summary>
    /// Ninguna fecha se lee en crudo fuera de la pieza: ni en C# ni en Razor.
    /// </summary>
    /// <remarks>
    /// Es el «tell» que #185 dejó escrito en CLAUDE.md §5 —<c>Value&lt;DateTime?&gt;</c> seguido de
    /// <c>.HasValue</c>— convertido en gate, y más ancho: también <c>Value&lt;DateTime&gt;</c>, que da el
    /// mismo año 1 y se defendía comparando con <c>default</c> en dos sitios. El árbol lo cumple
    /// entero, así que es un trinquete absoluto, sin línea base.
    /// </remarks>
    [Fact]
    public void Ninguna_fecha_se_lee_en_crudo_fuera_de_la_pieza()
    {
        var web = Path.Combine(RepoRoot(), "Synergos.CMS.Web");
        var pieza = Path.Combine(web, "Services", "FechasDelEditor.cs");
        var crudas = new Regex(@"Value\s*<\s*DateTime\s*\??\s*>");

        var ficheros = new[] { "*.cs", "*.cshtml" }
            .SelectMany(p => Directory.EnumerateFiles(web, p, SearchOption.AllDirectories))
            .Where(f => !EnCarpeta(f, "bin") && !EnCarpeta(f, "obj")
                        && !f.Contains(Path.Combine("umbraco", "models"), StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(ficheros.Count > 300, $"Se miraron {ficheros.Count} ficheros: el descubrimiento está roto.");
        Assert.Contains(pieza, ficheros);

        var fuera = ficheros
            .Where(f => f != pieza)
            .SelectMany(f => File.ReadAllLines(f)
                .Select((linea, i) => (Linea: linea, N: i + 1))
                .Where(x => crudas.IsMatch(x.Linea))
                .Select(x => $"{Path.GetRelativePath(web, f)}:{x.N}: {x.Linea.Trim()}"))
            .ToList();

        Assert.True(fuera.Count == 0,
            "Un campo de fecha vacío llega como el año 1, no como null: se lee con FechaDelEditor "
            + "(Services/FechasDelEditor.cs), o un hito sin fecha pinta «1/01/0001» (#188)."
            + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", fuera));
    }

    private static bool EnCarpeta(string ruta, string carpeta)
        => ruta.Split(Path.DirectorySeparatorChar).Contains(carpeta, StringComparer.OrdinalIgnoreCase);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
