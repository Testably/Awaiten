using System.Threading;
using System.Threading.Tasks;

namespace Awaiten.Tests.Support;

/// <summary>The service a consumer in another assembly takes as a <c>Task&lt;T&gt;</c>.</summary>
public interface ITaskStore
{
	/// <summary>Whether the module's async initializer ran.</summary>
	bool Initialized { get; }
}

/// <summary>The convention the module initializes its store through.</summary>
public interface ITaskStoreStartable
{
	/// <summary>Initializes the store.</summary>
	Task StartAsync(CancellationToken cancellationToken);
}

/// <summary>A public implementation the module registers under <see cref="ITaskStore" /> and initializes asynchronously.</summary>
public sealed class TaskStore : ITaskStore, ITaskStoreStartable
{
	/// <inheritdoc />
	public bool Initialized { get; private set; }

	/// <inheritdoc />
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		await Task.Yield();
		Initialized = true;
	}
}

/// <summary>
///     A <c>[Module]</c> that registers an async-initialized public implementation under a service it implements, so a
///     container in another assembly adapts the implementation's task to the task of the service.
/// </summary>
[Module]
[AsyncInitializer<ITaskStoreStartable>(nameof(ITaskStoreStartable.StartAsync))]
[Scoped<TaskStore, ITaskStore>]
public static class CrossAssemblyTaskOfServiceModule;
