using System.Collections.Concurrent;
using System.Globalization;
using Synergos.Core;

namespace Synergos.Bff.Tienda.Domain;

/// <summary>
/// Cuántos códigos de promoción que NO sirven probó cada comprador en la ventana, y el tope (004-tienda-compra,
/// US 8, R-12).
/// </summary>
/// <remarks>
/// <para><b>Es una COPIA declarada del freno de <c>Bff.Eventos</c></b> (<c>Synergos.Bff.Eventos.Domain.IntentosDeCodigo</c>):
/// el genérico es la necesidad N-11 del núcleo, y mientras no llegue la tienda lleva el suyo con su código. Cuando
/// el núcleo lo ponga en <c>Bff.Core</c>/<c>Bff.Pasos</c>, éste se va. La diferencia con aquél es sólo de forma:
/// cuenta por el CÓDIGO del rechazo, porque acá lo lee un filtro de la respuesta (<c>CuponDeLaCompra</c>) y no el
/// resultado del dominio.</para>
///
/// <para><b>Por qué existe.</b> «cotizar» no pide llave, no aparta nada y contesta distinto a un código que no
/// existe, a uno vencido y a uno que vale: es lo que quien compra necesita oír, y por eso mismo un miembro podía
/// mandarla en bucle con códigos candidatos y enumerar cuáles existen. «abrir» con un código que no sirve rechaza
/// igual antes de apartar, con una llave que inventa el navegador: el mismo oráculo.</para>
///
/// <para><b>Qué cuenta y qué corta.</b> Cuenta los rechazos de Pricing por el código (<c>pricing.promotion_*</c>)
/// de cada sujeto, en las dos operaciones; un código que vale no cuenta, y una compra sin código ni cuenta ni se
/// corta. Agotado el tope, toda cotización o apertura CON código de ese sujeto contesta <see cref="Codigo"/>
/// —firme (409): la pieza del CMS repite en el acto un transitorio con llave— ANTES de llamar a Pricing.</para>
///
/// <para><b>En memoria y por réplica, a propósito</b>: es un freno contra el bucle de un miembro, no una cuenta
/// de usos; con N réplicas el tope real es N veces el configurado, y un reinicio lo vacía.</para>
/// </remarks>
public sealed class IntentosDeCodigo
{
    /// <summary>El rechazo de quien agotó el tope: firme (409); pasada la ventana se vuelve a pedir.</summary>
    public const string Codigo = "tienda.demasiados_codigos";

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

    /// <summary>Si el código de un rechazo es de Pricing por el código de promoción: lo que cuenta.</summary>
    public static bool EsDelCodigo(string? codigo)
        => codigo is not null && codigo.StartsWith(PrefijoDeLosRechazosDelCodigo, StringComparison.Ordinal);

    /// <summary>
    /// Si <paramref name="sujeto"/> puede probar otro código: <c>null</c> si sí, y el rechazo firme
    /// <see cref="Codigo"/> (409, no transitorio) si ya agotó la ventana.
    /// </summary>
    public Rejection? Revisar(Ref sujeto)
    {
        ArgumentNullException.ThrowIfNull(sujeto);
        if (!_fallos.TryGetValue(sujeto.ToString(), out var fallos)) return null;

        lock (fallos)
        {
            Podar(fallos);
            if (fallos.Count < Tope) return null;
        }

        var minutos = Math.Max(1, (int)Math.Ceiling(Ventana.TotalMinutes));
        return Rejection.Conflict(Codigo, string.Create(CultureInfo.InvariantCulture,
            $"Demasiados códigos de promoción que no sirven en {minutos} min. Se puede volver a probar más tarde, o comprar sin código."));
    }

    /// <summary>Anota el resultado de una cotización o apertura CON código: sólo cuenta si Pricing rechazó el código.</summary>
    /// <param name="sujeto">Quién lo probó.</param>
    /// <param name="codigo">El código del rechazo con que terminó; nulo si salió bien.</param>
    public void Anotar(Ref sujeto, string? codigo)
    {
        ArgumentNullException.ThrowIfNull(sujeto);
        if (!EsDelCodigo(codigo)) return;

        var fallos = _fallos.GetOrAdd(sujeto.ToString(), _ => new Queue<DateTimeOffset>());
        lock (fallos)
        {
            Podar(fallos);
            fallos.Enqueue(_reloj.GetUtcNow());
            // Nunca más de lo que hace falta para decidir: la cola no crece con el bucle.
            while (fallos.Count > Tope) fallos.Dequeue();
        }
    }

    private void Podar(Queue<DateTimeOffset> fallos)
    {
        var desde = _reloj.GetUtcNow() - Ventana;
        while (fallos.Count > 0 && fallos.Peek() <= desde) fallos.Dequeue();
    }
}
