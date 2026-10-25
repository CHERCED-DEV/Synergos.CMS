using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services.Puerta;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La entrada de la reserva de viaje en la puerta del CMS (línea 006, T014): <c>Synergos:Puerta:Flujos:viajes.reserva</c>
/// del <c>appsettings.json</c> que reciben todos los entornos.
/// </summary>
/// <remarks>
/// <para>La puerta la valida contra los contratos incrustados (el de <c>Bff.Viajes</c> marca las seis operaciones de
/// <c>viajes.reserva</c>), abre sólo cuando el despliegue le da destino a ese orquestador (<c>Synergos:Viajes:BaseUrl</c>),
/// y su sujeto es el MISMO <c>Kind</c> con que los clientes de viajes del CMS nombran a quien viaja: con otro, lo que
/// salió por la puerta sería de un dueño que la ruta directa no encuentra.</para>
///
/// <para>Que la entrada exista no cambia el camino de ningún sitio: la regla abre la puerta sólo donde el sitio lo
/// declara, y «directa» es el valor por defecto de los dos elementos (006-P5, T015).</para>
/// </remarks>
public sealed class PuertaDeViajesTests
{
    private const string Flujo = "viajes.reserva";

    [Fact]
    public void La_entrada_existe_y_la_puerta_la_valida_contra_el_contrato_incrustado()
    {
        var ajustes = Ajustes();

        Assert.True(ajustes.Flujos.TryGetValue(Flujo, out var reserva), $"{Flujo} no está en Synergos:Puerta:Flujos.");
        Assert.Equal(AccesoDeLaPuerta.Miembro, reserva!.Acceso);
        Assert.Equal("/viajes/?viaje={id}", reserva.Aviso?.Ruta);

        // Sólo esta entrada: las demás las vigila su propia línea.
        var sola = new PuertaSettings
        {
            Flujos = new Dictionary<string, FlujoDeLaPuertaSettings>(StringComparer.Ordinal) { [Flujo] = reserva },
        };
        var resultado = new ValidadorDeLaPuerta(TablaDeLaPuerta.Incrustada()).Validate(null, sola);
        Assert.True(resultado.Succeeded, resultado.FailureMessage);
    }

    [Fact]
    public void El_sujeto_de_la_puerta_es_quien_viaja_para_los_clientes_de_viajes()
        => Assert.Equal(new ViajesSettings().TravellerKind, Ajustes().Flujos[Flujo].SujetoKind);

    [Fact]
    public void La_tabla_expone_las_seis_operaciones_de_la_reserva_y_ninguna_de_operacion()
    {
        var operaciones = TablaDeLaPuerta.Incrustada().DelFlujo(Flujo).Select(o => o.Operacion).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "abrir", "cancelar", "cerrar", "consultar", "cotizar", "listar" }, operaciones);
    }

    /// <summary>
    /// Lo de quien vende sobre un viaje confirmado (línea 006, T026 y T027): devolver una parte y cancelar con penalidad
    /// son flujos POSTERIORES del personal. La puerta los abre sólo a quien opera con su rol, con un sujeto que no es el de
    /// quien viaja; el contrato incrustado los marca del personal.
    /// </summary>
    [Theory]
    [InlineData("viajes.devolucion", "devolver")]
    [InlineData("viajes.cancelacion", "abrir")]
    public void Lo_de_quien_vende_es_del_personal_con_su_rol_y_la_puerta_lo_valida(string flujo, string operacion)
    {
        var entrada = Ajustes().Flujos[flujo];
        Assert.Equal(AccesoDeLaPuerta.Personal, entrada.Acceso);
        Assert.Equal(new[] { "admin" }, entrada.Roles);
        Assert.Equal("viajes.personal", entrada.SujetoKind);

        var tabla = TablaDeLaPuerta.Incrustada();
        Assert.Contains(tabla.DelFlujo(flujo), op => op.Operacion == operacion && op.Personal);
        Assert.All(tabla.DelFlujo(flujo), op => Assert.True(op.Personal, $"{flujo} {op.Operacion} no está marcada del personal."));

        var sola = new PuertaSettings
        {
            Flujos = new Dictionary<string, FlujoDeLaPuertaSettings>(StringComparer.Ordinal) { [flujo] = entrada },
        };
        var resultado = new ValidadorDeLaPuerta(tabla).Validate(null, sola);
        Assert.True(resultado.Succeeded, resultado.FailureMessage);
    }

    [Fact]
    public void Abre_solo_con_destino_para_el_orquestador_de_viajes()
    {
        var tabla = TablaDeLaPuerta.Incrustada();
        // El destino sale, por convención, de Synergos:{orquestador}:BaseUrl: aquí, Synergos:Viajes:BaseUrl.
        Assert.All(tabla.DelFlujo(Flujo), op => Assert.Equal("Viajes", op.Orquestador));

        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(Ajustes());

        Assert.False(new PuertaDeLosFlujos(tabla, monitor, new DestinosDeLaPuerta([])).Abre(Flujo));
        Assert.False(new PuertaDeLosFlujos(tabla, monitor, new DestinosDeLaPuerta(["Alquiler"])).Abre(Flujo));
        Assert.True(new PuertaDeLosFlujos(tabla, monitor, new DestinosDeLaPuerta(["Viajes"])).Abre(Flujo));
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
