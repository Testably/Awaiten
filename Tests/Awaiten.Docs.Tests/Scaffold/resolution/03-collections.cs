using System.Collections;

namespace Scaffold;

public sealed class SyrupBundle : IEnumerable<ISyrup>
{
	public IEnumerator<ISyrup> GetEnumerator() => Enumerable.Empty<ISyrup>().GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
