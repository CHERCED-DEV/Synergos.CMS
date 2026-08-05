using Microsoft.Extensions.Options;
using Synergos.Api.Consent.Domain;
using Synergos.Api.Consent.Endpoints;
using Synergos.Api.Consent.Storage;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Api.Consent — qué autorizó cada persona, y para qué.
//
// Sale de Identity por dos razones: Gobierno y Salud necesitan consentimiento SIN
// ser dueños de la identidad, y el derecho al olvido tiene que poder retirar
// permisos sin tocar la identidad que la bitácora todavía necesita.
//
// UN CONSENTIMIENTO POR PROPÓSITO, nunca uno global: quien acepta que le agenden
// una cita no aceptó que le manden publicidad. Y con la VERSIÓN del texto, porque
// sin ella "dio consentimiento" no dice a qué.
//
// Revocar marca, no borra. "Nunca lo dio" y "lo dio y lo retiró el martes" son
// cosas distintas para quien tiene que justificar lo que hizo mientras estaba
// vigente.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// El hilo que permite seguir una compra por los seis procesos (HU #28).
builder.AddCorrelation();

builder.Services.Configure<ConsentStorageOptions>(builder.Configuration.GetSection("Consent:Storage"));
builder.Services.AddSingleton<IConsentStore, FileSystemConsentStore>();
builder.Services.AddSingleton<IIdempotencyLedger>(sp =>
    new FileIdempotencyLedger(sp.GetRequiredService<IOptions<ConsentStorageOptions>>().Value.Root));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ConsentService>();

var app = builder.Build();

app.UseCorrelation();
app.UseSharedKeyAuth(app.Configuration["Consent:ApiKey"]);
app.MapConsentEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
