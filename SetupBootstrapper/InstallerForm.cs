using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

namespace SetupBootstrapper;

public class InstallerForm : Form
{
    private const int FORM_W = 680;
    private const int FORM_H = 540;
    private const string APP_NAME = "DR Bahig Books Portal";
    private static readonly Color Accent = Color.FromArgb(16, 185, 129);
    private static readonly Color BgDark = Color.FromArgb(18, 20, 30);
    private static readonly Color BgPanel = Color.FromArgb(24, 27, 38);
    private static readonly Color BgHeader = Color.FromArgb(14, 16, 24);
    private static readonly Color FgPrimary = Color.White;
    private static readonly Color FgSecondary = Color.FromArgb(170, 174, 190);
    private static readonly Color FgMuted = Color.FromArgb(120, 124, 140);

    private Panel sidePanel, headerPanel, contentPanel, bottomPanel;
    private Label welcomeStep, installStep, completeStep;
    private Label titleLabel, statusLabel;
    private ProgressBar progressBar;
    private Button installBtn, cancelBtn;
    private CheckBox shortcutCb, launchCb;
    private int step;

    private const int WELCOME = 0, INSTALLING = 1, COMPLETE = 2;

    public InstallerForm()
    {
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(FORM_W, FORM_H);
        Text = APP_NAME + " Setup";
        BackColor = BgDark;
        Font = new Font("Segoe UI", 10);
        Icon = LoadAppIcon();

        // ── Side panel (branding bar) ──
        sidePanel = new Panel
        {
            Dock = DockStyle.Left,
            Width = 150,
            BackColor = BgPanel
        };

        var sideIcon = new PictureBox
        {
            Size = new Size(56, 56),
            Location = new Point(47, 24),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = Icon?.ToBitmap()
        };
        var sideTitle = new Label
        {
            Text = APP_NAME,
            Location = new Point(15, 88),
            Width = 120,
            Height = 44,
            ForeColor = FgPrimary,
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            TextAlign = ContentAlignment.TopCenter
        };
        var sideSub = new Label
        {
            Text = "Print Agent",
            Location = new Point(15, 128),
            Width = 120,
            Height = 20,
            ForeColor = FgSecondary,
            Font = new Font("Segoe UI", 9),
            TextAlign = ContentAlignment.TopCenter
        };

        // Step indicators
        welcomeStep = CreateStepLabel("\u25CF  Welcome", 180);
        installStep = CreateStepLabel("\u25CB  Install", 210);
        completeStep = CreateStepLabel("\u25CB  Complete", 240);

        var sideLine = new Panel
        {
            Location = new Point(36, 176),
            Size = new Size(78, 2),
            BackColor = Color.FromArgb(50, 54, 70)
        };
        sidePanel.Controls.Add(sideLine);

        sidePanel.Controls.AddRange([sideIcon, sideTitle, sideSub, welcomeStep, installStep, completeStep]);

        // ── Header ──
        headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 80,
            BackColor = BgHeader
        };
        titleLabel = new Label
        {
            Location = new Point(24, 24),
            AutoSize = true,
            Text = "Welcome",
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            ForeColor = FgPrimary
        };
        headerPanel.Controls.Add(titleLabel);

