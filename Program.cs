using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using Microsoft.Win32;

// Built with Path.Combine so the separator matches the host OS ('\' on Windows,
// '/' on Linux/macOS). A hardcoded literal with '\' would not be normalized on
// Unix, where '\' is an ordinary filename character.
string RELATIVE_PATH = GamePaths.RelativeCoreModule;

Console.WriteLine("FixCoreModule — MelonLoader duplicate type fix");
Console.WriteLine("https://github.com/LavaGang/MelonLoader/issues/1142");
Console.WriteLine();

// Collect all affected DLL paths
var affectedPaths = FindAllAffectedPaths();

if (affectedPaths.Count == 0)
{
    Console.WriteLine("No affected games found automatically.");
    Console.WriteLine();

    string gamePath = null;

    // Try native folder picker on Windows
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        Console.WriteLine("Opening folder picker — select your game folder...");
        gamePath = OpenFolderPicker();
    }

    // Fall back to manual input
    if (string.IsNullOrEmpty(gamePath))
    {
        Console.WriteLine("Enter the full path to your game folder:");
        Console.WriteLine(RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? @"  e.g.  C:\Program Files (x86)\Steam\steamapps\common\My Game"
            : "  e.g.  ~/.steam/steam/steamapps/common/My Game");
        Console.Write("> ");
        gamePath = Console.ReadLine()?.Trim().Trim('"') ?? "";
        gamePath = ExpandHome(gamePath);
    }

    string candidate = Path.Combine(gamePath, RELATIVE_PATH);
    if (File.Exists(candidate))
        affectedPaths.Add(candidate);
    else
    {
        Console.WriteLine($"File not found at: {candidate}");
        Console.WriteLine("Make sure the game has MelonLoader installed.");
        Console.WriteLine("Press any key to exit.");
        Console.ReadKey();
        return;
    }
}

int fixed_ = 0;
int skipped = 0;
int failed = 0;

foreach (string dllPath in affectedPaths)
{
    string gameName = Path.GetFileName(Path.GetDirectoryName(
        Path.GetDirectoryName(Path.GetDirectoryName(dllPath))));

    Console.WriteLine($"── {gameName} ──");
    Console.WriteLine($"  {dllPath}");

    // Ask permission before touching each game
    Console.Write($"  Fix this game? [Y/n] ");
    var key = Console.ReadKey();
    Console.WriteLine();

    if (key.Key != ConsoleKey.Y && key.Key != ConsoleKey.Enter)
    {
        Console.WriteLine("  Skipped.");
        Console.WriteLine();
        skipped++;
        continue;
    }

    if (!FixDll(dllPath))
        failed++;
    else
        fixed_++;

    Console.WriteLine();
}

Console.WriteLine($"Done! Fixed: {fixed_}, Skipped: {skipped}, Failed: {failed}");
Console.WriteLine("Press any key to exit.");
Console.ReadKey();

// ── Fix a single DLL ─────────────────────────────────────────────────────────

static bool FixDll(string dllPath)
{
    var backupPath = dllPath + ".bak";
    var tempPath   = dllPath + ".tmp";

    byte[] bytes;
    try
    {
        bytes = File.ReadAllBytes(dllPath);
    }
    catch (IOException)
    {
        Console.WriteLine("  ERROR: Could not read the DLL — is the game still running?");
        return false;
    }

    bool hasDuplicates = ScanForDuplicates(bytes, verbose: true);

    if (!hasDuplicates)
    {
        Console.WriteLine("  DLL is already clean — nothing to do.");
        return true;
    }

    Console.WriteLine("  Duplicates found — fixing...");

    try
    {
        CecilFix.Run(dllPath, tempPath);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ERROR: Cecil failed — {ex.Message}");
        if (File.Exists(tempPath)) try { File.Delete(tempPath); } catch { }
        return false;
    }

    try
    {
        File.Copy(dllPath, backupPath, overwrite: true);
        File.Delete(dllPath);
        File.Move(tempPath, dllPath);
    }
    catch (IOException)
    {
        if (File.Exists(tempPath)) try { File.Delete(tempPath); } catch { }
        Console.WriteLine("  ERROR: Could not write the patched DLL — is the game still running?");
        return false;
    }

    Console.WriteLine($"  Backup saved: {backupPath}");

    bool stillDirty = ScanForDuplicates(File.ReadAllBytes(dllPath), verbose: false);
    Console.WriteLine(stillDirty
        ? "  WARNING: duplicates still present after fix!"
        : "  Fixed successfully.");
    return !stillDirty;
}

// ── Path discovery ───────────────────────────────────────────────────────────

