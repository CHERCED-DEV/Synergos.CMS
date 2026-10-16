using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services.Puerta;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Qué flujo coloca una vista (núcleo N3, FR-033): el que elige el editor si es de la lista cerrada —lo
/// expone la puerta y el sitio lo declara—; si no, el por defecto de la vista. El contenido no decide solo.
/// </summary>
public sealed class FlujosColocablesTests
{
    private const string Expuesto = "eventos.compra";
    private const string PorDefecto = "eventos.compra";

    private static (FlujosColocables Colocables, LogQueCuenta Log) Colocables(params string[] declarados)
    {
        var ajustes = new PuertaSettings();
        foreach (var flujo in declarados) ajustes.Flujos[flujo] = new FlujoDeLaPuertaSettings { Acceso = AccesoDeLaPuerta.Miembro };
        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(ajustes);
        var log = new LogQueCuenta();
        return (new FlujosColocables(TablaDeLaPuerta.Incrustada(), monitor, log), log);
    }

    [Fact]
    public void Sin_eleccion_queda_el_por_defecto_sin_avisar()
    {
        var (colocables, log) = Colocables(Expuesto);

        Assert.Equal(PorDefecto, colocables.Elegir(null, PorDefecto));
        Assert.Equal(PorDefecto, colocables.Elegir("   ", PorDefecto));
        Assert.Equal(0, log.Avisos);
    }

    [Fact]
    public void Lo_que_elige_el_editor_vale_si_la_puerta_lo_expone_y_el_sitio_lo_declara()
    {
        var (colocables, log) = Colocables(Expuesto);

        Assert.True(colocables.EsColocable(Expuesto));
        Assert.Equal(Expuesto, colocables.Elegir($" {Expuesto} ", "otro.defecto"));
        Assert.Equal(0, log.Avisos);
    }

    [Fact]
    public void Fuera_de_la_lista_cerrada_vale_el_por_defecto_y_queda_en_el_log()
    {
        // Lo expone la puerta pero el sitio no lo declara: un flujo cerrado después de guardar el contenido.
        var (sinDeclarar, log) = Colocables();
        Assert.False(sinDeclarar.EsColocable(Expuesto));
        Assert.Equal("otro.defecto", sinDeclarar.Elegir(Expuesto, "otro.defecto"));
        Assert.Equal(1, log.Avisos);

        // Lo declara el sitio pero ninguna puerta lo expone.
        var (sinContrato, otroLog) = Colocables(Expuesto, "salud.cita");
        Assert.False(sinContrato.EsColocable("salud.cita"));
        Assert.Equal(PorDefecto, sinContrato.Elegir("salud.cita", PorDefecto));
        Assert.Equal(1, otroLog.Avisos);
    }

    private sealed class LogQueCuenta : ILogger<FlujosColocables>
    {
        public int Avisos { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Avisos++;
            }
        }
    }
}
