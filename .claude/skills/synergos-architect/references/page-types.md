# Page types de Synergos — cuándo cada uno

Fuente autoritaria: `refactor-docs/architecture/07-page-composition-standard.md` + ADR 0022.

## Los 4 perfiles canónicos

| Page type | Alias uSync | Cuándo recomendar | Composiciones extra | Características |
|---|---|---|---|---|
| **Standard** | `pageBase` | Editorial completa: artículos, páginas institucionales, "Acerca de", noticias, contenido con intro automática. | Alex + Orchestration + Theme + Navigation | Renderiza `_PageIntro` (heading/subheading/summary/featuredImage), luego `sections` (Layout Composer), luego `sectionsAfterBody`. |
| **Canvas** | `pageBasic` | Página donde el primer Section trae todo el cromo. Layout Composer puro sin intro automática. Hub pages, dashboards editoriales, landings de campañas que no encajan en `pageLanding`. | Alex + Orchestration + Theme + Navigation | Solo `sections`. Sin `_PageIntro`, sin `sectionsAfterBody`. |
| **Bare** | `pageBare` | Sin chrome compartido: embeds para iframes, modales independientes, capturas, vistas para pantallas externas. | Orchestration + Theme | Layout=null. Sin header/footer/breadcrumbs. Sin Alex. |
| **Landing** | `pageLanding` | Conversión: hero arriba, Layout Composer en medio, CTA cierre abajo. Páginas de campaña, lead generation, oferta. | Alex + Orchestration + Theme + Navigation | Hero + sections + CTA. Tema típicamente override (dark/contrast). |

## Cómo elegir — preguntas en orden

1. **¿La página existe para convertir un visitante en lead/cliente?** → `pageLanding`.
2. **¿La página debe rendererse fuera de la shell (iframe, modal, captura)?** → `pageBare`.
3. **¿La página es contenido editorial estándar con intro/heading/imagen destacada arriba?** → `pageBase` (Standard).
4. **¿La página es un hub donde el editor compone todo desde el primer Section sin querer la intro automática?** → `pageBasic` (Canvas).

## Cascada de orquestación (`IPageRenderContextResolver`)

Cada page hereda de `siteRoot` que hereda de `Defaults`. Los 4 page types componen `compPageOrchestration`, `compPageTheme`, `compNavigation` (Bare omite Alex y Navigation).

Resolución por campo:

```
page.Value(alias) ≠ "inherit" ∧ ≠ vacío    → usa page
   else siteRoot.Value(alias) ≠ "inherit" ∧ ≠ vacío  → usa siteRoot
   else PageRenderContext.Defaults()
```

**Implicación práctica:** si el arquitecto pregunta "¿pongo `chromeMode=full` aquí?", la respuesta default es "deja `inherit` y configura el siteRoot". Solo override por excepción.

## compPageOrchestration — campos clave

- `chromeMode`: `full | minimal | none | bare | embedded` — controla si se ven header/footer/breadcrumbs.
- `headerMode`: `default | minimal | hidden`.
- `footerMode`: `default | minimal | hidden`.
- `showTitle` / `showIntro` / `showBreadcrumbs`: booleans tri-state (`inherit | true | false`).
- `pageContainerType`: ancho del container (`narrow | default | wide | fluid`).
- `pageSpacingScale`: vertical rhythm scale.

## compPageTheme — page-level theme override

Distinto de `themeSettings` (brand-wide). Override puntual:

- `pageThemeVariant`: `inherit | light | dark | silverGold | brand`
- `pageSurface`: `inherit | default | muted | contrast | brand | transparent`
- `visualProfile`: `inherit | institutional | editorial | commercial | premium | minimal`

Caso típico: una landing page sobre marca light pero la landing va en dark con surface=contrast.

## compAlex — cintilla contextual

Top-of-page strip para anuncios, alertas, promos. Visibilidad implementada Ola 49:

- `always` — siempre visible si `alexEnabled=true`
- `scheduled` — ventana `alexScheduleStart/End` UTC
- `manual` — solo si `enabled=true`
- `authenticatedOnly` / `anonymousOnly` — diferidos a ola de miembros (no implementado aún)

Campos: `alexText`, `alexCtaLabel`, `alexCtaLink`, `alexIcon`, `alexVariant`, `alexTone`, `alexVisibilityMode`, `alexScheduleStart/End`, `alexRenderMode`, `alexDismissible`.

**Si el arquitecto pide "alerta global a todo el sitio"**: NO se hace en compAlex de una página — se hace en `cfgAlert` (Settings layer, global) consumido vía `IGlobalComponentResolver`. compAlex es per-page.

## compNavigation — visibilidad en menús

- `navigationTitle` — override del nombre del nodo en menús (no afecta el `<title>` ni el heading H1).
- `hideFromMainMenu` / `hideFromFooter` / `hideFromBreadcrumbs` / `hideFromSearch`.
- `navigationWeight` — ordenamiento numérico.
- `navigationIcon` — icono Umbraco stock (ver `reference_umbraco13_icons.txt`).

## Templates Razor (referencia)

```
Views/_Layout.cshtml          ← consume el resolver, decide chrome
Views/PageBase.cshtml         ← Standard
Views/PageBasic.cshtml        ← Canvas
Views/PageBare.cshtml         ← Bare
Views/PageLanding.cshtml      ← Landing
Views/Shared/_PageAlex.cshtml ← cintilla con scheduled visibility
Views/Shared/_PageIntro.cshtml← heading/subheading/summary/featuredImage
Views/Shared/_Breadcrumbs.cshtml ← respeta hideFromBreadcrumbs
```

`_Layout.cshtml` aplica `data-theme/surface/profile` en `<html>` y `syn-page--chrome-{mode} syn-page--header-{mode} syn-page--footer-{mode}` en `<body>` para que el design system enganche estilos sin if/else en Razor.

## Anti-patrones a vetar

- ❌ "Vamos a hacer una página `pageBare` con header custom encima" — Bare no tiene chrome. Si necesitas chrome custom, pide `pageBasic` (Canvas) y construye el chrome desde el primer Section.
- ❌ "Esta landing también es la home institucional" — separa. Landing es conversión; home institucional es Standard o Canvas.
- ❌ "Hardcodea `chromeMode=full` en cada página" — eso es lo que la cascada vino a evitar. Configura siteRoot, deja `inherit` en pages.
