using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// El emisor de identidad del clon limpio: no emite nada.
/// </summary>
/// <remarks>
/// <para><b>No es un placeholder, es el camino por defecto.</b> Sin <c>Api.Identity</c>
/// levantada el CMS sigue operando exactamente como antes de la HU #14: dice quién actúa y la
/// capacidad le cree, con la fuerza que eso tiene y ni una más. Lo que cambia con el emisor de
/// verdad no es que se pueda actuar, es <i>con qué se respalda</i>.</para>
///
/// <para><b>Devuelve <c>null</c> y no una cadena vacía.</b> Una cabecera de identidad vacía la
/// rechaza la capacidad —bien— pero convierte «no hay identidad» en «hay una identidad ilegible»,
/// que son dos cosas distintas y llevan a preguntas distintas cuando algo falla.</para>
/// </remarks>
public sealed class StubIdentityTokenIssuer : IIdentityTokenIssuer
{
    public Task<string?> IssueAsync(IdentitySubject subject, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);
}
