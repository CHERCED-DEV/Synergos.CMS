using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Tests.Services;
using Synergos.CMS.Web.Notifications;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Synergos.CMS.Tests.Contracts;

/// <summary>
/// Lo que el CMS le manda a <c>Bff.Reservas</c> cabe en el contrato que el orquestador publica
/// (<c>docs/contracts/openapi/Synergos.Bff.Reservas.json</c>, 011-booking-reserva): el gate de consumidor
/// CMS → orquestador, como el de Eventos (principio III).
/// </summary>
/// <remarks>
/// <para><b>Contra el documento COMITEADO</b>, que la deriva mantiene fiel al código del orquestador
/// (<c>ContratoOpenApiTests</c>, en la otra suite): consumidor → documento → productor, sin que esta suite
/// referencie el árbol de servicios.</para>
///
/// <para><b>Lo que se juzga es lo que SALE</b> del publicar real, grabado del cliente: la ruta y el método
/// existen, la llave está declarada y cabe, y cada campo del cuerpo está en el esquema con un tipo que admite.
/// Un campo renombrado —<c>duracion</c> por <c>duracionMinutos</c>— lo rechazaría el orquestador y la ficha no
/// quedaría reservable.</para>
/// </remarks>
public sealed class ConsumidorCmsDeBffReservasTests
{
    private static readonly Lazy<JsonDocument> Documento = new(() => JsonDocument.Parse(File.ReadAllText(Path.Combine(
        RepoRoot(), "Synergos.CMS.Web", "docs", "contracts", "openapi", "Synergos.Bff.Reservas.json"))));

    private static JsonElement Raiz => Documento.Value.RootElement;

    [Fact]
    public async Task Lo_que_manda_el_publicar_de_una_ficha_reservable_cabe_en_PublishServiceOffer()
    {
        var orquestador = new PublicadorDeOfertasDeReservasTests.OrquestadorFalso();
        var alPublicar = new OfertaDeReservaAlPublicar(
            PublicadorDeOfertasDeReservasTests.Publicador(orquestador),
            Options.Create(new CartSettings()),
            Options.Create(new ListadosSettings()),
            NullLogger<OfertaDeReservaAlPublicar>.Instance);

        await alPublicar.HandleAsync(new ContentPublishedNotification(PublicadorDeOfertasDeReservasTests.Ficha(), new EventMessages()), default);

        var (ruta, llave, cuerpo) = Assert.Single(orquestador.Llamadas);
        Assert.True(Raiz.GetProperty("paths").TryGetProperty(ruta, out var operaciones) && operaciones.TryGetProperty("post", out _),
            $"El orquestador no publica POST {ruta}.");
        var op = operaciones.GetProperty("post");
        Assert.Equal("PublishServiceOffer", op.GetProperty("operationId").GetString());
        AdmiteLaLlave(op, llave);

        var esquema = op.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        var malas = new List<string>();
        using var enviado = JsonDocument.Parse(cuerpo);
        Cabe(enviado.RootElement, esquema, "$", malas);

        Assert.True(malas.Count == 0,
            $"Lo que manda el publicador no cabe en PublishServiceOffer:{Environment.NewLine}" +
            string.Join(Environment.NewLine, malas.Select(m => "  " + m)));
        // Y el recorrido no fue en vano: el cuerpo trae el horario con sus bloques.
        Assert.Contains("\"desde\"", cuerpo, StringComparison.Ordinal);
    }

