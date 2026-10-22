using System.Reflection;
using System.Text.Json.Nodes;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.CMS.Tests.Architecture;
using Synergos.CMS.Tests.Bff.FlujoDeMuestra;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Contratos;

/// <summary>
/// Lo que un orquestador expone en la puerta del CMS (ADR 0140 F3), sobre los contratos COMITEADOS: los
/// que el CMS incrusta y de los que arma su tabla.
/// </summary>
/// <remarks>
/// <para><b>La lista es cerrada por defecto, y por eso lo que importa es lo que NO se marca.</b> Reintentar
/// una compra rendida y la vista de compensaciones son de operación: con la marca, cualquier miembro con
/// sesión podría reintentar compensaciones ajenas o listar las de todos. Y una operación marcada que no
/// declara el sujeto no puede saber de quién es lo que toca.</para>
///
/// <para>La deriva (<c>ContratoOpenApiTests</c>) garantiza que el comiteado es el del código: marcar en
/// el código sin regenerar ya es rojo allá, y regenerado, es rojo acá.</para>
///
/// <para><b>La única excepción al sujeto sale de la declaración</b> (D-001-LECTURAS): una lectura sin saga que una
/// definición del orquestador declara en <c>lecturas</c> (<c>disponibilidad</c>, NB-3) no tiene dueño, y va sin
/// sujeto por diseño. No hay una lista escrita a mano: el gate lee los mismos <c>flujos/*.json</c> que el host.</para>
///
/// <para><b>Y el gate opuesto:</b> una lectura declarada no publica el sujeto, el contacto ni la llave. La puerta la abre
/// sin sesión a quien la defina como de invitados, y no hay de quién ni adónde avisar, ni nada que repetir sin efecto.</para>
/// </remarks>
public sealed class LoQueExponeLaPuertaTests
{
    /// <summary>Los contratos de orquestador que se publican.</summary>
    public static TheoryData<string> Orquestadores()
    {
        var datos = new TheoryData<string>();
        foreach (var p in ContratoOpenApi.Piezas.Where(p => p.Ensamblado.StartsWith("Synergos.Bff.", StringComparison.Ordinal)))
        {
            datos.Add(p.Ensamblado);
        }
        return datos;
    }

    private static IEnumerable<(OperacionPublicada Op, JsonObject Marca)> Marcadas(string ensamblado)
        => Marcadas(ContratoOpenApi.Comiteado(ensamblado));

    [Theory]
    [MemberData(nameof(Orquestadores))]
    public void Ni_reintentar_ni_las_compensaciones_se_abren_al_navegador(string ensamblado)
    {
        var abiertas = Marcadas(ensamblado)
            .Where(m => m.Op.Ruta.EndsWith("/retry", StringComparison.Ordinal)
                        || m.Op.Ruta.StartsWith("/v1/compensations", StringComparison.Ordinal))
            .Select(m => m.Op.ToString())
            .ToList();

        Assert.True(abiertas.Count == 0,
            $"{ensamblado} expone en la puerta operaciones de operación: {string.Join(", ", abiertas)}. "
            + "Cualquier miembro con sesión podría reintentar o listar las compensaciones de todos.");
    }

    /// <summary>Las lecturas que DECLARAN las definiciones de un orquestador, como (flujo, lectura).</summary>
    internal static IReadOnlySet<(string Flujo, string Lectura)> LecturasDeclaradas(Assembly ensamblado, string? carpeta = null)
        => FlujosDeclarados.Cargar(ensamblado, carpeta)
            .SelectMany(l => l.Flujo.LecturasDeclaradas.Keys.Select(lectura => (l.Flujo.Clave, lectura)))
            .ToHashSet();

    /// <summary>
    /// Las operaciones marcadas que no exigen el sujeto, salvo exactamente las lecturas declaradas: el mismo par
    /// (flujo, lectura) con que el host la marca.
    /// </summary>
    internal static IReadOnlyList<string> SinSujeto(
        IEnumerable<(OperacionPublicada Op, JsonObject Marca)> marcadas, IReadOnlySet<(string Flujo, string Lectura)> lecturas)
        => marcadas
            .Where(m => !lecturas.Contains(((string)m.Marca["flujo"]!, (string)m.Marca["operacion"]!)))
            .Where(m => !(m.Op.Op["parameters"]?.AsArray() ?? []).Any(p =>
                (string?)p!["in"] == "header"
                && (string?)p["name"] == CabecerasDeLaPuerta.Sujeto
                && (bool?)p["required"] == true
                && (bool?)p[ContratoOpenApi.MarcaDeLaPuerta] == true))
            .Select(m => m.Op.ToString())
            .ToList();

    /// <summary>Lo que una lectura no lleva: no tiene dueño, no avisa y no escribe nada que repetir.</summary>
    private static readonly string[] NoVanEnUnaLectura = [CabecerasDeLaPuerta.Sujeto, CabecerasDeLaPuerta.Contacto, IdempotencyHeader.Name];

    /// <summary>Las lecturas declaradas que publican una cabecera de <see cref="NoVanEnUnaLectura"/>, con su nombre.</summary>
    internal static IReadOnlyList<string> LecturasConCabeceraDeMas(
        IEnumerable<(OperacionPublicada Op, JsonObject Marca)> marcadas, IReadOnlySet<(string Flujo, string Lectura)> lecturas)
        => marcadas
            .Where(m => lecturas.Contains(((string)m.Marca["flujo"]!, (string)m.Marca["operacion"]!)))
            .SelectMany(m => (m.Op.Op["parameters"]?.AsArray() ?? [])
                .Where(p => (string?)p!["in"] == "header"
                            && NoVanEnUnaLectura.Contains((string?)p["name"], StringComparer.OrdinalIgnoreCase))
                .Select(p => $"{m.Op} ({(string?)p!["name"]})"))
            .ToList();

