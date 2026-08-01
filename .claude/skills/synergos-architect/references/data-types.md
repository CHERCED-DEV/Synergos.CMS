# DataType picker semantics — intent → DataType

Fuente autoritaria: ADR 0021 + memoria `feedback_picker_semantics`.

## Regla de oro

**Nunca TextBox para datos con semántica de tipo.** Cada intent tiene un DataType canónico. Romper esto:
- Convierte la validación en client/server-side custom (más código, más bugs).
- Pierde el editor UI nativo de Umbraco (preview, picker dialog, etc.).
- Hace imposible análisis offline del contenido.

## Tabla canonical

| Intent | DataType alias | Archivo uSync | Notas |
|---|---|---|---|
| URL externa o interna | `MultiURLPicker` | `DataTypes/MultiURLPicker.config` | Soporta interno (Content node), externo (URL libre), media (link a archivo). NUNCA TextBox para "link". |
| Imagen única | `ImageMediaPicker` | `DataTypes/ImageMediaPicker.config` | Restringe a tipo `image`. |
| Múltiples imágenes (galería) | `MultipleImageMediaPicker` | `DataTypes/MultipleImageMediaPicker.config` | Idem en multi. |
| Media genérica única (img/video/audio/doc/svg) | `MediaPicker` | `DataTypes/MediaPicker.config` | Sin restricción de tipo. |
| Media múltiple genérica | `MultipleMediaPicker` | `DataTypes/MultipleMediaPicker.config` | |
| Documento (PDF/Word/Excel) | `MediaPicker` con MediaType `file` | `DataTypes/MediaPicker.config` | El `MediaType` filtra qué se permite. |
| Video upload | `UploadVideo` | `DataTypes/UploadVideo.config` | |
| Audio upload | `UploadAudio` | `DataTypes/UploadAudio.config` | |
| File upload (genérico) | `UploadFile` | `DataTypes/UploadFile.config` | |
| Vector graphics (SVG) | `UploadVectorGraphics` | `DataTypes/UploadVectorGraphics.config` | |
| Article upload | `UploadArticle` | `DataTypes/UploadArticle.config` | |
| Enum cerrado (variante de tema, alineación, dirección flex…) | `Dropdown` (Dropdown.Flexible) | `DataTypes/Dropdown.config` o `DTSelect*.config` | El proyecto tiene 35+ `DTSelect*` específicos para enums de layout/theme. Reusa antes de crear DataType nuevo. |
| Enum multi-select | `DropdownMultiple` | `DataTypes/DropdownMultiple.config` | |
| Boolean (sí/no, on/off, mostrar/ocultar) | `TrueFalse` | `DataTypes/Truefalse.config` | NUNCA Dropdown con dos opciones para esto. |
| Bool tri-state (`inherit | true | false`) — específico de la cascada page→siteRoot | `Dropdown` con 3 opciones | Convención Synergos para campos heredables del orchestration. |
| Enum visual con radio | `Radiobox` | `DataTypes/Radiobox.config` | Pocos casos — preferir Dropdown salvo UI razón. |
| Multi-checkbox enum | `CheckboxList` | `DataTypes/CheckboxList.config` | |
| Color brand (palette curada) | `ApprovedColor` | `DataTypes/ApprovedColor.config` | Limita a la paleta del brand, no color picker libre. |
| Color libre | (no estándar — preferir token CSS o ApprovedColor) | — | Si hay que romper esto, pedir excepción razonada. |
| Texto corto, una línea (label, alias técnico, código) | `Textstring` | `DataTypes/Textstring.config` | |
| Texto plano multilínea, sin formato | `Textarea` | `DataTypes/Textarea.config` | |
| Texto rico (HTML, listas, links inline, blockquote) | `RichtextEditor` | `DataTypes/RichtextEditor.config` | Configurar toolbar al mínimo necesario — no abrir Pandora. |
| Numérico entero/decimal | `Numeric` | `DataTypes/Numeric.config` | |
| Tags (taxonomía libre) | `Tags` | `DataTypes/Tags.config` | |
| Fecha (sin hora) | `DatePicker` | `DataTypes/DatePicker.config` | |
| Fecha + hora | `DatePickerWithTime` | `DataTypes/DatePickerWithTime.config` | UTC para visibilityMode=scheduled (compAlex). |
| Pick a content node (referencia interna) | `ContentPicker` | `DataTypes/ContentPicker.config` | Para single-node refs. Para URL preferir MultiURLPicker. |
| Pick a member | `MemberPicker` | `DataTypes/MemberPicker.config` | |
| Block List items (FAQ, testimonios, gallery, timeline, banner slides, features, form fields, nav items, tabs, logos, CTA items) | `DTBlockList*` | `DataTypes/DTBlockList*.config` | 12 list types curados. Reusa el que aplique. |
| Block Grid (sections de página) | `DTBlockGridSections` | (DataType) | Solo páginas usan esto para `sections`. |
| Label readonly (display-only computed) | `Label*` (LabelString, LabelInteger, LabelBigint, LabelDecimal, LabelDatetime, LabelTime) | `DataTypes/Label*.config` | Para mostrar valor sin permitir editar. |

