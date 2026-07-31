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
	///     Appends a line per disagreeing shape. A shape may also be withheld by throwing; for a framework's
	///     purposes that is still "no instance", but it is recorded distinctly so a failure says which happened.
	/// </summary>
	private static void Collect(
		IServiceProvider provider, string where, Type[] shapes, List<string> disagreements)
	{
		IServiceProviderIsService probe =
			(IServiceProviderIsService)provider.GetService(typeof(IServiceProviderIsService))!;

		foreach (Type shape in shapes)
		{
			bool claimed = probe.IsService(shape);
			string outcome;
			try
			{
				outcome = provider.GetService(shape) is not null ? "instance" : "null";
			}
			catch (Exception exception)
			{
				outcome = exception.GetType().Name;
			}

			if (claimed != (outcome == "instance"))
			{
				disagreements.Add($"{where} {shape}: IsService={claimed}, GetService={outcome}");
			}
		}
	}
}
