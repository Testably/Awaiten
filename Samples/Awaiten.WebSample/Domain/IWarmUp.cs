using System.Threading;
using System.Threading.Tasks;

namespace Awaiten.WebSample.Domain;

/// <summary>A domain interface for services that warm up asynchronously before use.</summary>
public interface IWarmUp
{
	Task WarmUpAsync(CancellationToken cancellationToken);
}
