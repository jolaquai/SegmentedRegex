// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Hand-written stand-in for the real repo's auto-generated SR.cs. Inside dotnet/runtime this file
// is emitted at build time by their internal GenerateResxSourceTask from Resources/Strings.resx;
// that tooling is repo-internal infrastructure, not something to vendor. This provides the same
// surface (bare properties + Format overloads) backed by the real, vendored Strings.resx, which
// is embedded as a resource so the actual message text stays byte-faithful to upstream and this
// file itself never needs to change when Strings.resx is re-synced (see scripts/Sync-RegexGenUpstream.ps1).

namespace System
{
    internal static class SR
    {
        private static global::System.Resources.ResourceManager? s_resourceManager;
        internal static global::System.Resources.ResourceManager ResourceManager =>
            s_resourceManager ??= new global::System.Resources.ResourceManager("SegEx.SourceGen.Strings", typeof(SR).Assembly);

        private static string GetResourceString(string key) => ResourceManager.GetString(key, null) ?? key;

        internal static string AlternationHasComment => GetResourceString("AlternationHasComment");
        internal static string AlternationHasMalformedCondition => GetResourceString("AlternationHasMalformedCondition");
        internal static string AlternationHasMalformedReference => GetResourceString("AlternationHasMalformedReference");
        internal static string AlternationHasNamedCapture => GetResourceString("AlternationHasNamedCapture");
        internal static string AlternationHasTooManyConditions => GetResourceString("AlternationHasTooManyConditions");
        internal static string AlternationHasUndefinedReference => GetResourceString("AlternationHasUndefinedReference");
        internal static string Arg_ArrayPlusOffTooSmall => GetResourceString("Arg_ArrayPlusOffTooSmall");
        internal static string BeginIndexNotNegative => GetResourceString("BeginIndexNotNegative");
        internal static string CaptureGroupNameInvalid => GetResourceString("CaptureGroupNameInvalid");
        internal static string CaptureGroupOfZero => GetResourceString("CaptureGroupOfZero");
        internal static string CountTooSmall => GetResourceString("CountTooSmall");
        internal static string EnumNotStarted => GetResourceString("EnumNotStarted");
        internal static string ExclusionGroupNotLast => GetResourceString("ExclusionGroupNotLast");
        internal static string Generic => GetResourceString("Generic");
        internal static string IllegalDefaultRegexMatchTimeoutInAppDomain => GetResourceString("IllegalDefaultRegexMatchTimeoutInAppDomain");
        internal static string InsufficientClosingParentheses => GetResourceString("InsufficientClosingParentheses");
        internal static string InsufficientOpeningParentheses => GetResourceString("InsufficientOpeningParentheses");
        internal static string InsufficientOrInvalidHexDigits => GetResourceString("InsufficientOrInvalidHexDigits");
        internal static string InvalidGeneratedRegexAttributeMessage => GetResourceString("InvalidGeneratedRegexAttributeMessage");
        internal static string InvalidGeneratedRegexAttributeTitle => GetResourceString("InvalidGeneratedRegexAttributeTitle");
        internal static string InvalidGroupingConstruct => GetResourceString("InvalidGroupingConstruct");
        internal static string InvalidRegexArgumentsMessage => GetResourceString("InvalidRegexArgumentsMessage");
        internal static string InvalidUnicodePropertyEscape => GetResourceString("InvalidUnicodePropertyEscape");
        internal static string LengthNotNegative => GetResourceString("LengthNotNegative");
        internal static string LimitedSourceGenerationMessage => GetResourceString("LimitedSourceGenerationMessage");
        internal static string LimitedSourceGenerationTitle => GetResourceString("LimitedSourceGenerationTitle");
        internal static string MakeException => GetResourceString("MakeException");
        internal static string MalformedNamedReference => GetResourceString("MalformedNamedReference");
        internal static string MalformedUnicodePropertyEscape => GetResourceString("MalformedUnicodePropertyEscape");
        internal static string MissingControlCharacter => GetResourceString("MissingControlCharacter");
        internal static string MultipleGeneratedRegexAttributesMessage => GetResourceString("MultipleGeneratedRegexAttributesMessage");
        internal static string NestedQuantifiersNotParenthesized => GetResourceString("NestedQuantifiersNotParenthesized");
        internal static string NoResultOnFailed => GetResourceString("NoResultOnFailed");
        internal static string NotSupported_ReadOnlyCollection => GetResourceString("NotSupported_ReadOnlyCollection");
        internal static string PlatformNotSupported_CompileToAssembly => GetResourceString("PlatformNotSupported_CompileToAssembly");
        internal static string QuantifierAfterNothing => GetResourceString("QuantifierAfterNothing");
        internal static string QuantifierOrCaptureGroupOutOfRange => GetResourceString("QuantifierOrCaptureGroupOutOfRange");
        internal static string RegexMatchTimeoutException_Occurred => GetResourceString("RegexMatchTimeoutException_Occurred");
        internal static string RegexMemberMustHaveValidSignatureMessage => GetResourceString("RegexMemberMustHaveValidSignatureMessage");
        internal static string ReversedCharacterRange => GetResourceString("ReversedCharacterRange");
        internal static string ReversedQuantifierRange => GetResourceString("ReversedQuantifierRange");
        internal static string ShorthandClassInCharacterRange => GetResourceString("ShorthandClassInCharacterRange");
        internal static string UndefinedNamedReference => GetResourceString("UndefinedNamedReference");
        internal static string UndefinedNumberedReference => GetResourceString("UndefinedNumberedReference");
        internal static string UnescapedEndingBackslash => GetResourceString("UnescapedEndingBackslash");
        internal static string UnrecognizedControlCharacter => GetResourceString("UnrecognizedControlCharacter");
        internal static string UnrecognizedEscape => GetResourceString("UnrecognizedEscape");
        internal static string UnrecognizedUnicodeProperty => GetResourceString("UnrecognizedUnicodeProperty");
        internal static string UnterminatedBracket => GetResourceString("UnterminatedBracket");
        internal static string UnterminatedComment => GetResourceString("UnterminatedComment");
        internal static string UseRegexSourceGeneratorMessage => GetResourceString("UseRegexSourceGeneratorMessage");
        internal static string UseRegexSourceGeneratorTitle => GetResourceString("UseRegexSourceGeneratorTitle");

        internal static string Format(string resourceFormat, object? p1) => string.Format(resourceFormat, p1);
        internal static string Format(string resourceFormat, object? p1, object? p2) => string.Format(resourceFormat, p1, p2);
        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3) => string.Format(resourceFormat, p1, p2, p3);
        internal static string Format(string resourceFormat, params object?[] args) => string.Format(resourceFormat, args);
    }
}

namespace FxResources.System.Text.RegularExpressions.Generator
{
    // Marker type only: DiagnosticDescriptors.cs passes typeof(FxResources...SR) to
    // LocalizableResourceString purely to identify the assembly the resources live in - the real
    // ResourceManager instance (System.SR.ResourceManager, above) is what actually does the lookup.
    internal static class SR
    {
    }
}
