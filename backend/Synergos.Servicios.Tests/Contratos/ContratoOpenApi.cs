using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Synergos.Bff.Core;
using Synergos.CMS.Tests.Architecture;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Contratos;

/// <summary>
/// Cómo sale el contrato HTTP publicado de una pieza (ADR 0140, F2): las opciones y los
/// transformers que lo hacen útil y DETERMINISTA, y el host real del que se genera.
/// </summary>
/// <remarks>
/// <para><b>Se genera aquí y no en el servicio.</b> El paquete va sólo en esta suite: dentro de un
/// servicio su generador de comentarios XML mete CRLF en Windows y cada comentario editado sería
/// deriva; en el build (ApiDescription.Server) el Program arranca en Production y la llave
/// compartida lo tumba. Aquí el documento sale de los metadatos de los endpoints del host REAL
/// —el mismo <c>Program</c>, con <c>AddOpenApi</c> metido por <c>ConfigureTestServices</c>— y se pide
/// al proveedor con clave, sin <c>MapOpenApi</c>: producción no lleva ni el paquete ni la ruta.</para>
///
/// <para><b>Determinista por construcción, no por suerte.</b> OpenAPI 3.1; <c>info</c> =
/// {nombre del ensamblado, "v1"}; sin <c>servers</c> (saldría el host de prueba), sin <c>tags</c> y
/// sin descripciones sacadas de comentarios; rutas, esquemas y respuestas en orden ordinal; LF, sin
/// BOM y con <c>'\n'</c> final. Dos procesos dan el mismo byte (medido en Windows). Lo único que
/// puede moverlo sin tocar código es un parche del runtime 10 (el esquema lo produce
/// System.Text.Json del framework compartido): por eso el rojo imprime la versión.</para>
///
/// <para><b>Lo que el documento dice de más, a propósito, y lo que no dice.</b> Cada operación
/// publica el 401 de la llave compartida (sin cuerpo), y la que puede rechazar —su tipo de retorno
/// incluye <c>ProblemHttpResult</c>— los seis estados de <see cref="RejectionResults.StatusCodeFor"/>
/// con el esquema <see cref="EsquemaRechazo"/>: salen de la única fuente que decide el código, así
/// que no hay listas por endpoint que deriven (y un GET documenta un 409 que no puede dar). Los
/// CÓDIGOS (<c>pricing.bad_subject</c>…) no se enumeran: no se derivan del tipo, y el BFF reenvía
/// los de sus capacidades. La cabecera <c>Idempotency-Key</c> sale del metadato
/// <see cref="LlaveDeIdempotenciaRequerida"/> y la del token de identidad de
/// <see cref="TokenDeIdentidadAdmitido"/>, y una sonda contra el host comprueba que lo declarado es
/// lo que el endpoint lee.</para>
/// </remarks>
internal static class ContratoOpenApi
{
    /// <summary>El nombre del documento que registra <c>AddOpenApi</c>.</summary>
    public const string Documento = "v1";

    /// <summary>El esquema único de los rechazos: ProblemDetails con <c>code</c> y <c>transient</c>.</summary>
    public const string EsquemaRechazo = "Rechazo";

    /// <summary>El esquema de seguridad de la llave compartida.</summary>
    public const string EsquemaLlave = "llaveCompartida";

    /// <summary>
    /// Las piezas que publican contrato en la F2: Bff.Eventos y sus tres capacidades.
    /// </summary>
    /// <remarks>
    /// El marcador es un tipo público de los <c>Contracts/</c> de cada una, como en
    /// <c>ArnesDeCapacidades</c>: <c>Program</c> vive en el namespace global de las veinte y
    /// <c>WebApplicationFactory&lt;Program&gt;</c> es ambiguo. La sección es la raíz de configuración
    /// de la pieza (<c>&lt;Seccion&gt;:Storage:Root</c>, <c>&lt;Seccion&gt;:ApiKey</c>).
    /// </remarks>
    public static IReadOnlyList<PiezaPublicada> Piezas { get; } =
    [
        new PiezaPublicada<Synergos.Api.Pricing.Contracts.PriceResponse>("Pricing"),
        new PiezaPublicada<Synergos.Api.Inventory.Contracts.StockItemResponse>("Inventory"),
        new PiezaPublicada<Synergos.Api.Payments.Contracts.PaymentResponse>("Payments"),
        new PiezaPublicada<Synergos.Bff.Eventos.Contracts.TicketPurchaseResponse>("Eventos"),
    ];

