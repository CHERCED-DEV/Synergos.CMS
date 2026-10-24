using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos.Http;

namespace Synergos.Bff.Pasos;

/// <summary>
/// El arranque de un orquestador del caso común (núcleo N1, P09): una llamada, y sus flujos genéricos
/// corren con saga, deshacer, pasos y adaptadores comunes.
/// </summary>
/// <remarks>
/// <para><b>Lo que un vertical escribe</b>, entonces, es su <c>flujos/*.json</c>, sus pasos de dominio si
/// los tiene, la configuración de sus capacidades y esto en su <c>Program.cs</c>:</para>
/// <code>
/// builder.AddOrquestadorDeclarado(new SagaVocabulary("salud", "la cita"), typeof(Program).Assembly);
/// …
/// app.MapFlujosDeclarados();
/// </code>
/// <para><b>Los flujos tipados no pasan por aquí</b> (Eventos): su saga está fijada byte a byte y su
/// orquestador los registra con su binding, como siempre. Un orquestador con los dos tipos de saga
/// registra la máquina de cada una (núcleo N2, NN-15): la genérica guarda en su propia subcarpeta
/// (<see cref="CarpetaDeLosFlujos"/>) y la tipada sigue en la raíz con sus datos de producción. Los clientes
/// de las capacidades comunes se configuran una sola vez, con la llave puesta una vez. Lo prueba
/// <c>DosMaquinasTests</c>.</para>
/// </remarks>
public static class OrquestadorDeclarado
{
    /// <summary>La subcarpeta de la raíz del almacén donde vive la saga genérica.</summary>
    public const string CarpetaDeLosFlujos = "flujos";

    /// <summary>
    /// Registra la máquina de la saga genérica con el deshacer común, los adaptadores HTTP de las
    /// capacidades, los pasos comunes más <paramref name="propios"/>, y los flujos genéricos del ensamblado.
    /// </summary>
    public static WebApplicationBuilder AddOrquestadorDeclarado(
        this WebApplicationBuilder builder, SagaVocabulary vocabulario, Assembly ensamblado, params IPaso[] propios)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddSagaMachinery<SagaDeFlujo, DeshacerComun>(vocabulario, [.. PuertosHttp.Capacidades]);
        builder.Services.AddSingleton(new CarpetaDeSagas<SagaDeFlujo>(CarpetaDeLosFlujos));

        builder.Services.AddSingleton<PuertosHttp>();
        builder.Services.AddSingleton<IPricingPort>(sp => sp.GetRequiredService<PuertosHttp>());
        builder.Services.AddSingleton<IInventoryPort>(sp => sp.GetRequiredService<PuertosHttp>());
        builder.Services.AddSingleton<IPaymentsPort>(sp => sp.GetRequiredService<PuertosHttp>());
        builder.Services.AddSingleton<INotificationsPort>(sp => sp.GetRequiredService<PuertosHttp>());
        builder.Services.AddSingleton<IBookingPort>(sp => sp.GetRequiredService<PuertosHttp>());
        builder.Services.AddSingleton<IDeshacerExistencias>(sp => sp.GetRequiredService<PuertosHttp>());
        builder.Services.AddSingleton<IDeshacerCobros>(sp => sp.GetRequiredService<PuertosHttp>());
        builder.Services.AddSingleton<IDeshacerAgenda>(sp => sp.GetRequiredService<PuertosHttp>());
        builder.Services.AddSingleton<IConsentPort>(sp => sp.GetRequiredService<PuertosHttp>());
        builder.Services.AddSingleton<ICartPort>(sp => sp.GetRequiredService<PuertosHttp>());
        builder.Services.AddSingleton<IOrdersPort>(sp => sp.GetRequiredService<PuertosHttp>());
        builder.Services.AddSingleton<IFulfillmentPort>(sp => sp.GetRequiredService<PuertosHttp>());
        builder.Services.AddSingleton<IDeshacerPedidos>(sp => sp.GetRequiredService<PuertosHttp>());
        builder.Services.AddSingleton<IWorkflowPort>(sp => sp.GetRequiredService<PuertosHttp>());

        builder.Services.AddSingleton(TiposDeValor.Con(new TipoContacto(), new TipoDireccion(), new TipoFranjas()));
        // flow.leer-original (P6) lee la saga original en el MISMO almacén: una operación posterior vive en el orquestador
        // de su original.
        builder.Services.AddSingleton<IRegistroDePasos>(sp => PasosComunes.Registro(sp.GetRequiredService<PuertosHttp>(),
            [new PasoLeerOriginal(sp.GetRequiredService<ISagaStore<SagaDeFlujo>>(), sp.GetRequiredService<TiposDeValor>()), .. propios]));
        return builder.AddFlujosGenericos(vocabulario, ensamblado);
    }
}

