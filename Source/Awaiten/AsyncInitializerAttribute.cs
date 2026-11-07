using System;

namespace Awaiten;

// S2326: the type parameter is the source generator's input. It reads the convention type from the attribute's type
// argument via Roslyn, so the body never references it.
#pragma warning disable S2326

/// <summary>
///     Declares an async initializer convention on a <see cref="ContainerAttribute">container</see> or a
///     <see cref="ModuleAttribute">module</see>: every service of that container or module whose implementation is
///     <typeparamref name="T" /> (implements the interface or derives from the class) is initialized by awaiting its
///     <see cref="Method" /> once, after construction and before it is handed out. The service is async-initialized,
///     so it can only be obtained asynchronously, and the domain type never references Awaiten.
/// </summary>
/// <remarks>
///     <para>
///         The method is an instance method of <typeparamref name="T" /> that returns <c>Task</c> or <c>ValueTask</c>.
///         It is called through <typeparamref name="T" />, so an explicit interface implementation or an override runs.
///         Its parameters are dependencies of the service and are resolved from the graph like the parameters of an
///         <c>OnActivated</c> hook; a <c>CancellationToken</c> receives the token passed to <c>ResolveAsync</c>,
///         <c>InitializeAsync</c> or <c>CreateScopeAsync</c>.
///     </para>
///     <para>
///         A registration is judged by the conventions of the container or module that declares it: a module's
///         conventions apply to its own registrations, and a container's to its own. A container that extends a base
///         inherits the base's conventions. The <c>OnActivated</c> hook runs first, then each matching convention in
///         declaration order, and a method reachable through several conventions runs once.
///     </para>
/// </remarks>
/// <typeparam name="T">The interface or class whose implementations the convention initializes.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class AsyncInitializerAttribute<T> : Attribute
	where T : class
{
	/// <summary>Declares that <paramref name="method" /> of <typeparamref name="T" /> initializes its implementations.</summary>
	/// <param name="method">
	///     The name of the instance method of <typeparamref name="T" /> that initializes an implementation, best given
	///     with <c>nameof</c>. It must not be overloaded.
	/// </param>
	public AsyncInitializerAttribute(string method)
	{
		Method = method;
	}

	/// <summary>The name of the method of <typeparamref name="T" /> that initializes an implementation.</summary>
	public string Method { get; }
}

#pragma warning restore S2326
