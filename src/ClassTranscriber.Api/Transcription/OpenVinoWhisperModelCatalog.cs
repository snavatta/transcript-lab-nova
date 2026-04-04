namespace ClassTranscriber.Api.Transcription;

public sealed record OpenVinoWhisperModelDefinition(
    string Model,
    string Repository,
    IReadOnlyCollection<string> RequiredFiles);

public static class OpenVinoWhisperModelCatalog
{
    // INT8 models include openvino_config.json (quantization parameters).
    // FP16 models do not publish that file.
    private static readonly string[] CommonRequiredFilesInt8 =
    [
        "config.json",
        "generation_config.json",
        "openvino_config.json",
        "openvino_encoder_model.xml",
        "openvino_encoder_model.bin",
        "openvino_decoder_model.xml",
        "openvino_decoder_model.bin",
        "openvino_tokenizer.xml",
        "openvino_tokenizer.bin",
        "openvino_detokenizer.xml",
        "openvino_detokenizer.bin",
        "preprocessor_config.json",
        "tokenizer.json",
        "tokenizer_config.json",
        "special_tokens_map.json",
        "merges.txt",
        "vocab.json",
    ];

    private static readonly string[] CommonRequiredFilesFp16 =
    [
        "config.json",
        "generation_config.json",
        "openvino_encoder_model.xml",
        "openvino_encoder_model.bin",
        "openvino_decoder_model.xml",
        "openvino_decoder_model.bin",
        "openvino_tokenizer.xml",
        "openvino_tokenizer.bin",
        "openvino_detokenizer.xml",
        "openvino_detokenizer.bin",
        "preprocessor_config.json",
        "tokenizer.json",
        "tokenizer_config.json",
        "special_tokens_map.json",
        "merges.txt",
        "vocab.json",
    ];

    private static readonly IReadOnlyDictionary<string, OpenVinoWhisperModelDefinition> Definitions =
        new Dictionary<string, OpenVinoWhisperModelDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["tiny-int8"] = new(
                "tiny-int8",
                "OpenVINO/whisper-tiny-int8-ov",
                CommonRequiredFilesInt8),
            ["tiny-fp16"] = new(
                "tiny-fp16",
                "OpenVINO/whisper-tiny-fp16-ov",
                CommonRequiredFilesFp16),
            ["base-int8"] = new(
                "base-int8",
                "OpenVINO/whisper-base-int8-ov",
                CommonRequiredFilesInt8),
            ["base-fp16"] = new(
                "base-fp16",
                "OpenVINO/whisper-base-fp16-ov",
                CommonRequiredFilesFp16),
            ["small-int8"] = new(
                "small-int8",
                "OpenVINO/whisper-small-int8-ov",
                CommonRequiredFilesInt8),
            ["small-fp16"] = new(
                "small-fp16",
                "OpenVINO/whisper-small-fp16-ov",
                CommonRequiredFilesFp16),
            ["medium-int8"] = new(
                "medium-int8",
                "OpenVINO/whisper-medium-int8-ov",
                CommonRequiredFilesInt8),
            ["medium-fp16"] = new(
                "medium-fp16",
                "OpenVINO/whisper-medium-fp16-ov",
                CommonRequiredFilesFp16),
            ["large-v3-int8"] = new(
                "large-v3-int8",
                "OpenVINO/whisper-large-v3-int8-ov",
                CommonRequiredFilesInt8),
            ["large-v3-fp16"] = new(
                "large-v3-fp16",
                "OpenVINO/whisper-large-v3-fp16-ov",
                CommonRequiredFilesFp16),
        };

    public static IReadOnlyCollection<string> SupportedModels => Definitions.Keys.ToArray();

    public static bool TryGet(string model, out OpenVinoWhisperModelDefinition definition)
        => Definitions.TryGetValue(model, out definition!);

    public static OpenVinoWhisperModelDefinition GetRequired(string model)
        => TryGet(model, out var definition)
            ? definition
            : throw new InvalidOperationException($"Unsupported OpenVINO Whisper model '{model}'.");
}

public static class OpenVinoWhisperModelAssets
{
    public static string GetModelDirectory(string modelsPath, string model)
    {
        ValidateModelName(model);
        var modelsRoot = Path.GetFullPath(modelsPath);
        Directory.CreateDirectory(modelsRoot);
        return Path.GetFullPath(Path.Combine(modelsRoot, model));
    }

    public static void ValidateInstalledModel(string installPath, OpenVinoWhisperModelDefinition definition)
    {
        if (!Directory.Exists(installPath))
            throw new DirectoryNotFoundException($"OpenVINO Whisper model directory not found at {installPath}.");

        foreach (var relativePath in definition.RequiredFiles)
        {
            var fullPath = Path.Combine(installPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"OpenVINO Whisper model asset not found at {fullPath}.");
        }
    }

    private static void ValidateModelName(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model name is required.", nameof(model));

        foreach (var ch in model)
        {
            if (!char.IsLetterOrDigit(ch) && ch is not '-' and not '_' and not '.')
                throw new InvalidOperationException($"Invalid OpenVINO Whisper model name '{model}'.");
        }
    }
}