    /// <summary>La pieza por el nombre de su ensamblado, que es también el del fichero.</summary>
    public static PiezaPublicada Pieza(string ensamblado)
        => Piezas.Single(p => string.Equals(p.Ensamblado, ensamblado, StringComparison.Ordinal));

    /// <summary>Dónde vive el documento comiteado de una pieza.</summary>
    /// <remarks>
    /// En <c>Synergos.CMS.Web/docs/contracts/</c>, que CLAUDE.md llama «la ÚNICA superficie de
    /// acople» con el UI, y con el nombre del ensamblado: el mismo que <c>info.title</c>, sin ninguna
    /// transformación que mantener entre los dos repos.
    /// </remarks>
    public static string Ruta(string ensamblado)
        => Proyectos.Dir("Synergos.CMS.Web", "docs", "contracts", "openapi", ensamblado + ".json");

    /// <summary>El documento COMITEADO de una pieza: lo que leen el UI y los consumidores.</summary>
    /// <remarks>
    /// Los gates que juzgan el contrato leen el comiteado y no uno recién generado: la deriva ya
    /// obliga a que sean el mismo, y así un documento empobrecido y regenerado sigue en rojo.
    /// </remarks>
    public static JsonObject Comiteado(string ensamblado)
        => JsonNode.Parse(File.ReadAllText(Ruta(ensamblado)))!.AsObject();

    /// <summary>Las operaciones de un documento, con su método en mayúsculas y su ruta.</summary>
    public static IReadOnlyList<OperacionPublicada> Operaciones(JsonObject doc)
        => doc["paths"]!.AsObject()
            .SelectMany(p => p.Value!.AsObject()
                .Where(m => MetodosHttp.Contains(m.Key))
                .Select(m => new OperacionPublicada(m.Key.ToUpperInvariant(), p.Key, m.Value!.AsObject())))
            .ToList();

    private static readonly HashSet<string> MetodosHttp = new(StringComparer.Ordinal)
    {
        "get", "post", "put", "delete", "patch",
    };

    /// <summary>Las opciones del documento de <paramref name="titulo"/>.</summary>
    public static Action<OpenApiOptions> Opciones(string titulo) => o =>
    {
        o.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;

        // /health es del molde (lo pide el healthcheck de la imagen), no del contrato de la pieza.
        o.ShouldInclude = d => !string.Equals(d.RelativePath, "health", StringComparison.Ordinal);

        // Un id de esquema, un tipo. ASP.NET nombra el esquema por el Name del tipo, y dos tipos
        // con el mismo Name en un documento NO salen como X y X2: el segundo apunta en silencio al
        // esquema del primero (medido con un MoneyDto {moneda} junto al {amount, currency}: el campo
        // nuevo publicaba la forma del otro). Se convierte en un rojo al generar, que regenerar no
        // salva; la salida es renombrar uno de los dos.
        var ids = new System.Collections.Concurrent.ConcurrentDictionary<string, Type>(StringComparer.Ordinal);
        o.CreateSchemaReferenceId = info =>
        {
            var id = OpenApiOptions.CreateDefaultSchemaReferenceId(info);
            if (id is not null && ids.GetOrAdd(id, info.Type) is var primero && primero != info.Type)
            {
                throw new InvalidOperationException(
                    $"Dos tipos publican el mismo esquema «{id}» en {titulo}: {primero.FullName} y " +
                    $"{info.Type.FullName}. El documento usaría la forma del primero para los dos; renombrá uno.");
            }

            return id;
        };

        o.AddSchemaTransformer(Numeros);
        o.AddOperationTransformer(Llave);
        o.AddOperationTransformer(Identidad);
        o.AddOperationTransformer(Puerta);
        o.AddOperationTransformer(Flujo);
        o.AddOperationTransformer(Rechazos);
        o.AddDocumentTransformer((doc, ctx, ct) =>
        {
            Normalizar(doc, titulo);
            return Task.CompletedTask;
        });
    };

