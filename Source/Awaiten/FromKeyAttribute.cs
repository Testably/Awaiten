using System;

namespace Awaiten;

/// <summary>
///     Selects the keyed registration of a constructor parameter's service type. The parameter is
///     resolved from the registration whose <c>Key</c> matches <see cref="Key" /> rather than the
///     unkeyed one.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class FromKeyAttribute : Attribute
{
	/// <summary>
	///     Initializes a new instance of the <see cref="FromKeyAttribute" /> class.
	/// </summary>
	/// <param name="key">The resolution key to select.</param>
	public FromKeyAttribute(string key) => Key = key;

	/// <summary>
	///     The resolution key to select.
	/// </summary>
	public string Key { get; }
}
