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
/// registra la máquina de cada una.</para>
/// </remarks>
public static class OrquestadorDeclarado
{
    /// <summary>
    /// Registra la máquina de la saga genérica con el deshacer común, los adaptadores HTTP de las
    /// capacidades, los pasos comunes más <paramref name="propios"/>, y los flujos genéricos del ensamblado.
    /// </summary>
    public static WebApplicationBuilder AddOrquestadorDeclarado(
        this WebApplicationBuilder builder, SagaVocabulary vocabulario, Assembly ensamblado, params IPaso[] propios)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddSagaMachinery<SagaDeFlujo, DeshacerComun>(vocabulario, [.. PuertosHttp.Capacidades]);

        builder.Services.AddSingleton<PuertosHttp>();
        builder.Services.AddSingleton<IPricingPort>(sp => sp.GetRequiredService<PuertosHttp>());
        builder.Services.AddSingleton<IInventoryPort>(sp => sp.GetRequiredService<PuertosHttp>());
        builder.Services.AddSingleton<IPaymentsPort>(sp => sp.GetRequiredService<PuertosHttp>());
        builder.Services.AddSingleton<INotificationsPort>(sp => sp.GetRequiredService<PuertosHttp>());
        builder.Services.AddSingleton<IBookingPort>(sp => sp.GetRequiredService<PuertosHttp>());
        builder.Services.AddSingleton<IDeshacerExistencias>(sp => sp.GetRequiredService<PuertosHttp>());
        builder.Services.AddSingleton<IDeshacerCobros>(sp => sp.GetRequiredService<PuertosHttp>());
        builder.Services.AddSingleton<IDeshacerAgenda>(sp => sp.GetRequiredService<PuertosHttp>());

        builder.Services.AddSingleton(TiposDeValor.Con(new TipoContacto()));
        builder.Services.AddSingleton<IRegistroDePasos>(sp => PasosComunes.Registro(sp.GetRequiredService<PuertosHttp>(), propios));
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
            new PasoInventarioHallar(puertos), new PasoInventarioApartar(puertos), new PasoInventarioConsumir(puertos),
            new PasoPagosAutorizar(puertos), new PasoPagosCapturar(puertos),
            new PasoAgendaHallar(puertos), new PasoAgendaApartar(puertos), new PasoAgendaApartarPorItem(puertos),
            new PasoAgendaConfirmar(puertos), new PasoAgendaConfirmarPorItem(puertos), new PasoAgendaLiberar(puertos),
            new PasoAvisar(puertos), new PasoUnaLinea(),
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
