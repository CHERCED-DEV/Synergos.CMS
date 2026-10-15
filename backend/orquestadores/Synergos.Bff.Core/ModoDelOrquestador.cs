using Microsoft.Extensions.Configuration;

namespace Synergos.Bff.Core;

/// <summary>Qué corre en un orquestador que conserva su flujo imperativo junto al declarado.</summary>
public enum ModoDelOrquestador
{
    /// <summary>El <c>*Flow.cs</c> de siempre.</summary>
    Imperativo,

    /// <summary>El flujo declarado en <c>flujos/*.json</c>.</summary>
    Declarado,
}

/// <summary>
/// El interruptor declarado/imperativo de un orquestador (núcleo N1, P09; supuesto P4):
/// <c>{Origen}:Flujos:Modo</c>.
/// </summary>
/// <remarks>
/// <para><b>Para los verticales que portan su flujo sin borrar el de antes</b> (superset, principio I): Salud
/// declara <c>salud.cita</c> y conserva <c>AppointmentFlow</c>. Los dos conviven y este interruptor dice cuál
/// corre; lo que no corre queda declarado, no retirado.</para>
///
/// <para><b>El valor por defecto lo pone el orquestador</b> (<c>imperativo</c> mientras exista el imperativo:
/// el despliegue no cambia hasta que alguien lo pida). <b>Una palabra que no es ninguna de las dos no
/// arranca</b>, como el <c>Interruptor</c> del CMS: leerla como el valor por defecto escondería una errata
/// del despliegue detrás de un comportamiento que nadie eligió.</para>
///
/// <para>Lo vigila <c>ModoDelOrquestadorTests</c>: un orquestador con un flujo imperativo y una definición
/// declarada a la vez lee este interruptor.</para>
/// </remarks>
public static class ModosDelOrquestador
{
    /// <summary>La clave, dentro de la sección del orquestador.</summary>
    public const string Clave = "Flujos:Modo";

    /// <summary>El modo del orquestador de <paramref name="vocabulario"/>; <paramref name="porDefecto"/> si no se configura.</summary>
    /// <exception cref="InvalidOperationException">Si la configuración trae otra palabra.</exception>
    public static ModoDelOrquestador Leer(IConfiguration configuracion, SagaVocabulary vocabulario, ModoDelOrquestador porDefecto)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        ArgumentNullException.ThrowIfNull(vocabulario);

        var valor = configuracion[$"{vocabulario.Origin}:{Clave}"];
        if (string.IsNullOrWhiteSpace(valor)) return porDefecto;

        return valor.Trim().ToLowerInvariant() switch
        {
            "imperativo" => ModoDelOrquestador.Imperativo,
            "declarado" => ModoDelOrquestador.Declarado,
            _ => throw new InvalidOperationException(
                $"{vocabulario.Origin}:{Clave} vale «{valor}» y sólo admite «declarado» o «imperativo».")
        };
    }
}
