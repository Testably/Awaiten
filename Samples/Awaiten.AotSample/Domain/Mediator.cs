using System;
using System.Collections.Generic;

namespace Awaiten.AotSample.Domain;

public interface IMessageHandler
{
	string Handle(object message);
}

public interface IMessageHandler<T> : IMessageHandler;

public sealed class Ping;

public sealed class PingHandler : IMessageHandler<Ping>
{
	public string Handle(object message) => "pong";
}

/// <summary>
///     Dispatches a message by its runtime type through the generated dispatch dictionary, which holds one
///     <c>Func</c> per scanned closing of <see cref="IMessageHandler{T}" />.
/// </summary>
public sealed class Mediator
{
	private readonly IReadOnlyDictionary<Type, Func<IMessageHandler>> _handlers;

	public Mediator(IReadOnlyDictionary<Type, Func<IMessageHandler>> handlers) => _handlers = handlers;

	public string Send(object message) => _handlers[message.GetType()]().Handle(message);
}
