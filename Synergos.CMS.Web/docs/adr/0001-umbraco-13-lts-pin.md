# ADR 0001 — Umbraco 13 LTS pin

- **Status:** Accepted
- **Date:** 2026-04-17
- **Deciders:** Project owner

## Context

Umbraco publishes both LTS (Long-Term Support) and non-LTS major versions.
At the time of this scaffolding:

- **Umbraco 13.13.1** is the latest LTS release. Support runs through
  October 2026 with security extended until 2027.
- **Umbraco 14.x and 15.x** are non-LTS, with shorter support windows
  and an ongoing backoffice rewrite (Bellissima).
- The solution is greenfield but intended to run in production, and the
  team is one person.

A single-maintainer project cannot afford to chase backoffice rewrites or
ride quarterly major bumps. Stability over feature velocity is the
explicit preference.

There is a known moderate-severity advisory on 13.x (NU1902, GHSA-54mj-vcvj-q3v5)
with no patched version in the 13.x line at the time of this ADR. The
Umbraco team has acknowledged it; a fix is expected within the 13 LTS
window.

## Decision

Pin Umbraco to **`13.13.1`** via Central Package Management. Do not
propose a major-version migration to 14 or 15 within this project's
lifetime on 13 LTS.

When the next LTS (14 LTS, 15 LTS, or later — Umbraco has not announced
which) becomes available and stable for ≥3 months, a successor ADR may
supersede this one.

## Actualización — 2026-09-17: 13.13.1 → 13.15.1

La decisión de arriba NO cambia: el pin sigue siendo el branch 13 LTS, y 14+ sigue
necesitando un ADR sucesor. Lo que cambia es el parche dentro del branch, y queda
anotado acá porque la versión exacta está escrita en la sección Decision.

**Qué lo forzó.** `GHSA-wr57-hqmp-fgvh`, severidad **alta**, publicada el 2026-09-17:
la Delivery API filtra contenido protegido por Public Access al expandir un Content
Picker o un Multi-Node Tree Picker. Afecta `>= 12.0.0, < 13.15.1`. La auditoría de
NuGet la convirtió en `NU1903` y **dejó el build de las cuatro soluciones en rojo**,
que es como se descubrió — no por mirar avisos de seguridad, sino porque el build paró.

**Exposición real: ninguna.** La Delivery API no está habilitada en este sitio; el
propio Umbraco lo registra al arrancar («The Delivery API is not enabled»). Se sube
igual, por dos razones: un build rojo bloquea el CI, y silenciar una advisory **alta**
con una razón al lado es exactamente el error que documenta el párrafo siguiente.

**De paso cerró otra, y ahí está la lección.** `GHSA-2qjj-h6wp-c7h7` (open redirect en
Surface Controllers) estaba parcheada en **13.14.0 desde mayo de 2026**, y nadie la
había recogido porque el `NoWarn` del repo afirmaba que NU1902 entero era «sin patch en
branch 13.x». Esa frase era falsa para una de las dos advisories y la blindó cuatro
meses. La que sí sigue sin patch en ninguna versión es `GHSA-54mj-vcvj-q3v5`, la que
esta ADR ya nombraba — y ahora el `NoWarn` la nombra a ella y no a la familia.

**Por qué 13.15.1 y no 13.16.2**, que es la última 13.x: es el **mínimo** que cierra las
dos parcheables. Misma seguridad con menos superficie de cambio.

**Verificado**: las cuatro soluciones en 0 avisos y 0 errores, las tres suites en verde
(3274 tests) y el sitio servido — portada en 200 con su import map y su custom element
montado.

## Consequences

**Positive**
- Stable runtime and backoffice behaviour for the life of this codebase.
- Freedom to adopt packages that explicitly target 13 LTS (uSync 13, etc.)
- Avoids the Bellissima transition risk.

**Negative**
- No access to new Umbraco 14+ features (new backoffice API, improved
  management APIs, new content delivery APIs).
- The NU1902 advisory will appear in every build until Umbraco ships a
  patch. Do not treat it as a build failure — add it to the known-issues
  list in `docs/operations/run-build-test.md` instead.
- When migration does happen, it will be a significant effort because
  the gap will have grown.

## Alternatives considered

- **Umbraco 15 latest**: rejected for LTS absence and Bellissima churn risk.
- **Upgrade-as-you-go**: rejected because a solo maintainer has no capacity
  for quarterly major bumps.