        // ── Bottom bar ──
        bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            BackColor = BgPanel
        };

        cancelBtn = new Button
        {
            Text = "Cancel",
            Location = new Point(FORM_W - 190 - 120, 13),
            Size = new Size(100, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(50, 54, 70),
            ForeColor = FgSecondary,
            FlatAppearance = { BorderColor = Color.FromArgb(70, 74, 90) },
            Cursor = Cursors.Hand
        };
        cancelBtn.Click += (_, _) => Application.Exit();

        installBtn = new Button
        {
            Text = "Install",
            Location = new Point(FORM_W - 190, 13),
            Size = new Size(170, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Accent,
            ForeColor = Color.White,
            FlatAppearance = { BorderColor = Accent },
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        installBtn.Click += OnInstallClick;

        bottomPanel.Controls.AddRange([cancelBtn, installBtn]);

        // ── Content ──
        contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = BgDark,
            Padding = new Padding(32, 16, 32, 0)
        };

        Controls.AddRange([sidePanel, headerPanel, contentPanel, bottomPanel]);

        ShowWelcome();
    }

    private static Icon? LoadAppIcon()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames().FirstOrDefault(r => r.EndsWith("book.ico", StringComparison.OrdinalIgnoreCase));
            if (name != null)
                using (var s = asm.GetManifestResourceStream(name)!)
                    return new Icon(s);
        }
        catch { }
        return SystemIcons.Application;
    }

    private static Label CreateStepLabel(string text, int y)
    {
        return new Label
        {
            Text = text,
            Location = new Point(16, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 9),
            ForeColor = FgMuted
        };
    }

    private void SetStep(int s)
    {
        step = s;
        welcomeStep.Text = (s >= WELCOME ? "\u25CF" : "\u25CB") + "  Welcome";
        installStep.Text = (s >= INSTALLING ? "\u25CF" : "\u25CB") + "  Install";
        completeStep.Text = (s >= COMPLETE ? "\u25CF" : "\u25CB") + "  Complete";
        welcomeStep.ForeColor = s >= WELCOME ? Accent : FgMuted;
        installStep.ForeColor = s >= INSTALLING ? Accent : FgMuted;
        completeStep.ForeColor = s >= COMPLETE ? Accent : FgMuted;
    }

    private void ClearContent()
    {
        contentPanel.Controls.Clear();
        progressBar = null!;
        statusLabel = null!;
        shortcutCb = null!;
        launchCb = null!;
    }

    // ============================================================
    //  STEP 1: Welcome
    // ============================================================
    private void ShowWelcome()
    {
        SetStep(WELCOME);
        ClearContent();
        titleLabel.Text = "Welcome";
        installBtn.Text = "Install";
        cancelBtn.Text = "Cancel";
        installBtn.Enabled = true;
        cancelBtn.Enabled = true;

        var desc = new Label
        {
            Text = "This wizard will install the " + APP_NAME + " Print Agent on your computer.\n\nThe agent runs in the background and allows printing books directly from the web portal to your local printer.",
            Location = new Point(0, 4),
            Size = new Size(FORM_W - 220, 60),
            ForeColor = FgSecondary
        };

        var infoTitle = new Label
        {
            Text = "Installation includes:",
            Location = new Point(0, 80),
            AutoSize = true,
            ForeColor = FgPrimary,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };

        var items = new[]
        {
            ("\u2713", "Print agent service", "Auto-starts on boot via Windows Scheduled Task"),
            ("\u2713", "Desktop shortcut", "Double-click to open the agent dashboard"),
            ("\u2713", "Printer detection", "Automatically detects USB, WiFi, and LAN printers"),
            ("\u2713", "System tray app", "Manage the agent from the notification area"),
            ("\u2713", "Automatic updates", "Replace existing files on re-installation")
        };

        var y = 110;
        foreach (var (check, title, sub) in items)
        {
            var c = new Label
            {
                Text = check,
                Location = new Point(0, y),
                AutoSize = true,
                ForeColor = Accent,
                Font = new Font("Segoe UI", 12, FontStyle.Bold)
            };
            var t = new Label
            {
                Text = title,
                Location = new Point(24, y),
                AutoSize = true,
                ForeColor = FgPrimary
            };
            var s = new Label
            {
                Text = sub,
                Location = new Point(24, y + 18),
                AutoSize = true,
                ForeColor = FgMuted,
                Font = new Font("Segoe UI", 8)
            };
            contentPanel.Controls.AddRange([c, t, s]);
            y += 44;
        }
    }

    // ============================================================
    //  STEP 2: Install
    // ============================================================
    private void ShowInstalling()
    {
        SetStep(INSTALLING);
        ClearContent();
        titleLabel.Text = "Installing...";
        installBtn.Enabled = false;
        installBtn.Text = "Installing\u2026";
        cancelBtn.Enabled = false;

        var spinner = new Label
        {
            Text = "\u25E0\u25E1\u25E2\u25E3",
            Font = new Font("Segoe UI", 28),
            Location = new Point(FORM_W / 2 - 200 - 60, 20),
            AutoSize = true,
            ForeColor = Accent
        };

        progressBar = new ProgressBar
        {
            Location = new Point(0, 80),
            Size = new Size(FORM_W - 280, 24),
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 100,
            ForeColor = Accent,
            BackColor = Color.FromArgb(40, 44, 58)
        };

        statusLabel = new Label
        {
            Text = "Preparing...",
            Location = new Point(0, 114),
            AutoSize = true,
            ForeColor = FgSecondary
        };

        var statusDetail = new Label
        {
            Text = "Please wait while the installation completes.",
            Location = new Point(0, 138),
            AutoSize = true,
            ForeColor = FgMuted,
            Font = new Font("Segoe UI", 9)
        };

        contentPanel.Controls.AddRange([progressBar, statusLabel, statusDetail]);
    }

    private async void OnInstallClick(object? sender, EventArgs e)
    {
        if (step == WELCOME)
        {
            ShowInstalling();
            await RunInstallation();
            ShowCompleted();
        }
        else if (step == COMPLETE)
        {
            if (launchCb?.Checked == true)
            {
                var installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BookShopPrintAgent");
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Path.Combine(installDir, "BookShopAgentUI.exe"),
                        WorkingDirectory = installDir,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true
                    });
                }
                catch { }
            }
            Application.Exit();
        }
    }

    private async Task RunInstallation()
    {
        try
        {
            var resourceDir = Path.Combine(Path.GetTempPath(), "BkSetup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(resourceDir);

            // ── Extract resources ──
            await ReportProgress(5, "Extracting installation files...");

            ExtractResource("BookShopPrintAgent.exe", resourceDir);
            ExtractResource("BookShopAgentUI.exe", resourceDir);
            ExtractResource("SumatraPDF-3.6.1-64.exe", resourceDir);
            ExtractResource("appsettings.json", resourceDir);
            ExtractResource("book.ico", resourceDir);

            // ── Prepare install directory ──
            await ReportProgress(15, "Preparing destination...");

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var installDir = Path.Combine(programFiles, "BookShopPrintAgent");
            Directory.CreateDirectory(installDir);

            // ── Kill running processes ──
            await ReportProgress(25, "Stopping any running agent processes...");

            KillProcess("BookShopPrintAgent");
            KillProcess("BookShopAgentUI");
            await Task.Delay(800);

            // ── Copy files ──
            await ReportProgress(35, "Cleaning old files...");
            // Remove old framework-dependent DLLs that conflict with single-file publish
            foreach (var oldFile in Directory.GetFiles(installDir, "*.dll"))
            {
                try { File.Delete(oldFile); } catch { }
            }
            foreach (var oldFile in Directory.GetFiles(installDir, "*.pdb"))
            {
                try { File.Delete(oldFile); } catch { }
            }
            try { File.Delete(Path.Combine(installDir, "BookShopPrintAgent.deps.json")); } catch { }
            try { File.Delete(Path.Combine(installDir, "BookShopPrintAgent.runtimeconfig.json")); } catch { }
            await Task.Delay(50);

            await ReportProgress(40, "Copying agent files...");
            File.Copy(Path.Combine(resourceDir, "BookShopPrintAgent.exe"), Path.Combine(installDir, "BookShopPrintAgent.exe"), true);
            await Task.Delay(50);

            await ReportProgress(45, "Copying agent UI...");
            File.Copy(Path.Combine(resourceDir, "BookShopAgentUI.exe"), Path.Combine(installDir, "BookShopAgentUI.exe"), true);
            await Task.Delay(50);

            await ReportProgress(48, "Copying SumatraPDF printer engine...");
            File.Copy(Path.Combine(resourceDir, "SumatraPDF-3.6.1-64.exe"), Path.Combine(installDir, "SumatraPDF-3.6.1-64.exe"), true);
            await Task.Delay(50);

            await ReportProgress(50, "Copying configuration (preserving existing)...");
            var destConfig = Path.Combine(installDir, "appsettings.json");
            if (!File.Exists(destConfig))
                File.Copy(Path.Combine(resourceDir, "appsettings.json"), destConfig, true);

            await ReportProgress(55, "Copying application icon...");
            File.Copy(Path.Combine(resourceDir, "book.ico"), Path.Combine(installDir, "book.ico"), true);

            // ── Create scheduled task ──
            await ReportProgress(65, "Creating scheduled task (auto-start on boot)...");
            RunProcess("schtasks", "/create /tn \"BookShopPrintAgent\" /tr \"'" + Path.Combine(installDir, "BookShopPrintAgent.exe") + "'\" /sc onstart /ru SYSTEM /rl highest /f");

            // ── Create uninstaller ──
            await ReportProgress(80, "Registering uninstaller...");
            CreateUninstallScript(installDir);
            RegisterUninstall(installDir);

            // ── Start agent UI ──
            await ReportProgress(90, "Starting agent...");
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(installDir, "BookShopAgentUI.exe"),
                WorkingDirectory = installDir,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            });

            await ReportProgress(100, "Installation complete!");
            await Task.Delay(400);

            try { Directory.Delete(resourceDir, true); } catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Installation failed:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Exit();
        }
    }

    private async Task ReportProgress(int value, string status)
    {
        if (progressBar != null && !progressBar.IsDisposed)
        {
            progressBar.Value = Math.Clamp(value, 0, 100);
            statusLabel.Text = status;
        }
        await Task.Delay(80);
    }

    // ============================================================
    //  STEP 3: Complete
    // ============================================================
    private void ShowCompleted()
    {
        SetStep(COMPLETE);
        ClearContent();
        titleLabel.Text = "Setup Complete";
        installBtn.Text = "Finish";
        cancelBtn.Text = "Close";
        installBtn.Enabled = true;
        cancelBtn.Enabled = true;

        var checkIcon = new Label
        {
            Text = "\u2713",
            Font = new Font("Segoe UI", 48, FontStyle.Bold),
            Location = new Point(FORM_W / 2 - 200 - 80, 12),
            AutoSize = true,
            ForeColor = Accent
        };

        var completeTitle = new Label
        {
            Text = APP_NAME + " Print Agent",
            Location = new Point(0, 76),
            AutoSize = true,
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = FgPrimary
        };

        var completeDesc = new Label
        {
            Text = "has been successfully installed on your computer.\nThe agent is now running in the background and ready to receive print jobs from the portal.",
            Location = new Point(0, 108),
            Size = new Size(FORM_W - 220, 52),
            ForeColor = FgSecondary
        };

        shortcutCb = new CheckBox
        {
            Text = "Create desktop shortcut",
            Location = new Point(0, 174),
            AutoSize = true,
            ForeColor = FgPrimary,
            Checked = true,
            Font = new Font("Segoe UI", 10)
        };

        launchCb = new CheckBox
        {
            Text = "Launch agent dashboard now",
            Location = new Point(0, 200),
            AutoSize = true,
            ForeColor = FgPrimary,
            Checked = true,
            Font = new Font("Segoe UI", 10)
        };

        var note = new Label
        {
            Text = "You can also manage the agent from the system tray.",
            Location = new Point(0, 236),
            AutoSize = true,
            ForeColor = FgMuted,
            Font = new Font("Segoe UI", 9)
        };

        contentPanel.Controls.AddRange([checkIcon, completeTitle, completeDesc, shortcutCb, launchCb, note]);

        // Create shortcut now (before user clicks Finish) so it's ready
        CreateDesktopShortcut();
    }

    // ============================================================
    //  HELPERS
    // ============================================================

    private static void KillProcess(string name)
    {
        foreach (var p in Process.GetProcessesByName(name))
        {
            try { p.Kill(); p.WaitForExit(3000); } catch { }
        }
    }

    private static void RunProcess(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                Verb = "runas"
            });
            p?.WaitForExit(10000);
        }
        catch { }
    }

    private static string FindResource(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        foreach (var r in asm.GetManifestResourceNames())
            if (r.EndsWith(name, StringComparison.OrdinalIgnoreCase)) return r;
        throw new InvalidOperationException("Resource not found: " + name);
    }

    private static void ExtractResource(string name, string destDir)
    {
        var asm = Assembly.GetExecutingAssembly();
        var fullName = FindResource(name);
        using var stream = asm.GetManifestResourceStream(fullName);
        if (stream == null) return;
        var path = Path.Combine(destDir, name);
        using var file = File.Create(path);
        stream.CopyTo(file);
    }

    // ============================================================
    //  SHORTCUT
    // ============================================================
    private void CreateDesktopShortcut()
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BookShopPrintAgent");
            var shortcutPath = Path.Combine(desktop, "DR Bahig Books Portal.lnk");
            var uiExe = Path.Combine(installDir, "BookShopAgentUI.exe");
            var iconPath = Path.Combine(installDir, "book.ico");
            if (!File.Exists(uiExe)) return;

            // Use WScript.Shell COM object via PowerShell
            var ps = "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('" + shortcutPath.Replace("'", "''") + "');" +
                     "$s.TargetPath='" + uiExe.Replace("'", "''") + "';" +
                     "$s.WorkingDirectory='" + installDir.Replace("'", "''") + "';" +
                     "$s.Description='Double-click to open the " + APP_NAME + " Print Agent dashboard. The agent starts automatically.';" +
                     "$s.IconLocation='" + iconPath.Replace("'", "''") + "';" +
                     "$s.Save()";

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -Command \"" + ps + "\"",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            });
        }
        catch { }
    }

    // ============================================================
    //  UNINSTALL
    // ============================================================
    private static void CreateUninstallScript(string installDir)
    {
        var psPath = Path.Combine(installDir, "uninstall.ps1");
        var psContent = @"# DR Bahig Books Portal Print Agent - Uninstaller
$dir = '" + installDir.Replace("'", "''") + @"'
$taskName = 'BookShopPrintAgent'

Write-Host 'Uninstalling DR Bahig Books Portal Print Agent...' -ForegroundColor Cyan

# Kill processes
Stop-Process -Name 'BookShopPrintAgent' -Force -ErrorAction SilentlyContinue
Stop-Process -Name 'BookShopAgentUI' -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

# Remove scheduled task
& schtasks /delete /tn $taskName /f 2>`$null

# Remove desktop shortcut
$shortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'DR Bahig Books Portal.lnk'
Remove-Item $shortcut -Force -ErrorAction SilentlyContinue

# Remove install directory
Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue

# Remove uninstall registry keys
reg delete 'HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\DR Bahig Books Portal' /f 2>`$null
reg delete 'HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\DR Bahig Books Portal' /f 2>`$null

Write-Host 'Uninstall complete.' -ForegroundColor Green
Start-Sleep -Seconds 2
";
        File.WriteAllText(psPath, psContent);
    }

    private static void RegisterUninstall(string installDir)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\DR Bahig Books Portal");
            key.SetValue("DisplayName", APP_NAME + " Print Agent");
            key.SetValue("DisplayVersion", "1.0.0");
            key.SetValue("Publisher", "DR Bahig Books");
            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            key.SetValue("DisplayIcon", Path.Combine(installDir, "book.ico"));
            key.SetValue("InstallLocation", installDir);
            key.SetValue("UninstallString", "powershell -NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(installDir, "uninstall.ps1") + "\"");
            key.SetValue("QuietUninstallString", "powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + Path.Combine(installDir, "uninstall.ps1") + "\"");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", 255000, RegistryValueKind.DWord);
        }
        catch { }
    }
}
