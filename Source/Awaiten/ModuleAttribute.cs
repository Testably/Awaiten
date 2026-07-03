using System;

namespace Awaiten;

/// <summary>
///     Marks a class as a reusable registration module. A module carries the same
///     <c>[Singleton]</c>/<c>[Transient]</c>/<c>[Scoped]</c> registration attributes as a
///     <see cref="ContainerAttribute">container</see> and is pulled into one with
///     <see cref="ImportAttribute" />.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ModuleAttribute : Attribute;
