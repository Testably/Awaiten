using System;
using System.ComponentModel;

namespace Awaiten;

/// <summary>
///     Emitted by the source generator onto every <c>[Module]</c> whose <c>[Scan]</c> it expanded, even when the
///     scan matched nothing. A consuming container uses it as the version-skew guard: a referenced module whose
///     metadata carries a <c>[Scan]</c> but not this marker was compiled without the Awaiten generator (or with a
///     version predating self-compiled module scans), so its scan would be silently dropped; the container reports
///     AWT154 instead.
/// </summary>
/// <remarks>Generated code, not intended to be written by hand.</remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class GeneratedScanExpansionAttribute : Attribute;
