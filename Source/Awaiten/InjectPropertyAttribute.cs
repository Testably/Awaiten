using System;

namespace Awaiten;

// S2326: the type parameter is the source generator's input. It reads the implementation type from the
// attribute's type argument via Roslyn, so the body never references it.
#pragma warning disable S2326

/// <summary>
///     Declares, on the <see cref="ContainerAttribute">container</see>, a property of
///     <typeparamref name="TImplementation" /> to fill from the object graph after construction - the
///     container-side counterpart of <see cref="InjectAttribute">[Inject]</see> that keeps the implementation a
///     plain POCO. The named property is filled exactly like an <c>[Inject]</c> one: its service type is
///     resolved like a constructor parameter (direct, <c>Func&lt;T&gt;</c> / <c>Lazy&lt;T&gt;</c>, a collection,
///     or a keyed registration) and assigned through an object initializer, so no reflection is used and the
///     instance is never observed half-set. Because it keys off the <em>implementation</em> type, it applies
///     wherever that implementation is constructed - including types brought in by a <c>[Scan]</c>, which a
///     list of names on a single registration attribute could not reach. Repeat it, once per property; pass
///     <c>nameof(TImplementation.Property)</c> so the name stays rename-safe.
/// </summary>
/// <remarks>
///     The named member must be a settable property on <typeparamref name="TImplementation" /> (or a base
///     type): an unknown, non-property or read-only name is <c>AWT177</c>, a setter the container cannot reach
///     is <c>AWT136</c>, and a duplicate entry for one property is <c>AWT179</c>. Property injection applies
///     only to a container-constructed instance, so a <typeparamref name="TImplementation" /> produced by a
///     <c>Factory</c> or <c>Instance</c> registration is <c>AWT178</c> (the source produces it whole). A
///     property that also carries <c>[Inject]</c> is filled once, with no diagnostic. A closed implementation
///     type is required: matching an <em>open</em> generic <typeparamref name="TImplementation" /> against
///     open-generic scanned types is not supported. Targeting a <c>[Decorate]</c> decorator type is likewise not
///     supported: a decorator wrapper is built by the decorator chain, not filled by an entry, so an
///     <c>[InjectProperty]</c> naming a decorator type has no effect (add <c>[Inject]</c> to the decorator's own
///     property instead). To fill a property on the wrapped implementation, name that implementation.
/// </remarks>
/// <typeparam name="TImplementation">The concrete type whose property is filled.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class InjectPropertyAttribute<TImplementation>(string propertyName) : Attribute
    where TImplementation : class
{
    /// <summary>
    ///     The name of the property on <typeparamref name="TImplementation" /> to fill. Use
    ///     <c>nameof(TImplementation.Property)</c> so a rename carries through.
    /// </summary>
    public string PropertyName { get; } = propertyName;

    /// <summary>
    ///     Makes the dependency optional: when its service type is not registered the property is left at its
    ///     default instead of reporting <c>AWT101</c>. When the dependency is registered an optional property is
    ///     filled exactly like a required one. Mirrors <see cref="InjectAttribute.Optional" /> and may combine
    ///     with <see cref="Deferred" />.
    /// </summary>
    public bool Optional { get; set; }

    /// <summary>
    ///     Defers the property's assignment until after the owning instance is constructed and cached, instead
    ///     of filling it in the object initializer, so it carries no cycle edge and can break a mutual
    ///     constructor cycle. Mirrors <see cref="InjectAttribute.Deferred" /> and requires an accessible plain
    ///     <c>set</c> accessor (an <c>init</c>-only or <c>required</c> property is <c>AWT144</c>).
    /// </summary>
    public bool Deferred { get; set; }

    /// <summary>
    ///     Optional resolution key: selects the keyed registration of the property's service type, exactly as a
    ///     <c>[FromKey]</c> would on an <c>[Inject]</c> property. A <c>string</c>, an <c>enum</c> value, or a
    ///     <c>typeof(...)</c>.
    /// </summary>
    public object? Key { get; set; }
}
