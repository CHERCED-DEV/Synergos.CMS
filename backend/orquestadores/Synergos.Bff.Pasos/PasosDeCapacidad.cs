using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.Bff.Pasos;

// ── Los pasos sobre capacidades ─────────────────────────────────────────────
// Cada uno es UNA llamada a su puerto, con la llave que su definición declara, y devuelve lo que
// la capacidad contestó: el valor, o su rechazo tal cual. Ninguno sabe de qué negocio es lo que
// cotiza, aparta o cobra — eso lo pone el orquestador en el contexto antes de llamarlos.

/// <summary>
/// <c>pricing.cotizar</c>: lee las líneas a cotizar y el código de la promoción, y escribe la cotización.
/// </summary>
/// <remarks>
/// <b>Las dos lecturas son obligatorias en la definición, y la segunda puede valer nulo</b>: sin
/// promoción no hay descuento. Es lo único que el navegador pone en el precio —un código—, y el
/// descuento lo calcula <c>Api.Pricing</c>; un código que no existe o que venció rechaza la compra con
/// el código de la capacidad, antes de apartar nada.
/// </remarks>
public sealed class PasoCotizacion(IPricingPort precios) : IPaso
{
    public string Tipo => "pricing.cotizar";
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(QuoteView)];
    public int Lecturas => 2;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var cotizacion = await precios.CotizarAsync(
            entrada.Lee<IReadOnlyList<(Ref Sujeto, int Cantidad)>>(0), entrada.Lee<string?>(1), ct);
        return cotizacion.IsOk ? SalidaDePaso.Sigue(cotizacion.Value) : SalidaDePaso.Rechaza(cotizacion.Rejection!);
    }
}

/// <summary><c>inventory.hallar</c>: lee el sujeto del pozo y escribe su identificador.</summary>
/// <remarks>El identificador lo genera la capacidad: se pregunta por el sujeto, no se adivina.</remarks>
public sealed class PasoInventarioHallar(IInventoryPort existencias) : IPaso
{
    public string Tipo => "inventory.hallar";
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string)];
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
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string)];
    public int Lecturas => 2;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.PorItem;

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var itemId = entrada.Lee<string>(0);
        var cantidad = entrada.Lee<int>(1);
        var apartado = await existencias.ApartarAsync(
            itemId, cantidad, entrada.Origen, entrada.LlavePara(itemId), ct);

        return apartado.IsOk
            ? SalidaDePaso.Reserva(new Reservado(apartado.Value, CierreId: itemId, Cantidad: cantidad), apartado.Value)
            : SalidaDePaso.Rechaza(apartado.Rejection!);
    }
}

/// <summary><c>inventory.consumir</c>: lee el apartado y lo consume. CIERRA una reserva.</summary>
public sealed class PasoInventarioConsumir(IInventoryPort existencias) : IPaso
{
    public string Tipo => "inventory.consumir";
    public IReadOnlyList<Type> TiposDeEscritura => [];
    public int Lecturas => 1;
    public int Escrituras => 0;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public bool ActuaSinLlave => true;

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
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string)];
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
    public IReadOnlyList<Type> TiposDeEscritura => [];
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
/// <c>payments.conservar</c>: lee el cobro y CIERRA su reserva sin capturarlo (núcleo, P7 = A del lote 6, NN-4 de 005).
/// </summary>
/// <remarks>
/// <para><b>No llama a nada.</b> La autorización sigue viva en <c>Api.Payments</c>, con su id en la salida del
/// flujo, y la resuelve la operación posterior: capturar una parte y devolver el resto, o anularla.</para>
///
/// <para><b>Hasta completar, deshacer la saga la sigue anulando</b> (<see cref="IPaso.Conserva"/>): el intérprete no le
/// cambia la compensación, así que da igual en qué lugar de la fase vaya. Es lo que <c>alquiler.conservar-garantia</c>
/// conseguía yendo al final.</para>
/// </remarks>
public sealed class PasoPagosConservar : IPaso
{
    public string Tipo => "payments.conservar";
    public IReadOnlyList<Type> TiposDeEscritura => [];
    public int Lecturas => 1;
    public int Escrituras => 0;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public bool Conserva => true;

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        ArgumentException.ThrowIfNullOrWhiteSpace(entrada.Lee<string>(0));
        return Task.FromResult(SalidaDePaso.Sigue());
    }
}

