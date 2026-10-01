using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La ventana programada de un aviso global (#185): sus dos puntas son OPCIONALES. Sin inicio
/// rige desde ya; sin fin, no vence.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cubren.</b> Un aviso publicado «hasta nuevo aviso» —sin fecha de
/// fin— no se pintaba nunca: el date picker de Umbraco entrega un campo vacío como
/// <c>0001-01-01</c>, no como <c>null</c>, y el resolver lo comparaba como una fecha real, así
/// que el aviso salía vencido desde el año 1. Medido en vivo sobre una copia de la base: con
/// fin vacío, <c>alerta: null</c>; con fin a 30 días, se pinta.</para>
///
/// <para><b>Las fechas vacías se fabrican COMO LAS ENTREGA UMBRACO</b> —la propiedad sin valor
/// y su conversor devolviendo <see cref="DateTime.MinValue"/>—, no como <c>null</c>. Con un
/// <c>null</c> el defecto no se reproduce y el test pasaría en verde sobre el código roto
/// (comprobado mutando).</para>
///
/// <para>El seam se ejerce por las TRES fuentes del resolver —selector explícito en el
/// siteRoot, repositorio de transversales y BlockList legacy—, que antes copiaban la regla tres
/// veces con el mismo defecto.</para>
/// </remarks>
public sealed class DefaultGlobalComponentResolverTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 1, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Ayer = Ahora.UtcDateTime.AddDays(-1);
    private static readonly DateTime Manana = Ahora.UtcDateTime.AddDays(1);

    /// <summary>Un campo de fecha que el editor dejó vacío.</summary>
    private static readonly object Vacia = new FechaVacia();

    private sealed class FechaVacia;

    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    // ── Cómo entrega Umbraco el contenido ───────────────────────────────────

    private static IPublishedProperty Propiedad(string alias, object? valor)
    {
        var propiedad = Substitute.For<IPublishedProperty>();
        propiedad.Alias.Returns(alias);
        if (valor is FechaVacia)
        {
            // Lo que hace el DatePickerValueConverter de Umbraco 13 con un campo vacío.
            propiedad.HasValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(false);
            propiedad.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(DateTime.MinValue);
        }
        else
        {
            propiedad.HasValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(valor is not null);
            propiedad.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(valor);
        }
        return propiedad;
    }

    private static IPublishedContent Nodo(string tipo, params (string Alias, object? Valor)[] valores)
    {
        var tipoDeContenido = Substitute.For<IPublishedContentType>();
        tipoDeContenido.Alias.Returns(tipo);

        var nodo = Substitute.For<IPublishedContent>();
        nodo.ContentType.Returns(tipoDeContenido);
        nodo.Parent.Returns((IPublishedContent?)null);
        nodo.ChildrenForAllCultures.Returns(Array.Empty<IPublishedContent>());
        foreach (var (alias, valor) in valores)
        {
            var propiedad = Propiedad(alias, valor);
            nodo.GetProperty(alias).Returns(propiedad);
        }
        return nodo;
    }

    private static IPublishedElement Elemento(string tipo, params (string Alias, object? Valor)[] valores)
    {
        var tipoDeContenido = Substitute.For<IPublishedContentType>();
        tipoDeContenido.Alias.Returns(tipo);

        var elemento = Substitute.For<IPublishedElement>();
        elemento.ContentType.Returns(tipoDeContenido);
        foreach (var (alias, valor) in valores)
        {
            var propiedad = Propiedad(alias, valor);
            elemento.GetProperty(alias).Returns(propiedad);
        }
        return elemento;
    }

    /// <summary>
    /// El resolver con la página <paramref name="pagina"/> en curso y <paramref name="raices"/>
    /// en la raíz del caché publicado.
    /// </summary>
    private static DefaultGlobalComponentResolver Resolver(IPublishedContent pagina, params IPublishedContent[] raices)
    {
        var cache = Substitute.For<IPublishedContentCache>();
        cache.GetAtRoot(Arg.Any<string?>()).Returns(raices);

        var peticion = Substitute.For<IPublishedRequest>();
        peticion.PublishedContent.Returns(pagina);

        var contexto = Substitute.For<IUmbracoContext>();
        contexto.Content.Returns(cache);
        contexto.PublishedRequest.Returns(peticion);

        var accesor = Substitute.For<IUmbracoContextAccessor>();
        accesor.TryGetUmbracoContext(out Arg.Any<IUmbracoContext?>())
            .Returns(llamada =>
            {
                llamada[0] = contexto;
                return true;
            });

        return new DefaultGlobalComponentResolver(
            accesor,
            Substitute.For<IVariationContextAccessor>(),
            new NoopPublishedValueFallback(),
            new RelojFijo());
    }

    /// <summary>Una página cualquiera, sin siteRoot por encima: el selector explícito no aplica.</summary>
    private static IPublishedContent Pagina() => Nodo("pageBase");

    /// <summary>Un aviso del repositorio (Prioridad 2), activo y con mensaje.</summary>
    private static IPublishedContent AlertaDelRepositorio(object? inicio, object? fin, bool activa = true)
        => Nodo("transversalAlert",
            ("alertActive", activa),
            ("alertMessage", "Hasta nuevo aviso"),
            ("alertScheduleStart", inicio),
            ("alertScheduleEnd", fin));

    private static CfgAlert? AlertaVigente(object? inicio, object? fin)
        => Resolver(Pagina(), AlertaDelRepositorio(inicio, fin)).GetActiveAlert();

    // ── Las cuatro combinaciones ────────────────────────────────────────────

    /// <summary>EL CASO DE HOY: sin inicio y sin fin es «desde ya y hasta nuevo aviso».</summary>
    [Fact]
    public void Sin_inicio_ni_fin_el_aviso_se_pinta()
    {
        var alerta = AlertaVigente(Vacia, Vacia);

        Assert.NotNull(alerta);
        Assert.Equal("Hasta nuevo aviso", alerta!.Message);
    }

    [Fact]
    public void Solo_con_inicio_rige_desde_el_inicio_y_no_vence()
    {
        Assert.NotNull(AlertaVigente(Ayer, Vacia));
        Assert.Null(AlertaVigente(Manana, Vacia));
    }

    [Fact]
    public void Solo_con_fin_rige_desde_ya_hasta_el_fin()
    {
        Assert.NotNull(AlertaVigente(Vacia, Manana));
        Assert.Null(AlertaVigente(Vacia, Ayer));
    }

    [Fact]
    public void Con_las_dos_fechas_rige_entre_ellas()
    {
        Assert.NotNull(AlertaVigente(Ayer, Manana));
        Assert.Null(AlertaVigente(Manana, Manana.AddDays(1)));
        Assert.Null(AlertaVigente(Ayer.AddDays(-1), Ayer));
    }

    /// <summary>
    /// Una fecha que de verdad no llegó (<c>null</c>, otro conversor) vale lo mismo que una vacía:
    /// no es un límite.
    /// </summary>
    [Fact]
    public void Una_fecha_nula_tampoco_es_un_limite()
    {
        Assert.NotNull(AlertaVigente(null, null));
    }

    // ── Lo que la ventana NO cambia ─────────────────────────────────────────

    [Fact]
    public void Un_aviso_inactivo_no_se_pinta_aunque_no_tenga_fechas()
    {
        var resolver = Resolver(Pagina(), AlertaDelRepositorio(Vacia, Vacia, activa: false));

        Assert.Null(resolver.GetActiveAlert());
    }

    [Fact]
    public void Sin_avisos_publicados_no_hay_aviso()
    {
        Assert.Null(Resolver(Pagina()).GetActiveAlert());
    }

    [Fact]
    public void Pedirlo_dos_veces_da_lo_mismo()
    {
        var resolver = Resolver(Pagina(), AlertaDelRepositorio(Vacia, Vacia));

        Assert.Equal(resolver.GetActiveAlert(), resolver.GetActiveAlert());
    }

    // ── Las tres fuentes, y los cuatro tipos ────────────────────────────────

    /// <summary>Prioridad 1: el siteRoot apunta al aviso con su selector explícito.</summary>
    [Fact]
    public void Por_el_selector_explicito_un_aviso_sin_fin_se_pinta()
    {
        var aviso = AlertaDelRepositorio(Vacia, Vacia);
        var siteRoot = Nodo("siteRoot", ("activeAlertNode", aviso));

        // La página en curso ES el siteRoot, y no hay nada más publicado: si sale, salió por
        // el selector.
        var alerta = Resolver(siteRoot).GetActiveAlert();

        Assert.NotNull(alerta);
    }

    /// <summary>Prioridad 3: el BlockList <c>globalComponents</c> del primer siteConfigSettings.</summary>
    [Fact]
    public void Por_el_BlockList_legacy_un_aviso_sin_fin_se_pinta()
    {
        var bloque = Elemento("cfgAlert",
            ("alertActive", true),
            ("alertMessage", "Del BlockList"),
            ("alertScheduleStart", Vacia),
            ("alertScheduleEnd", Vacia));
        var lista = new BlockListModel(new List<BlockListItem>
        {
            new(Udi.Create(Constants.UdiEntityType.Element, Guid.NewGuid()), bloque, null!, null!),
        });
        var configuracion = Nodo("siteConfigSettings", ("globalComponents", lista));

        var alerta = Resolver(Pagina(), configuracion).GetActiveAlert();

        Assert.Equal("Del BlockList", alerta?.Message);
    }

    [Fact]
    public void El_banner_el_modal_y_la_nota_del_pie_tampoco_vencen_sin_fin()
    {
        var banner = Nodo("transversalBanner",
            ("bannerActive", true), ("bannerMessage", "banner"),
            ("bannerScheduleStart", Vacia), ("bannerScheduleEnd", Vacia));
        var modal = Nodo("transversalModal",
            ("modalActive", true), ("modalTitle", "modal"),
            ("modalScheduleStart", Vacia), ("modalScheduleEnd", Vacia));
        var nota = Nodo("transversalFooterNote",
            ("footerNoteActive", true), ("footerNoteText", "nota"),
            ("footerNoteScheduleStart", Vacia), ("footerNoteScheduleEnd", Vacia));

        var resolver = Resolver(Pagina(), banner, modal, nota);

        Assert.Equal("banner", resolver.GetActiveBanner()?.Message);
        Assert.Equal("modal", resolver.GetActiveModal()?.Title);
        Assert.Equal("nota", resolver.GetActiveFooterNote()?.Text);
    }
}
