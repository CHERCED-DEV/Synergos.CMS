namespace Synergos.CMS.Interfaces;

/// <summary>
/// El resumen de salud de una historia clínica (003-salud-cita, I2: P7 y P8): las condiciones, las alergias y las
/// vacunas. Vive DENTRO de <see cref="PatientRecord"/>, así que va cifrado y versionado con ella (ADR 0098).
/// </summary>
/// <remarks>
/// <para><b>Un dato tiene dueño y estado.</b> Lo que el paciente escribe en su pre-registro entra
/// <see cref="EstadosDelDato.Declarado"/>, y sólo el médico, con el permiso del paciente, lo deja
/// <see cref="EstadosDelDato.Validado"/>. El paciente nunca escribe un dato clínico validado: el portal le muestra lo
/// suyo como pendiente hasta que el médico lo revise.</para>
/// </remarks>
/// <param name="Datos">Los datos, en el orden en que entraron.</param>
public sealed record ResumenDeSalud(IReadOnlyList<DatoDeSalud> Datos)
{
    /// <summary>Un resumen sin datos: la historia existe y nadie declaró nada todavía.</summary>
    public static ResumenDeSalud Vacio { get; } = new(Array.Empty<DatoDeSalud>());
}

/// <summary>Una condición, una alergia o una vacuna del resumen de salud.</summary>
/// <param name="Id">Estable dentro de la historia: es por donde el médico lo valida.</param>
/// <param name="Tipo">Uno de <see cref="TiposDeDatoDeSalud"/>.</param>
/// <param name="Descripcion">Lo que es, como lo escribió quien lo registró.</param>
/// <param name="Fecha">Día ISO (<c>aaaa-mm-dd</c>), si lo tiene: cuándo se aplicó una vacuna. <c>null</c> si no se dijo.</param>
/// <param name="Estado">Uno de <see cref="EstadosDelDato"/>.</param>
/// <param name="Origen">Quién lo registró: <c>paciente</c> o <c>medico</c>.</param>
/// <param name="RegistradoUtc">Cuándo entró.</param>
/// <param name="RegistradoPor">El miembro que lo registró.</param>
/// <param name="ValidadoUtc">Cuándo lo validó el médico, o <c>null</c>.</param>
/// <param name="ValidadoPor">El miembro médico que lo validó, o <c>null</c>.</param>
public sealed record DatoDeSalud(
    string Id,
    string Tipo,
    string Descripcion,
    string? Fecha,
    string Estado,
    string Origen,
    DateTime RegistradoUtc,
    Guid RegistradoPor,
    DateTime? ValidadoUtc = null,
    Guid? ValidadoPor = null);

/// <summary>Lo que el paciente dice antes de su cita (P8): quién es, su aseguradora y lo que toma.</summary>
/// <remarks>
/// Es DECLARADO: sirve para la recepción y para el médico, que lo confirma en la consulta. Cada check-in lo reemplaza
/// entero (la versión anterior queda archivada con la historia); lo clínico que declara va al resumen, como dato
/// declarado.
/// </remarks>
/// <param name="Documento">El documento de identidad, como lo escribe el paciente.</param>
/// <param name="Telefono">Un teléfono de contacto.</param>
/// <param name="Direccion">Dónde vive.</param>
/// <param name="ContactoDeEmergencia">A quién llamar, con su teléfono.</param>
/// <param name="Aseguradora">Su EPS o aseguradora.</param>
/// <param name="NumeroDeAfiliacion">Su número de afiliación.</param>
/// <param name="Medicamentos">Lo que dice que toma hoy. La medicación activa es la de las recetas (I5), no esta.</param>
/// <param name="MotivoDeLaVisita">Para qué viene, en sus palabras.</param>
/// <param name="DeclaradoUtc">Cuándo lo declaró.</param>
public sealed record PreRegistro(
    string? Documento,
    string? Telefono,
    string? Direccion,
    string? ContactoDeEmergencia,
    string? Aseguradora,
    string? NumeroDeAfiliacion,
    IReadOnlyList<string> Medicamentos,
    string? MotivoDeLaVisita,
    DateTime DeclaradoUtc);

/// <summary>Los tipos de dato del resumen de salud.</summary>
public static class TiposDeDatoDeSalud
{
    public const string Condicion = "condicion";
    public const string Alergia = "alergia";
    public const string Vacuna = "vacuna";

    /// <summary>Los tres, en el orden en que los pinta el portal.</summary>
    public static IReadOnlyList<string> Todos { get; } = new[] { Condicion, Alergia, Vacuna };
}

/// <summary>Los estados de un dato del resumen.</summary>
public static class EstadosDelDato
{
    /// <summary>Lo dijo el paciente; el médico no lo ha revisado.</summary>
    public const string Declarado = "declarado";

    /// <summary>El médico lo revisó y lo dejó en la historia.</summary>
    public const string Validado = "validado";
}
