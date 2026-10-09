using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El correo de las entradas confirmadas no le dice «llévalas contigo» a quien las compró para
/// otros: <see cref="NotificationEvent.Variant"/> elige el texto, por el canal de correo real.
/// </summary>
/// <remarks>
/// <para>Se mira lo que llega a la plantilla y el asunto que sale, no el mapa suelto: un mapa con la
/// variante y un canal que no la pasa es exactamente el defecto, y los dos en verde por separado
/// no lo ven.</para>
///
/// <para>La variante elige el copy y NADA MÁS: no es otro hecho, así que no puede abrir un segundo
/// aviso de la misma compra.</para>
/// </remarks>
public sealed class TextoDelAvisoDeEntradasTests
{
    private sealed class Monitor<T>(T valor) : IOptionsMonitor<T>
    {
        public T CurrentValue => valor;
        public T Get(string? name) => valor;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private static NotificationEvent Entradas(string? variante, string tipo = NotificationTypes.EventTicketsConfirmed) => new(
        Type: tipo,
        SubjectId: "evord_texto",
        ToEmail: "regala@ejemplo.co",
        ToName: "Quien Regala",
        Code: "SYN-EVT-TEXTO",
        OccurredAt: new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.FromHours(-5)),
        Amount: 100_800m,
        Currency: "COP",
        Variant: variante);

    /// <summary>Despacha por el canal de correo real y devuelve lo que vio la plantilla y el asunto.</summary>
    private static async Task<(TransactionalEmailModel Modelo, string Asunto)> EnviarAsync(NotificationEvent aviso)
    {
        var correo = Substitute.For<IEmailService>();
        var plantilla = Substitute.For<IEmailTemplateRenderer>();
        TransactionalEmailModel? modelo = null;
        plantilla.RenderAsync(Arg.Any<string>(), Arg.Any<TransactionalEmailModel>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                modelo = ci.ArgAt<TransactionalEmailModel>(1);
                return Task.FromResult("<p>cuerpo</p>");
            });
        var marca = Substitute.For<IBrandingProvider>();
        marca.GetCurrent().Returns(new BrandIdentity("synergos", "SynergosLabs"));
        EmailMessage? enviado = null;
        await correo.SendAsync(Arg.Do<EmailMessage>(m => enviado = m), Arg.Any<CancellationToken>());

        var canal = new EmailTransactionalNotifier(
            correo, plantilla, marca,
            new Monitor<NotificationsSettings>(new NotificationsSettings { Enabled = true }),
            NullLogger<EmailTransactionalNotifier>.Instance);
        await canal.DispatchAsync(aviso);

        return (modelo!, enviado!.Subject);
    }

    /// <summary>
    /// happy: a quien compró sin ir, «las entradas que compraste» y que las comparta.
    /// </summary>
    [Fact]
    public async Task A_quien_compra_sin_ir_el_correo_le_pide_compartir_las_entradas()
    {
        var (modelo, asunto) = await EnviarAsync(Entradas(NotificationVariants.EventBuyerNotAttending));

        Assert.Equal("SynergosLabs · Las entradas que compraste están listas (SYN-EVT-TEXTO)", asunto);
        Assert.Equal("¡Tu compra está confirmada!", modelo.Heading);
        Assert.Contains("Compártelas con las personas que van a asistir", modelo.Intro, StringComparison.Ordinal);
        Assert.DoesNotContain("llévalas contigo", modelo.Intro, StringComparison.Ordinal);
    }

    /// <summary>
    /// empty: sin variante, el texto de siempre — el de quien va.
    /// </summary>
    [Fact]
    public async Task Sin_variante_el_correo_es_el_de_quien_va()
    {
        var (modelo, asunto) = await EnviarAsync(Entradas(variante: null));

        Assert.Equal("SynergosLabs · Tus entradas están listas (SYN-EVT-TEXTO)", asunto);
        Assert.Equal("¡Nos vemos en el evento!", modelo.Heading);
        Assert.Contains("llévalas contigo", modelo.Intro, StringComparison.Ordinal);
    }

    /// <summary>
    /// filter: una variante sin copy cae al de SU tipo, no al genérico; y la de Eventos no se cuela
    /// en el correo de otro hecho.
    /// </summary>
    [Fact]
    public void Una_variante_sin_copy_cae_al_texto_de_su_tipo()
    {
        var desconocida = TransactionalEmailCopy.For(NotificationTypes.EventTicketsConfirmed, "no-existe", out var caeAlGenerico);
        var deOtroHecho = TransactionalEmailCopy.For(NotificationTypes.ShopOrderPaid, NotificationVariants.EventBuyerNotAttending, out _);

        Assert.False(caeAlGenerico);
        Assert.Equal(TransactionalEmailCopy.For(NotificationTypes.EventTicketsConfirmed, out _), desconocida);
        Assert.Equal(TransactionalEmailCopy.For(NotificationTypes.ShopOrderPaid, out _), deOtroHecho);
    }

    /// <summary>
    /// idempotent: la variante no es otro hecho. La misma compra con y sin ella es UNA clave, así
    /// que el despachador no manda dos correos si una re-emisión llegara con otro texto.
    /// </summary>
    [Fact]
    public void La_variante_no_cambia_la_llave_del_hecho()
    {
        Assert.Equal(
            Entradas(variante: null).ResolvedDedupeKey,
            Entradas(NotificationVariants.EventBuyerNotAttending).ResolvedDedupeKey);
    }
}
