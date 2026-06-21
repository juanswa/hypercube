using System.Text;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace Hypercube.AI.Onnx;

/// <summary>In-process ONNX Runtime GenAI text generator. Loads one model for the process.</summary>
public sealed class OnnxTextGenerator : IDisposable
{
    private readonly OgaHandle _oga = new();
    private readonly Model _model;
    private readonly Tokenizer _tokenizer;
    private readonly object _gate = new();
    private readonly int _maxLength;

    public OnnxTextGenerator(string modelDirectory, int maxLength = 1536)
    {
        if (!Directory.Exists(modelDirectory))
        {
            throw new DirectoryNotFoundException($"ONNX model directory not found: {modelDirectory}");
        }

        _model = new Model(modelDirectory);
        _tokenizer = new Tokenizer(_model);
        _maxLength = maxLength;
    }

    public string Generate(string systemPrompt, string userPrompt)
    {
        var sb = new StringBuilder();
        GenerateStreaming(systemPrompt, userPrompt, token => sb.Append(token));
        return sb.ToString();
    }

    public void GenerateStreaming(string systemPrompt, string userPrompt, Action<string> onToken)
    {
        var prompt = $"<|system|>\n{systemPrompt}<|end|>\n<|user|>\n{userPrompt}<|end|>\n<|assistant|>\n";
        var tokenCount = 0;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        lock (_gate)
        {
            using var sequences = _tokenizer.Encode(prompt);
            using var prms = new GeneratorParams(_model);
            prms.SetSearchOption("max_length", _maxLength);
            prms.SetSearchOption("do_sample", false);

            using var generator = new Generator(_model, prms);
            generator.AppendTokenSequences(sequences);

            using var stream = _tokenizer.CreateStream();
            while (!generator.IsDone())
            {
                generator.GenerateNextToken();
                var token = stream.Decode(generator.GetSequence(0)[^1]);
                onToken(token);
                tokenCount++;
                if (HasStopMarker(token))
                {
                    break;
                }
            }
        }

        stopwatch.Stop();
        LastTokenCount = tokenCount;
        LastInferenceTokensPerSecond = stopwatch.Elapsed.TotalSeconds > 0
            ? tokenCount / stopwatch.Elapsed.TotalSeconds
            : 0;
        LastInferenceDurationMs = stopwatch.Elapsed.TotalMilliseconds;
    }

    public int LastTokenCount { get; private set; }

    public double LastInferenceTokensPerSecond { get; private set; }

    public double LastInferenceDurationMs { get; private set; }

    private static bool HasStopMarker(string token) =>
        token.Contains("<|end|>", StringComparison.Ordinal) ||
        token.Contains("<|user|>", StringComparison.Ordinal) ||
        token.Contains("<|system|>", StringComparison.Ordinal);

    public void Dispose()
    {
        _tokenizer.Dispose();
        _model.Dispose();
        _oga.Dispose();
    }
}
