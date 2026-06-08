//! HTTP client for Koina fragment-intensity prediction (KServe v2 protocol).

using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Datum.Koina;

/// <summary>
/// Client for the Koina inference server (KServe v2 protocol). Predicts fragment-ion
/// intensities for a peptide precursor using a Prosit-style model and returns the top-N
/// fragments by intensity, normalized to the base peak.
/// </summary>
/// <remarks>
/// Endpoint: <c>POST {BaseUrl}/{model}/infer</c>. Inputs are <c>peptide_sequences</c> (BYTES),
/// <c>precursor_charges</c> (INT32), and <c>collision_energies</c> (FP32); outputs are
/// <c>intensities</c>, <c>mz</c>, and <c>annotation</c>. Prosit emits -1 for impossible ions,
/// which are filtered out here.
/// </remarks>
public sealed class KoinaClient
{
    /// <summary>Default public Koina server base URL (models live under this path).</summary>
    public const string DefaultBaseUrl = "https://koina.wilhelmlab.org/v2/models";

    /// <summary>Default fragment-intensity model.</summary>
    public const string DefaultModel = "Prosit_2020_intensity_HCD";

    private readonly HttpClient _http;
    private readonly string _baseUrl;

    /// <summary>Create a client, optionally supplying an <see cref="HttpClient"/> and base URL.</summary>
    public KoinaClient(HttpClient? http = null, string baseUrl = DefaultBaseUrl)
    {
        _http = http ?? new HttpClient { Timeout = System.TimeSpan.FromSeconds(15) };
        _baseUrl = baseUrl.TrimEnd('/');
    }

    /// <summary>
    /// Predict fragment intensities for one peptide precursor and return the top-N fragments
    /// (by intensity), each normalized to the base peak.
    /// </summary>
    /// <param name="peptide">Peptide sequence (Prosit-style, optionally with modifications).</param>
    /// <param name="precursorCharge">Precursor charge state.</param>
    /// <param name="collisionEnergy">Normalized collision energy.</param>
    /// <param name="topN">Maximum number of fragments to return.</param>
    /// <param name="model">Model name (defaults to Prosit_2020_intensity_HCD).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<FragmentPrediction>> PredictAsync(
        string peptide,
        int precursorCharge,
        double collisionEnergy,
        int topN = 6,
        string model = DefaultModel,
        CancellationToken cancellationToken = default)
    {
        var request = new InferRequest
        {
            Id = "0",
            Inputs = new List<InferInput>
            {
                new() { Name = "peptide_sequences", Shape = new[] { 1, 1 }, Datatype = "BYTES", Data = new object[] { peptide } },
                new() { Name = "precursor_charges", Shape = new[] { 1, 1 }, Datatype = "INT32", Data = new object[] { precursorCharge } },
                new() { Name = "collision_energies", Shape = new[] { 1, 1 }, Datatype = "FP32", Data = new object[] { collisionEnergy } },
            },
        };

        string url = $"{_baseUrl}/{model}/infer";
        using HttpResponseMessage response = await _http.PostAsJsonAsync(url, request, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        InferResponse? payload = await response.Content
            .ReadFromJsonAsync<InferResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (payload?.Outputs is null)
        {
            return System.Array.Empty<FragmentPrediction>();
        }

        double[] intensities = ReadNumbers(payload, "intensities");
        double[] mz = ReadNumbers(payload, "mz");
        string[] annotation = ReadStrings(payload, "annotation");

        return ToTopFragments(annotation, mz, intensities, topN);
    }

    /// <summary>
    /// Convert raw Koina output arrays into the top-N normalized fragments. Exposed for offline
    /// testing without a network call.
    /// </summary>
    public static IReadOnlyList<FragmentPrediction> ToTopFragments(
        string[] annotation, double[] mz, double[] intensities, int topN)
    {
        var fragments = new List<FragmentPrediction>();
        double max = 0.0;
        foreach (double v in intensities)
        {
            if (v > max)
            {
                max = v;
            }
        }

        if (max <= 0.0)
        {
            return fragments;
        }

        int n = intensities.Length;
        for (int i = 0; i < n; i++)
        {
            if (intensities[i] <= 0.0)
            {
                continue; // Prosit emits -1 / 0 for impossible ions.
            }

            string ann = i < annotation.Length ? annotation[i] : $"ion{i}";
            double m = i < mz.Length ? mz[i] : 0.0;
            fragments.Add(new FragmentPrediction(ann, m, intensities[i] / max));
        }

        fragments.Sort((a, b) => b.RelativeIntensity.CompareTo(a.RelativeIntensity));
        return fragments.Count > topN ? fragments.GetRange(0, topN) : fragments;
    }

    private static double[] ReadNumbers(InferResponse payload, string name)
    {
        InferOutput? output = payload.Outputs?.Find(o => o.Name == name);
        if (output is null || output.Data.ValueKind != JsonValueKind.Array)
        {
            return System.Array.Empty<double>();
        }

        var list = new List<double>();
        foreach (JsonElement el in output.Data.EnumerateArray())
        {
            list.Add(el.ValueKind == JsonValueKind.Number ? el.GetDouble() : 0.0);
        }

        return list.ToArray();
    }

    private static string[] ReadStrings(InferResponse payload, string name)
    {
        InferOutput? output = payload.Outputs?.Find(o => o.Name == name);
        if (output is null || output.Data.ValueKind != JsonValueKind.Array)
        {
            return System.Array.Empty<string>();
        }

        var list = new List<string>();
        foreach (JsonElement el in output.Data.EnumerateArray())
        {
            list.Add(el.ValueKind == JsonValueKind.String ? el.GetString() ?? string.Empty : el.ToString());
        }

        return list.ToArray();
    }

    // ---- KServe v2 DTOs ----

    private sealed class InferRequest
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "0";

        [JsonPropertyName("inputs")] public List<InferInput> Inputs { get; set; } = new();
    }

    private sealed class InferInput
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

        [JsonPropertyName("shape")] public int[] Shape { get; set; } = System.Array.Empty<int>();

        [JsonPropertyName("datatype")] public string Datatype { get; set; } = string.Empty;

        [JsonPropertyName("data")] public object[] Data { get; set; } = System.Array.Empty<object>();
    }

    private sealed class InferResponse
    {
        [JsonPropertyName("outputs")] public List<InferOutput>? Outputs { get; set; }
    }

    private sealed class InferOutput
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

        [JsonPropertyName("shape")] public int[] Shape { get; set; } = System.Array.Empty<int>();

        [JsonPropertyName("datatype")] public string Datatype { get; set; } = string.Empty;

        [JsonPropertyName("data")] public JsonElement Data { get; set; }
    }
}
