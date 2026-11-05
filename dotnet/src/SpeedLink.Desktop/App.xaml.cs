using System.Windows;
namespace SpeedLink.Desktop;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        InstallCrashReporting();
        if (await RunCrashChecks(e.Args)) return;
        if (e.Args.Contains("--gen1-flash-smoke", StringComparer.Ordinal))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            string Get(string key)
            {
                int at = Array.IndexOf(e.Args, key);
                return at >= 0 && at + 1 < e.Args.Length ? e.Args[at + 1] : throw new ArgumentException("Missing " + key);
            }
            BenchFlashWindow? flash = null;
            try
            {
                string output = Get("--output");
                var fixture = await Task.Run(() => BenchFlashUiPeerState.Create(output));
                flash = new BenchFlashWindow(System.IO.Path.Combine(output, "data"), fixture.Peer.Open,
                    (fixture.Inspector, fixture.CodeGuard)) { ShowActivated = false };
                MainWindow = flash;
                var report = await flash.Smoke(fixture.Backup, fixture.Backup, output, fixture.Peer.SimulateInterruptedWrite);
                System.IO.File.WriteAllText(Get("--smoke-report"), System.Text.Json.JsonSerializer.Serialize(report));
                flash.Close();
                Shutdown(0);
            }
            catch (Exception error)
            {
                System.IO.File.WriteAllText(Get("--smoke-report"), System.Text.Json.JsonSerializer.Serialize(new { passed = false, error = error.ToString() }));
                flash?.Close();
                Shutdown(1);
            }
            return;
        }
        if (e.Args.Contains("--menus-smoke", StringComparer.Ordinal) || e.Args.Contains("--support-smoke", StringComparer.Ordinal))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            string GetMenu(string key) { int at = Array.IndexOf(e.Args, key); return at >= 0 && at + 1 < e.Args.Length ? e.Args[at + 1] : throw new ArgumentException("Missing " + key); }
            MainWindow? menuWindow = null;
            try
            {
                string output = GetMenu("--output"); System.IO.Directory.CreateDirectory(output);
                menuWindow = new MainWindow(System.IO.Path.Combine(output, "data")); MainWindow = menuWindow;
                var result = e.Args.Contains("--support-smoke", StringComparer.Ordinal) ? await menuWindow.ExerciseDiagnosticsOffline(output) : await menuWindow.ExerciseMenusOffline(output);
                System.IO.File.WriteAllText(GetMenu("--smoke-report"), System.Text.Json.JsonSerializer.Serialize(result));
                menuWindow.CloseWorkbenchTest(); Shutdown(0);
            }
            catch (Exception error)
            {
                System.IO.File.WriteAllText(GetMenu("--smoke-report"), System.Text.Json.JsonSerializer.Serialize(new { passed = false, error = error.ToString() }));
                menuWindow?.CloseWorkbenchTest(); Shutdown(1);
            }
            return;
        }
        if (e.Args.Contains("--editor-smoke", StringComparer.Ordinal))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            string GetEditor(string key) { int at = Array.IndexOf(e.Args, key); return at >= 0 && at + 1 < e.Args.Length ? e.Args[at + 1] : throw new ArgumentException("Missing " + key); }
            EditorWindow? editor = null;
            try
            {
                editor = new(); MainWindow = editor;
                var result = await editor.ExerciseOffline(GetEditor("--file"), GetEditor("--output"));
                System.IO.File.WriteAllText(GetEditor("--smoke-report"), System.Text.Json.JsonSerializer.Serialize(result)); editor.Close(); Shutdown(0);
            }
            catch (Exception error)
            {
                System.IO.File.WriteAllText(GetEditor("--smoke-report"), System.Text.Json.JsonSerializer.Serialize(new { passed = false, error = error.ToString() })); editor?.CloseOfflineTest(); Shutdown(1);
            }
            return;
        }
        if (e.Args.Contains("--workbench-smoke", StringComparer.Ordinal))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            string GetWorkbench(string key) { int at = Array.IndexOf(e.Args, key); return at >= 0 && at + 1 < e.Args.Length ? e.Args[at + 1] : throw new ArgumentException("Missing " + key); }
            MainWindow? workbench = null;
            try
            {
                workbench = new MainWindow(); MainWindow = workbench;
                var result = await workbench.ExerciseWorkbenchOffline(GetWorkbench("--file"), GetWorkbench("--output"), e.Args.Contains("--recording", StringComparer.Ordinal) ? GetWorkbench("--recording") : null);
                System.IO.File.WriteAllText(GetWorkbench("--smoke-report"), System.Text.Json.JsonSerializer.Serialize(result)); workbench.CloseWorkbenchTest(); Shutdown(0);
            }
            catch (Exception error)
            {
                System.IO.File.WriteAllText(GetWorkbench("--smoke-report"), System.Text.Json.JsonSerializer.Serialize(new { passed = false, error = error.ToString() })); workbench?.CloseWorkbenchTest(); Shutdown(1);
            }
            return;
        }
        if (e.Args.Contains("--editor", StringComparer.Ordinal))
        {
            var editor = new MainWindow(); crashWindow = editor; MainWindow = editor; editor.Show(); OfferCrashReports(editor); editor.OpenEditorFile();
            int at = Array.IndexOf(e.Args, "--file");
            if (at >= 0 && at + 1 < e.Args.Length)
                try { editor.OpenEditorFile(e.Args[at + 1]); } catch (Exception error) { MessageBox.Show(editor, error.Message, "SpeedLink ROM editor"); }
            return;
        }
        if (e.Args.Contains("--replay-smoke", StringComparer.Ordinal))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            string Get(string key)
            {
                var at = Array.IndexOf(e.Args, key);
                return at >= 0 && at + 1 < e.Args.Length ? e.Args[at + 1] : throw new ArgumentException("Missing " + key);
            }
            LogViewerWindow? viewer = null;
            try
            {
                viewer = new(); MainWindow = viewer;
                var result = await viewer.ExerciseOffline(Get("--file"), Get("--screenshot"));
                System.IO.File.WriteAllText(Get("--smoke-report"), System.Text.Json.JsonSerializer.Serialize(result));
                Shutdown(0);
            }
            catch (Exception error)
            {
                var at = Array.IndexOf(e.Args, "--smoke-report");
                if (at >= 0 && at + 1 < e.Args.Length)
                    System.IO.File.WriteAllText(e.Args[at + 1], System.Text.Json.JsonSerializer.Serialize(new { passed = false, error = error.ToString() }));
                Shutdown(1);
            }
            finally { viewer?.Close(); }
            return;
        }
        var window = new MainWindow();
        crashWindow = window;
        MainWindow = window;
        if (e.Args.Contains("--smoke-test", StringComparer.Ordinal))
        {
            try
            {
                window.RunSmoke(e.Args);
                Shutdown(0);
            }
            catch (Exception error)
            {
                var index = Array.IndexOf(e.Args, "--smoke-report");
                if (index >= 0 && index + 1 < e.Args.Length)
                    System.IO.File.WriteAllText(e.Args[index + 1], System.Text.Json.JsonSerializer.Serialize(new
                    {
                        passed = false,
                        error = error.ToString()
                    }));
                Shutdown(1);
            }
        }
        else
        {
            window.Show();
            OfferCrashReports(window);
        }
    }
}
