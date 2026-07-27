using System.Diagnostics;
using System.Reflection;

namespace SetupBootstrapper;

public class InstallerForm : Form
{
    private const int FORM_W = 560;
    private const int FORM_H = 420;
    private Panel headerPanel, contentPanel, bottomPanel;
    private Label titleLabel, subtitleLabel, statusLabel;
    private ProgressBar progressBar;
    private Button nextBtn, cancelBtn;
    private CheckBox shortcutCheck;
    private Label descLabel;
    private FlowLayoutPanel iconPanel;
    private int step;

    public InstallerForm()
    {
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(FORM_W, FORM_H);
        Text = "DR Bahig Books Portal — Setup";
        BackColor = Color.FromArgb(30, 34, 50);
        Font = new Font("Segoe UI", 10);

        // ─── Header ───
        headerPanel = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = Color.FromArgb(22, 25, 38) };
        iconPanel = new FlowLayoutPanel
        {
            Location = new Point(18, 14), Size = new Size(52, 52),
            BackColor = Color.Transparent
        };
        try
        {
            var ico = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            if (ico != null)
                iconPanel.Controls.Add(new PictureBox { Image = ico.ToBitmap(), Size = new Size(52, 52), SizeMode = PictureBoxSizeMode.Zoom });
        }
        catch { }
        titleLabel = new Label
        {
            Location = new Point(82, 18), AutoSize = true,
            Text = "DR Bahig Books Portal", Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = Color.White
        };
        subtitleLabel = new Label
        {
            Location = new Point(82, 46), AutoSize = true,
            Text = "Print Agent Installation", Font = new Font("Segoe UI", 10),
            ForeColor = Color.FromArgb(180, 180, 200)
        };
        headerPanel.Controls.AddRange([iconPanel, titleLabel, subtitleLabel]);

