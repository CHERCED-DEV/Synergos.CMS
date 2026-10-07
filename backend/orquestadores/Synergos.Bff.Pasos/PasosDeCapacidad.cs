using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.Bff.Pasos;

// ── Los pasos sobre capacidades ─────────────────────────────────────────────
// Cada uno es UNA llamada a su puerto, con la llave que su definición declara, y devuelve lo que
// la capacidad contestó: el valor, o su rechazo tal cual. Ninguno sabe de qué negocio es lo que
// cotiza, aparta o cobra — eso lo pone el orquestador en el contexto antes de llamarlos.

/// <summary><c>pricing.cotizar</c>: lee las líneas a cotizar y escribe la cotización.</summary>
public sealed class PasoCotizacion(IPricingPort precios) : IPaso
{
    public string Tipo => "pricing.cotizar";
    public int Lecturas => 1;
    public int Escrituras => 1;

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var cotizacion = await precios.CotizarAsync(entrada.Lee<IReadOnlyList<(Ref Sujeto, int Cantidad)>>(0), ct);
        return cotizacion.IsOk ? SalidaDePaso.Sigue(cotizacion.Value) : SalidaDePaso.Rechaza(cotizacion.Rejection!);
    }
}

/// <summary><c>inventory.hallar</c>: lee el sujeto del pozo y escribe su identificador.</summary>
/// <remarks>El identificador lo genera la capacidad: se pregunta por el sujeto, no se adivina.</remarks>
public sealed class PasoInventarioHallar(IInventoryPort existencias) : IPaso
{
    public string Tipo => "inventory.hallar";
    public int Lecturas => 1;
    public int Escrituras => 1;

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var pozo = await existencias.HallarAsync(entrada.Lee<Ref>(0), ct);
        return pozo.IsOk ? SalidaDePaso.Sigue(pozo.Value) : SalidaDePaso.Rechaza(pozo.Rejection!);
    }
}

/// <summary>
/// <c>inventory.apartar</c>: lee el pozo y la cantidad, aparta y escribe el apartado. RESERVA.
/// </summary>
/// <remarks>
/// La llave es por ítem —<c>llave_base</c> más el pozo— y no por posición. Lo reservado se deshace
/// soltando el apartado; una vez consumido, ajustando el POZO, que es lo que devuelve como cierre.
/// </remarks>
public sealed class PasoInventarioApartar(IInventoryPort existencias) : IPaso
{
    public string Tipo => "inventory.apartar";
    public int Lecturas => 2;
    public int Escrituras => 1;

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var itemId = entrada.Lee<string>(0);
        var apartado = await existencias.ApartarAsync(
            itemId, entrada.Lee<int>(1), entrada.Origen, entrada.LlavePara(itemId), ct);

        return apartado.IsOk
            ? SalidaDePaso.Reserva(new Reservado(apartado.Value, CierreId: itemId), apartado.Value)
            : SalidaDePaso.Rechaza(apartado.Rejection!);
    }
}

/// <summary><c>inventory.consumir</c>: lee el apartado y lo consume. CIERRA una reserva.</summary>
public sealed class PasoInventarioConsumir(IInventoryPort existencias) : IPaso
{
    public string Tipo => "inventory.consumir";
    public int Lecturas => 1;
    public int Escrituras => 0;

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var consumido = await existencias.ConsumirAsync(entrada.Lee<string>(0), ct);
        return consumido.IsOk ? SalidaDePaso.Sigue() : SalidaDePaso.Rechaza(consumido.Rejection!);
    }
}

/// <summary>
/// <c>payments.autorizar</c>: lee quién paga y cuánto, autoriza y escribe el cobro. RESERVA.
/// </summary>
/// <remarks>Reserva cupo en el medio de pago SIN mover plata: deshacerlo es anular, no devolver.</remarks>
public sealed class PasoPagosAutorizar(IPaymentsPort pagos) : IPaso
{
    public string Tipo => "payments.autorizar";
    public int Lecturas => 2;
    public int Escrituras => 1;

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var cobro = await pagos.AutorizarAsync(
            entrada.Origen, entrada.Lee<Ref>(0), entrada.Lee<Money>(1), entrada.Llave(), ct);

        return cobro.IsOk
            ? SalidaDePaso.Reserva(new Reservado(cobro.Value), cobro.Value)
            : SalidaDePaso.Rechaza(cobro.Rejection!);
    }
}

/// <summary><c>payments.capturar</c>: lee el cobro y lo captura. CIERRA una reserva.</summary>
/// <remarks>A partir de acá hay plata movida, y deshacer significa devolver.</remarks>
public sealed class PasoPagosCapturar(IPaymentsPort pagos) : IPaso
{
    public string Tipo => "payments.capturar";
    public int Lecturas => 1;
    public int Escrituras => 0;

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var capturado = await pagos.CapturarAsync(entrada.Lee<string>(0), entrada.Llave(), ct);
        return capturado.IsOk ? SalidaDePaso.Sigue() : SalidaDePaso.Rechaza(capturado.Rejection!);
    }
}
