using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

#pragma warning disable
// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices;

/// <summary>
///     Shim required by C# 9's <c>init</c> accessor and positional records when targeting
///     <c>netstandard2.0</c>, which does not define
///     <c>System.Runtime.CompilerServices.IsExternalInit</c>.
/// </summary>
[ExcludeFromCodeCoverage]
[DebuggerNonUserCode]
internal static class IsExternalInit;

#pragma warning restore