        // ─── Bottom ───
        bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Color.FromArgb(22, 25, 38) };
        cancelBtn = new Button
        {
            Text = "Cancel", Location = new Point(FORM_W - 190, 14), Size = new Size(80, 30),
            FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(50, 54, 70),
            ForeColor = Color.White, FlatAppearance = { BorderColor = Color.FromArgb(80, 84, 100) },
            Cursor = Cursors.Hand
        };
        cancelBtn.Click += (_, _) => Application.Exit();
        nextBtn = new Button
        {
            Text = "Install", Location = new Point(FORM_W - 100, 14), Size = new Size(85, 30),
            FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(220, 38, 38),
            ForeColor = Color.White, FlatAppearance = { BorderColor = Color.FromArgb(220, 38, 38) },
            Cursor = Cursors.Hand, Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        nextBtn.Click += NextClick;
        bottomPanel.Controls.AddRange([cancelBtn, nextBtn]);

        // ─── Content ───
        contentPanel = new Panel
        {
            Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 34, 50),
            Padding = new Padding(32, 24, 32, 0)
        };
        Controls.AddRange([headerPanel, contentPanel, bottomPanel]);

        ShowWelcome();
    }

    private void ClearContent()
    {
        contentPanel.Controls.Clear();
        statusLabel = null; progressBar = null; descLabel = null; shortcutCheck = null;
    }

    private void ShowWelcome()
    {
        step = 0;
        ClearContent();
        nextBtn.Text = "Install";
        cancelBtn.Text = "Cancel";

        var desc = new Label
        {
            Text = "This will install the DR Bahig Books Print Agent on your computer.\n\nThe agent runs in the background and allows printing books directly from the portal to your local printer.",
            Location = new Point(0, 0), Size = new Size(FORM_W - 64, 80),
            ForeColor = Color.FromArgb(200, 200, 215), Font = new Font("Segoe UI", 10)
        };

        var infoTitle = new Label
        {
            Text = "Installation includes:", Location = new Point(0, 100),
            AutoSize = true, ForeColor = Color.FromArgb(180, 180, 200),
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };

        var items = new List<string>
        {
            "  \u2022  Print agent service (auto-start on boot)",
            "  \u2022  Desktop shortcut to manage the agent",
            "  \u2022  Background printer detection (USB, WiFi, LAN)"
        };
        var y = 126;
        foreach (var item in items)
        {
            var l = new Label { Text = item, Location = new Point(0, y), AutoSize = true, ForeColor = Color.FromArgb(160, 160, 180) };
            contentPanel.Controls.Add(l);
            y += 26;
        }
        descLabel = desc;
        contentPanel.Controls.AddRange([desc, infoTitle]);
    }

    private async void NextClick(object? sender, EventArgs e)
    {
        if (step == 0)
        {
            // Start installation
            step = 1;
            ShowInstalling();
            await RunInstallation();
            step = 2;
            ShowCompleted();
        }
        else if (step == 2)
        {
            CreateShortcut();
            Application.Exit();
        }
    }

    private void ShowInstalling()
    {
        ClearContent();
        nextBtn.Enabled = false;
        nextBtn.Text = "Installing...";
        cancelBtn.Enabled = false;

        progressBar = new ProgressBar
        {
            Location = new Point(0, 20), Size = new Size(FORM_W - 64, 22),
            Style = ProgressBarStyle.Continuous, Minimum = 0, Maximum = 100,
            ForeColor = Color.FromArgb(220, 38, 38), BackColor = Color.FromArgb(50, 54, 70)
        };
        statusLabel = new Label
        {
            Location = new Point(0, 54), AutoSize = true,
            ForeColor = Color.FromArgb(200, 200, 215), Font = new Font("Segoe UI", 10),
            Text = "Preparing..."
        };
        contentPanel.Controls.AddRange([progressBar, statusLabel]);
    }

    private async Task RunInstallation()
    {
        try
        {
            statusLabel.Text = "Extracting agent files...";
            progressBar.Value = 10;
            await Task.Delay(100);

            var resourceDir = Path.Combine(Path.GetTempPath(), "BkSetup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(resourceDir);

            string FindResource(string name)
            {
                var asm = Assembly.GetExecutingAssembly();
                foreach (var r in asm.GetManifestResourceNames())
                    if (r.EndsWith(name, StringComparison.OrdinalIgnoreCase)) return r;
                throw new InvalidOperationException("Resource not found: " + name);
            }

            void ExtractResource(string name, string destDir)
            {
                var asm = Assembly.GetExecutingAssembly();
                var fullName = FindResource(name);
                using var stream = asm.GetManifestResourceStream(fullName);
                if (stream == null) return;
                var path = Path.Combine(destDir, name);
                using var file = File.Create(path);
                stream.CopyTo(file);
            }

            ExtractResource("BookShopPrintAgent.exe", resourceDir);
            progressBar.Value = 30;
            statusLabel.Text = "Copying to Program Files...";
            await Task.Delay(100);

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var installDir = Path.Combine(programFiles, "BookShopPrintAgent");
            Directory.CreateDirectory(installDir);

            // Kill any running agent process that might lock the file
            foreach (var proc in Process.GetProcessesByName("BookShopPrintAgent"))
            {
                try { proc.Kill(); proc.WaitForExit(5000); } catch { }
            }
            await Task.Delay(500);

            File.Copy(Path.Combine(resourceDir, "BookShopPrintAgent.exe"), Path.Combine(installDir, "BookShopPrintAgent.exe"), true);
            ExtractResource("appsettings.json", resourceDir);
            File.Copy(Path.Combine(resourceDir, "appsettings.json"), Path.Combine(installDir, "appsettings.json"), true);

            progressBar.Value = 60;
            statusLabel.Text = "Creating scheduled task (auto-start on boot)...";
            await Task.Delay(100);

            var taskPsi = new ProcessStartInfo
            {
                FileName = "schtasks",
                Arguments = "/create /tn \"BookShopPrintAgent\" /tr \"'" + Path.Combine(installDir, "BookShopPrintAgent.exe") + "'\" /sc onstart /ru SYSTEM /rl highest /f",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                Verb = "runas"
            };
            using var taskProc = Process.Start(taskPsi);
            taskProc?.WaitForExit(10000);

            progressBar.Value = 80;
            statusLabel.Text = "Starting print agent...";
            await Task.Delay(100);

            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(installDir, "BookShopPrintAgent.exe"),
                WorkingDirectory = installDir,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            });

            progressBar.Value = 100;
            await Task.Delay(200);

            try { Directory.Delete(resourceDir, true); } catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Installation failed:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Exit();
        }
    }

    private void ShowCompleted()
    {
        ClearContent();
        nextBtn.Enabled = true;
        nextBtn.Text = "Finish";
        cancelBtn.Text = "Close";
        cancelBtn.Enabled = true;

        var completeIcon = new Label
        {
            Text = "\u2713", Font = new Font("Segoe UI", 36, FontStyle.Bold),
            Location = new Point(FORM_W / 2 - 100, 10), AutoSize = true,
            ForeColor = Color.FromArgb(16, 185, 129)
        };

        var completeTitle = new Label
        {
            Text = "Installation Complete", Font = new Font("Segoe UI", 16, FontStyle.Bold),
            Location = new Point(FORM_W / 2 - 110, 70), AutoSize = true,
            ForeColor = Color.White
        };

        var completeDesc = new Label
        {
            Text = "The DR Bahig Books Print Agent is now running\nin the background and will start automatically\non every boot.",
            Location = new Point(0, 110), Size = new Size(FORM_W - 64, 70),
            ForeColor = Color.FromArgb(180, 180, 200), TextAlign = ContentAlignment.TopCenter
        };

        shortcutCheck = new CheckBox
        {
            Text = "Create desktop shortcut to start/stop the agent",
            Location = new Point(30, 190), AutoSize = true,
            ForeColor = Color.FromArgb(200, 200, 215),
            Checked = true, Font = new Font("Segoe UI", 9)
        };

        contentPanel.Controls.AddRange([completeIcon, completeTitle, completeDesc, shortcutCheck]);
    }

    private void CreateShortcut()
    {
        if (shortcutCheck == null || !shortcutCheck.Checked) return;
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BookShopPrintAgent");
            var agentExe = Path.Combine(installDir, "BookShopPrintAgent.exe");
            if (!File.Exists(agentExe)) return;

            var psCmd = "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('" + desktop.Replace("'", "''") + "\\DR Bahig Books Portal.lnk');" +
                        "$s.TargetPath='" + agentExe.Replace("'", "''") + "';" +
                        "$s.WorkingDirectory='" + installDir.Replace("'", "''") + "';" +
                        "$s.Description='DR Bahig Books Print Agent';" +
                        "$s.IconLocation='" + agentExe.Replace("'", "''") + "';" +
                        "$s.Save()";

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -Command \"" + psCmd + "\"",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            });
        }
        catch { }
    }
}