    /// <summary>
    /// El retiro de la oferta (al despublicar o borrar una ficha) existe en el orquestador con la ruta y la llave
    /// que manda el CMS: si no, una ficha despublicada se seguiría reservando por la puerta.
    /// </summary>
    [Fact]
    public async Task Lo_que_manda_el_retiro_de_la_oferta_existe_en_RetireServiceOffer()
    {
        var orquestador = new PublicadorDeOfertasDeReservasTests.OrquestadorFalso();

        await PublicadorDeOfertasDeReservasTests.Publicador(orquestador).RetirarAsync("svc-1");

        var (ruta, llave, cuerpo) = Assert.Single(orquestador.Llamadas);
        Assert.Equal("/v1/ofertas/svc-1/retirar", ruta);
        var op = Raiz.GetProperty("paths").GetProperty("/v1/ofertas/{servicio}/retirar").GetProperty("post");
        Assert.Equal("RetireServiceOffer", op.GetProperty("operationId").GetString());
        AdmiteLaLlave(op, llave);
        Assert.False(op.TryGetProperty("requestBody", out _));
        Assert.Equal("", cuerpo);
    }

    private static void AdmiteLaLlave(JsonElement op, string? llave)
    {
        var cabecera = op.GetProperty("parameters").EnumerateArray()
            .Single(p => p.GetProperty("in").GetString() == "header" && p.GetProperty("name").GetString() == "Idempotency-Key");
        Assert.NotNull(llave);
        if (cabecera.GetProperty("schema").TryGetProperty("maxLength", out var largo))
        {
            Assert.True(llave!.Length <= largo.GetInt32());
        }
    }

    /// <summary>Si <paramref name="valor"/> cabe en <paramref name="esquema"/>; lo que no, a <paramref name="malas"/>.</summary>
    private static void Cabe(JsonElement valor, JsonElement esquema, string donde, List<string> malas)
    {
        esquema = Resolver(esquema);

        if (esquema.TryGetProperty("type", out var tipo))
        {
            var admitidos = tipo.ValueKind == JsonValueKind.Array
                ? tipo.EnumerateArray().Select(t => t.GetString()!).ToHashSet(StringComparer.Ordinal)
                : [tipo.GetString()!];
            if (!admitidos.Contains(TipoDe(valor)) && !(TipoDe(valor) == "integer" && admitidos.Contains("number")))
            {
                malas.Add($"{donde}: manda {TipoDe(valor)} y el esquema admite {string.Join("|", admitidos)}.");
                return;
            }
        }

        switch (valor.ValueKind)
        {
            case JsonValueKind.Object:
                var propiedades = esquema.TryGetProperty("properties", out var p) ? p : default;
                foreach (var campo in valor.EnumerateObject())
                {
                    if (propiedades.ValueKind != JsonValueKind.Object || !propiedades.TryGetProperty(campo.Name, out var sub))
                    {
                        malas.Add($"{donde}.{campo.Name}: el esquema no lo declara.");
                        continue;
                    }
                    Cabe(campo.Value, sub, $"{donde}.{campo.Name}", malas);
                }
                if (esquema.TryGetProperty("required", out var requeridos))
                {
                    foreach (var r in requeridos.EnumerateArray().Select(r => r.GetString()!))
                    {
                        if (!valor.TryGetProperty(r, out _)) malas.Add($"{donde}.{r}: el esquema lo exige y no se manda.");
                    }
                }
                break;
            case JsonValueKind.Array when esquema.TryGetProperty("items", out var items):
                var i = 0;
                foreach (var elemento in valor.EnumerateArray()) Cabe(elemento, items, $"{donde}[{i++}]", malas);
                break;
        }
    }

    private static JsonElement Resolver(JsonElement esquema)
    {
        // Un anulable sale como oneOf [null, $ref]: se mira la rama que no es nula.
        if (esquema.TryGetProperty("oneOf", out var ramas))
        {
            esquema = ramas.EnumerateArray().First(r => !(r.TryGetProperty("type", out var t) && t.GetString() == "null"));
        }
        while (esquema.TryGetProperty("$ref", out var referencia))
        {
            esquema = Raiz.GetProperty("components").GetProperty("schemas").GetProperty(referencia.GetString()!.Split('/').Last());
        }
        return esquema;
    }

    private static string TipoDe(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Null => "null",
        JsonValueKind.String => "string",
        JsonValueKind.Number => v.TryGetInt64(out _) ? "integer" : "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Array => "array",
        _ => "object",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
