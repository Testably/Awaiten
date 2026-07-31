using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;

namespace Awaiten.WebSample;

/// <summary>
///     Drives the sample end to end and reports an exit code, so a build can prove the bridge actually serves
///     requests rather than only compiling. Run it with <c>dotnet run -- --smoke</c>.
/// </summary>
internal static class SmokeTest
{
	public static async Task<int> RunAsync(WebApplication app)
	{
		await app.StartAsync().ConfigureAwait(false);
		try
		{
			using HttpClient client = new()
			{
				BaseAddress = new Uri(app.Urls.First()),
			};
			List<string> failures = [];

			// The async-initialized singleton was warmed during startup, before any request arrived.
			await CheckAsync(client, "/menu", "\"warm\":true", failures).ConfigureAwait(false);
			await CheckAsync(client, "/menu", "Espresso", failures).ConfigureAwait(false);

			// Scope alignment and transient freshness, within one request.
			await CheckAsync(client, "/orders/Espresso", "\"sameScopePerRequest\":true", failures)
				.ConfigureAwait(false);
			await CheckAsync(client, "/orders/Espresso", "\"distinctTransients\":true", failures)
				.ConfigureAwait(false);

			// Keyed registrations reached through [FromKeyedServices].
			await CheckAsync(client, "/receipt/text/Latte", "Latte", failures).ConfigureAwait(false);
			await CheckAsync(client, "/receipt/json/Latte", "\"price\":3.80", failures).ConfigureAwait(false);

			await CheckScopeChangesPerRequestAsync(client, failures).ConfigureAwait(false);

			foreach (string failure in failures)
			{
				Console.Error.WriteLine(failure);
			}

			Console.WriteLine(failures.Count == 0
				? "smoke: all checks passed"
				: $"smoke: {failures.Count} check(s) failed");
			return failures.Count == 0 ? 0 : 1;
		}
		finally
		{
			await app.StopAsync().ConfigureAwait(false);
		}
	}

	private static async Task CheckAsync(HttpClient client, string path, string expected, List<string> failures)
	{
		HttpResponseMessage response = await client.GetAsync(path).ConfigureAwait(false);
		string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
		if (!response.IsSuccessStatusCode)
		{
			failures.Add($"GET {path} -> {(int)response.StatusCode}: {body}");
		}
		else if (!body.Contains(expected, StringComparison.Ordinal))
		{
			failures.Add($"GET {path} -> expected '{expected}' in: {body}");
		}
	}

	/// <summary>Two requests must land in two different Awaiten scopes, so their order ids differ.</summary>
	private static async Task CheckScopeChangesPerRequestAsync(HttpClient client, List<string> failures)
	{
		string first = await client.GetStringAsync("/orders/Espresso").ConfigureAwait(false);
		string second = await client.GetStringAsync("/orders/Espresso").ConfigureAwait(false);
		if (string.Equals(first, second, StringComparison.Ordinal))
		{
			failures.Add("two requests shared one scope: the order id did not change between them");
		}
	}
}
