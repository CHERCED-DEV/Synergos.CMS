# ADR 0142 — La historia clínica no se suprime mientras dure su retención legal: la supresión borra la cuenta y desliga al miembro, la retención es de 15 años desde la última atención y la eliminación es un trámite (enmienda la 0098 §4 y §5)

- **Estado:** **Aceptada (2026-11-05)**. La decidió el director como D-003-I2-5 (`DECISIONES.md`), con la
  recomendación A de la línea 003. Las citas legales las leyeron 002 y 003 el 2026-11-05 (ver el contexto, con lo que
  cada uno leyó y dónde). Queda **una condición abierta**: el aviso al paciente (§4) lo aprueba el arquitecto antes del
  release, como los corchetes de las legales.
- **Fecha:** 2026-11-05
- **Propone:** la línea 003-salud-cita, en I2 (la historia clínica). El hallazgo lo midió en el código
  (`specs/003-salud-cita/bitacora.md`, 2026-11-05).
- **Enmienda, sin borrar nada:** [ADR 0098](0098-healthcare-practice-management-module-phi.md)
  - §4: la retención de los registros clínicos, 6 años;
  - §5: la supresión que de-identifica.
- **Respeta:**
  - ADR 0098 §1–§3: la guardia única que audita y falla cerrada, el almacén cifrado y atómico, y la
    minimización;
  - [ADR 0121](0121-la-auditoria-de-acceso-a-phi-tiene-su-propia-retencion.md): la auditoría clínica, aparte y sin purga por defecto;
  - [ADR 0013](0013-no-automatic-seeders.md): sin sembradores;
  - CLAUDE.md §0.C.21 (superset): el de-identificador y sus nombres se quedan.

## Contexto

> Marcas de certeza:
> - ✔ comprobado en el código, o leído en una publicación oficial al escribir esta ADR;
> - ◐ leído entero en una compilación normativa (la de Colpensiones, de Avance Jurídico), no en el Diario Oficial;
> - ○ sólo en fuentes secundarias.
>
> Código: CMS `ac94ad04` (`linea/salud-cita`).

### Lo que se midió ✔

1. **El de-identificador sólo sirve para la supresión.**
   - `FileSystemHealthcareDataAnonymizer` lo llama únicamente `FileSystemGdprRtbfCoordinator`, antes de borrar
     la cuenta del miembro. No hay otro uso, como exportar sin datos personales.
   - Reescribe sólo la versión VIGENTE (`patients`): pone el nombre en blanco, deja el año de nacimiento y
     desliga el `MemberKey`.
2. **Las versiones archivadas no se tocan.**
   - Cada cambio de la historia archiva la versión anterior entera en `patient-history`: nombre, nacimiento
     exacto, `MemberKey`, notas y, desde I2, el pre-registro.
   - Nadie las de-identifica. Tras una supresión, la identidad sigue en todas, ligada a una cuenta que ya no
     existe y cuya clave pueden nombrar otras capacidades.
3. **La purga es automática y corta.**
   - `HealthcareRetentionPolicy` borra todo `.phi` de `syn-healthcare` sin escribir en
     `Synergos:Healthcare:RecordRetentionDays`.
   - Su valor por defecto es 2190 días (6 años, la cifra de HIPAA), y ninguna configuración lo cambia.
   - Barre también la historia vigente, las citas, las recetas y los consentimientos.
4. **El ADR razonó con otra norma.** La 0098 se escribió con HIPAA (6 años) y con el art. 17 del GDPR
   (de-identificar), y su §5 no nombra las versiones archivadas.

### La norma que aplica es la colombiana

Salud es un consultorio en Colombia: EPS, es-CO y la Ley 1581 de 2012 en la política de tratamiento.

