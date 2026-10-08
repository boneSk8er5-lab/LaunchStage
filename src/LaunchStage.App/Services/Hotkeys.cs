using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using LaunchStage.Core.Logging;
using LaunchStage.Core.Storage;

namespace LaunchStageApp.Services;

/// <summary>
/// Global hotkeys: key combinations that work anywhere in Windows while LaunchStage is running, even hidden in the
/// tray. Registered through a hidden message-only window.
/// </summary>
internal sealed class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x1;
    private const uint MOD_CONTROL = 0x2;
    private const uint MOD_SHIFT = 0x4;
    private const uint MOD_WIN = 0x8;
    private const uint MOD_NOREPEAT = 0x4000; // holding the keys down doesn't fire again and again

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _window;
    private readonly Dictionary<int, Action> _active = new();
    private readonly List<(string Combo, string What, Action Action)> _wanted = new();
    private int _pauses;

    public HotkeyManager()
    {
        // A message-only window (parent HWND_MESSAGE): never shown, only receives the hotkey messages.
        _window = new HwndSource(new HwndSourceParameters("LaunchStage hotkeys") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        _window.AddHook(WndProc);
    }

    /// <summary>Replaces all hotkeys. Returns a plain-language line for each one that couldn't be turned on.</summary>
    public List<string> Set(IEnumerable<(string Combo, string What, Action Action)> hotkeys)
    {
        _wanted.Clear();
        _wanted.AddRange(hotkeys);
        return _pauses > 0 ? new List<string>() : RegisterAll();
    }

    /// <summary>Turns hotkeys off while a hotkey box is being filled in, so pressing a key there doesn't run it.</summary>
    public void Pause()
    {
        if (_pauses++ == 0)
        {
            UnregisterAll();
        }
    }

    public void Resume()
    {
        if (_pauses > 0 && --_pauses == 0)
        {
            RegisterAll();
        }
    }

    private List<string> RegisterAll()
    {
        UnregisterAll();
        var failed = new List<string>();
        int id = 1;
        foreach (var (combo, what, action) in _wanted)
        {
            if (!HotkeyText.TryParse(combo, out var modifiers, out var key))
            {
                failed.Add($"{combo} ({what}) isn't a key combination LaunchStage understands");
                continue;
            }

            if (RegisterHotKey(_window.Handle, id, ToNative(modifiers) | MOD_NOREPEAT, (uint)KeyInterop.VirtualKeyFromKey(key)))
            {
                _active[id] = action;
                Log.Info($"Hotkey {combo}: {what}.");
            }
            else
            {
                failed.Add($"{combo} ({what}) is already used by another program");
                Log.Warn($"Hotkey {combo} ({what}) couldn't be registered (Windows error {Marshal.GetLastWin32Error()}); another program probably uses it.");
            }

            id++;
        }

        return failed;
    }

    private void UnregisterAll()
    {
        foreach (int id in _active.Keys)
        {
            UnregisterHotKey(_window.Handle, id);
        }

        _active.Clear();
    }

    private static uint ToNative(ModifierKeys modifiers) =>
        (modifiers.HasFlag(ModifierKeys.Alt) ? MOD_ALT : 0)
        | (modifiers.HasFlag(ModifierKeys.Control) ? MOD_CONTROL : 0)
        | (modifiers.HasFlag(ModifierKeys.Shift) ? MOD_SHIFT : 0)
        | (modifiers.HasFlag(ModifierKeys.Windows) ? MOD_WIN : 0);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _active.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            _window.Dispatcher.InvokeAsync(action); // run after this message, so dialogs (like the PIN box) behave
        }

        return IntPtr.Zero;
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterAll();
        _window.Dispose();
    }
}

/// <summary>Hotkeys as text ("Ctrl+Alt+1"), and hotkey boxes you fill in by pressing the keys.</summary>
internal static class HotkeyText
{
    public static string Format(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => "Num " + ((int)key - (int)Key.NumPad0),
            _ => key.ToString()
        });
        return string.Join("+", parts);
    }

    public static bool TryParse(string? text, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        foreach (string part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "win" or "windows":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    return false;
            }
        }

        string last = parts[^1];
        if (last.Length == 1 && char.IsAsciiDigit(last[0]))
        {
            key = Key.D0 + (last[0] - '0');
        }
        else if (last.StartsWith("Num ", StringComparison.OrdinalIgnoreCase) && int.TryParse(last[4..], out int n) && n is >= 0 and <= 9)
        {
            key = Key.NumPad0 + n;
        }
        else if (!Enum.TryParse(last, ignoreCase: true, out key))
        {
            return false;
        }

        // Needs Ctrl, Alt or Win (Shift alone is just typing), except F13 to F24.
        return key != Key.None && ((modifiers & ~ModifierKeys.Shift) != ModifierKeys.None || IsStandaloneKey(key));
    }

    /// <summary>The same combination written the standard way ("alt+ctrl+1" becomes "Ctrl+Alt+1"), or null if empty.</summary>
    public static string? Normalize(string? text) =>
        TryParse(text, out var modifiers, out var key) ? Format(modifiers, key) : null;

    /// <summary>F13 to F24 (handy on macro pads) can be used without Ctrl, Alt, Shift or Win.</summary>
    public static bool IsStandaloneKey(Key key) => key is >= Key.F13 and <= Key.F24;

    /// <summary>What in LaunchStage already uses this combination ("the game picker", "Stream"), or null.</summary>
    public static string? FindOwner(string combo, string? exceptProfile, bool includeSettings = true)
    {
        string? wanted = Normalize(combo);
        if (wanted == null)
        {
            return null;
        }

        if (includeSettings && Normalize(App.Instance.Settings.GamePickerHotkey) == wanted)
        {
            return "the game picker";
        }

        foreach (var profile in ProfileStore.LoadAll())
        {
            if (exceptProfile != null && string.Equals(profile.Name, exceptProfile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Normalize(profile.Hotkey) == wanted)
            {
                return $"opening or closing '{profile.Name}'";
            }

            if (Normalize(profile.ResnapHotkey) == wanted)
            {
                return $"putting '{profile.Name}' windows back";
            }
        }

        return null;
    }

    /// <summary>
    /// Makes a text box capture a key combination: click it and press the keys. Backspace, Delete or Esc clears it.
    /// LaunchStage's own hotkeys are paused while the box has focus.
    /// </summary>
    public static void Attach(TextBox box)
    {
        box.IsReadOnly = true;
        box.IsReadOnlyCaretVisible = false;
        bool paused = false;

        box.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Tab)
            {
                return; // let Tab move to the next control
            }

            e.Handled = true;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
                or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed)
            {
                return; // wait for the actual key
            }

            var modifiers = Keyboard.Modifiers;
            if (modifiers == ModifierKeys.None && key is Key.Back or Key.Delete or Key.Escape)
            {
                box.Text = "";
                return;
            }

            // A plain key or Shift+key is normal typing; it needs Ctrl, Alt or Win (except F13 to F24).
            if ((modifiers & ~ModifierKeys.Shift) == ModifierKeys.None && !IsStandaloneKey(key))
            {
                return;
            }

            box.Text = Format(modifiers, key);
        };

        box.GotKeyboardFocus += (_, _) =>
        {
            if (!paused)
            {
                paused = true;
                App.Instance.Hotkeys?.Pause();
            }
        };

        void Release()
        {
            if (paused)
            {
                paused = false;
                App.Instance.Hotkeys?.Resume();
            }
        }

        box.LostKeyboardFocus += (_, _) => Release();
        box.Unloaded += (_, _) => Release();
    }
}
