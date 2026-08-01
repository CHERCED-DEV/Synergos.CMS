# App Bootstrap Recipes — 5 verticales SYNERGOS

> **Premisa polimórfica**: SYNERGOS es plataforma multi-vertical. Un código,
> un CMS, 122 bundles UI = N productos distintos. Las recetas no cambian
> código — cambian schema instances + brand assets + settings + pages.
> Si una receta requiere cambios de código, **se rompe la promesa polimórfica**
> y hay que rediseñar antes de añadir.

## Estructura de toda receta

Todo aplicativo SYNERGOS sigue el mismo flujo de bootstrap:

1. **Brand assets** (`brand` + `themeSettings` + media library): logos, colors, fonts, social og image.
2. **Settings globales** (`siteRoot` + `siteConfigSettings` + `cfgAlert`/`cfgBanner`/etc.): SEO defaults, analytics, alertas globales, footer, modal global.
3. **Page Types** (cuáles de los 4 canónicos: Standard / Canvas / Bare / Landing — ver `page-types.md`).
4. **Estructura de navegación** (siteRoot tree: Home, secciones, footer entries).
5. **Páginas iniciales** (cada vertical tiene un set mínimo).
6. **Blocks reutilizables** (cuáles `elementSyn*` se van a usar más en este vertical).
7. **Empalme UI** (los `<synergos-*>` correspondientes — ver `cms-to-ui-mapping.md`).

Cada receta abajo te lo da concreto.

---

## Receta 1 — Profesional independiente (médico / abogado / coach / consultor)

**Use case**: una persona o pequeño equipo que ofrece servicios profesionales y necesita presencia digital + posibilidad de agendar.

### Brand assets

- Logo (square + horizontal versions)
- Color primario (típicamente conservative — azul, verde, terracota)
- Tipografía: Manrope (default) o serif para verticals tradicionales (legal / medical institutional)
- Social og image: foto del profesional o team

### Settings globales (`siteRoot` + `siteConfigSettings`)

- `defaultSeoTitle`: "{Nombre} — {Especialidad}"
- `defaultSeoDescription`: 1 frase con propuesta de valor + ubicación
- `defaultOgImage`: foto profesional
- `analyticsId`: GA o Plausible
- `cfgAlert`: opcional para horarios especiales o mensajes de crisis
- `cfgBanner`: opcional para promos o anuncios

### Páginas iniciales (5 páginas mínimas)

| Página | Page Type | Estructura |
|---|---|---|
| Home | `landingPage` | Hero + Servicios overview + Bio breve + Testimonials + CTA agendar |
| Sobre mí / Sobre el equipo | `pageBasic` (Standard) | Bio extendida + credenciales + filosofía + galería |
| Servicios | `pageBasic` (Standard) | Lista de servicios con cards expandibles + FAQ |
| Blog | `blogIndex` (auto-resolve postPage children) | ArticleList con filtros |
| Contacto / Agendar | `pageBasic` (Standard) o `landingPage` | Form contacto + map-pin + horarios + redes |

### Blocks que se usan más

Schema CMS → UI bundle (consultar `cms-to-ui-mapping.md` para detalles):

- `elementSynHero` → `<synergos-hero>` — sección Hero del Home
- `elementSynFeatureItem` × N → `<synergos-feature-item>` — cards de servicios
- `elementSynTestimonialItem` × N → `<synergos-testimonial-item>` — opiniones
- `elementSynFaqItem` × N → `<synergos-faq-item>` — preguntas frecuentes
- `elementSynNewsletterForm` → `<synergos-newsletter-form>` — captación email
- `elementSynMapPin` → `<synergos-map-pin>` — ubicación consultorio
- `elementSynShareBar` → `<synergos-share-bar>` — share posts blog
- `elementSynBreadcrumb` → `<synergos-breadcrumb>` — nav posts/services

### Empalme con apps externas (futuro / agendamiento)

