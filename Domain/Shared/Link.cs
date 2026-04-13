namespace Synergos.CMS.Domain.Shared;

/// <summary>
/// Representa un enlace editorial con su destino y comportamiento de apertura.
/// Label es opcional: se usa cuando el enlace tiene texto propio
/// en vez de tomarlo del contexto (ej. botón CTA vs enlace en texto).
/// </summary>
public sealed record Link(string Url, string Target = "_self", string? Label = null);
