using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Synergos.Api.Documents.Domain;
using Synergos.Api.Documents.Endpoints;
using Synergos.Api.Documents.Storage;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Api.Documents — ficheros con dueño, huella, enlaces firmados y retiro.
//
// AGNÓSTICA: el dueño es un Ref opaco. No sabe si lo que guarda es un resultado
// clínico, un certificado o una factura — y por eso puede servirle a los nueve
// dominios sin heredar el régimen de ninguno.
//
// Lo que SÍ decide sola: qué tipos entran (lista blanca, nunca negra), cuánto
// pesan, y que un enlace de descarga vence.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DocumentStorageOptions>(builder.Configuration.GetSection("Documents:Storage"));
builder.Services.AddSingleton<IDocumentStore, FileSystemDocumentStore>();
builder.Services.AddSingleton<IBlobStore, FileSystemBlobStore>();
builder.Services.AddSingleton<IIdempotencyLedger>(sp =>
    new FileIdempotencyLedger(sp.GetRequiredService<IOptions<DocumentStorageOptions>>().Value.Root));
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<DocumentStorageOptions>>().Value;

    // Sin llave configurada se genera una POR ARRANQUE: los enlaces emitidos dejan
    // de valer al reiniciar. Es molesto y es lo correcto — una llave fija en el
    // repositorio convertiría cada enlace firmado en un enlace público permanente.
    var key = options.SigningKey;
    if (string.IsNullOrWhiteSpace(key))
    {
        key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        sp.GetRequiredService<ILoggerFactory>().CreateLogger("Documents").LogWarning(
            "Documents:Storage:SigningKey NO está configurada: se generó una efímera y los " +
            "enlaces firmados dejarán de valer al reiniciar el proceso.");
    }

    return new DocumentService(
        sp.GetRequiredService<IDocumentStore>(),
        sp.GetRequiredService<IBlobStore>(),
        sp.GetRequiredService<IIdempotencyLedger>(),
        sp.GetRequiredService<TimeProvider>(),
        key);
});

var app = builder.Build();

app.UseSharedKeyAuth(app.Configuration["Documents:ApiKey"]);
app.MapDocumentEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
