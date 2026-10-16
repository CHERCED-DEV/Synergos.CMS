using System.Net;
using System.Text.Json;
using Synergos.Bff.Core;
using Synergos.Bff.Reservas.Domain;
using Synergos.CMS.Tests.Architecture;

namespace Synergos.CMS.Tests.Bff.Reservas;

/// <summary>
/// Historia 1 de 011-booking-reserva: reservar un servicio en una franja y pagarlo, por la puerta, contra las
/// capacidades reales (agenda, precios, pagos, avisos).
/// </summary>
public sealed class ReservaDeServicioTests
{
    private static async Task<int> Ocupadas(ReservaDeServicioReal r, DateTimeOffset? inicio = null, int minutos = 60)
    {
        var recurso = (await r.Leer("booking", $"v1/resources?subjectKind=reservas.servicio&subjectId={ReservaDeServicioReal.Servicio}"))
            .GetProperty("id").GetString();
        var desde = inicio ?? ReservaDeServicioReal.Inicio;
        var a = await r.Leer("booking",
            $"v1/resources/{recurso}/availability?start={Uri.EscapeDataString(desde.ToString("O"))}&end={Uri.EscapeDataString(desde.AddMinutes(minutos).ToString("O"))}");
        return a.GetProperty("taken").GetInt32();
    }

    /// <summary>El estado de cada reserva de la agenda a nombre del cliente (la saga no guarda su id: FR-014).</summary>
    internal static async Task<List<string>> Reservas(ReservaDeServicioReal r, string cliente = "u-1")
        => (await r.Leer("booking", $"v1/reservations?forKind=reservas.cliente&forId={cliente}")).GetProperty("items")
            .EnumerateArray().Select(i => i.GetProperty("status").GetString()!).ToList();

    [Fact]
    public async Task Abre_aparta_y_autoriza_cierra_captura_confirma_y_avisa_una_vez()
    {
        using var r = await new ReservaDeServicioReal().ConOferta();

        var (estado, abierta) = await r.Abrir();
        Assert.True(estado == HttpStatusCode.Created, abierta.ToString());
        Assert.Equal("Running", abierta.GetProperty("estado").GetString());
        Assert.Equal(140_000m, abierta.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal());
        var id = abierta.GetProperty("id").GetString()!;
        Assert.Equal(1, await Ocupadas(r));
        var cobro = r.Dato(id, "cobroId");
        Assert.Equal("Authorized", (await r.Leer("payments", $"v1/payments/{cobro}")).GetProperty("status").GetString());

        var (cerro, cerrada) = await r.Cerrar(id);
        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());

