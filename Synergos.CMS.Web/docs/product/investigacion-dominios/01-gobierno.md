## 1. El comprador

**Segmento concreto:** la **alcaldía de categoría 1–4 y la gobernación**, no "el sector público". Y dentro de ella, tres firmas distintas que hay que saber separar:

| Quién | Qué le duele | Qué firma |
|---|---|---|
| **Secretario General** (o Jefe de la Oficina TIC / de Gobierno Digital) | El ITA de la Procuraduría, el PQRSD vencido, el requerimiento de un ente de control | El contrato de portal / trámites / PQRSD |
| **Secretario de Hacienda** | Recaudo de predial e ICA, conciliación, cartera | El ERP municipal (el contrato grande) |
| **Alcalde** | Que su plan de desarrollo tenga "gobierno digital" ejecutado antes de que se acabe el periodo | El presupuesto, vía plan de adquisiciones |

**Tamaño típico y realidad fiscal.** El **87,9 % de los municipios colombianos es categoría 6** (≤10.000 habitantes, ICLD ≤15.000 SMMLV) — VERIFICADO ([SciELO / Reflexión Política, *Los municipios de sexta categoría de Colombia*](http://www.scielo.org.co/scielo.php?script=sci_arttext&pid=S0120-30532020000100137)). Ese 88 % **no es el comprador**: no tiene ni presupuesto ni personal. El mercado real son los ~100–150 municipios de categoría especial a 4 más las 32 gobernaciones y las entidades descentralizadas (ESE, empresas de servicios públicos, institutos). MinTIC contó **939 alcaldías y 10 gobernaciones con sede electrónica en GOV.CO/Territorial** (VERIFICADO — [MinTIC, sala de prensa](https://www.mintic.gov.co/portal/inicio/Sala-de-Prensa/Noticias/161369:En-2021-migraran-3-450-sitios-web-de-entidades-publicas-al-portal-unico-del-Estado-GOV-CO-Territorial-ministra-Karen-Abudinen)); el universo total de municipios (~1.100) es INFERIDO, no lo confirmé en esta sesión.

**Qué gasta hoy — plata real, no estimados.** Consulté el dataset abierto de contratación (SECOP Integrado, `jbjy-vk9h` en datos.gov.co) y estos son contratos firmados, VERIFICADOS uno por uno:

| Qué compró | Entidad | Valor (COP) | Fecha |
|---|---|---:|---|
| ERP **SYSMAN WEB** — licenciamiento, soporte, alojamiento | Alcaldía de Melgar | 546.665.510 | 2024-10-03 |
| **SJT ERP** SaaS (financiera, RRHH, gestión) | Alcaldía de Barbosa (Santander) | 111.950.000 | 2025-06-06 |
| **NEPTUNO** impuestos + contable + **portal web trámites en línea** | Alcaldía de Aguachica | 70.000.000 | 2023-06-06 |
| **SYS APOLO** software integrado | Alcaldía de Orito | 55.000.000 | 2026-07-28 |
| **Software Integrado V6** + alojamiento | Alcaldía de Salgar | 55.000.000 / 50.000.000 | 2026 / 2025 |
| **SIGAM** software integrado | Alcaldía de Manzanares | 40.000.000 | 2026-01-23 |
| Licenciamiento software **DOCU** (Planeación) | Alcaldía de Girardot | 37.995.451 | 2025-06-27 |
| **SGDEA** SaaS, gerencia integral de implementación | ACODER | 400.000.000 | 2024-12-02 |
| SGDEA soporte + desarrollo (**ARCHIVOX / TRAMITEX**) | DIVRI (MinDefensa) | 570.018.626 | 2024-05-29 |
| SGDEA soporte y nuevos desarrollos | MinDefensa | 1.166.242.040 | 2023-08-08 |

**Y el dato que duele:** el **sitio web no se compra, se contrata como persona**. Muestra de contratos de 2024 con objeto "página web / portal web" en alcaldías: Calarcá COP 8.635.029; Iza COP 12.600.000 (incluye "informe ITA y PETI"); Ocaña COP 13.500.000; Guateque COP 16.200.000; Valdivia COP 20.500.000 ("de acuerdo a los lineamientos de la Ley 1712"); Ricaurte COP 28.000.000 ("administración del módulo de trámites de la página web"). Todos VERIFICADOS en el mismo dataset. Son **contratos de prestación de servicios (OPS) a un profesional**, no licencias de software.

**El dolor por el que ya paga:** no es "quiero un mejor sitio". Es **(a)** que la Procuraduría le mide el ITA y lo publica, **(b)** que los PQRSD se le vencen y le llegan tutelas, **(c)** que el recaudo de predial se le cae si no hay pago en línea, y **(d)** que su ERP no le deja publicar un trámite nuevo sin pedirle un desarrollo al proveedor.

---

## 2. La competencia

| Competidor real | Qué hace bien | Dónde deja el hueco | Fuente |
|---|---|---|---|
| **Mi Colombia Digital / GOV.CO Territorial (MinTIC)** | **Regala el sitio web**: hosting, administración técnica, mantenimiento, actualizaciones, soporte y backups, gratis, desde hace más de una década. 360 portales activos (223 municipios, 7 gobernaciones). Cumple la identidad visual GOV.CO por construcción. | Es una plantilla de **publicación**: integra con SECOP/SIGEP "como enlaces de contenido". No radica un expediente, no corre una máquina de estados, no cobra una tasa, no notifica con acuse. Ahí está todo el hueco. | VERIFICADO — [Gobierno Digital](https://www.gobiernodigital.gov.co/623/w3-article-72719.html), [MinTIC plataformas territoriales](https://gobiernodigital.mintic.gov.co/692/w3-article-76019.html) |
| **Sysman (Stefanini Sysman)** — ERP sector público | 30+ años en gobierno; predial, ICA, tasas ambientales, financiera (>150 entidades), rentas; **ya tiene "trámites en línea de impuesto predial"** y factura con código de barras | El trámite es un **módulo de código**: agregar uno nuevo es un desarrollo suyo. La cara pública es pobre y no cubre la carga de transparencia/accesibilidad. Dueño del dato tributario ⇒ integrarse es en sus términos. | VERIFICADO — [sysman.com.co](https://sysman.com.co/erp-sector-publico-y-gobierno/), [trámites en línea predial](https://sysman.com.co/tramites-en-linea-de-impuesto-predial/) + contrato Melgar en SECOP |
| **Orfeo / Orfeo NG / Orfeo Express / Argo (Infométrika, SkinaTech)** | SGDEA **GPL**, nacido en la Superintendencia de Servicios Públicos. Orfeo NG SaaS se posiciona explícitamente para "municipios de sexta categoría y entidades con presupuesto limitado". Cubre PQRS, ventanilla única y trazabilidad de términos. | Es correspondencia y archivo, no servicio al ciudadano: la cara pública, la ficha del trámite y el pago no son suyos. UX de los 2000. | VERIFICADO — [orfeolibre.org](https://orfeolibre.org/inicio/orfeo-software-de-gestion-documental/), [Infométrika Argo](https://infometrika.com/sgdea-sistema-de-gestion-documental-argo-orfeogpl-ik/), [Orfeo Express](https://www.orfeoexpress.com/portal/) |
| **Mercurio (Servisoft)** | SGDEA comercial consolidado, 300+ organizaciones; presente en contratos grandes (INDER Medellín, digitalización certificada) | Mismo hueco que Orfeo, en versión cara. | VERIFICADO — [servisoft.co](https://servisoft.co/) + SECOP |
| **SAIA Software** | 5 módulos: correspondencia, documental, archivo, procesos (BPM) | BPM documental ≠ trámite ciudadano con tasa, cita y acto administrativo notificado | VERIFICADO — [saiasoftware.com](https://www.saiasoftware.com/) |
| **NEPTUNO, SJT, SYS APOLO, V6, SIGAM, DOCU** | ERP/soluciones territoriales de nicho regional, precio COP 38–112 M/año, relación política local | Ninguno vende cara pública ni cumplimiento de Resolución 1519. Compiten por precio y por conocer al secretario de hacienda. | VERIFICADO — contratos SECOP citados en §1 |
| **El contratista OPS de comunicaciones** | Cuesta COP 9–28 M/año, actualiza la web, diligencia el ITA y el PETI | No escala, no es software, se va con el alcalde | VERIFICADO — contratos SECOP citados en §1 |

**Global (INFERIDO, no verificado en esta sesión):** Granicus, CivicPlus, Accela, Tyler Technologies, OpenGov en EE. UU.; 1Doc y Betha en Brasil. **Ninguno compite en Colombia hoy** — no aparecen en los contratos que revisé. No cambian la conclusión y por eso no los desarrollo.

---

## 3. Lo que la ley obliga

Este dominio es el de **mayor carga regulatoria de los nueve**. Y la mayor parte de esa carga es *contenido publicado*, que es exactamente el terreno del CMS.

| Exigencia | Jurisdicción | Traducida a capacidad / feature concreta | Fuente |
|---|---|---|---|
| **WCAG 2.1 nivel AA** obligatorio desde el 1-ene-2022 en todo portal y sede electrónica, y en todo rediseño o actualización | Colombia — Res. MinTIC 1519/2020, Anexo 1 | Gate de accesibilidad en el pipeline del CMS (contraste, foco, landmarks, alt obligatorio, `lang`, formularios etiquetados) + **declaración de conformidad publicable**. Hoy el schema ya fuerza alt en media; falta el gate automatizado. | VERIFICADO — [Normograma MinTIC](https://normograma.mintic.gov.co/mintic/compilacion/docs/resolucion_mintic_1519_2020.htm), [Directrices Anexo 1 (PDF)](https://gobiernodigital.mintic.gov.co/692/articles-160770_Directrices_Accesibilidad_web.pdf) |
| **Menú "Transparencia y acceso a la información pública"** con estructura estandarizada; alimenta el **ITA** que mide la Procuraduría | Colombia — Ley 1712/2014 + Res. 1519/2020 Anexo 2 | **`schema-del-cms`**: un árbol de DocTypes que *nace* con los 10 niveles del menú, más el esquema de publicación, el registro de activos de información y el índice de información clasificada. Es la feature más vendible del CMS en este dominio. | VERIFICADO — [Procuraduría ITA](https://www.procuraduria.gov.co/Pages/ita.aspx), [Anexo 2 (PDF)](https://gobiernodigital.mintic.gov.co/692/articles-178658_Estandares_informacion.pdf). El detalle "10 niveles / 225 de 239 ítems" lo leí en el resumen de la [auditoría ITA de la ANI](https://www.ani.gov.co/sites/default/files/auditoria_de_cumplimiento_a_ita_y_resolucion_1519.pdf), no abriendo el PDF completo |
| **Términos del derecho de petición**: 15 días hábiles general; **10 días** para peticiones de documentos e información | Colombia — Ley 1755/2015 art. 14 | **Reloj de términos** sobre la instancia de `Api.Workflow`: fecha de vencimiento calculada en días **hábiles colombianos**, alerta a N días, marca de vencido. Hoy `Api.Workflow` no tiene ningún concepto de tiempo. | VERIFICADO — [Función Pública, Ley 1755](https://www.funcionpublica.gov.co/eva/gestornormativo/norma.php?i=65334), [DNP, normativa PQRSD (PDF)](https://colaboracion.dnp.gov.co/CDT/Programa%20Nacional%20del%20Servicio%20al%20Ciudadano/NORMATIVA%20GESTI%C3%93N%20PQRSD%20F.pdf). El plazo de 30 días para consultas es INFERIDO |
| **Sede electrónica** con calidad, seguridad, disponibilidad, accesibilidad, neutralidad e interoperabilidad; toda autoridad debe tener al menos una | Colombia — Ley 2080/2021 (reforma CPACA) | Es el *nombre legal* del producto. Implica dominio `.gov.co`, integración al Portal Único como sede compartida, y disponibilidad demostrable. | VERIFICADO — [Ley 2080 de 2021](https://www.funcionpublica.gov.co/eva/gestornormativo/norma.php?i=156590) |
| **Expediente electrónico** con autenticidad, integridad y disponibilidad garantizadas | Colombia — Ley 2080/2021 + Acuerdo AGN 003/2015 (reglamenta art. 21 Ley 594/2000) | `Api.Documents` + `Api.Workflow` + `Api.Audit` **más**: foliado consecutivo, metadatos archivísticos, TRD y política de retención. El foliado y la TRD hoy no existen en ninguna parte. | VERIFICADO — [Acuerdo 003 de 2015, AGN](https://normativa.archivogeneral.gov.co/acuerdo-003-de-2015/) |
| **Notificación electrónica**: solo si el administrado la aceptó, y surte efecto desde que accede — **la administración debe certificar fecha y hora del acceso** | Colombia — Ley 2080/2021 | No basta el log de envío de `Api.Notifications`. Hace falta **acuse de acceso certificado** (quién abrió, cuándo, con evidencia sellada). `Api.Messaging` tiene `POST /v1/messages/{id}/read`, que es el germen correcto. | VERIFICADO — [Ley 2080 de 2021](https://www.funcionpublica.gov.co/eva/gestornormativo/norma.php?i=156590) |
| **Racionalización de trámites** obligatoria en nivel nacional **y territorial**; registro en **SUIT**; se puede dar incentivo a quien tramite en línea | Colombia — Ley 2052/2020 | El catálogo de trámites del CMS debe hablar el vocabulario del SUIT (nombre, normativa, requisitos, canal, costo, tiempo). `tramitePage` **ya tiene esos campos**. | VERIFICADO — [Ley 2052 de 2020](https://www.funcionpublica.gov.co/eva/gestornormativo/norma.php?i=140250) |
| **Servicios Ciudadanos Digitales**: autenticación digital, carpeta ciudadana, interoperabilidad; integrables al Portal Único | Colombia — Decreto 620/2020 | `integracion-externa`: federar identidad contra la Autenticación Digital del Estado en vez de emitir credencial propia. | VERIFICADO — [Decreto 620 de 2020](https://www.funcionpublica.gov.co/eva/gestornormativo/norma.php?i=118337) |
| **Firma digital / mensaje de datos** con valor probatorio | Colombia — Ley 527/1999, Decreto 2364/2012 | El certificado que emite la entidad necesita **certificado digital de una entidad de certificación acreditada** (Certicámara, Andes SCD, GSE) y estampado cronológico. `Api.Signing` es HMAC: sirve para verificar, **no** para dar fe pública. | INFERIDO en cuanto al detalle operativo; el marco normativo es de conocimiento público y no lo verifiqué con URL en esta sesión |
| **Recaudo de dinero público** vía PSE con convenio de recaudo del banco de la entidad | Colombia — práctica de tesorería + ACH Colombia | No es una cuenta de comercio de un PSP: es convenio bancario + referencia de recaudo + conciliación con tesorería. Los municipios ya operan así (Villanueva, Gutiérrez en psepagos.co). | VERIFICADO — [PSE](https://www.pse.com.co/persona/), [Banco de Bogotá botón PSE](https://www.bancodebogota.com/empresas/recaudos-y-pagos/recaudo-en-medios-electronicos/boton-pagos-internet-pse) |

---

## 4. Ajuste contra lo construido

### Ya cubierto

- **`Synergos.Api.Workflow`** — es la pieza más afortunada del árbol para este dominio, y no por casualidad: `WorkflowDefinition.cs` documenta la clave de ejemplo como `gov.tramite` y las transiciones como `radicar` / `aprobar`. Leí `Domain/WorkflowRules.cs` completo: la definición es **dato, no código** (agregar un estado no exige desplegar), hay **guarda por rol** en `Resolve()` con el comentario explícito *"la guarda por rol es lo que hace que esta capacidad sirva a Gobierno: radicar lo hace el ciudadano y aprobar el funcionario"*, y rechaza sola `transition_not_allowed`, `role_required`, `instance_closed`, `transition_from_final`, `ambiguous_transition`, `initial_is_dead_end`. `Endpoints/WorkflowEndpoints.cs` expone `POST /v1/instances`, `POST /v1/instances/{id}/fire` con `Idempotency-Key`, y `GET /v1/instances?subjectKind&subjectId` — o sea, "todos los expedientes de este ciudadano". El historial (`HistoryEntry`) guarda transición, origen, destino, **quién**, nota y timestamp. Eso *es* la historia del acto administrativo.
- **`Synergos.Api.Audit`** — `AuditRules.CheckWritable` rechaza entrada sin actor (*"un proceso también es un actor: dilo"*) y sin target; `CheckQuery` exige filtro para que la bitácora no sea un volcado. Es la trazabilidad que exige el dominio.
- **`Synergos.Api.Documents`** — allowlist explícita (PDF, PNG/JPEG/WEBP/GIF, docx, xlsx, csv), tope 10 MB, huella SHA-256 de cada fichero, y enlaces firmados con **el vencimiento dentro de la firma** y la firma verificada **antes** que el vencimiento. Es el manejo de anexos correcto.
- **`Synergos.Api.Consent`** — `CheckGrant` exige propósito acotado (*"un consentimiento global no es consentimiento informado"*) y **versión del texto aceptado**; `CheckActive` distingue los tres "no" (nunca otorgado / revocado / vencido) y hay `POST /v1/grants/forget`. Es Ley 1581 con la granularidad correcta.
- **`Synergos.Api.Messaging`** — hilos con participantes cerrados, rechazo a quien no es participante, y `POST /v1/messages/{id}/read`. Es la correspondencia del expediente.
- **`Synergos.Api.Orders` + `Synergos.Api.Payments` separadas** — el doc 07 §"Dinero" justifica el corte precisamente con este dominio: *"un trámite gubernamental se radica y puede pagarse después, o no pagarse nunca"*. `OrderEndpoints.cs` (place/fulfill/cancel) confirma que el pedido vive sin cobro.
- **El CMS ya tiene el vertical.** `uSync/v9/ContentTypes/tramitepage.config` define `tramiteSlug`, `tramiteName`, `tramiteSummary`, `tramiteCategory`, `tramiteAgency`, `tramiteChannel`, `tramiteDescription`, `tramiteEligibility`, `tramiteRequired`, `tramiteNormativa`, `tramiteIsFree`, `tramiteFee`, `tramiteEstimatedDays`, `tramiteRequiresAppointment` y `tramiteFormSections` (BlockList) — más `elementtramiteformsection.config` y `elementtramiteformfield.config`. `Controllers/GovController.cs` (803 líneas) sirve catálogo → ficha → formulario dinámico → radicar → carpeta del ciudadano → cola del funcionario → decisión, con identidad **server-trusted** desde el gate de Member (el comentario documenta que antes salía del body y cualquiera radicaba a nombre de otro) y allowlist de subida pdf/jpeg/png a 10 MB.

### Parcial

- **`Synergos.Api.Workflow` — le falta el tiempo.** Leí las reglas enteras: no hay un solo `DateTimeOffset` fuera de `HistoryEntry.AtUtc`. No hay vencimiento, ni días hábiles, ni escalamiento, ni "muéstrame lo que vence esta semana". En PQRSD **el plazo es el producto**: los 15 y 10 días de la Ley 1755 son lo que el jefe de la oficina jurídica mira todas las mañanas. Sin esto, `Workflow` sirve para Salud y para Tienda pero no cierra una venta de Gobierno.
- **`Synergos.Api.Signing` — firma, pero no da fe.** `SigningRules.Sign` arma `keyId.expiraUnix.payloadB64.HMAC` con el keyId y el vencimiento **dentro** de lo firmado, y verifica la firma antes que la vigencia. Está bien construido y sirve para verificar un certificado emitido. Lo que **no** hace es firmar con certificado digital de entidad acreditada ni estampar cronológicamente — que es lo que le da valor probatorio al certificado que expide una alcaldía.
- **`Synergos.Api.Audit` — append-only por convención, no por criptografía.** No hay encadenamiento de hash ni sellado del bloque. Para "trazabilidad del acto administrativo" ante un ente de control, un fichero append-only sin cadena de integridad es una afirmación, no una prueba.
- **`Synergos.Api.Documents` — le falta lo archivístico.** Tope de 10 MB con el contenido en base64 **en el cuerpo** de la petición: un plano escaneado o un expediente de licencia urbanística no cabe. Y no hay foliado consecutivo, ni serie/subserie documental, ni TRD, ni PDF/A — que es justo lo que exige el Acuerdo 003 de 2015 y lo que venden Orfeo, Mercurio y SAIA.
- **`Synergos.Api.Notifications` — no envía y no acusa.** `Program.cs` registra `LoggingNotificationSender`. Además `NotificationRules` valida plantilla, canal y tope de frecuencia (20/hora/destinatario), pero el registro es de **envío**, no de **acceso**: la notificación electrónica del CPACA surte efecto cuando el administrado accede, y la administración debe certificarlo.
- **`Synergos.Api.Identity` — menos de lo que dice el catálogo.** El doc 07 lista "2FA" entre lo que posee; en `Endpoints/IdentityEndpoints.cs` solo hay `principals`, `credentials/verify`, `roles/grant`, `roles/revoke`, `unlock`. Hay bloqueo por intentos (15 min) pero **no encontré endpoint de segundo factor**. Para un funcionario que aprueba actos administrativos, eso se pregunta en el pliego.
- **El vertical Gobierno del CMS no usa el árbol.** Este es el hallazgo incómodo: `Synergos.CMS.Application/Services/Impl/StubCaseWorkflowService.cs` implementa **su propia** máquina de estados (`Radicado → EnRevision → Subsanacion → Resuelto/Rechazado`, con tabla de outcomes `approve`/`reject`/`request-info`) en memoria/fichero, sin llamar a `Api.Workflow` por HTTP. Y según la investigación de pagos ya hecha en el repo (`investigacion-pagos/03-necesidades-por-vertical.md`, §Gobierno), `StubApplicationService` *"solo llama `CreateSessionAsync` cuando la tasa aplica — nunca llama `CaptureAsync` ni `GetStatusAsync` ni `RefundAsync`"* y el `paymentSessionId` **ni se persiste**. La demo funciona; el cableado no existe.
- **No hay `Synergos.Bff.Gobierno`.** Solo `Bff.Salud` y `Bff.Tienda` sobre `Bff.Core` (`Saga.cs`, `SagaEngine.cs`, `Compensator.cs`, `CompensationSweeper.cs`).

### Falta

| Necesidad | Por qué ninguna existente la cubre | Tipo |
|---|---|---|
| **Reloj de términos legales** (días hábiles colombianos con festivos, vencimiento, alerta, escalamiento) | `Api.Workflow` no modela tiempo en absoluto | `feature-del-bff` + tipo puro en `Synergos.Core` (calendario hábil = función pura, no capacidad — filtro 3 del doc 07) |
| **PQRSD como objeto de primera clase**: petición anónima, con reserva de identidad, y **traslado por competencia a otra entidad** | El traslado cruza fronteras de entidad; `Api.Workflow` avanza una instancia, no la transfiere a otro operador | `feature-del-bff` |
| **Kit de cumplimiento de sede electrónica**: menú de Transparencia con los 10 niveles del Anexo 2, esquema de publicación, registro de activos de información, datos abiertos, declaración de accesibilidad, UI kit GOV.CO | Ninguna `Api.*` publica contenido; es exactamente lo que el CMS sabe hacer y hoy no trae armado | `schema-del-cms` |
| **Gate automatizado WCAG 2.1 AA** en el pipeline | `check-css-parity.mjs` verifica clases, no accesibilidad | `schema-del-cms` (herramienta de build) |
| **Autenticación Digital del Estado / Carpeta Ciudadana** (Decreto 620/2020) | `Api.Identity` emite credencial propia; el Estado exige federar | `integracion-externa` |
| **Interoperabilidad (plataforma del Estado)** para no volver a pedirle al ciudadano lo que el Estado ya tiene | Ninguna capacidad consulta terceros; el `Ref` es opaco por diseño | `integracion-externa` |
| **Firma digital certificada + estampado cronológico** | `Api.Signing` es HMAC | `integracion-externa` (Certicámara / Andes SCD / GSE) |
| **Recaudo público con convenio bancario + conciliación de tesorería** | `Api.Payments` modela intención/captura/devolución de comercio, y encima con `LoggingPaymentProvider`; el municipio recauda contra su cuenta oficial con referencia de recaudo | `integracion-externa` + `feature-del-bff` |
| **Expediente archivístico** (foliado, series/subseries, TRD, transferencias, PDF/A, retención) | `Api.Documents` guarda ficheros con retención; no es un SGDEA | `capacidad-nueva` (o, más barato y más honesto: **integrar Orfeo** en vez de competirle) |
| **`Synergos.Bff.Gobierno`** | Los seis orquestadores faltantes incluyen Gobierno | `feature-del-bff` |

---

## 5. Backlog priorizado

| Feature | Por qué vende (y por qué es verdad) | Esfuerzo | Depende de | Prio |
|---|---|:-:|---|:-:|
| **Reloj de términos legales** sobre la instancia de Workflow (días hábiles CO, vencimiento, semáforo, alerta) | *"Su oficina jurídica abre la cola y ve, en rojo, los cuatro PQRSD que vencen el jueves."* Verdad: la Ley 1755 fija 15 y 10 días hábiles y hoy `Api.Workflow` no tiene ni un `DateTimeOffset` de vencimiento — sin esto no es un módulo de PQRSD, es una lista | M | `Api.Workflow`, calendario en `Core` | **1** |
| **Kit ITA / Resolución 1519**: menú de Transparencia con los 10 niveles precargados como schema + accesibilidad AA verificada + UI kit GOV.CO | *"Su ITA sube el día que publica, no cuando contrate a alguien a diligenciar la matriz."* Verdad: la Procuraduría publica el índice; los contratos de Iza y Valdivia dicen literalmente "informe ITA" y "lineamientos Ley 1712" | L | `schema-del-cms` | **1** |
| **`Bff.Gobierno`** cableando el vertical del CMS a `Api.Workflow` + `Api.Documents` + `Api.Audit` + `Api.Consent` | *"El expediente que ve el funcionario y la bitácora que audita el control interno son el mismo dato."* Verdad: hoy `StubCaseWorkflowService` duplica la máquina de estados dentro del CMS y no llama a ninguna capacidad | L | `Bff.Core` | **1** |
| **Emisor real de notificaciones** (correo/SMS) + **acuse de acceso certificado** | *"La notificación surte efecto y usted puede probar cuándo la abrió."* Verdad: Ley 2080 exige certificar fecha y hora de acceso; hoy hay `LoggingNotificationSender` | M | `Api.Notifications`, `Api.Messaging` | **1** |
| **Autoría de trámites sin desplegar** (ya existe: `tramitePage` + secciones/campos) — empaquetarlo y demostrarlo | *"Sale el decreto el lunes; el martes su secretaria publica el trámite nuevo."* Verdad: el DocType y el BlockList de formulario ya están; ningún ERP municipal lo ofrece | S (empaquetado) | ya construido | 2 |
| **Recaudo con convenio bancario + referencia + conciliación** | *"El dinero cae en la cuenta de la tesorería, con el mismo formato de conciliación que ya usan."* Verdad: los municipios ya recaudan por PSE con convenio; una cuenta de comercio de PSP no aplica a fondos públicos | L | `Api.Payments`, banco | 2 |
| **Federación con Autenticación Digital del Estado** | *"El ciudadano entra con la identidad del Estado; usted no custodia contraseñas."* Verdad: Decreto 620/2020 y quita del pliego la pregunta de custodia de credenciales | L | `integracion-externa` | 2 |
| **Firma digital certificada + estampado** del certificado emitido | *"El certificado que descarga el ciudadano tiene valor probatorio."* Verdad: `Api.Signing` HMAC no da fe pública | M | Certicámara/ONAC | 2 |
| **Cadena de integridad en `Api.Audit`** (hash encadenado + sellado) | *"El control interno verifica que nadie tocó la bitácora."* | S | `Api.Audit` | 3 |
| **Foliado + TRD en `Api.Documents`** o adaptador a Orfeo | *"Su expediente electrónico cumple el Acuerdo 003."* | XL / M (adaptador) | AGN | 3 |
| **Cita presencial** (`Api.Booking` para ventanilla) | *"Reserve turno para el trámite que exige presencia."* Verdad: `tramiteRequiresAppointment` ya existe en el schema y no lo consume nada | S | `Api.Booking` | 3 |

**Los cuatro de prioridad 1 no son negociables** porque los tres primeros son la diferencia entre un demo y un sistema, y el cuarto es la diferencia entre un trámite y un acto administrativo. Todo lo demás cierra tratos o diferencia, pero no bloquea.

---

## 6. El ángulo CMS — sin ser amable

**Qué puede cambiar el editor sin tocar código, hoy, verificado en el schema:**

Un funcionario sin desarrollador puede crear una `tramitePage` completa: slug, nombre, resumen, categoría, entidad, canal de atención, descripción, **quién puede** (lista), **documentos requeridos** (lista), **normativa**, si es gratuito, la **tasa en COP**, los **días estimados**, si **requiere cita**, y el **formulario entero** — secciones y campos — armado con Block List. Publica, y el catálogo, la ficha y el formulario del ciudadano existen. Eso es real: está en `tramitepage.config`, `elementtramiteformsection.config`, `elementtramiteformfield.config` y lo sirve `GovController`.

**Por qué importa acá más que en ningún otro de los nueve dominios:** un trámite **cambia por acto administrativo**, no por sprint. Sale un decreto, cambia un requisito, se elimina un trámite por racionalización (Ley 2052). En Sysman, en Neptuno o en SYS APOLO eso es una orden de desarrollo al proveedor, con cotización y tiempo. Y encima, la mitad de la obligación legal de este dominio —transparencia, publicación proactiva, accesibilidad, datos abiertos— **es literalmente gestión de contenido**. En Salud o en Tienda el CMS es la vitrina; acá el CMS es parte del cumplimiento.

**El veredicto honesto: el CMS es un extra con dientes, no el argumento de venta.** Y la razón es dura y verificada: **MinTIC regala el sitio web** — hosting, administración, mantenimiento, soporte y backups — desde hace más de una década, y ya corre 223 portales municipales. El presupuesto que hoy existe para "la página" son COP 9–28 millones al año **para contratar a una persona**, no para licenciar software. Si el pitch es "les vendemos un CMS mejor", la respuesta es "ya tengo uno gratis del ministerio" y la conversación se acaba.

El argumento de venta es el **expediente**: radicar, avanzar con guardas de rol, vencer en plazo, notificar con acuse, y dejar bitácora. El CMS es lo que hace creíble la promesa de que **agregar el trámite número 41 no cuesta un contrato de desarrollo**. Es el diferenciador *dentro* de la venta, no la venta.

Y sobre la promesa del arquitecto —"nos adaptamos a cualquier sistema de negocio"— en Gobierno hay que decirlo al revés. **La flexibilidad no es un argumento en un pliego; es un riesgo.** El comprador público no premia la elegancia arquitectónica: premia "cumple la Resolución 1519", "cumple el Acuerdo 003", "acredita experiencia en contratos de objeto similar". Vender adaptabilidad a un comité evaluador es vender incertidumbre. Lo que hay que vender es **conformidad demostrable**, y usar la adaptabilidad para llegar a esa conformidad más barato que el competidor.

---

## 7. El demo de 5 minutos que cierra

**Audiencia:** Secretario General + Jefe de Oficina TIC de un municipio categoría 2–3 o una gobernación. **Regla:** no se menciona la palabra "CMS" ni una vez.

- **0:00 — El espejo.** Abrir el ITA de **su** entidad en el portal de la Procuraduría y su propio sitio al lado. Señalar dos ítems del menú de Transparencia que no cumplen. No se dice nada más. Cuarenta segundos de silencio incómodo hacen más que cualquier lámina.
- **0:40 — El trámite nuevo, en vivo.** Desde el backoffice, crear el trámite que ellos mismos nombren en el momento. Llenar nombre, requisitos, normativa, tasa, días estimados, y armar el formulario arrastrando dos secciones y cinco campos. **Publicar.** Refrescar el portal ciudadano: el trámite está en el catálogo, con su ficha y su formulario. **Cronometrarlo en voz alta.** Este es el momento del demo.
- **2:00 — El ciudadano.** Iniciar sesión, diligenciar, adjuntar un PDF, radicar. Sale el número de radicado. Mostrar la carpeta: estado, hitos, correspondencia.
- **3:00 — El funcionario.** Cambiar de cara. La cola, **ordenada por lo que vence primero**, con los vencidos en rojo. Abrir el expediente, pedir subsanación. Volver a la cara del ciudadano: el estado cambió y llegó la comunicación.
- **4:00 — El control interno.** Abrir la bitácora del expediente: quién radicó, quién decidió, cuándo, con qué nota. Exportar.
- **4:30 — El cierre.** *"Todo lo que vieron después del minuto dos son datos. Lo del minuto uno —el trámite nuevo— lo hizo una persona de su equipo, sin nosotros, sin un contrato de desarrollo y sin esperar una ventana de despliegue. Cuando salga el decreto que cambia los requisitos de ese trámite, así se cambia."*

Si en el minuto 1 se ve que crear el trámite exige tocar código, el demo no existe. Por eso el ítem "empaquetar la autoría de trámites" es prioridad 2 y no 3.

---

## 8. El riesgo que mata

**El requisito habilitante de experiencia.** Este, y no la tecnología, es el que mata. La contratación pública colombiana se adjudica sobre **experiencia acreditada en contratos de objeto y cuantía similares** — un proveedor sin contratos previos con entidades públicas no pasa la verificación de requisitos habilitantes, por mejor que sea el producto. Es un problema circular y no se resuelve con features: se resuelve con **una primera entidad ancla** (una descentralizada pequeña, una ESE, una empresa de servicios públicos), o entrando como **subcontratista de un integrador que ya tenga la experiencia**, o por **unión temporal**. Cualquier plan de este dominio que no empiece resolviendo esto es un plan de escribir software que nadie va a poder comprar. (INFERIDO en cuanto a la mecánica exacta del pliego; no verifiqué el texto de los manuales de Colombia Compra Eficiente en esta sesión.)

Los otros cuatro, en orden de letalidad:

1. **El portal gratis de MinTIC** ya destruyó el mercado del sitio web institucional. VERIFICADO. Cualquier pricing que dependa de cobrar por "la página" está muerto antes de nacer.
2. **El ciclo político.** El alcalde dura cuatro años, el plan de desarrollo manda, y hay ventanas de restricción de contratación en periodo preelectoral. El ciclo de venta se mide en **presupuestos anuales**, no en trimestres, y el campeón interno se va con la administración. (INFERIDO.)
3. **El incumbente es dueño del dato.** El trámite útil de verdad —paz y salvo de predial, certificado de estratificación, estado de cuenta de ICA— necesita leer la base del ERP de Sysman, Neptuno o V6. Ese proveedor lleva 30 años, tiene relación con Hacienda, y no tiene ningún incentivo para abrir una API. Sin ese dato, el catálogo de trámites es de trámites informativos, que es exactamente lo que ya hace el portal gratis.
4. **El 88 % del universo no tiene dinero.** Los municipios de categoría 6 son la mayoría de la lista y la minoría del mercado. Confundir "1.100 municipios" con "mercado direccionable" es el error de tamaño más caro que se puede cometer acá.

---

## 9. Confianza

**VERIFICADAS con URL: 24.** El portal gratuito de MinTIC y sus 360 portales activos; los 3.349 entes territoriales con sede electrónica (939 alcaldías); WCAG 2.1 AA obligatorio desde 2022 por la Res. 1519/2020 y sus sujetos obligados; el menú de Transparencia del Anexo 2 y el ITA de la Procuraduría; los términos de 15 y 10 días de la Ley 1755/2015; la definición de sede electrónica, sede compartida, expediente electrónico y notificación electrónica de la Ley 2080/2021; el alcance territorial obligatorio y el SUIT de la Ley 2052/2020; los servicios ciudadanos digitales del Decreto 620/2020; el Acuerdo 003/2015 del AGN; el 87,9 % de municipios en categoría 6; PSE/ACH como canal de recaudo municipal; los sitios y el posicionamiento de Sysman, Orfeo, Orfeo NG, Orfeo Express/Argo, Mercurio y SAIA; y **diez contratos individuales de SECOP con entidad, objeto, valor y fecha** (dataset `jbjy-vk9h` de datos.gov.co).

**INFERIDAS y marcadas como tales: 8.** El total de ~1.100 municipios; el plazo de 30 días para consultas; los detalles operativos de la firma digital certificada y del estampado cronológico; el requisito habilitante de experiencia en pliegos públicos; la restricción de contratación en periodo preelectoral; la existencia y no-competencia de Granicus/CivicPlus/Accela/Tyler/1Doc/Betha en Colombia; la ausencia real de 2FA en `Api.Identity` (leí los endpoints, no descarto que viva en otro sitio); la afirmación de "10 niveles / 225 de 239 ítems" del Anexo 2, que leí en el resumen de un PDF de auditoría de la ANI y no en el anexo original — el PDF oficial no me dio texto extraíble.

**Qué no pude averiguar:**

1. **Precio de mercado de una solución de trámites/PQRSD para alcaldía.** Los contratos que encontré empaquetan el ERP completo (COP 40–546 M) o son personas (COP 9–28 M). No hay una línea limpia de "módulo de trámites" que permita fijar precio. Habría que consultar SECOP II por objeto específico, entidad por entidad.
2. **El texto exacto de los 10 niveles del Anexo 2.** El PDF de MinTIC no rindió texto y no hay una versión HTML citable. Antes de construir el `schema-del-cms` hay que sacar ese anexo literal — cada ítem es un DocType o una propiedad.
3. **Si Mi Colombia Digital sigue activo y con qué cobertura hoy (2026).** Los datos que encontré son de la etapa de migración a GOV.CO/Territorial; no confirmé el estado actual del programa ni si MinTIC lo dejó de sostener. **Esta es la incógnita más importante del informe**: si el portal gratuito se desfinanció, se abre exactamente el hueco que el CMS podría llenar, y la conclusión de §6 cambia de "extra" a "argumento".
4. **Cuántas entidades tienen realmente trámites transaccionales en línea** (no solo informativos) registrados en el SUIT. Ese número es el tamaño real del mercado de este backlog, y no lo tengo.
5. **El costo y el tiempo de integrarse a la Autenticación Digital del Estado.** Sin eso, la estimación "L" de esa fila del backlog es un dedo mojado al viento.