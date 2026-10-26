using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Educacion.Domain;
using Synergos.Bff.Educacion.Endpoints;
using Synergos.Bff.Pasos;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Bff.Educacion — la matrícula a un curso con pago (010-educacion-matricula), un orquestador del CASO
// COMÚN del núcleo (N1): su flujo es flujos/educacion.matricula.json y lo sirve el host genérico. No escribe
// saga, enlace, fachada ni adaptadores. La clase del curso la declara la definición (NR-1).
//
// La oferta es la común del núcleo (P12, 001-P11 A con N1): la pieza es el curso (educacion:Oferta:Clase) y sus
// variantes, los planes; la cotiza forma.una-linea-de-oferta. Lo que la oferta común todavía no publica —cómo se
// llaman el curso y sus planes, para el aviso— queda aparte y atribuido a NM-3: el paso educacion.nombres y su ruta.
//
// ANTES DE DESPLEGAR: los destinos de pricing, payments y notifications
// (Educacion__Capabilities__<cap>__BaseUrl/ApiKey), Educacion:ApiKey, Educacion:Alerts y la plantilla
// educacion.matricula.confirmada publicada en Api.Notifications (tools/provisionar.plantillas.json).
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);
builder.AddCorrelation();

var nombres = NombresDeCursos.De(builder.Configuration);
builder.AddOrquestadorDeclarado(new SagaVocabulary("educacion", "la matrícula"), typeof(Program).Assembly, new PasoNombresDelCurso(nombres));
builder.AddNombresDeCursos(nombres);

var app = builder.Build();
app.UseCorrelation();
app.UseSharedKeyAuth(app.Configuration["Educacion:ApiKey"]);
app.MapFlujosDeclarados();
app.MapOfertaDeclarada();
app.MapNombresDeCursos();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
