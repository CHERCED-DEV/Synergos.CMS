# ADR 0010 — Branding vía provider, sin conditional branching

- **Status:** Accepted
- **Date:** 2026-04-18
- **Deciders:** Project owner
- **Source:** promoted from `refactor-docs/adr-drafts/0010-branding-via-provider.md`

## Context

Synergos CMS debe soportar polimorfismo de marca sin contaminar el
núcleo. ADR 0009 estableció el mecanismo genérico (seams en Interfaces).
Este ADR formaliza el caso específico de branding porque es el vector
donde más rápido se erosionan las arquitecturas.

## Decision

La marca activa se resuelve exclusivamente vía el seam:

```csharp
namespace Synergos.CMS.Interfaces;

public interface IBrandingProvider
{
    BrandIdentity GetCurrent();
}

public sealed record BrandIdentity(
    string Key,
    string DisplayName
    // …campos adicionales sólo cuando un caso real lo justifique…
);
```

- La implementación por defecto **`DefaultBrandingProvider`** lee de
  `IOptions<BrandingSettings>` bound desde `appsettings`.
- Implementaciones alternativas (resolución por host, tenant, cookie,
  URL segment) viven en la **capa custom futura**, no en el core.
- **Prohibido** que los consumidores del core contengan `if/switch`
  sobre `brand.Key`. La lógica condicional de marca vive en providers
  alternativos o en composers de la capa custom.

## Consequences

**Positive**

- Añadir una marca no modifica el core.
- Los consumidores del core compilan una sola vez; su comportamiento
  se parametriza en runtime.
- Testing brand-dependent usa fakes del provider.

**Negative**

- El primer uso parece over-engineered frente a "leer `brand` de
  appsettings". La alternativa es peor a mediano plazo.

## Anti-patrones prohibidos

```csharp
// ❌ PROHIBIDO
if (branding.Key == "brandA") { /* … */ }

// ❌ PROHIBIDO
switch (branding.Key)
{
    case "brandA": return LogoA;
    case "brandB": return LogoB;
}

// ❌ PROHIBIDO
var logo = branding.Key switch { "brandA" => LogoA, _ => DefaultLogo };
```

Patrón correcto: el provider devuelve el dato final (o un sub-objeto
tipado), los consumidores lo leen.

```csharp
// ✅ OK
public string GetLogo(IBrandingProvider provider) =>
    provider.GetCurrent().LogoUri;
```

## Alternatives considered

- **Herencia por marca** (`BrandACMS : SynergosCMS`) — rechazado
  por rigidez y contradecir "composición sobre herencia".
- **Múltiples `appsettings.{brand}.json` sin seam** — rechazado:
  no permite resolución dinámica ni lógica más rica que lookup
  plano.
- **Branding como property bag genérico** — rechazado: pierde
  tipado, introduce strings mágicos.
