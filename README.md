# WraithLite

A .NET MAUI front-end for GemStone IV that integrates with Lich5.

## Features
- Launches Lich5 with `--client-mode`
- Pipes output into the MAUI UI
- Styled game console
- Cross-platform (Windows primary, macOS/Linux ready)

## Requirements
- [.NET 8 SDK](https://dotnet.microsoft.com)
- [Lich5 installed](https://github.com/rpherbig/lich-5)
- Ruby installed and added to PATH

## Run Locally

```bash
git clone https://github.com/yourusername/WraithLite.git
cd WraithLite
dotnet build
dotnet run
