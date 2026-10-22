using Microsoft.Extensions.Logging;

namespace Synergos.CMS.Tests.Bff.Reservas;

/// <summary>
/// T045 (FR-009b, NB-9): una reserva se sigue en el registro del orquestador por el NÚMERO que ve la persona y que
/// lleva el aviso: cada llamada a una capacidad dentro de una fase, y cada compensación, lo lleva.
/// </summary>
/// <remarks>
/// La correlación de la puerta hasta cada capacidad no se ve aquí: el arnés sustituye la fábrica de clientes y se salta
/// el manejador que la propaga. Se verificó en vivo (bitácora, 2026-10-16).
/// </remarks>
public sealed class NumeroEnElRegistroTests
{
    private static List<(string Categoria, LogLevel Nivel, string Mensaje, List<string> Textos, Dictionary<string, object?> Valores)> Llamadas(
        RegistroConAmbitos registro)
        => registro.Lineas.Where(l => l.Categoria == ReservaDeServicioReal.LlamadasAlArnes).ToList();

    private static void TodasLoLlevan(IEnumerable<(string Categoria, LogLevel Nivel, string Mensaje, List<string> Textos, Dictionary<string, object?> Valores)> lineas,
        string saga, string numero)
        => Assert.All(lineas, l =>
        {
            Assert.True(numero.Equals(l.Valores.GetValueOrDefault("NumeroDeSaga")), $"«{l.Mensaje}» no lleva el número {numero}.");
            Assert.Equal(saga, l.Valores.GetValueOrDefault("Saga"));
            Assert.Contains($"Saga:{saga} Numero:{numero}", l.Textos);
        });

    [Fact]
    public async Task Cada_llamada_de_abrir_y_de_cerrar_lleva_el_numero_de_la_reserva()
    {
        var registro = new RegistroConAmbitos();
        using var r = await new ReservaDeServicioReal(registro: registro).ConOferta();
        lock (registro.Lineas) registro.Lineas.Clear();

        var (_, abierta) = await r.Abrir();
        var id = abierta.GetProperty("id").GetString()!;
        var (_, cerrada) = await r.Cerrar(id);

        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        var llamadas = Llamadas(registro);
        foreach (var ruta in new[] { "/v1/quotes", "/v1/holds", "/v1/payments", "/capture", "/confirm", "/v1/deliveries" })
        {
            Assert.Contains(llamadas, l => l.Mensaje.Contains(ruta, StringComparison.Ordinal));
        }
        TodasLoLlevan(llamadas, id, abierta.GetProperty("numero").GetString()!);
    }

    [Fact]
    public async Task Lo_que_se_deshace_tambien_lleva_el_numero()
    {
        var registro = new RegistroConAmbitos();
        using var r = await new ReservaDeServicioReal(
            falla: p => ReservaDeServicioReal.Es(p, "POST", "^/v1/payments/[^/]+/capture$") ? "payments.capture_declined" : null,
            registro: registro).ConOferta();
        var (_, abierta) = await r.Abrir();
        var id = abierta.GetProperty("id").GetString()!;
        lock (registro.Lineas) registro.Lineas.Clear();

        var (estado, _) = await r.Cerrar(id);

        Assert.Equal(System.Net.HttpStatusCode.Conflict, estado);
        var deshacer = registro.Lineas.Where(l => l.Categoria == "Synergos.Bff.Core.Compensator").ToList();
        Assert.Equal(2, deshacer.Count);
        TodasLoLlevan(deshacer.Concat(Llamadas(registro)), id, abierta.GetProperty("numero").GetString()!);
    }
}

/// <summary>Un registrador que guarda cada línea con sus ámbitos, como los imprime la consola.</summary>
internal sealed class RegistroConAmbitos : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider _ambitos = new LoggerExternalScopeProvider();

    public List<(string Categoria, LogLevel Nivel, string Mensaje, List<string> Textos, Dictionary<string, object?> Valores)> Lineas { get; } = [];

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _ambitos = scopeProvider;

    public ILogger CreateLogger(string categoryName) => new Registrador(this, categoryName);

    public void Dispose() { }

    private sealed class Registrador(RegistroConAmbitos dueno, string categoria) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => dueno._ambitos.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var textos = new List<string>();
            var valores = new Dictionary<string, object?>(StringComparer.Ordinal);
            dueno._ambitos.ForEachScope((ambito, _) =>
            {
                textos.Add(ambito?.ToString() ?? string.Empty);
                if (ambito is IEnumerable<KeyValuePair<string, object?>> pares)
                {
                    foreach (var (k, v) in pares) valores[k] = v;
                }
            }, (object?)null);
            lock (dueno.Lineas) dueno.Lineas.Add((categoria, logLevel, formatter(state, exception), textos, valores));
        }
    }
}
