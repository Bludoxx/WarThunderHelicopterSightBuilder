namespace HeliSightBuilder.Native;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args is ["--build-test", var template, var source, var output])
        {
            VromfsPackage.Build(template, source, output);
            return 0;
        }
        if (args is ["--embedded-build-test", var embeddedOutput])
        {
            var resources = Path.Combine(Path.GetTempPath(), "HeliSightBuilder",
                "embedded-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                EmbeddedResources.ExtractTo(resources);
                VromfsPackage.Build(
                    Path.Combine(resources, "template", "pkg_user", "base.vromfs.bin"),
                    Path.Combine(resources, "source"),
                    embeddedOutput);
            }
            finally
            {
                if (Directory.Exists(resources)) Directory.Delete(resources, true);
            }
            return 0;
        }

        ApplicationConfiguration.Initialize();
        if (args is ["--ui-test", var report])
        {
            using var form = new MainForm();
            form.RunInteractionQualityChecks(report);
            return 0;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) => ReportUnhandledError(eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception) WriteCrashLog(exception);
        };
        Application.Run(new MainForm());
        return 0;
    }

    private static void ReportUnhandledError(Exception exception)
    {
        var log = WriteCrashLog(exception);
        MessageBox.Show(
            $"The editor recovered from an unexpected error.\n\n" +
            $"No invalid state was saved. Diagnostic log:\n{log}",
            "War Thunder Helicopter Sight Builder",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static string WriteCrashLog(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HeliSightBuilder", "logs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(path, exception.ToString());
            return path;
        }
        catch
        {
            return "The diagnostic log could not be written.";
        }
    }
}
