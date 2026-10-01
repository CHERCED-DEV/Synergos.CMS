using System.Globalization;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Diccionario;

namespace Synergos.CMS.Tests.Services.Diccionario;

/// <summary>
/// Cubre <see cref="SeccionesDeLaPagina"/> y <see cref="EmisorQueAnotaElDiccionario"/>: la página
/// junta las secciones que piden los elementos que se EMITEN, y el bridge publica su unión una vez
/// (ADR 0136 §1).
/// </summary>
public sealed class SeccionesDeLaPaginaTests
{
    private static SeccionesDeLaPagina Con(HttpContext? contexto)
        => new(new HttpContextAccessor { HttpContext = contexto });

    private static SynHostEmitRequest Solicitud(string bloque, params string[] secciones)
        => new(bloque, null, null, CultureInfo.GetCultureInfo("es-CO"), Diccionario: secciones);

    [Fact]
    public void Sin_peticion_no_anota_ni_lanza()
    {
        var secciones = Con(null);

        secciones.Anotar(new[] { "Slider" });

        Assert.Empty(secciones.Declaradas);
    }

    [Fact]
    public void Una_pagina_sin_elementos_con_secciones_no_declara_ninguna()
    {
        Assert.Empty(Con(new DefaultHttpContext()).Declaradas);
    }

    [Fact]
    public void La_pagina_publica_la_union_de_lo_que_piden_sus_elementos_sin_repetir()
    {
        var http = new DefaultHttpContext();

        Con(http).Anotar(new[] { "Slider", "Common.States" });
        Con(http).Anotar(new[] { "common.states", "Rating", " " });

        // Otra instancia, la misma petición: la unión vive en la petición, no en el servicio.
        Assert.Equal(new[] { "Common.States", "Rating", "Slider" }, Con(http).Declaradas);
    }

    [Fact]
    public void Dos_peticiones_no_se_mezclan()
    {
        var una = new DefaultHttpContext();
        var otra = new DefaultHttpContext();

        Con(una).Anotar(new[] { "Slider" });

        Assert.Empty(Con(otra).Declaradas);
    }

    [Fact]
    public async Task El_emitter_de_la_pagina_anota_lo_que_declara_cada_elemento_y_delega()
    {
        var http = new DefaultHttpContext();
        var esperado = new SynHostEmitResult("<script></script>", "<synergos-carousel></synergos-carousel>", true);
        var interno = Substitute.For<ISynHostEmitter>();
        interno.EmitAsync(Arg.Any<SynHostEmitRequest>(), Arg.Any<CancellationToken>()).Returns(esperado);
        var emisor = new EmisorQueAnotaElDiccionario(interno, Con(http));

        var resultado = await emisor.EmitAsync(Solicitud("carousel", "Slider"));
        await emisor.EmitAsync(new SynHostEmitRequest("badge", null, null, CultureInfo.InvariantCulture));
        await emisor.EmitAsync(Solicitud("rating-stars", "Rating"));

        Assert.Same(esperado, resultado);
        Assert.Equal(new[] { "Rating", "Slider" }, Con(http).Declaradas);
        await interno.Received(3).EmitAsync(Arg.Any<SynHostEmitRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, new string[0])]
    [InlineData("", new string[0])]
    [InlineData("Slider,Rating", new[] { "Slider", "Rating" })]
    [InlineData(" Common.States , slider,Slider", new[] { "Common.States", "slider" })]
    [InlineData("<script>,Slider.,.Slider,Sli der,9Slider,Synhost.Kpi", new[] { "Synhost.Kpi" })]
    public void La_consulta_del_modo_CSP_solo_deja_pasar_secciones(string? consulta, string[] esperado)
    {
        Assert.Equal(esperado, SeccionesDeLaPagina.DeLaConsulta(consulta));
    }

    [Fact]
    public void La_consulta_tiene_tope_y_ida_y_vuelta_es_la_identidad()
    {
        var muchas = Enumerable.Range(0, 200).Select(i => $"S{i}").ToList();

        Assert.Equal(SeccionesDeLaPagina.MaximoEnLaConsulta, SeccionesDeLaPagina.DeLaConsulta(string.Join(',', muchas)).Count);
        Assert.Equal(
            new[] { "Common.States", "Slider" },
            SeccionesDeLaPagina.DeLaConsulta(SeccionesDeLaPagina.ALaConsulta(new[] { "Common.States", "Slider" })));
    }
}