// ── Lo posterior al cierre (núcleo, lote 6): mover plata sobre una saga ya completada ─────────
// Los da un flujo aparte (P6 = A) después de leer la original. Van sobre el puerto de deshacer, que ya habla con
// esas mismas rutas de Api.Payments: no hay método nuevo en el adaptador.

/// <summary>
/// <c>payments.devolver</c>: lee el cobro y el monto, y devuelve ESE monto con la llave fija del paso (núcleo, T071).
/// </summary>
/// <remarks>
/// <para><b>El tope lo pone <c>Api.Payments</c>, no el paso</b>: devolver más de lo que queda por devolver es
/// <c>payments.refund_over_captured</c>, también sumando lo que devolvieron otras sagas. Dos operaciones posteriores con
/// llaves distintas no devuelven dos veces lo mismo.</para>
///
/// <para><b>El motivo es quién devuelve</b>: la operación posterior y su saga (<c>tienda.devolucion:…</c>). En el cobro
/// queda escrito de qué saga salió cada devolución.</para>
/// </remarks>
public sealed class PasoPagosDevolver(IDeshacerCobros cobros) : IPaso
{
    public string Tipo => "payments.devolver";
    public IReadOnlyList<Type> TiposDeEscritura => [];
    public int Lecturas => 2;
    public int Escrituras => 0;
    public LlaveRequerida Llave => LlaveRequerida.Fija;

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var devuelto = await cobros.DevolverAsync(
            entrada.Lee<string>(0), entrada.Lee<Money>(1), entrada.Origen.ToString(), entrada.Llave(), ct);
        return devuelto.IsOk ? SalidaDePaso.Sigue() : SalidaDePaso.Rechaza(devuelto.Rejection!);
    }
}

/// <summary><c>payments.anular</c>: lee el cobro y anula su autorización, sin capturar nada (núcleo, T071).</summary>
/// <remarks>Suelta la garantía conservada (P7) cuando no se cobra nada. Anular es idempotente en la capacidad: sin llave.</remarks>
public sealed class PasoPagosAnular(IDeshacerCobros cobros) : IPaso
{
    public string Tipo => "payments.anular";
    public IReadOnlyList<Type> TiposDeEscritura => [];
    public int Lecturas => 1;
    public int Escrituras => 0;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public bool ActuaSinLlave => true;

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var anulado = await cobros.AnularAsync(entrada.Lee<string>(0), ct);
        return anulado.IsOk ? SalidaDePaso.Sigue() : SalidaDePaso.Rechaza(anulado.Rejection!);
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
///
/// <para><b>Y los que declare la definición</b> (núcleo, NM-2 de 010; NN-6 de 003, NR-3 de 008): <c>"valores"</c> nombra
/// lo del contexto que también va, cada uno con el marcador del mismo nombre —el título del curso, el plan, la hora de la
/// cita—. Sin expresiones: un nombre, un marcador, escrito en es-CO según su tipo (<see cref="Escribir"/>). La hora sale en
/// la del sitio, la zona que declara el orquestador.</para>
///
/// <para><b>El total se puede no nombrar</b> (NR-3 de 008): un aviso de algo que no se cobra no escribe un cero que nadie
/// fijó, y entonces no lleva <c>{total}</c>.</para>
/// </remarks>
public sealed class PasoAvisar(INotificationsPort avisos, TimeZoneInfo zona) : IPaso
{
    /// <summary>La zona del sitio si el orquestador no declara otra (<c>{Origen}:ZonaHoraria</c>), como en Propiedades.</summary>
    public const string ZonaPorDefecto = "America/Bogota";

    private static readonly string[] Fijos = ["nombre", "numero", "total", "enlace", "sitio"];

    /// <summary>El total como se lee en es-CO, sin depender de la cultura del proceso.</summary>
    private static readonly System.Globalization.NumberFormatInfo EsCo = new()
    {
        NumberGroupSeparator = ".",
        NumberDecimalSeparator = ",",
        NumberGroupSizes = new[] { 3 },
    };

    public PasoAvisar(INotificationsPort avisos) : this(avisos, TimeZoneInfo.FindSystemTimeZoneById(ZonaPorDefecto)) { }

