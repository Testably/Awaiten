using Awaiten.AotSample.Domain;

namespace Awaiten.AotSample;

[Container]
[AsyncInitializer<IWarmUp>(nameof(IWarmUp.WarmUpAsync))]
[ImportService<Banner>]
[Singleton<SystemClock, IClock>]
[Transient<Report>]
[Singleton<Warmup>]
[Scan(typeof(IMessageHandler<>), As = ScanAs.Marker)]
[Transient<Mediator>]
public static partial class SampleContainer;
