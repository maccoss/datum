//! A predicted fragment ion (annotation, m/z, relative intensity) from Koina.

namespace Datum.Koina;

/// <summary>A single predicted fragment ion for a peptide precursor.</summary>
/// <param name="Annotation">Ion annotation, e.g. "y7+1" or "b3+2".</param>
/// <param name="Mz">Fragment mass-to-charge ratio.</param>
/// <param name="RelativeIntensity">Predicted intensity normalized to the base (most intense) fragment.</param>
public readonly record struct FragmentPrediction(string Annotation, double Mz, double RelativeIntensity);
