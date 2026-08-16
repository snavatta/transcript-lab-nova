using System.Text.Json;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Transcription;
using FluentAssertions;

namespace ClassTranscriber.Api.Tests;

public sealed class HostedProcessingContractTests
{
    [Fact]
    public void HybridHostedMetadataRoundTripsDistinctSttAndDiarizationFields()
    {
        var metadata = new HostedProcessingMetadataDto
        {
            SttProvider = "OpenRouter",
            SttModel = "openai/whisper-large-v3",
            RequestCount = 3,
            NativeDiarizationUsed = false,
            DiarizationSource = "Xai",
            DiarizationProvider = "xAI",
            DiarizationModel = "grok-stt-1.0",
            DiarizationRequestCount = 1,
            DiarizationCostUsd = 0.10m,
            DiarizationRateUsdPerHour = 0.10m,
            DiarizationCostClassification = "Estimated",
            TotalContainsEstimate = true,
        };

        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("requestCount").GetInt32().Should().Be(3);
        root.GetProperty("diarizationRequestCount").GetInt32().Should().Be(1);
        root.GetProperty("diarizationSource").GetString().Should().Be("Xai");
        root.GetProperty("diarizationProvider").GetString().Should().Be("xAI");
        root.GetProperty("diarizationModel").GetString().Should().Be("grok-stt-1.0");
        root.GetProperty("diarizationCostUsd").GetDecimal().Should().Be(0.10m);
        root.GetProperty("diarizationRateUsdPerHour").GetDecimal().Should().Be(0.10m);
        root.GetProperty("diarizationCostClassification").GetString().Should().Be("Estimated");
    }

    [Fact]
    public void InternalProcessingMetadataKeepsDiarizationMicroUsdIndependent()
    {
        var metadata = new TranscriptionProcessingMetadata(
            "OpenRouter",
            "openai/whisper-large-v3",
            3,
            false,
            DiarizationSource: "Xai",
            DiarizationProvider: "xAI",
            DiarizationModel: "grok-stt-1.0",
            DiarizationRequestCount: 1,
            DiarizationCostMicroUsd: 100_000,
            DiarizationRateMicroUsdPerHour: 100_000,
            DiarizationCostClassification: "Estimated");

        metadata.RequestCount.Should().Be(3);
        metadata.DiarizationRequestCount.Should().Be(1);
        metadata.DiarizationCostMicroUsd.Should().Be(100_000);
    }
}
