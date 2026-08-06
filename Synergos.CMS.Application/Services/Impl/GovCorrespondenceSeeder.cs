using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Siembra la correspondencia de demo del vertical Gobierno sobre el seam GENÉRICO
/// <see cref="IMessagingService"/> (contexto <c>gov</c>, <c>contextRef</c> = número de
/// radicado del expediente). Es la "mensajería con la entidad" que la carpeta del
/// ciudadano muestra en el detalle del expediente. Lógica pura (ADR 0002); se invoca
/// desde un hosted service al boot (no en el ctor de la mensajería genérica, que la
/// comparten varios dominios). Idempotente: el ThreadId es determinista por (contexto +
/// par), así que re-ejecutar agrega al mismo hilo — por eso solo se siembra una vez.
/// </summary>
public static class GovCorrespondenceSeeder
{
    /// <summary>Actor de la entidad (funcionario) que responde la correspondencia.</summary>
    public const string OfficerParticipant = "funcionario@entidad.gov.co";

    /// <summary>Contexto del seam de mensajería para el dominio Gobierno.</summary>
    public const string Context = "gov";

    /// <summary>Nº de hilos sembrados (para el log del hosted service).</summary>
    public const int ThreadCount = 2;

    public static async Task SeedAsync(IMessagingService messaging, CancellationToken cancellationToken = default)
    {
        // Expediente en subsanación (case-1003 / SG-2026-001003) — Juliana Ríos:
        // la entidad pide subsanar y la ciudadana responde.
        await messaging.StartThreadAsync(
            $"{Context}:SG-2026-001003",
            OfficerParticipant,
            "juliana.rios@correo.co",
            "Su foto no cumple el requisito de fondo blanco. Por favor adjunte una nueva para continuar.",
            cancellationToken);
        await messaging.ReplyAsync(
            BuildThreadId($"{Context}:SG-2026-001003", OfficerParticipant, "juliana.rios@correo.co"),
            "juliana.rios@correo.co",
            "De acuerdo, adjunté una foto nueva con fondo blanco. Quedo atenta.",
            cancellationToken);

        // Expediente resuelto (case-1004 / SG-2026-001004) — Andrés Torres: aviso de
        // resolución favorable.
        await messaging.StartThreadAsync(
            $"{Context}:SG-2026-001004",
            OfficerParticipant,
            "andres.torres@correo.co",
            "Su registro mercantil fue expedido. Puede descargar el certificado desde su carpeta.",
            cancellationToken);
    }

    // Espejo del ThreadId determinista de StubMessagingService (contexto + par
    // normalizado). Se reproduce aquí SOLO para retomar el mismo hilo dentro del seed
    // (StartThread + Reply); el consumidor real usa GetInboxAsync/GetThreadAsync.
    private static string BuildThreadId(string contextRef, string a, string b)
    {
        var pair = new[] { a.ToLowerInvariant(), b.ToLowerInvariant() }
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
        var seed = $"{contextRef.ToLowerInvariant()}|{pair[0]}|{pair[1]}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        return "thr_" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
