using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.Moderacion;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services.Lecturas;
using Synergos.CMS.Web.Services.Moderacion;
using static Synergos.CMS.Tests.Controllers.Lecturas.LecturasDePrueba;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// La moderación de la red (012-blogs-social, I5, P8 A), vista desde la app: denunciar con sesión, la cola sólo para
/// quien modera, y retirar con su motivo, que saca lo retirado para todos.
/// </summary>
/// <remarks>
/// Con la moderación <c>Local</c> real sobre el almacén en memoria, el stream de la red sin lo retirado y los dos tipos
/// moderables de Blogs; los servicios de abajo (el stream, los comentarios) son falsos.
/// </remarks>
public sealed class ModeracionContractTests : IDisposable
{
    private const string Ana = "ana.autora@synergos.co";
    private const string Otra = "otra.lectora@synergos.co";
    private const int Nodo = 4321;

    private static readonly ContentStreamItem DeAna = new(
        Id: "it-ana",
        Kind: "post",
        Author: new ContentAuthor(Ana, Ana, Ana, null),
        Body: "Algo que alguien va a denunciar",
        MediaUrl: null,
        CreatedUtc: new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc),
        Metrics: new ContentMetrics(),
        CmsNodeId: Nodo);

    private static readonly ContentStreamItem OtraPublicacion = DeAna with { Id = "it-otra", Body = "Otra cosa" };

    private readonly InMemoryJsonEntityStore _almacen = new();
    private readonly ICommentModeration _moderacionDeComentarios = Substitute.For<ICommentModeration>();
    private readonly RegistroDeRetirados _retirados;
    private readonly StreamSinRetirados _red;
    private readonly IModeracion _moderacion;

    // Los mensajes directos: la mensajería guardada y, por fuera, la que oculta lo retirado, como en el composer.
    private readonly StubMessagingService _guardada;
    private readonly MensajeriaSinRetirados _mensajeria;

    public ModeracionContractTests()
    {
        var stream = Substitute.For<IContentStream>();
        stream.GetFeedAsync(Arg.Any<FeedQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ContentStreamPage(new[] { DeAna, OtraPublicacion }, null));
        stream.GetItemAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((ContentStreamItem?)null);
        stream.GetItemAsync("it-ana", Arg.Any<CancellationToken>()).Returns(DeAna);
        stream.GetItemAsync("it-otra", Arg.Any<CancellationToken>()).Returns(OtraPublicacion);
        // UN registro, como el singleton del composer: el stream y quien retira leen lo mismo.
        _retirados = new RegistroDeRetirados(_almacen);
        _red = new StreamSinRetirados(stream, _retirados);
        _moderacion = new ModeracionLocal(_almacen, TimeProvider.System);
        _guardada = new StubMessagingService(null, _almacen, StubMessagingService.DefaultResourceType);
        _mensajeria = new MensajeriaSinRetirados(_guardada, _retirados);
    }

    public void Dispose()
    {
        _red.Dispose();
        _mensajeria.Dispose();
        _retirados.Dispose();
        (_moderacion as IDisposable)?.Dispose();
    }

    // ─── Denunciar ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Denunciar_pide_sesion_un_motivo_y_algo_que_exista()
    {
        Assert.Equal(401, Estado(await Controlador(SinSesion()).Denunciar(Publicacion("it-ana"), default)));
        Assert.Equal(400, Estado(await Controlador(Sesion()).Denunciar(Publicacion("it-ana", "  "), default)));
        Assert.Equal(400, Estado(await Controlador(Sesion()).Denunciar(Publicacion("it-ana", new string('x', 501)), default)));
        Assert.Equal(404, Estado(await Controlador(Sesion()).Denunciar(new("tienda.producto", "p-1", "Spam"), default)));
        Assert.Equal(404, Estado(await Controlador(Sesion()).Denunciar(Publicacion("no-existe"), default)));
    }

    /// <summary>
    /// Un caso por objetivo: la misma persona dos veces es una denuncia, otra persona suma. Quien denuncia no sale en la
    /// cola, y lo denunciado sale con su autor por su cara pública, nunca por el correo.
    /// </summary>
    [Fact]
    public async Task Las_denuncias_de_lo_mismo_entran_a_un_caso_y_la_cola_no_nombra_a_quien_denuncio()
    {
        var primera = Wire(await Controlador(Sesion()).Denunciar(Publicacion("it-ana", "Es spam"), default));
        await Controlador(Sesion()).Denunciar(Publicacion("it-ana", "Otra vez"), default);
        var deOtra = Wire(await Controlador(Sesion(), Otra).Denunciar(Publicacion("it-ana", "Ofende"), default));

        Assert.Equal(primera.GetProperty("caso").GetString(), deOtra.GetProperty("caso").GetString());
        var cola = Wire(await Controlador(Moderadora()).Cola(null, default));
        var caso = Assert.Single(cola.GetProperty("casos").EnumerateArray());
        TieneLasClaves(caso, "id", "tipo", "objetivoId", "resumen", "motivo", "denuncias", "denunciado");
        Assert.Equal("blogs.publicacion", caso.GetProperty("tipo").GetString());
        Assert.Equal("Es spam", caso.GetProperty("motivo").GetString());
        Assert.Equal(2, caso.GetProperty("denuncias").GetInt32());
        Assert.Equal("Ana Autora: Algo que alguien va a denunciar", caso.GetProperty("resumen").GetString());
        Assert.DoesNotContain("@synergos.co", cola.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    // ─── La cola y decidir: sólo quien modera ───────────────────────────────────

    /// <summary>La cola y la decisión son de admin, moderator y editor: un miembro cualquiera recibe 403.</summary>
    [Fact]
    public async Task La_cola_y_decidir_son_solo_de_quien_modera()
    {
        var caso = Wire(await Controlador(Sesion()).Denunciar(Publicacion("it-ana"), default)).GetProperty("caso").GetString()!;

        Assert.Equal(401, Estado(await Controlador(SinSesion()).Cola(null, default)));
        Assert.Equal(403, Estado(await Controlador(Sesion()).Cola(null, default)));
        Assert.Equal(403, Estado(await Controlador(Sesion()).Decidir(caso, new("retirar", "Spam"), default)));
        Assert.NotNull(await _red.GetItemAsync("it-ana"));
        Assert.Equal(200, Estado(await Controlador(Moderadora()).Cola(null, default)));
    }

    /// <summary>
    /// Retirar con su motivo: el caso sale de la cola con quién y por qué, y la publicación desaparece para todos, del
    /// feed y por su id. Decidir lo mismo otra vez no cambia nada; lo contrario es un conflicto.
    /// </summary>
    [Fact]
    public async Task Retirar_saca_la_publicacion_para_todos_y_queda_quien_y_por_que()
    {
        var caso = Wire(await Controlador(Sesion()).Denunciar(Publicacion("it-ana"), default)).GetProperty("caso").GetString()!;

        Assert.Equal(400, Estado(await Controlador(Moderadora()).Decidir(caso, new("borrar", "Spam"), default)));
        Assert.Equal(400, Estado(await Controlador(Moderadora()).Decidir(caso, new("retirar", " "), default)));
        var decidido = Wire(await Controlador(Moderadora()).Decidir(caso, new("retirar", "Es spam comercial"), default));

        Assert.Equal("retirar", decidido.GetProperty("decision").GetString());
        Assert.Equal("Mónica Moderadora", decidido.GetProperty("quien").GetString());
        Assert.Equal("Es spam comercial", decidido.GetProperty("porQue").GetString());
        Assert.Null(await _red.GetItemAsync("it-ana"));
        Assert.Equal(new[] { "it-otra" }, (await _red.GetFeedAsync(new FeedQuery())).Items.Select(i => i.Id));
        Assert.Empty(Wire(await Controlador(Moderadora()).Cola(null, default)).GetProperty("casos").EnumerateArray());
        Assert.Equal(200, Estado(await Controlador(Moderadora()).Decidir(caso, new("retirar", "Otra vez"), default)));
        Assert.Equal(409, Estado(await Controlador(Moderadora()).Decidir(caso, new("mantener", "Me equivoqué"), default)));
        Assert.Equal(404, Estado(await Controlador(Sesion()).Denunciar(Publicacion("it-ana"), default)));
    }

    [Fact]
    public async Task Mantener_deja_la_publicacion_y_saca_el_caso_de_la_cola()
    {
        var caso = Wire(await Controlador(Sesion()).Denunciar(Publicacion("it-ana"), default)).GetProperty("caso").GetString()!;

        await Controlador(Moderadora()).Decidir(caso, new("mantener", "No incumple nada"), default);

        Assert.NotNull(await _red.GetItemAsync("it-ana"));
        Assert.Empty(Wire(await Controlador(Moderadora()).Cola(null, default)).GetProperty("casos").EnumerateArray());
    }

    /// <summary>
    /// Con la moderación en Api y la capacidad sin contestar (o sin dirección), denunciar, la cola y decidir dicen que no
    /// se pudo (503): ni una denuncia por recibida, ni una cola vacía, ni una decisión por guardada.
    /// </summary>
    [Fact]
    public async Task Si_la_moderacion_no_contesta_se_dice_y_no_se_finge()
    {
        var caida = new ModeracionSinDestino();

        Assert.Equal(503, Estado(await Controlador(Sesion(), moderacion: caida).Denunciar(Publicacion("it-ana"), default)));
        Assert.Equal(503, Estado(await Controlador(Moderadora(), moderacion: caida).Cola(null, default)));
        Assert.Equal(503, Estado(await Controlador(Moderadora(), moderacion: caida).Decidir("caso", new("retirar", "Spam"), default)));
        Assert.NotNull(await _red.GetItemAsync("it-ana"));
    }

    /// <summary>Un comentario se nombra por su publicación y su id, y retirarlo es la moderación de comentarios de hoy.</summary>
    [Fact]
    public async Task Retirar_un_comentario_lo_retira_en_el_hilo_de_su_publicacion()
    {
        var comentario = new Comment("c1", Nodo, null, "Bruno", "Un comentario ofensivo", DateTime.UtcNow, Approved: true);
        var caso = Wire(await Controlador(Sesion(), comentarios: [comentario])
            .Denunciar(new("blogs.comentario", "it-ana:c1", "Insulta"), default)).GetProperty("caso").GetString()!;

        await Controlador(Moderadora(), comentarios: [comentario]).Decidir(caso, new("retirar", "Insulto"), default);

        await _moderacionDeComentarios.Received(1).RetirarAsync(Nodo, "c1", Arg.Any<CancellationToken>());
        Assert.Equal(404, Estado(await Controlador(Sesion(), comentarios: [comentario])
            .Denunciar(new("blogs.comentario", "it-ana:no-existe", "Insulta"), default)));
    }

    // ─── El montaje ─────────────────────────────────────────────────────────────

    private ModeracionController Controlador(
        IMemberAccessGate sesion,
        string? correo = null,
        IReadOnlyList<Comment>? comentarios = null,
        IModeracion? moderacion = null)
    {
        if (correo is not null)
        {
            sesion.CurrentMemberEmail.Returns(correo);
        }

        var lector = Substitute.For<ICommentReader>();
        lector.GetApprovedForNode(Nodo).Returns(comentarios ?? []);
        // Cada correo tiene su llave (la huella), como la identidad real: dos personas no se confunden.
        var identidad = Substitute.For<IIdentidadPublica>();
        identidad.De(Arg.Any<string>()).Returns(ci => CaraPublica.De(LlaveDe(ci.ArgAt<string>(0)), "Persona"));
        identidad.De(Ana).Returns(CaraPublica.De("a1b2c3d4e5f60718", "Ana Autora"));
        var tipos = new ITipoModerable[]
        {
            new PublicacionesModerables(_red, _retirados, identidad),
            new ComentariosModerables(_red, lector, _moderacionDeComentarios),
            new MensajesModerables(
                _mensajeria,
                _retirados,
                [new ContextoDeMensajesDeBlogs(identidad, Substitute.For<ISocialProfileProjection>())]),
        };
        return new ModeracionController(
            moderacion ?? _moderacion, tipos, sesion, identidad, NullLogger<ModeracionController>.Instance);
    }

    private static string LlaveDe(string correo)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(correo)))
            .ToLowerInvariant()[..16];

    private static IMemberAccessGate Moderadora()
    {
        var sesion = Sesion(personal: true);
        sesion.CurrentMemberDisplayName.Returns("Mónica Moderadora");
        return sesion;
    }

    private static ModeracionController.DenunciaPedida Publicacion(string id, string motivo = "Es spam")
        => new("blogs.publicacion", id, motivo);

    private static ModeracionController.DenunciaPedida Mensaje(string id, string motivo = "Me acosa")
        => new("blogs.mensaje", id, motivo);

    // ─── Un mensaje directo: la conversación es privada ──────────────────────────

    /// <summary>
    /// Un mensaje directo lo denuncia quien está en la conversación, y sólo lo que le mandó la otra persona: quien no
    /// participa recibe 404, para que la cola no le muestre a nadie una conversación ajena, y lo propio no se denuncia.
    /// La cola lo resume con quien lo mandó, por su cara pública.
    /// </summary>
    [Fact]
    public async Task Un_mensaje_directo_solo_lo_denuncia_quien_esta_en_la_conversacion()
    {
        var hilo = await _mensajeria.StartThreadAsync("dm", Ana, Yo, "Un mensaje que ofende");
        var id = $"{hilo.ThreadId}:{hilo.Messages[0].MessageId}";

        Assert.Equal(404, Estado(await Controlador(Sesion(), Otra).Denunciar(Mensaje(id), default)));
        Assert.Equal(404, Estado(await Controlador(Sesion(), Ana).Denunciar(Mensaje(id), default)));
        Assert.Equal(200, Estado(await Controlador(Sesion()).Denunciar(Mensaje(id), default)));

        var caso = Assert.Single(Wire(await Controlador(Moderadora()).Cola(null, default)).GetProperty("casos").EnumerateArray());
        Assert.Equal("blogs.mensaje", caso.GetProperty("tipo").GetString());
        Assert.Equal("Ana Autora: Un mensaje que ofende", caso.GetProperty("resumen").GetString());
    }

    /// <summary>
    /// Retirar un mensaje lo oculta para los dos, en su hilo y como el último de la bandeja, y sigue guardado. El resto
    /// de la conversación sigue igual, y lo retirado ya no se puede denunciar.
    /// </summary>
    [Fact]
    public async Task Retirar_un_mensaje_lo_oculta_para_los_dos_y_sigue_guardado()
    {
        var hilo = await _mensajeria.StartThreadAsync("dm", Yo, Ana, "Hola, ¿cómo vas?");
        hilo = await _mensajeria.ReplyAsync(hilo.ThreadId, Ana, "Un mensaje que ofende");
        var id = $"{hilo.ThreadId}:{hilo.Messages[^1].MessageId}";
        var caso = Wire(await Controlador(Sesion()).Denunciar(Mensaje(id), default)).GetProperty("caso").GetString()!;

        Assert.Equal(200, Estado(await Controlador(Moderadora()).Decidir(caso, new("retirar", "Acoso"), default)));

        Assert.Equal(["Hola, ¿cómo vas?"], (await _mensajeria.GetThreadAsync(hilo.ThreadId))!.Messages.Select(m => m.Body));
        foreach (var quien in new[] { Yo, Ana })
        {
            var resumen = Assert.Single(await _mensajeria.GetInboxAsync(quien));
            Assert.Equal("Hola, ¿cómo vas?", resumen.LastMessagePreview);
            Assert.Equal(1, resumen.MessageCount);
        }

        Assert.Equal(2, (await _guardada.GetThreadAsync(hilo.ThreadId))!.Messages.Count);
        Assert.Equal(404, Estado(await Controlador(Sesion()).Denunciar(Mensaje(id), default)));
    }

    /// <summary>La mensajería es de todos: un hilo que no es un mensaje directo de Blogs pasa tal cual.</summary>
    [Fact]
    public async Task La_mensajeria_de_los_demas_pasa_tal_cual()
    {
        var deLaTienda = await _guardada.StartThreadAsync("shop:pedido-1", Yo, Ana, "¿Ya salió mi pedido?");
        await _retirados.RetirarAsync(new ObjetivoModerable(
            MensajesModerables.TipoDelMensaje, $"{deLaTienda.ThreadId}:{deLaTienda.Messages[0].MessageId}"));

        Assert.Single((await _mensajeria.GetThreadAsync(deLaTienda.ThreadId))!.Messages);
        Assert.Equal(1, Assert.Single(await _mensajeria.GetInboxAsync(Yo)).MessageCount);
    }
}
