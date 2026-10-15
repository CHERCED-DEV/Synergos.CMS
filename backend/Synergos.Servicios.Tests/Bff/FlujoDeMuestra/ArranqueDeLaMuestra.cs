using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Bff.FlujoDeMuestra;

/// <summary>
/// TODO el C# propio de un orquestador del caso común (núcleo N1): lo que un vertical escribiría en su
/// <c>Program.cs</c> para servir <c>flujos/muestra.cita.json</c>. Lo demás —saga, binding, fachada,
/// endpoints, adaptadores, deshacer— es del núcleo.
/// </summary>
/// <remarks>
/// <para><b>Es la medida de la meta</b> (SC-001: ≤ 60 líneas sin comentarios ni líneas en blanco; hoy un
/// flujo declarado costaba ~316, informe 81). <c>MetricaDelFlujoDeMuestraTests</c> la cuenta y no deja que
/// crezca sin que se vea. La cita no necesita pasos de dominio: un vertical que los necesite los pasa a
/// <c>AddOrquestadorDeclarado</c>.</para>
///
/// <para>Vive en las pruebas y no como un orquestador más porque un vertical nuevo es trabajo de su línea;
/// esto es el molde que ellas copian.</para>
/// </remarks>
public static class ArranqueDeLaMuestra
{
    public static readonly SagaVocabulary Vocabulario = new("muestra", "la cita");

    public static WebApplication Construir(WebApplicationBuilder builder)
    {
        builder.AddCorrelation();
        builder.AddOrquestadorDeclarado(Vocabulario, typeof(ArranqueDeLaMuestra).Assembly);

        var app = builder.Build();
        app.UseCorrelation();
        app.UseSharedKeyAuth(app.Configuration["Muestra:ApiKey"]);
        app.MapFlujosDeclarados();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        return app;
    }
}