## DTSelect* — enums específicos del Layout Composer

35+ DataTypes `DTSelect*` curados para campos de layout/theme. Algunos:

- `DTSelectDisplayMode`, `DTSelectFlexDirection`, `DTSelectFlexWrap`, `DTSelectJustifyContent`, `DTSelectAlignItems`, `DTSelectAlignContent`, `DTSelectJustifyItems`
- `DTSelectGridTemplate`, `DTSelectGridRows`
- `DTSelectSpacingScale`, `DTSelectContainerType`
- `DTSelectTheme`, `DTSelectMobileCollapse`

**Antes de crear un DataType `Dropdown` nuevo**: busca si ya existe un `DTSelect*` con las mismas opciones.

## Reglas adicionales

### Cambio de storage type

Si vas a cambiar un campo de TextBox → Dropdown/UrlPicker/MediaPicker/Tags, **asigna Key nueva al property**. Reusar el mismo Key corrompe data legacy (memoria `feedback_new_key_when_storage_type_changes`).

### Variations

Defecto **Culture** para campos de texto/copy editor-facing. Defecto **Nothing** solo para:
- Flags técnicos (booleans on/off de comportamiento).
- Enums de layout/theme/style (compartidos entre culturas).
- Timestamps.
- IDs / aliases técnicos.
- Números/cantidades sin connotación cultural.

### Descriptions

Cap **≤120 chars**, 1 frase, lenguaje editor (no ADR jargon). Estas descriptions son UI del backoffice — el editor las lee al hover sobre el label del campo.

Ejemplos buenos:
- ✅ "Texto principal del banner. Aparece en grande en el centro."
- ✅ "Imagen destacada. Se usa también como og:image si no hay seoOgImage."

Ejemplos malos:
- ❌ "compcontentheading.heading — el heading editorial siguiendo ADR 0021 mapping de DataType por intent."
- ❌ "Field para el title de la card que se renderiza en SynHost/Card.cshtml partial."

## Iconos

Si el DataType tiene icon prop o el field requiere asociar un icono, **verifica en `~/.claude/projects/c--Users-HITMA-Desktop-synergos/memory/reference_umbraco13_icons.txt`** antes de sugerir.

## Cuando proponer DataType nuevo

Solo si:
1. No existe DataType estándar Umbraco que cubra el intent.
2. No existe `DTSelect*` con las opciones del enum.
3. El intent es genuinamente recurrente (3+ campos en el schema lo van a usar).

Si se cumple, **eso es trabajo de ola schema**, no de autoría de contenido. Redirige al flow de la memoria `feedback_ola_execution_flow`.
