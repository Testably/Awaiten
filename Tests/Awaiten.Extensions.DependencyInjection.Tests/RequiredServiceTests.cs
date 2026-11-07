using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection.Tests;

public sealed partial class RequiredServiceTests
{
	public interface IClock;

	public interface IUnknown;

	public sealed class Reporter
	{
		public Reporter(IClock clock) => Clock = clock;

		public IClock Clock { get; }
	}

	public sealed class KeyedReporter
	{
		public KeyedReporter([FromKey("utc")] IClock clock) => Clock = clock;

		public IClock Clock { get; }
	}

	public sealed class Menu;

	[Container]
	[ImportService<IClock>]
	[Transient<Reporter>]
	[Transient<KeyedReporter>]
	[Singleton<Menu>]
	public static partial class ImportContainer;

	[Container]
	[ImportServices]
	[Transient<Reporter>]
	public static partial class ImportServicesContainer;

	private sealed class EmptyResolver : IExternalResolver
	{
		public bool TryResolve(Type serviceType, object? key, [NotNullWhen(true)] out object? instance)
		{
			instance = null;
			return false;
		}
	}

	public sealed class FromTheRoot
	{
		[Test]
		public async Task ARegisteredType_ReturnsTheContainersInstance()
		{
			using ImportContainer.Root root = new(new EmptyResolver());
			using AwaitenServiceProvider provider = new(root, ownsContainer: false);

			await That(provider.GetRequiredService<Menu>()).IsSameAs(root.Resolve<Menu>());
		}

		[Test]
		public async Task AnImportTheHostLacks_ThrowsTheContainersGuidance()
		{
			using ImportContainer.Root root = new(new EmptyResolver());
			using AwaitenServiceProvider provider = new(root, ownsContainer: false);

			await That(() => provider.GetRequiredService<IClock>()).Throws<InvalidOperationException>()
				.WithMessage("*external dependency*IClock*is not available*register it in the host provider*").AsWildcard()
				.Because("the container routes the import to the host, so the error names that cause rather than claiming no registration");
		}

		[Test]
		public async Task AKeyedImportTheHostLacks_ThrowsTheContainersGuidance()
		{
			using ImportContainer.Root root = new(new EmptyResolver());
			using AwaitenServiceProvider provider = new(root, ownsContainer: false);

			await That(() => provider.GetRequiredKeyedService<IClock>("utc")).Throws<InvalidOperationException>()
				.WithMessage("*external dependency*IClock*(key: utc)*is not available*").AsWildcard()
				.Because("the keyed surface routes the import to the host too, so it names the same cause");
		}

		[Test]
		public async Task AnImportServicesMiss_ThrowsTheContainersGuidance()
		{
			using ImportServicesContainer.Root root = new(new EmptyResolver());
			using AwaitenServiceProvider provider = new(root, ownsContainer: false);

			await That(() => provider.GetRequiredService<IUnknown>()).Throws<InvalidOperationException>()
				.WithMessage("*external dependency*IUnknown*is not available*").AsWildcard()
				.Because("[ImportServices] routes every unregistered type to the host, so a miss is the host's");
		}

		[Test]
		public async Task AnUnknownType_ThrowsAnInvalidOperationExceptionNamingIt()
		{
			using ImportContainer.Root root = new(new EmptyResolver());
			using AwaitenServiceProvider provider = new(root, ownsContainer: false);

			await That(() => provider.GetRequiredService<IUnknown>()).Throws<InvalidOperationException>()
				.WithMessage("*IUnknown*").AsWildcard()
				.Because("MS.DI throws an InvalidOperationException for a missing required service, which callers catch");
		}

		[Test]
		public async Task AnUnknownKeyedType_ThrowsAnInvalidOperationExceptionNamingIt()
		{
			using ImportContainer.Root root = new(new EmptyResolver());
			using AwaitenServiceProvider provider = new(root, ownsContainer: false);

			await That(() => provider.GetRequiredKeyedService<Menu>("utc")).Throws<InvalidOperationException>()
				.WithMessage("*Menu*utc*").AsWildcard();
		}

		[Test]
		public async Task AnyKey_ThrowsAnInvalidOperationException()
		{
			using ImportContainer.Root root = new(new EmptyResolver());
			using AwaitenServiceProvider provider = new(root, ownsContainer: false);

			await That(() => provider.GetRequiredKeyedService<IClock>(KeyedService.AnyKey)).Throws<InvalidOperationException>()
				.Because("the provider declines AnyKey instead of passing it on to the host");
		}
	}

	public sealed class FromAScope
	{
		[Test]
		public async Task ARegisteredType_ReturnsTheContainersInstance()
		{
			using ImportContainer.Root root = new(new EmptyResolver());
			using AwaitenServiceProvider provider = new(root, ownsContainer: false);
			using IServiceScope scope = provider.CreateScope();

			await That(scope.ServiceProvider.GetRequiredService<Menu>()).IsSameAs(root.Resolve<Menu>());
		}

		[Test]
		public async Task AnImportTheHostLacks_ThrowsTheContainersGuidance()
		{
			using ImportContainer.Root root = new(new EmptyResolver());
			using AwaitenServiceProvider provider = new(root, ownsContainer: false);
			using IServiceScope scope = provider.CreateScope();

			await That(() => scope.ServiceProvider.GetRequiredService<IClock>()).Throws<InvalidOperationException>()
				.WithMessage("*external dependency*IClock*is not available*").AsWildcard()
				.Because("a scope provider adapts a child scope, which routes the import to the root's resolver");
		}

		[Test]
		public async Task AnImportServicesMiss_ThrowsTheContainersGuidance()
		{
			using ImportServicesContainer.Root root = new(new EmptyResolver());
			using AwaitenServiceProvider provider = new(root, ownsContainer: false);
			using IServiceScope scope = provider.CreateScope();

			await That(() => scope.ServiceProvider.GetRequiredService<IUnknown>()).Throws<InvalidOperationException>()
				.WithMessage("*external dependency*IUnknown*is not available*").AsWildcard();
		}

		[Test]
		public async Task AnUnknownType_ThrowsAnInvalidOperationExceptionNamingIt()
		{
			using ImportContainer.Root root = new(new EmptyResolver());
			using AwaitenServiceProvider provider = new(root, ownsContainer: false);
			using IServiceScope scope = provider.CreateScope();

			await That(() => scope.ServiceProvider.GetRequiredService<IUnknown>()).Throws<InvalidOperationException>()
				.WithMessage("*IUnknown*").AsWildcard();
		}
	}
}
