using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Notifications;
using Synergos.CMS.Web.Services.Reservas;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Publicar una ficha reservable publica su oferta en <c>Bff.Reservas</c>, y despublicarla la retira
/// (011-booking-reserva, T027–T028).
/// </summary>
/// <remarks>
/// Con un orquestador falso que graba lo que le llega: lo que se mira es lo que SALE —la ruta, la llave y el
/// cuerpo— y lo que el editor lee en el backoffice. Que esa forma sea la del contrato del orquestador lo cruza
/// <c>ConsumidorCmsDeBffReservasTests</c>.
/// </remarks>
public sealed class PublicadorDeOfertasDeReservasTests
{
    internal static readonly Guid Clave = Guid.Parse("51e3c7f8-96f8-4458-a650-5adec6126174");

    internal sealed class OrquestadorFalso : HttpMessageHandler
    {
        public List<(string Ruta, string? Llave, string Cuerpo)> Llamadas { get; } = [];

        public Func<HttpRequestMessage, HttpResponseMessage> Responde { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"servicio":"x","duracionMinutos":60,"cupos":1,"precio":1,"moneda":"COP","retirada":false}""",
                Encoding.UTF8, "application/json"),
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Llamadas.Add((req.RequestUri!.AbsolutePath,
                req.Headers.TryGetValues("Idempotency-Key", out var v) ? v.Single() : null,
                req.Content is null ? "" : await req.Content.ReadAsStringAsync(ct)));
            return Responde(req);
        }
    }

    internal sealed class Fabrica(HttpMessageHandler cable) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(PublicadorDeOfertasDeReservas.ClientName, name);
            return new HttpClient(cable, disposeHandler: false) { BaseAddress = new Uri("http://bff-reservas.local/") };
        }
    }

    internal static HttpResponseMessage Rechazo(HttpStatusCode estado, string codigo) => new(estado)
    {
        Content = new StringContent($$"""{"code":"{{codigo}}","detail":"no","transient":false}""", Encoding.UTF8, "application/problem+json"),
    };

    /// <summary>Una ficha como la guarda el backoffice: con la composición y lo que se le pase.</summary>
    internal static IContent Ficha(bool conComposicion = true, params (string Alias, object? Valor)[] cambios)
    {
        var valores = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["reservable"] = 1,
            ["duracionMinutos"] = 60,
            ["diasDeAtencion"] = """["miércoles","lunes","viernes"]""",
            ["abre"] = "08:00",
            ["cierra"] = "18:00",
            ["cupos"] = 2,
            ["plazoCancelacionHoras"] = 24,
            ["productPriceBase"] = "140000",
        };
        foreach (var (alias, valor) in cambios) valores[alias] = valor;

        var ficha = Substitute.For<IContent>();
        ficha.Key.Returns(Clave);
        ficha.Name.Returns("Masaje terapéutico");
        ficha.HasProperty(Arg.Any<string>()).Returns(c => conComposicion && valores.ContainsKey(c.Arg<string>()));
        ficha.GetValue(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool>())
            .Returns(c => valores.GetValueOrDefault(c.ArgAt<string>(0)));
        return ficha;
    }

    internal static PublicadorDeOfertasDeReservas Publicador(OrquestadorFalso orquestador, bool hayDestino = true)
        => new(new Fabrica(orquestador), NullLogger<PublicadorDeOfertasDeReservas>.Instance, hayDestino);

    private static OfertaDeReservaAlPublicar AlPublicar(PublicadorDeOfertasDeReservas publicador)
        => new(publicador,
            Options.Create(new CartSettings { Currency = "COP" }),
            Options.Create(new ListadosSettings { ZonaHoraria = "America/Bogota" }),
            NullLogger<OfertaDeReservaAlPublicar>.Instance);

    private static async Task<EventMessages> PublicarAsync(PublicadorDeOfertasDeReservas publicador, params IContent[] fichas)
    {
        var mensajes = new EventMessages();
        await AlPublicar(publicador).HandleAsync(new ContentPublishedNotification(fichas, mensajes), default);
        return mensajes;
    }

    [Fact]
    public async Task Publicar_una_ficha_completa_manda_su_oferta_con_la_moneda_y_la_zona_del_sitio()
    {
        var orquestador = new OrquestadorFalso();

        var mensajes = await PublicarAsync(Publicador(orquestador), Ficha());

        var (ruta, llave, cuerpo) = Assert.Single(orquestador.Llamadas);
        Assert.Equal("/v1/ofertas", ruta);
        Assert.False(string.IsNullOrWhiteSpace(llave));
        var o = JsonDocument.Parse(cuerpo).RootElement;
        Assert.Equal(Clave.ToString("D"), o.GetProperty("servicio").GetString());
        Assert.Equal("Masaje terapéutico", o.GetProperty("nombre").GetString());
        Assert.Equal(60, o.GetProperty("duracionMinutos").GetInt32());
        Assert.Equal(["Monday", "Wednesday", "Friday"], o.GetProperty("horario").EnumerateArray().Select(b => b.GetProperty("dia").GetString()));
        Assert.All(o.GetProperty("horario").EnumerateArray(), b =>
        {
            Assert.Equal("08:00", b.GetProperty("desde").GetString());
            Assert.Equal("18:00", b.GetProperty("hasta").GetString());
        });
        Assert.Equal(2, o.GetProperty("cupos").GetInt32());
        Assert.Equal(24, o.GetProperty("plazoCancelacionHoras").GetInt32());
        Assert.Equal(140_000m, o.GetProperty("precio").GetDecimal());
        Assert.Equal("COP", o.GetProperty("moneda").GetString());
        Assert.Equal("America/Bogota", o.GetProperty("zona").GetString());
        Assert.Equal(0, mensajes.Count);
    }

    [Fact]
    public async Task Republicar_es_otra_publicacion_con_su_propia_llave()
    {
        var orquestador = new OrquestadorFalso();
        var publicador = Publicador(orquestador);

        await PublicarAsync(publicador, Ficha());
        await PublicarAsync(publicador, Ficha());

        Assert.Equal(2, orquestador.Llamadas.Select(l => l.Llave).Distinct().Count());
    }

    [Fact]
    public async Task Una_ficha_incompleta_no_se_publica_lo_dice_en_el_backoffice_y_retira_la_que_hubiera()
    {
        var orquestador = new OrquestadorFalso();

        var mensajes = await PublicarAsync(Publicador(orquestador), Ficha(true, ("duracionMinutos", null), ("abre", "8am")));

        Assert.DoesNotContain(orquestador.Llamadas, l => l.Ruta == "/v1/ofertas");
        Assert.Equal($"/v1/ofertas/{Clave:D}/retirar", Assert.Single(orquestador.Llamadas).Ruta);
        var mensaje = Assert.Single(mensajes.GetAll());
        Assert.Equal(EventMessageType.Warning, mensaje.MessageType);
        Assert.Contains("duración", mensaje.Message, StringComparison.Ordinal);
        Assert.Contains("horario", mensaje.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Una_ficha_sin_marcar_reservable_retira_y_si_nunca_se_ofrecio_no_hay_nada_que_decir()
    {
        var orquestador = new OrquestadorFalso { Responde = _ => Rechazo(HttpStatusCode.NotFound, PublicadorDeOfertasDeReservas.NoExiste) };

        var mensajes = await PublicarAsync(Publicador(orquestador), Ficha(true, ("reservable", 0)));

        Assert.Equal($"/v1/ofertas/{Clave:D}/retirar", Assert.Single(orquestador.Llamadas).Ruta);
        Assert.Equal(0, mensajes.Count);
        Assert.Equal(EstadoDeLaOferta.Hecha, (await Publicador(orquestador).RetirarAsync(Clave.ToString("D"))).Estado);
    }

    [Fact]
    public async Task Una_ficha_de_la_tienda_sin_la_composicion_no_se_publica_aqui()
    {
        var orquestador = new OrquestadorFalso();

        await PublicarAsync(Publicador(orquestador), Ficha(conComposicion: false));

        Assert.Empty(orquestador.Llamadas);
    }

    [Fact]
    public async Task Otra_zona_que_la_agenda_no_cambia_llega_al_editor_como_error_sin_publicar_a_medias()
    {
        var orquestador = new OrquestadorFalso { Responde = _ => Rechazo(HttpStatusCode.Conflict, OfertaDeReservaAlPublicar.NoActualizable) };

        var mensajes = await PublicarAsync(Publicador(orquestador), Ficha(true, ("cierra", "20:00")));

        Assert.Single(orquestador.Llamadas);
        var mensaje = Assert.Single(mensajes.GetAll());
        Assert.Equal(EventMessageType.Error, mensaje.MessageType);
        Assert.Contains("zona horaria", mensaje.Message, StringComparison.Ordinal);
        Assert.Contains("no se publicó nada", mensaje.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// T028 con NB-7: el horario nuevo se publica (la agenda revisa el recurso) y, si deja reservas confirmadas
    /// fuera, el editor lo lee: la agenda no las cancela y nadie más se va a enterar.
    /// </summary>
    [Fact]
    public async Task Un_horario_nuevo_que_deja_reservas_fuera_se_publica_y_el_editor_lo_lee()
    {
        var orquestador = new OrquestadorFalso
        {
            Responde = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"servicio":"x","duracionMinutos":60,"cupos":1,"precio":1,"moneda":"COP","retirada":false,"reservasFuera":2}""",
                    Encoding.UTF8, "application/json"),
            },
        };

        var mensajes = await PublicarAsync(Publicador(orquestador), Ficha(true, ("cierra", "12:00")));

        var mensaje = Assert.Single(mensajes.GetAll());
        Assert.Equal(EventMessageType.Warning, mensaje.MessageType);
        Assert.Contains("2 reserva(s)", mensaje.Message, StringComparison.Ordinal);
        Assert.Contains("No se cancelaron", mensaje.Message, StringComparison.Ordinal);
        Assert.Equal(0, (await PublicarAsync(Publicador(new OrquestadorFalso()), Ficha())).Count);
    }

    [Fact]
    public async Task Un_orquestador_caido_no_tumba_el_publicar_y_el_editor_lo_lee()
    {
        var orquestador = new OrquestadorFalso { Responde = _ => throw new HttpRequestException("Connection refused") };

        var mensajes = await PublicarAsync(Publicador(orquestador), Ficha());

        Assert.Equal(EventMessageType.Warning, Assert.Single(mensajes.GetAll()).MessageType);
    }

    [Fact]
    public async Task Sin_destino_no_sale_a_la_red()
    {
        var orquestador = new OrquestadorFalso();
        var publicador = Publicador(orquestador, hayDestino: false);

        var mensajes = await PublicarAsync(publicador, Ficha());

        Assert.Equal(EstadoDeLaOferta.SinDestino, (await publicador.RetirarAsync("x")).Estado);
        Assert.Empty(orquestador.Llamadas);
        Assert.Equal(0, mensajes.Count);
    }

    [Fact]
    public async Task Despublicar_mandar_a_la_papelera_o_borrar_retira_la_oferta()
    {
        var orquestador = new OrquestadorFalso();
        var contenidos = Substitute.For<IContentService>();
        contenidos.GetPagedDescendants(Arg.Any<int>(), Arg.Any<long>(), Arg.Any<int>(), out Arg.Any<long>(), Arg.Any<IQuery<IContent>?>(), Arg.Any<Ordering?>()).Returns(Array.Empty<IContent>());
        var alRetirar = new OfertaDeReservaAlRetirar(Publicador(orquestador), contenidos, NullLogger<OfertaDeReservaAlRetirar>.Instance);

        await alRetirar.HandleAsync(new ContentUnpublishedNotification(Ficha(), new EventMessages()), default);
        await alRetirar.HandleAsync(new ContentDeletedNotification(Ficha(), new EventMessages()), default);
        await alRetirar.HandleAsync(new ContentUnpublishedNotification(Ficha(conComposicion: false), new EventMessages()), default);

        Assert.Equal(2, orquestador.Llamadas.Count);
        Assert.All(orquestador.Llamadas, l => Assert.Equal($"/v1/ofertas/{Clave:D}/retirar", l.Ruta));
        Assert.All(orquestador.Llamadas, l => Assert.Equal("", l.Cuerpo));
    }

    [Fact]
    public async Task Despublicar_el_padre_retira_las_fichas_reservables_que_contiene()
    {
        var orquestador = new OrquestadorFalso();
        var padre = Ficha(conComposicion: false);
        var hija = Ficha();
        padre.Id.Returns(7);
        var contenidos = Substitute.For<IContentService>();
        contenidos.GetPagedDescendants(Arg.Is(7), Arg.Is(0L), Arg.Any<int>(), out Arg.Any<long>(), Arg.Any<IQuery<IContent>?>(), Arg.Any<Ordering?>())
            .Returns(c => { c[3] = 1L; return new[] { hija }; });
        var alRetirar = new OfertaDeReservaAlRetirar(Publicador(orquestador), contenidos, NullLogger<OfertaDeReservaAlRetirar>.Instance);

        await alRetirar.HandleAsync(new ContentUnpublishedNotification(padre, new EventMessages()), default);

        Assert.Equal($"/v1/ofertas/{Clave:D}/retirar", Assert.Single(orquestador.Llamadas).Ruta);
    }
}
