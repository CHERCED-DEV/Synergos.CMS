using Microsoft.AspNetCore.Http.HttpResults;
using Synergos.Bff.Educacion.Clients;
using Synergos.Bff.Educacion.Contracts;
using Synergos.Bff.Educacion.Domain;
using Synergos.Shared;

namespace Synergos.Bff.Educacion.Endpoints;

/// <summary>
/// La oferta de los cursos: la llama el CMS al publicar y al despublicar un curso, y «republicar» a mano para la
/// siembra. Sin marca de la puerta: no es del navegador.
/// </summary>
/// <remarks>Código del vertical (P12, NM-1): no cuenta para la meta del flujo.</remarks>
public static class OfertasEndpoints
{
    /// <summary>Prefijo de los códigos de rechazo de este orquestador.</summary>
    public const string CodePrefix = "educacion";

    /// <summary>Registra la oferta y su rastro.</summary>
    public static WebApplicationBuilder AddOfertaDeCursos(this WebApplicationBuilder builder, RastroDeCursos rastro)
    {
        builder.Services.AddSingleton(rastro);
        builder.Services.AddSingleton<CapacidadesDeLaOferta>();
        builder.Services.AddSingleton<OfertaDeCursos>();
        return builder;
    }

    public static IEndpointRouteBuilder MapOfertas(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/ofertas", async Task<Results<Ok<OfertaDeCursoResponse>, ProblemHttpResult>> (
            PublicarOfertaDeCursoRequest req, HttpRequest http, OfertaDeCursos ofertas, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, CodePrefix, out var key, out var falta)) return falta!;
            return (await ofertas.PublicarAsync(req, key, ct)).Map(Respuesta).ToHttp();
        }).WithName("PublishCourseOffer").ConLlaveDeIdempotencia();

        app.MapPost("/v1/ofertas/{curso}/retirar", async Task<Results<Ok<OfertaDeCursoResponse>, ProblemHttpResult>> (
            string curso, HttpRequest http, OfertaDeCursos ofertas, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, CodePrefix, out var key, out var falta)) return falta!;
            return (await ofertas.RetirarAsync(curso, key, ct)).Map(Respuesta).ToHttp();
        }).WithName("RetireCourseOffer").ConLlaveDeIdempotencia();

        return app;
    }

    private static OfertaDeCursoResponse Respuesta(CursoOfertado o)
        => new(o.Curso, o.Precio, o.Moneda, o.Planes.Select(p => p.Codigo).ToList(), o.Retirada ? 0 : 1 + o.Planes.Count, o.Retirada);
}
