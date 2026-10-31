using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Gobierno;
using Synergos.CMS.Web.Services.Puerta;
using static Synergos.CMS.Tests.Services.Gobierno.ArtefactoDelExpedienteTests;

namespace Synergos.CMS.Tests.Services.Gobierno;

/// <summary>
/// La pieza de los actos sobre un expediente de la puerta (009-gobierno-tramite T039): los controles de la puerta antes
/// de salir —el flujo abierto en el sitio, la sesión y, en uno del personal, su rol— y la llave atada a la persona. Con la
/// tabla incrustada y las entradas del <c>appsettings.json</c>; el orquestador, simulado.
/// </summary>
public sealed class ActosDeLaPuertaTests
{
    private static readonly Guid Rosa = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid Tomas = Guid.Parse("99999999-8888-7777-6666-555555555555");

    private readonly OrquestadorDeGobiernoFalso _orquestador = new();

    private ActosDeLaPuerta Pieza(IMemberAccessGate miembro, bool conDestino = true)
    {
        var tabla = TablaDeLaPuerta.Incrustada();
        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "appsettings.json"), optional: false)
            .Build().GetSection("Synergos:Puerta").Get<PuertaSettings>()!);
        return new ActosDeLaPuerta(
            tabla, new PuertaDeLosFlujos(tabla, monitor, new DestinosDeLaPuerta(conDestino ? ["Gobierno"] : [])),
            new ReenvioDeLaPuerta(_orquestador, NullLogger<ReenvioDeLaPuerta>.Instance), miembro, monitor);
    }

    private static IMemberAccessGate Miembro(Guid? quien, bool funcionario = false)
    {
        var gate = Substitute.For<IMemberAccessGate>();
        gate.IsAuthenticated.Returns(quien is not null);
        gate.CurrentMemberKey.Returns(quien);
        gate.HasAnyRole(Arg.Any<string?>()).Returns(funcionario);
        return gate;
    }

    [Fact]
    public async Task Sin_el_rol_del_flujo_o_sin_sesion_no_sale_nada()
    {
        var sinRol = await Pieza(Miembro(Rosa)).TomarAsync("rad-1", null, "toma-k1", default);
        var sinSesion = await Pieza(Miembro(null)).DecidirAsync("rad-1", "approve", "ok", "decision-k1", default);

        Assert.Equal((403, "puerta.personal_requerido"), (sinRol.Estado, sinRol.Codigo));
        Assert.Equal((401, "puerta.sesion_requerida"), (sinSesion.Estado, sinSesion.Codigo));
        Assert.Empty(_orquestador.Actos);
    }

    /// <summary>La subsanación es del dueño, como miembro: no pide rol.</summary>
    [Fact]
    public async Task La_subsanacion_no_pide_rol_y_sale_con_el_sujeto_del_ciudadano()
    {
        var r = await Pieza(Miembro(Rosa)).SubsanarAsync("rad-1", "Adjunto el certificado.", "subsanacion-k1", default);

        Assert.True(r.Bien, r.Mensaje);
        Assert.Equal($"gobierno.ciudadano:{Rosa:n}", Assert.Single(_orquestador.Actos).Sujeto);
    }

    [Fact]
    public async Task Sin_el_flujo_abierto_en_el_sitio_no_sale_nada()
    {
        var r = await Pieza(Miembro(Rosa, funcionario: true), conDestino: false).TomarAsync("rad-1", null, "toma-k1", default);

        Assert.Equal((503, "puerta.flujo_no_disponible", true), (r.Estado, r.Codigo, r.Transitorio));
        Assert.Empty(_orquestador.Actos);
    }

    /// <summary>
    /// La llave está atada a la persona, como la reemite la puerta: el reintento de Rosa es la misma llave —el mismo
    /// acto— y la misma llave del navegador en Tomás es otra, así que dos funcionarios a la vez son dos actos.
    /// </summary>
    [Fact]
    public async Task La_llave_esta_atada_a_la_persona()
    {
        await Pieza(Miembro(Rosa, funcionario: true)).TomarAsync("rad-1", null, "toma-k1", default);
        await Pieza(Miembro(Rosa, funcionario: true)).TomarAsync("rad-1", null, "toma-k1", default);
        await Pieza(Miembro(Tomas, funcionario: true)).TomarAsync("rad-1", null, "toma-k1", default);

        var llaves = _orquestador.Actos.Select(a => a.Llave).ToList();
        Assert.Equal(llaves[0], llaves[1]);
        Assert.NotEqual(llaves[0], llaves[2]);
        Assert.All(llaves, l => Assert.StartsWith(LoQuePoneLaPuerta.PrefijoDeLaLlave, l, StringComparison.Ordinal));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
