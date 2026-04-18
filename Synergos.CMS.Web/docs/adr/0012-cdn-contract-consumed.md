# ADR 0012 — El contrato CDN se consume, no se posee

- **Status:** Accepted · Blocked externally (adapter real)
- **Date:** 2026-04-18
- **Deciders:** Project owner + equipo CDN (para el contrato concreto)
- **Source:** promoted from `refactor-docs/adr-drafts/0012-cdn-contract-consumed.md`

## Context

Los proyectos previos cableaban el esquema de URL del bundle en un
`StaticUrlBuilder`, aunque los valores individuales venían de
appsettings. Si el equipo CDN/UI cambiaba el layout de publicación,
el CMS no se enteraba hasta ver 404s en logs.

El dueño del formato del registry es el equipo CDN. El CMS es
consumidor.

## Decision

- El CMS **no hardcodea** la forma del path, del manifest ni del
  registry.
- El CMS define el seam `IBundleRegistryClient` en
  `Synergos.CMS.Interfaces`. Contrato mínimo:

```csharp
public interface IBundleRegistryClient
{
    Task<BundleDescriptor?> TryResolveAsync(
        string elementKey,
        CancellationToken ct = default);
}

public sealed record BundleDescriptor(
    Uri MainEntryUri,
    IReadOnlyList<Uri> Dependencies,
    string Version);
```

(La forma exacta del contrato queda pendiente hasta que CDN publique
su especificación; este ADR fija el patrón, no los campos.)

- La implementación real `HttpBundleRegistryClient` vive en
  `Synergos.CMS.Application/Proxies/Impl/` y usa `HttpClient` tipado.
- Mientras CDN no publique contrato, existe **sólo** el stub
  `StubBundleRegistryClient` que retorna `null` en dev y documenta
  el bloqueo.
- **Ningún consumidor** del core conoce la forma interna del
  registry; sólo el contrato.

## Consequences

**Positive**

- CDN puede evolucionar formato sin romper CMS; se adapta el adapter.
- Tests de consumidores no dependen de un servidor CDN vivo.
- Bloqueo CDN no detiene el resto del trabajo.

**Negative**

- Feature de resolución de bundles queda **incompleta** hasta que
  CDN publique y se implemente el adapter real.
- Drift posible entre contrato tipado y realidad CDN si la
  comunicación es pobre — mitigado con contract tests.

## Pre-requisitos para activar el adapter real

- [ ] CDN publica especificación del registry (formato, versionado,
      endpoints, semántica de errores).
- [ ] Se redacta ADR sucesor / complementario confirmando el
      contrato congelado.
- [ ] Se implementa `HttpBundleRegistryClient` con contract tests.

## Guardrails operativos

- Prohibido construir URLs de CDN concatenando `string.Format` en
  el core. Si hace falta, es señal de que falta metadato en
  `BundleDescriptor`.
- Prohibido publicar `StubBundleRegistryClient` a staging / prod.
  El registro del stub sólo ocurre cuando
  `Synergos:CDN:Mode = "stub"`.

## Alternatives considered

- **CMS publica el formato y CDN consume** — rechazado: el CMS no
  es autoridad del storage layout.
- **Acoplar vía URL pattern en appsettings** — rechazado: es el
  patrón que falló antes.
