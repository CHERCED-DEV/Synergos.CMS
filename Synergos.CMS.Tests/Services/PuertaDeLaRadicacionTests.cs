using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Puerta;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La entrada de la radicación en la puerta del CMS (009-gobierno-tramite T026):
/// <c>Synergos:Puerta:Flujos:gobierno.radicacion</c> del <c>appsettings.json</c> que reciben todos los entornos.
/// </summary>
/// <remarks>
/// <para>Por la puerta sólo miembros: el sujeto es el ciudadano, el mismo dueño con que el orquestador guarda la
/// radicación. El destino es el de la convención, <c>Synergos:Gobierno:BaseUrl</c>, que pone el despliegue: sin él la
/// puerta no abre el flujo y contesta 503 <c>puerta.flujo_no_disponible</c> (la misma condición que
/// <see cref="PuertaDeLosFlujos.Abre"/>).</para>
///
/// <para><b>El camino se declara «directa»</b>: radicar por la puerta mueve plata, y el sitio que la quiera la declara
/// (T033). Con la puerta abierta, el camino sigue siendo el directo.</para>
///
/// <para><b>La ruta del aviso lleva UN parámetro con marcador</b> (<c>?radicacion={id}</c>): G-15 lo exige —sin él el
/// participante no sabría qué leer— y es la página del portal, que abre la radicación en «Mis solicitudes».</para>
/// </remarks>
public sealed class PuertaDeLaRadicacionTests
{
    private const string Radicacion = "gobierno.radicacion";

    [Fact]
    public void La_entrada_existe_y_la_puerta_la_valida_contra_el_contrato_incrustado()
    {
        var ajustes = Ajustes();
        Assert.True(ajustes.Flujos.TryGetValue(Radicacion, out var radicacion), $"{Radicacion} no está en Synergos:Puerta:Flujos.");

        // Sólo esta entrada: las demás las vigila su propia línea. Es el validador con que el CMS no arranca.
        var sola = new PuertaSettings
        {
            Flujos = new Dictionary<string, FlujoDeLaPuertaSettings>(StringComparer.Ordinal) { [Radicacion] = radicacion! },
        };
        var resultado = new ValidadorDeLaPuerta(TablaDeLaPuerta.Incrustada()).Validate(null, sola);
        Assert.True(resultado.Succeeded, resultado.FailureMessage);

        Assert.Equal(AccesoDeLaPuerta.Miembro, radicacion!.Acceso);
        Assert.Equal("gobierno.ciudadano", radicacion.SujetoKind);
        Assert.Empty(radicacion.Invitado);
    }

    [Fact]
    public void El_aviso_lleva_a_la_radicacion_en_el_portal_de_gobierno()
    {
        Assert.Equal("/gobierno/?radicacion={id}", Ajustes().Flujos[Radicacion].Aviso?.Ruta);
    }

    [Fact]
    public void Abre_solo_con_destino_para_el_orquestador_de_gobierno_y_el_camino_sigue_siendo_el_directo()
    {
        var tabla = TablaDeLaPuerta.Incrustada();
        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(Ajustes());
        var sinDestino = new PuertaDeLosFlujos(tabla, monitor, new DestinosDeLaPuerta([]));
        var conDestino = new PuertaDeLosFlujos(tabla, monitor, new DestinosDeLaPuerta(["Gobierno"]));

        Assert.NotEmpty(tabla.DelFlujo(Radicacion));
        Assert.All(tabla.DelFlujo(Radicacion), op => Assert.Equal("Gobierno", op.Orquestador));
        Assert.False(sinDestino.Abre(Radicacion));
        Assert.True(conDestino.Abre(Radicacion));
        Assert.Equal(CaminoDelFlujo.Directa,
            new CaminoDeLosFlujos(conDestino, monitor, NullLogger<CaminoDeLosFlujos>.Instance).Decidir(Radicacion));
    }

    /// <summary><c>Synergos:Puerta</c> del <c>appsettings.json</c> base, enlazada como la enlaza el CMS.</summary>
    private static PuertaSettings Ajustes()
        => new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "appsettings.json"), optional: false)
            .Build()
            .GetSection("Synergos:Puerta")
            .Get<PuertaSettings>()!;

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
