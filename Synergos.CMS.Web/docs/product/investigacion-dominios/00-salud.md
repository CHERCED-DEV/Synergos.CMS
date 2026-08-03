## 1. El comprador

**No es "el sector salud". Son tres compradores distintos y solo uno es alcanzable.**

| Segmento | Tamaño | Quién firma | Dolor que YA paga | Gasto |
|---|---|---|---|---|
| **A. IPS pequeña/mediana** (1–5 sedes, baja/mediana complejidad) | **10.839 IPS en REPS a jun-2025**; 6.996 de mediana complejidad (65%), 2.979 de baja (27%) — VERIFICADO ([consultorsalud/UNIPS](https://consultorsalud.com/unips-primer-semestre-de-2025-cerraron-332-ips/)) | Gerente/dueño, a veces el contador. No hay CIO. | **Glosas y cartera.** La factura rechazada por la EPS es el problema que despierta al gerente, no la UX — VERIFICADO ([PRAVICE](https://recaudocarteraipsyeps.com/Servicios/recuperacion-cartera-eps)) | COP 150.000–450.000/mes por sede (Medifolios) — VERIFICADO ([medifolios.net](https://medifolios.net/articulos/software-medico-colombia.php)) |
| **B. Profesional independiente / consultorio** (odontología, especialista) | REPS reportaba **59.092 prestadores, 74.444 sedes, 226.887 servicios** a ene-2025 — VERIFICADO en fuente secundaria (agregado de búsqueda sobre [REPS/SISPRO](https://www.sispro.gov.co/central-prestadores-de-servicios/Pages/REPS-Registro-especial-de-prestadores-de-servicios-de-salud.aspx); no pude abrir el corte oficial). La diferencia contra las 10.839 IPS son mayoritariamente profesionales independientes | El propio médico/odontólogo | Agenda, no-show, y ahora **cumplir el RDA sin contratar a nadie** | COP 89.000–379.000/profesional/mes — VERIFICADO ([Saludtools](https://www.saludtools.com/precios), [Doctoralia Pro CO](https://pro.doctoralia.co/precios/para-especialistas)) |
| **C. Clínica/hospital de alta complejidad** | 846 IPS de alta complejidad (8%) — VERIFICADO (misma fuente UNIPS) | Comité, junta, CIO | Integración HIS↔ERP↔laboratorio↔imágenes | Contratos anuales de seis y siete cifras COP — INFERIDO (los HIS grandes no publican precio) |

**El comprador realista para Synergos es A, y de forma marginal B.** C está fuera: Hosvital-HIS y los HIS instalados llevan 20 años, tienen módulo de farmacia, quirófanos y camas, y el costo de reemplazo es un proyecto de años.

**Y hay un cuarto comprador que nadie está mirando y que sí encaja con lo construido: el software house / integrador** que ya vendió un HIS y ahora tiene que producir RDA, RIPS JSON y prescripción interoperable, y no quiere reescribir su motor. Ese compra *capacidades*, no un CMS. INFERIDO, pero es la lectura honesta del árbol de 20 APIs.

**Contexto que aprieta el bolsillo del comprador A:** en el primer semestre de 2025 **cerraron 332 IPS y más de 6.000 servicios** — VERIFICADO ([El Tiempo](https://www.eltiempo.com/salud/la-crisis-silenciosa-en-la-red-hospitalaria-en-el-primer-semestre-de-2025-las-ips-del-pais-estan-cerrando-servicios-al-mismo-ritmo-que-en-todo-2024-3497727)). El comprador A no tiene caja. Vende barato o no vende.

---

## 2. La competencia

| Competidor real | Qué hace bien | El hueco que podríamos llenar | Fuente |
|---|---|---|---|
| **Medifolios** (CO) | 900+ IPS activas, 40+ módulos, **RIPS automáticos + FEV DIAN nativos**. Precio publicado y agresivo (desde COP 150k/mes) | Es un monolito vertical. No expone su motor a terceros ni se compone con otros sistemas. El hueco es *composabilidad*, que ninguna IPS pide | [medifolios.net](https://medifolios.net/) |
| **Saludtools** (CO) | 7.000+ médicos, telemedicina nativa, IA de notas clínicas y transcripción de laboratorio, portal del paciente. Precio por usuario publicado | Orientado al profesional/clínica pequeña; poco fuerte en flujos institucionales (auditoría de cuentas, contratación con EPS) | [saludtools.com/precios](https://www.saludtools.com/precios) |
| **Hosvital-HIS / Digital Ware** (CO) | HIS hospitalario completo: urgencias, hospitalización, farmacia, quirófanos, camas, laboratorio, imágenes | Inalcanzable en su terreno. No es un hueco: es un muro | [digitalware.com.co](https://www.digitalware.com.co/software-his/) |
| **Doctoralia / Docplanner** (global, opera CO) | **Demanda**, no software: el paciente lo busca a él. Agenda + recordatorios WhatsApp + web profesional por COP 25.000/mes | No es EHR institucional ni resuelve RIPS/RDA. Pero se lleva al comprador B con un argumento que Synergos no puede dar: *te traigo pacientes* | [pro.doctoralia.co](https://pro.doctoralia.co/precios/para-especialistas) |
| **Dentalink / Rydent** (odontología) | Vertical odontológico con odontograma, presupuestos, imágenes. Dentalink ya publica su guía de Resolución 1888 | El odontograma es dominio profundo; entrar ahí sin él es no entrar | [softwaredentalink.com](https://www.softwaredentalink.com/blog/resolucion-1888-colombia), [rydent.com](https://rydent.com/f/historia-cl%C3%ADnica-electr%C3%B3nica-en-colombia) |
| **Whatoko / "bus de interoperabilidad"**, ITACA, K2B Health | **La categoría nueva y el competidor más relevante para nosotros**: middleware que captura, normaliza y transmite a la IHCE *sin reemplazar el HIS* | Es exactamente la forma que Synergos podría tomar. Ya hay ocupantes, con producto y con clientes | [busdeinteroperabilidad.com](https://busdeinteroperabilidad.com/), [k2bhealth.com](https://k2bhealth.com/blog/post/resolucion-1888-interoperabilidad-clinica-colombia/) |
| **Clinera** (regional) | Capa de IA conversacional sobre el HIS. Precio USD 279–479/mes + USD 750 setup | Admite en su propia comparativa que "para RIPS/DIAN el sistema autoritativo sigue siendo Medifolios" — o sea, es complemento, no reemplazo | [clinera.io](https://www.clinera.io/comparativas/medifolios) (página del propio vendedor: sesgada) |

**Nadie compite vendiendo "CMS + salud".** No encontré un solo competidor cuyo argumento sea la libertad editorial. Eso no es un océano azul: es una señal de que el comprador no lo pide.

---

## 3. Lo que la ley obliga

| Exigencia | Jurisdicción | Traducida a capacidad / feature | Fuente |
|---|---|---|---|
| **RDA obligatorio.** Cada atención genera un documento HL7 **FHIR R4** y se transmite a la IHCE. Transición de 6 meses desde 15-oct-2025; **obligatorio desde el 15-abr-2026** (ya venció) | CO — Res. 1888 de 2025 | `capacidad-nueva`: emisor FHIR R4 con los 47 perfiles, 25 extensiones, 80 CodeSystems y 82 ValueSets de la guía colombiana. Cuatro tipos de RDA (evento, hospitalización, consulta externa, urgencias) | [Res. 1888 (PDF MinSalud)](https://www.minsalud.gov.co/Normatividad_Nuevo/Resolucion%20No%201888%20de%202025.pdf), [Guía RDA v1.0.0](https://vulcano.ihcecol.gov.co/), [consultorsalud](https://consultorsalud.com/colombia-resumen-digital-de-atencion-en-salud/) |
| **Credenciales y certificación ante MinSalud.** Registro en **Hércules** (hercules.sispro.gov.co), delegado administrativo, ClientID/ClientSecret, sandbox, y **>95% de transmisiones exitosas** antes de habilitar producción. TLS 1.2/1.3 | CO — Manual de Gestión de Credenciales IHCE | `integracion-externa` **con gate humano**: no es un API key que se pide por formulario. Es un proceso de 8–15 semanas | [Manual de operaciones IHCE (MinSalud)](https://www.minsalud.gov.co/ihce/Manuales/Manual_de_operaciones_Historia_Clinica_Interoperable_IHCE_V1.pdf); detalle operativo en [softwaremedico.com.co](https://softwaremedico.com.co/interoperabilidad-en-salud-conecte-su-ips-ya/) (fuente secundaria, blog de proveedor) |
| **Catálogos terminológicos obligatorios**: CIE-10/CIE-11 (diagnóstico), CUPS (procedimiento), **IUM** (medicamento) | CO — Res. 866 de 2021 | `capacidad-nueva` o extensión: servidor de terminologías con versión y vigencia. No es `Api.Catalog` (eso es índice de publicables) | [Res. 866 de 2021](https://vlex.com.co/vid/resolucion-numero-0000866-2021-870405675), [Documento Maestro IHCE](https://www.minsalud.gov.co/ihce/Manuales/Documento_Maestro_IHCE.pdf) |
| **Prescripción UPC ambulatoria migra de MIPRES al RDA** con `MedicationRequest` FHIR + IUM. MIPRES queda solo para No-UPC. Excluye antirretrovirales y control especial | CO — Circular Externa 019 de 2026 (29-may-2026), deroga la Circular 044 de 2025 | `feature-del-bff` + emisor FHIR: el flujo receta → direccionamiento → dispensación cruza IHCE | [Circular 044 de 2025 (PDF MinSalud)](https://www.minsalud.gov.co/Normatividad_Nuevo/Circular%20Externa%20No%20044%20de%202025.pdf); la Circular 019/2026 la conozco por [fuente secundaria](https://softwaremedico.com.co/circular-19-de-2026/) — **no pude abrir el PDF oficial** |
| **RIPS en JSON validados en el MUV, con CUV, como soporte de la Factura Electrónica de Venta.** Radicación en 22 días hábiles | CO — Res. 2275 de 2023 | `integracion-externa` (MUV + proveedor tecnológico DIAN) + `feature-del-bff`. **Es el flujo que le paga a la IPS** | [Res. 2275 (PDF MinSalud)](https://www.minsalud.gov.co/Normatividad_Nuevo/Resoluci%C3%B3n%20No%202275%20de%202023.pdf), [Salud Total](https://saludtotal.com.co/plan-de-beneficios-en-salud/normas-de-interes-facturacion-electronica-y-rips-resolucion-2275-y-2284-de-2023/) |
| **Retención de la historia clínica: 15 años** desde la última atención — 5 en archivo de gestión, 10 en archivo central. Se duplica para víctimas de DDHH; permanente si entra en proceso de lesa humanidad | CO — Res. 839 de 2017 (modifica Res. 1995 de 1999) | `capacidad-nueva` o extensión de `Api.Documents`: calendario de retención con dos fases, disposición final auditable y excepciones por marca legal | [Res. 839 (PDF MinSalud)](https://www.minsalud.gov.co/Normatividad_Nuevo/Resolucion%20No%20839%20de%202017.pdf), [consultorsalud](https://consultorsalud.com/tiempo-de-retencion-y-conservacion-de-la-historia-clinica-resolucion-839-de-2017/) |
| **Habilitación**: estándar de historia clínica y registros verificable en visita | CO — Res. 3100 de 2019 (vigente) | Trazabilidad de acceso y de modificación, exportable para el verificador | [Res. 3100 (SUIN-Juriscol)](https://www.suin-juriscol.gov.co/viewDocument.asp?ruta=Resolucion/30039964) |
| **Telemedicina**: la plataforma debe cumplir HL7 y DICOM, autenticación, cifrado y gestión de accesos; el servicio se habilita en el SUH | CO — Res. 2654 de 2019 | `integracion-externa` (video) + el mismo guard de PHI | [Res. 2654 (SafetYA)](https://safetya.co/normatividad/resolucion-2654-de-2019/), [Marco reglamentario MinSalud](https://www.minsalud.gov.co/sites/rid/Lists/BibliotecaDigital/RIDE/DE/OT/nuevo-marco-reglamentario-para-la-telesalud-en-colombia-18122019.pdf) |
| **Datos de salud = dato sensible**; tratamiento requiere autorización previa, expresa e informada | CO — Ley 1581 de 2012 | `Api.Consent` — ya construida | [Ley 1581 vía consultorsalud](https://consultorsalud.com/colombia-resumen-digital-de-atencion-en-salud/) |

**HIPAA no aplica** salvo que se atienda a pacientes o pagadores de EE.UU. **HL7 FHIR R4 sí aplica, y no como buena práctica: como obligación colombiana desde abril de 2026.** Esa es la diferencia que cambia todo el análisis.

---

## 4. Ajuste contra lo construido

### Ya cubierto

- **`Synergos.Api.Consent`** — leí `Domain/ConsentRules.cs`, `Domain/ConsentGrant.cs`, `Domain/ConsentService.cs`, `Endpoints/ConsentEndpoints.cs`. Es de las piezas mejor pensadas del árbol para este dominio: consentimiento **por propósito** (rechaza `bad_purpose`), exige **`policyVersion`** (rechaza `policy_version_required` — "sin la versión del texto, 'dio consentimiento' no dice a QUÉ"), distingue los tres "no" (`not_granted` / `revoked` / `expired`), revocar **marca y no borra**, y `POST /v1/grants/forget` revoca todo sin destruir la prueba. `check` es POST *a propósito*, para que sujeto+propósito no queden en logs de proxy. Eso es Ley 1581 implementada, no citada.
- **`Synergos.Api.Audit`** — leí `Domain/AuditRules.cs`, `Domain/AuditEntry.cs`, `Endpoints/AuditEndpoints.cs`. Append-only real: no existe `MapPut`/`MapPatch`/`MapDelete` y el xmldoc dice por qué. Rechaza entradas sin actor (`actor_required`), sin target, y **rechaza consultas sin filtro** (`filter_required`, "sin filtro esto es un volcado de la bitácora"). El reloj lo pone el servicio, no el origen. Es lo que un verificador de Res. 3100 quiere ver.
- **`Synergos.Api.Booking`** — leí `Domain/BookingRules.cs`. Holds con TTL, horarios de apertura por recurso con huso horario, y el rechazo `crosses_midnight` con la explicación de por qué una agenda clínica y una noche de hotel no son lo mismo. Agenda de citas: resuelto.
- **`Synergos.Api.Documents`** — leí `Domain/DocumentRules.cs` y `Endpoints/DocumentEndpoints.cs`. Lista **blanca** de content-types, tope de 10 MB, enlace firmado HMAC con el vencimiento **dentro** de lo firmado, y verificación de firma *antes* que de vencimiento para no confirmar existencia. Resultados y adjuntos: resuelto a nivel de entrega.
- **`Synergos.Bff.Salud`** — leí `Domain/AppointmentFlow.cs` completo. El orden es correcto y está justificado: consentimiento → hold → cotizar → **autorizar** (no capturar) → [confirmar] capturar → confirmar cupo. Y el detalle que casi nadie hace bien: al capturar, la compensación **muta** de `VoidPayment` a `RefundPayment`, porque liberar una autorización ya capturada la rechazaría el proveedor y la compensación quedaría colgada para siempre. Eso es ingeniería real.
- **Guard de PHI en el CMS** — leí `Synergos.CMS.Web/Services/DefaultPhiAccessGuard.cs`. Audita **siempre** y es **fail-closed**: si la auditoría no se puede escribir, deniega aunque la política permita (`audit-unavailable`). La rama de auto-acceso del paciente —que el inventario `docs/product/inventario/verticales-regulados.md` documentaba como *muerta*— hoy está viva y resuelta **en el guard**, no en el controller, y acotada a `action == "read"` para que un paciente no pueda reescribir su propia historia. Ese arreglo está hecho.

### Parcial

| Capacidad | Qué le falta exactamente |
|---|---|
| `Api.Payments` | Registra `LoggingPaymentProvider`: **no cobra**. Además, `investigacion-pagos/03-necesidades-por-vertical.md` documenta que el consumidor de Salud del CMS (`StubClinicalSchedulingService`) es *el peor manejo de fallo de los ocho*: confirma la reserva incondicionalmente aunque `CaptureAsync` falle, y deja la cita en `"pending"` con el turno ya tomado. El `Bff.Salud` lo hace bien; el CMS lo hace mal. Son dos caminos distintos al mismo dominio. |
| `Api.Notifications` | `LoggingNotificationSender`: **no manda nada**. El recordatorio de cita —el feature que el comprador B compra— no existe en el borde. Además el tope es 20 envíos/hora/destinatario, razonable pero hay que dimensionarlo. |
| `Api.Documents` | Sin motor de retención. Busqué `Retention` en `Domain/DocumentService.cs`: no aparece. Hay `POST /{id}/delete`, pero no calendario de 15 años, ni las dos fases (gestión 5 / central 10), ni marca legal que suspenda el borrado. Res. 839 no está modelada. |
| `Api.Audit` | El doc 07 dice que "posee bitácora append-only **y retención**". Las reglas solo validan escritura y filtro de consulta; no encontré política de retención ni exportación sellada para entregar a un verificador. |
| `Api.Signing` | Es HMAC-SHA256 con llave por propósito (leí `Domain/SigningRules.cs` y `Domain/SigningKey.cs` — el diseño de rotación es correcto: retirar deja de emitir pero sigue verificando). Pero una receta o certificado médico con validez probatoria en Colombia necesita firma electrónica/digital respaldada, no un MAC compartido. Sirve para el ticket de un evento; no para lo que firma un médico. |
| `Api.Identity` | Leí `Domain/IdentityRules.cs`: PBKDF2-SHA256 con 210.000 iteraciones, lockout a 5 intentos, comparación de tiempo constante. Sólido. Lo que falta es lo del dominio: no valida contra **ReTHUS** que quien prescribe esté registrado, ni federa con **Mi Seguridad Social / Hércules**. |
| Schema del CMS | `uSync/v9/ContentTypes/elementsynehr.config` es **un** ElementType con `heading`, `subheading`, `apiBase` y `config` (JSON). Es un contenedor para el bundle Angular, no schema clínico. El editor no puede componer nada de salud. |

### Falta

| Necesidad | Por qué ninguna existente la cubre | Tipo |
|---|---|---|
| **Emisor RDA / FHIR R4** (Composition + Bundle, 47 perfiles colombianos, 4 tipos de RDA) | Grepeé `fhir|hl7|cie-?10|cups|IUM|snomed|dicom` en todo `Synergos.Api.*` y `Synergos.Bff.*`: **cero coincidencias reales**. No hay nada que traducir; hay que construirlo. Y no cabe en ninguna capacidad existente: tiene almacén propio (mapeos, versiones de perfil, cola de reintentos) y puede decir NO sola (bundle no conforme) | `capacidad-nueva` (p.ej. `Api.ClinicalExchange`) |
| **Conexión a la IHCE**: Hércules, ClientID/ClientSecret, MPI/identificador VIDA, sandbox, certificación >95% | Proceso administrativo + técnico con gate humano de MinSalud. Ninguna capacidad puede fabricarlo | `integracion-externa` |
| **Servidor de terminologías** CIE-10/CIE-11, CUPS, IUM, ReTHUS con vigencias | `Api.Catalog` es "índice de lo publicable, búsqueda y facetas" (doc 07). Una terminología no se publica: se versiona, se vence y se mapea. Almacén distinto, "no" distinto | `capacidad-nueva` |
| **RIPS JSON + MUV + CUV + FEV DIAN** | `Api.Orders` tiene pedido/líneas/ciclo de vida; `Api.Payments` tiene captura. Ninguna sabe qué es un CUV ni cómo se arma un RIPS por tipo de atención. Y el proveedor tecnológico DIAN es un tercero | `integracion-externa` + `feature-del-bff` |
| **Prescripción UPC vía RDA** (`MedicationRequest`) y MIPRES para No-UPC | No existe ningún concepto de medicamento en el árbol | `feature-del-bff` sobre la capacidad nueva de arriba |
| **Retención 15 años con disposición final** | Ver "Parcial": `Api.Documents` no lo modela | extensión de capacidad (no capacidad nueva: es el mismo almacén) |
| **Telemedicina** (video, grabación, consentimiento del acto) | Nada en el árbol. `Api.Messaging` es texto e hilos (leí `Domain/MessagingRules.cs`) | `integracion-externa` |
| **Schema clínico editorial en el CMS** | Un solo `elementSynEhr` genérico | `schema-del-cms` |

---

## 5. Backlog priorizado

| Feature | Por qué vende (y por qué es verdad) | Esfuerzo | Depende de | Prioridad |
|---|---|---|---|---|
| **Emisor RDA FHIR R4 + conexión IHCE certificada** | *"Su IPS transmite el RDA hoy y pasa la certificación del Ministerio."* Verdad: es obligación vencida desde el 15-abr-2026, no una mejora | **XL** | Terminologías; credenciales Hércules del cliente | **1** |
| **Terminologías CIE-10/CIE-11 + CUPS + IUM versionadas** | *"Su diagnóstico y su medicamento salen codificados como los exige la 866."* Verdad: sin IUM el `MedicationRequest` no valida | M | — | **1** |
| **RIPS JSON validados (MUV/CUV) + FEV** | *"Radica en 22 días con RIPS validado y deja de perder plata en glosas."* Verdad: es lo único de esta lista que **le entra plata** al comprador, y es lo que Medifolios ya tiene | L | Proveedor tecnológico DIAN | **1** |
| **Retención 15 años + disposición final auditable** | *"El verificador le pide el expediente de hace once años y usted lo entrega."* Verdad: Res. 839 lo exige y hoy `Api.Documents` no lo modela | M | `Api.Documents` | **1** |
| **Prescripción UPC vía RDA + MIPRES No-UPC** | *"Su médico prescribe una vez, no dos."* Verdad: la Circular 019/2026 nació justo por la queja de la doble digitación | L | Emisor RDA; terminologías | 2 |
| **PSP real para copago (PSE/tarjeta)** | *"El copago se cobra de verdad."* Verdad: hoy es `LoggingPaymentProvider`. Ya hay investigación previa en `docs/product/investigacion-pagos/01-psp-colombia.md` — construir encima | M | `Api.Payments` | 2 |
| **Notificaciones reales (WhatsApp + SMS + correo)** | *"El no-show baja."* Verdad: es el argumento con el que Doctoralia cobra COP 229.000/profesional/mes | S–M | `Api.Notifications` | 2 |
| **Firma con validez probatoria para receta y certificado** | *"La incapacidad que firma su médico es oponible."* Verdad: HMAC no lo es | M | `Api.Signing` + entidad de certificación | 2 |
| **Consentimiento informado como contenido editorial versionado** | *"La abogada cambia el texto del consentimiento y el sistema registra quién aceptó cuál versión, sin desplegar."* Verdad: `ConsentRules` **ya exige** `policyVersion`; el CMS ya versiona contenido. Es el único puente CMS↔salud que no es cosmético | **S** | `Api.Consent` + `schema-del-cms` | 3 |
| **Portal del paciente editorial** (secciones, textos, marca por sede) | *"Cada sede tiene su portal sin pedirle nada a TI."* Verdad a medias: existe, pero nadie cambia de EHR por esto | S | Guard de PHI (ya está) | 3 |
| **Telemedicina** | *"Habilite el servicio de telemedicina."* Verdad, pero Saludtools ya la trae nativa | L | Video de terceros | 3 |

**Prioridad 1 son cuatro cosas, y las cuatro son de cumplimiento.** Ninguna es el CMS. Si esa lista no está completa, no hay conversación con una IPS — no hay descuento, ni piloto, ni "empecemos por lo demás". El RDA no es un diferenciador: es el precio de entrada, y el plazo ya venció.

---

## 6. El ángulo CMS — sin ser amable

**Qué puede cambiar el editor hoy sin tocar código:** el sitio público de la IPS (páginas, secciones, CTAs, navegación, marca y tema por hostname, es-CO/en-US) y los cuatro campos de `elementSynEhr` (`heading`, `subheading`, `apiBase`, `config`). Es decir: **la vitrina, y un título encima de un dashboard**. El dashboard clínico en sí lo pinta un bundle Angular; el editor no compone nada clínico.

**Por qué importa acá — el caso honesto y pequeño:** hay exactamente **dos** cosas del dominio que son legítimamente contenido y no código:

1. **El texto del consentimiento informado.** Cambia por servicio, por procedimiento y por asesoría legal, y `Api.Consent` ya exige `policyVersion` en cada otorgamiento. Que la abogada edite el texto en el backoffice y que el sistema registre quién aceptó qué versión, sin despliegue, es real y es vendible. Es el mejor puente que existe entre las dos mitades del producto.
2. **El portafolio de servicios y el directorio de profesionales.** Contenido puro, hoy hecho a mano en WordPress por casi todas las IPS.

**El veredicto: en Salud, el CMS es un extra — y en la venta a IPS es prácticamente irrelevante.**

La promesa del arquitecto —"robustece la creación de contenido estático porque da libertad, y flexibiliza las interfaces"— no es falsa, es **fuera de tema**. La IPS no compra libertad editorial: compra no cerrar. La historia clínica es lo contrario de la libertad de composición — es un documento cuya estructura la fija la Res. 866 y cuyo formato lo fija FHIR R4. Un editor que "compone libremente" la historia clínica es un editor que produce un RDA no conforme.

Peor: el CMS **agrega superficie de riesgo**. Un producto que mezcla PHI cifrada con un backoffice de autoría tiene que explicarle al auditor de habilitación por qué el editor de contenido corre en el mismo proceso que el guard de PHI. Ese argumento se puede ganar (el guard es fail-closed y audita siempre, lo verifiqué en el código), pero **cuesta reunión** y no da ni un peso de valor.

La parte del producto que sí vende en salud es la que **no** es el CMS: `Api.Consent`, `Api.Audit`, `Api.Booking`, `Api.Documents` y la saga de `Bff.Salud`. Si Synergos entra a salud, entra como **motor de cumplimiento e interoperabilidad**, con el CMS de regalo — no al revés.

---

## 7. El demo de 5 minutos que cierra

**Advertencia previa: hoy este demo no existe, porque le falta el minuto 4.** Lo que sigue es el guion que habría que poder hacer. Sin el conector RDA, el minuto 4 es una diapositiva y la reunión se acaba ahí.

**Comprador en la sala:** gerente de una IPS de mediana complejidad, con la contadora al lado.

- **0:00–0:45 — El susto, en su propio tablero.** No abro una landing. Abro el panel de transmisión IHCE: "atenciones de hoy: 47 · RDA transmitidos: 47 · tasa de éxito 100% · último error hace 6 días, reintentado y resuelto". Pregunto: *"¿usted puede ver esta pantalla hoy?"*. La respuesta es no. Ahí empieza la venta.
- **0:45–2:00 — Una atención de punta a punta.** Agendo una cita (`Api.Booking`, hold con TTL). El sistema **bloquea** antes de tocar la agenda porque no hay consentimiento vigente — muestro el rechazo literal `consent.not_granted`, no un genérico. Firmo el consentimiento; la pantalla dice **qué versión del texto** se aceptó. Registro la atención con diagnóstico CIE-10 y medicamento con IUM.
- **2:00–2:45 — El texto lo cambia la abogada, no el proveedor.** Voy al backoffice, edito el consentimiento informado, publico. Vuelvo: el siguiente paciente ve el texto nuevo y queda con `policyVersion` nueva; el anterior conserva la vieja. *"El día que le pregunten qué autorizó ese paciente en 2024, hay una respuesta."* **Este es el único momento donde el CMS es protagonista, y dura 45 segundos.**
- **2:45–3:45 — La pregunta que hace el verificador.** Consulto la bitácora: quién abrió esa historia, cuándo, desde dónde, con qué resultado. Muestro que **no hay endpoint para borrar ni editar una entrada** — abro el código si hace falta. Y muestro el fail-closed: simulo caída del almacén de auditoría y el acceso a PHI **se deniega**, no se permite en silencio. Eso es Res. 3100 y Res. 839 contestadas con software, no con una política en Word.
- **3:45–4:30 — El RDA sale solo.** Al cerrar la atención, el Bundle FHIR R4 se genera y se transmite; muestro el JSON, el HTTP 200 y el documento en el Visor RDA del Ministerio. Y la prescripción UPC viajando como `MedicationRequest` — *"su médico no vuelve a digitar en dos sistemas"*.
- **4:30–5:00 — La plata.** Del mismo cierre sale el RIPS JSON validado con su CUV y la FEV. Cierro con la única frase que le importa a la contadora: *"lo que acaba de ver es la misma atención generando, a la vez, el RDA que exige el Ministerio y la factura que le paga la EPS. Una sola digitación, dos obligaciones."*

Nunca digo "CMS", "bloques" ni "Layout Composer". Si el gerente pregunta por la página web, contesto en diez segundos y vuelvo al tablero.

---

## 8. El riesgo que mata

**Cuatro, y los dos primeros bastan para matar el dominio.**

1. **Llegamos tarde a una fecha que ya pasó.** El RDA es obligatorio desde el **15 de abril de 2026**; hoy es 3 de agosto de 2026. Medifolios, Saludtools, Dentalink y Siesa publicaron sus guías de cumplimiento *antes* del plazo y ya movieron a sus bases. El proceso de conexión toma **8–15 semanas** entre Hércules, sandbox y certificación >95%. Entrar hoy desde cero, con **cero líneas de FHIR en el repo**, significa no tener producto vendible antes de finales de 2026 — cuando el mercado ya resolvió el problema. Y una vez que una IPS certificó con un proveedor, el incentivo para repetir el vía crucis es nulo.

2. **El costo de cambio de una historia clínica es brutal y el comprador no tiene caja.** Migrar datos clínicos de 15 años, recapacitar al personal, re-certificar ante el Ministerio y arriesgar la habilitación — todo eso contra un comprador del que **cerraron 332 instituciones en seis meses**. La IPS que sobrevive no cambia de EHR: aguanta. El ciclo de venta institucional en salud colombiana es de 6 a 18 meses (INFERIDO) para un ticket de COP 150.000–450.000/mes. La aritmética de adquisición no cierra.

3. **El diferencial declarado no le habla a este comprador.** "Libertad de composición" es exactamente lo que la Res. 866 y FHIR R4 le quitan a la historia clínica. Vender flexibilidad donde la ley exige rigidez no es un mal pitch: es un pitch que genera desconfianza.

4. **El riesgo de reputación asimétrico.** Un bug en el carrito de una tienda cuesta una venta. Una fuga de PHI cuesta la empresa: dato sensible bajo Ley 1581, con la SIC encima. Y el propio inventario del repo advierte que el almacén cifrado es *"cifrado at-rest baseline, NO HIPAA-grade"* y **single-instance, sin lock distribuido**. Escalar eso a varias IPS reales es un proyecto de infraestructura que nadie ha empezado.

**La salida, si se quiere insistir:** no vender el EHR. Vender el **conector RDA/RIPS como producto independiente** al comprador D — los software houses y las IPS con HIS legado que no pueden migrar pero sí tienen que transmitir. Ese es el hueco que Whatoko, ITACA y K2B ya están ocupando, es donde el árbol de capacidades agnósticas de verdad rinde, y es la única entrada a este dominio donde llegar en 2026 no es llegar tarde. Ahí el CMS no aparece en la conversación — y está bien.

---

## 9. Confianza

**Afirmaciones VERIFICADAS: 27.** Todas con URL en el cuerpo del informe. Se reparten así: 10 sobre normativa colombiana (Ley 2015/2020, Res. 866/2021, Res. 1888/2025, Res. 2275/2023, Res. 839/2017, Res. 3100/2019, Res. 2654/2019, Ley 1581/2012, Circular 044/2025, guía RDA v1.0.0); 6 sobre precios de competidores (Saludtools, Medifolios, Clinera, Doctoralia — todas de páginas de los propios vendedores, por tanto sesgadas al alza en funcionalidad y a la baja en precio de entrada); 3 sobre tamaño de mercado (10.839 IPS, distribución por complejidad, 332 cierres en 1S-2025); 8 sobre el código del repo, leídas fichero por fichero.

**Afirmaciones INFERIDAS: 9.** Las marcadas explícitamente en el cuerpo — precio de los HIS de alta complejidad, duración del ciclo de venta institucional, la existencia y el atractivo del "comprador D" (software house), la lectura de que el CMS agrega superficie de riesgo regulatorio, y el juicio de que ningún competidor vende el ángulo editorial.

**Verificación de código (lo más sólido del informe).** Leí completos: `Synergos.Api.Consent/Domain/{ConsentRules,ConsentGrant,ConsentService}.cs` y `Endpoints/ConsentEndpoints.cs`; `Synergos.Api.Audit/Domain/{AuditRules,AuditEntry}.cs` y `Endpoints/AuditEndpoints.cs`; `Synergos.Api.Documents/Domain/DocumentRules.cs` y `Endpoints/DocumentEndpoints.cs`; `Synergos.Api.Signing/Domain/{SigningRules,SigningKey}.cs` y `Endpoints/SigningEndpoints.cs`; `Synergos.Api.Booking/Domain/BookingRules.cs` (parcial); `Synergos.Api.Workflow/Domain/WorkflowRules.cs`; `Synergos.Api.Messaging/Domain/MessagingRules.cs`; `Synergos.Api.Notifications/Domain/NotificationRules.cs` (parcial); `Synergos.Api.Identity/Domain/IdentityRules.cs` (parcial); `Synergos.Bff.Salud/Domain/AppointmentFlow.cs`, `Contracts/SaludContracts.cs`, `Endpoints/SaludEndpoints.cs`; `Synergos.CMS.Web/Services/DefaultPhiAccessGuard.cs`; `Synergos.CMS.Web/uSync/v9/ContentTypes/elementsynehr.config`. Y los docs `08-despiece-apis.md`, `07-diseno-atomico-capacidades.md`, `inventario/verticales-regulados.md`, `investigacion-pagos/03-necesidades-por-vertical.md`.

**Lo que NO pude averiguar:**

- **El texto oficial de la Resolución 1888 de 2025.** El PDF de MinSalud es un escaneo del que no pude extraer texto. Los plazos (15-oct-2025 → 15-abr-2026), los cuatro tipos de RDA y los requisitos de cifrado los tomé de fuentes secundarias concordantes (ConsultorSalud, Siesa, la guía FHIR oficial de vulcano.ihcecol.gov.co). **Antes de comprometer una fecha en una propuesta comercial, hay que leer el articulado.**
- **La Circular Externa 019 de 2026.** Solo la conozco por un blog de proveedor. Sí abrí la Circular 044 de 2025 que dice derogar. La migración MIPRES→RDA es la afirmación más consecuente del informe y la peor sostenida: **verificarla en el Diario Oficial antes de construir sobre ella**.
- **Sanciones concretas** por no transmitir el RDA. No encontré el régimen sancionatorio ni un caso aplicado. Sin eso, no sé si el mercado corre por miedo o por inercia — y eso cambia la urgencia de compra.
- **Cuántas IPS ya certificaron ante la IHCE.** No hay listado público que haya encontrado. Es el dato que decidiría si el mercado ya se cerró (riesgo #1) o si todavía hay rezagados masivos — la diferencia entre "no entrar" y "entrar por el conector". **Es el dato más caro que falta.**
- **Precios reales de los HIS grandes** (Hosvital, Servinte) y de los buses de interoperabilidad (Whatoko, ITACA, K2B). Ninguno publica. Sin eso no puedo decir si el conector RDA como producto independiente tiene margen.
- **El corte oficial del REPS** con el desglose por tipo de prestador (IPS vs. profesional independiente vs. transporte especial) y por servicio (odontología, laboratorio). La cifra de 59.092 prestadores viene de un agregado de búsqueda que no pude abrir en su fuente primaria.
- **Si algún competidor ya vende "CMS + EHR".** Busqué y no encontré ninguno. Ausencia de evidencia, no evidencia de ausencia — aunque en este caso apunta en la misma dirección que el resto del análisis.