using System.Runtime.InteropServices;

namespace Nikse.SubtitleEdit.UiLogic.SpellCheck.Thai;

/// <summary>
/// Loads onnxruntime natives from AppData (downloaded with AttaCut) or from the app folder
/// (dev / side-by-side publish). Must run before any OnnxRuntime API call so Windows does not
/// bind a mismatched System32\onnxruntime.dll.
/// </summary>
public static class OnnxNativeBootstrap
{
    private static readonly object Sync = new();
    private static bool _resolverRegistered;
    private static bool _loaded;
    private static string? _loadedDirectory;

    public static bool IsRuntimeAvailable()
    {
        return GetPreferredRuntimeDirectory() != null;
    }

    public static string? GetPreferredRuntimeDirectory()
    {
        // Prefer the AppData pack when present (installer builds download here).
        if (ThaiSpellPaths.IsOnnxRuntimeInstalled())
        {
            return ThaiSpellPaths.GetOnnxRuntimeFolder();
        }

        // Dev / framework publish may still ship natives next to the exe.
        var appDir = AppContext.BaseDirectory;
        if (HasRuntimeFiles(appDir))
        {
            return appDir;
        }

        return null;
    }

    public static bool TryEnsureLoaded()
    {
        lock (Sync)
        {
            RegisterResolver();

            if (_loaded)
            {
                return true;
            }

            var dir = GetPreferredRuntimeDirectory();
            if (dir == null)
            {
                return false;
            }

            try
            {
                // Dependency first, then the main library (same folder search for deps).
                TryLoadFile(Path.Combine(dir, ThaiSpellPaths.OnnxProvidersSharedFileName));
                if (!TryLoadFile(Path.Combine(dir, ThaiSpellPaths.OnnxRuntimeFileName)))
                {
                    return false;
                }

                _loadedDirectory = dir;
                _loaded = true;
                return true;
            }
            catch (Exception ex)
            {
                SpellCheckConfig.LogError("ONNX Runtime native load failed: " + ex.Message);
                return false;
            }
        }
    }

    private static void RegisterResolver()
    {
        if (_resolverRegistered)
        {
            return;
        }

        _resolverRegistered = true;
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(Microsoft.ML.OnnxRuntime.InferenceSession).Assembly,
                (name, _, _) =>
                {
                    if (!IsOnnxLibraryName(name))
                    {
                        return IntPtr.Zero;
                    }

                    var dir = _loadedDirectory ?? GetPreferredRuntimeDirectory();
                    if (dir == null)
                    {
                        return IntPtr.Zero;
                    }

                    var fileName = name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                        ? name
                        : name + ".dll";
                    var fullPath = Path.Combine(dir, fileName);
                    return NativeLibrary.TryLoad(fullPath, out var handle) ? handle : IntPtr.Zero;
                });
        }
        catch (InvalidOperationException)
        {
            // Resolver already set for this assembly (e.g. tests / hot reload).
        }
    }

    private static bool IsOnnxLibraryName(string name)
    {
        return name.Equals("onnxruntime", StringComparison.OrdinalIgnoreCase)
               || name.Equals("onnxruntime.dll", StringComparison.OrdinalIgnoreCase)
               || name.Equals("onnxruntime_providers_shared", StringComparison.OrdinalIgnoreCase)
               || name.Equals("onnxruntime_providers_shared.dll", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryLoadFile(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            return false;
        }

        return NativeLibrary.TryLoad(fullPath, out _);
    }

    private static bool HasRuntimeFiles(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var main = Path.Combine(directory, ThaiSpellPaths.OnnxRuntimeFileName);
        var shared = Path.Combine(directory, ThaiSpellPaths.OnnxProvidersSharedFileName);
        return File.Exists(main) && new FileInfo(main).Length > 1_000_000
               && File.Exists(shared) && new FileInfo(shared).Length > 1_000;
    }
}
