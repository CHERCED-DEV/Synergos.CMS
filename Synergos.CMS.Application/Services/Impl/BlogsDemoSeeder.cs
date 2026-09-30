using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Siembra la data de demo de Blogs (OLA 6) que vive en seams GENÉRICOS
/// compartidos con otros dominios: los hilos de DM (<see cref="IMessagingService"/>
/// contexto <c>dm</c>) y los ítems guardados (<see cref="IUserCollection"/>
/// colección <c>saved</c>). Vive en Application (lógica pura, ADR 0002) para que
/// pueda leer la semilla interna <see cref="SocialDemoSeed"/>. La invoca
/// <c>POST /dev/seed-blogs-demo</c>, detrás de <c>Synergos:DevSeed:Enabled</c>, y NADA
/// más: ni un hosted service ni el arranque (ADR 0013, #176).
/// </summary>
/// <remarks>
/// <para>Idempotente. <see cref="IUserCollection.AddAsync"/> lo es por (owner +
/// colección + ítem), así que los guardados se re-siembran sin duplicar.
/// <see cref="IMessagingService.StartThreadAsync"/>, en cambio, es idempotente
/// en el HILO pero NO en el MENSAJE: siempre agrega el cuerpo que recibe.
/// Mientras la mensajería vivía en memoria eso daba igual —cada arranque partía
/// de cero—, pero con el store durable (ADR 0105) re-sembrar volvería a
/// APPENDear los mismos mensajes en cada reinicio, creciendo sin límite. Por eso
/// la siembra de DMs verifica antes si el hilo ya existe con mensajes.</para>
/// <para><b>Escribe en el almacén durable, y por eso no corre al arrancar.</b> Hasta el
/// #176 lo llamaba un hosted service en CADA arranque y en todo entorno, con un
/// <c>remarks</c> que decía que «solo hidrataba stubs en memoria»: era verdad antes de la
/// ADR 0105 y dejó de serlo sin que nadie lo releyera. La mensajería y las colecciones
/// son seams GENÉRICOS que comparten varios verticales, así que sembrar en producción es
/// meter conversaciones y guardados inventados en los almacenes reales.</para>
/// </remarks>
public static class BlogsDemoSeeder
{
    /// <summary>Cantidad de hilos de DM sembrados (para logging del host).</summary>
    public static int DmThreadCount => SocialDemoSeed.DmThreads.Count;

    /// <summary>Cantidad de actores con guardados sembrados (para logging del host).</summary>
    public static int SavedOwnerCount => SocialDemoSeed.Saved.Count;

    /// <summary>
    /// Siembra los hilos de DM y los guardados sobre los seams provistos.
    /// </summary>
    /// <returns>Cuántos hilos de DM se crearon en ESTA llamada: 0 si ya estaban.</returns>
    public static async Task<int> SeedAsync(
        IMessagingService messaging,
        IUserCollection collections,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messaging);
        ArgumentNullException.ThrowIfNull(collections);

        var creados = 0;

        // DMs: cada hilo sembrado → StartThread (primer mensaje) + Reply (resto).
        foreach (var thread in SocialDemoSeed.DmThreads)
        {
            if (thread.Messages.Count == 0)
            {
                continue;
            }

            var participants = thread.Messages
                .Select(m => m.From)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (participants.Count < 2)
            {
                continue; // un hilo de DM necesita 2 participantes distintos.
            }

            var first = thread.Messages[0];
            var to = participants.First(p =>
                !string.Equals(p, first.From, StringComparison.OrdinalIgnoreCase));

            // Con la mensajería durable, el hilo sobrevive el reinicio: volver a
            // sembrarlo APPENDearía los mismos mensajes otra vez (StartThreadAsync
            // agrega, no deduplica). Si ya está sembrado, no se re-siembra.
            var inbox = await messaging.GetInboxAsync(first.From, cancellationToken);
            if (inbox.Any(t =>
                    string.Equals(t.ContextRef, SocialDemoSeed.DmContext, StringComparison.Ordinal)
                    && t.Participants.Any(p => string.Equals(p, to, StringComparison.OrdinalIgnoreCase))))
            {
                continue;
            }

            var state = await messaging.StartThreadAsync(
                SocialDemoSeed.DmContext, first.From, to, first.Body, cancellationToken);

            for (var i = 1; i < thread.Messages.Count; i++)
            {
                var msg = thread.Messages[i];
                await messaging.ReplyAsync(state.ThreadId, msg.From, msg.Body, cancellationToken);
            }

            creados++;
        }

        // Guardados: owner → [postId] en la colección "saved".
        foreach (var (owner, posts) in SocialDemoSeed.Saved)
        {
            foreach (var postId in posts)
            {
                await collections.AddAsync(
                    owner, SocialDemoSeed.SavedCollection, postId, cancellationToken);
            }
        }

        return creados;
    }
}