| Norma | Qué dice | Estado |
|---|---|---|
| Resolución 839 de 2017 (MinSalud y MinCultura, 23 de marzo de 2017; Diario Oficial 50.185 del 24 de marzo), art. 3 «Retención y tiempos de conservación documental del expediente de la historia clínica» | conservarla «por un periodo mínimo de quince (15) años, contados a partir de la fecha de la última atención»: los cinco primeros en el archivo de gestión y los diez siguientes en el central. Los términos «se duplicarán» para víctimas de violaciones de DDHH o infracciones graves al DIH, y la conservación es permanente si la historia hace parte de un proceso por lesa humanidad. Cumplidos los términos, y «con miras a propender por la entrega de la historia clínica al usuario», van dos avisos en un diario de amplia circulación nacional, con ocho días de intervalo | ◐ lo leyó 002 en la compilación de Colpensiones; la página no muestra modificación ni derogatoria (última actualización 31-mar-2018). El PDF de minsalud.gov.co es un escaneado que ni 002 ni 003 pudieron leer, y no hay OCR del original |
| Resolución 839 de 2017, art. 4 «Disposición final del expediente de historia clínica» | la eliminación procede «por parte del responsable de su custodia, siempre que concurran» tres condiciones: 4.1, que se haya cumplido el tiempo del art. 3; 4.2, la publicación del art. 3; 4.3, la valoración de si tiene «valor secundario (científico, histórico o cultural)», con un acta firmada por el representante legal (y el revisor fiscal cuando haya lugar) y un inventario. Parágrafo: la que tiene valor secundario se conserva | ◐ como el art. 3 |
| Decreto 1377 de 2013, art. 9 | la supresión y la revocatoria «no procederán cuando el Titular tenga un deber legal o contractual de permanecer en la base de datos» | ✔ lo leyó 003 en el Gestor Normativo de Función Pública (`norma.php?i=53646`). Esa página lo marca «Derogado Parcialmente por el Decreto 1081 de 2015» |
| Decreto 1074 de 2015, cap. 25, sección 2, art. **2.2.2.25.2.6** «Revocatoria de la autorización y/o supresión del dato» | compila el art. 9 del 1377, con la misma frase literal | ◐ lo leyó 002 en la compilación de Colpensiones. El mapa: los arts. 2.2.2.25.2.1 a .2.9 son los arts. 4 a 12 del 1377. La SIC invoca el .2.6 para la revocatoria en una resolución de 2020. La página oficial de Función Pública (`norma.php?i=76608`) se corta antes de ese capítulo |
| Decreto 1074 de 2015, art. 2.2.2.25.2.8 «Limitaciones temporales al tratamiento de los datos personales» (art. 11 del 1377) | pasado el término del tratamiento, «deberán proceder a la supresión de los datos personales en su posesión», salvo que los datos «deberán ser conservados cuando así se requiera para el cumplimiento de una obligación legal o contractual» | ◐ como el anterior |
| Proyecto de resolución de MinSalud «Por la cual se establecen las disposiciones aplicables a la historia clínica y sus modalidades» (julio de 2026) | derogaría la 1995 de 1999 y la 839 de 2017, que quedarían «absorbidas e integradas». Mantiene los 15 años desde la última atención, la duplicación y la permanencia, y **agrega una condición**: no se elimina mientras haya un proceso judicial, disciplinario o administrativo relacionado con la atención, aunque el plazo haya vencido | ○ sigue siendo PROYECTO según lo que encontró 002: consulta pública del 24 de julio al 4 u 8 de agosto de 2026, sin número ni fecha de expedición al 12 de septiembre. El PDF oficial da 404; lo dicen fuentes secundarias (softwaremedico.com.co, Fenalco y Cerlatam) |

**Lo que eso cambia:**
- De-identificar la historia durante su retención va contra el deber de conservarla: deja de servir como
  historia del paciente, y no se le puede entregar ni a él ni a quien la pida con derecho.
- La supresión de los datos de la historia no procede mientras dure ese deber.
- La purga a los 6 años borra antes del mínimo y se salta el trámite del art. 4.

## Decisión

### 1. La supresión no alcanza la historia clínica mientras dure su retención legal

- La supresión borra la cuenta, como antes, y **desliga** al miembro de su historia en la versión vigente y en
  **todas** las archivadas:
  - el `MemberKey` pasa a `Guid.Empty`;
  - las claves del miembro como autor o validador de un dato del resumen, también.
- La historia queda **completa y cifrada**: nombre, nacimiento, pre-registro y notas. Sin cuenta no hay acceso
  propio; lo demás sigue pasando por la guardia, sólo para lo que la ley pide.
- Para reescribir lo archivado, que se guarda con una clave al azar, `IPhiStore` gana
  `ListEntriesAsync`: cada documento con su clave.

### 2. Quince años desde la última atención, configurable pero nunca menor

- `Synergos:Healthcare:RecordRetentionDays` pasa a valer por defecto `HealthcareSettings.RetencionMinimaDias`:
  15 años, 5479 días.
- Un valor menor (el 2190 de antes, un 0 o un negativo) cuenta como el mínimo. `RetencionEfectivaDias` es lo
  que se aplica.
- **«La última atención» todavía no se registra por historia.** La fecha de la última escritura de cada documento
  es una cota inferior: un documento sin escribir en el término es CANDIDATO, y el trámite confirma la fecha antes
  de eliminar nada.

### 3. La eliminación es un trámite, no una purga

- `HealthcareRetentionPolicy` ya no borra documentos clínicos. Cuenta los que pasaron el término y lo avisa en
  el registro, para quien lleva el trámite del art. 4.
- **El trámite, por pasos:**
  1. El término del art. 3 está cumplido, con la fecha de la última atención confirmada.
  2. Van los dos avisos del art. 3, para que el usuario reclame su historia.
  3. La valoración del valor secundario, con su acta firmada y su inventario.
  4. Sólo si las tres concurren, el responsable de la custodia elimina. La historia con valor secundario se
     conserva.
- La auditoría clínica sigue como la dejó la ADR 0121.
- La guía de operación del trámite (quién lo lleva, dónde se publica, el acta) es de 002 y queda pendiente. El
  código no la automatiza: lo que hace es no borrar.

### 4. El paciente lo sabe antes de dar su permiso

- El aviso del permiso de datos de salud (la sección `Health.Consent` del diccionario) gana
  `Health.Consent.Retention`: la historia se conserva el tiempo que la ley obliga aunque se borre la cuenta o se
  pida suprimir los datos.
