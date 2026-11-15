using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Avalonia;

namespace WeaponLab.App;

internal static partial class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        PinWorkspace();

        // One Weapon Lab per workspace: two would both write its profiles and sandboxes (seen on PC2, 2026-10-28).
        using var guard = new Mutex(initiallyOwned: true, MutexName(), out var first);
        if (!first)
        {
            AlreadyRunning();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the Avalonia designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }

    /// <summary>
    /// The first run with 0.24 settings saves the workspace in use (an earlier version's, else the default beside
    /// the app), so every later version, wherever it is unpacked, finds the same profiles. A failure only means it
    /// is worked out again next time.
    /// </summary>
    private static void PinWorkspace()
    {
        if (Core.Settings.AppSettings.Exists || Environment.GetEnvironmentVariable("WEAPONLAB_WORKSPACE") is { Length: > 0 })
        {
            return;
        }

        try
        {
            new Core.Settings.AppSettings { WorkspaceFolder = Core.Sandbox.SandboxStore.DefaultWorkspacePath() }.Save();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Could not save the settings: " + error.Message);
        }
    }

    /// <summary>A per-workspace name, so a second copy pointed at another workspace (WEAPONLAB_WORKSPACE) still starts.</summary>
    private static string MutexName()
    {
        var workspace = Path.GetFullPath(Core.Sandbox.SandboxStore.DefaultWorkspacePath()).ToUpperInvariant();
        return "Local\\CS2WeaponLab-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(workspace)))[..16];
    }

    private static void AlreadyRunning()
    {
        const string Message = "CS2 Weapon Lab is already running for this workspace. Use the open window (check the taskbar).";
        if (OperatingSystem.IsWindows())
        {
            MessageBox(IntPtr.Zero, Message, "CS2 Weapon Lab", 0x40); // MB_ICONINFORMATION
        }
        else
        {
            Console.Error.WriteLine(Message);
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(IntPtr window, string text, string caption, uint type);
}
