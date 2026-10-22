using System.Net;
using System.Text.Json;

namespace Synergos.CMS.Tests.Bff.Reservas;

/// <summary>
/// Historia 4 de 011-booking-reserva (T030): las franjas libres de un servicio, por la lectura <c>disponibilidad</c>
/// de la definición (NB-3 sobre NB-2), contra la agenda real. Sin sujeto, sin saga y sin apartar nada.
/// </summary>
public sealed class DisponibilidadDeLaReservaTests
{
    private static List<(DateTimeOffset Inicio, int Libres)> Franjas(JsonElement lectura)
        => lectura.GetProperty("datos").GetProperty("franjas").EnumerateArray()
            .Select(f => (f.GetProperty("inicio").GetDateTimeOffset(), f.GetProperty("libres").GetInt32()))
            .ToList();

    private static int Escrito(ReservaDeServicioReal r)
        => Directory.Exists(r.Almacen) ? Directory.EnumerateFiles(r.Almacen, "*", SearchOption.AllDirectories).Count() : 0;

    [Fact]
    public async Task Sin_sesion_ve_las_franjas_y_las_apartadas_cuentan_aunque_no_esten_confirmadas()
    {
        using var r = await new ReservaDeServicioReal().ConOferta();
        var inicio = ReservaDeServicioReal.Inicio;
        Assert.Equal(HttpStatusCode.Created, (await r.Abrir()).Estado);
        Assert.Equal(HttpStatusCode.Created, (await r.Abrir(llave: "reserva-2", cliente: "u-2")).Estado);
        var escrito = Escrito(r);

        var (estado, lectura) = await r.Disponibilidad(inicio.AddHours(-2), inicio.AddHours(2));

        Assert.True(estado == HttpStatusCode.OK, lectura.ToString());
        Assert.Equal("reservas.servicio", lectura.GetProperty("flujo").GetString());
        Assert.Equal(
            new[] { (inicio.AddHours(-2), 2), (inicio.AddHours(-1), 2), (inicio, 0), (inicio.AddHours(1), 2) },
            Franjas(lectura));
        Assert.Equal(escrito, Escrito(r));
    }

    /// <summary>SC-006: lo que la lectura ofrece es lo que la agenda acepta en ese instante, libre y lleno.</summary>
    [Fact]
    public async Task Lo_que_ofrece_libre_se_aparta_y_lo_que_ofrece_lleno_la_agenda_lo_rechaza()
    {
        using var r = await new ReservaDeServicioReal().ConOferta(ReservaDeServicioReal.Oferta(cupos: 1));
        var inicio = ReservaDeServicioReal.Inicio;
        Assert.Equal(HttpStatusCode.Created, (await r.Abrir()).Estado);

        var (_, lectura) = await r.Disponibilidad(inicio, inicio.AddHours(2));
        var franjas = Franjas(lectura);
        Assert.Equal(new[] { (inicio, 0), (inicio.AddHours(1), 1) }, franjas);

        var (libre, abierta) = await r.Abrir(llave: "r-libre", cliente: "u-2",
            cuerpo: ReservaDeServicioReal.Cuerpo(inicio: franjas[1].Inicio));
        var (llena, rechazo) = await r.Abrir(llave: "r-llena", cliente: "u-3",
            cuerpo: ReservaDeServicioReal.Cuerpo(inicio: franjas[0].Inicio));

        Assert.True(libre == HttpStatusCode.Created, abierta.ToString());
        Assert.Equal(HttpStatusCode.Conflict, llena);
        Assert.Equal("booking.at_capacity", rechazo.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Una_referencia_de_otra_clase_o_un_servicio_sin_oferta_no_lee_nada()
    {
        using var r = await new ReservaDeServicioReal().ConOferta();
        var inicio = ReservaDeServicioReal.Inicio;

        var (otraClase, problema) = await r.Disponibilidad(inicio, inicio.AddHours(1), clase: "salud.profesional");
        var (sinOferta, ausente) = await r.Disponibilidad(inicio, inicio.AddHours(1), servicio: "otro-servicio");

        Assert.Equal((HttpStatusCode.BadRequest, "reservas.entrada_invalida"), (otraClase, problema.GetProperty("code").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, sinOferta);
        Assert.Equal("booking.resource_not_found", ausente.GetProperty("code").GetString());
    }
}
