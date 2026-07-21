If `ocr.json`'s `Enabled` is true, `Program.cs` registers one opt-in tool - `ocr_image` - the same
"just another `BaseTool`" pattern as [[RAG]], [[MCP]] and [[Web Search]] tools: no always-on prompt
injection, the model calls it on demand.

## `ocr_image`

`src/Aiyara.Harness.Tools/OcrImageTool.cs` - extracts text from an image file (screenshot, scan,
photo) via a local [Tesseract](https://github.com/tesseract-ocr/tesseract) engine, wrapped by the
`Tesseract` NuGet package (`charlesw/tesseract`). No API key or network call needed - the model
already gets `TessDataPath` and a default `Language` from `ocr.json`; a call can optionally pass
its own `language` (e.g. `eng+tha`) to override the default for that one image.

A fresh `TesseractEngine` is created and disposed **per call** rather than held for the session -
OCR calls are infrequent enough that the reload cost isn't worth managing a long-lived native
engine's lifetime/thread-safety, the same trade-off `WebSearchTool` makes by not pooling its
`HttpClient` connections beyond the one instance it's given.

## Why it exists alongside `open_image`

[[Tools]]'s `open_image` hands the model raw image bytes so a *vision-capable* model (e.g.
llava/qwen-vl) can read them itself - it does nothing useful on a text-only model. `ocr_image` is
the deterministic counterpart: it transcribes literal text out of an image regardless of whether
the active model can see, at the cost of only recognizing text (not describing a photo's contents).

## `ocr.json` example

```json
{
  "Enabled": true,
  "TessDataPath": "C:\\Users\\<you>\\.aiyara\\ocr\\tessdata",
  "Language": "eng"
}
```

`TessDataPath` defaults to a subfolder under `UserConfigPaths.Directory` (a sibling of the RAG
index folder) but isn't seeded with data - trained-data files (`<lang>.traineddata`) have to be
downloaded manually from the [tessdata repo](https://github.com/tesseract-ocr/tessdata) and placed
there. `Program.cs` checks that the file for `Language`'s first `+`-separated component exists
*before* registering the tool; if it's missing, a warning is logged and `ocr_image` is simply
absent from that session's tool list - same resilience policy as a bad RAG/websearch config or an
unreachable MCP server, not a startup failure.

## Gotcha: native binaries are win-x64/win-x86 only

The `Tesseract` NuGet package ships prebuilt `leptonica`/`tesseract50` native DLLs for Windows only
(x64 and x86) - there's no linux/osx runtime folder like the ones `SQLitePCLRaw`/`sqlite-vec` ship
for [[RAG]]'s SQLite backends. On a non-Windows host the trained-data check at startup still
passes (it's just a file-existence check), but the first `ocr_image` call fails when the native
library can't load - caught by `BaseTool.InvokeMethod` and returned as an ordinary tool error, not
a crash, but effectively means OCR is a Windows-only tool today.

## Related

[[Index]] · [[Tools]] · [[Web Search]] · [[RAG]] · [[Multi-Agent]]
