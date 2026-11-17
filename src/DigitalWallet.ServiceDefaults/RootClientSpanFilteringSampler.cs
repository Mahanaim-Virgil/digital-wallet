using System.Diagnostics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Ebeveyni olmayan <see cref="ActivityKind.Client"/> span'lerini kaydetmez (karar 7).
/// Arka plan döngülerinin (ör. outbox yayıncısının saniyede bir yoklama sorgusu) her sorgu için ayrı
/// kök iz üretip dashboard'u gürültüyle doldurmasını engeller. Bir isteğin veya mesajın içindeki
/// sorgular ebeveynli olduğu için etkilenmez. Geri kalanı varsayılanla aynı: ParentBased(AlwaysOn).
/// </summary>
public sealed class RootClientSpanFilteringSampler : Sampler
{
    private readonly Sampler _inner = new ParentBasedSampler(new AlwaysOnSampler());

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        if (samplingParameters.ParentContext.TraceId == default && samplingParameters.Kind == ActivityKind.Client)
            return new SamplingResult(SamplingDecision.Drop);

        return _inner.ShouldSample(samplingParameters);
    }
}
