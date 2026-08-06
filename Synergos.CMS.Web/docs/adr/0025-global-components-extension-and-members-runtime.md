# ADR 0025 — Global components extension + Members runtime (Ola 52)

- **Status:** Accepted
- **Date:** 2026-04-25
- **Deciders:** Arquitecto + agente, durante Ola 52
- **Extiende:** ADR 0023 (Componentization Layered Architecture)

## Context

Tras Olas 50 + 51 quedaron tres frentes pendientes con bajo riesgo y
alto valor:

1. **Pattern Global Component limitado a Alert.** ADR 0023 lo
   describió como transversal pero hasta ahora solo aterrizó
   `cfgAlert`. Sin extensión a otras piezas, el patrón no estaba
   probado.
2. **Sweep de descripciones / Names incompleto.** Olas 49 + 50.3 +
   51.2 limpiaron los superficies más visibles (compPageOrchestration,
   compPageTheme, compNavigation, compSeo, cfgAlert, page DocTypes).
   Quedaban con jerga: `compContent*` (10), `compDom*` (9), Element
   Types (~75 con "Element Type SSR-native" en sus Descriptions).
3. **Members runtime diferido desde Ola 35.** El schema
   (compMemberGating con `requiresAuth` + `allowedRolesCsv`) existía
   sin runtime que lo aplicara. Páginas marcadas como "miembros only"
   se mostraban a todos.

## Decision

### Parte A — Extender Global Component pattern (transversalidad real)

Sumar `cfgBanner`, `cfgFooterNote`, `cfgModal` al pattern de Ola 50.2.
Cada uno demuestra una variación distinta:

- **cfgBanner**: pieza visual con imagen + CTA + placement
  (top/bottom). Demuestra render condicional doble (encima del header
  o debajo del main).
- **cfgFooterNote**: pieza compacta dentro del footer. Demuestra
  inserción en sub-zona de chrome existente.
- **cfgModal**: pieza con interactividad cliente. El resolver decide
  si aplica al request; el JS del design-system decide cuándo abrir
  (data-trigger + data-frequency atributos).

`IGlobalComponentResolver` gana 3 métodos hermanos
(`GetActiveBanner`, `GetActiveFooterNote`, `GetActiveModal`) +
3 records (`CfgBanner`, `CfgFooterNote`, `CfgModal`).
`DefaultGlobalComponentResolver` se refactoriza con dos helpers
internos (`TryResolve` para suppress + BlockList,
`FindActiveScheduled` para active+ventana) que se reusan entre los
4 cfg*. Cada `GetActive*` solo proyecta los campos de su tipo.

`compPageOrchestration` gana 3 toggles suppress correspondientes
(`suppressGlobalBanner`, `suppressGlobalFooterNote`,
`suppressGlobalModal`). KISS: 4 toggles separados, no multiselect.
Editor entiende inmediatamente.

### Parte B — Sweep total de Names + descripciones

Aplicar el style guide ratificado en ADR 0024 a las superficies
restantes:

