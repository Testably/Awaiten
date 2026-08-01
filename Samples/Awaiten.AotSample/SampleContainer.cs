using Awaiten.AotSample.Domain;

namespace Awaiten.AotSample;

[Container]
[ImportService<Banner>]
[Singleton<SystemClock, IClock>]
[Transient<Report>]
[Singleton<Warmup>]
public static partial class SampleContainer;
