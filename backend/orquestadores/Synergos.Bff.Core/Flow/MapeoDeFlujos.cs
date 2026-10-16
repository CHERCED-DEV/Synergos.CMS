using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Bff.Core.Flow;

/// <summary>Lo que devuelve un flujo genérico al abrir, consultar, continuar o cancelar.</summary>
/// <param name="Id">La saga.</param>
/// <param name="Flujo">La clave del flujo.</param>
/// <param name="Numero">El número que se le muestra a una persona (como el del aviso).</param>
/// <param name="Estado">En qué punto está: Running, Completed, Compensating, Compensated, CompensationFailed.</param>
/// <param name="Datos">La salida que declara el flujo, con sus tipos. El contrato publicado la tipa por flujo.</param>
/// <param name="CompensacionesPendientes">Cuánto queda por deshacer.</param>
/// <param name="Error">Qué falló, si falló.</param>
/// <param name="Deshaciendo">Si está deshaciendo algo (lo armado mientras corre no cuenta).</param>
public sealed record RespuestaDeFlujo(
    string Id, string Flujo, string Numero, string Estado, IReadOnlyDictionary<string, JsonElement?> Datos,
    int CompensacionesPendientes, string? Error, bool Deshaciendo);

/// <summary>Lo que costaría abrir, sin reservar nada: la salida que el flujo ya tiene antes de reservar.</summary>
public sealed record CotizacionDeFlujo(string Flujo, IReadOnlyDictionary<string, JsonElement?> Datos);

/// <summary>Una compensación pendiente de una saga genérica: la vista de operación.</summary>
public sealed record CompensacionDeFlujo(
    string SagaId, string Flujo, string Kind, string Reason, int Attempts, DateTimeOffset? NextAttemptUtc,
    string? LastError, bool Stuck, DateTimeOffset? AlertedAtUtc);

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PaginaDeFlujo<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);

/// <summary>
/// Metadato de un endpoint por convención: el esquema de su cuerpo y el de la salida de su flujo, que el
/// contrato publicado necesita y el tipo de retorno no puede dar (son por flujo, no por tipo .NET).
/// </summary>
/// <param name="Nombre">La base de los esquemas que publica: la clave del flujo en Pascal (<c>MuestraCita</c>).</param>
/// <param name="Cuerpo">El esquema del cuerpo, si la operación lo lleva.</param>
/// <param name="Datos">El esquema de <c>datos</c> en la respuesta.</param>
public sealed record ContratoDeLaOperacion(string Nombre, JsonObject? Cuerpo, JsonObject Datos);

/// <summary>Un flujo genérico registrado en el host: lo que el mapeo por convención necesita de él.</summary>
public sealed record FlujoGenerico(
    FlujoDef Definicion, BindingDeFlujo Binding, FachadaDeFlujo<SagaDeFlujo> Fachada, SagaVocabulary Vocabulario);

/// <summary>
/// Los endpoints de un flujo genérico, por convención (núcleo N1): lo que cada orquestador escribía en su
/// <c>*Endpoints.cs</c> para su fachada.
/// </summary>
/// <remarks>
/// <para><b>Las rutas</b> son las de <c>specs/001-nucleo/contracts/endpoints-por-convencion.md</c>: abrir
/// en <c>POST /v1/flujos/{clave}</c>, consultar en <c>GET …/{id}</c>, cada fase que continúa en
/// <c>POST …/{id}/{fase}</c>, cancelar, y —sólo si la fase que abre se puede anticipar— cotizar. Las de
/// operación (reintentar, compensaciones) van sin marca de la puerta: no existen para el navegador.</para>
///
/// <para><b>Las cabeceras de la puerta se leen ANTES que el cuerpo</b>, como en Eventos: sin sujeto no hay
/// a quién atar la saga, y un sujeto opcional dejaba leer y confirmar lo de cualquiera (F3).</para>
///
/// <para><b>El cuerpo es estricto</b>: un campo que la definición no declara se rechaza, igual que el lector
/// rechaza un campo que no conoce, y una referencia sólo se lee de la clase que la definición declara. Un
/// error de entrada contesta <c>400 {origen}.entrada_invalida</c> con el nombre del campo.</para>
/// </remarks>
public static class MapeoDeFlujos
{
    /// <summary>Mapea los endpoints de todos los flujos genéricos registrados.</summary>
    public static IEndpointRouteBuilder MapFlujosDeclarados(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var flujos = app.ServiceProvider.GetServices<FlujoGenerico>().ToList();
        foreach (var f in flujos) MapFlujo(app, f);

        if (flujos.Count > 0)
        {
            // La vista de operación: qué quedó colgado, de todos los flujos genéricos. Sin marca de la puerta.
            app.MapGet("/v1/flujos/compensaciones", Ok<PaginaDeFlujo<CompensacionDeFlujo>> (int? offset, int? limit) =>
            {
                var todas = flujos
                    .SelectMany(f => f.Fachada.Pendientes())
                    .SelectMany(s => s.Pending().Select(c => new CompensacionDeFlujo(
                        s.Id, s.Flujo, c.Kind, c.Reason, c.Attempts, c.NextAttemptUtc, c.LastError, c.IsStuck, s.AlertedAtUtc)))
                    .ToList();
                var off = Math.Max(0, offset ?? 0);
                var lim = QueryWindow.Limit(limit);
                return TypedResults.Ok(new PaginaDeFlujo<CompensacionDeFlujo>(
                    todas.Skip(off).Take(lim).ToList(), todas.Count, off, off + lim < todas.Count));
            }).WithName("FlujosCompensaciones");
        }

        return app;
    }

