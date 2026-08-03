namespace Synergos.Api.Sessions.Domain;

/// <summary>Una búsqueda ejecutada, tal como la reporta un origen.</summary>
/// <param name="Query">Texto buscado. Se normaliza a minúsculas y sin espacios sobrantes.</param>
/// <param name="ResultCount">Cuántos resultados devolvió.</param>
/// <param name="ElapsedMs">Cuánto tardó, en milisegundos.</param>
/// <param name="AtUtc">Cuándo ocurrió.</param>
public sealed record SearchEvent(string Query, int ResultCount, long ElapsedMs, DateTime AtUtc);

/// <summary>Un query agregado dentro de una ventana temporal.</summary>
public sealed record QueryStat(string Query, int Count, int LastResultCount, DateTime LastSeenUtc);