static List<string> FindAllAffectedPaths()
{
    var results = new List<string>();

    // Real (symlink-resolved) paths already found. On Linux ~/.steam/steam is usually a symlink to
    // ~/.local/share/Steam, so the same game would otherwise be offered once per path.
    var seenDlls = new HashSet<string>(StringComparer.Ordinal);

    // 1. Check if the tool is placed directly in a game folder
    string localCandidate = Path.Combine(AppContext.BaseDirectory, GamePaths.RelativeCoreModule);
    if (File.Exists(localCandidate))
    {
        try
        {
            byte[] data = File.ReadAllBytes(localCandidate);
            if (ScanForDuplicates(data, verbose: false))
            {
                Console.WriteLine($"Found affected game: {Path.GetFileName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))}");
                results.Add(localCandidate);
                seenDlls.Add(RealPath(localCandidate));
            }
        }
        catch { /* unreadable — skip */ }
    }

    // 2. Gather Steam library roots (platform-specific default locations)
    var libraryRoots = new List<string>();

    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        foreach (char drive in new[] { 'C', 'D', 'E' })
        {
            libraryRoots.Add($@"{drive}:\Program Files (x86)\Steam\steamapps\common");
            libraryRoots.Add($@"{drive}:\Program Files\Steam\steamapps\common");
            libraryRoots.Add($@"{drive}:\Steam\steamapps\common");
            libraryRoots.Add($@"{drive}:\SteamLibrary\steamapps\common");
        }
    }
    else
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            libraryRoots.Add(Path.Combine(home, "Library", "Application Support",
                "Steam", "steamapps", "common"));
        }
        else // Linux and other Unix-likes
        {
            libraryRoots.Add(Path.Combine(home, ".steam", "steam", "steamapps", "common"));
            libraryRoots.Add(Path.Combine(home, ".local", "share", "Steam", "steamapps", "common"));
            libraryRoots.Add(Path.Combine(home, ".var", "app", "com.valvesoftware.Steam",
                ".local", "share", "Steam", "steamapps", "common")); // Flatpak
        }
    }

    try
    {
        string steamPath = FindSteamInstallPath();

        if (steamPath != null)
        {
            libraryRoots.Insert(0, Path.Combine(steamPath, "steamapps", "common"));

            string vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (string line in File.ReadAllLines(vdf))
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("\"path\""))
                    {
                        // VDF escapes backslashes as "\\"; harmless on Unix paths.
                        string libPath = trimmed.Split('"')[3].Replace(@"\\", @"\");
                        libraryRoots.Add(Path.Combine(libPath, "steamapps", "common"));
                    }
                }
            }
        }
    }
    catch { /* Steam not found — continue */ }

    // 3. Scan every game folder in every library root for the DLL
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (string root in libraryRoots)
    {
        if (!Directory.Exists(root) || !seen.Add(RealPath(root))) continue;
        try
        {
            foreach (string gameDir in Directory.GetDirectories(root))
            {
                string candidate = Path.Combine(gameDir, GamePaths.RelativeCoreModule);
                if (!File.Exists(candidate)) continue;

                // Skip if we already found this DLL (e.g. local candidate, or the same
                // library reached through a symlink)
                if (results.Contains(candidate) || seenDlls.Contains(RealPath(candidate))) continue;

                try
                {
                    byte[] data = File.ReadAllBytes(candidate);
                    if (ScanForDuplicates(data, verbose: false))
                    {
                        Console.WriteLine($"Found affected game: {Path.GetFileName(gameDir)}");
                        results.Add(candidate);
                        seenDlls.Add(RealPath(candidate));
                    }
                }
                catch { /* unreadable — skip */ }
            }
        }
        catch { /* access denied — skip */ }
    }

    return results;
}

// ── Steam install-path lookup (platform-specific) ────────────────────────────

static string FindSteamInstallPath()
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        return (string)Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null)
            ?? (string)Registry.GetValue(
            @"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam", "SteamPath", null);
    }

    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    string[] candidates = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
        ? new[] { Path.Combine(home, "Library", "Application Support", "Steam") }
        : new[]
        {
            Path.Combine(home, ".steam", "steam"),
            Path.Combine(home, ".local", "share", "Steam"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
        };

    foreach (string dir in candidates)
        if (Directory.Exists(Path.Combine(dir, "steamapps")))
            return dir;

    return null;
}

// ── Resolve symlinks in a path (Unix), so one folder reached two ways counts once ──

static string RealPath(string path)
{
    try
    {
        string full = Path.GetFullPath(path);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return full;

        // Walk the path one component at a time; when a component is a symlink,
        // splice its target in and carry on. Bounded, so a symlink loop can't hang.
        var parts = new List<string>(full.Split('/', StringSplitOptions.RemoveEmptyEntries));
        string current = "/";
        int i = 0, hops = 0;
        while (i < parts.Count)
        {
            string next = Path.Combine(current, parts[i]);
            string target = new FileInfo(next).LinkTarget;
            if (target == null)
            {
                current = next;
                i++;
                continue;
            }
            if (++hops > 40)
                return full;
            string resolved = Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(current, target));
            var rest = parts.GetRange(i + 1, parts.Count - i - 1);
            parts = new List<string>(resolved.Split('/', StringSplitOptions.RemoveEmptyEntries));
            parts.AddRange(rest);
            current = "/";
            i = 0;
        }
        return current;
    }
    catch
    {
        return path;
    }
}

