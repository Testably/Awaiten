using System;

namespace Awaiten;

/// <summary>
///     Marks a settable or <c>init</c> property to be filled from the object graph after construction.
///     Property injection is opt-in: only a property carrying this attribute is filled. The property's
///     service type is resolved like a constructor parameter (direct, <c>Func&lt;T&gt;</c> / <c>Lazy&lt;T&gt;</c>,
///     a collection, or a keyed registration via <c>[FromKey]</c>) and assigned through an object initializer,
///     so no reflection is used and the instance is never observed half-set. A <see cref="Deferred" /> property
///     is instead assigned after construction to break a mutual cycle. By default the property is required:
///     an unregistered service type reports <c>AWT101</c>, like a missing constructor parameter. Mark it
///     <see cref="Optional" /> to leave it unassigned when nothing is registered.
/// </summary>
/// <remarks>
///     The property needs a <c>set</c> or <c>init</c> accessor (otherwise <c>AWT136</c>). <c>[Arg]</c> is not
///     valid on an injected property (<c>AWT137</c>); runtime arguments flow only through a <c>Func&lt;…&gt;</c>
///     factory into constructor parameters. A plain property edge participates fully in cycle (<c>AWT102</c>),
///     captive (<c>AWT105</c>) and async-initialization analysis, like a constructor parameter. Only constructed
///     instances are filled; a factory or pre-built-instance registration is produced whole by its source.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InjectAttribute : Attribute
{
    /// <summary>
    ///     Defers the property's assignment until after the owning instance is constructed and cached,
    ///     instead of filling it in the object initializer. A deferred property carries no cycle edge, so it
    ///     can break a mutual constructor cycle: two singletons that reference each other through a deferred
    ///     property are both constructed and cached first, then wired up. It still participates in captive
    ///     (<c>AWT105</c>) and async-initialization analysis.
    /// </summary>
    /// <remarks>
    ///     Requires an accessible plain <c>set</c> accessor: post-construction assignment cannot use an
    ///     <c>init</c>-only accessor, and a <c>required</c> member cannot be omitted from the construction-time
    ///     object initializer (either is <c>AWT144</c>). A cycle-breaking deferred property needs a synchronous
    ///     singleton or scoped participant whose cache terminates the re-entrant resolve. Diagnostics cover the
    ///     cases that cannot terminate: an all-transient deferred cycle (<c>AWT145</c>), a cycle through an
    ///     async-initialized participant (<c>AWT146</c>), and a mixed cycle whose non-deferred edge re-enters an
    ///     uncached participant (<c>AWT147</c>).
    /// </remarks>
    public bool Deferred { get; set; }

    /// <summary>
    ///     Makes the dependency optional: when its service type is not registered the property is left at its
    ///     default instead of reporting <c>AWT101</c>. When the dependency is registered an optional property is
    ///     filled exactly like a required one, so <c>Optional</c> only changes what happens when nothing is registered.
    /// </summary>
    /// <remarks>
    ///     The property must be omittable from the construction-time object initializer. A <c>required</c> member
    ///     cannot be omitted (an optional <c>required</c> property is <c>AWT157</c>). An <c>init</c>-only property is
    ///     allowed but reports the suppressible warning <c>AWT158</c>: when the dependency is absent it can never be
    ///     assigned afterwards, so prefer a plain <c>set</c> accessor. <c>Optional</c> has no effect on a collection
    ///     property, since an unregistered collection already yields an empty one.
    /// </remarks>
    public bool Optional { get; set; }
}