    private static IEnumerable<(OperacionPublicada Op, JsonObject Marca)> Marcadas(JsonObject doc)
        => ContratoOpenApi.Operaciones(doc)
            .Where(o => o.Op[ContratoOpenApi.MarcaDelFlujo] is JsonObject)
            .Select(o => (o, o.Op[ContratoOpenApi.MarcaDelFlujo]!.AsObject()));

    [Theory]
    [MemberData(nameof(Orquestadores))]
    public void Toda_operacion_expuesta_exige_el_sujeto(string ensamblado)
    {
        var sinSujeto = SinSujeto(Marcadas(ensamblado), LecturasDeclaradas(Assembly.Load(new AssemblyName(ensamblado))));

        Assert.True(sinSujeto.Count == 0,
            $"{ensamblado} expone sin exigir {CabecerasDeLaPuerta.Sujeto}: {string.Join(", ", sinSujeto)}. "
            + "Sin sujeto, el orquestador no puede saber de quién es lo que toca. Sólo una lectura que su definición "
            + "declara en «lecturas» va sin él.");
    }

    [Theory]
    [MemberData(nameof(Orquestadores))]
    public void Una_lectura_declarada_no_lleva_sujeto_contacto_ni_llave(string ensamblado)
    {
        var deMas = LecturasConCabeceraDeMas(Marcadas(ensamblado), LecturasDeclaradas(Assembly.Load(new AssemblyName(ensamblado))));

        Assert.True(deMas.Count == 0,
            $"{ensamblado} publica en una lectura declarada lo que una lectura no lleva: {string.Join(", ", deMas)}. "
            + "Una lectura no tiene dueño, no avisa a nadie y no escribe nada que repetir.");
    }

    [Fact]
    public async Task Va_sin_sujeto_exactamente_la_lectura_que_declara_su_definicion()
    {
        // Ningún orquestador real declara todavía una lectura en este árbol (la enciende 011): la regla se prueba
        // sobre la muestra, que declara «disponibilidad» en muestra.cita y la publica sin sujeto.
        var doc = await ContratoDeFlujosGenericosTests.Documento();
        var lecturas = LecturasDeclaradas(typeof(ArranqueDeLaMuestra).Assembly,
            Path.Combine(Proyectos.Dir("Synergos.Servicios.Tests"), "Bff", "FlujoDeMuestra", "flujos"));
        const string Lectura = "POST /v1/flujos/muestra.cita/disponibilidad";

        Assert.Equal([("muestra.cita", "disponibilidad")], lecturas);
        Assert.Empty(SinSujeto(Marcadas(doc), lecturas));
        Assert.Equal([Lectura], SinSujeto(Marcadas(doc), new HashSet<(string, string)>()));
        Assert.Equal([Lectura], SinSujeto(Marcadas(doc), new HashSet<(string, string)> { ("muestra.compra", "disponibilidad") }));
        Assert.Empty(LecturasConCabeceraDeMas(Marcadas(doc), lecturas));

        // El gate opuesto: la misma lectura, si publicara el sujeto, el contacto o la llave, sale con su nombre.
        var disponibilidad = doc["paths"]!["/v1/flujos/muestra.cita/disponibilidad"]!["post"]!.AsObject();
        var cabeceras = disponibilidad["parameters"] as JsonArray ?? [];
        disponibilidad["parameters"] = cabeceras;
        foreach (var nombre in new[] { "x-synergos-sujeto", CabecerasDeLaPuerta.Contacto, IdempotencyHeader.Name })
        {
            cabeceras.Add(new JsonObject { ["name"] = nombre, ["in"] = "header", ["required"] = false });
        }
        Assert.Equal(
            [$"{Lectura} (x-synergos-sujeto)", $"{Lectura} ({CabecerasDeLaPuerta.Contacto})", $"{Lectura} ({IdempotencyHeader.Name})"],
            LecturasConCabeceraDeMas(Marcadas(doc), lecturas));
        Assert.Empty(LecturasConCabeceraDeMas(Marcadas(doc), new HashSet<(string, string)>()));

        // Y una operación que no es una lectura sigue exigiéndolo aunque su flujo declare una.
        var consultar = doc["paths"]!["/v1/flujos/muestra.cita/{id}"]!["get"]!["parameters"]!.AsArray();
        consultar.Remove(consultar.Single(p => (string?)p!["name"] == CabecerasDeLaPuerta.Sujeto));
        Assert.Equal(["GET /v1/flujos/muestra.cita/{id}"], SinSujeto(Marcadas(doc), lecturas));
    }

    [Fact]
    public void Eventos_expone_la_compra_y_nada_mas()
    {
        var marcadas = Marcadas("Synergos.Bff.Eventos")
            .Select(m => $"{m.Op.Op["operationId"]} {m.Marca["flujo"]}/{m.Marca["operacion"]}")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
        [
            "BuyTickets eventos.compra/abrir",
            "CancelTicketPurchase eventos.compra/cancelar",
            "ConfirmTicketPurchase eventos.compra/cerrar",
            "GetTicketPurchase eventos.compra/consultar",
            "QuoteTickets eventos.compra/cotizar",
        ], marcadas);
    }
}
