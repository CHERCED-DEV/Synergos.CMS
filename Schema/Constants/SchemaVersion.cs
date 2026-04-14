namespace Synergos.CMS.Schema.Constants;

/// <summary>
/// Idempotency key stored in Umbraco's key-value store.
/// Increment the version string to force a re-run of schema initialization.
/// </summary>
public static class SchemaVersion
{
    public const string Key   = "Synergos.Schema.Version";
    public const string Value = "10.1.0"; // 10.1.0 — PageBare doc type + BlockGrid centralization + Dictionary sweep + SEO bug fix
}
