# ADR 0095 — Asistente IA composable por-siteRoot + capa "aprender del negocio" (proyección)

- **Status:** Proposed (proyección de arquitectura — nada construido todavía; este ADR es el plano rector para las olas futuras)
- **Date:** 2026-06-25
- **Deciders:** Arquitecto + agente, durante la fase SynergosLabs (post-híbrido CMS↔Angular CDN vivo + identidad por-siteRoot + línea premium consagrada).
- **Modelo de referencia:** Claude (Anthropic). SDK oficial .NET (`Anthropic` NuGet). Default `claude-opus-4-8`.

## Context

SynergosLabs ya es un producto híbrido: el CMS (Umbraco 13) compone y
configura, y la CDN Angular renderiza/hidrata componentes
framework-agnósticos (`elementSyn*` → `<synergos-*>`). La identidad es
por-siteRoot (Entidad / Blogs / Ecommerce, posicionamiento "un motor,
mil productos"), la línea de diseño está consagrada como única fuente de
verdad (ADR 0094), y existe un conjunto rico de **seams** que ya modelan
el negocio:

- `ISearchQuery` + `ExamineSearchProvider` (ADR 0031) — índice full-text
  BM25 sobre el contenido publicado.
- `IFormSubmissionHandler` / `IFormSubmissionReader` (ADR 0030) — captura
  de leads con honeypot + rate-limit + notificadores.
- `IAnalyticsTracker` (ADR 0037) + `IAuditTrailWriter` (ADR 0067) — eventos
  de negocio + trazo forense append-only.
- `IShopQuery` / `IBlogQuery` — catálogo y posts.
- `IBundleRegistryClient` (ADR 0012/0089) — CDN viva sirviendo Web
  Components reales.
- `IBrandThemeProvider` / `IPageRenderContextResolver` — identidad y
  contexto de render por-siteRoot.

El arquitecto quiere **proyectar la próxima gran capacidad: IA** — un
chatbot que conversa con el prospecto/cliente y "aprende del negocio".
La pregunta de este ADR no es *si* sino *cómo encaja* en la arquitectura
sin romper ninguno de los 10 principios. La respuesta corta: la IA entra
como **una familia más de seams + un componente composable más**, y el
"aprender" es un **lazo cerrado con humano-en-el-medio** (la IA propone,
el arquitecto compone y publica) — exactamente el patrón "componer,
nunca hardcodear".

## Decision

Se adopta una **capa de IA en 6 fases**, composable y agnóstica, donde:

1. El **chatbot** es un componente híbrido más: `elementSynChatbot`
   (schema uSync) configurado en el CMS, `<synergos-chatbot>` (Angular
   WC) renderizado por la CDN, con fallback SSR Razor.
2. El **"aprender del negocio"** = RAG sobre el contenido del CMS
   (reutilizando Examine) + minería de submissions y analytics por un
   job batch que propone contenido nuevo para que el arquitecto lo
   apruebe.
3. Todo respeta el grafo de dependencias, se expone por seams con tests,
   se integra solo (sin "el operador corre X una vez"), y mantiene la
   identidad por-siteRoot.

### 0. Principios que la capa IA NO viola

| Principio | Cómo lo respeta la capa IA |
|---|---|
| Grafo unidireccional (ADR 0002) | Seams en `Interfaces`; orquestación + SDK Anthropic (lib .NET pura) en `Application`; adaptadores Umbraco (Examine, IPublishedContent, controller SSE) en `Web`. Application **no** referencia Umbraco/AspNetCore. |
| Schema vía uSync (ADR 0008) | `elementSynChatbot` + `DTSelect*` como XML uSync; cero code-first. |
| Seeders prohibidos (ADR 0013) | El índice de conocimiento se construye en runtime (hosted service + `ContentPublishedNotification`), no en boot. |
| Framework-agnóstico (ADR 0015) | `<synergos-chatbot>` vía `IBundleRegistryClient`; framework resuelto en runtime, no en schema. |
| No multi-tenant SaaS | Persona/conocimiento por-siteRoot vía hostname nativo, no `ITenantContext`. |
| Auto-integración sin scripts (ADR 0089) | Defaults sensatos + opt-in por settings; hot-reload del digest en publicación. |
| Componer, nunca hardcodear | La IA **propone** FAQ/contenido; el arquitecto lo **compone y publica**. Cero contenido baked-in. |
| Tests por seam (ADR 0075) | Cada seam llega con casos empty / happy / scope-filter / idempotent + gating de tools side-effectful. |

### 1. Estrategia de modelos (Claude)

El tier es **configurable por-siteRoot** (DataType dropdown), con default
sensato. No se baja de tier "por costo" sin decisión del arquitecto.

| Rol | Modelo | Cuándo |
|---|---|---|
| Orquestación / razonamiento duro (tool use + síntesis RAG) | `claude-opus-4-8` | Default del loop principal. Adaptive thinking. |
| Chat de alto volumen, equilibrado | `claude-sonnet-4-6` | Default recomendado del componente para producción. |
| Routing / clasificación / scoring de leads / extracción de FAQ | `claude-haiku-4-5` | Jobs baratos y latencia-sensibles. |

- **Campo de schema** `chatbotModelTier` (DTSelect: `Económico` = Haiku /
  `Equilibrado` = Sonnet / `Máximo` = Opus). Default `Equilibrado`.
- **SDK**: `Anthropic` (NuGet oficial .NET) — `AnthropicClient`,
  `Model.ClaudeOpus4_8`, `ThinkingConfigAdaptive`, `CreateStreaming`
  para SSE, structured outputs (`JsonOutputFormat`) para los jobs de
  clasificación. La API-key vive en config/secrets, **nunca** en
  contenido CMS ni commiteada.

### 2. Patrones (fundamento técnico)

**RAG por etapas (no se sobre-construye):**

- **Fase A — context-stuffing cacheado.** El contenido del sitio es
  pequeño. Se construye un *digest de conocimiento* por-siteRoot (páginas
  publicadas → resumen markdown, productos, precios, FAQ) y se inyecta en
  el **system prompt cacheado**. Prompt caching = prefijo estable →
  lecturas ~90% más baratas. Sin vector DB. Se reconstruye en
  `ContentPublished`.
- **Fase B — retrieval vía Examine.** Se reutiliza
  `ISearchQuery`/`ExamineSearchProvider` (BM25, ADR 0031) como retriever:
  `IKnowledgeRetriever` consulta Examine, devuelve top-k pasajes, los
  inyecta **después** del prefijo cacheado. **Cero infra nueva.** Esto es
  "RAG sobre el contenido del CMS" sin embeddings.
- **Fase C — semántico (diferido).** Si el contenido crece y la recall
  de Examine no alcanza, se añade un adaptador de embeddings detrás del
  mismo `IKnowledgeRetriever`. Solo si hace falta.

**Tool use — las tools del asistente mapean 1:1 a seams existentes**
(esta es la elegancia del diseño; el CMS hospeda el loop, no se usan
Managed Agents para el request path):

| Tool | Seam | Tipo |
|---|---|---|
| `search_content` | `ISearchQuery` | read-only |
| `list_products` | `IShopQuery` | read-only |
| `list_blog_posts` | `IBlogQuery` | read-only |
| `get_pricing` | contenido del `elementPricingTable` | read-only |
| `capture_lead` / `book_demo` | `IFormSubmissionHandler` | **write — con confirmación** |

Las tools write piden confirmación al usuario (el WC muestra un paso de
"¿confirmas estos datos?" y el loop manual en el CMS hace el gate antes
de ejecutar). `capture_lead` escribe vía `IFormSubmissionHandler`, así
que hereda honeypot + rate-limit + notificadores + audit ya existentes.

**Prompt caching.** El system prompt por-siteRoot (persona + voz de
marca + digest de conocimiento + descripciones de tools) es el prefijo
cacheado estable; el turno del usuario es el sufijo volátil. Se invalida
en `ContentPublished` (rebuild del digest). Verificable por
`cache_read_input_tokens`.

**Streaming.** SSE desde `ChatController` → el WC `<synergos-chatbot>`
pinta tokens en vivo. Alineado con los contratos (`docs/contracts/`:
dom-events CustomEvents, host-bridge, css-tokens).

**Structured outputs.** Para los jobs de "aprendizaje": clasificar
submissions por intención/tema, extraer candidatos de FAQ, scorear leads
— `output_config.format` + JSON schema.

**Agents / MCP — la capa batch de "aprender":**

- **Managed Agent o tarea programada** (nightly/weekly) que re-lee el
  estado del negocio (contenido + digests **anonimizados** de
  submissions + agregados de analytics) y produce: (a) candidatos de FAQ
  para publicar, (b) huecos de contenido que el chatbot no supo
  responder, (c) tendencias de leads. Salida = un reporte + borradores
  que el arquitecto revisa en el dashboard admin. Aquí sí encaja Managed
  Agents (Anthropic corre el loop; le pasamos datos por custom tools que
  mantienen el PII del lado del host).
- **MCP** (diferido): exponer el CMS como servidor MCP
  (`search_content`, `get_analytics`) para que el agente de aprendizaje
  — o futuras superficies Claude — lo consuman por protocolo estándar.
  El loop tool-use in-process cubre v1.
- **El lazo de aprendizaje** = conversaciones + preguntas sin responder
  se loguean (`IConversationStore`), el agente las mina, el arquitecto
  aprueba contenido nuevo, el digest se reconstruye → el chatbot mejora.
  Lazo cerrado, humano-en-el-medio en la publicación.

### 3. Schema (composable — el principio fuerte)

ElementType `elementSynChatbot` (IsElement, Culture, descripciones
editor-facing ≤120 chars):

| Prop | DataType | Propósito |
|---|---|---|
| `chatbotPersona` | textarea | Persona del system prompt. |
| `chatbotWelcome` | textarea | Primer mensaje. |
| `chatbotSuggestedPrompts` | textarea (1/línea) | Chips de inicio. |
| `chatbotModelTier` | DTSelectModelTier | Económico/Equilibrado/Máximo. |
| `chatbotKnowledgeScope` | DTSelectKnowledgeScope | SoloEstaPágina / TodoElSiteRoot / TodoElSitio. |
| `chatbotTools` | DTSelectChatbotTools (multi) | Qué tools habilitar. |
| `chatbotLeadFormKey` | textbox | Form destino de `capture_lead`. |
| compIntegration + compDom* | (universal) | Como todo `elementSyn*`. |

Más una **composición de settings por-siteRoot** (`cfgAiAssistant`,
patrón Global Component ADR 0023/0025, como `cfgAlert`) para fijar
persona/conocimiento/tier **una vez por vertical** y que todas las
páginas lo consuman; el `elementSynChatbot` de página puede sobre-escribir.

DataTypes nuevos (XML uSync, GUIDs frescos verificados cuádruple):
`DTSelectModelTier`, `DTSelectKnowledgeScope`, `DTSelectChatbotTools`.

### 4. Render (híbrido)

- `<synergos-chatbot>` Angular WC (CDN, componente real) — config vía
  `input config` + `createConfigInputTransform` + `resolveConfigValue`.
  Consume `/api/assistant/{siteRoot}/chat/stream` (SSE).
- Fallback SSR `Views/Partials/SynHost/Chatbot.cshtml`: si el bundle CDN
  falta o el JS está off → bloque estilado "¿Tienes preguntas?
  Escríbenos →" enlazando a `/synergos/contacto`. Degradación elegante
  (ADR 0091/0092 fallback visible).

### 5. Capas backend (grafo respetado)

- **Interfaces**: `IAiAssistant` (ChatAsync(siteRoot, history, opts) →
  stream), `IKnowledgeRetriever`, `IKnowledgeDigestBuilder`,
  `IConversationStore`, `IAiToolHandler`. Seams puros.
- **Application**: `AssistantOrchestrator` envuelve `AnthropicClient`,
  arma el prompt cacheado, corre el loop tool-use, llama retriever +
  tool handlers por seam. POCO `AiSettings` (Enabled, ApiKey,
  DefaultModelTier, MaxTurns, RetrievalTopK…). El SDK Anthropic es lib
  .NET pura → válido en Application.
- **Web**: `ChatController` (SSE, rate-limit + honeypot reuse),
  `ExamineKnowledgeRetriever` (→ ISearchQuery),
  `UmbracoKnowledgeDigestBuilder` (→ IPublishedContentCache), tool
  handlers (→ IShopQuery/IBlogQuery/IFormSubmissionHandler),
  `ContentPublishedNotification` → invalida digest,
  `SynHost/Chatbot.cshtml`, wrapper blockgrid `elementSynChatbot`,
  registro en `DTBlockGridSections`. `SeamComposer` cablea todo.
- **Tests**: por seam — empty (sin contenido), happy (respuesta
  grounded), scope-filter (respeta knowledgeScope), idempotent (digest
  rebuild estable); gating de tools write; PII no filtrado al modelo.

## Phases (el plan)

| Fase | Entregable | Verificable |
|---|---|---|
| **0 — Cimientos** | `IAiAssistant` + `AssistantOrchestrator` + SDK Anthropic + `AiSettings` + feature flag `Synergos:Ai:Enabled`. Endpoint que responde desde un system prompt estático (sin RAG). | curl al endpoint; tests del orquestador. |
| **1 — Componente composable** | `elementSynChatbot` + DataTypes + `cfgAiAssistant` + `<synergos-chatbot>` WC + fallback SSR + registro DTBlockGridSections. DevContentFiller lo compone en una página. | El chatbot aparece, configurable por página/siteRoot. |
| **2 — RAG sobre CMS (Examine)** | `IKnowledgeRetriever` + `ExamineKnowledgeRetriever` + `IKnowledgeDigestBuilder` + invalidación en ContentPublished + prompt caching. | El chatbot responde grounded en el contenido del vertical; respeta scope. |
| **3 — Tool use (seams como tools)** | Tools read-only (search/products/blog/pricing) + tools write con confirmación (capture_lead/book_demo → IFormSubmissionHandler). | El chatbot agenda demos y captura leads reales. |
| **4 — Aprender del negocio (batch)** | `IConversationStore` + job de aprendizaje (Managed Agent o tarea programada) que mina conversaciones + submissions + analytics → candidatos de FAQ + reporte de huecos + insights de leads, en el dashboard admin para que el arquitecto revise/publique. | Lazo cerrado; el arquitecto ve sugerencias accionables. |
| **5 — Semántico + multi-canal (diferido)** | Retriever de embeddings si Examine no alcanza; servidor MCP del CMS; asistente en otras superficies (WhatsApp/triage de email). | Solo bajo demanda concreta. |

## Consequences

**Positivas**
- La IA entra sin tocar ningún principio: es "una familia de seams + un
  componente más". El grafo, el schema-via-uSync, lo composable y la
  identidad por-siteRoot se mantienen intactos.
- RAG arranca con **cero infra nueva** reutilizando Examine; el costo
  dominante (el digest) se mitiga con prompt caching.
- Las tools son seams existentes: el chatbot se vuelve accionable
  (captura leads vía el pipeline de forms ya endurecido) sin código
  duplicado.
- "Aprender del negocio" es un lazo con humano-en-el-medio → respeta
  "componer, nunca hardcodear" y no introduce contenido autónomo no
  revisado.
- Encaja en "un motor, mil productos": cada vertical obtiene su asistente
  con persona + conocimiento + marca propios desde el mismo motor.

**Costos / riesgos**
- Nuevo paquete NuGet (`Anthropic`) → requiere verificar versión en
  nuget.org y ADR de dependencia (regla de paquetes).
- Costo por token: mitigado por tier configurable + prompt caching +
  cap de turnos por sesión + Haiku para routing.
- Privacidad/PII: `capture_lead` pasa por `IFormSubmissionHandler`
  (honeypot/rate-limit/audit existentes); el agente de aprendizaje recibe
  digests anonimizados/agregados, nunca PII cruda (patrón custom-tool del
  lado del host). Todo turno se audita (`IAuditTrailWriter`) y se trackea
  (`IAnalyticsTracker`). CSP allowlist para el endpoint SSE.
- Latencia de turnos largos (Opus con thinking) → SSE + UX de progreso en
  el WC.

**Diferido explícito** (no se construye hasta que haya demanda): vector
DB/embeddings, servidor MCP, multi-canal. Marcados Fase 5.

## Relación con otros ADRs

Extiende: 0009 (seams), 0011 (config tipada), 0012/0089 (CDN consumida),
0015 (framework-agnóstico), 0021 (DataTypes por intención), 0023/0025
(Global Component), 0030 (forms), 0031 (Examine), 0037/0067 (analytics +
audit), 0094 (identidad por-siteRoot). No reemplaza ninguno.
