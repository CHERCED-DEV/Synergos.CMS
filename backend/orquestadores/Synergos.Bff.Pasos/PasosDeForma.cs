using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.Bff.Pasos;

// ── Los pasos de forma, sin parámetros (núcleo N1, supuesto P2) ─────────────
// Adaptan lo que un paso escribe a lo que otro lee, sin parámetros en el dato: lo que necesitaría una
// plantilla o una constante (un sujeto armado con el evento y la localidad, un tope de líneas) sigue
// siendo un paso de dominio corto, hasta que el arquitecto decida otra cosa (P2).

/// <summary><c>forma.una-linea</c>: lee un sujeto y escribe la lista de una línea a cotizar (ese sujeto, uno).</summary>
/// <remarks>Lo que cotiza una cita (un servicio) o una visita: la cotización lee líneas, no un sujeto.</remarks>
public sealed class PasoUnaLinea : IPaso
{
    public string Tipo => "forma.una-linea";
    public int Lecturas => 1;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(IReadOnlyList<(Ref Sujeto, int Cantidad)>)];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        IReadOnlyList<(Ref Sujeto, int Cantidad)> lineas = [(entrada.Lee<Ref>(0), 1)];
        return Task.FromResult(SalidaDePaso.Sigue(lineas));
    }
}

/// <summary>
/// <c>pricing.desglose</c>: lee una cotización y escribe sus cuatro montos: subtotal, descuento, impuesto y
/// total, en ese orden.
/// </summary>
/// <remarks>
/// Es lo que deja a un flujo autorizar el total de la cotización —o mostrar su desglose como salida— sin un
/// paso de dominio que lo saque. Los cuatro son <c>dinero</c>, que una saga genérica puede guardar; la
/// cotización entera no.
/// </remarks>
public sealed class PasoDesglose : IPaso
{
    public string Tipo => "pricing.desglose";
    public int Lecturas => 1;
    public int Escrituras => 4;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(Money), typeof(Money), typeof(Money), typeof(Money)];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var c = entrada.Lee<QuoteView>(0);
        return Task.FromResult(SalidaDePaso.Sigue(c.Subtotal, c.Descuento, c.Impuesto, c.Total));
    }
}

/// <summary><c>pricing.cotizar-sin-codigo</c>: cotiza las líneas sin promoción. Lee sólo las líneas.</summary>
/// <remarks>Para el flujo que no tiene códigos (la cita): sin él, declararía una entrada que vale siempre nulo.</remarks>
public sealed class PasoCotizacionSinCodigo(IPricingPort precios) : IPaso
{
    public string Tipo => "pricing.cotizar-sin-codigo";
    public int Lecturas => 1;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(QuoteView)];

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var cotizacion = await precios.CotizarAsync(entrada.Lee<IReadOnlyList<(Ref Sujeto, int Cantidad)>>(0), null, ct);
        return cotizacion.IsOk ? SalidaDePaso.Sigue(cotizacion.Value) : SalidaDePaso.Rechaza(cotizacion.Rejection!);
    }
}

/// <summary>
/// <c>pricing.cotizar-o-cero</c>: cotiza sin promoción y, si no hay precio publicado, deja una cotización en
/// cero. Cualquier otro rechazo vuelve tal cual.
/// </summary>
/// <remarks>
/// <para><b>Sólo «no hay precio» es cero</b> (<see cref="SinPrecio"/>): una capacidad caída o un sujeto mal
/// escrito no son gratis, y tratarlos así es el defecto que la cita imperativa tenía («cualquier falla =
/// gratis»). Con cero, el cobro se omite (<c>omitir_si_cero</c>) y la reserva sigue.</para>
///
/// <para><b>El cero va en pesos</b> (<see cref="Money.Cop"/>, la moneda del producto): no hay precio del que
/// sacar otra, y un cero no se cobra.</para>
/// </remarks>
public sealed class PasoCotizacionOCero(IPricingPort precios) : IPaso
{
    /// <summary>El código de <c>Api.Pricing</c> cuando un sujeto no tiene precio publicado.</summary>
    public const string SinPrecio = "pricing.price_not_found";

    public string Tipo => "pricing.cotizar-o-cero";
    public int Lecturas => 1;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(QuoteView)];

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var cotizacion = await precios.CotizarAsync(entrada.Lee<IReadOnlyList<(Ref Sujeto, int Cantidad)>>(0), null, ct);
        if (cotizacion.IsOk) return SalidaDePaso.Sigue(cotizacion.Value);
        if (cotizacion.Rejection!.Code != SinPrecio) return SalidaDePaso.Rechaza(cotizacion.Rejection);

        var cero = Money.Zero(Money.Cop);
        return SalidaDePaso.Sigue(new QuoteView(cero, cero, cero, cero));
    }
}
