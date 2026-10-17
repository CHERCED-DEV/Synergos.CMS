using System.Collections.Concurrent;

namespace Synergos.CMS.Web.Services.Puerta;

/// <summary>
/// El freno de lo que la puerta abre a invitados (núcleo, NB-3 de 011): cuántas veces por minuto se pide una
/// operación desde una misma IP, o por un mismo miembro.
/// </summary>
/// <remarks>
/// <para><b>Existe porque abrir sin sesión es abrir sin nombre.</b> Una lectura de disponibilidad no aparta ni cobra,
/// pero cada una cuesta una pregunta a la agenda; sin freno, cualquiera la convertiría en un bucle contra el
/// orquestador. Lo que pide un miembro con sesión ya tiene dueño; esto es para lo que no.</para>
///
/// <para><b>Ventana deslizante de un minuto, en memoria</b>, como el freno de los formularios
/// (<see cref="InMemoryFormRateLimiter"/>): se reinicia con el proceso y no se comparte entre réplicas. Es el freno
/// razonable para un despliegue de una sola instancia, que es el de hoy, y el primero que habría que cambiar con
/// dos.</para>
///
/// <para><b>Un singleton</b>, y por eso la puerta no lo crea: un freno por petición no frenaría nada. Si no está
/// registrado, la puerta no abre nada a invitados (falla cerrada).</para>
/// </remarks>
public sealed class FrenoDeLaPuerta
{
    private static readonly TimeSpan Ventana = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _reloj;
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _pedidos = new(StringComparer.Ordinal);

    public FrenoDeLaPuerta(TimeProvider? reloj = null) => _reloj = reloj ?? TimeProvider.System;

    /// <summary>Si <paramref name="quien"/> puede pedir otra vez la operación; si sí, cuenta este pedido.</summary>
    /// <param name="flujo">La clave del flujo.</param>
    /// <param name="operacion">La operación de la puerta.</param>
    /// <param name="quien">Quién pide: <c>ip:…</c> o <c>miembro:…</c>.</param>
    /// <param name="porMinuto">Cuántas admite en un minuto.</param>
    /// <param name="reintentarEn">Si no, cuánto falta para que vuelva a poder.</param>
    public bool Permitir(string flujo, string operacion, string quien, int porMinuto, out TimeSpan reintentarEn)
    {
        var ahora = _reloj.GetUtcNow();
        var cola = _pedidos.GetOrAdd($"{flujo}|{operacion}|{quien}", _ => new Queue<DateTimeOffset>());
        lock (cola)
        {
            while (cola.Count > 0 && ahora - cola.Peek() >= Ventana) cola.Dequeue();
            if (cola.Count >= porMinuto)
            {
                reintentarEn = cola.Peek() + Ventana - ahora;
                return false;
            }
            cola.Enqueue(ahora);
            reintentarEn = TimeSpan.Zero;
            return true;
        }
    }
}