- Es **borrador**, como el resto de la sección, y lo aprueba el arquitecto antes del release.
- La política de tratamiento de datos (de 002, `/synergos/legal/politica-de-tratamiento-de-datos/`) lo dice en su
  §8 «Seguridad y conservación», justo después de «…después se suprimen.».
  - Lo escribió 002 con el párrafo propuesto por 003: CMS `58c730a3` en `linea/produccion`, spec `fe59b64`, por
    export de uSync.
  - Cita el Decreto 1377 de 2013, art. 9, «compilado en el Decreto 1074 de 2015, art. 2.2.2.25.2.6».
  - Su `[VERIFICAR]` pide comprobar que la Resolución 839 de 2017 siga vigente, o que la norma que la sustituya fije
    el mismo plazo.
  - Lo aprueba el arquitecto con los demás corchetes de las legales (F2 de la guía de 002).
  - Esta ADR no toca ese contenido.
- **Ese párrafo afirma lo que esta ADR construye** (la cuenta desligada, la historia cifrada). Sale cierto porque el
  primer release espera a Salud entera (R-1): la LISTA de I2 lleva esas dos piezas.

## Alternativas consideradas

- **B. Completar la 0098 tal como está.** Era de-identificar también las versiones archivadas, conservando lo
  clínico y la cadena de versiones. Cierra la brecha con poco código, pero tiene tres problemas:
  - rompe la inmutabilidad del archivo en la identidad;
  - con la norma colombiana, la historia de-identificada deja de servirle al paciente;
  - los 6 años siguen por debajo del mínimo.

  Rechazada.
- **C. Dejarlo y documentarlo.** La supresión seguía incompleta y la purga, por debajo del mínimo legal.
  Rechazada.
- **Borrar la historia en la supresión.** Va directamente contra el deber de conservarla. Rechazada.

## Consecuencias

**Positivas:**
- La supresión deja de dejar una cuenta borrada ligada a su historia, también en lo archivado.
- La historia se conserva el tiempo que pide la norma que aplica, y la eliminación deja de ser silenciosa.

**Negativas o trade-offs:**
- **Pedir la supresión ya no borra la identidad clínica.** Por eso el aviso al paciente (§4) es parte de la
  decisión y no un adorno.
- **Los documentos clínicos crecen sin purga automática** durante al menos 15 años. Cada historia pesa unos KB
  cifrados, y el disco lo absorbe.
- **«La última atención» es aproximada** mientras la historia no la registre: el trámite tiene que confirmarla.
- **Si el proyecto de MinSalud de 2026 se expide,** derogará la 839 de 2017.
  - Según lo que se sabe, mantiene el plazo.
  - Suma una condición al trámite: no se elimina mientras haya un proceso judicial, disciplinario o administrativo
    relacionado con la atención.
  - **Criterio de reapertura:** una norma expedida que cambie el plazo mínimo, cómo se cuenta o el trámite de
    eliminación reabre esta ADR y `RetencionMinimaDias`. Si se expide el proyecto, esta ADR lo cita y suma esa
    condición al trámite.

**Notas de implementación:** se construye en `linea/salud-cita` (003, I2 T066), en el mismo commit que esta ADR:
- `IPhiStore.ListEntriesAsync` y su implementación en `FileSystemEncryptedPhiStore`, más los siete dobles de
  prueba;
- `FileSystemHealthcareDataAnonymizer` desliga en `patients` y `patient-history`;
- `HealthcareSettings.RetencionMinimaDias` y `RetencionEfectivaDias`;
- `HealthcareRetentionPolicy.AvisarLosCandidatos`;
- `Health.Consent.Retention` en uSync y en el UI.

Las pruebas que afirmaban lo contrario (el nombre «[de-identificado]» y la purga a los 6 años) cambiaron con la
decisión, y lo dicen en su propio comentario.

## Relación con otras ADRs

- **[ADR 0098](0098-healthcare-practice-management-module-phi.md):** la enmienda en §4 y §5. Lo demás sigue en pie.
- **[ADR 0121](0121-la-auditoria-de-acceso-a-phi-tiene-su-propia-retencion.md):** la auditoría clínica no cambia.

## Referencias

- `specs/003-salud-cita/bitacora.md`, 2026-11-05: el hallazgo medido y las opciones A, B y C.
- `DECISIONES.md`, D-003-I2-5.
- Decreto 1377 de 2013, en el Gestor Normativo de Función Pública:
  https://www.funcionpublica.gov.co/eva/gestornormativo/norma.php?i=53646
- Resolución 839 de 2017:
  - en el sitio de MinSalud, https://www.minsalud.gov.co/Normatividad_Nuevo/Resolucion%20No%20839%20de%202017.pdf
    (imagen escaneada, sin OCR);
  - leída por 002 en el normograma de Colpensiones (compilación de Avance Jurídico),
    `normativa.colpensiones.gov.co`.
- Decreto 1074 de 2015, cap. 25, sección 2: leído por 002 en la misma compilación.
