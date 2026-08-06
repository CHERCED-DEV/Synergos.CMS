# ADR 0014 — Document Type `PageBasic` (primer caso de producto)

- **Status:** Accepted
- **Date:** 2026-04-18
- **Deciders:** Project owner
- **Source:** promoted from `refactor-docs/adr-drafts/0014-document-type-page-basic.md`
- **Authorises:** Ola 7 of the migration plan

## Context

Las olas 1–6 dejaron Synergos.CMS como andamio completo: cuatro
proyectos con fronteras enforced, seams + defaults, schema governance
vía uSync (ADR 0008), primera Composition (`compSeo`), factorías
genéricas para mappers/readers, y contrato CDN consumido con stub
mientras el equipo CDN no publique (ADR 0012). Todo ese stack **nunca
se ha ejercitado end-to-end**: el repositorio no tiene un solo
Document Type instanciable.

Ola 7 cierra ese hueco con **un único Document Type mínimo viable**.
Su justificación de producto es cubrir **páginas estáticas**: About,
Términos de Servicio, Política de Privacidad, landings simples —
páginas cuyo cuerpo es esencialmente richtext libre con metadatos SEO,
sin layout estructurado, sin interactividad por página.

Es intencional elegir el tipo más delgado posible: el valor de Ola 7
está en **probar la pipeline completa** (creación en backoffice →
export uSync → XML en git → import → Mapper → ViewModel → Razor →
ruta HTTP → response), no en completar el modelo de contenido del
producto. Document Types más ricos (Home, Article, Blog) llegan con
sus propios ADRs cuando el caso de producto exista.

## Decision

Se crea **un (1)** Document Type con esta forma:

| Atributo | Valor |
|---|---|
| **Alias técnico** | `pageBasic` |
| **Name (backoffice)** | `Page — Basic` |
| **Icon** | `icon-document` (o el que Umbraco sugiera por defecto) |
| **IsElement** | `false` (es una página instanciable, no un Element Type) |
| **AllowAtRoot** | `true` **(temporal — ver §Consequences)** |
| **AllowedTemplates** | `PageBasic` (template asociado, provisto abajo) |
| **DefaultTemplate** | `PageBasic` |

**Compositions heredadas**:

- `compSeo` (GUID `85e75635-b950-4583-b5ca-2a51c08892e3`, declarado en
  Ola 4) — aporta `seoTitle`.

**Propiedades propias** (una única):

| Name | Alias | Tipo (Data Type built-in) | Obligatorio | Tab | Description literal |
|---|---|---|---|---|---|
| `Body` | `body` | `Richtext editor` (built-in `Umbraco.TinyMCE`) | No | `Content` | Cuerpo principal de la página. Richtext libre. Usado por páginas estáticas (About, Términos, Privacidad, landings simples). Se renderiza dentro de `<article>` en `PageBasic.cshtml`. |

**GUIDs**: generados por Umbraco al crear en backoffice; el GUID real
del ContentType se captura post-export y se añade a
`Synergos.CMS.Application/Dto/Constants/ContentTypeKeys.cs` como
`PageBasic`.

## Scope de código autorizado por este ADR

En este orden:

1. **uSync XML** del tipo, generado por `ExportOnSave="All"` cuando el
   arquitecto guarda en backoffice. El archivo se valida, no se
   escribe a mano.
2. **`Synergos.CMS.Application/Dto/Responses/PageBasicResponse.cs`** —
   record `(string? SeoTitle, string? BodyHtml)`. Vive en Application
   (neutro, sin tipos Umbraco). Strings, no `IHtmlContent` (evita
   arrastrar `Microsoft.AspNetCore.Html.*` a Application).
3. **`Synergos.CMS.Web/Controllers/PageBasicController.cs`** — extiende
   `Umbraco.Cms.Web.Common.Controllers.RenderController`. Proyecta
   `CurrentPage` a `PageBasicResponse` y retorna
   `View("PageBasic", response)`. Mapping inline, sin clase Mapper
   extra (no hay segundo consumidor que justifique extracción).
4. **`Synergos.CMS.Web/Views/PageBasic.cshtml`** — `@model PageBasicResponse`.
   Renderiza `<h1>@Model.SeoTitle</h1>` y
   `<article>@Html.Raw(Model.BodyHtml)</article>` (Raw es consciente:
   `Umbraco.TinyMCE` sanitiza entrada según config ya presente en
   appsettings — `SanitizeTinyMce = true`).
