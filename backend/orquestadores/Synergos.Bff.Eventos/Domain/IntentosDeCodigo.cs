using System.Collections.Concurrent;
using System.Globalization;
using Synergos.Core;

namespace Synergos.Bff.Eventos.Domain;

/// <summary>
/// Cuántos códigos de promoción que NO sirven probó cada comprador en la ventana, y el tope (ADR 0140,
/// el cupón endurecido).
/// </summary>
/// <remarks>
/// <para><b>Por qué existe.</b> «cotizar» es la vista previa del cupón: no pide llave, no aparta nada
/// y contesta distinto a un código que no existe (404 <c>pricing.promotion_not_found</c>), a uno
/// vencido (410 <c>pricing.promotion_expired</c>) y a uno que vale (200 con su descuento). Es lo que
/// quien compra necesita oír, y por eso mismo un miembro podía mandarla en bucle con códigos
/// candidatos y enumerar cuáles existen y cuánto descuentan. «abrir» con un código que no sirve
/// rechaza igual antes de apartar, con una llave que inventa el navegador: el mismo oráculo.</para>
///
/// <para><b>Qué cuenta y qué corta.</b> Cuenta los rechazos de Pricing por el código
/// (<c>pricing.promotion_*</c>) de cada sujeto, en las dos operaciones; un código que vale no cuenta,
/// y una compra sin código ni cuenta ni se corta. Agotado el tope, toda cotización o apertura CON
/// código de ese sujeto contesta <see cref="Codigo"/> —firme (409): repetirlo en el acto da lo
/// mismo; pasada la ventana se vuelve a pedir— ANTES de llamar a Pricing, así que tampoco se aparta
/// ni se autoriza nada. Sin código se sigue comprando igual: la semántica de los pagos no cambia
/// (#183).</para>
///
/// <para><b>Firme y no transitorio, a propósito.</b> Un transitorio dice «repetir LO MISMO puede
/// dar otra cosa», y la pieza del CMS que llama por la puerta repite en el acto todo transitorio con
/// llave: el corte salía tres veces seguidas y contaba como caída. Nada cambia en segundos; lo que
/// lo levanta es la ventana.</para>
///
/// <para><b>Una apertura que el orquestador devolvería tal cual no se corta</b>: la misma llave con
/// su saga viva no vuelve a cotizar, así que no es oráculo, y cortarla dejaría a quien ya apartó
/// sin poder reintentar su propia compra.</para>
///
/// <para><b>En memoria y por réplica, a propósito.</b> Es un freno contra el bucle de un miembro, no
/// una cuenta de usos: con N réplicas el tope real es N veces el configurado, y un reinicio lo
/// vacía. Un cupón de un solo uso o por miembro es una regla de <c>Api.Pricing</c> que no existe
/// (queda dicho en la ADR).</para>
/// </remarks>
public sealed class IntentosDeCodigo
{
    /// <summary>El rechazo de quien agotó el tope: firme (409); pasada la ventana se vuelve a pedir.</summary>
    public const string Codigo = "eventos.demasiados_codigos";

    /// <summary>Cuántos códigos que no sirven admite la ventana si el despliegue no dice otra cosa.</summary>
    public const int TopePorDefecto = 10;

    /// <summary>La ventana si el despliegue no dice otra cosa.</summary>
    public static readonly TimeSpan VentanaPorDefecto = TimeSpan.FromMinutes(15);

    private const string PrefijoDeLosRechazosDelCodigo = "pricing.promotion_";

    private readonly TimeProvider _reloj;
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _fallos = new(StringComparer.Ordinal);

    /// <param name="reloj">El reloj del proceso (el mismo que el de las sagas).</param>
    /// <param name="tope">Cuántos códigos que no sirven caben en la ventana, mayor que cero.</param>
    /// <param name="ventana">Cuánto recuerda cada fallo, mayor que cero.</param>
    public IntentosDeCodigo(TimeProvider reloj, int tope, TimeSpan ventana)
    {
        ArgumentNullException.ThrowIfNull(reloj);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(tope, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ventana, TimeSpan.Zero);
        _reloj = reloj;
        Tope = tope;
        Ventana = ventana;
    }

    /// <summary>Cuántos códigos que no sirven caben en la ventana.</summary>
    public int Tope { get; }

    /// <summary>Cuánto recuerda cada fallo.</summary>
    public TimeSpan Ventana { get; }

    /// <summary>Si un rechazo es de Pricing por el código: lo que cuenta.</summary>
    public static bool EsDelCodigo(Rejection? rechazo)
        => rechazo is not null && rechazo.Code.StartsWith(PrefijoDeLosRechazosDelCodigo, StringComparison.Ordinal);

    /// <summary>
    /// Si <paramref name="sujeto"/> puede probar otro código: <c>null</c> si sí, y el rechazo
    /// firme <see cref="Codigo"/> (409, no transitorio) si ya agotó la ventana.
    /// </summary>
    public Rejection? Revisar(Ref sujeto)
    {
        ArgumentNullException.ThrowIfNull(sujeto);
        if (!_fallos.TryGetValue(sujeto.ToString(), out var fallos))
        {
            return null;
        }

        lock (fallos)
        {
            Podar(fallos);
            if (fallos.Count < Tope)
            {
                return null;
            }
        }

        var minutos = Math.Max(1, (int)Math.Ceiling(Ventana.TotalMinutes));
        return Rejection.Conflict(Codigo, string.Create(CultureInfo.InvariantCulture,
            $"Demasiados códigos de promoción que no sirven en {minutos} min. Se puede volver a probar más tarde, o comprar sin código."));
    }

    /// <summary>Anota el resultado de una cotización o apertura CON código: sólo cuenta si Pricing rechazó el código.</summary>
    public void Anotar(Ref sujeto, Rejection? rechazo)
    {
        ArgumentNullException.ThrowIfNull(sujeto);
        if (!EsDelCodigo(rechazo))
        {
            return;
        }

        var fallos = _fallos.GetOrAdd(sujeto.ToString(), _ => new Queue<DateTimeOffset>());
        lock (fallos)
        {
            Podar(fallos);
            fallos.Enqueue(_reloj.GetUtcNow());
            // Nunca más de lo que hace falta para decidir: la cola no crece con el bucle.
            while (fallos.Count > Tope)
            {
                fallos.Dequeue();
            }
        }
    }

    private void Podar(Queue<DateTimeOffset> fallos)
    {
        var desde = _reloj.GetUtcNow() - Ventana;
        while (fallos.Count > 0 && fallos.Peek() <= desde)
        {
            fallos.Dequeue();
        }
    }
}
