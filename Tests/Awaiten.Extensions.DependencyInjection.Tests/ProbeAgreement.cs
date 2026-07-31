using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection.Tests;

/// <summary>
///     Compares what <see cref="IServiceProviderIsService" /> claims against what resolution actually does, for a
///     given set of type shapes. This is the invariant that makes the probe worth implementing: a false positive
///     makes a framework resolve null, a false negative makes it bind the parameter from somewhere else entirely,
///     and both are silent.
/// </summary>
internal static class ProbeAgreement
{
	/// <summary>
	///     Every shape where the probe and the resolution disagree, as one readable string — empty when they agree
	///     everywhere. Both the root provider and a scope of it are asked, because some of the container's
	///     resolution rules differ between the two, so checking only one would hide them.
	/// </summary>
	public static string Disagreements(AwaitenServiceProvider provider, Type[] shapes)
	{
		List<string> disagreements = [];
		Collect(provider, "root", shapes, disagreements);

		using IServiceScope scope = provider.CreateScope();
		Collect(scope.ServiceProvider, "scope", shapes, disagreements);

		return string.Join("; ", disagreements);
	}

	/// <summary>
	///     The same comparison for the keyed surface: <see cref="IServiceProviderIsKeyedService" /> against
	///     <see cref="IKeyedServiceProvider.GetKeyedService" /> under <paramref name="key" />. The keyed probe
	///     shares its rule set with the unkeyed one but takes different branches through it (a collection or
	///     dictionary shape is unkeyed-only, a <c>Task&lt;T&gt;</c> looks up the converter under the key), so it
	///     needs its own sweep.
	/// </summary>
	public static string Disagreements(AwaitenServiceProvider provider, Type[] shapes, object? key)
	{
		List<string> disagreements = [];
		CollectKeyed(provider, "root", shapes, key, disagreements);

		using IServiceScope scope = provider.CreateScope();
		CollectKeyed(scope.ServiceProvider, "scope", shapes, key, disagreements);

		return string.Join("; ", disagreements);
	}

	private static void Collect(
		IServiceProvider provider, string where, Type[] shapes, List<string> disagreements)
	{
		IServiceProviderIsService probe =
			(IServiceProviderIsService)provider.GetService(typeof(IServiceProviderIsService))!;

		foreach (Type shape in shapes)
		{
			Record(where, shape, probe.IsService(shape), () => provider.GetService(shape), disagreements);
		}
	}

	private static void CollectKeyed(
		IServiceProvider provider, string where, Type[] shapes, object? key, List<string> disagreements)
	{
		IServiceProviderIsKeyedService probe =
			(IServiceProviderIsKeyedService)provider.GetService(typeof(IServiceProviderIsKeyedService))!;
		IKeyedServiceProvider keyed = (IKeyedServiceProvider)provider;

		foreach (Type shape in shapes)
		{
			Record($"{where} key '{key}'", shape, probe.IsKeyedService(shape, key),
				() => keyed.GetKeyedService(shape, key), disagreements);
		}
	}

	/// <summary>
	///     Appends a line when the claim and the resolution disagree. A shape may also be withheld by throwing; for
	///     a framework's purposes that is still "no instance", but it is recorded distinctly so a failure says which
	///     happened.
	/// </summary>
	private static void Record(
		string where, Type shape, bool claimed, Func<object?> resolve, List<string> disagreements)
	{
		string outcome;
		try
		{
			outcome = resolve() is not null ? "instance" : "null";
		}
		catch (Exception exception)
		{
			outcome = exception.GetType().Name;
		}

		if (claimed != (outcome == "instance"))
		{
			disagreements.Add($"{where} {shape}: claimed={claimed}, resolved={outcome}");
		}
	}
}
