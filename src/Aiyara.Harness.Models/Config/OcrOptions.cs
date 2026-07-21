namespace Aiyara.Harness.Models.Config;

/// <summary>
/// Settings for the <c>ocr_image</c> tool, stored in <c>ocr.json</c>. Backed by a local
/// <a href="https://github.com/tesseract-ocr/tesseract">Tesseract</a> engine (via the
/// <c>Tesseract</c> NuGet package) - fully offline, no API key needed, matching the harness's
/// other local-first tools (<c>search_documents</c>, <c>web_search</c>).
/// </summary>
public sealed class OcrOptions
{
    /// <summary>
    /// Whether <c>ocr_image</c> is registered for the session.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Folder containing Tesseract's trained-data files (e.g. <c>eng.traineddata</c>), downloadable
    /// from https://github.com/tesseract-ocr/tessdata. Defaults to a subfolder under
    /// <see cref="UserConfigPaths.Directory"/>, a sibling of the RAG index folder - the folder
    /// itself isn't created or seeded with data, only referenced; <c>Program.cs</c> skips
    /// registering the tool (logging a warning) if the file for <see cref="Language"/> isn't found
    /// there.
    /// </summary>
    public string TessDataPath { get; init; } = Path.Combine(UserConfigPaths.Directory, "ocr", "tessdata");

    /// <summary>
    /// Default Tesseract language code used when a call to <c>ocr_image</c> doesn't specify one,
    /// e.g. <c>eng</c>, <c>tha</c>, or a combination like <c>eng+tha</c>. Must match a
    /// <c>.traineddata</c> file present under <see cref="TessDataPath"/>.
    /// </summary>
    public string Language { get; init; } = "eng";
}