    public string Tipo => "notifications.avisar";
    public IReadOnlyList<Type> TiposDeEscritura => [];
    public int Lecturas => 3;
    public int LecturasOpcionales => 1;
    public int Escrituras => 0;
    public LlaveRequerida Llave => LlaveRequerida.Fija;
    public bool UsaPlantilla => true;
    public IReadOnlyCollection<string>? ValoresFijos => Fijos;

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var contacto = entrada.Lee<Contacto?>(1);
        if (contacto is null || string.IsNullOrWhiteSpace(contacto.Correo)) return SalidaDePaso.Sigue();

        var valores = new Dictionary<string, string>(
            Valores(entrada.SagaId, contacto, entrada.Paso.Lee.Count > 2 ? entrada.Lee<Money>(2) : null), StringComparer.Ordinal);
        foreach (var nombre in entrada.Paso.Valores ?? [])
        {
            valores[nombre] = Escribir(entrada.LeePorNombre(nombre), zona, nombre);
        }

        var avisado = await avisos.AvisarAsync(
            entrada.Lee<Ref>(0), contacto.Correo, entrada.Paso.Plantilla!, valores, entrada.Llave(), ct);

        return avisado.IsOk ? SalidaDePaso.Sigue() : SalidaDePaso.Rechaza(avisado.Rejection!);
    }

    /// <summary>Los valores fijos del aviso; sin <paramref name="total"/>, no lleva <c>total</c>.</summary>
    public static IReadOnlyDictionary<string, string> Valores(string sagaId, Contacto contacto, Money? total)
    {
        ArgumentNullException.ThrowIfNull(contacto);
        var alfanumerico = new string(sagaId.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();

        var valores = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["nombre"] = contacto.Nombre ?? string.Empty,
            ["numero"] = alfanumerico.Length <= 8 ? alfanumerico : alfanumerico[^8..],
            ["enlace"] = contacto.Enlace ?? string.Empty,
            ["sitio"] = contacto.Sitio ?? string.Empty,
        };
        if (total is { } t) valores["total"] = Dinero(t);
        return valores;
    }

    /// <summary>
    /// Un valor declarado como se lee en un correo es-CO: el texto tal cual; los números y el dinero con el punto de miles;
    /// la fecha <c>dd/MM/yyyy</c>; un instante y una ventana en la hora del sitio (<paramref name="zona"/>); sí o no. Nulo,
    /// vacío.
    /// </summary>
    /// <exception cref="InvalidOperationException">Si es de un tipo que no se escribe en un correo; el validador no deja
    /// arrancar una definición que lo declare.</exception>
    public static string Escribir(object? valor, TimeZoneInfo zona, string nombre = "valor")
    {
        ArgumentNullException.ThrowIfNull(zona);
        var c = System.Globalization.CultureInfo.InvariantCulture;
        return valor switch
        {
            null => string.Empty,
            string s => s,
            int n => n.ToString("#,##0", EsCo),
            decimal d => d.ToString("#,##0.##", EsCo),
            Money m => Dinero(m),
            DateOnly f => f.ToString("dd/MM/yyyy", c),
            DateTimeOffset i => TimeZoneInfo.ConvertTime(i, zona).ToString("dd/MM/yyyy HH:mm", c),
            TimeWindow v => Ventana(v, zona),
            bool b => b ? "sí" : "no",
            _ => throw new InvalidOperationException($"«{nombre}» es {valor.GetType().Name}, que no se escribe en un correo."),
        };
    }

    private static string Dinero(Money m) => $"{m.Amount.ToString("#,##0.##", EsCo)} {m.Currency}";

    private static string Ventana(TimeWindow v, TimeZoneInfo zona)
    {
        var c = System.Globalization.CultureInfo.InvariantCulture;
        var desde = TimeZoneInfo.ConvertTime(v.Start, zona);
        var hasta = TimeZoneInfo.ConvertTime(v.End, zona);
        return desde.Date == hasta.Date
            ? $"{desde.ToString("dd/MM/yyyy HH:mm", c)} a {hasta.ToString("HH:mm", c)}"
            : $"{desde.ToString("dd/MM/yyyy HH:mm", c)} a {hasta.ToString("dd/MM/yyyy HH:mm", c)}";
    }
}
