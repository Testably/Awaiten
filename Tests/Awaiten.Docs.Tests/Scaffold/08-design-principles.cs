// The architecture test runs in a TUnit test project.
global using TUnit.Core;
global using System.Security.Principal;

namespace Snippets;

// The ambient logger that the page warns against.
public static class Log
{
	public static void Info(string message)
	{
	}
}
