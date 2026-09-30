using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Las dos siembras de demo que escriben en seams GENÉRICOS y durables: los DM y guardados de
/// Blogs y la correspondencia de Gobierno (#176).
/// </summary>
/// <remarks>
/// <para><b>Lo que se vigila es la idempotencia A TRAVÉS DE UN REINICIO</b>, que es la única que
/// importa desde la ADR 0105: cada caso arma un segundo juego de servicios sobre el MISMO almacén,
/// que es lo que ve un proceso nuevo. Con un solo juego en memoria, sembrar dos veces «pasaba»
/// por la razón equivocada.</para>
///
/// <para><b>Y la de Gobierno no lo era.</b> Su comentario decía «idempotente: el ThreadId es
/// determinista, así que re-ejecutar agrega al mismo hilo — por eso solo se siembra una vez».
/// Agregar al mismo hilo ES duplicar: el hosted service que la llamaba en cada arranque le sumaba
/// tres mensajes a la carpeta de dos ciudadanos por cada reinicio. La frase describía el mundo en
/// memoria, donde cada arranque partía de cero.</para>
/// </remarks>
public sealed class SiembraDeDemoTests
{
    private const string Juliana = "juliana.rios@correo.co";
    private const string Andres = "andres.torres@correo.co";

    private static StubMessagingService Mensajeria(IJsonEntityStore almacen)
        => new(null, almacen, StubMessagingService.DefaultResourceType);

    private static StubUserCollection Colecciones(IJsonEntityStore almacen)
        => new(null, almacen, null);

    private static async Task<MessageThreadSummary> HiloDelExpediente(
        IMessagingService mensajeria, string ciudadano, string radicado)
    {
        var bandeja = await mensajeria.GetInboxAsync(ciudadano);
        return Assert.Single(bandeja, t => t.ContextRef == $"{GovCorrespondenceSeeder.Context}:{radicado}");
    }

    [Fact] // vacío: sin sembrar, la carpeta del ciudadano no tiene correspondencia
    public async Task Sin_sembrar_no_hay_correspondencia()
    {
        var mensajeria = Mensajeria(new InMemoryJsonEntityStore());

        Assert.Empty(await mensajeria.GetInboxAsync(Juliana));
        Assert.Empty(await mensajeria.GetInboxAsync(Andres));
    }

    [Fact] // feliz: los dos expedientes con su conversación, en orden
    public async Task La_correspondencia_de_Gobierno_se_siembra_con_su_conversacion()
    {
        var mensajeria = Mensajeria(new InMemoryJsonEntityStore());

        var creados = await GovCorrespondenceSeeder.SeedAsync(mensajeria);

        Assert.Equal(GovCorrespondenceSeeder.ThreadCount, creados);
        var subsanacion = await HiloDelExpediente(mensajeria, Juliana, "SG-2026-001003");
        Assert.Equal(2, subsanacion.MessageCount);
        var hilo = await mensajeria.GetThreadAsync(subsanacion.ThreadId);
        Assert.Equal(GovCorrespondenceSeeder.OfficerParticipant, hilo!.Messages[0].From);
        Assert.Equal(Juliana, hilo.Messages[1].From);
        Assert.Equal(1, (await HiloDelExpediente(mensajeria, Andres, "SG-2026-001004")).MessageCount);
    }

    [Fact] // idempotente: re-sembrar tras un reinicio NO agrega mensajes
    public async Task Sembrar_Gobierno_otra_vez_tras_un_reinicio_no_duplica_la_correspondencia()
    {
        var almacen = new InMemoryJsonEntityStore();
        await GovCorrespondenceSeeder.SeedAsync(Mensajeria(almacen));

        var otroProceso = Mensajeria(almacen);
        var creados = await GovCorrespondenceSeeder.SeedAsync(otroProceso);

        Assert.Equal(2, (await HiloDelExpediente(otroProceso, Juliana, "SG-2026-001003")).MessageCount);
        Assert.Equal(1, (await HiloDelExpediente(otroProceso, Andres, "SG-2026-001004")).MessageCount);
        Assert.Equal(0, creados);
    }

    [Fact] // idempotente: una conversación que ya existe no se pisa con la de demo
    public async Task Sembrar_Gobierno_no_toca_un_expediente_que_ya_tiene_correspondencia()
    {
        var almacen = new InMemoryJsonEntityStore();
        var mensajeria = Mensajeria(almacen);
        await mensajeria.StartThreadAsync(
            $"{GovCorrespondenceSeeder.Context}:SG-2026-001004",
            GovCorrespondenceSeeder.OfficerParticipant,
            Andres,
            "Su trámite quedó radicado.");

        var creados = await GovCorrespondenceSeeder.SeedAsync(mensajeria);

        var hilo = await HiloDelExpediente(mensajeria, Andres, "SG-2026-001004");
        Assert.Equal(1, hilo.MessageCount);
        Assert.Equal("Su trámite quedó radicado.", hilo.LastMessagePreview);
        Assert.Equal(1, creados);
    }

    [Fact] // idempotente: los DM y los guardados de Blogs tampoco crecen al re-sembrar
    public async Task Sembrar_Blogs_otra_vez_tras_un_reinicio_no_duplica_nada()
    {
        var almacen = new InMemoryJsonEntityStore();
        var primeros = await BlogsDemoSeeder.SeedAsync(Mensajeria(almacen), Colecciones(almacen));

        var mensajeria = Mensajeria(almacen);
        var colecciones = Colecciones(almacen);
        var antes = (await mensajeria.GetInboxAsync("act-elena")).Sum(t => t.MessageCount);
        var guardadosAntes = (await colecciones.GetAsync("act-elena", SocialDemoSeed.SavedCollection)).Count;

        var segundos = await BlogsDemoSeeder.SeedAsync(mensajeria, colecciones);

        Assert.True(antes > 0, "La siembra de Blogs no dejó ningún DM en la bandeja de act-elena.");
        Assert.Equal(antes, (await mensajeria.GetInboxAsync("act-elena")).Sum(t => t.MessageCount));
        Assert.Equal(guardadosAntes, (await colecciones.GetAsync("act-elena", SocialDemoSeed.SavedCollection)).Count);
        Assert.Equal(BlogsDemoSeeder.DmThreadCount, primeros);
        Assert.Equal(0, segundos);
    }
}
