using System;

namespace Awaiten;

/// <summary>
///     Selects the keyed registration of a constructor parameter's or injected property's service type.
///     The dependency is resolved from the registration whose <c>Key</c> matches <see cref="Key" />
///     rather than the unkeyed one.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class FromKeyAttribute : Attribute
{
	/// <param name="key">The resolution key to select.</param>
	public FromKeyAttribute(string key) => Key = key;

	/// <summary>The resolution key to select.</summary>
	public string Key { get; }
}
