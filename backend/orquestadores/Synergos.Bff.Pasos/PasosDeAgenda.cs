using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.Bff.Pasos;

// ── Los pasos sobre la agenda (Api.Booking), núcleo N1 ──────────────────────
// Los de la cita (una franja por saga, llave FIJA) y los de varios apartados (por ítem). Dos tipos y
// no uno, porque un tipo de paso declara qué llave usa y el validador lo cruza: una franja por saga
// llama con `{saga}|{llave}`, igual que el flujo imperativo de Salud (se puede comparar llamada a
// llamada), y varios apartados no caben en una llave fija.

/// <summary><c>booking.hallar</c>: lee el sujeto de la agenda y escribe el identificador del recurso.</summary>
/// <remarks>El identificador lo genera la capacidad: se pregunta por el sujeto (el profesional, la sala), no se adivina.</remarks>
public sealed class PasoAgendaHallar(IBookingPort agenda) : IPaso
{
    public string Tipo => "booking.hallar";
    public int Lecturas => 1;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string)];

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var recurso = await agenda.HallarAsync(entrada.Lee<Ref>(0), ct);
        return recurso.IsOk ? SalidaDePaso.Sigue(recurso.Value) : SalidaDePaso.Rechaza(recurso.Rejection!);
    }
}

/// <summary>
/// <c>booking.apartar</c>: lee el recurso, la ventana y para quién; aparta y escribe el apartado y —si la
/// definición lo nombra— cuándo vence (<c>instante</c>). RESERVA, una vez por saga, con llave FIJA.
/// </summary>
/// <remarks>
/// Lo reservado se deshace soltando el apartado (<c>booking.liberar</c>); una vez confirmado, cancelando la
/// reserva que devuelve confirmar (<c>booking.cancelar</c>), si la definición lo declara.
/// </remarks>
public sealed class PasoAgendaApartar(IBookingPort agenda) : IPaso
{
    public string Tipo => "booking.apartar";
    public int Lecturas => 3;
    public int Escrituras => 2;
    public int EscriturasOpcionales => 1;
    public LlaveRequerida Llave => LlaveRequerida.Fija;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string), typeof(DateTimeOffset)];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        return PasosDeAgenda.ApartarAsync(agenda, entrada, entrada.Llave(), ct);
    }
}

/// <summary>
/// <c>booking.apartar-por-item</c>: el mismo apartado, una vez por ítem de un bloque, con llave por ítem.
/// </summary>
/// <remarks>
/// <para><b>El discriminante es el recurso y la ventana, resumidos</b> (32 hexadecimales del SHA-256): la
/// posición no sirve (reordenar dos ítems entre dos intentos apartaría dos veces) y el recurso solo
/// tampoco (dos noches distintas del mismo cuarto). <c>hold:</c> + 32 caben en los 40 que admite
/// <see cref="Synergos.Bff.Core.LlaveDeSaga.PasoMaximo"/>.</para>
///
/// <para><b>Dos ítems idénticos (mismo recurso y misma ventana) comparten llave</b>: el segundo apartado
/// devuelve el primero. Un dominio que los admita (dos cuartos iguales la misma noche) tiene que
/// distinguirlos antes, como Eventos rechaza la misma butaca dos veces.</para>
/// </remarks>
public sealed class PasoAgendaApartarPorItem(IBookingPort agenda) : IPaso
{
    public string Tipo => "booking.apartar-por-item";
    public int Lecturas => 3;
    public int Escrituras => 2;
    public int EscriturasOpcionales => 1;
    public LlaveRequerida Llave => LlaveRequerida.PorItem;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string), typeof(DateTimeOffset)];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        return PasosDeAgenda.ApartarAsync(agenda, entrada,
            entrada.LlavePara(PasosDeAgenda.Discriminante(entrada.Lee<string>(0), entrada.Lee<TimeWindow>(1))), ct);
    }
}

/// <summary>
/// <c>booking.confirmar</c>: lee el apartado y lo confirma. CIERRA la reserva y produce la reserva
/// confirmada, que es sobre lo que se cancela después. Llave FIJA: una franja por saga.
/// </summary>
public sealed class PasoAgendaConfirmar(IBookingPort agenda) : IPaso
{
    public string Tipo => "booking.confirmar";
    public int Lecturas => 1;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Fija;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string)];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        return PasosDeAgenda.ConfirmarAsync(agenda, entrada, entrada.Llave(), ct);
    }
}