Si se necesita reservas online, dos approaches sin tocar código:

1. **`elementSynIframeEmbed`** apuntando a Calendly/Cal.com/SimplyBook embed.
2. **`elementSynExternalWidget`** + bundle externo (cuando el agendamiento sea custom Synergos).

### Comments para escenarios específicos

- **Médico/clínica**: agregar `cfgAlert` con disclaimer de "no es consejo médico" en blog posts.
- **Abogado**: agregar `cfgFooterNote` con la barra de abogados / disclaimer legal.
- **Coach**: hero con CTA fuerte "Agenda tu sesión gratuita" + testimonials prominentes.

---

## Receta 2 — E-commerce

**Use case**: catálogo de productos + carrito + checkout. Hoy el shop está en `apps/domains/shop/` con bundles propios (cart-item, cart-summary, product-card, product-detail, etc.) — vertical más maduro de SYNERGOS.

### Brand assets

- Logo (priority horizontal — usado en cart bar)
- Color primario + secundario (CTA "Agregar al carrito" debe destacar)
- Tipografía: Manrope (default) o más bold para fashion/lifestyle
- Imagen og: hero product foto

### Settings globales

- `siteConfigSettings`:
  - `analyticsId` (con e-commerce events configurados)
  - `cfgBanner` para "envío gratis +$X" o promos site-wide
  - `cfgFooterNote` con políticas (devoluciones, envíos, garantía)
- `cartSettings` (Synergos:Cart en appsettings):
  - `SecretKey` (HMAC para cookie del carrito anónimo — rotar antes de prod, ADR 0028)
  - Cart abandonment threshold (por default 24h)

### Páginas iniciales

| Página | Page Type | Estructura |
|---|---|---|
| Home | `landingPage` | Hero + ProductGrid hot-sellers + CategoryShowcase + Testimonials + Newsletter CTA |
| Catálogo (root) | `productCategoryPage` | ProductGrid + filters por categoría + sort |
| Categoría X | `productCategoryPage` (children) | ProductGrid scoped + breadcrumb |
| Producto detail | `productPage` | ProductDetail + VariantPicker + RelatedProducts + Reviews |
| Carrito | `cartPage` | CartSummary + items + ButtonGroup checkout |
| Checkout | `checkoutPage` | FormStepper (shipping/payment/review) |
| Cuenta | `accountPage` | Member info + Orders history |
| Blog | `blogIndex` | ArticleList |

### Blocks que se usan más (shop-specific)

- `elementShopProductCard` → `<synergos-product-card>` — grid card
- `elementShopProductDetail` → `<synergos-product-detail>` — page
- `elementShopProductGrid` → `<synergos-product-grid>` — listing
- `elementShopVariantPicker` → `<synergos-variant-picker>` — color/size selectors
- `elementShopCartItem` → `<synergos-cart-item>` — fila de cart
- `elementShopCartSummary` → `<synergos-cart-summary>` — totales + checkout
- `elementShopPriceDisplay` → `<synergos-price-display>` — formato precio + descuentos
- `elementShopQuantitySelector` → `<synergos-quantity-selector>` — +/- controls

### Blocks editoriales reusados

- `elementSynHero`, `elementSynBannerSlider`, `elementSynFeatureGrid` — homepage
- `elementSynTestimonialCarousel` — social proof
- `elementSynFaqItem` — políticas/FAQ
- `elementSynShareBar` — products
- `elementSynNewsletterForm` — captación

### Settings críticos para checkout

- `Synergos:Cart:SecretKey` — **rotar antes de prod** (ADR 0028)
- `Synergos:Email:Provider` configurado (transactional emails: order confirm, shipping, etc.)
- `Synergos:Payment:Provider` (futuro — Stripe/MercadoPago/PayU adapter)

---

## Receta 3 — Marca corporativa institucional

**Use case**: sitio de empresa establecida — institucional, casos de éxito, careers, blog corporate. Foco en credibilidad, no en conversión inmediata.

