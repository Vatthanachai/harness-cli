using Aiyara.Harness.Models.Config;

using OllamaSharp.Models.Chat;

using Tesseract;

namespace Aiyara.Harness.Tools;

/// <summary>
/// Extracts text from an image file via a local Tesseract OCR engine. Opt-in, like
/// <c>web_search</c> - only registered when <c>ocr.json</c>'s <c>Enabled</c> is true and its
/// <c>TessDataPath</c> holds a matching <c>.traineddata</c> file (see <c>Program.cs</c>).
/// Complements <see cref="OpenImageTool"/>: that hands the model raw image bytes for a
/// vision-capable model to read itself, this deterministically transcribes text so a text-only
/// model can use it too. The underlying <c>Tesseract</c> NuGet package ships native binaries for
/// win-x64/win-x86 only - on other platforms every call fails with a clear error (caught by
/// <see cref="BaseTool.InvokeMethod"/>) rather than crashing the session.
/// </summary>
public sealed class OcrImageTool : BaseTool
{
    private readonly string _tessDataPath;
    private readonly string _defaultLanguage;

    public OcrImageTool(string tessDataPath, string defaultLanguage)
    {
        _tessDataPath = tessDataPath;
        _defaultLanguage = defaultLanguage;

        Type = "function";
        Function = new Function
        {
            Name = "ocr_image",
            Description = "Extract text from an image file (screenshot, scan, photo) using OCR and return it " +
                          "as plain text.",
            Parameters = new Parameters
            {
                Properties = new Dictionary<string, Property>
                {
                    {
                        "file_path",
                        new Property { Type = "string", Description = "The path to the image file to read." }
                    },
                    {
                        "language",
                        new Property
                        {
                            Type = "string",
                            Description = "Tesseract language code to use, e.g. 'eng' or 'eng+tha'. Defaults to " +
                                          "ocr.json's configured Language if omitted."
                        }
                    }
                },
                Required = new List<string> { "file_path" }
            }
        };
    }

    protected override object? Execute(IDictionary<string, object?>? args)
    {
        var filePath = args?.TryGetValue("file_path", out var fp) == true ? fp?.ToString() : null;
        var language = args?.TryGetValue("language", out var lang) == true ? lang?.ToString() : null;

        return RecognizeText(filePath, string.IsNullOrWhiteSpace(language) ? _defaultLanguage : language);
    }

    /// <summary>
    /// Runs Tesseract over an image file and returns the recognized text. A fresh
    /// <see cref="TesseractEngine"/> is created per call rather than held for the session - OCR
    /// calls are infrequent enough that the load cost isn't worth the complexity of managing a
    /// long-lived native engine's lifetime/thread-safety.
    /// </summary>
    /// <param name="filePath"></param>
    /// <param name="language"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="FileNotFoundException"></exception>
    public string RecognizeText(string? filePath, string language)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
        }

        var resolvedPath = Workspace.ResolvePath(filePath);

        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"The file '{filePath}' does not exist.");
        }

        using var engine = new TesseractEngine(_tessDataPath, language, EngineMode.Default);
        using var img = Pix.LoadFromFile(resolvedPath);
        using var page = engine.Process(img);

        var text = page.GetText();
        return string.IsNullOrWhiteSpace(text) ? "No text found in the image." : text.Trim();
    }
}
