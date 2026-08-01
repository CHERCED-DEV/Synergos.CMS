# Copy style — drafting de texto editor-facing

Cuando el arquitecto pide texto para campos de contenido (heading, subheading, summary, CTA label, alert text, descripción de card, FAQ pregunta/respuesta, etc.), aplica esta guía.

## Idiomas

Por default ofrecer drafts en `es-CO` (default del sitio) **y** `en-US`. Synergos es multi-culture por diseño (memoria `feedback_variations_culture_default`). Si el arquitecto solo quiere uno, lo pide explícito.

Formato sugerido:

```
**es-CO**: <texto draft>
**en-US**: <texto draft>
```

## Brand-neutral

Drafts NUNCA mencionan marca específica salvo que el arquitecto la nombre. Usa placeholders como `[Marca]`, `[Producto]`, `[Servicio]` cuando el copy lo necesita. Esto permite reusar el draft entre brands del producto sin reescribir.

```
✅ "Conoce cómo [Producto] simplifica tu día"
❌ "Conoce cómo Synergos simplifica tu día"
```

## Caps por tipo de campo

| Campo | Cap (es-CO) | Cap (en-US) | Notas |
|---|---|---|---|
| Heading H1 (page title, hero title) | 60-80 chars | 50-65 chars | Contundente, sin punto final. |
| Heading H2 / Section title | 40-60 chars | 35-55 chars | |
| Heading H3 / Subheading | 30-50 chars | 25-45 chars | |
| Summary / Lead | 140-200 chars | 120-180 chars | 1 frase larga o 2 cortas. |
| Card title | 40-60 chars | 35-55 chars | |
| Card description | 80-140 chars | 70-130 chars | 1 frase. |
| CTA label (action button) | 12-25 chars | 10-22 chars | Verbo de acción + objeto opcional. |
| Alert text (compAlex/cfgAlert) | 60-120 chars | 55-110 chars | Conciso, urgente si aplica. |
| FAQ pregunta | 50-80 chars | 45-75 chars | Termina en `?`. |
| FAQ respuesta | 200-400 chars | 180-350 chars | 1-3 frases. |
| Banner / Hero subtitle | 80-140 chars | 70-130 chars | Complementa el title sin repetirlo. |
| Description del schema (uSync) | **≤120 chars** | **≤120 chars** | Editor-facing, regla dura. |

## Tono

- **Voz activa**, no pasiva. ("Activa tu cuenta" > "Tu cuenta puede ser activada")
- **Tú/tu** (es-CO), no usted. (a menos que el arquitecto lo pida explícito).
- **You/your** (en-US), no `the user`.
- Sin signos de exclamación salvo que el campo sea explícitamente promocional/urgente.
- Sin "etc.", "etcétera", "y más".
- Sin claims comerciales sin aprobación de marca ("el mejor", "líder en…", "el #1") — si el arquitecto los pide, recuérdale que el equipo de marca debe aprobar.

## CTA labels — patrones

Verbo de acción + objeto:

- ✅ "Empezar ahora", "Conocer más", "Descargar PDF", "Reservar demo", "Ver planes", "Contactar ventas"
- ❌ "Click aquí", "Aquí", "Ver", "Más" (sin contexto)

Inglés:

- ✅ "Start now", "Learn more", "Download PDF", "Book a demo", "See plans", "Contact sales"
- ❌ "Click here", "Here", "View", "More"

## SEO — heading H1 vs `<title>` vs description

- **Heading H1** (visible en página): copy editorial, optimizado para humano que lee.
- **seoTitle** (`<title>` HTML): copy SEO, optimizado para SERP, 50-60 chars, puede repetir o variar el H1.
- **seoDescription** (meta description): 140-160 chars, llamada a clic desde SERP, distinta del summary visible en página.

Si el arquitecto solo da el heading, ofrece variantes para los 3.

## Heading + subheading — patrón complementario

El subheading **complementa**, no repite el heading.

```
✅
Heading: "Activa tu cuenta en 2 minutos"
Subheading: "Sin tarjeta de crédito, sin compromiso"

❌
Heading: "Activa tu cuenta"
Subheading: "Activa tu cuenta hoy mismo"
```

## Alert / cfgAlert text — patrones

- **Informativo:** `[Producto] estará en mantenimiento el [fecha] de [hora] a [hora]. Algunas funciones pueden no estar disponibles.`
- **Urgente:** `Acción requerida: actualiza tu contraseña antes del [fecha].`
- **Promocional:** `Lanzamiento: [Característica] ya disponible para todos los planes.`

CTA label corto: "Ver detalles", "Actualizar ahora", "Conocer más".

## FAQ — patrón

Pregunta en primera persona del editor del sitio (NO del usuario):

```
✅ "¿Cómo cancelo mi suscripción?"
✅ "¿Cuánto cuesta el plan Pro?"
❌ "Quiero cancelar mi suscripción"
❌ "Cuánto cuesta?"
```

Respuesta directa, primera frase resuelve la duda; siguientes (si las hay) dan detalle:

```
✅ "Puedes cancelar desde tu panel en cualquier momento. Ve a Configuración → Suscripción → Cancelar. El cambio aplica al final del período facturado actual."
```

## Cuando el arquitecto pide "más opciones"

Si el arquitecto pide alternativas, ofrece **3 variantes** con tono distinto:

1. **Conservadora** (institucional, formal)
2. **Estándar** (default, neutral)
3. **Atrevida** (más conversacional o más punchy)

```
**es-CO**:
1. (formal) "Soluciones empresariales que crecen con tu negocio"
2. (neutral) "Herramientas que crecen contigo"
3. (punchy) "Tu negocio crece. Tu stack también."
```

## Hard rules

- ❌ **Nunca** menciones de marca específica sin que el arquitecto la nombre.
- ❌ **Nunca** claims sin aprobación ("líder", "mejor", "#1", "exclusivo").
- ❌ **Nunca** texto en HTML/Markdown si el campo es plain text. Sí HTML solo si el DataType es `RichtextEditor`.
- ❌ **Nunca** menciones de competidores.
- ❌ **Nunca** placeholders Lorem Ipsum como entrega final — solo si el arquitecto lo pide explícito como filler.
- ✅ Drafts siempre en es-CO + en-US salvo petición contraria.
- ✅ Caps respetados — si excedes, ofrece versión recortada.
- ✅ Tono coherente entre los campos de la misma pieza (heading + subheading + CTA tienen que sonar como un equipo).
