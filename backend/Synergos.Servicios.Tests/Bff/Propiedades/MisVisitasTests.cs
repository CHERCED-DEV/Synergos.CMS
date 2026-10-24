using System.Net;
using System.Text.Json;

namespace Synergos.CMS.Tests.Bff.Propiedades;

/// <summary>
/// Historia 9 de 008-propiedades-visita (FR-026): «Mis visitas» lee, por la puerta, las sagas de su dueño (núcleo NB-6,
/// <c>listar</c>), y las cancelaciones de las confirmadas, para decir cuáles ya no van.
/// </summary>
public sealed class MisVisitasTests
{
    [Fact]
    public async Task El_dueno_ve_sus_visitas_confirmadas_y_otra_persona_ninguna()
    {
        using var r = new VisitaReal();
        var visita = await r.Confirmada();

        var (estado, mias) = await r.Listar("propiedades.visita", estado: "Completed");
        var (_, ajenas) = await r.Listar("propiedades.visita", quien: "otra", estado: "Completed");

        Assert.True(estado == HttpStatusCode.OK, mias.ToString());
        Assert.Equal([visita], Ids(mias));
        Assert.Empty(Ids(ajenas));
    }

    [Fact]
    public async Task La_cancelacion_de_una_confirmada_se_lista_con_la_visita_que_cancela()
    {
        using var r = new VisitaReal();
        var visita = await r.Confirmada();
        await r.CancelarConfirmada(visita);

        var (_, canceladas) = await r.Listar("propiedades.visita-cancelacion", estado: "Completed");

        Assert.Equal([visita], canceladas.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("datos").GetProperty("visita").GetString()).ToList());
    }

    private static List<string?> Ids(JsonElement pagina)
        => pagina.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).ToList();
}
