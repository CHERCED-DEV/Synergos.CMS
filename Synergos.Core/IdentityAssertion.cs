namespace Synergos.Core;

/// <summary>
/// Con qué fuerza se afirmó que alguien era quien decía ser.
/// </summary>
/// <remarks>
/// <para><b>Este valor es lo que hace reversible la decisión de quién certifica la identidad.</b>
/// Sin él, el día que se pase a una afirmación más fuerte los registros viejos quedarían
/// indistinguibles de los nuevos — y todo el archivo pasaría a afirmar más de lo que puede
/// sostener. Con él, un auditor ve que el acceso de agosto se certificó con la sesión del CMS y
/// el de octubre con un token verificable.</para>
///
/// <para><b>El orden es de fuerza creciente</b>, y no es decorativo: es lo que permite responder
/// «¿este registro aguanta?» sin volver a leer el código que lo escribió.</para>
///
/// <para><b>Vive en <c>Synergos.Core</c> desde el segundo consumidor</b>, no antes. Nació dentro
/// de <c>Api.Messaging</c> con la HU #13, que era su único usuario, y ahí estaba bien. Subió al
/// vocabulario cuando el asiento de auditoría de la HU #15 necesitó decir exactamente lo mismo:
/// un segundo enum con los mismos tres valores habría sido dos definiciones de «cuánto vale este
/// registro» capaces de divergir. Es <c>CLAUDE.md</c> §17 aplicado con fecha.</para>
///
/// <para><b>Y por qué está junto a <see cref="Actor"/> y no en <c>Synergos.Shared</c>:</b> esto no
/// es fontanería de host. No sabe qué es una cabecera ni un middleware — es una propiedad del
/// hecho que se está guardando, igual que <see cref="Money"/> o <see cref="TimeWindow"/>.</para>
///
/// <para><b>Lo que NO es:</b> un permiso. Que la afirmación sea fuerte no dice que quien la hizo
/// pueda hacer lo que pide; eso lo decide cada capacidad con sus propias reglas. Confundir las dos
/// cosas convertiría este enum en un sistema de autorización por la puerta de atrás.</para>
/// </remarks>
public enum IdentityAssertion
{
    /// <summary>
    /// Lo afirma la sesión del CMS. <b>Registro de acceso autenticado, no acuse con valor
    /// probatorio</b>: quien certifica es nuestro propio sistema. Sirve para operar, auditar y
    /// discutir de buena fe; no para sostener solo que un término empezó a correr.
    /// </summary>
    CmsSession = 1,

    /// <summary><c>Api.Identity</c> emitió un token verificable.</summary>
    /// <remarks>
    /// <para><b>Los acuses anteriores a la HU #14 que digan <c>IdentityToken</c> mienten sobre sí
    /// mismos, y hay que leerlos como <see cref="CmsSession"/>.</b> Antes de esa HU nadie sabía
    /// emitir tokens, y aun así <c>Api.Messaging</c> anotaba con este valor el acuse del propio
    /// autor de un mensaje: el razonamiento era «no hay duda de quién escribió», que mide
    /// CONFIANZA cuando el campo mide QUIÉN DIO FE. Es el defecto #42.</para>
    ///
    /// <para><b>Y no se corrigen.</b> Reescribir un registro append-only para que diga lo que
    /// debió decir lo convierte en editable, que es exactamente lo que lo inutiliza como prueba.
    /// El archivo dice lo que dijo; esta nota dice cuánto vale. Por eso queda escrita acá y no en
    /// una migración: la fecha del acuse es lo que permite distinguir unos de otros.</para>
    /// </remarks>
    IdentityToken = 2,

    /// <summary>Federación con Autenticación Digital del Estado (Dec. 620/2020).</summary>
    GovFederation = 3,
}
