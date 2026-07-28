using System.Globalization;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Nikse.SubtitleEdit.UiLogic.SpellCheck.Thai;

/// <summary>
/// LEKCut attacut-c ONNX tokenizer (character-only AttaCut), ported from
/// https://github.com/PyThaiNLP/LEKCut lekcut/attacut.py AttacutCTokenizer.
/// </summary>
public sealed class AttacutCOnnxTokenizer : IThaiTokenizer, IDisposable
{
    private readonly InferenceSession _session;
    private readonly Dictionary<string, int> _ch2Ix;
    private readonly int _spaceIx;
    private bool _disposed;

    public AttacutCOnnxTokenizer(string onnxPath, string charactersJsonPath, string provider)
    {
        if (!File.Exists(onnxPath))
        {
            throw new FileNotFoundException("AttaCut ONNX model not found.", onnxPath);
        }

        if (!File.Exists(charactersJsonPath))
        {
            throw new FileNotFoundException("AttaCut character map not found.", charactersJsonPath);
        }

        using var json = File.OpenRead(charactersJsonPath);
        _ch2Ix = JsonSerializer.Deserialize<Dictionary<string, int>>(json)
                 ?? throw new InvalidOperationException("Invalid attacut-c-characters.json");
        if (!_ch2Ix.TryGetValue(" ", out _spaceIx))
        {
            _spaceIx = 0;
        }

        _session = CreateSession(onnxPath, provider);
    }

    public bool IsReady => !_disposed;

    public static AttacutCOnnxTokenizer? TryCreate(string provider)
    {
        if (!ThaiSpellPaths.IsAttacutInstalled())
        {
            return null;
        }

        try
        {
            return new AttacutCOnnxTokenizer(
                ThaiSpellPaths.GetAttacutOnnxPath(),
                ThaiSpellPaths.GetAttacutCharactersPath(),
                provider);
        }
        catch (Exception ex)
        {
            SpellCheckConfig.LogError("AttaCut ONNX init failed: " + ex.Message);
            if (!string.Equals(provider, ThaiOnnxProviders.Cpu, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return new AttacutCOnnxTokenizer(
                        ThaiSpellPaths.GetAttacutOnnxPath(),
                        ThaiSpellPaths.GetAttacutCharactersPath(),
                        ThaiOnnxProviders.Cpu);
                }
                catch (Exception fallbackEx)
                {
                    SpellCheckConfig.LogError("AttaCut ONNX CPU fallback failed: " + fallbackEx.Message);
                }
            }

            return null;
        }
    }

    public IReadOnlyList<ThaiTokenSpan> Segment(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<ThaiTokenSpan>();
        }

        if (!ThaiScript.ContainsThai(text))
        {
            return new[] { new ThaiTokenSpan(0, text) };
        }

        var result = new List<ThaiTokenSpan>();
        var i = 0;
        while (i < text.Length)
        {
            if (!ThaiScript.IsThaiChar(text[i]))
            {
                var start = i;
                while (i < text.Length && !ThaiScript.IsThaiChar(text[i]))
                {
                    i++;
                }

                result.Add(new ThaiTokenSpan(start, text.Substring(start, i - start)));
                continue;
            }

            var thaiStart = i;
            while (i < text.Length && ThaiScript.IsThaiChar(text[i]))
            {
                i++;
            }

            var run = text.Substring(thaiStart, i - thaiStart);
            foreach (var span in SegmentThaiRun(run, thaiStart))
            {
                result.Add(span);
            }
        }

        return result;
    }

    private List<ThaiTokenSpan> SegmentThaiRun(string run, int absoluteStart)
    {
        var chars = run.ToCharArray();
        if (chars.Length == 0)
        {
            return new List<ThaiTokenSpan>();
        }

        var indices = new long[chars.Length];
        for (var i = 0; i < chars.Length; i++)
        {
            indices[i] = CharacterToIx(chars[i]);
        }

        var input = new DenseTensor<long>(indices, new[] { 1, chars.Length });
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input", input) };

        using var results = _session.Run(inputs);
        var logits = results[0].AsEnumerable<float>().ToArray();
        var preds = new int[chars.Length];
        for (var i = 0; i < chars.Length; i++)
        {
            var logit = i < logits.Length ? logits[i] : 0f;
            preds[i] = Sigmoid(logit) > 0.5f ? 1 : 0;
        }

        return FindWordsFromPreds(chars, preds, absoluteStart);
    }

    private int CharacterToIx(char c)
    {
        if (c == '\0')
        {
            return _spaceIx;
        }

        if (char.IsPunctuation(c))
        {
            return _spaceIx;
        }

        var key = c.ToString();
        return _ch2Ix.TryGetValue(key, out var ix) ? ix : _spaceIx;
    }

    private static float Sigmoid(float x)
    {
        var clipped = Math.Clamp(x, -500f, 500f);
        return 1f / (1f + (float)Math.Exp(-clipped));
    }

    private static List<ThaiTokenSpan> FindWordsFromPreds(char[] tokens, int[] preds, int absoluteStart)
    {
        var words = new List<ThaiTokenSpan>();
        if (tokens.Length == 0)
        {
            return words;
        }

        var currStart = 0;
        var curr = new string(tokens[0], 1);
        for (var i = 1; i < tokens.Length; i++)
        {
            if (preds[i] == 0)
            {
                curr += tokens[i];
            }
            else
            {
                words.Add(new ThaiTokenSpan(absoluteStart + currStart, curr));
                currStart = i;
                curr = new string(tokens[i], 1);
            }
        }

        words.Add(new ThaiTokenSpan(absoluteStart + currStart, curr));
        return words;
    }

    private static InferenceSession CreateSession(string onnxPath, string provider)
    {
        var options = new SessionOptions();
        try
        {
            if (string.Equals(provider, ThaiOnnxProviders.DirectMl, StringComparison.OrdinalIgnoreCase))
            {
                options.AppendExecutionProvider_DML(0);
            }
            else if (string.Equals(provider, ThaiOnnxProviders.Cuda, StringComparison.OrdinalIgnoreCase))
            {
                options.AppendExecutionProvider_CUDA(0);
            }
        }
        catch (Exception ex)
        {
            SpellCheckConfig.LogError(
                $"ONNX provider '{provider}' unavailable ({ex.Message}); using CPU.");
            options = new SessionOptions();
        }

        return new InferenceSession(onnxPath, options);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _session.Dispose();
        _disposed = true;
    }
}
