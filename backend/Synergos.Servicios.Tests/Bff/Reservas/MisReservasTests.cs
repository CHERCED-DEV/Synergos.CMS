using System.Net;
using System.Text.Json;

namespace Synergos.CMS.Tests.Bff.Reservas;

/// <summary>
/// Historia 10 de 011-booking-reserva (T035): «mis reservas» es <c>listar</c> del flujo (NB-6), con el dueño que pone la
/// puerta. Las de otro no salen nunca.
/// </summary>
public sealed class MisReservasTests
{
    private static List<string> Ids(JsonElement pagina)
        => pagina.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()!).ToList();

    [Fact]
    public async Task Cada_cliente_ve_las_suyas_de_la_mas_nueva_a_la_mas_vieja_y_por_estado()
    {
        using var r = await new ReservaDeServicioReal().ConOferta();
        var (_, primera) = await r.Abrir();
        Assert.Equal(HttpStatusCode.OK, (await r.Cerrar(primera.GetProperty("id").GetString()!)).Estado);
        var (_, segunda) = await r.Abrir(llave: "reserva-2", cuerpo: ReservaDeServicioReal.Cuerpo(inicio: ReservaDeServicioReal.Inicio.AddHours(1)));
        var (_, ajena) = await r.Abrir(llave: "reserva-3", cliente: "u-2");

        var (estado, mias) = await r.Listar();
        var (_, completadas) = await r.Listar("?estado=Completed");
        var (_, deOtro) = await r.Listar(cliente: "u-2");

        Assert.True(estado == HttpStatusCode.OK, mias.ToString());
        Assert.Equal([segunda.GetProperty("id").GetString()!, primera.GetProperty("id").GetString()!], Ids(mias));
        Assert.Equal(2, mias.GetProperty("total").GetInt32());
        Assert.Equal([primera.GetProperty("id").GetString()!], Ids(completadas));
        Assert.Equal([ajena.GetProperty("id").GetString()!], Ids(deOtro));
        var item = mias.GetProperty("items")[1];
        Assert.Equal(primera.GetProperty("numero").GetString(), item.GetProperty("numero").GetString());
        Assert.Equal(["servicio", "total", "vence", "ventana"], item.GetProperty("datos").EnumerateObject().Select(p => p.Name).Order().ToList());
    }

    [Fact]
    public async Task Sin_reservas_la_lista_esta_vacia_y_un_estado_inventado_se_rechaza()
    {
        using var r = await new ReservaDeServicioReal().ConOferta();

        var (_, vacia) = await r.Listar(cliente: "u-9");
        var (mal, problema) = await r.Listar("?estado=Pagada");

        Assert.Equal(0, vacia.GetProperty("total").GetInt32());
        Assert.Equal((HttpStatusCode.BadRequest, "reservas.entrada_invalida"), (mal, problema.GetProperty("code").GetString()));
    }
}
