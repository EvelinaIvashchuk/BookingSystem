using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BookingSystem.Ai.Ranking;

/// <summary>Identifiers of the car ranking models an administrator can choose (see <see cref="IRankingModelSettings"/>).</summary>
public static class RankingModelIds
{
    /// <summary>Gradient-boosted trees (LightGBM LambdaRank), the model trained in E5.</summary>
    public const string LightGbm = "lightgbm";

    /// <summary>Multilayer perceptron, an ensemble of 5 networks (E6).</summary>
    public const string Mlp = "mlp";

    /// <summary>Two-tower neural network, an ensemble of 5 networks (E6).</summary>
    public const string TwoTower = "two_tower";

    public static readonly IReadOnlyList<string> All = [LightGbm, Mlp, TwoTower];
}

/// <summary>Scores cars for one wish. Every ranking model (trees or neural network) is exposed through this interface.</summary>
public interface ICarScorer
{
    string Id { get; }

    /// <summary>Embedding model the scorer was trained with; the wish and car embeddings must come from the same model.</summary>
    string EmbeddingModel { get; }

    /// <param name="wish">Wish embedding.</param>
    /// <param name="cosines">cosine(wish, car text) for every car.</param>
    /// <param name="carVectors">Car features (<see cref="CarRankingFeatures.CarVector"/>) for every car; unknown values are NaN.</param>
    /// <returns>One score per car, higher is better.</returns>
    double[] Score(ReadOnlySpan<float> wish, IReadOnlyList<double> cosines, IReadOnlyList<double[]> carVectors);
}

/// <summary>What is known about a ranking model without loading its weights (used by the admin panel).</summary>
public sealed record RankingModelInfo(string Id, double? TestNdcg5);

/// <summary>The LightGBM model (trees over cosine, wish PCA components and car features) as an <see cref="ICarScorer"/>.</summary>
public sealed class LightGbmScorer(CarRankingModel model) : ICarScorer
{
    public string Id => RankingModelIds.LightGbm;
    public string EmbeddingModel => model.EmbeddingModel;

    public double[] Score(ReadOnlySpan<float> wish, IReadOnlyList<double> cosines, IReadOnlyList<double[]> carVectors)
    {
        var pcs = model.Project(wish);
        var scores = new double[carVectors.Count];
        for (var i = 0; i < scores.Length; i++)
            scores[i] = model.Score(CarRanker.Features(model, cosines[i], pcs, carVectors[i]));
        return scores;
    }
}

/// <summary>
/// A neural ranking model exported by <c>tools/ranking/export_nn.py</c>: a JSON header plus one blob (float16 weights, float32 statistics,
/// zlib, base64). The networks are evaluated here in plain C#; the result is the mean over the ensemble of networks.
/// </summary>
internal sealed class NeuralModelFile
{
    public required string Format { get; init; }
    public required string EmbeddingModel { get; init; }
    public required JsonElement Config { get; init; }
    public required Dictionary<string, float[]> Tensors { get; init; }

    public int Int(string name) => Config.GetProperty(name).GetInt32();

    public float[] T(string name) =>
        Tensors.TryGetValue(name, out var t) ? t : throw new InvalidDataException($"Tensor '{name}' is missing in the ranking model.");

    public static NeuralModelFile Load(string path, string expectedFormat)
    {
        using var stream = File.OpenRead(path);
        var file = JsonSerializer.Deserialize<FileDto>(stream, JsonOptions)
                   ?? throw new InvalidDataException($"Empty ranking model: {path}");
        if (file.Format != expectedFormat)
            throw new InvalidDataException($"Unsupported ranking model format '{file.Format}' in {path} (expected {expectedFormat})");

        using var compressed = new MemoryStream(Convert.FromBase64String(file.Data));
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        var bytes = raw.GetBuffer().AsSpan(0, (int)raw.Length);

        var tensors = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (var t in file.Tensors)
        {
            var slice = bytes.Slice(t.Offset, t.Length);
            float[] values;
            switch (t.Dtype)
            {
                case "f16":
                    var halves = MemoryMarshal.Cast<byte, Half>(slice);
                    values = new float[halves.Length];
                    for (var i = 0; i < values.Length; i++) values[i] = (float)halves[i];
                    break;
                case "f32":
                    values = MemoryMarshal.Cast<byte, float>(slice).ToArray();
                    break;
                default:
                    throw new InvalidDataException($"Unsupported tensor type '{t.Dtype}'.");
            }
            tensors[t.Name] = values;
        }

        return new NeuralModelFile { Format = file.Format, EmbeddingModel = file.EmbeddingModel, Config = file.Config, Tensors = tensors };
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record FileDto(string Format, string EmbeddingModel, JsonElement Config, List<TensorDto> Tensors, string Data);

    private sealed record TensorDto(string Name, string Dtype, int Offset, int Length);
}

/// <summary>Fully connected layer, row-major weights <c>[out, in]</c>.</summary>
internal sealed class Linear(float[] weights, float[] bias)
{
    public int Out => bias.Length;
    public int In => weights.Length / bias.Length;