    /// <summary>Serializa el documento como lo escribe el fichero comiteado.</summary>
    public static async Task<string> Serializar(OpenApiDocument doc)
    {
        using var sw = new StringWriter(CultureInfo.InvariantCulture);
        var w = new OpenApiJsonWriter(sw);
        doc.SerializeAsV31(w);
        await sw.FlushAsync();

        // El escritor indenta con '\n', pero el TextWriter de Windows termina con su NewLine: se
        // normaliza para que el byte sea el mismo en las dos máquinas, como ContratoSynHostTests.
        return sw.ToString().ReplaceLineEndings("\n") + "\n";
    }

    /// <summary>
    /// Los números salen como número, y el decimal como decimal.
    /// </summary>
    /// <remarks>
    /// <para>Con los defaults web de System.Text.Json (<c>AllowReadingFromString</c>) ASP.NET publica
    /// <c>int</c> y <c>decimal</c> como <c>["integer","string"]</c> con un <c>pattern</c>, también en las
    /// respuestas, donde el servidor nunca escribe una cadena. Eso da <c>number | string</c> en el
    /// UI y deja ciego al gate de compatibilidad ante un cambio de tipo (medido: el mutante
    /// <c>Available</c> de int a string quedaba verde). Que el servidor ACEPTE la cadena al leer es
    /// tolerancia del borde, no contrato.</para>
    ///
    /// <para><c>decimal</c> salía con <c>format: double</c>, que es justo lo que el dinero no es.</para>
    /// </remarks>
    private static Task Numeros(OpenApiSchema schema, OpenApiSchemaTransformerContext ctx, CancellationToken ct)
    {
        if (schema.Type is { } t
            && (t.HasFlag(JsonSchemaType.Number) || t.HasFlag(JsonSchemaType.Integer))
            && t.HasFlag(JsonSchemaType.String))
        {
            schema.Type = t & ~JsonSchemaType.String;
            schema.Pattern = null;
        }

        var tipo = Nullable.GetUnderlyingType(ctx.JsonTypeInfo.Type) ?? ctx.JsonTypeInfo.Type;
        if (tipo == typeof(decimal)) schema.Format = "decimal";
        return Task.CompletedTask;
    }

