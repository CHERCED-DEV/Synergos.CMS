using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Un inmueble no vive en dos nodos sin control (016, D-016-C22): la ficha (<c>productPage</c>) y el inmueble que lee la
/// app (<c>propertyListing</c>) salen de la misma fila de <see cref="DevContenidoDeVerticales.InmueblesDelCms"/>, y lo
/// que el repo versiona (uSync) dice lo mismo en los dos lados: precio, operación, área, habitaciones, baños y estrato.
/// Si un inmueble está en un lado y no en el otro, la prueba falla.
/// </summary>
public sealed class InventarioDePropiedadesTests
{
    private sealed record Inmueble(string Slug, decimal Precio, string Operacion, int Area, int Habitaciones, int Banos, int Estrato);

    private static readonly IReadOnlyList<(string Fichero, string Xml)> Contenido = LeerContenido();

    private static IReadOnlyList<(string, string)> LeerContenido()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln"))) { dir = dir.Parent; }
        var carpeta = Path.Combine(dir!.FullName, "Synergos.CMS.Web", "uSync", "v9", "Content");
        return Directory.GetFiles(carpeta, "*.config").Select(f => (Path.GetFileName(f), File.ReadAllText(f))).ToList();
    }

    private static string? Valor(string xml, string alias)
    {
        var m = Regex.Match(xml, "<" + alias + @">\s*<Value[^>]*><!\[CDATA\[(.*?)\]\]></Value>", RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static int Entero(string xml, string alias)
        => int.TryParse(Valor(xml, alias), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static decimal Decimal(string? texto)
        => decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : -1;

    private static Dictionary<string, Inmueble> InventarioDelRepo()
        => Contenido.Where(c => c.Xml.Contains("<ContentType>propertyListing</ContentType>", StringComparison.Ordinal))
            .Select(c => new Inmueble(
                Valor(c.Xml, "propertySlug") ?? string.Empty,
                Decimal(Valor(c.Xml, "propertyPrice")),
                JsonNode.Parse(Valor(c.Xml, "propertyOperation") ?? "[]")!.AsArray().FirstOrDefault()?.GetValue<string>() ?? string.Empty,
                Entero(c.Xml, "propertyAreaM2"), Entero(c.Xml, "propertyBeds"), Entero(c.Xml, "propertyBaths"), Entero(c.Xml, "propertyStratum")))
            .ToDictionary(i => i.Slug, StringComparer.Ordinal);

    private static Dictionary<Guid, string> FichasDePropiedades()
        => Contenido.Where(c => c.Xml.Contains("<ContentType>productPage</ContentType>", StringComparison.Ordinal)
                                && c.Xml.Contains("<Path>/SynergosLabs/Propiedades/", StringComparison.Ordinal))
            .ToDictionary(c => Guid.Parse(Regex.Match(c.Xml, "<Content Key=\"([^\"]+)\"").Groups[1].Value), c => c.Xml);

    [Fact]
    public void Cada_inmueble_de_la_tabla_esta_en_el_repo_y_el_repo_no_tiene_otros()
    {
        var tabla = DevContenidoDeVerticales.InmueblesDelCms.Select(i => i.Slug).ToHashSet(StringComparer.Ordinal);
        var repo = InventarioDelRepo().Keys.ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(tabla);
        Assert.True(tabla.SetEquals(repo),
            $"En la tabla y no en el repo: {string.Join(", ", tabla.Except(repo))}. En el repo y no en la tabla: {string.Join(", ", repo.Except(tabla))}.");
    }

    [Fact]
    public void Cada_ficha_de_Propiedades_es_de_un_inmueble_de_la_tabla_y_al_reves()
    {
        var fichas = FichasDePropiedades().Keys.ToHashSet();
        var tabla = DevContenidoDeVerticales.InmueblesDelCms.Where(i => i.Ficha is not null).Select(i => i.Ficha!.Value).ToHashSet();

        Assert.NotEmpty(tabla);
        Assert.True(fichas.SetEquals(tabla),
            $"Fichas sin inmueble: {string.Join(", ", fichas.Except(tabla))}. Inmuebles con ficha que no está: {string.Join(", ", tabla.Except(fichas))}.");
    }

    [Fact]
    public void El_inmueble_del_repo_dice_lo_mismo_que_su_fila()
    {
        var repo = InventarioDelRepo();
        foreach (var i in DevContenidoDeVerticales.InmueblesDelCms)
        {
            Assert.Equal(new Inmueble(i.Slug, i.Precio, i.Operacion, i.AreaM2, i.Habitaciones, i.Banos, i.Estrato), repo[i.Slug]);
        }
    }

    [Fact]
    public void La_ficha_dice_lo_mismo_que_su_inmueble_y_lleva_a_el_en_la_app()
    {
        var fichas = FichasDePropiedades();
        foreach (var i in DevContenidoDeVerticales.InmueblesDelCms.Where(x => x.Ficha is not null))
        {
            var xml = fichas[i.Ficha!.Value];
            Assert.Equal(i.Precio, Decimal(Valor(xml, "productPriceBase")));

            var cuerpo = JsonNode.Parse(Valor(xml, "sections")!)!;
            var rejilla = cuerpo["contentData"]!.AsArray().OfType<JsonObject>()
                .Single(b => b["headingText"]?.GetValue<string>() == "Ficha técnica");
            var crudo = rejilla["itemsJson"]!;
            var items = (crudo is JsonValue v && v.TryGetValue<string>(out var t) ? JsonNode.Parse(t)! : crudo).AsArray().OfType<JsonObject>()
                .ToDictionary(o => o["heading"]!.GetValue<string>(), o => o["body"]!.GetValue<string>());

            foreach (var (rotulo, texto, numero) in DevContenidoDeVerticales.DatosDeLaFicha(i))
            {
                Assert.True(items.ContainsKey(rotulo), $"La ficha de {i.Slug} no dice «{rotulo}».");
                if (numero is null) { Assert.Equal(texto, items[rotulo]); }
                else { Assert.Equal(numero, DevContenidoDeVerticales.NumeroDe(items[rotulo])); }
            }
            foreach (var cero in new[] { "Habitaciones", "Baños", "Parqueaderos", "Estrato" }.Except(DevContenidoDeVerticales.DatosDeLaFicha(i).Select(d => d.Rotulo)))
            {
                Assert.False(items.ContainsKey(cero) && DevContenidoDeVerticales.NumeroDe(items[cero]) is > 0,
                    $"La ficha de {i.Slug} dice «{cero}: {(items.TryGetValue(cero, out var x) ? x : "")}» y su inmueble no tiene.");
            }

            Assert.Contains(DevContenidoDeVerticales.FichaEnLaApp(i), xml, StringComparison.Ordinal);
            Assert.DoesNotContain("/propiedades/propiedad#contacto", xml, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("240 m² en dos niveles", 240)]
    [InlineData(" 3", 3)]
    [InlineData("4 y estudio", 4)]
    [InlineData("Privado", null)]
    [InlineData(null, null)]
    public void El_numero_de_un_dato_es_el_que_lo_abre(string? texto, int? numero)
    {
        Assert.Equal(numero, DevContenidoDeVerticales.NumeroDe(texto));
    }
}