/// <summary>Los pasos comunes sobre los adaptadores HTTP: los que cualquier flujo declarado puede nombrar.</summary>
public static class PasosComunes
{
    /// <summary>Los pasos comunes, cumplidos por <paramref name="puertos"/>.</summary>
    public static IReadOnlyList<IPaso> De(PuertosHttp puertos)
        =>
        [
            new PasoCotizacion(puertos), new PasoCotizacionSinCodigo(puertos), new PasoCotizacionOCero(puertos), new PasoDesglose(),
            new PasoPreciosPorLinea(),
            new PasoInventarioHallar(puertos), new PasoInventarioApartar(puertos), new PasoInventarioConsumir(puertos),
            new PasoPagosAutorizar(puertos), new PasoPagosCapturar(puertos), new PasoPagosConservar(),
            new PasoPagosDevolver(puertos), new PasoPagosAnular(puertos), new PasoAgendaCancelar(puertos),
            new PasoAgendaHallar(puertos), new PasoAgendaApartar(puertos), new PasoAgendaApartarPorItem(puertos),
            new PasoAgendaConfirmar(puertos), new PasoAgendaConfirmarPorItem(puertos), new PasoAgendaLiberar(puertos),
            new PasoAgendaApartarPorIndice(puertos), new PasoAgendaConfirmarPorIndice(puertos), new PasoAgendaFranjas(puertos),
            new PasoAvisar(puertos), new PasoUnaLinea(),
            new PasoConsentimientoVerificar(puertos), new PasoCanastaLeer(puertos), new PasoCanastaCerrar(puertos),
            new PasoPedidoCrear(puertos), new PasoPedidoCumplir(puertos), new PasoDespachar(puertos),
            new PasoProcesoArrancar(puertos), new PasoProcesoDisparar(puertos), new PasoProcesoDispararElegida(puertos),
            new PasoProcesoLeer(puertos),
        ];

    /// <summary>El registro con los comunes y los del dominio. Un tipo repetido no arranca.</summary>
    public static IRegistroDePasos Registro(PuertosHttp puertos, params IPaso[] propios)
        => new RegistroDePasos(De(puertos).Concat(propios));
}

/// <summary>
/// <c>contacto</c>: adónde y a nombre de quién va un aviso, <c>{ correo, nombre?, enlace?, sitio? }</c>, como
/// lo escribe la puerta en <c>X-Synergos-Contacto</c>. Sólo efímero: NO se guarda (es un dato personal).
/// </summary>
public sealed class TipoContacto : ITipoDeValor
{
    public string Nombre => TipoDeclarado.TipoDelContacto;
    public Type Clr => typeof(Contacto);
    public bool Guardable => false;

    public object Leer(JsonElement valor, string donde)
    {
        if (valor.ValueKind != JsonValueKind.Object) throw new FormatException($"{donde}: tiene que ser un objeto {{ correo, nombre, enlace, sitio }}.");
        foreach (var p in valor.EnumerateObject().Where(p => p.Name is not ("correo" or "nombre" or "enlace" or "sitio")))
        {
            throw new FormatException($"{donde}: trae «{p.Name}», y sólo admite correo, nombre, enlace y sitio.");
        }

        var correo = Texto(valor, "correo");
        if (string.IsNullOrWhiteSpace(correo)) throw new FormatException($"{donde}: sin «correo» no hay a quién avisar.");
        return new Contacto(correo, Texto(valor, "nombre"), Texto(valor, "enlace"), Texto(valor, "sitio"));

        string? Texto(JsonElement o, string campo)
            => !o.TryGetProperty(campo, out var v) || v.ValueKind == JsonValueKind.Null ? null
                : v.ValueKind == JsonValueKind.String ? v.GetString()
                : throw new FormatException($"{donde}.{campo}: tiene que ser un texto.");
    }

    public JsonNode Escribir(object valor)
    {
        var c = (Contacto)valor;
        return new JsonObject { ["correo"] = c.Correo, ["nombre"] = c.Nombre, ["enlace"] = c.Enlace, ["sitio"] = c.Sitio };
    }

