namespace Synergos.CMS.Interfaces;

/// <summary>
/// Qué hace un viaje por la puerta si un ítem no se puede confirmar después de cobrar (línea 006, historia 3): lo decide
/// quien vende, por sitio (<c>Synergos:Features:TravelShell:ConfirmationPolicy</c>), y la puerta la manda a
/// <c>Bff.Viajes</c> en la cabecera de negocio al abrir. El navegador no la pone.
/// </summary>
public static class PoliticaDeConfirmacion
{
    /// <summary>Por defecto: un ítem caído deshace el viaje entero y se devuelve todo.</summary>
    public const string TodoONada = "todo-o-nada";

    /// <summary>Un carrito de viaje: lo que salió se queda, lo caído se suelta y se devuelve su parte.</summary>
    public const string Parcial = "parcial";

    /// <summary>La política sin espacios ni mayúsculas; nula si viene vacía.</summary>
    public static string? Normalizar(string? politica)
        => string.IsNullOrWhiteSpace(politica) ? null : politica.Trim().ToLowerInvariant();

    /// <summary>Si es una de las dos.</summary>
    public static bool EsValida(string? politica) => politica is TodoONada or Parcial;
}