/// <summary>
/// <c>booking.confirmar-por-item</c>: el mismo cierre dentro de un bloque <c>reservas:&lt;paso&gt;</c>, con
/// llave por ítem (el apartado, un Guid «n» de 32: <c>confirm:</c> + 32 = 40).
/// </summary>
public sealed class PasoAgendaConfirmarPorItem(IBookingPort agenda) : IPaso
{
    public string Tipo => "booking.confirmar-por-item";
    public int Lecturas => 1;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.PorItem;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string)];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        return PasosDeAgenda.ConfirmarAsync(agenda, entrada, entrada.LlavePara(entrada.Lee<string>(0)), ct);
    }
}

/// <summary>
/// <c>booking.apartar-por-indice</c>: el apartado de un ítem de un bloque, con la llave POR POSICIÓN
/// (<c>{saga}|hold:{i}</c>), la que arma <c>TripFlow</c> (núcleo N2, NV-1 de Viajes).
/// </summary>
/// <remarks>
/// <para><b>Para la paridad entre motores</b>: con el interruptor declarado/imperativo, un viaje abierto por un
/// motor puede confirmarse por el otro, y <c>Api.Booking</c> sólo devuelve el mismo apartado ante la MISMA
/// llave.</para>
///
/// <para><b>Por posición es seguro donde abrir corre una sola vez por saga</b> (un intento nuevo es otra saga,
/// con otras llaves) y el dominio rechaza dos ítems iguales. Dos ítems idénticos tienen aquí llaves
/// distintas, que es justo lo que <c>booking.apartar-por-item</c> no da (NN-3 de Alquiler).</para>
/// </remarks>
public sealed class PasoAgendaApartarPorIndice(IBookingPort agenda) : IPaso
{
    public string Tipo => "booking.apartar-por-indice";
    public int Lecturas => 3;
    public int Escrituras => 2;
    public int EscriturasOpcionales => 1;
    public LlaveRequerida Llave => LlaveRequerida.PorItem;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string), typeof(DateTimeOffset)];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        return PasosDeAgenda.ApartarAsync(agenda, entrada, entrada.LlavePara(PasosDeAgenda.Posicion(entrada)), ct);
    }
}

/// <summary>
/// <c>booking.confirmar-por-indice</c>: el cierre de un bloque <c>reservas:&lt;paso&gt;</c> con la llave POR
/// POSICIÓN (<c>{saga}|confirm:{i}</c>), la de <c>TripFlow</c> (NV-1 de Viajes).
/// </summary>
public sealed class PasoAgendaConfirmarPorIndice(IBookingPort agenda) : IPaso
{
    public string Tipo => "booking.confirmar-por-indice";
    public int Lecturas => 1;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.PorItem;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string)];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        return PasosDeAgenda.ConfirmarAsync(agenda, entrada, entrada.LlavePara(PasosDeAgenda.Posicion(entrada)), ct);
    }
}

/// <summary>
/// <c>booking.liberar</c>: lee el apartado y lo suelta. CIERRA la reserva SIN consumirla: una fase que
/// abandona la franja a propósito (la definición no le declara <c>despues</c>).
/// </summary>
public sealed class PasoAgendaLiberar(IDeshacerAgenda agenda) : IPaso
{
    public string Tipo => "booking.liberar";
    public int Lecturas => 1;
    public int Escrituras => 0;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [];

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var suelto = await agenda.LiberarAsync(entrada.Lee<string>(0), ct);
        return suelto.IsOk ? SalidaDePaso.Sigue() : SalidaDePaso.Rechaza(suelto.Rejection!);
    }
}

