using Microsoft.AspNetCore.Http.HttpResults;
using Synergos.Bff.Gobierno.Clients;
using Synergos.Bff.Gobierno.Contracts;
using Synergos.Bff.Gobierno.Domain;
using Synergos.Shared;

namespace Synergos.Bff.Gobierno.Endpoints;

/// <summary>
/// La oferta de los trámites: la llama el CMS al publicar y al despublicar un <c>tramitePage</c>. Sin marca de la
/// puerta: no es del navegador.
/// </summary>
/// <remarks>Código del vertical (P12, NB-8): no cuenta para la medida del flujo.</remarks>
public static class OfertasEndpoints
{
    /// <summary>Prefijo de los códigos de rechazo de este orquestador.</summary>
    public const string CodePrefix = "gobierno";

    /// <summary>Registra la oferta y su rastro.</summary>
    public static WebApplicationBuilder AddOfertaDeGobierno(this WebApplicationBuilder builder, RastroDeTramites rastro)
    {
        builder.Services.AddSingleton(rastro);
        builder.Services.AddSingleton<CapacidadesDeLaOferta>();
        builder.Services.AddSingleton<OfertaDeGobierno>();
        return builder;
    }

    public static IEndpointRouteBuilder MapOfertas(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/ofertas", async Task<Results<Ok<OfertaDeTramiteResponse>, ProblemHttpResult>> (
            PublicarOfertaDeTramiteRequest req, HttpRequest http, OfertaDeGobierno ofertas, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, CodePrefix, out var key, out var falta)) return falta!;
            return (await ofertas.PublicarAsync(req, key, ct)).Map(Respuesta).ToHttp();
        }).WithName("PublishTramiteOffer").ConLlaveDeIdempotencia();

        app.MapPost("/v1/ofertas/{tramite}/retirar", async Task<Results<Ok<OfertaDeTramiteResponse>, ProblemHttpResult>> (
            string tramite, HttpRequest http, OfertaDeGobierno ofertas, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, CodePrefix, out var key, out var falta)) return falta!;
            return (await ofertas.RetirarAsync(tramite, key, ct)).Map(Respuesta).ToHttp();
        }).WithName("RetireTramiteOffer").ConLlaveDeIdempotencia();

        return app;
    }

    private static OfertaDeTramiteResponse Respuesta(TramiteOfertado o) => new(o.Tramite, o.Tasa, o.Moneda, o.Retirada);
}
