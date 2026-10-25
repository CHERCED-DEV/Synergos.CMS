namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Contra qué avanza un expediente (HU #44) — sección <c>Synergos:Gob</c>.
/// </summary>
/// <remarks>
/// <para><b>Dice <c>Api</c> y no <c>Bff</c>, y no es un descuido</b> — igual que la visita al
/// inmueble (#33a) y al revés que la compra de una entrada (#35). La pregunta no es cuántas
/// capacidades toca: es si hay algo que <b>deshacer</b> cuando un segundo paso falla. Decidir
/// sobre un expediente es UN paso, sin plata en medio. Meter un orquestador sería una saga de un
/// paso: la máquina de compensar sin nada que compensar.</para>
///
/// <para><b>La tasa del trámite sí necesita orquestador</b>, y es otro ticket: radicar-con-cobro
/// son dos pasos con dinero en medio. Esta sección no lo toca.</para>
///
/// <para><b>El default es la tabla en proceso, y no es una transición.</b> Un clon limpio decide
/// expedientes sin levantar nada. Con <c>Api</c> la legalidad la resuelve <c>Api.Workflow</c>, que
/// tiene las transiciones como DATO: cambiar el proceso de un trámite deja de ser un despliegue
/// del CMS.</para>
///
/// <para><b>Con la capacidad caída, la cara de funcionario degrada y lo DICE.</b> No cae a la
/// tabla local en silencio: eso convertiría una caída en decisiones tomadas con un proceso que
/// quizá ya no es el vigente, y nadie se enteraría. Es el mismo criterio que la HU #27 aplicó a
/// los cobros — lo que no existe es el stub sirviendo sin avisar.</para>
/// </remarks>
public sealed class GobSettings
{
    /// <summary>
    /// <c>Stub</c> (default, la tabla en proceso) o <c>Api</c> (contra <c>Synergos.Api.Workflow</c>).
    /// </summary>
    public string Mode { get; init; } = "Stub";

    /// <summary>Dónde vive la capacidad.</summary>
    public string BaseUrl { get; init; } = "http://127.0.0.1:5215/";

    /// <summary>La llave compartida servicio↔servicio. Sin ella todo responde 401.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// La clave de la definición con que ARRANCAN los expedientes nuevos en <c>Api.Workflow</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Configurable porque versionar una definición ES publicar otra clave.</b> La capacidad
    /// se niega a reescribir una definición viva (<c>workflow.key_taken</c>): cambiarle las
    /// transiciones a instancias en marcha las dejaría en estados imposibles. El proceso cambió de
    /// verdad el 2026-10-16 (009-gobierno-tramite: tomar antes de decidir y la subsanación que vuelve
    /// a revisión), así que el default es <c>gov.tramite.v2</c>, la de
    /// <c>tools/provisionar.procesos.json</c>.</para>
    ///
    /// <para><b>Sólo decide con qué arranca un expediente.</b> Uno que ya tiene instancia sigue con la
    /// definición de SU instancia (la v1 los que nacieron con ella), y un despliegue que fije aquí
    /// <c>gov.tramite</c> sigue arrancando con la v1.</para>
    /// </remarks>
    public string DefinitionKey { get; init; } = "gov.tramite.v2";

    /// <summary>
    /// El <c>Kind</c> con el que este vertical nombra al expediente.
    /// </summary>
    /// <remarks>
    /// Viaja opaco: la capacidad lo guarda y lo devuelve, y no ramifica sobre él
    /// (<c>CLAUDE.md</c> §13).
    /// </remarks>
    public string CaseKind { get; init; } = "gov.expediente";

    /// <summary>
    /// Segundos de espera. Corto a propósito, al revés que en los flujos con plata.
    /// </summary>
    /// <remarks>
    /// Acá un timeout no deja nada a medias que cueste dinero: la decisión se aplica de este lado
    /// <b>después</b> de que la capacidad la declaró legal, así que agotar el plazo significa «no
    /// se decidió», y el funcionario vuelve a intentar sobre un expediente intacto.
    /// </remarks>
    public int TimeoutSeconds { get; init; } = 10;
}
