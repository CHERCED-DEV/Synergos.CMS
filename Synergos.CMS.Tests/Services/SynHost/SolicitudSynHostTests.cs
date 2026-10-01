using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using NSubstitute;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="SolicitudSynHost"/>: el record entra al emitter de siempre y sale el mismo
/// cable —<c>&lt;synergos-x config='{json}'&gt;</c>— con las claves del record.
/// </summary>
public sealed class SolicitudSynHostTests
{
    private static readonly CultureInfo EsCo = CultureInfo.GetCultureInfo("es-CO");

    private static ElementoResuelto<KpiCardProps> Kpi(string? respaldo = null)
        => new(new KpiCardProps("Ventas", "1.234", "up", null, null), respaldo);

    /// <summary>El JSON del atributo <c>config</c> tal como lo lee el navegador.</summary>
    internal static JsonElement ConfigEmitido(string elementHtml)
    {
        var atributo = Regex.Match(elementHtml, "config='([^']*)'");
        Assert.True(atributo.Success, $"El tag no trae config='…': {elementHtml}");
        return JsonDocument.Parse(WebUtility.HtmlDecode(atributo.Groups[1].Value)).RootElement.Clone();
    }

    [Fact]
    public void El_alias_del_bloque_es_el_nombre_del_registry_que_declara_el_record()
    {
        var solicitud = SolicitudSynHost.Para(Kpi(), null, EsCo);

        Assert.Equal("kpi-card", solicitud.BlockAlias);
        Assert.Same(EsCo, solicitud.Culture);
    }

    [Fact]
    public void Los_props_son_las_claves_del_record_en_camelCase_y_sin_nulos()
    {
        var solicitud = SolicitudSynHost.Para(Kpi(), null, EsCo);

        Assert.Equal(new[] { "label", "value", "trend" }, solicitud.Props!.Keys);
    }

    [Fact]
    public void El_respaldo_SSR_pasa_tal_cual_al_emitter()
    {
        Assert.Equal("<p>ssr</p>", SolicitudSynHost.Para(Kpi("<p>ssr</p>"), null, EsCo).FallbackHtml);
        Assert.Null(SolicitudSynHost.Para(Kpi(), null, EsCo).FallbackHtml);
    }

    /// <summary>
    /// Las secciones de diccionario que declara el record viajan en la solicitud —para que la
    /// página las junte y el bridge las publique (ADR 0136)— y no en el <c>config</c> del tag.
    /// </summary>
    [Fact]
    public void Las_secciones_que_declara_el_record_viajan_en_la_solicitud_y_no_en_el_tag()
    {
        var solicitud = SolicitudSynHost.Para(Kpi(), null, EsCo);

        Assert.Equal(new[] { "Synhost.Kpi" }, solicitud.Diccionario);
        Assert.DoesNotContain(solicitud.Props!.Keys, k => k.Contains("iccionario", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Los_datos_estructurados_pasan_tal_cual_al_emitter()
    {
        var conLd = new ElementoResuelto<KpiCardProps>(new KpiCardProps("Ventas", null, null, null, null), DatosEstructurados: "{}");

        Assert.Equal("{}", SolicitudSynHost.Para(conLd, null, EsCo).StructuredDataJson);
        Assert.Null(SolicitudSynHost.Para(Kpi(), null, EsCo).StructuredDataJson);
    }

    [Fact]
    public void El_override_del_editor_solo_puede_pisar_campos_que_el_record_declara()
    {
        var solicitud = SolicitudSynHost.Para(Kpi(), """{"label":"Pisado","sparkline":[1,2],"kpiLabel":"x"}""", EsCo);

        Assert.Equal("""{"label":"Pisado"}""", solicitud.ConfigOverrideJson);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("{no es json")]
    [InlineData("[1,2]")]
    [InlineData("""{"sparkline":[1]}""")]
    public void Un_override_vacio_roto_o_sin_campos_declarados_no_viaja(string? configOverride)
    {
        Assert.Null(SolicitudSynHost.Para(Kpi(), configOverride, EsCo).ConfigOverrideJson);
    }

    [Fact]
    public void Un_tipo_sin_ElementoSynHost_no_sabe_a_que_elemento_va()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SolicitudSynHost.Elemento(typeof(string)));
        Assert.Contains("ElementoSynHost", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// El cable NO cambia (ADR 0135 §7): el emitter de siempre, con su <c>culture</c> delante y el
    /// override encima, escribe las claves del record.
    /// </summary>
    [Fact]
    public async Task Por_el_emitter_de_siempre_sale_el_mismo_tag_con_las_claves_del_record()
    {
        var emitter = new DefaultSynHostEmitter(Substitute.For<IBundleRegistryClient>());

        var resultado = await emitter.EmitAsync(
            SolicitudSynHost.Para(Kpi(), """{"value":"Pisado","culture":"xx"}""", EsCo));

        Assert.StartsWith("<synergos-kpi-card ", resultado.ElementHtml, StringComparison.Ordinal);
        var config = ConfigEmitido(resultado.ElementHtml);
        Assert.Equal(
            new[] { "culture", "label", "value", "trend" },
            config.EnumerateObject().Select(p => p.Name));
        Assert.Equal("es-CO", config.GetProperty("culture").GetString());
        Assert.Equal("Pisado", config.GetProperty("value").GetString());
    }
}
