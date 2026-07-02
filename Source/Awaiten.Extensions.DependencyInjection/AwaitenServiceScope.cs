using System;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     An <see cref="IServiceScope" /> backed by an <see cref="IAwaitenScope" />. Its
///     <see cref="ServiceProvider" /> resolves from the scope; disposing it disposes the underlying
///     Awaiten scope (and the scoped instances and disposable transients it owns).
/// </summary>
internal sealed class AwaitenServiceScope : IServiceScope
{
	private readonly IAwaitenScope _scope;

	public AwaitenServiceScope(IAwaitenScope scope)
	{
		_scope = scope;
		ServiceProvider = new AwaitenScopeServiceProvider(scope);
	}

	public IServiceProvider ServiceProvider { get; }

	public void Dispose() => _scope.Dispose();

	private sealed class AwaitenScopeServiceProvider : IServiceProvider
	{
		private readonly IAwaitenScope _scope;

		public AwaitenScopeServiceProvider(IAwaitenScope scope) => _scope = scope;

		public object? GetService(Type serviceType)
		{
			if (serviceType is null)
			{
				throw new ArgumentNullException(nameof(serviceType));
			}

			if (serviceType == typeof(IServiceProvider))
			{
				return this;
			}

			return _scope.TryResolve(serviceType, out object? instance) ? instance : null;
		}
	}
}
