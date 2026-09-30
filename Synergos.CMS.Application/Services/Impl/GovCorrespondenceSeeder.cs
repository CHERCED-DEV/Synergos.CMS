using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Siembra la correspondencia de demo del vertical Gobierno sobre el seam GENÉRICO
/// <see cref="IMessagingService"/> (contexto <c>gov</c>, <c>contextRef</c> = número de
/// radicado del expediente). Es la "mensajería con la entidad" que la carpeta del
/// ciudadano muestra en el detalle del expediente. Lógica pura (ADR 0002). La invoca
/// <c>POST /dev/seed-gov-correspondence</c>, detrás de <c>Synergos:DevSeed:Enabled</c>, y
/// NADA más: ni un hosted service ni el arranque (ADR 0013, #176).
/// </summary>
/// <remarks>
/// <para><b>Idempotente por expediente, y lo es DE VERDAD desde el #176.</b> Decía «el
/// ThreadId es determinista, así que re-ejecutar agrega al mismo hilo — por eso solo se
/// siembra una vez», y las dos mitades se contradecían: agregar al mismo hilo ES duplicar
/// (<see cref="IMessagingService.StartThreadAsync"/> es idempotente en el hilo, no en el
/// mensaje), y «una vez» sólo era cierto mientras la mensajería vivió en memoria. Con el
/// almacén durable (ADR 0105) y un hosted service llamándola en cada arranque, la carpeta
/// de los dos ciudadanos crecía tres mensajes por reinicio. Hoy un expediente que ya tiene
/// hilo con el ciudadano no se toca: ni se duplica la demo ni se le cuelga encima a una
/// conversación que ya existe.</para>
/// </remarks>
public static class GovCorrespondenceSeeder
{
    /// <summary>Actor de la entidad (funcionario) que responde la correspondencia.</summary>
    public const string OfficerParticipant = "funcionario@entidad.gov.co";

    /// <summary>Contexto del seam de mensajería para el dominio Gobierno.</summary>
    public const string Context = "gov";

    /// <summary>Nº de hilos que siembra una base vacía.</summary>
    public const int ThreadCount = 2;

    /// <summary>
    /// Un hilo de demo: el expediente, el ciudadano y la conversación en orden. El primer
    /// mensaje lo escribe siempre la entidad; <c>DeLaEntidad</c> dice quién escribe cada uno.
    /// </summary>
    private sealed record HiloDeDemo(
        string Radicado,
        string Ciudadano,
        IReadOnlyList<(bool DeLaEntidad, string Texto)> Mensajes);

    private static readonly HiloDeDemo[] Hilos =
    [
        // Expediente en subsanación (case-1003 / SG-2026-001003) — Juliana Ríos:
        // la entidad pide subsanar y la ciudadana responde.
        new("SG-2026-001003", "juliana.rios@correo.co",
        [
            (true, "Su foto no cumple el requisito de fondo blanco. Por favor adjunte una nueva para continuar."),
            (false, "De acuerdo, adjunté una foto nueva con fondo blanco. Quedo atenta."),
        ]),

        // Expediente resuelto (case-1004 / SG-2026-001004) — Andrés Torres: aviso de
        // resolución favorable.
        new("SG-2026-001004", "andres.torres@correo.co",
        [
            (true, "Su registro mercantil fue expedido. Puede descargar el certificado desde su carpeta."),
        ]),
    ];

    /// <summary>Siembra la correspondencia de los expedientes que todavía no la tienen.</summary>
    /// <returns>Cuántos hilos se crearon en ESTA llamada: 0 si ya estaban todos.</returns>
    public static async Task<int> SeedAsync(IMessagingService messaging, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messaging);

        var creados = 0;
        foreach (var hilo in Hilos)
        {
            var contexto = $"{Context}:{hilo.Radicado}";

            // Se pregunta por la bandeja y no se recalcula el ThreadId: el identificador es un
            // detalle del almacén de mensajería, y copiar su fórmula acá era una segunda
            // verdad sobre algo que no es de este fichero.
            var bandeja = await messaging.GetInboxAsync(hilo.Ciudadano, cancellationToken).ConfigureAwait(false);
            if (bandeja.Any(t => string.Equals(t.ContextRef, contexto, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var primero = hilo.Mensajes[0];
            var estado = await messaging.StartThreadAsync(
                contexto, OfficerParticipant, hilo.Ciudadano, primero.Texto, cancellationToken).ConfigureAwait(false);

            foreach (var (deLaEntidad, texto) in hilo.Mensajes.Skip(1))
            {
                await messaging.ReplyAsync(
                    estado.ThreadId,
                    deLaEntidad ? OfficerParticipant : hilo.Ciudadano,
                    texto,
                    cancellationToken).ConfigureAwait(false);
            }

            creados++;
        }

        return creados;
    }
}
