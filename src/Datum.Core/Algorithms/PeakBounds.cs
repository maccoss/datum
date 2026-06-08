//! Result of peak detection: apex and integration boundaries on a sampled trace.

namespace Datum.Core.Algorithms;

/// <summary>
/// The outcome of peak detection over a sampled chromatogram: the apex and the start/end
/// integration boundaries, expressed both as array indices and retention times. When no
/// peak is found, <see cref="Detected"/> is false.
/// </summary>
public readonly record struct PeakBounds(
    bool Detected,
    int StartIndex,
    int EndIndex,
    int ApexIndex,
    double StartRt,
    double EndRt,
    double ApexRt)
{
    /// <summary>A sentinel value indicating no peak was detected.</summary>
    public static PeakBounds NotFound => new(false, 0, 0, 0, 0.0, 0.0, 0.0);
}
