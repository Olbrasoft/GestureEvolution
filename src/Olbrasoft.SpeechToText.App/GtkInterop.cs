using System.Runtime.InteropServices;

namespace Olbrasoft.SpeechToText.App;

/// <summary>
/// P/Invoke bindings for libayatana-appindicator3.
/// Provides system tray icon functionality on Linux/GNOME.
/// </summary>
internal static class AppIndicator
{
    private const string LibName = "libayatana-appindicator3.so.1";

    public enum Category
    {
        ApplicationStatus = 0,
        Communications = 1,
        SystemServices = 2,
        Hardware = 3,
        Other = 4
    }

    public enum Status
    {
        Passive = 0,
        Active = 1,
        Attention = 2
    }

    [DllImport(LibName)]
    public static extern IntPtr app_indicator_new(string id, string icon_name, Category category);

    [DllImport(LibName)]
    public static extern void app_indicator_set_status(IntPtr indicator, Status status);

    [DllImport(LibName)]
    public static extern void app_indicator_set_menu(IntPtr indicator, IntPtr menu);

    [DllImport(LibName)]
    public static extern void app_indicator_set_icon_theme_path(IntPtr indicator, string icon_theme_path);

    [DllImport(LibName)]
    public static extern void app_indicator_set_title(IntPtr indicator, string title);

    [DllImport(LibName)]
    public static extern void app_indicator_set_icon(IntPtr indicator, string icon_name);

    [DllImport(LibName)]
    public static extern void app_indicator_set_icon_full(IntPtr indicator, string icon_name, string icon_desc);
}

/// <summary>
/// P/Invoke bindings for GTK3.
/// </summary>
internal static class Gtk
{
    private const string LibName = "libgtk-3.so.0";

    [DllImport(LibName)]
    public static extern void gtk_init(ref int argc, ref IntPtr argv);

    [DllImport(LibName)]
    public static extern void gtk_main();

    [DllImport(LibName)]
    public static extern void gtk_main_quit();

    [DllImport(LibName)]
    public static extern IntPtr gtk_menu_new();

    [DllImport(LibName)]
    public static extern IntPtr gtk_menu_item_new_with_label(string label);

    [DllImport(LibName)]
    public static extern IntPtr gtk_separator_menu_item_new();

    [DllImport(LibName)]
    public static extern void gtk_menu_shell_append(IntPtr menu_shell, IntPtr child);

    [DllImport(LibName)]
    public static extern void gtk_widget_show_all(IntPtr widget);

    [DllImport(LibName)]
    public static extern void gtk_widget_set_sensitive(IntPtr widget, bool sensitive);

    [DllImport(LibName)]
    public static extern void gtk_menu_item_set_label(IntPtr menu_item, string label);

    // About dialog functions
    [DllImport(LibName)]
    public static extern IntPtr gtk_about_dialog_new();

    [DllImport(LibName)]
    public static extern void gtk_about_dialog_set_program_name(IntPtr about, string name);

    [DllImport(LibName)]
    public static extern void gtk_about_dialog_set_version(IntPtr about, string version);

    [DllImport(LibName)]
    public static extern void gtk_about_dialog_set_comments(IntPtr about, string comments);

    [DllImport(LibName)]
    public static extern void gtk_about_dialog_set_website(IntPtr about, string website);

    [DllImport(LibName)]
    public static extern void gtk_about_dialog_set_website_label(IntPtr about, string website_label);

    [DllImport(LibName)]
    public static extern void gtk_about_dialog_set_logo_icon_name(IntPtr about, string icon_name);

    [DllImport(LibName)]
    public static extern int gtk_dialog_run(IntPtr dialog);

    [DllImport(LibName)]
    public static extern void gtk_widget_destroy(IntPtr widget);

    [DllImport(LibName)]
    public static extern void gtk_window_set_title(IntPtr window, string title);
}

/// <summary>
/// P/Invoke bindings for GLib.
/// </summary>
internal static class GLib
{
    private const string LibName = "libglib-2.0.so.0";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate bool GSourceFunc(IntPtr data);

    [DllImport(LibName)]
    public static extern uint g_idle_add(GSourceFunc function, IntPtr data);

    [DllImport(LibName)]
    public static extern uint g_timeout_add(uint interval, GSourceFunc function, IntPtr data);

    [DllImport(LibName)]
    public static extern bool g_source_remove(uint tag);
}

/// <summary>
/// P/Invoke bindings for GObject.
/// </summary>
internal static class GObject
{
    private const string LibName = "libgobject-2.0.so.0";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void GCallback(IntPtr widget, IntPtr data);

    [DllImport(LibName)]
    public static extern ulong g_signal_connect_data(
        IntPtr instance,
        string detailed_signal,
        GCallback c_handler,
        IntPtr data,
        IntPtr destroy_data,
        int connect_flags);
}