### Brand assets

- Logo brand book oficial (variantes light/dark)
- Color palette completa (primary + secondary + neutrals + accent)
- Tipografía: corporate set (puede ser custom — wire vía `themeSettings` brand assets)
- Social og image: brand pattern + claim
- `themeSettings` con `silverGold` u override custom si la marca lo requiere

### Settings globales

- `siteConfigSettings`:
  - `defaultSeoTitle` con tagline corporativo
  - `analyticsId` + GTM (corporate suelen tener marketing stack más complejo)
  - `cfgBanner` para press releases, CSR, eventos
  - `cfgFooterNote` con corporate info (RUT, dirección legal)
- Multi-language: `Languages` configurar es-CO + en-US (mínimo). siteRoot variations Culture.

### Páginas iniciales

| Página | Page Type | Estructura |
|---|---|---|
| Home | `landingPage` | Hero + ValueProps + ClientLogoCloud + CaseHighlights + Newsroom + CTA contact |
| Quiénes somos | `pageBasic` | Bio + timeline + leadership team + values |
| Servicios / Soluciones | `pageBasic` con sub-pages | FeatureGrid → detail pages cada uno |
| Casos de éxito | `caseStudyIndex` (custom o `pageBasic` + ProductGrid pattern) | Cards filtrables por industria |
| Caso individual | `caseStudyPage` | Hero + Cliente + Reto + Solución + Resultados + Quote |
| Blog / Insights | `blogIndex` | ArticleList con categories |
| Newsroom / Press | `newsroomPage` | Lista de press releases ordenada por fecha |
| Careers | `careersPage` | Values + open positions + apply CTA |
| Contacto | `pageBasic` | Form + offices map (varios MapPins) + leadership emails |

### Blocks que se usan más

- `elementSynHero`, `elementSynFeatureGrid`, `elementSynLogoCloud`, `elementSynLogoItem` — homepage
- `elementSynTestimonialSection` + `elementSynTestimonialItem` (case quotes)
- `elementSynBanner` o `elementSynBannerSlider` — promos / press
- `elementSynKpiCard` × N — números corporativos (clientes, países, años, equipo)
- `elementSynTimeline` o `elementSynTimelineHorizontal` — historia compañía
- `elementSynShareBar` — blog/insights/case studies
- `elementSynBreadcrumb` — nav profunda
- `elementSynNewsletterForm` — insights subscribers

### Multi-domain corporate

Si tiene presencia regional (synergos.com.co, synergos.com.mx, synergos.com.br):
- 1 `platformRoot` global
- N `siteRoot` (uno por país) con su `IBrandingProvider` resolviendo por hostname (ADR 0010)
- Cada `siteRoot` tiene su propio brand/settings/pages
- Compartir blog posts via `pageBasic` cross-site references o publishing pipeline

---

## Receta 4 — Membership / Portal SaaS

**Use case**: producto SaaS o asociación con miembros. Tiene una sección pública (landing + pricing + signup) y una privada (dashboard + member tools + content gated).

### Brand assets

- Logo (priority light — usado en dashboard)
- Color primario (typically tech-flavored — purple, indigo, teal)
- Fonts: Manrope o variante geometric sans
- App icon (futuro PWA)

### Settings globales

- `siteConfigSettings`:
  - `cfgBanner` opt-in para anuncios producto (release notes, mantenimiento)
  - `cfgModal` opcional para onboarding (welcome / tour)
- `MembersSettings`:
  - `RequireEmailConfirmation: true` (ADR 0044)
  - `EnableTwoFactor: true` (recomendado para portales con data sensible — ADR 0074)
- `AdminSettings`:
  - Roles: `admin`, `member`, `staff` mínimo

### Páginas iniciales

#### Pública

