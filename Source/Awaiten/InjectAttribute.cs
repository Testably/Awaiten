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
///     arguments flow only through a <c>Func&lt;…&gt;</c> factory into constructor parameters. A plain
///     property edge participates fully in cycle (<c>AWT102</c>), captive (<c>AWT105</c>) and
///     async-initialization analysis, exactly like a constructor parameter - unless it is marked
///     <see cref="Deferred" />, which excludes it so it can break a mutual constructor cycle. Only
///     constructed instances are filled; a factory- or pre-built-instance registration is produced whole
///     by its source.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InjectAttribute : Attribute
{
    /// <summary>
    ///     Defers the property's assignment until after the owning instance is constructed and cached,
    ///     instead of filling it inside the object initializer. A deferred property contributes no graph
    ///     edge - it is excluded from cycle (<c>AWT102</c>), captive (<c>AWT105</c>) and async-taint
    ///     analysis exactly like a <c>Func&lt;T&gt;</c> / <c>Lazy&lt;T&gt;</c> relationship - so it can
    ///     break a mutual constructor cycle: two singletons that each reference the other through a
    ///     deferred property are both constructed and cached first, then wired up. Requires an accessible
    ///     <c>set</c> accessor (post-construction assignment cannot use an <c>init</c>-only accessor,
    ///     which is <c>AWT138</c>). A cycle-breaking deferred property applies only to synchronous singleton
    ///     and scoped registrations: a transient has no cache to terminate a mutual cycle (a transient deferred
    ///     cycle is <c>AWT139</c>), and an async-initialized participant publishes its memoized task only after
    ///     the re-entrant resolve has returned (a deferred cycle through one is <c>AWT140</c>).
    /// </summary>
    public bool Deferred { get; set; }
}
