namespace Snippets;

// Uses the contravariant IHandler<in T> that the page declares.
public sealed class OrderConsumer(IHandler<OrderPlaced> handler);