5. **`Synergos.CMS.Application/Dto/Constants/ContentTypeKeys.cs`** —
   añadir `public static readonly Guid PageBasic = Guid.Parse("<guid-real>");`
   después del export.
6. **Tests**:
   - Unit test sobre la proyección (si se extrae a método estático) —
     valida que mapea `seoTitle` y `body` del content a los campos del
     response.
   - **No** integration test con `WebApplicationFactory` — añadiría
     `Microsoft.AspNetCore.Mvc.Testing` NuGet fuera de scope.
   - Smoke manual del arquitecto: crea un nodo PageBasic en backoffice
     con title/body reales, publica, accede a la URL, verifica HTML.

## What this ADR does NOT authorise

- Ningún otro Document Type en Ola 7.
- Ningún Data Type custom.
- Block Grid o Block List.
- Multi-site / SiteRoot / multi-tenant.
- Modificación de `compSeo` (la deuda menor de naming visual de Ola 4
  queda registrada, no se resuelve aquí — reopen de `compSeo` sería
  ola aparte).
- Cambio al contrato `IElementViewModelMapper<TIn, TOut>` o al
  `ElementViewModelResolver` — PageBasic es página, no element;
  no usa el factory genérico.
- Nuevos paquetes NuGet.
- Cambios a `appsettings.json` base.

## Consequences

**Positive**

- Primera vez que la pipeline completa se ejercita: uSync export,
  backoffice instanciación de content, ruta URL, template rendering,
  ViewModel proyección.
- Cubre el caso de producto "páginas estáticas" con el footprint
  mínimo.
- Valida operacionalmente el pattern ADR-first + uSync-first.

**Negative**

- `AllowAtRoot = true` es **explícitamente temporal** para el smoke
  de Ola 7. Se revisa y **se restringe** cuando aparezca un
  `HomePage` / `SiteRoot` dedicado, que típicamente es el único tipo
  allowed-at-root en una jerarquía de content. Esta decisión vive
  anotada en el inventario bajo Ola 7 como deuda de higiene
  estructural a reconciliar en la ola que introduzca HomePage.
  Aceptable como pragmatismo mientras tanto: sin él, no se puede
  crear la primera página sin resolver antes el modelo de site-root.
- `body` es richtext libre — no soporta layouts. Está pensado así;
  si el producto necesita bloques, aparece otro Document Type o una
  revisión posterior con ADR sucesor.
- El controller contiene mapping inline en vez de un `IPageMapper`
  seam. Tradeoff elegido: sin segundo consumer no se justifica la
  interfaz (ADR 0009 filter).

## Guardrails para la ejecución

Para no repetir las deudas menores de `compSeo` (Ola 4):

- **Name del tipo** debe ser `Page — Basic` (con guión largo y
  espacios), no `pageBasic` ni `PageBasic`. El alias técnico es
  `pageBasic`; el Name visible es `Page — Basic`.
- **Name de la propiedad** debe ser `Body` (humano), alias `body`.
  Description: **texto literal de la tabla arriba** — copy-paste exacto.
- **Tab** debe llamarse `Content` con alias `content` (camelCase).
- Si al crear en backoffice Umbraco auto-deriva un alias de tab con
  capitalización rara (`conteNt` o similar), corregir **en el mismo
  flujo de creación** — no posterior.

Verificación cuádruple GUID (MIGRATION_GUARDRAILS §6.2) aplica sobre
el GUID real post-export, incluso aunque `umbracoNode` se siga
verificando por construcción mientras `sqlite3` no esté instalado.

## Alternatives considered

- **HomePage / SiteRoot** como primer tipo — más foundational pero
  fuerza decisiones sobre site root, multi-site y routing raíz antes
  de tener un smoke funcional. Rechazado como primer smoke; se hará
  cuando el producto pida un HomePage real.
- **ContentArticle** (título + body + author + date + category) — más
  campos, más validaciones, pero no prueba nada que PageBasic no
  pruebe. Rechazado por friction sin valor incremental para un smoke.
- **Zero-property Document Type** — no ejercita mapping de
  propiedades. Rechazado.
- **Usar `IElementViewModelMapper` factory para PageBasic** —
  PageBasic es página (heredaría de IPublishedContent), no element
  (IPublishedElement). Son jerarquías distintas en Umbraco; forzar
  la factoría genérica existente aquí sería acoplar prematuramente
  dos abstracciones diferentes.
