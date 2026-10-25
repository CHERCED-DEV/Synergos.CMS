using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.Bff.Propiedades.Domain;

// Los pasos de dominio de la visita. NO son del caso común: son interinos y esperan la pieza del núcleo que los
// retira (008-propiedades-visita, research R3). Se cuentan aparte de la meta del flujo (≤ 60 líneas) y se
// atribuyen a su necesidad. La clase fija del inmueble (NR-1) ya no es de aquí: la declara la definición, y el aviso
// tampoco: es el común del núcleo con sus «valores» (T091, NR-3).

/// <summary>Lo que el despliegue fija de la visita: cuánto dura.</summary>
/// <param name="Minutos">La duración de una visita (<c>Propiedades:Visita:Minutos</c>, 60 por defecto).</param>
/// <remarks>La zona del sitio (<c>Propiedades:ZonaHoraria</c>) la lee el aviso común del núcleo, que no deja arrancar con
/// una que no existe.</remarks>
public sealed record AjustesDeLaVisita(int Minutos)
{
    /// <summary>Lee la sección; una duración que no es positiva no deja arrancar.</summary>
    public static AjustesDeLaVisita De(IConfiguration configuracion)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        var minutos = configuracion.GetValue("Propiedades:Visita:Minutos", 60);
        if (minutos <= 0) throw new InvalidOperationException($"Propiedades:Visita:Minutos tiene que ser positivo y es {minutos}.");
        return new AjustesDeLaVisita(minutos);
    }
}

/// <summary>
/// <c>propiedades.validar-visita</c> (interino de NB-1 y NR-4): la ventana dura lo que dura una visita, y la
/// modalidad es una de las dos que hay.
/// </summary>
/// <remarks>
/// <para><b>El largo lo fija el servidor</b> (FR-007): la ventana llega del navegador, y sin esta regla una
/// visita de tres días bloquearía la agenda del agente. Lo retira la duración de franja en la agenda (NB-1).</para>
///
/// <para><b>La modalidad usa el vocabulario de la ruta directa</b> (<c>in-person</c> | <c>video</c>), así el
/// elemento no traduce según el camino. Sin modalidad es presencial, y se ESCRIBE así: la salida no deja la
/// ausencia para que cada lector la interprete. Lo retira un tipo enumerado en la definición (NR-4).</para>
///
/// <para><b>Y la escribe también en palabras</b> (<c>modoEnTexto</c>: «presencial» o «por videollamada»): es lo que el
/// aviso común pone en el correo como un valor declarado (T091). El código sigue siendo el de la salida.</para>
/// </remarks>
public sealed class PasoValidarVisita(int minutos) : IPaso
{
    /// <summary>El código cuando la ventana no dura una visita.</summary>
    public const string DuracionInvalida = "propiedades.duracion_invalida";

    /// <summary>El código cuando la modalidad no es ninguna de las dos.</summary>
    public const string ModalidadInvalida = "propiedades.modalidad_invalida";

    /// <summary>Presencial: el agente recibe en el inmueble.</summary>
    public const string Presencial = "in-person";

    /// <summary>Por videollamada.</summary>
    public const string Video = "video";

    public string Tipo => "propiedades.validar-visita";
    public int Lecturas => 2;
    public int Escrituras => 2;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string), typeof(string)];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var ventana = entrada.Lee<TimeWindow>(0);
        if ((ventana.End - ventana.Start).TotalMinutes != minutos)
        {
            return Task.FromResult(SalidaDePaso.Rechaza(Rejection.Invalid(DuracionInvalida,
                $"Una visita dura {minutos} minutos: elegí una de las franjas de la agenda.")));
        }

        var modalidad = entrada.Lee<string?>(1)?.Trim().ToLowerInvariant();
        return Task.FromResult(modalidad switch
        {
            null or "" or Presencial => SalidaDePaso.Sigue(Presencial, "presencial"),
            Video => SalidaDePaso.Sigue(Video, "por videollamada"),
            _ => SalidaDePaso.Rechaza(Rejection.Invalid(ModalidadInvalida,
                $"La modalidad tiene que ser «{Presencial}» o «{Video}»."))
        });
    }
}
