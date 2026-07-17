using System.Diagnostics.CodeAnalysis;

// xUnit test method naming uses underscores by convention
// (<c>Method_Behavior_When…</c>); CA1707 is silenced at assembly level for
// the test project so reviewers see only signal in the build output.
[assembly: SuppressMessage(
    "Naming", "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit test method naming convention.")]

// Inline array literals (`new[] { "a", "b" }`) are idiomatic in xUnit
// arrange-act-assert blocks; the micro-allocation cost the rule warns
// about is irrelevant for tests.
[assembly: SuppressMessage(
    "Performance", "CA1861:Avoid constant arrays as arguments",
    Justification = "Inline array literals are idiomatic in xUnit tests.")]
