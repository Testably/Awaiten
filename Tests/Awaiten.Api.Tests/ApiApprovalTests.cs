namespace Awaiten.Api.Tests;

/// <summary>
///     Whenever a test fails, this means that the public API surface changed.
///     If the change was intentional, execute the <see cref="ApiAcceptance.AcceptApiChanges()" /> test to take over the
///     current public API surface. The changes will become part of the pull request and will be reviewed accordingly.
/// </summary>
public sealed class ApiApprovalTests
{
	[Theory]
	[MemberData(nameof(TargetFrameworksTheoryData))]
	public async Task VerifyPublicApi(string assemblyName, string framework)
	{
		string publicApi = Helper.CreatePublicApi(framework, assemblyName);
		string expectedApi = Helper.GetExpectedApi(framework, assemblyName);

		await That(publicApi).IsEqualTo(expectedApi);
	}

	public static TheoryData<string, string> TargetFrameworksTheoryData()
	{
		TheoryData<string, string> theoryData = new();
		foreach (string assemblyName in Helper.GetAssemblyNames())
		{
			foreach (string targetFramework in Helper.GetTargetFrameworks())
			{
				theoryData.Add(assemblyName, targetFramework);
			}
		}

		return theoryData;
	}
}