    /// <summary>La clave en Pascal: <c>muestra.cita</c> → <c>MuestraCita</c>, <c>salud.cita-previa</c> → <c>SaludCitaPrevia</c>.</summary>
    public static string Pascal(string clave)
    {
        ArgumentNullException.ThrowIfNull(clave);
        return string.Concat(clave.Split('.', '-').Where(p => p.Length > 0).Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    private static void MapFlujo(IEndpointRouteBuilder app, FlujoGenerico f)
    {
        var def = f.Definicion;
        var clave = def.Clave;
        var nombre = Pascal(clave);
        var origen = f.Vocabulario.Origin;
        var abre = def.Fases[0].Nombre;
        var datos = EsquemaDeDatos(f);
        var conNegocio = def.Entrada.Any(n => def.Tipos![n].Origen == OrigenDelValor.Negocio);
        var cuerpoDeAbrir = EsquemaDelCuerpo(f, def.Entrada);

        var abrir = app.MapPost($"/v1/flujos/{clave}", async Task<Results<Created<RespuestaDeFlujo>, ProblemHttpResult>> (
            JsonElement cuerpo, HttpRequest http, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, origen, out var llave, out var falta)) return falta!;
            var entrada = new FlowContext();
            if (Leer(f, http, cuerpo, def.Entrada, entrada) is { } mala) return mala;

            var r = await f.Fachada.AbrirAsync(llave.Value, entrada.Get<Ref>(f.Binding.Sujeto), entrada, ct);
            return r.Map(s => Respuesta(f, s)).ToCreated(s => $"/v1/flujos/{clave}/{s.Id}");
        }).WithName($"{nombre}{Pascal(abre)}").ConLlaveDeSaga()
          .ConCabeceraDeLaPuerta(CabecerasDeLaPuerta.Sujeto, requerida: true)
          .WithMetadata(new ContratoDeLaOperacion(nombre, cuerpoDeAbrir, datos));
        if (conNegocio) abrir.ConCabeceraDeLaPuerta(CabecerasDeLaPuerta.Negocio, requerida: true);
        Marcar(abrir, clave, abre);

        if (SeAnticipa(f))
        {
            var cotizar = app.MapPost($"/v1/flujos/{clave}/cotizar", async Task<Results<Ok<CotizacionDeFlujo>, ProblemHttpResult>> (
                JsonElement cuerpo, HttpRequest http, CancellationToken ct) =>
            {
                var entrada = new FlowContext();
                if (Leer(f, http, cuerpo, def.Entrada, entrada) is { } mala) return mala;

                var r = await f.Fachada.AnticiparAsync(entrada, ct);
                return r.Map(ctx => new CotizacionDeFlujo(clave, DatosDe(f, ctx))).ToHttp();
            }).WithName($"{nombre}Cotizar")
              .ConCabeceraDeLaPuerta(CabecerasDeLaPuerta.Sujeto, requerida: true)
              .WithMetadata(new ContratoDeLaOperacion(nombre, cuerpoDeAbrir, datos));
            if (conNegocio) cotizar.ConCabeceraDeLaPuerta(CabecerasDeLaPuerta.Negocio, requerida: true);
            cotizar.EnLaPuerta(clave, "cotizar");
        }

        app.MapGet($"/v1/flujos/{clave}/{{id}}", Results<Ok<RespuestaDeFlujo>, ProblemHttpResult> (string id, HttpRequest http) =>
        {
            if (Sujeto(f, http, out var dueno) is { } sinSujeto) return sinSujeto;
            return f.Fachada.Consultar(id, dueno).Map(s => Respuesta(f, s)).ToHttp();
        }).WithName($"{nombre}Consultar").ConCabeceraDeLaPuerta(CabecerasDeLaPuerta.Sujeto, requerida: true)
          .WithMetadata(new ContratoDeLaOperacion(nombre, null, datos))
          .EnLaPuerta(clave, "consultar");

        foreach (var fase in def.Fases.Skip(1).Select(x => x.Nombre))
        {
            var efimera = def.EfimeraDe(fase);
            var cuerpoDeFase = EsquemaDelCuerpo(f, efimera);
            var conContacto = efimera.Any(n => def.Tipos![n].Origen == OrigenDelValor.Contacto);

            var continuar = cuerpoDeFase is null
                ? app.MapPost($"/v1/flujos/{clave}/{{id}}/{fase}", async Task<Results<Ok<RespuestaDeFlujo>, ProblemHttpResult>> (
                    string id, HttpRequest http, CancellationToken ct) => await ContinuarAsync(f, fase, id, http, null, ct))
                : app.MapPost($"/v1/flujos/{clave}/{{id}}/{fase}", async Task<Results<Ok<RespuestaDeFlujo>, ProblemHttpResult>> (
                    string id, JsonElement cuerpo, HttpRequest http, CancellationToken ct) => await ContinuarAsync(f, fase, id, http, cuerpo, ct));

            continuar.WithName($"{nombre}{Pascal(fase)}")
                .ConCabeceraDeLaPuerta(CabecerasDeLaPuerta.Sujeto, requerida: true)
                .WithMetadata(new ContratoDeLaOperacion(nombre, cuerpoDeFase, datos));
            if (conContacto) continuar.ConCabeceraDeLaPuerta(CabecerasDeLaPuerta.Contacto);
            Marcar(continuar, clave, fase);
        }

        app.MapPost($"/v1/flujos/{clave}/{{id}}/cancelar", async Task<Results<Ok<RespuestaDeFlujo>, ProblemHttpResult>> (
            string id, HttpRequest http, CancellationToken ct) =>
        {
            if (Sujeto(f, http, out var dueno) is { } sinSujeto) return sinSujeto;
            return (await f.Fachada.CancelarAsync(id, dueno, ct)).Map(s => Respuesta(f, s)).ToHttp();
        }).WithName($"{nombre}Cancelar").ConCabeceraDeLaPuerta(CabecerasDeLaPuerta.Sujeto, requerida: true)
          .WithMetadata(new ContratoDeLaOperacion(nombre, null, datos))
          .EnLaPuerta(clave, "cancelar");

        // Volver a intentar lo que se rindió: la puerta de la persona a la que se le avisó. Sin marca.
        app.MapPost($"/v1/flujos/{clave}/{{id}}/reintentar", async Task<Results<Ok<RespuestaDeFlujo>, ProblemHttpResult>> (
            string id, CancellationToken ct) => (await f.Fachada.ReintentarAsync(id, ct)).Map(s => Respuesta(f, s)).ToHttp())
            .WithName($"{nombre}Reintentar")
            .WithMetadata(new ContratoDeLaOperacion(nombre, null, datos));
    }