// ── Expand a leading '~' to the user's home directory (Unix convention) ───────

static string ExpandHome(string path)
{
    if (string.IsNullOrEmpty(path)) return path;
    if (path == "~" || path.StartsWith("~/") || path.StartsWith(@"~\"))
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.Length == 1 ? home : Path.Combine(home, path.Substring(2));
    }
    return path;
}

// ── Folder picker (Windows COM) ──────────────────────────────────────────────

static string OpenFolderPicker()
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return null;

    // SHBrowseForFolder requires an STA thread — console apps default to MTA
    string result = null;
    var thread = new System.Threading.Thread(() =>
    {
        try
        {
            NativeMethods.OleInitialize(IntPtr.Zero);
            IntPtr displayBuf = Marshal.AllocCoTaskMem(260 * 2);
            try
            {
                var bi = new BrowseInfo
                {
                    pszDisplayName = displayBuf,
                    lpszTitle = "Select your game folder",
                    ulFlags = 0x00000040 | 0x00000010, // BIF_NEWDIALOGSTYLE | BIF_EDITBOX
                };
                IntPtr pidl = NativeMethods.SHBrowseForFolder(ref bi);
                if (pidl == IntPtr.Zero) return;

                var path = new char[260];
                bool ok = NativeMethods.SHGetPathFromIDList(pidl, path);
                Marshal.FreeCoTaskMem(pidl);
                if (ok) result = new string(path).TrimEnd('\0');
            }
            finally
            {
                Marshal.FreeCoTaskMem(displayBuf);
                NativeMethods.OleUninitialize();
            }
        }
        catch { }
    });
    thread.SetApartmentState(System.Threading.ApartmentState.STA);
    thread.Start();
    thread.Join();
    return result;
}

// ── Duplicate scanner ────────────────────────────────────────────────────────

static bool ScanForDuplicates(byte[] data, bool verbose)
{
    bool found = false;
    using var ms   = new MemoryStream(data);
    using var pe   = new PEReader(ms);
    var meta = pe.GetMetadataReader(MetadataReaderOptions.None);

    var topSeen = new HashSet<string>();
    foreach (var h in meta.TypeDefinitions)
    {
        var td = meta.GetTypeDefinition(h);
        if (!td.GetDeclaringType().IsNil) continue;
        string fqn = $"{meta.GetString(td.Namespace)}.{meta.GetString(td.Name)}".TrimStart('.');
        if (!topSeen.Add(fqn))
        {
            if (verbose) Console.WriteLine($"    Top-level duplicate: {fqn}");
            found = true;
        }
    }

    var outerToNames = new Dictionary<int, HashSet<string>>();
    foreach (var h in meta.TypeDefinitions)
    {
        var td   = meta.GetTypeDefinition(h);
        var decl = td.GetDeclaringType();
        if (decl.IsNil) continue;
        int encRid = meta.GetRowNumber(decl);
        string name = meta.GetString(td.Name);
        if (!outerToNames.ContainsKey(encRid)) outerToNames[encRid] = new HashSet<string>();
        if (!outerToNames[encRid].Add(name))
        {
            if (verbose)
            {
                var outer = meta.GetTypeDefinition(MetadataTokens.TypeDefinitionHandle(encRid));
                Console.WriteLine($"    Nested duplicate '{name}' in '{meta.GetString(outer.Name)}'");
            }
            found = true;
        }
    }
    return found;
}

// ── Cross-platform relative paths ────────────────────────────────────────────

static class GamePaths
{
    // MelonLoader/Il2CppAssemblies/UnityEngine.CoreModule.dll, assembled with the
    // host OS separator so it works on Windows, Linux and macOS alike.
    public static readonly string RelativeCoreModule =
        Path.Combine("MelonLoader", "Il2CppAssemblies", "UnityEngine.CoreModule.dll");
}

// ── Win32 P/Invoke for folder picker ─────────────────────────────────────────

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct BrowseInfo
{
    public IntPtr hwndOwner;
    public IntPtr pidlRoot;
    public IntPtr pszDisplayName;
    public string lpszTitle;
    public uint ulFlags;
    public IntPtr lpfn;
    public IntPtr lParam;
    public int iImage;
}

static class NativeMethods
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SHBrowseForFolder(ref BrowseInfo lpbi);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool SHGetPathFromIDList(IntPtr pidl, [Out] char[] pszPath);

    [DllImport("ole32.dll")]
    public static extern int OleInitialize(IntPtr pvReserved);

    [DllImport("ole32.dll")]
    public static extern void OleUninitialize();
}
