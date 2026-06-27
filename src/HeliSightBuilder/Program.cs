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
            var resources = Path.Combine(AppContext.BaseDirectory, "Resources");
            VromfsPackage.Build(
                Path.Combine(resources, "template", "pkg_user", "base.vromfs.bin"),
                Path.Combine(resources, "source"),
                embeddedOutput);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        if (args is ["--ui-test", var report])
        {
            using var form = new MainForm();
            File.WriteAllLines(report, form.RunInteractionQualityChecks());
            return 0;
        }
        Application.Run(new MainForm());
        return 0;
    }
}