    public JsonObject Esquema() => new()
    {
        ["type"] = "object",
        ["required"] = new JsonArray("correo"),
        ["properties"] = new JsonObject
        {
            ["correo"] = new JsonObject { ["type"] = "string" },
            ["nombre"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
            ["enlace"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
            ["sitio"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
        },
    };
}

/// <summary>
/// <c>franjas</c>: las franjas de una agenda con lo libre de cada una, <c>[{ inicio, fin, libres }]</c> (núcleo, NB-3
/// de 011). Lo que devuelve una lectura de disponibilidad; NO se guarda: una lista no es estado de una saga, y lo
/// libre de hace un minuto no vale para nada.
/// </summary>
public sealed class TipoFranjas : ITipoDeValor
{
    private static readonly string[] Campos = ["inicio", "fin", "libres"];

    public string Nombre => "franjas";
    public Type Clr => typeof(IReadOnlyList<FranjaLibre>);
    public bool Guardable => false;

    public object Leer(JsonElement valor, string donde)
    {
        if (valor.ValueKind != JsonValueKind.Array) throw new FormatException($"{donde}: tiene que ser una lista de franjas.");

        var instante = new Instante();
        var franjas = new List<FranjaLibre>();
        var i = 0;
        foreach (var f in valor.EnumerateArray())
        {
            var aqui = $"{donde}[{i++}]";
            if (f.ValueKind != JsonValueKind.Object) throw new FormatException($"{aqui}: tiene que ser un objeto {{ inicio, fin, libres }}.");
            foreach (var p in f.EnumerateObject().Where(p => !Campos.Contains(p.Name, StringComparer.Ordinal)))
            {
                throw new FormatException($"{aqui}: trae «{p.Name}», y sólo admite inicio, fin y libres.");
            }
            if (!f.TryGetProperty("libres", out var libres) || !libres.TryGetInt32(out var cuantas) || cuantas < 0)
            {
                throw new FormatException($"{aqui}.libres: tiene que ser un entero sin signo.");
            }
            franjas.Add(new FranjaLibre(
                (DateTimeOffset)instante.Leer(f.TryGetProperty("inicio", out var ini) ? ini : default, $"{aqui}.inicio"),
                (DateTimeOffset)instante.Leer(f.TryGetProperty("fin", out var fin) ? fin : default, $"{aqui}.fin"),
                cuantas));
        }
        return franjas;
    }

    public JsonNode Escribir(object valor)
    {
        var instante = new Instante();
        return new JsonArray(((IReadOnlyList<FranjaLibre>)valor)
            .Select(f => (JsonNode)new JsonObject
            {
                ["inicio"] = instante.Escribir(f.Inicio), ["fin"] = instante.Escribir(f.Fin), ["libres"] = f.Libres,
            })
            .ToArray());
    }

    public JsonObject Esquema() => new()
    {
        ["type"] = "array",
        ["items"] = new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray(Campos.Select(c => (JsonNode?)c).ToArray()),
            ["properties"] = new JsonObject
            {
                ["inicio"] = new Instante().Esquema(),
                ["fin"] = new Instante().Esquema(),
                ["libres"] = new JsonObject { ["type"] = "integer", ["format"] = "int32" },
            },
        },
    };
}

/// <summary>
/// <c>direccion</c>: adónde se despacha, <c>{ linea1, linea2?, ciudad, region?, codigoPostal?, pais, contacto? }</c>
/// (núcleo N2). Sólo efímero: NO se guarda (es un dato personal), como el contacto del aviso.
/// </summary>
public sealed class TipoDireccion : ITipoDeValor
{
    private static readonly string[] Campos = ["linea1", "linea2", "ciudad", "region", "codigoPostal", "pais", "contacto"];
    private static readonly string[] Obligatorios = ["linea1", "ciudad", "pais"];

    public string Nombre => "direccion";
    public Type Clr => typeof(DireccionDeEnvio);
    public bool Guardable => false;

    public object Leer(JsonElement valor, string donde)
    {
        if (valor.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"{donde}: tiene que ser un objeto {{ {string.Join(", ", Campos)} }}.");
        }
        foreach (var p in valor.EnumerateObject().Where(p => !Campos.Contains(p.Name, StringComparer.Ordinal)))
        {
            throw new FormatException($"{donde}: trae «{p.Name}», y sólo admite {string.Join(", ", Campos)}.");
        }
        foreach (var falta in Obligatorios.Where(c => string.IsNullOrWhiteSpace(Texto(valor, c))))
        {
            throw new FormatException($"{donde}: sin «{falta}» no hay adónde despachar.");
        }

        return new DireccionDeEnvio(
            Texto(valor, "linea1")!, Texto(valor, "linea2"), Texto(valor, "ciudad")!, Texto(valor, "region"),
            Texto(valor, "codigoPostal"), Texto(valor, "pais")!, Texto(valor, "contacto"));

        string? Texto(JsonElement o, string campo)
            => !o.TryGetProperty(campo, out var v) || v.ValueKind == JsonValueKind.Null ? null
                : v.ValueKind == JsonValueKind.String ? v.GetString()
                : throw new FormatException($"{donde}.{campo}: tiene que ser un texto.");
    }

    public JsonNode Escribir(object valor)
    {
        var d = (DireccionDeEnvio)valor;
        return new JsonObject
        {
            ["linea1"] = d.Linea1, ["linea2"] = d.Linea2, ["ciudad"] = d.Ciudad, ["region"] = d.Region,
            ["codigoPostal"] = d.CodigoPostal, ["pais"] = d.Pais, ["contacto"] = d.Contacto,
        };
    }

    public JsonObject Esquema() => new()
    {
        ["type"] = "object",
        ["required"] = new JsonArray(Obligatorios.Select(c => (JsonNode?)c).ToArray()),
        ["properties"] = new JsonObject(Campos.Select(c => KeyValuePair.Create(c, (JsonNode?)new JsonObject
        {
            ["type"] = Obligatorios.Contains(c, StringComparer.Ordinal) ? (JsonNode)"string" : new JsonArray("string", "null"),
        }))),
    };
}
