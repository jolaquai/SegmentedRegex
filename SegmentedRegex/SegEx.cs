namespace SegmentedRegex;

/// <summary>
/// A regular expression that matches against a segmented, non-contiguous subject.
/// </summary>
/// <remarks>
/// <see cref="ReadOnlySequence{T}"/> of <see cref="char"/> is the currency of the API; the overloads taking
/// contiguous input wrap it as a single segment. Instances obtained from <see cref="Create(string)"/> and its
/// overloads run the fallback engine, which materializes the subject and delegates to
/// <see cref="Regex"/>. Applying <see cref="GeneratedSegExAttribute"/> to a partial method produces an
/// implementation that matches over the segments directly.
/// </remarks>
public abstract class SegEx
{
    /// <summary>Initializes the shared state of a <see cref="SegEx"/>.</summary>
    /// <param name="pattern">The pattern this instance matches.</param>
    /// <param name="options">The options the pattern was constructed with.</param>
    /// <param name="matchTimeout">The per-match timeout, or <see cref="Regex.InfiniteMatchTimeout"/>.</param>
    protected SegEx(string pattern, RegexOptions options, TimeSpan matchTimeout)
    {
        Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
        Options = options;
        MatchTimeout = matchTimeout;
    }

    /// <summary>The pattern this instance matches.</summary>
    public string Pattern { get; }

    /// <summary>The options this instance was constructed with.</summary>
    public RegexOptions Options { get; }

    /// <summary>The per-match timeout, or <see cref="Regex.InfiniteMatchTimeout"/> when unbounded.</summary>
    public TimeSpan MatchTimeout { get; }

    /// <summary>Determines whether <paramref name="input"/> contains a match.</summary>
    /// <param name="input">The subject to search.</param>
    /// <returns><see langword="true"/> if the pattern matches anywhere in the subject.</returns>
    public abstract bool IsMatch(in ReadOnlySequence<char> input);

    /// <summary>Finds the first match in <paramref name="input"/>.</summary>
    /// <param name="input">The subject to search.</param>
    /// <returns>The match; check <see cref="SegExGroup.Success"/> to see whether one was found.</returns>
    public abstract SegExMatch Match(in ReadOnlySequence<char> input);

    /// <summary>Finds every non-overlapping match in <paramref name="input"/>.</summary>
    /// <param name="input">The subject to search.</param>
    /// <returns>The matches, in the order they were found.</returns>
    public abstract SegExMatchCollection Matches(in ReadOnlySequence<char> input);

    /// <summary>Creates a <see cref="SegEx"/> for <paramref name="pattern"/>.</summary>
    /// <param name="pattern">The pattern to match.</param>
    /// <returns>A <see cref="SegEx"/> backed by the fallback engine.</returns>
    /// <remarks>The per-match timeout is the process-wide <c>REGEX_DEFAULT_MATCH_TIMEOUT</c> default, if one is set.</remarks>
    public static SegEx Create(string pattern) => Create(pattern, RegexOptions.None, DefaultMatchTimeout());

    /// <summary>Creates a <see cref="SegEx"/> for <paramref name="pattern"/>.</summary>
    /// <param name="pattern">The pattern to match.</param>
    /// <param name="options">The options to construct it with.</param>
    /// <returns>A <see cref="SegEx"/> backed by the fallback engine.</returns>
    /// <remarks>The per-match timeout is the process-wide <c>REGEX_DEFAULT_MATCH_TIMEOUT</c> default, if one is set.</remarks>
    public static SegEx Create(string pattern, RegexOptions options) => Create(pattern, options, DefaultMatchTimeout());

    /// <summary>Creates a <see cref="SegEx"/> for <paramref name="pattern"/>.</summary>
    /// <param name="pattern">The pattern to match.</param>
    /// <param name="options">The options to construct it with.</param>
    /// <param name="matchTimeout">The per-match timeout.</param>
    /// <returns>A <see cref="SegEx"/> backed by the fallback engine.</returns>
    /// <remarks>
    /// Runtime-constructed instances always use the fallback engine; only the source generator can emit the
    /// segment-native one.
    /// </remarks>
    public static SegEx Create(string pattern, RegexOptions options, TimeSpan matchTimeout) => new FallbackSegEx(pattern, options, matchTimeout);

