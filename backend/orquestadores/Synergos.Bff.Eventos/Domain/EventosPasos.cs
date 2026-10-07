using Synergos.Bff.Core.Flow;
using Synergos.Bff.Eventos.Clients;
using Synergos.Bff.Pasos;
using Synergos.Core;

namespace Synergos.Bff.Eventos.Domain;

/// <summary>
/// Los pasos con los que se ejecuta <c>eventos.compra</c>: los comunes sobre capacidades y los que
/// son de la compra de entradas y de nadie más.
/// </summary>
/// <remarks>
/// <para><b>Un solo sitio arma el registro</b>, y lo usan los dos que lo necesitan: la fachada, que
/// interpreta el flujo, y el arranque, que lo valida. Dos listas escritas a mano se desviarían, y el
/// arranque validaría contra pasos que la compra no tiene.</para>
///
/// <para><b>Lo que es del dominio se queda en C#</b>: revisar las líneas, la comisión de servicio,
/// cómo se nombra el pozo de una butaca. Declararlo sería escribir un lenguaje de expresiones para
/// decir lo que estas cinco clases dicen en una línea cada una.</para>
/// </remarks>
public static class EventosPasos
{
    public static IRegistroDePasos Registro(EventosCapabilities caps)
    {
        var puertos = new EventosPorts(caps);
        return new RegistroDePasos(new IPaso[]
        {
            new PasoRevisarLineas(),
            new PasoRevisarComision(),
            new PasoSujetosDePrecio(),
            new PasoTotal(),
            new PasoSujetoDePozo(),
            new PasoCotizacion(puertos),
            new PasoInventarioHallar(puertos),
            new PasoInventarioApartar(puertos),
            new PasoInventarioConsumir(puertos),
            new PasoPagosAutorizar(puertos),
            new PasoPagosCapturar(puertos),
        });
    }
}

/// <summary>Los flujos que este orquestador declara, leídos de sus recursos.</summary>
public static class EventosFlujos
{
    private static readonly Lazy<FlujoDef> _compra = new(() => Leer("flujos/eventos.compra.json"));

    /// <summary>
    /// <c>eventos.compra</c>: apartar y autorizar al abrir; capturar y consumir al cerrar.
    /// </summary>
    /// <remarks>
    /// Viaja DENTRO del ensamblado y no como fichero suelto: un despliegue no puede quedar con el
    /// binario de una versión y la definición de otra.
    /// </remarks>
    public static FlujoDef Compra => _compra.Value;

    private static FlujoDef Leer(string recurso)
    {
        using var flujo = typeof(EventosFlujos).Assembly.GetManifestResourceStream(recurso)
            ?? throw new InvalidOperationException($"Falta el recurso «{recurso}» en {typeof(EventosFlujos).Assembly.GetName().Name}.");
        using var lector = new StreamReader(flujo);
        return FlujoDef.Leer(lector.ReadToEnd());
    }
}

/// <summary><c>eventos.revisar-lineas</c>: lo que una compra de entradas tiene que cumplir. Sin HTTP.</summary>
internal sealed class PasoRevisarLineas : IPaso
{
    public string Tipo => "eventos.revisar-lineas";
    public int Lecturas => 1;
    public int Escrituras => 0;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        var lineas = entrada.Lee<IReadOnlyList<FlowContext>>(0).Select(EventosFlowBinding.Linea).ToList();
        return Task.FromResult(TicketingFlow.Revisar(lineas) is { } motivo
            ? SalidaDePaso.Rechaza(motivo)
            : SalidaDePaso.Sigue());
    }
}

/// <summary><c>eventos.revisar-comision</c>: de 0 a 100 y con dos decimales como mucho (ADR 0137).</summary>
internal sealed class PasoRevisarComision : IPaso
{
    public string Tipo => "eventos.revisar-comision";
    public int Lecturas => 1;
    public int Escrituras => 0;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
        => Task.FromResult(ComisionDeServicio.Revisar(entrada.Lee<decimal>(0)) is { } motivo
            ? SalidaDePaso.Rechaza(motivo)
            : SalidaDePaso.Sigue());
}

/// <summary>
/// <c>eventos.sujetos-precio</c>: qué se cotiza — una línea por LOCALIDAD, sin la butaca.
/// </summary>
/// <remarks>
/// Dos butacas de la misma localidad valen lo mismo; meter el asiento obligaría a cargar un precio
/// por butaca. Se agrupan en el orden en que aparecen, que es el orden en que se cotizaban.
/// </remarks>
internal sealed class PasoSujetosDePrecio : IPaso
{
    public string Tipo => "eventos.sujetos-precio";
    public int Lecturas => 2;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        var eventId = entrada.Lee<string>(0);
        IReadOnlyList<(Ref Sujeto, int Cantidad)> aCotizar = entrada.Lee<IReadOnlyList<FlowContext>>(1)
            .Select(EventosFlowBinding.Linea)
            .GroupBy(l => l.Tier, StringComparer.Ordinal)
            .Select(g => (Sujeto: AforoSubject.PriceOf(eventId, g.Key), Cantidad: g.Sum(l => l.Quantity)))
            .ToList();
        return Task.FromResult(SalidaDePaso.Sigue(aCotizar));
    }
}

/// <summary>
/// <c>eventos.total</c>: el total de la cotización más la comisión sobre su SUBTOTAL (#194).
/// </summary>
/// <remarks>Sin ella, quien compraba veía un total y se le autorizaba otro.</remarks>
internal sealed class PasoTotal : IPaso
{
    public string Tipo => "eventos.total";
    public int Lecturas => 2;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        var cotizacion = entrada.Lee<QuoteView>(0);
        var total = cotizacion.Total + ComisionDeServicio.Sobre(cotizacion.Subtotal, entrada.Lee<decimal>(1));
        return Task.FromResult(SalidaDePaso.Sigue(total));
    }
}

/// <summary><c>eventos.sujeto-pozo</c>: de qué pozo de aforo sale una línea (<see cref="AforoSubject"/>).</summary>
internal sealed class PasoSujetoDePozo : IPaso
{
    public string Tipo => "eventos.sujeto-pozo";
    public int Lecturas => 2;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        var linea = EventosFlowBinding.Linea(entrada.Lee<FlowContext>(1));
        return Task.FromResult(SalidaDePaso.Sigue(AforoSubject.For(entrada.Lee<string>(0), linea.Tier, linea.Seat)));
    }
}