- **`compContent*` (10)**: Names traducidos a español editorial
  ("Author Name" → "Nombre del autor", "CTA Label" → "Texto del
  CTA", "Heading Title" → "Título", etc.).
- **`compDom*` (9)**: Names traducidos con sufijo de contexto
  ("Flex Direction" → "Dirección (flex)", "Hide on Mobile" →
  "Ocultar en móvil", "ARIA Label" → "Etiqueta accesible (ARIA)").
- **Element Types (~75)**: sweep masivo de jerga en Descriptions
  visible en el block picker:
  - "Element Type SSR-native composite atómico tipo X" → "Bloque
    tipo X"
  - "Element Type SSR-native para X" → "Bloque para X"
  - "Element Type SSR-native composite" → "Bloque"
  - "Element Type" → "Bloque"
  - "SSR-native" → "servidor"
  - "Variations=Culture." residual → ""

Esto cierra el barrido de UX editorial empezado en Ola 51.2. El
editor ahora ve consistentemente Names + Descriptions claros, en
español, sin jerga arquitectónica.

### Parte C — Members runtime middleware

Aterriza el primer módulo diferido sobre el schema existente.

**Seam (`Synergos.CMS.Interfaces/IMemberAccessGate.cs`):**
- `bool IsAuthenticated { get; }`
- `string? CurrentMemberDisplayName { get; }`
- `IReadOnlyCollection<string> CurrentMemberRoles { get; }`
- `bool HasAnyRole(string? allowedRolesCsv)` — comparación
  case-insensitive contra el CSV del schema.

**Implementación
(`Synergos.CMS.Web/Services/DefaultMemberAccessGate.cs`):**
Lee de `HttpContext.User` (claims poblados por el middleware Umbraco
antes de llegar a templates / handlers). Sync — no async. Roles se
extraen de claims con tipo `ClaimTypes.Role`.

**Handler
(`Synergos.CMS.Web/Notifications/MemberGatingHandler.cs`):**
Hook `RoutingRequestNotification` (cuando Umbraco resuelve a un
`PublishedContent`). Lee `requiresAuth` + `allowedRolesCsv` del
nodo. Si `_memberAccessGate.HasAnyRole(allowedRolesCsv)` retorna
false → `RequestBuilder.SetRedirect("/login?returnUrl=...")`. Logea
`Information` con el destino.

**Decisión de diseño**: el gate responde "puede el miembro actual
ver" — el handler decide la acción (redirect). Separación clara.
La acción específica (redirect vs 401 vs página gated) queda
intercambiable sin tocar el seam.

## Consequences

**Positivas:**

- **Pattern Global Component validado** con 3 piezas adicionales
  cubriendo 3 estilos distintos (chrome insertion, sub-zona,
  cliente-interactivo). Próximas piezas (`cfgCookie`,
  `cfgTracking`, etc.) siguen el mismo template sin tocar el
  resolver core.
- **Editor UX consistente**: Names + Descriptions en español
  editorial en TODAS las composiciones y Element Types principales.
  El backoffice deja de mostrar "DT.Select.X" y "Element Type
  SSR-native" como jerga visible.
- **Members runtime funciona** con el schema ya existente. Una
  página marcada `requiresAuth=true` + `allowedRolesCsv="premium"`
  redirige a anónimos / no-premium a `/login`. Cero cambios en
  schema; solo runtime.
- **Seam IMemberAccessGate desacopla del IMemberManager**. Tests
  futuros pueden mockearlo. Cambiar de Members a auth externo
  (OIDC, JWT) es un swap del default impl.

**Negativas:**

- **`/login` está hardcoded** en `MemberGatingHandler`. Cuando
  llegue un sitio que use otro path (`/iniciar-sesion`,
  `/account/signin`), habrá que extraer a
  `IOptions<MembersSettings>`. Documentado como TODO; no urgente.
- **No hay forbidden 403 distinto del unauthenticated 401**. Hoy
  ambos casos van al mismo `/login`. Si se necesita una página
  "Acceso denegado" separada para members logged-in pero sin
  role, se agrega en una micro-ola (extender el handler para
  diferenciar).
- **`elementMemberLogin/Logout/Profile`** aún no consumen
  `IMemberAccessGate`. Sus renderers deberían mostrar "Salir"
  vs "Entrar" según `IsAuthenticated`. Documentado como TODO;
  fuera del scope de Ola 52.

**Neutras:**

- 4 nuevos `cfg*` ContentTypes en `Settings/Components/`. Editor
  ve un BlockList "Componentes globales" con 4 opciones (Alerta,
  Banner, Aviso footer, Modal).
- 4 toggles suppress en `compPageOrchestration` (no consolidados
  en uno por KISS de UX editorial).
- `MemberGatingHandler` es **Singleton** indirectamente vía
  notification handler — Umbraco lo registra una vez. La impl
  depende de `IHttpContextAccessor` (también Singleton, accede
  per-request al HttpContext via `AsyncLocal`).

## Alternatives considered

- **Multiselect "qué componentes globales suprimir"**. Descartado.
  4 toggles dan UX más clara que un dropdown multi.
- **Hacer `IGlobalComponentResolver` genérico con `Get<T>()`**
  + interface marker `IGlobalComponent`. Descartado por
  sobre-diseño. 4 métodos hermanos son explícitos y descubribles
  en autocomplete; cada uno puede tener su lógica propia (ej.
  modal con frequency/trigger).
- **Crear un middleware ASP.NET propio para member gating**.
  Descartado. `RoutingRequestNotification` se ejecuta dentro del
  pipeline Umbraco después de resolver el `PublishedContent` —
  más limpio que duplicar el routing en un middleware externo.
- **Hacer `IMemberAccessGate.GetMember()` que devuelva un objeto
  rico**. Descartado. Lo único que el handler necesita es
  `HasAnyRole`. Devolver el objeto entero acopla consumidores a
  shape de Member que puede cambiar.

## Implementation summary (Ola 52, 4 commits)

| Commit | Hash | Foco |
|---|---|---|
| `feat(ola-52.A)` | `666f923` | cfgBanner + cfgFooterNote + cfgModal + 3 DataTypes + DTBlockListGlobalComponents extendido + IGlobalComponentResolver con 3 métodos + 3 records + 3 partials Razor + _Layout consume + 3 suppress flags |
| `refactor(ola-52.B)` | `9f33b20` | Names compContent (10) + compDom (9) traducidos a español; Element Types (~75) descriptions sweep masivo (Element Type/SSR-native → Bloque) |
| `feat(ola-52.C)` | `3aa4364` | IMemberAccessGate + DefaultMemberAccessGate + MemberGatingHandler + wire en SeamComposer |

## References

- ADR 0023 — Componentization Layered Architecture (extiende)
- ADR 0024 — Pages mínimas + descripciones editor-facing
  (style guide aplicado en Parte B)
- `feedback_componentization_layered` — memoria refinada
- `feedback_editor_description_style` — memoria refinada
- `refactor-docs/migration/05-legacy-refinement-inventory.md` —
  desbloqueo del módulo Members runtime (item #13 del backlog)
