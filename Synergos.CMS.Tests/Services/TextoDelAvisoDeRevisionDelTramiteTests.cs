using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Entrar a «en revisión» no es una decisión (009): el aviso de tomar el expediente y el de la subsanación
/// que vuelve llevan su variante, y el correo dice que está en revisión, no que «tiene una decisión».
/// </summary>
/// <remarks>
/// <para>Medido en vivo en la copia piloto (T018): los cuatro avisos de un expediente que se toma, pide
/// información, se subsana y se rechaza decían todos «Tu solicitud fue revisada y tiene una decisión»,
/// también los dos de «En revisión». Antes de la v2 nada entraba a ese estado, así que ese aviso no salía.</para>
/// <para>Como en <see cref="TextoDelAvisoDeEntradasTests"/>: la variante se mira en el aviso que sale del
/// motor, y el texto, por el canal de correo real.</para>
/// </remarks>
public sealed class TextoDelAvisoDeRevisionDelTramiteTests
{
    private static readonly DateTimeOffset Clock = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly CaseActor Ana = new(
        CaseRoles.KindFuncionario, "f-ana", "Ana Funcionaria", "ana@entidad.gov.co");

    private static readonly CaseActor Maria = new(
        CaseRoles.KindCiudadano, "c-maria", "María Fernanda López", "maria.lopez@correo.co");

    private sealed class Monitor<T>(T valor) : IOptionsMonitor<T>
    {
        public T CurrentValue => valor;
        public T Get(string? name) => valor;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class AvisosRecibidos : ITransactionalNotifier
    {
        public List<NotificationEvent> Avisos { get; } = new();

        public Task DispatchAsync(NotificationEvent notification, CancellationToken cancellationToken = default)
        {
            Avisos.Add(notification);
            return Task.CompletedTask;
        }
    }

    private static NotificationEvent Aviso(string? variante) => new(
        Type: NotificationTypes.GovCaseDecided,
        SubjectId: "case-texto",
        ToEmail: "maria.lopez@correo.co",
        ToName: "María Fernanda López",
        Code: "SG-2026-009999",
        OccurredAt: Clock,
        DedupeKey: "gov.case.decided:SG-2026-009999:in-review",
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
    /// happy: tomar y subsanar avisan con la variante de revisión; pedir información y rechazar, sin ella.
    /// </summary>
    [Fact]
    public async Task Entrar_a_revision_avisa_con_su_variante_y_decidir_sin_ella()
    {
        var avisos = new AvisosRecibidos();
        var cases = new StubApplicationService(
            new StubTramiteCatalogProvider(), new StubGovFeeCalculator(), new StubPaymentProvider(), null, () => Clock);
        var workflow = new StubCaseWorkflowService(cases, null, () => Clock, avisos);

        await workflow.TakeAsync("case-1001", Ana, null);
        await workflow.DecideAsync("case-1001", "request-info", "Falta el recibo.", Ana);
        await workflow.ResubmitAsync("case-1001", Maria, null);
        await workflow.DecideAsync("case-1001", "reject", "El recibo no corresponde.", Ana);

        Assert.Equal(
            new[] { NotificationVariants.GovCaseInReview, null, NotificationVariants.GovCaseInReview, null },
            avisos.Avisos.Select(a => a.Variant).ToArray());
    }

    /// <summary>
    /// happy: el correo de revisión dice que está en revisión y no que la revisaron y hay una decisión.
    /// </summary>
    [Fact]
    public async Task El_correo_de_revision_no_dice_que_hay_una_decision()
    {
        var (modelo, asunto) = await EnviarAsync(Aviso(NotificationVariants.GovCaseInReview));

        Assert.Equal("SynergosLabs · Tu trámite está en revisión (SG-2026-009999)", asunto);
        Assert.Equal("Estamos revisando tu trámite", modelo.Heading);
        Assert.Contains("está en revisión", modelo.Intro, StringComparison.Ordinal);
        Assert.DoesNotContain("tiene una decisión", modelo.Intro, StringComparison.Ordinal);
        Assert.DoesNotContain("fue revisada", modelo.Intro, StringComparison.Ordinal);
    }

    /// <summary>
    /// empty: sin variante, el correo de la decisión de siempre.
    /// </summary>
    [Fact]
    public async Task Sin_variante_el_correo_es_el_de_la_decision()
    {
        var (modelo, asunto) = await EnviarAsync(Aviso(variante: null));

        Assert.Equal("SynergosLabs · Tu trámite tiene una decisión (SG-2026-009999)", asunto);
        Assert.Contains("tiene una decisión", modelo.Intro, StringComparison.Ordinal);
    }

    /// <summary>
    /// filter: la variante de revisión no cambia el texto de otro hecho, ni cae al genérico.
    /// </summary>
    [Fact]
    public void La_variante_de_revision_no_cambia_el_texto_de_otro_hecho()
    {
        var deOtroHecho = TransactionalEmailCopy.For(NotificationTypes.GovCaseFiled, NotificationVariants.GovCaseInReview, out var generico);

        Assert.False(generico);
        Assert.Equal(TransactionalEmailCopy.For(NotificationTypes.GovCaseFiled, out _), deOtroHecho);
    }

    /// <summary>
    /// idempotent: la variante no es otro hecho; la llave del aviso es la misma con ella y sin ella.
    /// </summary>
    [Fact]
    public void La_variante_no_cambia_la_llave_del_aviso()
    {
        Assert.Equal(Aviso(variante: null).ResolvedDedupeKey, Aviso(NotificationVariants.GovCaseInReview).ResolvedDedupeKey);
    }
}
