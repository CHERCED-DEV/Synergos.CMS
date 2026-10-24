using System.Globalization;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.Core;

namespace Synergos.Bff.Propiedades.Domain;

// Los pasos de dominio de la visita. NO son del caso común: son interinos y esperan la pieza del núcleo que los
// retira (008-propiedades-visita, research R3). Se cuentan aparte de la meta del flujo (≤ 60 líneas) y se
// atribuyen a su necesidad. La clase fija del inmueble (NR-1) ya no es de aquí: la declara la definición.

/// <summary>Lo que el despliegue fija de la visita: cuánto dura y en qué huso se leen sus horas.</summary>
/// <param name="Minutos">La duración de una visita (<c>Propiedades:Visita:Minutos</c>, 60 por defecto).</param>
/// <param name="Zona">La zona del sitio (<c>Propiedades:ZonaHoraria</c>, <c>America/Bogota</c> por defecto).</param>
public sealed record AjustesDeLaVisita(int Minutos, TimeZoneInfo Zona)
{
    /// <summary>Lee la sección; una duración que no es positiva o una zona que no existe no dejan arrancar.</summary>
    public static AjustesDeLaVisita De(IConfiguration configuracion)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        var minutos = configuracion.GetValue("Propiedades:Visita:Minutos", 60);
        if (minutos <= 0) throw new InvalidOperationException($"Propiedades:Visita:Minutos tiene que ser positivo y es {minutos}.");
        var zona = configuracion["Propiedades:ZonaHoraria"] is { Length: > 0 } id ? id : "America/Bogota";
        return new AjustesDeLaVisita(minutos, TimeZoneInfo.FindSystemTimeZoneById(zona));
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
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string)];

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
            null or "" or Presencial => SalidaDePaso.Sigue(Presencial),
            Video => SalidaDePaso.Sigue(Video),
            _ => SalidaDePaso.Rechaza(Rejection.Invalid(ModalidadInvalida,
                $"La modalidad tiene que ser «{Presencial}» o «{Video}»."))
        });
    }
}

/// <summary>
/// <c>propiedades.avisar-visita</c> (interino de NR-3): el aviso de la visita confirmada, SIN importe.
/// </summary>
/// <remarks>
/// <para><b>Por qué no es <c>notifications.avisar</c></b>: el común lee un total en dinero, y la visita no se
/// cobra. Escribir un cero para cumplir la firma afirmaría un precio que nadie fijó (principio VI).</para>
///
/// <para><b>Los valores son fijos</b> —<c>nombre</c>, <c>numero</c>, <c>fecha</c>, <c>hora</c>,
/// <c>modalidad</c>, <c>enlace</c>, <c>sitio</c>— y la hora sale en la del sitio: la visita de las 9 de Bogotá es
/// «09:00» en el correo. El título del inmueble no va: el orquestador no lo conoce y tomarlo del navegador
/// pondría texto ajeno en un correo; el enlace lleva a la visita con su inmueble.</para>
///
/// <para><b>El puerto llega diferido</b> (<paramref name="avisos"/>): un paso de dominio se construye antes que
/// el contenedor de servicios. Sin dirección no avisa, y sigue; la llave es fija por saga, así que un cierre
/// reintentado no manda dos correos.</para>
/// </remarks>
public sealed class PasoAvisarVisita(Func<INotificationsPort> avisos, TimeZoneInfo zona) : IPaso
{
    public string Tipo => "propiedades.avisar-visita";
    public IReadOnlyList<Type> TiposDeEscritura => [];
    public int Lecturas => 4;
    public int Escrituras => 0;
    public LlaveRequerida Llave => LlaveRequerida.Fija;
    public bool UsaPlantilla => true;

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var contacto = entrada.Lee<Contacto?>(1);
        if (contacto is null || string.IsNullOrWhiteSpace(contacto.Correo)) return SalidaDePaso.Sigue();

        var avisado = await avisos().AvisarAsync(
            entrada.Lee<Ref>(0), contacto.Correo, entrada.Paso.Plantilla!,
            Valores(entrada.SagaId, contacto, entrada.Lee<TimeWindow>(2), entrada.Lee<string>(3), zona), entrada.Llave(), ct);

        return avisado.IsOk ? SalidaDePaso.Sigue() : SalidaDePaso.Rechaza(avisado.Rejection!);
    }

    /// <summary>Los valores fijos del aviso de la visita.</summary>
    public static IReadOnlyDictionary<string, string> Valores(
        string sagaId, Contacto contacto, TimeWindow ventana, string modo, TimeZoneInfo zona)
    {
        ArgumentNullException.ThrowIfNull(contacto);
        ArgumentNullException.ThrowIfNull(sagaId);
        var alfanumerico = new string(sagaId.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        var local = TimeZoneInfo.ConvertTime(ventana.Start, zona);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["nombre"] = contacto.Nombre ?? string.Empty,
            ["numero"] = alfanumerico.Length <= 8 ? alfanumerico : alfanumerico[^8..],
            ["fecha"] = local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            ["hora"] = local.ToString("HH:mm", CultureInfo.InvariantCulture),
            ["modalidad"] = modo == PasoValidarVisita.Video ? "por videollamada" : "presencial",
            ["enlace"] = contacto.Enlace ?? string.Empty,
            ["sitio"] = contacto.Sitio ?? string.Empty,
        };
    }
}
