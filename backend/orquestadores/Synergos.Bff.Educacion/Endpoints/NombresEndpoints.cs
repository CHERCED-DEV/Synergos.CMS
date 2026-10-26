using Microsoft.AspNetCore.Http.HttpResults;
using Synergos.Bff.Educacion.Contracts;
using Synergos.Bff.Educacion.Domain;
using Synergos.Shared;

namespace Synergos.Bff.Educacion.Endpoints;

/// <summary>
/// Los nombres de un curso y de sus planes: la llama el CMS al publicar la oferta del curso, junto a la oferta común.
/// Sin marca de la puerta: no es del navegador.
/// </summary>
/// <remarks>Interino (NM-3): lo que la oferta común todavía no publica. No cuenta para la meta del flujo.</remarks>
public static class NombresEndpoints
{
    /// <summary>Prefijo de los códigos de rechazo de este orquestador.</summary>
    public const string CodePrefix = "educacion";

    public static WebApplicationBuilder AddNombresDeCursos(this WebApplicationBuilder builder, NombresDeCursos nombres)
    {
        builder.Services.AddSingleton(nombres);
        return builder;
    }

    public static IEndpointRouteBuilder MapNombresDeCursos(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/cursos/{curso}/nombres", Results<Ok<NombresDelCursoResponse>, ProblemHttpResult> (
            string curso, NombresDelCursoRequest req, HttpRequest http, NombresDeCursos nombres) =>
        {
            if (!IdempotencyHeader.TryRead(http, CodePrefix, out _, out var falta)) return falta!;
            return nombres.Guardar(curso, req).Map(Respuesta).ToHttp();
        }).WithName("PublishCourseNames").ConLlaveDeIdempotencia();

        return app;
    }

    private static NombresDelCursoResponse Respuesta(NombresDelCurso n) => new(n.Curso, n.Nombre, n.Planes.Keys.Order(StringComparer.Ordinal).ToList());
}
