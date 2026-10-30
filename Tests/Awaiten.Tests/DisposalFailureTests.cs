using System.Collections.Generic;
using System.IO;

namespace Awaiten.Tests;

/// <summary>
///     Teardown keeps going past failures: when an <c>OnRelease</c> hook or an instance's own disposal throws,
///     every other queued release and tracked instance still gets its teardown, and the failures surface once
///     at the end (a single one unchanged, several as one <see cref="AggregateException" /> in teardown order).
///     Each test runs against the root and against a child scope; the probes report into a per-container
///     <see cref="Recorder" /> singleton.
/// </summary>
public partial class DisposalFailureTests
{
	public class SynchronousDispose
	{
		[Test]
		[Arguments(true)]
		[Arguments(false)]
		public async Task WhenAReleaseHookThrows_DisposesEveryInstanceAndRethrowsTheHookFailure(bool onRoot)
		{
			using ReleaseFailureContainer.Root root = new();
			Recorder recorder = root.Resolve<Recorder>();
			ReleaseFailureContainer.Scope owner = onRoot ? root : root.CreateScope();
			owner.Resolve<First>();
			owner.Resolve<FlushedSecond>();
			owner.Resolve<Third>();

			IOException thrown = await That(() => owner.Dispose()).Throws<IOException>();

			await That(thrown).IsSameAs(recorder.Failures[0])
				.Because("a single teardown failure is rethrown unchanged");
			await That(recorder.Log).IsEqualTo(new[] { "released:FlushedSecond", "disposed:Third", "disposed:FlushedSecond", "disposed:First" })
				.Because("a throwing release hook skips no disposal, not even of its own instance");
		}

		[Test]
		[Arguments(true)]
		[Arguments(false)]
		public async Task WhenAMiddleInstanceThrows_DisposesTheOthersInReverseOrderAndRethrowsTheFailure(bool onRoot)
		{
			using DisposeFailureContainer.Root root = new();
			Recorder recorder = root.Resolve<Recorder>();
			DisposeFailureContainer.Scope owner = onRoot ? root : root.CreateScope();
			owner.Resolve<First>();
			owner.Resolve<FaultySecond>();
			owner.Resolve<Third>();

			InvalidOperationException thrown = await That(() => owner.Dispose()).Throws<InvalidOperationException>();

			await That(thrown).IsSameAs(recorder.Failures[0])
				.Because("a single teardown failure is rethrown unchanged");
			await That(recorder.Log).IsEqualTo(new[] { "disposed:Third", "failed:FaultySecond", "disposed:First" })
				.Because("an instance created before the failing one is still disposed");
		}

		[Test]
		[Arguments(true)]
		[Arguments(false)]
		public async Task WhenSeveralTeardownsThrow_ThrowsOneAggregateExceptionInTeardownOrder(bool onRoot)
		{
			using SeveralFailuresContainer.Root root = new();
			Recorder recorder = root.Resolve<Recorder>();
			SeveralFailuresContainer.Scope owner = onRoot ? root : root.CreateScope();
			owner.Resolve<FaultyFirst>();
			owner.Resolve<Second>();
			owner.Resolve<FaultyThird>();

			AggregateException thrown = await That(() => owner.Dispose()).Throws<AggregateException>();

			await That(thrown.InnerExceptions).IsEqualTo(recorder.Failures)
				.Because("every failure is reported, in teardown order");
			await That(recorder.Log).IsEqualTo(new[] { "failed:FaultyThird", "disposed:Second", "failed:FaultyFirst" });
		}

		[Test]
		[Arguments(true)]
		[Arguments(false)]
		public async Task AfterAFailedDispose_IsANoOp(bool onRoot)
		{
			using DisposeFailureContainer.Root root = new();
			Recorder recorder = root.Resolve<Recorder>();
			DisposeFailureContainer.Scope owner = onRoot ? root : root.CreateScope();
			owner.Resolve<First>();
			owner.Resolve<FaultySecond>();
			owner.Resolve<Third>();
			await That(() => owner.Dispose()).Throws<InvalidOperationException>();

			await That(() => owner.Dispose()).DoesNotThrow();

			await That(recorder.Log).IsEqualTo(new[] { "disposed:Third", "failed:FaultySecond", "disposed:First" })
				.Because("the first Dispose already gave every instance its teardown attempt");
		}

