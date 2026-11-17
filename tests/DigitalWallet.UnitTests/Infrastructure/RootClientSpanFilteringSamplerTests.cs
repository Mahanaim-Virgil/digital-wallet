using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;

namespace DigitalWallet.UnitTests.Infrastructure;

public sealed class RootClientSpanFilteringSamplerTests
{
    private readonly RootClientSpanFilteringSampler _sampler = new();

    private static SamplingParameters Params(ActivityContext parent, ActivityKind kind) =>
        new(parent, ActivityTraceId.CreateRandom(), "postgresql", kind);

    private static ActivityContext SampledParent() =>
        new(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded);

    [Fact]
    public void Root_client_span_is_dropped() =>
        Assert.Equal(SamplingDecision.Drop, _sampler.ShouldSample(Params(default, ActivityKind.Client)).Decision);

    [Fact]
    public void Client_span_with_parent_is_recorded() =>
        Assert.Equal(SamplingDecision.RecordAndSample, _sampler.ShouldSample(Params(SampledParent(), ActivityKind.Client)).Decision);

    [Theory]
    [InlineData(ActivityKind.Server)]
    [InlineData(ActivityKind.Internal)]
    [InlineData(ActivityKind.Producer)]
    public void Root_non_client_spans_are_recorded(ActivityKind kind) =>
        Assert.Equal(SamplingDecision.RecordAndSample, _sampler.ShouldSample(Params(default, kind)).Decision);
}
