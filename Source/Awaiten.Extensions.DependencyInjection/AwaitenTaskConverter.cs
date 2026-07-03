using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Tasks;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     Adapts the container's <c>Task&lt;object&gt;</c> ResolveAsync result to the strongly-typed
///     <c>Task&lt;T&gt;</c> a consumer asks for. The conversion is type-specific, not container-specific, so
///     the bound delegates are cached per service type and shared by both bridge paths (the collection
///     projection and <see cref="AwaitenServiceProvider" />).
/// </summary>
internal static class AwaitenTaskConverter
{
	private static readonly MethodInfo AsTaskMethod =
		typeof(AwaitenTaskConverter).GetMethod(nameof(AsTask), BindingFlags.NonPublic | BindingFlags.Static)!;

	private static readonly ConcurrentDictionary<Type, Func<Task<object>, object>> Converters = new();

	public static Func<Task<object>, object> For(Type serviceType)
		=> Converters.GetOrAdd(serviceType, static type =>
			(Func<Task<object>, object>)AsTaskMethod.MakeGenericMethod(type).CreateDelegate(typeof(Func<Task<object>, object>)));

	private static async Task<T> AsTask<T>(Task<object> resolution) => (T)await resolution.ConfigureAwait(false);
}
