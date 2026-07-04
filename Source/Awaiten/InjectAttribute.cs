using System;

namespace Awaiten;

/// <summary>
///     Marks a settable or <c>init</c> property to be filled from the object graph after the instance
///     is constructed. Property injection is opt-in: only a property carrying this attribute is filled -
///     a plain <c>required</c> property is left to the caller, exactly like any other property. Awaiten
///     resolves the property's service type the same way a constructor parameter is resolved - direct,
///     <c>Func&lt;T&gt;</c> / <c>Lazy&lt;T&gt;</c>, a collection, or a keyed registration with
///     <c>[FromKey]</c> - and, unless the property is marked <see cref="Deferred" />, assigns it through an
///     object initializer, so no reflection is used and the instance is never observed half-set. A
///     <see cref="Deferred" /> property is instead assigned after construction (to break a mutual cycle); see
///     that member for its narrower rules. By default the property is required: if its service type is not
///     registered the container reports <c>AWT101</c>, exactly like a missing constructor parameter. Mark the
///     property <see cref="Optional" /> to instead leave it unassigned when nothing is registered; see that
///     member for its rules.
/// </summary>
/// <remarks>
///     The property must have a <c>set</c> or <c>init</c> accessor the container can assign through
///     (otherwise <c>AWT136</c>); <c>[Arg]</c> is not valid on an injected property (<c>AWT137</c>) - runtime
///     arguments flow only through a <c>Func&lt;…&gt;</c> factory into constructor parameters. A plain
///     property edge participates fully in cycle (<c>AWT102</c>), captive (<c>AWT105</c>) and
///     async-initialization analysis, exactly like a constructor parameter - and even when marked
///     <see cref="Deferred" /> it still participates in captive and async-initialization analysis; only its
///     cycle edge is dropped, so it can break a mutual constructor cycle. Only constructed instances are
///     filled; a factory- or pre-built-instance registration is produced whole by its source.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InjectAttribute : Attribute
{
    /// <summary>
    ///     Defers the property's assignment until after the owning instance is constructed and cached,
    ///     instead of filling it inside the object initializer. A deferred property contributes no cycle
    ///     edge - it is excluded from cycle analysis (<c>AWT102</c>) - so it can break a mutual constructor
    ///     cycle: two singletons that each reference the other through a deferred property are both
    ///     constructed and cached first, then wired up. It still participates in captive (<c>AWT105</c>) and
    ///     async-initialization analysis, since its assignment captures the target for the owner's lifetime
    ///     and awaits an async-initialized target just like a constructor parameter. Requires an accessible plain
    ///     <c>set</c> accessor: post-construction assignment cannot use an <c>init</c>-only accessor, and a
    ///     <c>required</c> member cannot be omitted from the construction-time object initializer (either is
    ///     <c>AWT144</c>). A cycle-breaking deferred property needs a synchronous singleton or scoped participant
    ///     whose cache terminates the re-entrant resolve: a cycle whose participants are all transients has no cache
    ///     anywhere to terminate it (an all-transient deferred cycle is <c>AWT145</c>), an async-initialized
    ///     participant publishes its memoized task only after the re-entrant resolve has returned (a deferred cycle
    ///     through one is <c>AWT146</c>), and a constructor parameter or plain <c>[Inject]</c> edge that leaves a
    ///     cached participant re-enters it before it is cached and constructs a duplicate (a mixed cycle is
    ///     <c>AWT147</c>) - such an edge is supported only when it starts at a transient and the cycle has a cached
    ///     participant; otherwise break the cycle by deferring both sides.
    /// </summary>
    public bool Deferred { get; set; }

    /// <summary>
    ///     Makes the property's dependency optional: when its service type is not registered on the container
    ///     the property is left unassigned (at its default) instead of reporting the missing-dependency error
    ///     (<c>AWT101</c>) that a required <c>[Inject]</c> property produces. When the dependency <em>is</em>
    ///     registered an optional property is filled exactly like a required one - through the object
    ///     initializer, participating fully in cycle, captive and async-initialization analysis - so
    ///     <c>Optional</c> only changes what happens when nothing is registered.
    /// </summary>
    /// <remarks>
    ///     The property must be omittable from the construction-time object initializer, since that is exactly
    ///     what happens when the dependency is absent. A <c>required</c> member cannot be omitted (the generated
    ///     construction would fail with <c>CS9035</c>), so an optional <c>required</c> property is <c>AWT157</c>.
    ///     An <c>init</c>-only property is omittable and therefore allowed, but reports the suppressible warning
    ///     <c>AWT158</c>: when the dependency is absent an <c>init</c>-only property can never be assigned
    ///     afterwards either, so it stays at its default with no fallback - give it a plain <c>set</c> accessor so
    ///     a value can still be assigned after construction when the dependency is not registered. <c>Optional</c>
    ///     has no effect on a collection property
    ///     (an unregistered collection already yields an empty collection, not a missing dependency).
    /// </remarks>
    public bool Optional { get; set; }
}
