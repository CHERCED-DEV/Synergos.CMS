using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El EJE 1 de Social: los posts salen del contenido del CMS, y lo hacen sin romper las dos
/// propiedades que el resto del vertical ya tenía (#146).
/// </summary>
/// <remarks>
/// <para><b>Qué medía el ticket, y en qué falló su predicción.</b> La cabecera del spec daba
/// Social por «un sub-spec y tres ajustes» mirando que no hubiera un
/// <c>Synergos:Catalog:Sources:Social</c>. Al medir aparecieron DOS lectores de «un post»:
/// <c>DefaultBlogQuery</c>, que lee <c>postPage</c> desde siempre y sirve las vistas Razor, y
/// <c>IContentStream</c>, que sirve la app social entera y trae <b>cinco posts cableados en
/// C#</b>. El eje 1 no estaba donde la tabla lo buscaba.</para>
///
/// <para><b>Y el almacén del eje 1 de Social no es una colección propia</b>, que es lo que lo
/// distingue de los otros siete: el <c>IContentStream</c> lo comparte Educación por su
/// <c>Kind</c>, y el producto <b>escribe</b> en él (<c>BlogsController.CreateAsync</c>). Por eso
/// se SIEMBRA en vez de reemplazar: las otras dos salidas obligan a contestar qué pasa con lo
/// que alguien publicó desde la app, y ésa es una pregunta de producto.</para>
/// </remarks>
public sealed class SocialWiringTests
{
    private const string Fuente = "UmbracoSocialContentSource.cs";
    private const string Sembrador = "CatalogContentStream.cs";

    private static string Dir(params string[] partes) => Proyectos.Ruta(partes);

    /// <summary>El fichero SIN comentarios — los `&lt;remarks&gt;` citan las formas prohibidas.</summary>
    private static string SinComentarios(string ruta)
        => string.Join('\n', File.ReadAllLines(ruta).Select(l =>
        {
            var t = l.TrimStart();
            if (t.StartsWith("//", StringComparison.Ordinal)
                || t.StartsWith('*')
                || t.StartsWith("/*", StringComparison.Ordinal))
            {
                return string.Empty;
            }
            var i = l.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? l[..i] : l;
        }));

    private static string FuenteDe(string fichero, params string[] carpeta)
    {
        var ruta = Path.Combine(Dir(carpeta), fichero);
        Assert.True(File.Exists(ruta), $"No existe {fichero}: revisar este gate.");
        return SinComentarios(ruta);
    }

    [Fact]
    public void El_catalogo_de_Social_NO_sale_a_la_red()
    {
        // Eje 1 del molde (doc 12 §3): lo que se MUESTRA sale del contenido de Umbraco, y
        // cablearlo a una capacidad sería un RETROCESO — el dato ya tiene dueño, y meter una ida
        // a la red le quita además al editor la superficie donde publica.
        var fuente = FuenteDe(Fuente, "Synergos.CMS.Web", "Services", "Catalog");

        Assert.DoesNotContain("HttpClient", fuente, StringComparison.Ordinal);
        Assert.DoesNotContain("IHttpClientFactory", fuente, StringComparison.Ordinal);
    }

    [Fact]
    public void La_fuente_de_Social_NO_siembra()
    {
        // Es el defecto del #100 y por eso se exige aunque hoy no haya nada que sembrar EN la
        // fuente: `ICatalogSource.GetAllAsync` se llama en CADA lectura del feed, así que sembrar
        // acá crecería el feed un item por post y por vuelta. Quien siembra es el decorador, con
        // huella y mapping durable.
        var fuente = FuenteDe(Fuente, "Synergos.CMS.Web", "Services", "Catalog");

        Assert.DoesNotContain("IContentStream", fuente, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateAsync", fuente, StringComparison.Ordinal);
    }

    [Fact]
    public void Social_NO_declara_interruptor_de_transaccion()
    {
        // `deshacer: no` — publicar un post no compone nada que haya que deshacer, así que Social
        // no tiene eje 2 y no le corresponde un `Synergos:Social:Mode`. El día que alguien le
        // ponga uno —el paywall de la épica #11 sí tendría algo que deshacer— que sea una
        // decisión y no un arrastre del copiar-pegar: este gate obliga a borrar esta línea.
        var composer = SinComentarios(
            Path.Combine(Dir("Synergos.CMS.Web", "Composers"), "SeamComposer.Social.cs"));

        Assert.False(
            Regex.IsMatch(composer, @"Synergos:Social:Mode|Synergos__Social__Mode"),
            "SeamComposer.Social declara un interruptor de transacción. Social contestó "
            + "«¿hay algo que deshacer?» con NO (doc 12 §4): publicar un post no compone una "
            + "saga. Si eso cambió —el paywall de la épica #11 sí tendría eje 2— el molde pide "
            + "contestar las tres preguntas otra vez, no heredar el interruptor (#146).");
    }

    [Fact]
    public void El_seed_de_demo_sigue_siendo_el_default()
    {
        // Un clon limpio tiene que servir feed sin que nadie haya autorado un solo postPage. El
        // modo `cms` es OPT-IN: se entra por IsCmsSource, que devuelve demo cuando la clave no
        // está, y el rollback es esa misma línea.
        var composer = SinComentarios(
            Path.Combine(Dir("Synergos.CMS.Web", "Composers"), "SeamComposer.Social.cs"));

        Assert.Contains("StubContentStream", composer, StringComparison.Ordinal);
        Assert.Contains("IsCmsSource(sp, UmbracoSocialContentSource.Vertical)", composer, StringComparison.Ordinal);
    }

    /// <summary>
    /// Lo que se publica DESDE la app no pasa por el sembrador.
    /// </summary>
    /// <remarks>
    /// <b>Es la propiedad que hizo elegir «sembrar» entre las tres salidas</b>, así que es la que
    /// hay que vigilar: un decorador que interceptara <c>CreateAsync</c> —para «normalizar», para
    /// «marcar el origen»— reabriría la pregunta que sembrar evita, y lo haría en silencio.
    /// </remarks>
    [Fact]
    public void Publicar_desde_la_app_se_DELEGA_tal_cual()
    {
        var sembrador = FuenteDe(Sembrador, "Synergos.CMS.Application", "Services", "Impl");

        var cuerpo = Regex.Match(
            sembrador,
            @"public Task<ContentStreamItem> CreateAsync\([^)]*\)(.*?);",
            RegexOptions.Singleline);

        Assert.True(cuerpo.Success, "No se encontró CreateAsync en " + Sembrador + ": revisar este gate.");
        Assert.Contains("_inner.CreateAsync(item, cancellationToken)", cuerpo.Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// La siembra lleva HUELLA del contenido, o editar un post no se ve nunca.
    /// </summary>
    /// <remarks>
    /// <see cref="Synergos.CMS.Interfaces.IContentStream"/> sólo sabe CREAR. Un mapping por slug
    /// a secas serviría para siempre el primer texto, y eso no falla: el editor corrige, guarda,
    /// y el feed sigue mostrando la versión vieja
    /// (<c>feedback_seeded_content_needs_fingerprint</c>).
    /// </remarks>
    [Fact]
    public void La_siembra_lleva_huella_y_mapping_durable()
    {
        var sembrador = FuenteDe(Sembrador, "Synergos.CMS.Application", "Services", "Impl");

        Assert.Contains("Fingerprint", sembrador, StringComparison.Ordinal);
        Assert.DoesNotContain("GetHashCode", sembrador, StringComparison.Ordinal);
        Assert.Contains("_store.WriteAsync", sembrador, StringComparison.Ordinal);
    }
}
