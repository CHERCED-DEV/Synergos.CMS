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
/// <c>booking.apartar</c>: lee el recurso, la ventana y para quién; aparta y escribe el apartado. RESERVA,
/// una vez por saga, con llave FIJA.
/// </summary>
/// <remarks>
/// Lo reservado se deshace soltando el apartado (<c>booking.liberar</c>); una vez confirmado, cancelando la
/// reserva que devuelve confirmar (<c>booking.cancelar</c>), si la definición lo declara.
/// </remarks>
public sealed class PasoAgendaApartar(IBookingPort agenda) : IPaso
{
    public string Tipo => "booking.apartar";
    public int Lecturas => 3;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Fija;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string)];

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
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.PorItem;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string)];

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

/// <summary>Lo que comparten los pasos de agenda.</summary>
public static class PasosDeAgenda
{
    /// <summary>El discriminante de un apartado por ítem: 32 hexadecimales del recurso y la ventana.</summary>
    public static string Discriminante(string recurso, TimeWindow ventana)
    {
        var texto = string.Create(CultureInfo.InvariantCulture, $"{recurso}|{ventana.Start.UtcDateTime:O}|{ventana.End.UtcDateTime:O}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(texto)))[..32];
    }

    internal static async Task<SalidaDePaso> ApartarAsync(IBookingPort agenda, EntradaDePaso entrada, IdempotencyKey llave, CancellationToken ct)
    {
        var apartado = await agenda.ApartarAsync(entrada.Lee<string>(0), entrada.Lee<TimeWindow>(1), entrada.Lee<Ref>(2), llave, ct);
        return apartado.IsOk
            ? SalidaDePaso.Reserva(new Reservado(apartado.Value), apartado.Value)
            : SalidaDePaso.Rechaza(apartado.Rejection!);
    }

    internal static async Task<SalidaDePaso> ConfirmarAsync(IBookingPort agenda, EntradaDePaso entrada, IdempotencyKey llave, CancellationToken ct)
    {
        var reserva = await agenda.ConfirmarAsync(entrada.Lee<string>(0), llave, ct);
        return reserva.IsOk ? SalidaDePaso.Cierra(reserva.Value, reserva.Value) : SalidaDePaso.Rechaza(reserva.Rejection!);
    }
}