    private static async Task<Results<Ok<RespuestaDeFlujo>, ProblemHttpResult>> ContinuarAsync(
        FlujoGenerico f, string fase, string id, HttpRequest http, JsonElement? cuerpo, CancellationToken ct)
    {
        if (Sujeto(f, http, out var dueno) is { } sinSujeto) return sinSujeto;
        var efimera = new FlowContext();
        if (Leer(f, http, cuerpo, f.Definicion.EfimeraDe(fase), efimera) is { } mala) return mala;

        return (await f.Fachada.ContinuarAsync(fase, id, dueno, efimera, ct)).Map(s => Respuesta(f, s)).ToHttp();
    }

    /// <summary>La operación va a la puerta si su nombre es del vocabulario fijo; si no, es sólo del orquestador.</summary>
    private static void Marcar(RouteHandlerBuilder builder, string clave, string operacion)
    {
        if (EnLaPuertaExtensions.Operaciones.Contains(operacion, StringComparer.Ordinal)) builder.EnLaPuerta(clave, operacion);
    }

    /// <summary>Si la fase que abre se puede anticipar: ningún paso antes de reservar pide llave ni cierra.</summary>
    private static bool SeAnticipa(FlujoGenerico f)
    {
        var fase = f.Definicion.Fases[0];
        foreach (var elemento in fase.Pasos)
        {
            var ids = elemento.ParaCada?.Pasos ?? [elemento.Paso!];
            var pasos = ids.Select(id => f.Definicion.Pasos[id]).ToList();
            if (pasos.Any(p => p.Reserva is not null)) return true;
            if (pasos.Any(p => p.CierraReserva is not null || p.Llave is not null || p.LlaveBase is not null)) return false;
        }
        return true;
    }

