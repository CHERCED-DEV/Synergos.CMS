namespace Synergos.CMS.Interfaces;

/// <summary>Los valores de negocio de la reserva de servicio ya fusionados para un sitio (ADR 0137, 011-booking-reserva).</summary>
/// <param name="Camino">Lo que el sitio DECLARA (<see cref="CaminoDeReserva.Puerta"/> o <see cref="CaminoDeReserva.Apagada"/>),
/// ya normalizado.</param>
public sealed record NegocioDeReserva(string Camino);

/// <summary>
/// Por dónde reserva el elemento de reserva de servicio: la puerta, o nada (apagada). No hay ruta directa: el CMS
/// no corre flujos nuevos (constitución, principio II) y esta reserva nunca tuvo una ruta propia.
/// </summary>
/// <remarks>
/// <para><b>Lo declarado gana</b>: con <see cref="Apagada"/> el elemento muestra el enlace alternativo del editor y no
/// llama a nada. Con <see cref="Puerta"/>, si la puerta no abre el flujo (no hay orquestador en el despliegue), el
/// elemento queda <see cref="NoDisponible"/>: lo dice, y no cae a otra forma de reservar.</para>
/// </remarks>
public static class CaminoDeReserva
{
    /// <summary>El flujo que el elemento pide a la puerta: el que coloca su vista alrededor.</summary>
    public const string Flujo = "reservas.servicio";

    public const string Puerta = "puerta";
    public const string Apagada = "apagada";

    /// <summary>Lo que resuelve el CMS cuando el sitio declara la puerta y la puerta no abre el flujo.</summary>
    public const string NoDisponible = "no-disponible";

    /// <summary>Si <paramref name="valor"/> es una de las dos palabras que se pueden declarar.</summary>
    public static bool EsValido(string? valor) => Normalizar(valor) is Puerta or Apagada;

    /// <summary>La palabra en minúsculas y sin espacios; sin valor, la puerta (el valor por defecto).</summary>
    public static string Normalizar(string? valor)
        => string.IsNullOrWhiteSpace(valor) ? Puerta : valor.Trim().ToLowerInvariant();

    /// <summary>Lo que llega al elemento: lo declarado si es apagada; si no, la puerta cuando abre el flujo.</summary>
    public static string Decidir(string declarado, bool laPuertaAbre)
        => Normalizar(declarado) == Apagada ? Apagada : laPuertaAbre ? Puerta : NoDisponible;
}
