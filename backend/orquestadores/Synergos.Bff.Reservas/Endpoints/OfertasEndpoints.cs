using Microsoft.AspNetCore.Http.HttpResults;
using Synergos.Bff.Reservas.Clients;
using Synergos.Bff.Reservas.Contracts;
using Synergos.Bff.Reservas.Domain;
using Synergos.Shared;

namespace Synergos.Bff.Reservas.Endpoints;

/// <summary>
/// La oferta de los servicios reservables: la llama el CMS al publicar y al despublicar la ficha de un servicio
/// (y un guion en la copia piloto). Sin marca de la puerta: no es del navegador.
/// </summary>
/// <remarks>Código del vertical (P12, NB-8): no cuenta para la meta del flujo.</remarks>
public static class OfertasEndpoints
{
    /// <summary>Prefijo de los códigos de rechazo de este orquestador.</summary>
    public const string CodePrefix = "reservas";

    /// <summary>Registra la oferta y su rastro, compartido con el paso interino de la duración.</summary>
    public static WebApplicationBuilder AddOfertaDeReservas(this WebApplicationBuilder builder, RastroDeOfertas rastro)
    {
        builder.Services.AddSingleton(rastro);
        builder.Services.AddSingleton<CapacidadesDeLaOferta>();
        builder.Services.AddSingleton<OfertaDeReservas>();
        return builder;
    }

    public static IEndpointRouteBuilder MapOfertas(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/ofertas", async Task<Results<Ok<OfertaDeServicioResponse>, ProblemHttpResult>> (
            PublicarOfertaRequest req, HttpRequest http, OfertaDeReservas ofertas, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, CodePrefix, out var key, out var falta)) return falta!;
            return (await ofertas.PublicarAsync(req, key, ct)).Map(Respuesta).ToHttp();
        }).WithName("PublishServiceOffer").ConLlaveDeIdempotencia();

        app.MapPost("/v1/ofertas/{servicio}/retirar", async Task<Results<Ok<OfertaDeServicioResponse>, ProblemHttpResult>> (
            string servicio, HttpRequest http, OfertaDeReservas ofertas, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, CodePrefix, out var key, out var falta)) return falta!;
            return (await ofertas.RetirarAsync(servicio, key, ct)).Map(Respuesta).ToHttp();
        }).WithName("RetireServiceOffer").ConLlaveDeIdempotencia();

        return app;
    }

    private static OfertaDeServicioResponse Respuesta(ServicioOfertado o)
        => new(o.Servicio, o.DuracionMinutos, o.Cupos, o.Precio, o.Moneda, o.Retirada);
}
