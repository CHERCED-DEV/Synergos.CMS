namespace Synergos.CMS.Interfaces;

/// <summary>
/// Desliga al miembro de su historia clínica ANTES del hard-delete de la supresión (ADR 0098 §5, enmendada por la
/// ADR 0142): quita su <c>MemberKey</c> de la versión vigente y de TODAS las archivadas, y la historia queda completa
/// y cifrada, porque se conserva por ley mientras dure su retención aunque la cuenta se borre.
/// </summary>
/// <remarks>
/// <para>Hasta la ADR 0142 de-identificaba (nombre en blanco, nacimiento sólo con el año) y sólo la versión vigente.
/// En Colombia la historia se conserva 15 años desde la última atención (Resolución 839 de 2017, art. 3) y la
/// supresión no procede ante un deber legal de permanecer (Decreto 1377 de 2013, art. 9): de-identificarla la volvía
/// inútil como historia del paciente.</para>
/// <para>Lo invoca <c>IGdprRtbfCoordinator</c> antes de borrar el Member. Implementación:
/// <c>Synergos.CMS.Web.Services.FileSystemHealthcareDataAnonymizer</c> sobre <see cref="IPhiStore"/>. Idempotente (un
/// segundo pase no encuentra match, porque ya desligó la clave). El nombre se queda (superset).</para>
/// </remarks>
public interface IHealthcareDataAnonymizer
{
    /// <summary>
    /// Desliga al miembro <paramref name="memberKey"/> de las versiones (vigentes y archivadas) que lo nombran.
    /// Devuelve cuántos documentos cambió.
    /// </summary>
    Task<int> AnonymizeForMemberAsync(Guid memberKey, CancellationToken cancellationToken);
}