        Assert.Equal(["Confirmed"], await Reservas(r));
        Assert.Equal("Captured", (await r.Leer("payments", $"v1/payments/{cobro}")).GetProperty("status").GetString());
        Assert.Equal(1, await r.Avisos());
    }

    [Fact]
    public async Task La_respuesta_dice_servicio_franja_y_total_y_ningun_identificador_de_las_capacidades()
    {
        using var r = await new ReservaDeServicioReal().ConOferta();
        var (_, abierta) = await r.Abrir();
        var (_, cerrada) = await r.Cerrar(abierta.GetProperty("id").GetString()!);

        var datos = cerrada.GetProperty("datos").EnumerateObject().Select(p => p.Name).Order().ToList();
        Assert.Equal(["servicio", "total", "ventana"], datos);
        var servicio = cerrada.GetProperty("datos").GetProperty("servicio");
        Assert.Equal(OfertaDeReservas.Clase, servicio.GetProperty("kind").GetString());
        Assert.Equal(ReservaDeServicioReal.Servicio, servicio.GetProperty("id").GetString());
    }

    [Fact]
    public async Task El_servicio_lleva_su_clase_fija_en_la_agenda_y_en_la_tarifa()
    {
        using var r = await new ReservaDeServicioReal().ConOferta();
        var (_, abierta) = await r.Abrir();
        var apartado = r.Dato(abierta.GetProperty("id").GetString()!, "apartadoId");

        var hold = await r.Leer("booking", $"v1/holds/{apartado}");
        var recurso = await r.Leer("booking", $"v1/resources/{hold.GetProperty("resourceId").GetString()}");
        Assert.Equal("reservas.servicio", recurso.GetProperty("subjectKind").GetString());
        Assert.Equal(ReservaDeServicioReal.Servicio, recurso.GetProperty("subjectId").GetString());
        Assert.Equal("reservas.cliente", hold.GetProperty("heldForKind").GetString());
    }

    [Fact]
    public async Task Una_referencia_con_clase_del_navegador_no_se_admite()
    {
        using var r = await new ReservaDeServicioReal().ConOferta();

        var (estado, _) = await r.Abrir(cuerpo: new
        {
            servicio = new { kind = "salud.profesional", id = "doc-demo-1" },
            ventana = new { start = ReservaDeServicioReal.Inicio, end = ReservaDeServicioReal.Inicio.AddMinutes(60) },
        });

        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Equal(0, await Ocupadas(r));
    }

    [Fact]
    public async Task Una_referencia_de_otra_clase_se_rechaza_sin_apartar_y_un_servicio_sin_oferta_no_se_reserva()
    {
        using var r = await new ReservaDeServicioReal().ConOferta();

        // NR-1: el navegador no elige la clase. Un profesional de Salud con el precio de un servicio no entra.
        var (estado, cuerpo) = await r.Abrir(cuerpo: ReservaDeServicioReal.Cuerpo(clase: "salud.profesional"));
        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Equal("reservas.entrada_invalida", cuerpo.GetProperty("code").GetString());

        // Con la clase de la definición, una clave que la oferta nunca publicó no tiene duración ni recurso.
        (estado, cuerpo) = await r.Abrir(llave: "reserva-2", cuerpo: ReservaDeServicioReal.Cuerpo(servicio: "../salud.profesional:doc"));
        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.Equal("reservas.servicio_no_reservable", cuerpo.GetProperty("code").GetString());
        Assert.Equal(0, await Ocupadas(r));
    }

    [Fact]
    public void La_clase_que_declara_la_definicion_es_la_que_publica_la_oferta()
    {
        using var definicion = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            Proyectos.Dir("Synergos.Bff.Reservas"), "flujos", "reservas.servicio.json")));

        var servicio = definicion.RootElement.GetProperty("tipos").GetProperty("servicio");
        Assert.Equal("ref", servicio.GetProperty("tipo").GetString());
        Assert.Equal(OfertaDeReservas.Clase, servicio.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Una_franja_de_otro_largo_se_rechaza_sin_apartar_ni_autorizar()
    {
        using var r = await new ReservaDeServicioReal().ConOferta();

        var (estado, cuerpo) = await r.Abrir(cuerpo: ReservaDeServicioReal.Cuerpo(minutos: 180));

        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Equal("reservas.franja_invalida", cuerpo.GetProperty("code").GetString());
        Assert.Equal(0, await Ocupadas(r, minutos: 180));
    }

    [Fact]
    public async Task Un_servicio_sin_oferta_no_se_reserva()
    {
        using var r = await new ReservaDeServicioReal().ConOferta();

        var (estado, cuerpo) = await r.Abrir(cuerpo: ReservaDeServicioReal.Cuerpo(servicio: "otro-servicio"));

        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.Equal("reservas.servicio_no_reservable", cuerpo.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Fuera_del_horario_o_en_el_pasado_lo_rechaza_la_agenda_sin_autorizar()
    {
        using var r = await new ReservaDeServicioReal().ConOferta();
        var madrugada = new DateTimeOffset(ReservaDeServicioReal.Inicio.Date.AddHours(3), TimeSpan.Zero);
        var ayer = new DateTimeOffset(DateTimeOffset.UtcNow.Date.AddDays(-1).AddHours(10), TimeSpan.Zero);

        var (fuera, cuerpoFuera) = await r.Abrir(llave: "r-fuera", cuerpo: ReservaDeServicioReal.Cuerpo(inicio: madrugada));
        var (pasado, cuerpoPasado) = await r.Abrir(llave: "r-pasado", cuerpo: ReservaDeServicioReal.Cuerpo(inicio: ayer));

        Assert.NotEqual(HttpStatusCode.Created, fuera);
        Assert.Equal("booking.outside_opening_hours", cuerpoFuera.GetProperty("code").GetString());
        Assert.NotEqual(HttpStatusCode.Created, pasado);
        Assert.Equal("booking.window_in_the_past", cuerpoPasado.GetProperty("code").GetString());
        Assert.False(r.Tiene("r-fuera", "cobroId"));
        Assert.False(r.Tiene("r-pasado", "cobroId"));
    }

    [Fact]
    public async Task El_doble_clic_no_duplica_y_una_reserva_ajena_no_existe()
    {
        using var r = await new ReservaDeServicioReal().ConOferta();
        var (_, abierta) = await r.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        var (otraVez, misma) = await r.Abrir();
        Assert.Equal(HttpStatusCode.Created, otraVez);
        Assert.Equal(id, misma.GetProperty("id").GetString());
        Assert.Equal(1, await Ocupadas(r));

        Assert.Equal(HttpStatusCode.NotFound, (await r.Consultar(id, cliente: "u-2")).Estado);
        Assert.Equal(HttpStatusCode.NotFound, (await r.Cerrar(id, cliente: "u-2")).Estado);
        Assert.Equal(HttpStatusCode.NotFound, (await r.Cancelar(id, cliente: "u-2")).Estado);

        Assert.Equal(HttpStatusCode.OK, (await r.Cerrar(id)).Estado);
        var (repetido, cuerpo) = await r.Cerrar(id);
        Assert.Equal(HttpStatusCode.OK, repetido);
        Assert.Equal("Completed", cuerpo.GetProperty("estado").GetString());
        Assert.Equal(1, await r.Avisos());
    }

    [Fact]
    public async Task Cancelar_lo_no_confirmado_suelta_la_franja_y_anula_la_autorizacion()
    {
        using var r = await new ReservaDeServicioReal().ConOferta();
        var (_, abierta) = await r.Abrir();
        var id = abierta.GetProperty("id").GetString()!;
        var cobro = r.Dato(id, "cobroId");

        var (estado, cancelada) = await r.Cancelar(id);

        Assert.True(estado == HttpStatusCode.OK, cancelada.ToString());
        Assert.Equal(SagaStatus.Compensated, r.Sagas.Find(id)!.Status);
        Assert.Equal(0, await Ocupadas(r));
        Assert.Equal("Voided", (await r.Leer("payments", $"v1/payments/{cobro}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Gratis_no_autoriza_ni_captura_y_la_franja_queda_confirmada()
    {
        using var r = await new ReservaDeServicioReal().ConOferta(ReservaDeServicioReal.Oferta(precio: 0m));
        var (_, abierta) = await r.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        var (estado, cerrada) = await r.Cerrar(id);

        Assert.True(estado == HttpStatusCode.OK, cerrada.ToString());
        Assert.False(r.Tiene(id, "cobroId"));
        Assert.Equal(["Confirmed"], await Reservas(r));
    }

    [Fact]
    public async Task Capturar_va_antes_de_confirmar_la_franja()
    {
        var orden = new List<string>();
        using var r = await new ReservaDeServicioReal(falla: p =>
        {
            if (ReservaDeServicioReal.Es(p, "POST", "^/v1/payments/[^/]+/capture$")) orden.Add("capturar");
            if (ReservaDeServicioReal.Es(p, "POST", "^/v1/holds/[^/]+/confirm$")) orden.Add("confirmar");
            return null;
        }).ConOferta();
        var (_, abierta) = await r.Abrir();

        await r.Cerrar(abierta.GetProperty("id").GetString()!);

        Assert.Equal(["capturar", "confirmar"], orden);
    }

    [Fact]
    public async Task Cotizar_dice_el_total_sin_apartar_ni_autorizar()
    {
        using var r = await new ReservaDeServicioReal().ConOferta();

        var (estado, cotizada) = await r.Cotizar();

        Assert.True(estado == HttpStatusCode.OK, cotizada.ToString());
        Assert.Equal(140_000m, cotizada.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal());
        Assert.Equal(0, await Ocupadas(r));
    }
}
