using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.HttpResults;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Tienda.Domain;
using Synergos.Shared;

namespace Synergos.Bff.Tienda.Endpoints;

/// <summary>
/// El cupón en «abrir» y «cotizar» de <c>tienda.compra</c> (004-tienda-compra, US 8, R-12): el código se recorta
/// antes de que lo lea el flujo, y los que no sirven se frenan por comprador (<see cref="IntentosDeCodigo"/>).
/// </summary>
/// <remarks>
/// <para><b>Un filtro y no código en las rutas</b>, porque las rutas del flujo las pone el host genérico del
/// núcleo (<c>MapFlujosDeclarados</c>) y la línea no las toca: el orquestador las monta dentro de un grupo con
/// este filtro. Mira sólo las dos operaciones por su nombre; las demás pasan sin tocar.</para>
///
/// <para><b>Recorta el código</b>: <c>Api.Pricing</c> busca la promoción sin recortar, así que « DIEZ » —un
/// espacio que se cuela al pegar el código— saldría «no existe» y contaría como un intento fallido. Uno que
/// queda en blanco es una compra sin código.</para>
///
/// <para><b>Frena como el de Eventos</b>: con código, se corta ANTES de cotizar contra Pricing —así que tampoco
/// se aparta ni se autoriza nada— si el sujeto agotó la ventana, y se anota después si Pricing rechazó el
/// código. Sin código no se mira nada. Una apertura que el orquestador devolvería tal cual (la misma llave con su
/// saga viva) no vuelve a cotizar, así que no es oráculo, y no se corta: cortarla dejaría a quien ya apartó sin
/// poder reintentar su propia compra.</para>
/// </remarks>
public sealed class CuponDeLaCompra : IEndpointFilter
{
    /// <summary>El flujo del cupón.</summary>
    public const string Flujo = "tienda.compra";

    /// <summary>La entrada del cuerpo que lleva el código.</summary>
    public const string Entrada = "promocion";

    private readonly IntentosDeCodigo _intentos;
    private readonly FlujoGenerico? _flujo;
    private readonly string _abrir;
    private readonly string _cotizar;

    public CuponDeLaCompra(IntentosDeCodigo intentos, IEnumerable<FlujoGenerico> flujos)
    {
        _intentos = intentos;
        _flujo = flujos.FirstOrDefault(f => string.Equals(f.Definicion.Clave, Flujo, StringComparison.Ordinal));
        var nombre = MapeoDeFlujos.Pascal(Flujo);
        _abrir = nombre + MapeoDeFlujos.Pascal(_flujo?.Definicion.Fases[0].Nombre ?? "abrir");
        _cotizar = nombre + "Cotizar";
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var operacion = http.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName;
        var abre = string.Equals(operacion, _abrir, StringComparison.Ordinal);
        if (_flujo is null || (!abre && !string.Equals(operacion, _cotizar, StringComparison.Ordinal))) return await next(context);

        if (!Recortar(context)) return await next(context);

        // Sin un sujeto que se lea, la ruta lo rechaza ella misma: el freno no tiene a quién contarle.
        if (!CabecerasDeLaPuerta.TryLeerSujeto(http.Request, out var sujeto) || sujeto is null) return await next(context);

        var reusa = abre
            && IdempotencyHeader.TryRead(http.Request, _flujo.Vocabulario.Origin, out var llave, out _)
            && _flujo.Fachada.DevolveriaLaMisma(llave.Value, sujeto);
        if (!reusa && _intentos.Revisar(sujeto) is { } demasiados) return demasiados.ToProblem();

        var respuesta = await next(context);
        _intentos.Anotar(sujeto, CodigoDe(respuesta));
        return respuesta;
    }

    /// <summary>
    /// Deja el código del cuerpo recortado —o nulo si queda en blanco— para lo que sigue. Dice si quedó un código.
    /// </summary>
    private static bool Recortar(EndpointFilterInvocationContext context)
    {
        var i = context.Arguments.ToList().FindIndex(a => a is JsonElement);
        if (i < 0 || context.Arguments[i] is not JsonElement { ValueKind: JsonValueKind.Object } cuerpo
            || !cuerpo.TryGetProperty(Entrada, out var codigo) || codigo.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var texto = codigo.GetString()!;
        var recortado = texto.Trim();
        if (recortado.Length != texto.Length)
        {
            var nuevo = JsonNode.Parse(cuerpo.GetRawText())!.AsObject();
            nuevo[Entrada] = recortado.Length == 0 ? null : recortado;
            context.Arguments[i] = JsonSerializer.SerializeToElement(nuevo);
        }
        return recortado.Length > 0;
    }

    /// <summary>El código del rechazo con que contestó la operación; nulo si salió bien.</summary>
    private static string? CodigoDe(object? respuesta)
    {
        var resultado = respuesta is INestedHttpResult anidado ? anidado.Result : respuesta as IResult;
        return resultado is ProblemHttpResult problema && problema.ProblemDetails.Extensions.TryGetValue("code", out var codigo)
            ? codigo?.ToString()
            : null;
    }
}