| Página | Page Type | Estructura |
|---|---|---|
| Home / Landing | `landingPage` | Hero + Features + Pricing + Testimonials + FAQ + CTA signup |
| Pricing | `pageBasic` | PricingCard × N (compositions/pricing-card) + FeatureComparison + FAQ |
| Features detail | `pageBasic` con sub-pages | FeatureGrid + use cases + screenshots |
| Login | `pageBasic` con `compMemberGating.requiresAuth=false` | LoginForm (Razor SSR, no bundle) |
| Signup | `pageBasic` | RegisterForm (Razor SSR) + email confirmation flow |
| Forgot password | `pageBasic` | Form (Razor SSR) |
| Blog público | `blogIndex` | Marketing content |

#### Privada (post-login, `compMemberGating.requiresAuth=true`)

| Página | Page Type | Estructura |
|---|---|---|
| Dashboard | `dashboardPage` | KpiCard × N + DataGrid recent activity + NotificationCenter |
| Mi perfil | `accountPage` | Member info + change password + 2FA setup + DataProtection settings |
| Members / equipo | `pageBasic` con role gate `admin` | MemberRosterReader (table + lock/unlock + GDPR erase) |
| Mis recursos / Mis proyectos | custom page type per use case | DataTable o DataGrid + filters + actions |
| Settings org | `pageBasic` con role gate `admin` | Forms con cfg* o custom settings |

### Blocks que se usan más

- `elementSynHero`, `elementSynFeatureGrid`, `elementSynPricingCard` — landing
- `elementSynKpiCard` × 4-6 — dashboard
- `elementSynDataGrid` o `elementSynDataTable` — listings
- `elementSynNotificationCenter`, `elementSynNotificationToast` — UX feedback
- `elementSynFormStepper` — onboarding multi-step / signup wizard
- `elementSynDrawer` — secondary panels en dashboard
- `elementSynModalTrigger` — confirmaciones destructivas

### Auth + 2FA wiring

SYNERGOS ya tiene infra completa (ver ADRs 0034 + 0074 + 0084):
- `IMemberAuthService` (login/register/2FA)
- `IMemberTwoFactorService` (TOTP RFC 6238 + recovery codes + QR rendering)
- Recovery codes 8-char alphanumeric, encrypted-at-rest via `IDataProtectionProvider`

No requiere código nuevo — solo configurar `MembersSettings` y crear las páginas Login / Signup / TwoFactorSetup / TwoFactorChallenge usando los Razor partials existentes.

---

## Receta 5 — Healthcare extendido (futuro / diferido)

**Use case**: clínica, consultorio multi-especialidad, plataforma médica. Construye sobre **Receta 1 (Profesional)** + **Receta 4 (Membership)**, agregando dominio salud.

> ⚠️ **Estado**: receta documentada para visión polimórfica futura. Algunos blocks
> mencionados no existen todavía como `elementSyn*` — requieren scaffolding nuevo
> antes de ser usables. Lista al final.

### Sobre Receta 1+4 base

Healthcare = Profesional + Membership, donde el "member" es el paciente con historia médica accesible.

### Diferencias críticas

- **HIPAA / GDPR sensitive data**: la historia médica + recetas requieren `IDataProtectionProvider` activo (`KeyringPath` configurado) + `IGdprRtbfCoordinator` para borrado completo (ADRs 0084 + 0085).
- **Audit trail obligatorio**: cada acceso a historia médica → `IAuditTrailWriter.AppendAsync({ action: "patient.record.viewed", actor, target })` (ADR 0067).
- **Roles más finos**: además de `admin/member/staff`, agregar `doctor`, `nurse`, `secretary`.
- **Email confirmación obligatoria** + 2FA obligatorio para staff (ADR 0074).
- **Retention policy específica**: historia médica NO se purga (override del retention sweep — agregar al `RetentionSettings` per-domain exemption).

### Páginas adicionales

#### Pública (sobre Receta 1)

- Landing especialidades — cada doctor con su perfil + agenda widget
- Sobre la clínica — equipo + facilities + accreditations
- Blog de salud — `blogIndex` con disclaimer legal