    /// <summary>
    /// The process-wide default match timeout, or <see cref="Regex.InfiniteMatchTimeout"/> when none is set.
    /// </summary>
    /// <remarks>
    /// Resolves the same <c>REGEX_DEFAULT_MATCH_TIMEOUT</c> <see cref="AppContext"/> value the generated path
    /// snapshots into its emitted <c>Utilities.s_defaultTimeout</c>, so runtime-constructed and fallback-routed
    /// patterns honor the default the same way the segment-native ones do. Read live per call rather than
    /// snapshotted, which the generated path cannot do; both agree for the usual startup-set switch.
    /// </remarks>
    internal static TimeSpan DefaultMatchTimeout() => AppContext.GetData("REGEX_DEFAULT_MATCH_TIMEOUT") is TimeSpan timeout ? timeout : Regex.InfiniteMatchTimeout;

    /// <inheritdoc cref="IsMatch(in ReadOnlySequence{char})"/>
    public bool IsMatch(string input) => IsMatch(Wrap(input));

    /// <inheritdoc cref="IsMatch(in ReadOnlySequence{char})"/>
    public bool IsMatch(char[] input) => IsMatch(Wrap(input));

    /// <inheritdoc cref="IsMatch(in ReadOnlySequence{char})"/>
    public bool IsMatch(ReadOnlyMemory<char> input) => IsMatch(new ReadOnlySequence<char>(input));

    /// <inheritdoc cref="IsMatch(in ReadOnlySequence{char})"/>
    /// <remarks>A span cannot be retained, so this overload copies the input.</remarks>
    public bool IsMatch(ReadOnlySpan<char> input) => IsMatch(Wrap(input));

    /// <inheritdoc cref="IsMatch(in ReadOnlySequence{char})"/>
    /// <inheritdoc cref="StringBuilderExtensions.AsSequence(StringBuilder)" path="/remarks"/>
    public bool IsMatch(StringBuilder input) => IsMatch(input.AsSequence());

    /// <inheritdoc cref="Match(in ReadOnlySequence{char})"/>
    public SegExMatch Match(string input) => Match(Wrap(input));

    /// <inheritdoc cref="Match(in ReadOnlySequence{char})"/>
    public SegExMatch Match(char[] input) => Match(Wrap(input));

    /// <inheritdoc cref="Match(in ReadOnlySequence{char})"/>
    public SegExMatch Match(ReadOnlyMemory<char> input) => Match(new ReadOnlySequence<char>(input));

    /// <inheritdoc cref="Match(in ReadOnlySequence{char})"/>
    /// <remarks>A span cannot be retained, so this overload copies the input.</remarks>
    public SegExMatch Match(ReadOnlySpan<char> input) => Match(Wrap(input));

    /// <inheritdoc cref="Match(in ReadOnlySequence{char})"/>
    /// <inheritdoc cref="StringBuilderExtensions.AsSequence(StringBuilder)" path="/remarks"/>
    public SegExMatch Match(StringBuilder input) => Match(input.AsSequence());

    /// <inheritdoc cref="Matches(in ReadOnlySequence{char})"/>
    public SegExMatchCollection Matches(string input) => Matches(Wrap(input));

    /// <inheritdoc cref="Matches(in ReadOnlySequence{char})"/>
    public SegExMatchCollection Matches(char[] input) => Matches(Wrap(input));

    /// <inheritdoc cref="Matches(in ReadOnlySequence{char})"/>
    public SegExMatchCollection Matches(ReadOnlyMemory<char> input) => Matches(new ReadOnlySequence<char>(input));

    /// <inheritdoc cref="Matches(in ReadOnlySequence{char})"/>
    /// <remarks>A span cannot be retained, so this overload copies the input.</remarks>
    public SegExMatchCollection Matches(ReadOnlySpan<char> input) => Matches(Wrap(input));

    /// <inheritdoc cref="Matches(in ReadOnlySequence{char})"/>
    /// <inheritdoc cref="StringBuilderExtensions.AsSequence(StringBuilder)" path="/remarks"/>
    public SegExMatchCollection Matches(StringBuilder input) => Matches(input.AsSequence());

    /// <inheritdoc/>
    public override string ToString() => Pattern;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySequence<char> Wrap(string input) => input is null
        ? throw new ArgumentNullException(nameof(input))
        : new ReadOnlySequence<char>(input.AsMemory());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySequence<char> Wrap(char[] input) => input is null
        ? throw new ArgumentNullException(nameof(input))
        : new ReadOnlySequence<char>(input);

    // Results keep a reference to their subject for lazy Value materialization, which a span cannot provide.
    private static ReadOnlySequence<char> Wrap(ReadOnlySpan<char> input) => new ReadOnlySequence<char>(SequenceText.FromSpan(input).AsMemory());
}