    private static ProblemHttpResult? Sujeto(FlujoGenerico f, HttpRequest http, out Ref? sujeto)
    {
        var origen = f.Vocabulario.Origin;
        if (!CabecerasDeLaPuerta.TryLeerSujeto(http, out sujeto))
        {
            return Rejection.Invalid(CabecerasDeLaPuerta.CodigoInvalido(origen, CabecerasDeLaPuerta.Sujeto),
                "El sujeto no se puede leer: <kind>:<id>.").ToProblem();
        }
        return sujeto is null
            ? Rejection.Invalid(CabecerasDeLaPuerta.CodigoRequerido(origen, CabecerasDeLaPuerta.Sujeto),
                "Hace falta el sujeto: lo pone la puerta del CMS.").ToProblem()
            : null;
    }

    /// <summary>
    /// Lee <paramref name="nombres"/> de su origen y los pone en <paramref name="ctx"/>: las cabeceras de la
    /// puerta primero, el cuerpo después. El rechazo, si algo no se lee.
    /// </summary>
    private static ProblemHttpResult? Leer(
        FlujoGenerico f, HttpRequest http, JsonElement? cuerpo, IReadOnlyList<string> nombres, FlowContext ctx)
    {
        var def = f.Definicion;
        var tipos = f.Binding.Tipos;
        var origen = f.Vocabulario.Origin;

        foreach (var nombre in nombres.Where(n => def.Tipos![n].Origen != OrigenDelValor.Cuerpo))
        {
            var tipo = def.Tipos![nombre];
            switch (tipo.Origen)
            {
                case OrigenDelValor.Sujeto:
                    if (Sujeto(f, http, out var sujeto) is { } sinSujeto) return sinSujeto;
                    ctx.Set(nombre, sujeto);
                    break;

                case OrigenDelValor.Negocio:
                    if (!CabecerasDeLaPuerta.TryLeerJson<JsonObject>(http, CabecerasDeLaPuerta.Negocio, out var negocio))
                    {
                        return Rejection.Invalid(CabecerasDeLaPuerta.CodigoInvalido(origen, CabecerasDeLaPuerta.Negocio),
                            "La configuración de negocio no se puede leer.").ToProblem();
                    }
                    var campo = negocio?.FirstOrDefault(p => string.Equals(p.Key, tipo.Campo, StringComparison.OrdinalIgnoreCase)).Value;
                    if (negocio is null || campo is null)
                    {
                        return Rejection.Invalid(CabecerasDeLaPuerta.CodigoRequerido(origen, CabecerasDeLaPuerta.Negocio),
                            $"Hace falta «{tipo.Campo}» de la configuración de negocio del sitio: la pone la puerta del CMS.").ToProblem();
                    }
                    if (LeerValor(tipos, tipo, JsonSerializer.SerializeToElement(campo), nombre, out var valorDeNegocio) is { } malNegocio)
                    {
                        return Rejection.Invalid(CabecerasDeLaPuerta.CodigoInvalido(origen, CabecerasDeLaPuerta.Negocio), malNegocio).ToProblem();
                    }
                    ctx.Set(nombre, valorDeNegocio);
                    break;

                case OrigenDelValor.Contacto:
                    if (!CabecerasDeLaPuerta.TryLeerJson<JsonObject>(http, CabecerasDeLaPuerta.Contacto, out var contacto))
                    {
                        return Rejection.Invalid(CabecerasDeLaPuerta.CodigoInvalido(origen, CabecerasDeLaPuerta.Contacto),
                            "El contacto del aviso no se puede leer.").ToProblem();
                    }
                    if (contacto is null)
                    {
                        // Sin contacto no se avisa: el paso de aviso lo sabe, y no es un error.
                        ctx.Set(nombre, null);
                        break;
                    }
                    if (LeerValor(tipos, tipo, JsonSerializer.SerializeToElement(contacto), nombre, out var valorDeContacto) is { } malContacto)
                    {
                        return Rejection.Invalid(CabecerasDeLaPuerta.CodigoInvalido(origen, CabecerasDeLaPuerta.Contacto), malContacto).ToProblem();
                    }
                    ctx.Set(nombre, valorDeContacto);
                    break;
            }
        }

        var delCuerpo = nombres.Where(n => def.Tipos![n].Origen == OrigenDelValor.Cuerpo).ToList();
        if (delCuerpo.Count == 0) return null;

        if (cuerpo is not { ValueKind: JsonValueKind.Object } objeto)
        {
            return Invalida(origen, "El cuerpo tiene que ser un objeto JSON con lo que pide el flujo.");
        }
        var sobra = objeto.EnumerateObject().Select(p => p.Name).FirstOrDefault(n => !delCuerpo.Contains(n, StringComparer.Ordinal));
        if (sobra is not null) return Invalida(origen, $"«{sobra}» no es una entrada del flujo «{def.Clave}».");
        foreach (var nombre in delCuerpo)
        {
            var valor = objeto.TryGetProperty(nombre, out var v) ? v : (JsonElement?)null;
            if (LeerValor(tipos, def.Tipos![nombre], valor, nombre, out var leido) is { } mal) return Invalida(origen, mal);
            ctx.Set(nombre, leido);
        }
        return null;
    }

