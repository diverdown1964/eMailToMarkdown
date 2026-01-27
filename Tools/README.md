# Tools Directory

## Pandoc

This project requires Pandoc for advanced markdown conversion.

### Installation

Download Pandoc from the official website:
- **Windows**: https://pandoc.org/installing.html
- Direct download: https://github.com/jgm/pandoc/releases/latest

### Setup for this project

1. Download `pandoc.exe` (Windows) from the releases page
2. Place `pandoc.exe` in this `Tools/` directory
3. The application will automatically use it for HTML-to-Markdown conversion

**Note**: `pandoc.exe` is not included in the repository due to its large file size (200+ MB).

### Alternative

You can also install Pandoc system-wide and the application will find it in your PATH.

```powershell
# Using Chocolatey
choco install pandoc

# Using Winget
winget install JohnMacFarlane.Pandoc
```
