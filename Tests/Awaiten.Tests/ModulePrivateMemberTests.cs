using Awaiten.Tests.ContainerSupport;
using Awaiten.Tests.Support;

namespace Awaiten.Tests;

/// <summary>
///     Runtime behavior of internal services a <c>[Module]</c> produces through private <c>Factory</c> and
///     <c>Instance</c> members. A container in another assembly takes them through their handles, a container in the
///     module's own assembly or in one granted <c>InternalsVisibleTo</c> builds them itself through internal wrappers of
///     the members, and each shares, hooks, initializes and disposes them alike.
/// </summary>
public partial class ModulePrivateMemberTests
{
	[Test]
	public async Task ContainerInAnotherAssembly_SharesHooksInitializesAndDisposesThem()
	{
		IPrivateMemberBarista first;
		IPrivateMemberBarista second;
		await using (PrivateMemberContainer.Root container = new())
		{
			await using (IAwaitenScope scope = await container.CreateScopeAsync())
			{
				first = await scope.ResolveAsync<IPrivateMemberBarista>();
			}

			await using (IAwaitenScope scope = await container.CreateScopeAsync())
			{
				second = await scope.ResolveAsync<IPrivateMemberBarista>();
			}

			await That(second.Kettle).IsSameAs(first.Kettle)
				.Because("the singleton the private async factory produces is one instance");
			await That(first.Cup.Kettle).IsSameAs(first.Kettle)
				.Because("the private factory of the cup takes the singleton kettle");
			await That(second.Cup).IsNotSameAs(first.Cup)
				.Because("each scope gets its own cup");
			await That(first.OtherScale).IsNotSameAs(first.Scale)
				.Because("each dependency gets its own transient scale");
			await That(second.Shelf).IsSameAs(first.Shelf);
			await That(first.Kettle.Activated && first.Kettle.Started).IsTrue()
				.Because("the module's hook and convention run on the instance the private factory produces");
			await That(first.Cup.Disposed && first.Scale.Disposed && first.OtherScale.Disposed).IsTrue()
				.Because("the scope disposes the scoped and transient services it built");
			await That(first.Kettle.Released || first.Kettle.Disposed).IsFalse();
		}

		await That(first.Kettle.Released && first.Kettle.Disposed).IsTrue()
			.Because("the container releases and disposes the singleton the private factory produced");
		await That(first.Shelf.Disposed).IsFalse()
			.Because("the container never disposes an Instance");
	}

	[Test]
	public async Task ContainerInAnAssemblyGrantedInternalsVisibleTo_SharesHooksInitializesAndDisposesThem()
	{
		IFriendPrivateMemberBarista first;
		IFriendPrivateMemberBarista second;
		await using (FriendPrivateMemberContainer.Root container = new())
		{
			await using (IAwaitenScope scope = await container.CreateScopeAsync())
			{
				first = await scope.ResolveAsync<IFriendPrivateMemberBarista>();
			}

			await using (IAwaitenScope scope = await container.CreateScopeAsync())
			{
				second = await scope.ResolveAsync<IFriendPrivateMemberBarista>();
			}

			await That(second.Kettle).IsSameAs(first.Kettle)
				.Because("the singleton the internal wrapper of the private async factory produces is one instance");
			await That(first.Cup.Kettle).IsSameAs(first.Kettle);
			await That(second.Cup).IsNotSameAs(first.Cup);
			await That(first.OtherScale).IsNotSameAs(first.Scale);
			await That(second.Shelf).IsSameAs(first.Shelf);
			await That(first.Kettle.Activated && first.Kettle.Started).IsTrue()
				.Because("the container calls the module's internal hook and runs the convention itself");
			await That(first.Cup.Disposed && first.Scale.Disposed && first.OtherScale.Disposed).IsTrue();
			await That(first.Kettle.Released || first.Kettle.Disposed).IsFalse();
		}

		await That(first.Kettle.Released && first.Kettle.Disposed).IsTrue();
		await That(first.Shelf.Disposed).IsFalse()
			.Because("the container never disposes an Instance");
	}

	[Test]
	public async Task ContainerInTheModulesAssembly_SharesHooksInitializesAndDisposesThem()
	{
		IFriendPrivateMemberBarista first;
		IFriendPrivateMemberBarista second;
		await using (FriendPrivateMemberLocalContainer.Root container = new())
		{
			await using (IAwaitenScope scope = await container.CreateScopeAsync())
			{
				first = await scope.ResolveAsync<IFriendPrivateMemberBarista>();
			}

			await using (IAwaitenScope scope = await container.CreateScopeAsync())
			{
				second = await scope.ResolveAsync<IFriendPrivateMemberBarista>();
			}

			await That(second.Kettle).IsSameAs(first.Kettle)
				.Because("the singleton the internal wrapper of the private async factory produces is one instance");
			await That(first.Cup.Kettle).IsSameAs(first.Kettle);
			await That(second.Cup).IsNotSameAs(first.Cup);
			await That(first.OtherScale).IsNotSameAs(first.Scale);
			await That(second.Shelf).IsSameAs(first.Shelf);
			await That(first.Kettle.Activated && first.Kettle.Started).IsTrue();
			await That(first.Cup.Disposed && first.Scale.Disposed && first.OtherScale.Disposed).IsTrue();
			await That(first.Kettle.Released || first.Kettle.Disposed).IsFalse();
		}

		await That(first.Kettle.Released && first.Kettle.Disposed).IsTrue();
		await That(first.Shelf.Disposed).IsFalse()
			.Because("the container never disposes an Instance");
	}

	[Container]
	[Import(typeof(CrossAssemblyPrivateMemberModule))]
	public static partial class PrivateMemberContainer;

	[Container]
	[Import(typeof(SupportFriendPrivateMemberModule))]
	public static partial class FriendPrivateMemberContainer;
}