		[Test]
		[Arguments(true)]
		[Arguments(false)]
		public async Task WithAnAsyncOnlyDisposable_DisposesEveryOtherInstanceAndThrowsTheGuidance(bool onRoot)
		{
#pragma warning disable AWT156
			using AsyncOnlyContainer.Root root = new();
#pragma warning restore AWT156
			Recorder recorder = root.Resolve<Recorder>();
			AsyncOnlyContainer.Scope owner = onRoot ? root : root.CreateScope();
			owner.Resolve<First>();
			owner.Resolve<AsyncOnlySecond>();
			owner.Resolve<Third>();

#pragma warning disable AWT156
			await That(() => owner.Dispose()).Throws<InvalidOperationException>()
				.WithMessage("*requires asynchronous disposal*DisposeAsync*").AsWildcard();
#pragma warning restore AWT156

			await That(recorder.Log).IsEqualTo(new[] { "disposed:Third", "disposed:First" })
				.Because("an async-only instance stays undisposed on the synchronous path, but no other instance is skipped");
		}
	}

	public class AsynchronousDispose
	{
		[Test]
		[Arguments(true)]
		[Arguments(false)]
		public async Task WhenAReleaseHookThrows_DisposesEveryInstanceAndRethrowsTheHookFailure(bool onRoot)
		{
			await using ReleaseFailureContainer.Root root = new();
			Recorder recorder = root.Resolve<Recorder>();
			ReleaseFailureContainer.Scope owner = onRoot ? root : root.CreateScope();
			owner.Resolve<First>();
			owner.Resolve<FlushedSecond>();
			owner.Resolve<Third>();

			IOException thrown = await That(async () => await owner.DisposeAsync()).Throws<IOException>();

			await That(thrown).IsSameAs(recorder.Failures[0])
				.Because("a single teardown failure is rethrown unchanged");
			await That(recorder.Log).IsEqualTo(new[] { "released:FlushedSecond", "disposed:Third", "disposed:FlushedSecond", "disposed:First" })
				.Because("a throwing release hook skips no disposal, not even of its own instance");
		}

		[Test]
		[Arguments(true)]
		[Arguments(false)]
		public async Task WhenAMiddleInstanceThrows_DisposesTheOthersInReverseOrderAndRethrowsTheFailure(bool onRoot)
		{
			await using DisposeFailureContainer.Root root = new();
			Recorder recorder = root.Resolve<Recorder>();
			DisposeFailureContainer.Scope owner = onRoot ? root : root.CreateScope();
			owner.Resolve<First>();
			owner.Resolve<FaultySecond>();
			owner.Resolve<Third>();

			InvalidOperationException thrown = await That(async () => await owner.DisposeAsync()).Throws<InvalidOperationException>();

			await That(thrown).IsSameAs(recorder.Failures[0])
				.Because("a single teardown failure is rethrown unchanged");
			await That(recorder.Log).IsEqualTo(new[] { "disposed:Third", "failedAsync:FaultySecond", "disposed:First" })
				.Because("an instance created before the failing one is still disposed");
		}

		[Test]
		[Arguments(true)]
		[Arguments(false)]
		public async Task WhenSeveralTeardownsThrow_ThrowsOneAggregateExceptionInTeardownOrder(bool onRoot)
		{
			await using SeveralFailuresContainer.Root root = new();
			Recorder recorder = root.Resolve<Recorder>();
			SeveralFailuresContainer.Scope owner = onRoot ? root : root.CreateScope();
			owner.Resolve<FaultyFirst>();
			owner.Resolve<Second>();
			owner.Resolve<FaultyThird>();

			AggregateException thrown = await That(async () => await owner.DisposeAsync()).Throws<AggregateException>();

			await That(thrown.InnerExceptions).IsEqualTo(recorder.Failures)
				.Because("every failure is reported, in teardown order");
			await That(recorder.Log).IsEqualTo(new[] { "failedAsync:FaultyThird", "disposed:Second", "failedAsync:FaultyFirst" });
		}