    private static void Normalizar(OpenApiDocument doc, string titulo)
    {
        doc.Info = new OpenApiInfo { Title = titulo, Version = Documento };
        doc.Servers = new List<OpenApiServer>();
        doc.Tags = null;

        doc.Components ??= new OpenApiComponents();
        doc.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        doc.Components.Schemas[EsquemaRechazo] = Rechazo();
        doc.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal)
        {
            [EsquemaLlave] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = SharedKeyAuth.HeaderName,
            },
        };
        doc.Security = new List<OpenApiSecurityRequirement>
        {
            new() { [new OpenApiSecuritySchemeReference(EsquemaLlave, doc)] = new List<string>() },
        };

        foreach (var item in doc.Paths.Values)
        {
            foreach (var op in item.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>())
            {
                // Se VACÍA y no se pone a null: el modelo ignora el null y el escritor seguía sacando
                // el nombre del ensamblado como tag (medido). Vacío, no se escribe.
                op.Tags?.Clear();
                op.Responses ??= new OpenApiResponses();

                var respuestas = op.Responses.OrderBy(r => r.Key, StringComparer.Ordinal).ToList();
                op.Responses = new OpenApiResponses();
                foreach (var (k, v) in respuestas) op.Responses[k] = v;
            }
        }

        var rutas = new OpenApiPaths();
        foreach (var (ruta, item) in doc.Paths.OrderBy(p => p.Key, StringComparer.Ordinal)) rutas[ruta] = item;
        doc.Paths = rutas;

        var esquemas = doc.Components.Schemas.OrderBy(s => s.Key, StringComparer.Ordinal).ToList();
        doc.Components.Schemas = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        foreach (var (k, v) in esquemas) doc.Components.Schemas[k] = v;
    }

    /// <summary>
    /// La cabecera <c>Idempotency-Key</c>, donde el endpoint declara que la lee.
    /// </summary>
    /// <remarks>
    /// <c>ApiExplorer</c> no la ve: <see cref="IdempotencyHeader.TryRead"/> la lee a mano del
    /// <c>HttpRequest</c>. Sale del metadato <see cref="LlaveDeIdempotenciaRequerida"/>, con
    /// <c>required</c> según lo exija siempre o sólo en un caso del cuerpo, y el largo que el
    /// endpoint acepta: el mismo número que lee <see cref="IdempotencyHeader.TryRead"/>, así que un
    /// orquestador, que deriva de la llave las de cada paso, publica menos de
    /// <see cref="IdempotencyKey.MaxLength"/>.
    /// </remarks>
    private static Task Llave(OpenApiOperation op, OpenApiOperationTransformerContext ctx, CancellationToken ct)
    {
        if (ctx.Description.ActionDescriptor.EndpointMetadata.OfType<LlaveDeIdempotenciaRequerida>().FirstOrDefault() is { } llave)
        {
            op.Parameters ??= new List<IOpenApiParameter>();
            op.Parameters.Add(new OpenApiParameter
            {
                Name = IdempotencyHeader.Name,
                In = ParameterLocation.Header,
                Required = llave.Siempre,
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = llave.MaxLength },
            });
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// La cabecera del token de identidad, opcional, donde el endpoint declara que la lee.
    /// </summary>
    /// <remarks>
    /// Como la llave: el endpoint la lee a mano y <c>ApiExplorer</c> no la ve, así que sale del
    /// metadato <see cref="TokenDeIdentidadAdmitido"/>. Siempre opcional: sin token la capacidad
    /// resuelve con lo que se declara, o con «no consta». Una sonda contra el host comprueba que la
    /// declarada sea la que el endpoint lee.
    /// </remarks>
    private static Task Identidad(OpenApiOperation op, OpenApiOperationTransformerContext ctx, CancellationToken ct)
    {
        if (ctx.Description.ActionDescriptor.EndpointMetadata.OfType<TokenDeIdentidadAdmitido>().Any())
        {
            op.Parameters ??= new List<IOpenApiParameter>();
            op.Parameters.Add(new OpenApiParameter
            {
                Name = IdentityTokens.HeaderName,
                In = ParameterLocation.Header,
                Required = false,
                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
            });
        }

        return Task.CompletedTask;
    }

    /// <summary>La marca de una cabecera que pone la puerta del CMS y no el navegador (ADR 0140 F3).</summary>
    public const string MarcaDeLaPuerta = "x-synergos-puerta";

    /// <summary>
    /// Las cabeceras que pone la PUERTA, donde el endpoint declara que las lee, marcadas con
    /// <see cref="MarcaDeLaPuerta"/>.
    /// </summary>
    /// <remarks>
    /// Como la llave y el token: el endpoint las lee a mano y <c>ApiExplorer</c> no las ve, así que
    /// salen del metadato <see cref="CabeceraDeLaPuerta"/>. La marca es lo que deja al generador del
    /// UI omitirlas del tipo del navegador —no las manda él— sin aflojar su regla de rechazar
    /// cualquier otra cabecera. Una sonda contra el host comprueba que la declarada sea la que el
    /// endpoint lee.
    /// </remarks>
    private static Task Puerta(OpenApiOperation op, OpenApiOperationTransformerContext ctx, CancellationToken ct)
    {
        foreach (var cabecera in ctx.Description.ActionDescriptor.EndpointMetadata.OfType<CabeceraDeLaPuerta>())
        {
            op.Parameters ??= new List<IOpenApiParameter>();
            op.Parameters.Add(new OpenApiParameter
            {
                Name = cabecera.Nombre,
                In = ParameterLocation.Header,
                Required = cabecera.Requerida,
                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                Extensions = new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal)
                {
                    [MarcaDeLaPuerta] = new JsonNodeExtension(JsonValue.Create(true)),
                },
            });
        }

        return Task.CompletedTask;
    }

    /// <summary>La marca de una operación que la puerta del CMS expone: <c>{ flujo, operacion }</c>.</summary>
    public const string MarcaDelFlujo = "x-synergos-flujo";

    /// <summary>
    /// La operación que el orquestador expone en la puerta, marcada con <see cref="MarcaDelFlujo"/>
    /// desde el metadato <see cref="OperacionEnLaPuerta"/> (ADR 0140 F3).
    /// </summary>
    /// <remarks>
    /// Es lo que lee la puerta del CMS para armar su tabla, y el generador del UI para saber qué emitir.
    /// Sin marca, la operación no existe para ninguno de los dos.
    /// </remarks>
    private static Task Flujo(OpenApiOperation op, OpenApiOperationTransformerContext ctx, CancellationToken ct)
    {
        if (ctx.Description.ActionDescriptor.EndpointMetadata.OfType<OperacionEnLaPuerta>().FirstOrDefault() is { } marca)
        {
            op.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
            op.Extensions[MarcaDelFlujo] = new JsonNodeExtension(new JsonObject
            {
                ["flujo"] = marca.Flujo,
                ["operacion"] = marca.Operacion,
            });
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// El 401 de la llave compartida en toda operación, y los seis estados de rechazo en la que
    /// puede rechazar.
    /// </summary>
    /// <remarks>
    /// El 401 va SIN cuerpo porque así sale de <c>SharedKeyAuth</c> (medido: sin Content-Type). Los
    /// seis estados salen de <see cref="RejectionResults.StatusCodeFor"/>, la única fuente que decide
    /// qué número lleva cada clase de rechazo, y se publican donde <see cref="PuedeRechazar"/>.
    /// </remarks>
    private static Task Rechazos(OpenApiOperation op, OpenApiOperationTransformerContext ctx, CancellationToken ct)
    {
        op.Responses ??= new OpenApiResponses();
        op.Responses["401"] = new OpenApiResponse
        {
            Description = "Falta la llave compartida o no es la buena (sin cuerpo).",
        };

        if (!PuedeRechazar(ctx.Description)) return Task.CompletedTask;

        foreach (var kind in Enum.GetValues<RejectionKind>())
        {
            var status = RejectionResults.StatusCodeFor(kind).ToString(CultureInfo.InvariantCulture);
            op.Responses[status] = new OpenApiResponse
            {
                Description = kind.ToString(),
                Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
                {
                    ["application/problem+json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchemaReference(EsquemaRechazo, ctx.Document),
                    },
                },
            };
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Si el endpoint puede contestar con un rechazo: su tipo de retorno incluye
    /// <c>ProblemHttpResult</c>.
    /// </summary>
    /// <remarks>
    /// Se lee del <see cref="MethodInfo"/> del handler, que Minimal APIs deja en los metadatos del
    /// endpoint. Si no está, o el handler devuelve un <c>IResult</c> sin tipo, no se puede saber, y
    /// se publica el rechazo: es el lado seguro (sobra en el documento, no falta). Hoy sólo
    /// <c>GET /v1/compensations</c> de Eventos no rechaza.
    /// </remarks>
    internal static bool PuedeRechazar(ApiDescription descripcion)
    {
        var metodo = descripcion.ActionDescriptor.EndpointMetadata.OfType<MethodInfo>().FirstOrDefault();
        if (metodo is null) return true;

        var tipo = metodo.ReturnType;
        if (tipo.IsGenericType && (tipo.GetGenericTypeDefinition() == typeof(Task<>)
                                || tipo.GetGenericTypeDefinition() == typeof(ValueTask<>)))
        {
            tipo = tipo.GetGenericArguments()[0];
        }

        if (tipo == typeof(ProblemHttpResult)) return true;
        if (tipo == typeof(IResult) || tipo == typeof(object)) return true;
        return tipo.IsGenericType && tipo.GetGenericArguments().Contains(typeof(ProblemHttpResult));
    }

    /// <summary>
    /// ProblemDetails (RFC 9457) con las dos extensiones que pone <see cref="RejectionResults.ToProblem"/>.
    /// </summary>
    /// <remarks>
    /// Está escrito aquí y no inferido, porque <c>ProblemHttpResult</c> no aporta metadatos y el
    /// <c>ProblemDetails</c> que ASP.NET conoce no trae <c>code</c> ni <c>transient</c> —justo lo que
    /// un cliente compara—. Por estar escrito a mano necesita un diente contra un cuerpo real: lo
    /// pone la sonda del contrato. <c>title</c> es el nombre del <see cref="RejectionKind"/>.
    /// </remarks>
    private static OpenApiSchema Rechazo() => new()
    {
        Type = JsonSchemaType.Object,
        Required = new HashSet<string>(StringComparer.Ordinal) { "type", "title", "status", "detail", "code", "transient" },
        Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
        {
            ["type"] = new OpenApiSchema { Type = JsonSchemaType.String },
            ["title"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = Enum.GetNames<RejectionKind>().Select(n => (JsonNode)JsonValue.Create(n)!).ToList(),
            },
            ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
            ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String },
            ["code"] = new OpenApiSchema { Type = JsonSchemaType.String },
            ["transient"] = new OpenApiSchema { Type = JsonSchemaType.Boolean },
        },
    };
}

/// <summary>Una operación de un documento publicado.</summary>
/// <param name="Metodo">GET, POST… en mayúsculas.</param>
/// <param name="Ruta">La plantilla de ruta, con sus <c>{parámetros}</c>.</param>
/// <param name="Op">El objeto de la operación en el JSON.</param>
internal sealed record OperacionPublicada(string Metodo, string Ruta, JsonObject Op)
{
    public override string ToString() => $"{Metodo} {Ruta}";
}

/// <summary>Una pieza que publica contrato, y cómo levantarla de verdad en proceso.</summary>
internal abstract class PiezaPublicada
{
    /// <summary>La llave compartida que exige el host de prueba.</summary>
    public const string Llave = "llave-del-contrato";

    /// <summary>El nombre del ensamblado: título del documento y nombre del fichero.</summary>
    public abstract string Ensamblado { get; }

    /// <summary>Levanta el host real de la pieza, con <c>AddOpenApi</c> si se pide.</summary>
    public abstract HostDeLaPieza Levantar(bool conContrato = true);

    /// <summary>El documento que sale del código de la pieza, serializado como el comiteado.</summary>
    public async Task<string> Generar()
    {
        using var host = Levantar();
        var proveedor = host.Servicios.GetRequiredKeyedService<IOpenApiDocumentProvider>(ContratoOpenApi.Documento);
        var doc = await proveedor.GetOpenApiDocumentAsync(CancellationToken.None);
        return await ContratoOpenApi.Serializar(doc);
    }

    public override string ToString() => Ensamblado;
}

/// <inheritdoc />
internal sealed class PiezaPublicada<TMarcador>(string seccion) : PiezaPublicada
    where TMarcador : class
{
    public override string Ensamblado { get; } = typeof(TMarcador).Assembly.GetName().Name!;

    public override HostDeLaPieza Levantar(bool conContrato = true)
    {
        var raiz = Path.Combine(Path.GetTempPath(), "contrato-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(raiz);

        var fabrica = new WebApplicationFactory<TMarcador>().WithWebHostBuilder(b =>
        {
            // Las mismas claves que ArnesDeCapacidades: con la sección de la pieza, o no las lee
            // nadie y el host cae a su almacén por defecto (#174).
            b.UseSetting($"{seccion}:Storage:Root", raiz);
            b.UseSetting($"{seccion}:ApiKey", Llave);
            if (conContrato)
            {
                b.ConfigureTestServices(s => s.AddOpenApi(ContratoOpenApi.Documento, ContratoOpenApi.Opciones(Ensamblado)));
            }
        });

        // CreateClient es lo que arranca el host: sin él, el fallo de arranque saldría más tarde
        // hablando de otra cosa.
        return new HostDeLaPieza(fabrica, fabrica.CreateClient(), fabrica.Services, raiz);
    }
}

/// <summary>Un host de prueba levantado, con su cliente sin llave y su almacén temporal.</summary>
internal sealed class HostDeLaPieza(IDisposable fabrica, HttpClient cliente, IServiceProvider servicios, string raiz)
    : IDisposable
{
    /// <summary>Un cliente SIN la llave compartida: quien la quiera, la pone.</summary>
    public HttpClient Cliente { get; } = cliente;

    public IServiceProvider Servicios { get; } = servicios;

    public void Dispose()
    {
        Cliente.Dispose();
        fabrica.Dispose();
        try { if (Directory.Exists(raiz)) Directory.Delete(raiz, recursive: true); }
        catch (IOException) { /* el host puede tener un fichero aún abierto; es un temporal */ }
    }
}
