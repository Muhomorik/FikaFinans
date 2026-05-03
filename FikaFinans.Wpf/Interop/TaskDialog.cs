using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FikaFinans.Wpf.Interop;

/// <summary>
/// P/Invoke wrapper for the Win32 <c>TaskDialog</c> API in <c>comctl32.dll</c> v6, giving
/// the modern Windows 11 dialog (rounded corners, Segoe UI Variable, themed icons) that
/// <see cref="MessageBox"/> can't render — WPF's MessageBox still calls the legacy User32
/// path. WPF apps already ship the v6 manifest this API needs, so no extra wiring.
/// </summary>
internal static class TaskDialog
{
    [Flags]
    private enum CommonButtons
    {
        Ok = 0x0001,
    }

    // commctrl.h: MAKEINTRESOURCEW(-1) etc — cast through ushort lands on 0xFFFF, 0xFFFD, ...
    private static readonly IntPtr WarningIcon = new(unchecked((ushort)-1));
    private static readonly IntPtr InformationIcon = new(unchecked((ushort)-3));

    [DllImport("comctl32.dll", EntryPoint = "TaskDialog", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
    private static extern void TaskDialogNative(
        IntPtr hwndParent,
        IntPtr hInstance,
        string pszWindowTitle,
        string? pszMainInstruction,
        string? pszContent,
        CommonButtons dwCommonButtons,
        IntPtr pszIcon,
        out int pnButton);

    public static void ShowWarning(Window? owner, string title, string mainInstruction, string content)
        => Show(owner, title, mainInstruction, content, WarningIcon);

    public static void ShowInformation(Window? owner, string title, string mainInstruction, string content)
        => Show(owner, title, mainInstruction, content, InformationIcon);

    private static void Show(Window? owner, string title, string mainInstruction, string content, IntPtr icon)
    {
        var hwnd = owner is null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle;
        TaskDialogNative(hwnd, IntPtr.Zero, title, mainInstruction, content, CommonButtons.Ok, icon, out _);
    }
}
