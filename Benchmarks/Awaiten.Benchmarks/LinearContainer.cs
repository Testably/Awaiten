using System.Diagnostics.CodeAnalysis;

namespace Awaiten.Benchmarks;

/// <summary>
///     A hand-written linear type-chain resolver: a sequence of <c>serviceType == key</c> comparisons,
///     equivalent to the <c>if</c>-chain Awaiten emits for its dispatch. It is O(N) in the number of keys,
///     so resolving the last key (or an unregistered type) scans them all. It stands in as the "before"
///     reference in the resolution benchmarks, against which the static <c>Type</c>-table dispatch will be
///     compared.
/// </summary>
public sealed class LinearContainer
{
	private static readonly object Sentinel = new();
	private readonly Type[] _keys;

	public LinearContainer(Type[] keys) => _keys = keys;

	public object Resolve(Type serviceType)
	{
		if (TryResolve(serviceType, out object? instance))
		{
			return instance!;
		}

		throw new InvalidOperationException($"No registration for type '{serviceType}'.");
	}

	public bool TryResolve(Type serviceType, [NotNullWhen(true)] out object? instance)
	{
		for (int i = 0; i < _keys.Length; i++)
		{
			if (serviceType == _keys[i])
			{
				instance = Sentinel;
				return true;
			}
		}

		instance = null;
		return false;
	}
}