		[Test]
		[Arguments(true)]
		[Arguments(false)]
		public async Task AfterAFailedDisposeAsync_IsANoOp(bool onRoot)
		{
			await using DisposeFailureContainer.Root root = new();
			Recorder recorder = root.Resolve<Recorder>();
			DisposeFailureContainer.Scope owner = onRoot ? root : root.CreateScope();
			owner.Resolve<First>();
			owner.Resolve<FaultySecond>();
			owner.Resolve<Third>();
			await That(async () => await owner.DisposeAsync()).Throws<InvalidOperationException>();

			await That(async () => await owner.DisposeAsync()).DoesNotThrow();

			await That(recorder.Log).IsEqualTo(new[] { "disposed:Third", "failedAsync:FaultySecond", "disposed:First" })
				.Because("the first DisposeAsync already gave every instance its teardown attempt");
		}
	}

	public sealed class Recorder
	{
		public List<string> Log { get; } = [];

		public List<Exception> Failures { get; } = [];

		public Exception Fail(string entry, Exception failure)
		{
			Log.Add(entry);
			Failures.Add(failure);
			return failure;
		}
	}

	public class Part(Recorder recorder) : IDisposable
	{
		public Recorder Recorder { get; } = recorder;

		public void Dispose()
		{
			Recorder.Log.Add("disposed:" + GetType().Name);
			GC.SuppressFinalize(this);
		}
	}

	public sealed class First(Recorder recorder) : Part(recorder);

	public sealed class Second(Recorder recorder) : Part(recorder);

	public sealed class Third(Recorder recorder) : Part(recorder);

	public sealed class FlushedSecond(Recorder recorder) : Part(recorder);

	public class FaultyPart(Recorder recorder) : IDisposable
		, IAsyncDisposable
	{
		public void Dispose()
		{
			GC.SuppressFinalize(this);
			throw recorder.Fail("failed:" + GetType().Name, new InvalidOperationException(GetType().Name + " failed"));
		}

		public ValueTask DisposeAsync()
		{
			GC.SuppressFinalize(this);
			throw recorder.Fail("failedAsync:" + GetType().Name, new InvalidOperationException(GetType().Name + " failed"));
		}
	}

	public sealed class FaultyFirst(Recorder recorder) : FaultyPart(recorder);

	public sealed class FaultySecond(Recorder recorder) : FaultyPart(recorder);

	public sealed class FaultyThird(Recorder recorder) : FaultyPart(recorder);

	[Container]
	[Singleton<Recorder>]
	[Scoped<First>]
	[Scoped<FlushedSecond>(OnRelease = nameof(Flush))]
	[Scoped<Third>]
	public static partial class ReleaseFailureContainer
	{
		private static void Flush(FlushedSecond second)
			=> throw second.Recorder.Fail("released:FlushedSecond", new IOException("disk full"));
	}

	[Container]
	[Singleton<Recorder>]
	[Scoped<First>]
	[Scoped<FaultySecond>]
	[Scoped<Third>]
	public static partial class DisposeFailureContainer;

	[Container]
	[Singleton<Recorder>]
	[Scoped<FaultyFirst>]
	[Scoped<Second>]
	[Scoped<FaultyThird>]
	public static partial class SeveralFailuresContainer;

	public sealed class AsyncOnlySecond : IAsyncDisposable
	{
		public ValueTask DisposeAsync() => default;
	}

	[Container]
	[Singleton<Recorder>]
	[Scoped<First>]
	[Scoped<AsyncOnlySecond>]
	[Scoped<Third>]
	public static partial class AsyncOnlyContainer;
}
