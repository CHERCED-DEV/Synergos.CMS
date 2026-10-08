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
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;

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
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;

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
    public LlaveRequerida Llave => LlaveRequerida.PorItem;

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
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;

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
    public LlaveRequerida Llave => LlaveRequerida.Fija;

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
    public LlaveRequerida Llave => LlaveRequerida.Fija;

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var capturado = await pagos.CapturarAsync(entrada.Lee<string>(0), entrada.Llave(), ct);
        return capturado.IsOk ? SalidaDePaso.Sigue() : SalidaDePaso.Rechaza(capturado.Rejection!);
    }
}

/// <summary>
/// <c>notifications.avisar</c>: lee a quién, adónde y cuánto, y avisa con la plantilla que declara.
/// </summary>
/// <remarks>
/// <para><b>Los valores son FIJOS y los define el paso</b>: <c>nombre</c>, <c>numero</c>,
/// <c>total</c>, <c>enlace</c> y <c>sitio</c>. Sin mapeo en el JSON, que sería el lenguaje de
/// expresiones que la ADR 0140 no quiere. La plantilla puede usar éstos y ninguno más
/// (<c>PlantillaDelAvisoTests</c> lo cruza contra lo que el paso manda de verdad).</para>
///
/// <para><b>El número sale de la saga</b>: sus últimos ocho caracteres alfanuméricos, sin prefijo.
/// El aviso del CMS lo armaba recortando un prefijo que este camino no tenía, y salía
/// «SYN-EVT-EVT-…».</para>
///
/// <para><b>Sin dirección no avisa, y sigue</b>: no es un fallo, es que nadie pidió el aviso por
/// este camino. La llave es fija por saga (<c>llave</c> en la definición), así que repetir el paso
/// —una confirmación reintentada— no manda dos correos. Lo que falle al mandar vuelve tal cual: si
/// eso deshace o no la saga lo decide la definición (<c>al_fallar</c>), no el paso.</para>
/// </remarks>
public sealed class PasoAvisar(INotificationsPort avisos) : IPaso
{
    /// <summary>El total como se lee en es-CO, sin depender de la cultura del proceso.</summary>
    private static readonly System.Globalization.NumberFormatInfo EsCo = new()
    {
        NumberGroupSeparator = ".",
        NumberDecimalSeparator = ",",
        NumberGroupSizes = new[] { 3 },
    };

    public string Tipo => "notifications.avisar";
    public int Lecturas => 3;
    public int Escrituras => 0;
    public LlaveRequerida Llave => LlaveRequerida.Fija;
    public bool UsaPlantilla => true;

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var contacto = entrada.Lee<Contacto?>(1);
        if (contacto is null || string.IsNullOrWhiteSpace(contacto.Correo)) return SalidaDePaso.Sigue();

        var avisado = await avisos.AvisarAsync(
            entrada.Lee<Ref>(0), contacto.Correo, entrada.Paso.Plantilla!,
            Valores(entrada.SagaId, contacto, entrada.Lee<Money>(2)), entrada.Llave(), ct);

        return avisado.IsOk ? SalidaDePaso.Sigue() : SalidaDePaso.Rechaza(avisado.Rejection!);
    }

    /// <summary>Los valores fijos del aviso.</summary>
    public static IReadOnlyDictionary<string, string> Valores(string sagaId, Contacto contacto, Money total)
    {
        ArgumentNullException.ThrowIfNull(contacto);
        var alfanumerico = new string(sagaId.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["nombre"] = contacto.Nombre ?? string.Empty,
            ["numero"] = alfanumerico.Length <= 8 ? alfanumerico : alfanumerico[^8..],
            ["total"] = $"{total.Amount.ToString("#,##0.##", EsCo)} {total.Currency}",
            ["enlace"] = contacto.Enlace ?? string.Empty,
            ["sitio"] = contacto.Sitio ?? string.Empty,
        };
    }
}
