using System;

namespace Awaiten;

/// <summary>
///     Marks a settable or <c>init</c> property to be filled from the object graph after the instance
///     is constructed. Property injection is opt-in: only a property carrying this attribute is filled -
///     a plain <c>required</c> property is left to the caller, exactly like any other property. Awaiten
///     resolves the property's service type the same way a constructor parameter is resolved - direct,
///     <c>Func&lt;T&gt;</c> / <c>Lazy&lt;T&gt;</c>, a collection, or a keyed registration with
///     <c>[FromKey]</c> - and assigns it through an object initializer, so no reflection is used and the
///     instance is never observed half-set.
/// </summary>
/// <remarks>
///     The property must have a <c>set</c> or <c>init</c> accessor the container can assign through
///     (otherwise <c>AWT136</c>); <c>[Arg]</c> is not valid on an injected property (<c>AWT137</c>) - runtime
///     arguments flow only through a <c>Func&lt;…&gt;</c> factory into constructor parameters. The
///     property edge participates fully in cycle (<c>AWT102</c>), captive (<c>AWT105</c>) and
///     async-initialization analysis, exactly like a constructor parameter. Only constructed instances
///     are filled; a factory- or pre-built-instance registration is produced whole by its source.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InjectAttribute : Attribute;
