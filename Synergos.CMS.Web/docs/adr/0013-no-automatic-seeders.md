# ADR 0013 — Cero seeders automáticos; tooling dev-only detrás de flag

- **Status:** Accepted
- **Date:** 2026-04-18
- **Deciders:** Project owner
- **Source:** promoted from `refactor-docs/adr-drafts/0013-no-automatic-seeders.md`

## Context

Los proyectos fallidos previos ejecutaban **4 seeders** en cada boot
(`ContentSeeder`, `PageBlockGridSeeder`, `FlowDemoSiteSeeder`,
`FlowEngineDemoSeeder`), guardados por idempotencia pero sistemáticamente
mutando contenido al arranque. Un fallo parcial dejaba la DB a medias
y generaba "comportamientos raros" post-deploy.

Arranque determinista y sin side-effects es requerimiento duro del
nuevo proyecto.

## Decision

1. **Ningún seeder se ejecuta en boot**.  Punto.
2. **Prohibido tocar Content** desde cualquier forma de seeder,
   initializer, composer, notification handler, hosted service o
   middleware. El contenido editorial es propiedad del editor humano.
3. Cualquier tooling de datos de desarrollo:
   - Vive detrás del flag `Synergos:DevSeed:Enabled` en
     `FeatureFlagsSettings`.
   - Default `false` en `appsettings.json`.
   - Se habilita **sólo** en `appsettings.Development.json`.
   - Se dispara por **invocación explícita** (endpoint HTTP,
     `dotnet run ... --seed`, o comando CLI custom). **Nunca**
     auto-ejecuta aunque el flag esté en `true`.
4. Cualquier seeder que afecte contenido real (no datos de demo)
   **requiere ADR específico** que justifique ventana, impacto y
   rollback.
5. `Program.cs` no contiene llamadas a seeders.
6. `IComposer` no registra handlers que muten contenido en
   `UmbracoApplicationStartedNotification`.

## Consequences

**Positive**

- Boot determinista en todos los ambientes.
- Sin "comportamientos raros al primer arranque post-deploy".
- Content 100 % en manos del editor.

**Negative**

- Onboarding de un dev nuevo requiere un paso extra: "para ver datos
  de prueba, corré X".
- No hay demo-site-with-sample-content automático; si se necesita
  para E2E, se provee por fixture en `Synergos.CMS.Tests/`, no por
  seeder de arranque.

## Casos borde

- **Unit tests**: pueden construir fixtures en memoria — no cuenta
  como "seeder".
- **Integration tests**: pueden invocar tooling de seed dev-only
  explícitamente. No cuenta como auto-seed.
- **Migración futura de contenido real**: se hace en ventana
  operativa con ADR propio; no se disfraza de seeder.

## Anti-patrones prohibidos

```csharp
// ❌ PROHIBIDO
services.AddHostedService<ContentSeederBackgroundService>();

// ❌ PROHIBIDO
builder.AddNotificationHandler<
    UmbracoApplicationStartedNotification,
    ContentInitializer>();   // cuando ContentInitializer muta contenido

// ❌ PROHIBIDO — auto-run al boot
public class Program
{
    public static async Task Main(string[] args)
    {
        var app = builder.Build();
        await SeedAsync(app.Services); // ❌
        await app.RunAsync();
    }
}

// ❌ PROHIBIDO — leer env var fuera de Development
if (Environment.GetEnvironmentVariable("SEED_ON_BOOT") == "1")
    await seeder.RunAsync();
```

## Alternatives considered

- **Seeders idempotentes con guardas** — rechazado: fallo parcial
  seguía dejando DB a medias.
- **Flag + auto-run si está activo** — rechazado: el flag se
  convierte en estado "mágico" que otros devs pisan sin saber.
