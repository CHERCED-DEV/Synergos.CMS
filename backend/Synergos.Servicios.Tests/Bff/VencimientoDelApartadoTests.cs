using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Synergos.Bff.Pasos;
using Synergos.Bff.Pasos.Http;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// El vencimiento que escribe <c>booking.apartar</c> es el que dice <c>Api.Booking</c>, leído del cable de verdad
/// (núcleo, NB-5 de 011 y NV-12 de 006).
/// </summary>
/// <remarks>
/// Los pasos se prueban con puertos falsos (<c>PasosYDeshacerTests</c>, <c>HostDeFlujosTests</c>); lo que un falso
/// no puede decir es si el adaptador lee el campo que la capacidad manda. Un nombre mal escrito en el DTO no
/// falla: deja la fecha en su valor por defecto, y la pantalla diría «vence en 1 de enero del año 1».
/// </remarks>
public sealed class VencimientoDelApartadoTests
{
    [Fact]
    public async Task El_adaptador_comun_devuelve_el_vencimiento_que_da_la_agenda()
    {
        using var capacidades = new ArnesDeCapacidades().Levanta<Synergos.Api.Booking.Contracts.HoldResponse>("booking");
        using var http = capacidades.CreateClient("booking");
        using var registro = new HttpRequestMessage(HttpMethod.Post, "v1/resources")
        {
            Content = JsonContent.Create(new { subjectKind = "prueba.sala", subjectId = "s-1", capacity = 1, timeZoneId = "UTC" }),
        };
        registro.Headers.Add("Idempotency-Key", "sala");
        using var creado = await http.SendAsync(registro);
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var recurso = (await creado.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        var inicio = new DateTimeOffset(DateTimeOffset.UtcNow.Date.AddDays(3).AddHours(15), TimeSpan.Zero);

        var apartado = await ((IBookingPort)new PuertosHttp(capacidades)).ApartarAsync(
            recurso, TimeWindow.Starting(inicio, TimeSpan.FromMinutes(30)), Ref.Create("prueba.persona", "p-1"),
            IdempotencyKey.Of("saga-1|hold"), CancellationToken.None);

        var enLaAgenda = (await http.GetFromJsonAsync<JsonElement>($"v1/holds/{apartado.Value.Id}")).GetProperty("expiresAt").GetDateTimeOffset();
        Assert.Equal(enLaAgenda, apartado.Value.Vence);
        Assert.True(apartado.Value.Vence > DateTimeOffset.UtcNow, "El vencimiento quedó en su valor por defecto: el DTO no lee el campo.");
    }
}
