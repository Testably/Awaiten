using System;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     Adapts a Microsoft.Extensions.DependencyInjection <see cref="IServiceProvider" /> to Awaiten's
///     <see cref="IExternalResolver" /> seam, so a generated container can resolve its
///     <c>[FromServices]</c> / <c>[ImportServices]</c> dependencies from the host's provider.
/// </summary>
public sealed class ServiceProviderExternalResolver : IExternalResolver
{
	private readonly IServiceProvider _provider;

	/// <summary>
	///     Initializes a new instance of the <see cref="ServiceProviderExternalResolver" /> class.
	/// </summary>
	/// <param name="provider">The provider to resolve external dependencies from.</param>
	public ServiceProviderExternalResolver(IServiceProvider provider)
		=> _provider = provider ?? throw new ArgumentNullException(nameof(provider));

	/// <inheritdoc />
	public bool TryResolve(Type serviceType, out object? instance)
	{
		if (serviceType is null)
		{
			throw new ArgumentNullException(nameof(serviceType));
		}

		instance = _provider.GetService(serviceType);
		return instance is not null;
	}
}