    public double[] Forward(ReadOnlySpan<double> x)
    {
        var y = new double[Out];
        var inputs = In;
        for (var o = 0; o < y.Length; o++)
        {
            double s = bias[o];
            var row = weights.AsSpan(o * inputs, inputs);
            for (var i = 0; i < row.Length; i++) s += row[i] * x[i];
            y[o] = s;
        }
        return y;
    }

    public static void Relu(double[] v)
    {
        for (var i = 0; i < v.Length; i++)
            if (v[i] < 0) v[i] = 0;
    }
}

/// <summary>Missing-value handling shared by the neural models: unknown car features are replaced by the training medians.</summary>
internal static class NeuralInputs
{
    public static double[] Impute(double[] carVector, float[] median)
    {
        var x = new double[carVector.Length];
        for (var j = 0; j < x.Length; j++) x[j] = double.IsNaN(carVector[j]) ? median[j] : carVector[j];
        return x;
    }
}

/// <summary>
/// Multilayer perceptron over the same features as the trees: [cosine, wish PCA components, car features], standardised with the
/// training statistics; the score of a car is the mean output of the networks.
/// </summary>
public sealed class MlpScorer : ICarScorer
{
    public const string FormatName = "car-ranker-mlp/1";

    private readonly float[] _pcaMean, _xMu, _xSd, _median;
    private readonly float[][] _pcaComponents;
    private readonly Linear[][] _networks;

    public string Id => RankingModelIds.Mlp;
    public string EmbeddingModel { get; }

    private MlpScorer(NeuralModelFile f)
    {
        EmbeddingModel = f.EmbeddingModel;
        _pcaMean = f.T("pca_mean");
        var k = f.Int("pcaDimensions");
        var flat = f.T("pca_components");
        _pcaComponents = Enumerable.Range(0, k).Select(i => flat.AsSpan(i * _pcaMean.Length, _pcaMean.Length).ToArray()).ToArray();
        _xMu = f.T("x_mu");
        _xSd = f.T("x_sd");
        _median = f.T("car_median");
        var layers = f.Config.GetProperty("layers").GetArrayLength() - 1;
        _networks = Enumerable.Range(0, f.Int("networks"))
            .Select(n => Enumerable.Range(0, layers).Select(j => new Linear(f.T($"n{n}.l{j}.w"), f.T($"n{n}.l{j}.b"))).ToArray())
            .ToArray();
    }

    public static MlpScorer Load(string path) => new(NeuralModelFile.Load(path, FormatName));

    public double[] Score(ReadOnlySpan<float> wish, IReadOnlyList<double> cosines, IReadOnlyList<double[]> carVectors)
    {
        var pcs = Project(wish);
        var scores = new double[carVectors.Count];
        var input = new double[_xMu.Length];
        for (var c = 0; c < scores.Length; c++)
        {
            input[0] = cosines[c];
            pcs.CopyTo(input, 1);
            NeuralInputs.Impute(carVectors[c], _median).CopyTo(input, 1 + pcs.Length);
            var x = new double[input.Length];
            for (var i = 0; i < x.Length; i++) x[i] = (input[i] - _xMu[i]) / _xSd[i];

            double sum = 0;
            foreach (var net in _networks)
            {
                var h = x;
                for (var j = 0; j < net.Length; j++)
                {
                    h = net[j].Forward(h);
                    if (j < net.Length - 1) Linear.Relu(h);
                }
                sum += h[0];
            }
            scores[c] = sum / _networks.Length;
        }
        return scores;
    }