#### Privada paciente (member portal)

| Página | Page Type | Estructura |
|---|---|---|
| Mi historia médica | custom `medicalRecordPage` | Timeline visits + diagnoses + vital signs + alergias |
| Mis recetas | custom `prescriptionsPage` | List + download PDF + recordatorios |
| Mis exámenes | custom `examResultsPage` | PDF viewer + results trending |
| Próximas citas | `pageBasic` | Calendar + cancel/reschedule actions |
| Resúmenes (futuro) | custom `careSummaryPage` | AI-generated summary del último mes |

#### Privada staff (rol `doctor` o `nurse`)

| Página | Page Type | Role |
|---|---|---|
| Pacientes | `pageBasic` con `compMemberGating.allowedRolesCsv=doctor,nurse` | DataGrid + search |
| Historia médica de paciente | custom `staffMedicalRecordPage` | Editable timeline (audit log every save) |
| Agendar paciente | custom `appointmentEditorPage` | FormStepper |

### Blocks que faltan (gap polimórfico)

Healthcare necesita blocks nuevos que NO existen como `elementSyn*`:

- `elementSynMedicalTimeline` — vital signs / diagnoses / events
- `elementSynPrescriptionCard` — rx detail + refill request
- `elementSynVitalSignChart` — trend de presión / glucosa / etc.
- `elementSynAppointmentScheduler` — calendario + slots disponibles
- `elementSynPatientRecordHeader` — paciente info + identificadores

**Acción**: cuando llegue un cliente healthcare real, scaffold estos elementos con `npx nx generate` siguiendo el pattern de los 122 existentes.

### Compliance settings críticos

- `Synergos:DataProtection:KeyringPath` configurado
- `Synergos:Audit:RetentionDays = 0` (infinito — never purge medical access logs)
- `MembersSettings:RequireEmailConfirmation = true`
- `MembersSettings:RequireTwoFactor = true` (staff)
- `Synergos:Gdpr:RtbfHandler` activo (cuando un paciente solicite borrado, el coordinator anonimiza records preservando audit trail)

---

## Multi-vertical en el mismo deploy

SYNERGOS soporta multi-siteRoot por hostname nativo de Umbraco (ADR 0010 — branding por host). Esto permite:

- 1 deploy CMS + 1 CDN
- N siteRoots distintos en el content tree (cada uno = 1 vertical)
- Cada hostname resuelve a su siteRoot vía `IBrandingProvider`
- Cada siteRoot tiene su brand + settings + pages independientes

Ejemplo:
- `clinica-saludtotal.com` → siteRoot "Clínica Salud Total" (Receta 5)
- `tienda-mode.com` → siteRoot "Tienda Mode" (Receta 2)
- `consultora-leyes.com` → siteRoot "Consultora Leyes" (Receta 1)

**Todo en el mismo proyecto Synergos.CMS** + mismo `C:\LOCAL_CDN`. La diferencia es el content tree per cliente.

## Cómo usar estas recetas

Cuando el arquitecto pregunte "quiero armar un aplicativo X" o "estamos onboarding un cliente Y":

1. Identificar la receta más cercana (1-5 arriba) o combinación.
2. Listar los **brand assets requeridos** (lo que debe pedirle al cliente).
3. Listar los **settings globales** a configurar.
4. Listar las **páginas iniciales** con su page type + estructura.
5. Para cada página, **citar los blocks elementSyn\*** + el bundle UI correspondiente (ver `cms-to-ui-mapping.md`).
6. Confirmar **multi-vertical** si aplica (siteRoot per cliente).
7. Marcar **gaps** si la receta requiere blocks nuevos no scaffolded todavía.

Cierra la respuesta con el orden de bootstrap concreto: **brand → settings → pages → blocks → empalme UI**. El arquitecto puede entonces operar el backoffice paso por paso.
