// Shim required by C# 9's init accessor on frameworks earlier than .NET 5 (e.g. net48), which do not
// define System.Runtime.CompilerServices.IsExternalInit. On .NET 5+ the framework provides it, so the shim
// is compiled out to avoid a duplicate-type conflict.
#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
	internal static class IsExternalInit;
}
#endif
