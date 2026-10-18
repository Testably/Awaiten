using System.Collections.Generic;
using System.Linq;
using aweXpect.Core;
using aweXpect.Results;
using aweXpect.Snapshots;
using aweXpect.Snapshots.PublicApi;
// ReSharper disable ExplicitCallerInfoArgument

namespace Awaiten.Api.Tests;

/// <summary>
///     Whenever a test fails, this means that the public API surface changed.
///     If the change was intentional, execute <see cref="AcceptApiChanges" />, rename the pending snapshot next to this
///     file or run the tests with the environment variable <c>AWEXPECT_ACCEPT_SNAPSHOTS</c> set to <c>all</c> to take
///     over the current public API surface. The changes will become part of the pull request and will be reviewed
///     accordingly.
/// </summary>
public sealed class ApiApprovalTests
{
	private static readonly string[] AssemblyNames =
	[
		"Awaiten",
		"Awaiten.Extensions.DependencyInjection",
	];

	[Theory]
	[MemberData(nameof(Builds))]
	public async Task VerifyPublicApi(string assemblyName, string framework)
		=> await MatchesPublicApiSnapshot(assemblyName, framework);

	/// <summary>
	///     Execute this test to update the expected public API to the current API surface.
	/// </summary>
	[Fact(Explicit = true)]
	public async Task AcceptApiChanges()
	{
		foreach (TheoryDataRow<string, string> build in Builds())
		{
			try
			{
				await MatchesPublicApiSnapshot(build.Data.Item1, build.Data.Item2).Accepting(SnapshotAcceptance.All);
			}
			catch (Exception exception) when (exception.Message.Contains("Accepted snapshot"))
			{
				// An accepted snapshot still fails the expectation.
			}
		}
	}

	public static TheoryData<string, string> Builds()
	{
		TheoryData<string, string> theoryData = new();
		foreach (string assemblyName in AssemblyNames)
		{
			foreach (string framework in Helper.GetTargetFrameworks())
			{
				theoryData.Add(assemblyName, framework);
			}
		}

		return theoryData;
	}

	/// <summary>
	///     The snapshot is named after the assembly and the target framework, so that both tests use the same one.
	/// </summary>
	private static SnapshotResult<string?, IThat<string?>> MatchesPublicApiSnapshot(string assemblyName,
		string framework)
		=> That(Helper.GetAssemblyFile(framework, assemblyName))
			.MatchesPublicApiSnapshot(callerMemberName: assemblyName).Named(framework);
}
