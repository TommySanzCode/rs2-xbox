using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace RS2XboxConnect;
public partial class App : Application
{
    private Mutex? singleton;
    protected override void OnStartup(StartupEventArgs e)
    {
        singleton = new Mutex(true, "Local\\RS2XboxConnect", out var created);
        if (!created) { MessageBox.Show("RS2 Xbox Connect is already running. Check its tray icon."); Shutdown(); return; }
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e) { singleton?.Dispose(); base.OnExit(e); }
}
