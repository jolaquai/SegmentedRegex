namespace SegmentedRegex;

/// <summary>
/// Instructs the SegmentedRegex source generator to implement the annotated partial method as a
/// <see cref="SegEx"/> for the given pattern.
/// </summary>
/// <remarks>
/// The method must be partial, parameterless, and return <see cref="SegEx"/>. Patterns the generator cannot
/// emit a segment-native matcher for fall back to the materialize-and-delegate engine rather than failing
/// the build.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class GeneratedSegExAttribute : Attribute
{
    /// <summary>Matches <paramref name="pattern"/> with no options and no timeout.</summary>
    /// <param name="pattern">The pattern to match.</param>
    public GeneratedSegExAttribute(string pattern)
        : this(pattern, RegexOptions.None)
    {
    }

    /// <summary>Matches <paramref name="pattern"/> with the given options and no timeout.</summary>
    /// <param name="pattern">The pattern to match.</param>
    /// <param name="options">The options to construct it with.</param>
    public GeneratedSegExAttribute(string pattern, RegexOptions options)
        : this(pattern, options, Timeout.Infinite)
    {
    }

    /// <summary>Matches <paramref name="pattern"/> with the given options and timeout.</summary>
    /// <param name="pattern">The pattern to match.</param>
    /// <param name="options">The options to construct it with.</param>
    /// <param name="matchTimeoutMilliseconds">The per-match timeout, or <see cref="Timeout.Infinite"/>.</param>
    public GeneratedSegExAttribute(string pattern, RegexOptions options, int matchTimeoutMilliseconds)
        : this(pattern, options, matchTimeoutMilliseconds, string.Empty)
    {
    }

    /// <summary>Matches <paramref name="pattern"/> with the given options, folding case using <paramref name="cultureName"/>.</summary>
    /// <param name="pattern">The pattern to match.</param>
    /// <param name="options">The options to construct it with.</param>
    /// <param name="cultureName">The culture whose casing rules apply. Only meaningful with <see cref="RegexOptions.IgnoreCase"/>.</param>
    public GeneratedSegExAttribute(string pattern, RegexOptions options, string cultureName)
        : this(pattern, options, Timeout.Infinite, cultureName)
    {
    }

    /// <summary>Matches <paramref name="pattern"/> with the given options, timeout and culture.</summary>
    /// <param name="pattern">The pattern to match.</param>
    /// <param name="options">The options to construct it with.</param>
    /// <param name="matchTimeoutMilliseconds">The per-match timeout, or <see cref="Timeout.Infinite"/>.</param>
    /// <param name="cultureName">The culture whose casing rules apply. Only meaningful with <see cref="RegexOptions.IgnoreCase"/>.</param>
    public GeneratedSegExAttribute(string pattern, RegexOptions options, int matchTimeoutMilliseconds, string cultureName)
    {
        Pattern = pattern;
        Options = options;
        MatchTimeoutMilliseconds = matchTimeoutMilliseconds;
        CultureName = cultureName;
    }

    /// <summary>The pattern to match.</summary>
    public string Pattern { get; }

    /// <summary>The options the pattern is constructed with.</summary>
    public RegexOptions Options { get; }

    /// <summary>The per-match timeout in milliseconds, or <see cref="Timeout.Infinite"/>.</summary>
    public int MatchTimeoutMilliseconds { get; }

    /// <summary>
    /// The culture whose casing rules apply to <see cref="RegexOptions.IgnoreCase"/>, or empty for the
    /// invariant culture.
    /// </summary>
    /// <remarks>
    /// Case folding is resolved when the matcher is generated, not when it runs, so an empty value means
    /// invariant rather than "whatever culture the process happens to be in". Specifying this alongside
    /// <see cref="RegexOptions.CultureInvariant"/> is an error. Patterns that cannot be generated fall back
    /// to an engine that always folds invariantly, so a named culture is not honored there.
    /// </remarks>
    public string CultureName { get; }
}