    private static ProblemHttpResult Invalida(string origen, string mensaje)
        => Rejection.Invalid($"{origen}.entrada_invalida", mensaje).ToProblem();

    /// <summary>Un valor con su tipo declarado; el mensaje del error, si no se lee.</summary>
    private static string? LeerValor(TiposDeValor tipos, TipoDeclarado tipo, JsonElement? valor, string nombre, out object? leido)
    {
        leido = null;
        try
        {
            if (tipo.EsLista)
            {
                if (valor is not { } lista) return $"{nombre}: hace falta, y es una lista.";
                leido = tipos.LeerLista(lista, tipo.Campos!, nombre);
            }
            else
            {
                leido = (tipos.Resolver(tipo.Tipo!, tipo.Clase) ?? throw new InvalidOperationException($"Nadie registró «{tipo.Tipo}»."))
                    .Leer(valor, nombre);
            }
            return null;
        }
        catch (FormatException ex)
        {
            return ex.Message;
        }
    }

    private static RespuestaDeFlujo Respuesta(FlujoGenerico f, SagaDeFlujo s)
        => new(
            s.Id, s.Flujo, SagaDeFlujo.NumeroDe(s.Id), s.Status.ToString(),
            f.Definicion.LoQueSale.ToDictionary(
                n => n, n => s.Datos.GetValueOrDefault(n)?.Valor, StringComparer.Ordinal),
            s.Pending().Count, s.LastError, s.IsUnwinding());

    /// <summary>La salida que el contexto ya tiene, escrita con su tipo.</summary>
    private static IReadOnlyDictionary<string, JsonElement?> DatosDe(FlujoGenerico f, FlowContext ctx)
        => f.Definicion.LoQueSale.ToDictionary(
            n => n,
            n => ctx.Has(n) && f.Binding.TipoDe(n)?.Escribir(ctx.Get<object?>(n)) is { } json
                ? JsonSerializer.SerializeToElement(json)
                : (JsonElement?)null,
            StringComparer.Ordinal);

    /// <summary>El esquema del cuerpo con los nombres de origen cuerpo; nulo si no hay ninguno.</summary>
    private static JsonObject? EsquemaDelCuerpo(FlujoGenerico f, IReadOnlyList<string> nombres)
    {
        var def = f.Definicion;
        var tipos = f.Binding.Tipos;
        var delCuerpo = nombres.Where(n => def.Tipos![n].Origen == OrigenDelValor.Cuerpo).ToList();
        if (delCuerpo.Count == 0) return null;

        var props = new JsonObject();
        var requeridos = new JsonArray();
        foreach (var nombre in delCuerpo)
        {
            var tipo = def.Tipos![nombre];
            if (tipo.EsLista)
            {
                props[nombre] = tipos.EsquemaDeLista(tipo.Campos!);
                requeridos.Add(nombre);
                continue;
            }
            var resuelto = tipos.Resolver(tipo.Tipo!, tipo.Clase)!;
            props[nombre] = resuelto.Esquema();
            if (!resuelto.Anulable) requeridos.Add(nombre);
        }
        return new JsonObject { ["type"] = "object", ["required"] = requeridos, ["properties"] = props };
    }

    /// <summary>El esquema de <c>datos</c>: la salida con sus tipos, todos anulables (puede no haberse escrito aún).</summary>
    private static JsonObject EsquemaDeDatos(FlujoGenerico f)
    {
        var props = new JsonObject();
        foreach (var nombre in f.Definicion.LoQueSale)
        {
            var tipo = f.Binding.TipoDe(nombre)!;
            props[nombre] = (tipo with { Anulable = true }).Esquema();
        }
        return new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray(f.Definicion.LoQueSale.Select(n => (JsonNode)JsonValue.Create(n)!).ToArray()),
            ["properties"] = props,
        };
    }
}