/// <summary>
/// <c>booking.cancelar</c>: lee una reserva CONFIRMADA y la cancela (núcleo, T071 del lote 6; NB-4 de 011). Es el paso
/// de la operación posterior que cancela lo confirmado.
/// </summary>
/// <remarks>
/// El plazo lo decide la agenda con la política del recurso (<c>CheckCancellable</c>): fuera de plazo vuelve su
/// <c>booking.cancellation_too_late</c> tal cual, y una ya cancelada, <c>booking.already_cancelled</c>.
/// </remarks>
public sealed class PasoAgendaCancelar(IDeshacerAgenda agenda) : IPaso
{
    public string Tipo => "booking.cancelar";
    public int Lecturas => 1;
    public int Escrituras => 0;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [];

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var cancelada = await agenda.CancelarAsync(entrada.Lee<string>(0), ct);
        return cancelada.IsOk ? SalidaDePaso.Sigue() : SalidaDePaso.Rechaza(cancelada.Rejection!);
    }
}

/// <summary>
/// <c>booking.franjas</c>: lee el recurso, desde y hasta (<c>instante</c>), y escribe sus franjas con lo libre de cada
/// una (<c>franjas</c>; núcleo, NB-3 de 011, sobre NB-2). El paso de una lectura de disponibilidad.
/// </summary>
/// <remarks>
/// Lee, no aparta: sin llave y sin reserva, que es lo que deja correrlo en una lectura sin saga y sin dueño. Que
/// la franja siga libre al apartarla no lo promete nadie: lo dice apartar, con su regla y en su momento.
/// </remarks>
public sealed class PasoAgendaFranjas(IBookingPort agenda) : IPaso
{
    public string Tipo => "booking.franjas";
    public int Lecturas => 3;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(IReadOnlyList<FranjaLibre>)];

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var franjas = await agenda.FranjasAsync(entrada.Lee<string>(0), entrada.Lee<DateTimeOffset>(1), entrada.Lee<DateTimeOffset>(2), ct);
        return franjas.IsOk ? SalidaDePaso.Sigue(franjas.Value) : SalidaDePaso.Rechaza(franjas.Rejection!);
    }
}

/// <summary>Lo que comparten los pasos de agenda.</summary>
public static class PasosDeAgenda
{
    /// <summary>El discriminante de un apartado por ítem: 32 hexadecimales del recurso y la ventana.</summary>
    public static string Discriminante(string recurso, TimeWindow ventana)
    {
        var texto = string.Create(CultureInfo.InvariantCulture, $"{recurso}|{ventana.Start.UtcDateTime:O}|{ventana.End.UtcDateTime:O}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(texto)))[..32];
    }

    /// <summary>La posición del ítem como discriminante; un paso por índice fuera de un bloque es un defecto de la definición.</summary>
    internal static string Posicion(EntradaDePaso entrada)
        => entrada.Indice is { } i
            ? i.ToString(CultureInfo.InvariantCulture)
            : throw new InvalidOperationException($"El paso «{entrada.Paso.Id}» ({entrada.Paso.Tipo}) va por posición y no está dentro de un bloque.");

    /// <remarks>
    /// Escribe el apartado y, si la definición lo nombra, cuándo vence (NB-5 de 011, NV-12 de 006): la pantalla
    /// sólo puede decir cuánto tiempo queda si lo dice el servidor.
    /// </remarks>
    internal static async Task<SalidaDePaso> ApartarAsync(IBookingPort agenda, EntradaDePaso entrada, IdempotencyKey llave, CancellationToken ct)
    {
        var apartado = await agenda.ApartarAsync(entrada.Lee<string>(0), entrada.Lee<TimeWindow>(1), entrada.Lee<Ref>(2), llave, ct);
        return apartado.IsOk
            ? SalidaDePaso.Reserva(new Reservado(apartado.Value.Id), apartado.Value.Id, apartado.Value.Vence)
            : SalidaDePaso.Rechaza(apartado.Rejection!);
    }

    internal static async Task<SalidaDePaso> ConfirmarAsync(IBookingPort agenda, EntradaDePaso entrada, IdempotencyKey llave, CancellationToken ct)
    {
        var reserva = await agenda.ConfirmarAsync(entrada.Lee<string>(0), llave, ct);
        return reserva.IsOk ? SalidaDePaso.Cierra(reserva.Value, reserva.Value) : SalidaDePaso.Rechaza(reserva.Rejection!);
    }
}
