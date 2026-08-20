# Sacred.Core.Analyzer

This Roslyn-powered console analyzer parses the `Sacred.Core` C# source into a
`CSharpCompilation`. It reads layout types, fields, constant attribute arguments,
inline arrays, and XML documentation from Roslyn symbols; it does not load or reflect
over `Sacred.Core.dll`.

The hard-coded game-file-to-layout map is in `GameFileCatalog.cs`. Add an entry with no
sections when a game file is known but has no layout class yet.

From the repository root, regenerate the checked-in report with:

```powershell
& 'C:\Users\Aytac\.dotnet\dotnet.exe' run --project Sacred.Core.Analyzer -- `
  --source-directory Sacred.Core `
  --game-directory 'E:\SteamLibrary\steamapps\common\Sacred Gold' `
  --output docs\game-file-formats.md
```

Omit `--game-directory` when no Sacred installation is available. Coverage still comes
from source, while installation-presence cells will say `not scanned`.
