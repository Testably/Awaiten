using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Awaiten.WebSample.Domain;

/// <summary>
///     An async-initialized singleton. Loading the price table is asynchronous, so the service is not usable
///     the moment it is constructed. <c>AddAwaitenInitialization</c> warms it during host startup, before the
///     first request is served, and <c>SyncResolveAfterInit</c> then lets ASP.NET resolve it synchronously
///     when binding a handler parameter.
/// </summary>
/// <remarks>
///     Its <see cref="IConfiguration" /> comes from the host, not from the Awaiten graph — see the
///     <c>[ImportService&lt;IConfiguration&gt;]</c> on <see cref="WebSample.CoffeeShop" />. The class itself carries no
///     Awaiten attribute; external-ness is declared on the container.
/// </remarks>
public sealed class PriceList : IAsyncInitializable
{
	private readonly IConfiguration _configuration;
	private readonly Dictionary<string, decimal> _prices = new(StringComparer.OrdinalIgnoreCase);

	public PriceList(IConfiguration configuration)
	{
		_configuration = configuration;
	}

	public bool IsWarm { get; private set; }

	public IReadOnlyCollection<string> Drinks => _prices.Keys;

	public async Task InitializeAsync(CancellationToken cancellationToken)
	{
		// Stands in for the real thing: reading a price table out of a store over the network.
		await Task.Delay(25, cancellationToken).ConfigureAwait(false);

		foreach (IConfigurationSection section in _configuration.GetSection("Menu").GetChildren())
		{
			_prices[section.Key] = decimal.Parse(section.Value!, CultureInfo.InvariantCulture);
		}

		IsWarm = true;
	}

	public decimal PriceOf(string drink)
		=> _prices.TryGetValue(drink, out decimal price)
			? price
			: throw new KeyNotFoundException($"'{drink}' is not on the menu.");
}
