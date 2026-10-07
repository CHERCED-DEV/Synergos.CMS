using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Synergos.CMS.Tests.Contratos;

/// <summary>
/// La respuesta de toda operación publicada la declara su TIPO DE RETORNO, y nada más (ADR 0140, F2).
/// </summary>
/// <remarks>
/// <para><b>Por qué desde el host y no sólo desde la fuente.</b> <c>ContratoHttpPublicadoTests</c>
/// busca el nombre <c>Produces*</c> en el código, y eso depende de cómo se escriba: el atributo sin
/// punto delante lo esquivó (medido: <c>[ProducesResponseType&lt;PriceResponse&gt;(200)]</c> sobre un
/// <c>GetPrice</c> que devolvía otra forma dejaba el documento idéntico byte a byte y las suites en
/// verde). Aquí se mira lo que ApiExplorer LEE para armar el documento —los metadatos del endpoint
/// en el host real—, así que da igual la sintaxis, el alias o el ayudante que lo esconda.</para>
///
/// <para><b>Tres reglas por operación.</b> (1) El handler devuelve un tipo concreto: un
/// <c>IResult</c> o un <c>object</c> no dicen qué sale, y el documento lo toma de lo que alguien
/// escriba al lado. (2) Ningún atributo de respuesta de MVC (<c>[ProducesResponseType]</c>,
/// <c>[Produces]</c>, <c>[ProducesDefaultResponseType]</c>). (3) Toda respuesta que declaran los
/// metadatos la explica el tipo de retorno: se le pide al tipo que se describa
/// (<c>IEndpointMetadataProvider.PopulateMetadata</c>, lo mismo que hace Minimal APIs al mapear), y lo
/// que sobra lo escribió alguien a mano —un <c>.Produces&lt;T&gt;()</c> o un
/// <c>.WithMetadata(new ProducesResponseTypeMetadata(…))</c> dentro de un ayudante que el gate de la
/// fuente no lee—.</para>
///
/// <para>Recorre las operaciones del documento COMITEADO y las busca en el host por su
/// <c>operationId</c>: lo publicado es lo que se juzga, y una operación que el host ya no tiene es
/// rojo aquí antes que deriva allá.</para>
/// </remarks>
public sealed class RespuestaPorElTipoDeRetornoTests
{
    public static TheoryData<string> Piezas() => ContratoOpenApiTests.Piezas();

    [Theory]
    [MemberData(nameof(Piezas))]
    public void La_respuesta_de_cada_operacion_la_declara_su_tipo_de_retorno(string ensamblado)
    {
        var doc = ContratoOpenApi.Comiteado(ensamblado);
        using var host = ContratoOpenApi.Pieza(ensamblado).Levantar(conContrato: false);
        var endpoints = host.Servicios.GetRequiredService<EndpointDataSource>().Endpoints;
        var malas = new List<string>();
        var vistas = 0;

        foreach (var op in ContratoOpenApi.Operaciones(doc))
        {
            var nombre = (string?)op.Op["operationId"];
            var endpoint = endpoints.SingleOrDefault(e =>
                string.Equals(e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName, nombre, StringComparison.Ordinal));
            if (endpoint is null)
            {
                malas.Add($"{op}: el host no tiene ningún endpoint llamado «{nombre}».");
                continue;
            }

            var metodo = endpoint.Metadata.OfType<MethodInfo>().FirstOrDefault();
            if (metodo is null)
            {
                malas.Add($"{op}: el endpoint no deja su handler en los metadatos; no se puede saber qué devuelve.");
                continue;
            }

            vistas++;
            var tipo = SinTarea(metodo.ReturnType);
            if (tipo is null || tipo.IsInterface || tipo == typeof(object))
            {
                malas.Add($"{op}: el handler devuelve {metodo.ReturnType.Name}, que no dice qué sale. Devolvé su tipo (TypedResults).");
            }

            foreach (var m in endpoint.Metadata.Where(m => m is IApiResponseMetadataProvider
                         or IApiDefaultResponseMetadataProvider or IApiResponseTypeMetadataProvider))
            {
                malas.Add($"{op}: lleva [{m.GetType().Name}], una respuesta escrita a mano al lado del tipo de retorno.");
            }

            var declaradas = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>().Select(Firma).ToList();
            foreach (var explicada in tipo is null ? Enumerable.Empty<string>() : DelTipo(tipo, metodo, host.Servicios))
            {
                declaradas.Remove(explicada);
            }

            malas.AddRange(declaradas.Select(d =>
                $"{op}: declara la respuesta {d} y su tipo de retorno no la produce (¿un .Produces o un metadato a mano?)."));
        }

        Assert.True(vistas >= 4, $"{ensamblado}: sólo se juzgaron {vistas} operaciones; el cruce con el host está roto.");
        Assert.True(malas.Count == 0,
            $"{ensamblado}: la respuesta de una operación publicada no la declara (sólo) su tipo de retorno. " +
            $"Lo escrito a mano deja el documento mintiendo con la deriva en verde.{Environment.NewLine}" +
            string.Join(Environment.NewLine, malas.Select(m => "  " + m)));
    }

    /// <summary>El tipo que sale de verdad: sin <c>Task&lt;T&gt;</c>/<c>ValueTask&lt;T&gt;</c>, y <c>null</c> si no sale ninguno.</summary>
    private static Type? SinTarea(Type t)
    {
        if (t == typeof(void) || t == typeof(Task) || t == typeof(ValueTask)) return null;
        return t.IsGenericType && (t.GetGenericTypeDefinition() == typeof(Task<>) || t.GetGenericTypeDefinition() == typeof(ValueTask<>))
            ? t.GetGenericArguments()[0]
            : t;
    }

    /// <summary>Las respuestas que el tipo de retorno declara de sí mismo, como lo pide Minimal APIs al mapear.</summary>
    private static IEnumerable<string> DelTipo(Type tipo, MethodInfo metodo, IServiceProvider servicios)
    {
        if (!typeof(IEndpointMetadataProvider).IsAssignableFrom(tipo)) return [];

        var builder = new RouteEndpointBuilder(null, RoutePatternFactory.Parse("/"), 0) { ApplicationServices = servicios };
        typeof(RespuestaPorElTipoDeRetornoTests)
            .GetMethod(nameof(Poblar), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(tipo)
            .Invoke(null, [metodo, builder]);
        return builder.Metadata.OfType<IProducesResponseTypeMetadata>().Select(Firma).ToList();
    }

    private static void Poblar<T>(MethodInfo metodo, EndpointBuilder builder)
        where T : IEndpointMetadataProvider
        => T.PopulateMetadata(metodo, builder);

    private static string Firma(IProducesResponseTypeMetadata m)
        => $"{m.StatusCode} {m.Type?.Name ?? "(sin cuerpo)"} [{string.Join(", ", m.ContentTypes)}]";
}