    private double[] Project(ReadOnlySpan<float> embedding)
    {
        var result = new double[_pcaComponents.Length];
        if (embedding.Length != _pcaMean.Length) return result;
        for (var k = 0; k < result.Length; k++)
        {
            double s = 0;
            var comp = _pcaComponents[k];
            for (var i = 0; i < embedding.Length; i++) s += (embedding[i] - (double)_pcaMean[i]) * comp[i];
            result[k] = s;
        }
        return result;
    }
}

/// <summary>
/// Two-tower network: the wish tower maps the standardised wish embedding to a vector, the car tower maps the standardised car features to a
/// vector of the same size, the score is their scaled dot product (mean over the ensemble). No PCA, no cosine feature.
/// </summary>
public sealed class TwoTowerScorer : ICarScorer
{
    public const string FormatName = "car-ranker-two-tower/1";

    private readonly float[] _eMu, _eSd, _median, _carMu, _carSd;
    private readonly int[] _nanColumns;
    private readonly double _scale;
    private readonly (Linear Wish1, Linear Wish2, Linear Car1, Linear Car2)[] _networks;

    public string Id => RankingModelIds.TwoTower;
    public string EmbeddingModel { get; }

    private TwoTowerScorer(NeuralModelFile f)
    {
        EmbeddingModel = f.EmbeddingModel;
        _eMu = f.T("e_mu");
        _eSd = f.T("e_sd");
        _median = f.T("car_median");
        _carMu = f.T("car_mu");
        _carSd = f.T("car_sd");
        _nanColumns = f.Config.GetProperty("nanColumns").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        _scale = Math.Pow(f.Int("dim"), -0.5);
        _networks = Enumerable.Range(0, f.Int("networks"))
            .Select(n => (new Linear(f.T($"n{n}.wish1.w"), f.T($"n{n}.wish1.b")), new Linear(f.T($"n{n}.wish2.w"), f.T($"n{n}.wish2.b")),
                          new Linear(f.T($"n{n}.car1.w"), f.T($"n{n}.car1.b")), new Linear(f.T($"n{n}.car2.w"), f.T($"n{n}.car2.b"))))
            .ToArray();
    }

    public static TwoTowerScorer Load(string path) => new(NeuralModelFile.Load(path, FormatName));

    public double[] Score(ReadOnlySpan<float> wish, IReadOnlyList<double> cosines, IReadOnlyList<double[]> carVectors)
    {
        var scores = new double[carVectors.Count];
        if (wish.Length != _eMu.Length) return scores;

        var w = new double[wish.Length];
        for (var i = 0; i < w.Length; i++) w[i] = (wish[i] - (double)_eMu[i]) / _eSd[i];

        var cars = carVectors.Select(CarInput).ToList();
        foreach (var (wish1, wish2, car1, car2) in _networks)
        {
            var wv = wish1.Forward(w);
            Linear.Relu(wv);
            wv = wish2.Forward(wv);
            for (var c = 0; c < scores.Length; c++)
            {
                var cv = car1.Forward(cars[c]);
                Linear.Relu(cv);
                cv = car2.Forward(cv);
                double dot = 0;
                for (var d = 0; d < cv.Length; d++) dot += cv[d] * wv[d];
                scores[c] += dot * _scale;
            }
        }

        for (var c = 0; c < scores.Length; c++) scores[c] /= _networks.Length;
        return scores;
    }

    /// <summary>Imputed car features followed by "was missing" indicators, standardised with the training statistics.</summary>
    private double[] CarInput(double[] carVector)
    {
        var x = new double[carVector.Length + _nanColumns.Length];
        NeuralInputs.Impute(carVector, _median).CopyTo(x, 0);
        for (var k = 0; k < _nanColumns.Length; k++) x[carVector.Length + k] = double.IsNaN(carVector[_nanColumns[k]]) ? 1 : 0;
        for (var i = 0; i < x.Length; i++) x[i] = (x[i] - _carMu[i]) / _carSd[i];
        return x;
    }
}

/// <summary>Reads the test quality of a model file without loading its weights (head and tail of the file are enough).</summary>
internal static partial class RankingModelFiles
{
    public static double? ReadTestNdcg5(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[8192];
            foreach (var offset in new[] { 0L, Math.Max(0, stream.Length - buffer.Length) })
            {
                stream.Seek(offset, SeekOrigin.Begin);
                var read = stream.Read(buffer, 0, buffer.Length);
                var m = TestNdcg().Match(System.Text.Encoding.UTF8.GetString(buffer, 0, read));
                if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var v))
                    return v;
            }
        }
        catch (IOException)
        {
        }
        return null;
    }

    [GeneratedRegex("\"testNdcg5\"\\s*:\\s*([0-9.eE+-]+)")]
    private static partial Regex TestNdcg();
}
