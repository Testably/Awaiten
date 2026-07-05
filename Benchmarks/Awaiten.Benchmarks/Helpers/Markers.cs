namespace Awaiten.Benchmarks.Helpers;

internal static class Markers
{
	/// <summary>
	///     The first <paramref name="size" /> B0..B255 markers in registration order, matching the types
	///     each container of that size registers.
	/// </summary>
	public static Type[] ServiceTypes(int size)
	{
		Type[] types = new Type[size];
		for (int i = 0; i < size; i++)
		{
			types[i] = Type.GetType($"Awaiten.Benchmarks.Helpers.B{i}", true)!;
		}

		return types;
	}
}
