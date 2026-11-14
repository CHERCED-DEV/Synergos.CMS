using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La mensajería común guarda cifrado (003-salud-cita, Q12, en la librería): lo que queda en el almacén no es el mensaje,
/// lo de antes del cifrado se sigue leyendo, y lo que no se descifra no se devuelve.
/// </summary>
public sealed class AlmacenCifradoTests
{
    private readonly InMemoryJsonEntityStore _disco = new();
    private readonly AlmacenCifrado _cifrado;

    public AlmacenCifradoTests()
        => _cifrado = new AlmacenCifrado(_disco, new EphemeralDataProtectionProvider(), AlmacenCifrado.PropositoDeLaMensajeria,
            NullLogger<AlmacenCifrado>.Instance);

    [Fact]
    public async Task Un_hilo_de_la_mensajeria_queda_cifrado_en_el_almacen_y_se_lee_entero()
    {
        using var mensajeria = new StubMessagingService(null, _cifrado, StubMessagingService.DefaultResourceType);

        var hilo = await mensajeria.StartThreadAsync("salud.consulta:dra-1", "salud.paciente:p1", "salud.profesional:dra-1",
            "Me duele la cabeza desde el martes");
        var crudos = await _disco.ListAsync(StubMessagingService.DefaultResourceType);
        var leido = await mensajeria.GetThreadAsync(hilo.ThreadId);

        var crudo = Assert.Single(crudos);
        Assert.DoesNotContain("cabeza", crudo, StringComparison.Ordinal);
        Assert.DoesNotContain("salud.paciente", crudo, StringComparison.Ordinal);
        Assert.Equal("Me duele la cabeza desde el martes", Assert.Single(leido!.Messages).Body);
    }

    [Fact]
    public async Task Lo_guardado_en_claro_antes_del_cifrado_se_sigue_leyendo_y_lo_que_no_se_descifra_no()
    {
        await _disco.WriteAsync("social-messages", "viejo", """{"ThreadId":"thr_1"}""");
        await _disco.WriteAsync("social-messages", "roto", "CfDJ8-esto-no-es-de-esta-llave");

        Assert.Equal("""{"ThreadId":"thr_1"}""", await _cifrado.ReadAsync("social-messages", "viejo"));
        Assert.Null(await _cifrado.ReadAsync("social-messages", "roto"));
        Assert.Equal(new[] { """{"ThreadId":"thr_1"}""" }, await _cifrado.ListAsync("social-messages"));
    }
}
