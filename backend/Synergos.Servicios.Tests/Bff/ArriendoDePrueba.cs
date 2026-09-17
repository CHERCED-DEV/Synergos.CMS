using Microsoft.Extensions.Options;
using Synergos.Bff.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// El arriendo de sagas que usan los tests que no van de concurrencia (#34).
/// </summary>
/// <remarks>
/// <para><b>Es el de VERDAD sobre un directorio propio</b>, y no un doble que siempre concede. Un
/// doble permisivo sería cómodo y tendría el problema que ya mordió en <c>AbandonoTests</c>: un
/// test que reimplementa lo que prueba no vigila nada. Acá el riesgo es peor todavía —el arriendo
/// no es reentrante, así que un doble que siempre dice que sí taparía justo el caso en el que un
/// camino se bloquea a sí mismo (<c>RetryStuckAsync</c> pidiéndolo dos veces)—.</para>
///
/// <para>Cada llamada da un directorio propio, así que dos tests que compensen la misma saga no se
/// estorban. Quien quiera probar DOS barridos sobre el mismo almacén tiene que pedir la raíz
/// explícitamente, que es exactamente lo que hace <see cref="ArriendoDeCompensacionTests"/>.</para>
/// </remarks>
internal static class ArriendoDePrueba
{
    public static ISagaLease Nuevo()
        => Sobre(Path.Combine(Path.GetTempPath(), "syn-arriendo-" + Guid.NewGuid().ToString("n")));

    /// <summary>Un arriendo sobre una raíz concreta — para simular dos procesos compartiéndola.</summary>
    public static ISagaLease Sobre(string raiz, TimeProvider? reloj = null, int segundos = 300)
        => new FileSystemSagaLease(
            Options.Create(new SagaStorageOptions { Root = raiz }),
            new MonitorFijo(new SweepOptions { CompensationLeaseSeconds = segundos }),
            reloj ?? TimeProvider.System);

    private sealed class MonitorFijo : IOptionsMonitor<SweepOptions>
    {
        public MonitorFijo(SweepOptions valor) => CurrentValue = valor;
        public SweepOptions CurrentValue { get; }
        public SweepOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<SweepOptions, string?> listener) => null;
    }
}
