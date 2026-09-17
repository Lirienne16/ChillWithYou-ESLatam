using System.Diagnostics;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ChillWithYouSpanishInstaller;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            try
            {
                return args[0] switch
                {
                    "--verify-payloads" => Report(InstallerCore.VerifyEmbeddedPayloads()),
                    "--install" when args.Length == 2 => Report(InstallerCore.Install(args[1])),
                    "--uninstall" when args.Length == 2 => Report(InstallerCore.Uninstall(args[1])),
                    "--status" when args.Length == 2 => Report(InstallerCore.GetStatus(args[1])),
                    _ => 2,
                };
            }
            catch
            {
                return 1;
            }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerForm());
        return 0;
    }

    private static int Report(OperationResult result)
    {
        Console.WriteLine(result.Message);
        return result.Success ? 0 : 1;
    }

}

internal sealed class InstallerForm : Form
{
    private const int WelcomePage = 0;
    private const int FolderPage = 1;
    private const int ReadyPage = 2;
    private const int FinishPage = 3;

    private readonly Panel[] _pages = new Panel[4];
    private readonly TextBox _pathBox = new();
    private readonly Label _folderStatus = new();
    private readonly Label _readyTitle = new();
    private readonly Label _readyBody = new();
    private readonly Label _readySummary = new();
    private readonly Panel _maintenanceOptions = new();
    private readonly RadioButton _repairOption = new();
    private readonly RadioButton _removeOption = new();
    private readonly Label _finishTitle = new();
    private readonly Label _finishBody = new();
    private readonly Button _backButton = new();
    private readonly Button _nextButton = new();
    private readonly Button _cancelButton = new();
    private readonly Image? _portrait;
    private int _pageIndex;
    private InstallState _detectedState;
    private bool _operationRunning;

    public InstallerForm()
    {
        Text = "Asistente de instalación — Chill With You Español Latinoamérica";
        ClientSize = new Size(640, 440);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = SystemColors.Control;
        Font = new Font("Segoe UI", 9F);
        if (Icon.ExtractAssociatedIcon(Application.ExecutablePath) is { } icon)
            Icon = icon;

        _portrait = LoadPortrait();
        Controls.Add(BuildBanner());

        var pageHost = new Panel
        {
            Location = new Point(170, 0),
            Size = new Size(470, 370),
            BackColor = Color.White,
        };
        _pages[WelcomePage] = BuildWelcomePage();
        _pages[FolderPage] = BuildFolderPage();
        _pages[ReadyPage] = BuildReadyPage();
        _pages[FinishPage] = BuildFinishPage();
        foreach (var page in _pages)
            pageHost.Controls.Add(page);
        Controls.Add(pageHost);

        var footer = new Panel
        {
            Location = new Point(0, 370),
            Size = new Size(640, 70),
            BackColor = SystemColors.Control,
        };
        footer.Controls.Add(new Panel
        {
            Location = Point.Empty,
            Size = new Size(640, 1),
            BackColor = SystemColors.ControlDark,
        });
        footer.Controls.Add(new Label
        {
            Text = "Versión 1.0",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Location = new Point(12, 28),
        });

        ConfigureWizardButton(_backButton, "< Atrás", new Point(350, 22));
        ConfigureWizardButton(_nextButton, "Siguiente >", new Point(445, 22));
        ConfigureWizardButton(_cancelButton, "Cancelar", new Point(540, 22));
        _backButton.Click += (_, _) => ShowPage(Math.Max(WelcomePage, _pageIndex - 1));
        _nextButton.Click += (_, _) => Next();
        _cancelButton.Click += (_, _) => Close();
        footer.Controls.AddRange([_backButton, _nextButton, _cancelButton]);
        Controls.Add(footer);

        AcceptButton = _nextButton;
        CancelButton = _cancelButton;
        FormClosing += (_, eventArgs) =>
        {
            if (_operationRunning)
                eventArgs.Cancel = true;
        };
        Shown += async (_, _) =>
        {
            ShowPage(WelcomePage);
            await CheckForUpdatesAsync();
        };
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var update = await UpdateService.CheckAsync();
            if (update is null || IsDisposed)
                return;

            string notes = string.IsNullOrWhiteSpace(update.Notes) ? "" : $"\n\n{update.Notes}";
            var choice = MessageBox.Show(this,
                $"Hay una actualización del mod disponible: versión {update.Version}.{notes}\n\n" +
                "¿Quieres descargar y abrir el instalador actualizado?",
                "Actualización disponible",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (choice != DialogResult.Yes)
                return;

            _operationRunning = true;
            Enabled = false;
            UseWaitCursor = true;
            string originalTitle = Text;
            var progress = new Progress<int>(percentage =>
                Text = $"Descargando actualización… {percentage}%");
            string installer = await UpdateService.DownloadAsync(update, progress);
            Text = originalTitle;
            UpdateService.Launch(installer);
            _operationRunning = false;
            Close();
        }
        catch (HttpRequestException)
        {
            // Trabajar sin conexión no impide instalar la versión integrada.
        }
        catch (TaskCanceledException)
        {
            // Un tiempo de espera de red tampoco impide la instalación local.
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"No se pudo descargar la actualización.\n\n{ex.Message}",
                "Actualización no completada",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            if (!IsDisposed)
            {
                _operationRunning = false;
                Enabled = true;
                UseWaitCursor = false;
            }
        }
    }

    private Panel BuildBanner()
    {
        var banner = new Panel
        {
            Location = Point.Empty,
            Size = new Size(170, 370),
            BackColor = Color.FromArgb(22, 42, 112),
        };
        var picture = new PictureBox
        {
            Location = new Point(15, 34),
            Size = new Size(140, 140),
            Image = _portrait,
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(74, 130, 211),
        };
        banner.Controls.Add(picture);
        banner.Controls.Add(new Label
        {
            Text = "CHILL WITH YOU",
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(10, 196),
            Size = new Size(150, 28),
        });
        banner.Controls.Add(new Label
        {
            Text = "ESPAÑOL\nLATINOAMÉRICA",
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(205, 219, 255),
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.TopCenter,
            Location = new Point(10, 229),
            Size = new Size(150, 48),
        });
        banner.Controls.Add(new Label
        {
            Text = "- por Susumi",
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = Color.FromArgb(183, 200, 244),
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(10, 311),
            Size = new Size(150, 42),
        });
        return banner;
    }

    private Panel BuildWelcomePage()
    {
        var page = CreatePage();
        page.Controls.Add(NewLabel(
            "Bienvenido al Asistente de instalación",
            new Rectangle(22, 24, 420, 34), 16F, FontStyle.Bold));
        page.Controls.Add(NewLabel(
            "Chill With You — Español Latinoamérica",
            new Rectangle(24, 63, 414, 28), 11F, FontStyle.Bold));
        page.Controls.Add(NewLabel(
            "Este asistente instalará la traducción al español latinoamericano.\n\n" +
            "Se creará una copia de seguridad del archivo modificado. El mod no modifica tus partidas guardadas.\n\n" +
            "Cierra el juego antes de continuar.",
            new Rectangle(24, 112, 410, 155), 9F));
        page.Controls.Add(NewLabel(
            "Para continuar, haz clic en Siguiente.",
            new Rectangle(24, 318, 410, 24), 9F));
        return page;
    }

    private Panel BuildFolderPage()
    {
        var page = CreatePage();
        page.Controls.Add(NewLabel(
            "Selecciona la carpeta del juego",
            new Rectangle(22, 22, 420, 34), 15F, FontStyle.Bold));
        page.Controls.Add(NewLabel(
            "Elige la carpeta que contiene «Chill With You.exe».",
            new Rectangle(24, 64, 414, 25), 9F));
        page.Controls.Add(NewLabel("Carpeta de instalación:", new Rectangle(24, 112, 410, 22), 9F));

        _pathBox.Location = new Point(24, 137);
        _pathBox.Size = new Size(315, 25);
        _pathBox.Text = InstallerCore.FindGameDirectory() ?? "";
        _pathBox.TextChanged += (_, _) => RefreshFolderStatus();
        page.Controls.Add(_pathBox);

        var browseButton = new Button
        {
            Text = "Examinar…",
            Location = new Point(348, 135),
            Size = new Size(92, 28),
            UseVisualStyleBackColor = true,
        };
        browseButton.Click += (_, _) => Browse();
        page.Controls.Add(browseButton);

        _folderStatus.Location = new Point(24, 187);
        _folderStatus.Size = new Size(414, 58);
        _folderStatus.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        page.Controls.Add(_folderStatus);
        page.Controls.Add(NewLabel(
            "El asistente comprobará la versión antes de cambiar cualquier archivo.",
            new Rectangle(24, 282, 414, 42), 8.5F));
        return page;
    }

    private Panel BuildReadyPage()
    {
        var page = CreatePage();
        _readyTitle.Location = new Point(22, 22);
        _readyTitle.Size = new Size(420, 36);
        _readyTitle.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
        _readyBody.Location = new Point(24, 72);
        _readyBody.Size = new Size(414, 68);
        _readySummary.Location = new Point(24, 265);
        _readySummary.Size = new Size(414, 58);
        _readySummary.ForeColor = SystemColors.GrayText;

        _maintenanceOptions.Location = new Point(24, 145);
        _maintenanceOptions.Size = new Size(400, 94);
        _maintenanceOptions.BackColor = Color.White;
        _repairOption.Text = "Reparar la traducción";
        _repairOption.Location = new Point(10, 10);
        _repairOption.Size = new Size(260, 24);
        _removeOption.Text = "Desinstalar y restaurar los archivos originales";
        _removeOption.Location = new Point(10, 46);
        _removeOption.Size = new Size(350, 24);
        _repairOption.CheckedChanged += (_, _) => UpdateReadyButton();
        _removeOption.CheckedChanged += (_, _) => UpdateReadyButton();
        _maintenanceOptions.Controls.AddRange([_repairOption, _removeOption]);

        page.Controls.AddRange([_readyTitle, _readyBody, _maintenanceOptions, _readySummary]);
        return page;
    }

    private Panel BuildFinishPage()
    {
        var page = CreatePage();
        _finishTitle.Location = new Point(22, 24);
        _finishTitle.Size = new Size(420, 70);
        _finishTitle.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
        _finishBody.Location = new Point(24, 116);
        _finishBody.Size = new Size(414, 145);
        page.Controls.AddRange([_finishTitle, _finishBody]);
        page.Controls.Add(NewLabel(
            "Haz clic en Finalizar para cerrar el asistente.",
            new Rectangle(24, 318, 410, 24), 9F));
        return page;
    }

    private static Panel CreatePage()
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Visible = false,
        };
    }

    private static Label NewLabel(
        string text,
        Rectangle bounds,
        float size,
        FontStyle style = FontStyle.Regular)
    {
        return new Label
        {
            Text = text,
            Bounds = bounds,
            Font = new Font("Segoe UI", size, style),
        };
    }

    private static void ConfigureWizardButton(Button button, string text, Point location)
    {
        button.Text = text;
        button.Location = location;
        button.Size = new Size(88, 28);
        button.UseVisualStyleBackColor = true;
    }

    private static Image? LoadPortrait()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SusumiMod.Portrait");
        if (stream is null)
            return null;
        using var loaded = Image.FromStream(stream);
        return new Bitmap(loaded);
    }

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Selecciona la carpeta que contiene Chill With You.exe",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_pathBox.Text) ? _pathBox.Text : "",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _pathBox.Text = dialog.SelectedPath;
    }

    private void RefreshFolderStatus()
    {
        var result = InstallerCore.GetStatus(_pathBox.Text);
        _folderStatus.Text = result.Message;
        _folderStatus.ForeColor = result.State == InstallState.Unknown
            ? Color.DarkRed
            : Color.DarkGreen;
        if (_pageIndex == FolderPage)
            _nextButton.Enabled = result.State != InstallState.Unknown;
    }

    private void ShowPage(int index)
    {
        _pageIndex = index;
        for (var i = 0; i < _pages.Length; i++)
            _pages[i].Visible = i == index;

        _backButton.Enabled = index is FolderPage or ReadyPage;
        _cancelButton.Visible = index != FinishPage;
        _nextButton.Enabled = true;
        _nextButton.Text = index switch
        {
            WelcomePage => "Siguiente >",
            FolderPage => "Siguiente >",
            ReadyPage => "Instalar",
            FinishPage => "Finalizar",
            _ => "Siguiente >",
        };

        if (index == FolderPage)
            RefreshFolderStatus();
        else if (index == ReadyPage)
            ConfigureReadyPage();
    }

    private void Next()
    {
        switch (_pageIndex)
        {
            case WelcomePage:
                ShowPage(FolderPage);
                break;
            case FolderPage:
            {
                var status = InstallerCore.GetStatus(_pathBox.Text);
                if (status.State == InstallState.Unknown)
                {
                    MessageBox.Show(this, status.Message, "Carpeta no válida",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _detectedState = status.State;
                ShowPage(ReadyPage);
                break;
            }
            case ReadyPage:
                RunOperation();
                break;
            case FinishPage:
                Close();
                break;
        }
    }

    private void ConfigureReadyPage()
    {
        var status = InstallerCore.GetStatus(_pathBox.Text);
        if (status.State == InstallState.Unknown)
        {
            ShowPage(FolderPage);
            return;
        }
        _detectedState = status.State;
        _maintenanceOptions.Visible = status.State == InstallState.Installed;
        _readySummary.Text = $"Carpeta seleccionada:\n{_pathBox.Text}";

        switch (status.State)
        {
            case InstallState.Original:
                _readyTitle.Text = "Listo para instalar";
                _readyBody.Text = "El asistente ya tiene toda la información necesaria para instalar la traducción.";
                _nextButton.Text = "Instalar";
                break;
            case InstallState.UpdateAvailable:
                _readyTitle.Text = "Listo para actualizar";
                _readyBody.Text = "El juego o la traducción cambiaron. El asistente reaplicará la capa en español y conservará una copia de seguridad.";
                _nextButton.Text = "Actualizar";
                break;
            case InstallState.Installed:
                _readyTitle.Text = "Mantenimiento del mod";
                _readyBody.Text = "La traducción ya está instalada. Selecciona la operación que deseas realizar:";
                if (!_repairOption.Checked && !_removeOption.Checked)
                    _repairOption.Checked = true;
                UpdateReadyButton();
                break;
        }
    }

    private void UpdateReadyButton()
    {
        if (_pageIndex == ReadyPage && _detectedState == InstallState.Installed)
            _nextButton.Text = _removeOption.Checked ? "Desinstalar" : "Reparar";
    }

    private void RunOperation()
    {
        var uninstall = _detectedState == InstallState.Installed && _removeOption.Checked;
        _operationRunning = true;
        Enabled = false;
        UseWaitCursor = true;
        Application.DoEvents();

        OperationResult result;
        try
        {
            result = uninstall
                ? InstallerCore.Uninstall(_pathBox.Text)
                : InstallerCore.Install(_pathBox.Text);
        }
        finally
        {
            UseWaitCursor = false;
            Enabled = true;
            _operationRunning = false;
        }

        if (!result.Success)
        {
            MessageBox.Show(this, result.Message, "No se pudo completar la operación",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            ConfigureReadyPage();
            return;
        }

        _finishTitle.Text = uninstall
            ? "Desinstalación completada"
            : "Instalación completada";
        _finishBody.Text = result.Message + (uninstall
            ? "\n\nPuedes volver a instalar el mod cuando quieras con este mismo asistente."
            : "\n\nYa puedes iniciar Chill With You y disfrutar la traducción.");
        ShowPage(FinishPage);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _portrait?.Dispose();
        base.Dispose(disposing);
    }
}

#if false
internal sealed class InstallerForm : Form
{
    private readonly TextBox _pathBox = new();
    private readonly Label _status = new();
    private readonly Label _statusDot = new();
    private readonly RoundedPanel _statusPanel = new();
    private readonly RoundedButton _installButton = new();
    private readonly RoundedButton _uninstallButton = new();

    public InstallerForm()
    {
        SuspendLayout();
        Text = "Chill With You — Español Latinoamérica";
        ClientSize = new Size(900, 620);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Palette.Page;
        Font = new Font("Segoe UI", 10F);
        if (Icon.ExtractAssociatedIcon(Application.ExecutablePath) is { } icon)
            Icon = icon;

        var hero = new HeroPanel
        {
            Location = Point.Empty,
            Size = new Size(ClientSize.Width, 190),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        var badge = new Label
        {
            Text = "  TRADUCCIÓN NO OFICIAL  ",
            Font = new Font("Segoe UI Semibold", 8.5F),
            ForeColor = Color.White,
            BackColor = Palette.Rose,
            AutoSize = true,
            Location = new Point(42, 24),
            Padding = new Padding(3, 4, 3, 4),
        };
        var title = new Label
        {
            Text = "Chill With You",
            Font = new Font("Segoe UI Semibold", 27F),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(38, 58),
        };
        var subtitle = new Label
        {
            Text = "Español Latinoamérica",
            Font = new Font("Segoe UI", 17F),
            ForeColor = Color.FromArgb(220, 234, 255),
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(42, 105),
        };
        var author = new Label
        {
            Text = "Traducción no oficial realizada por Susumi",
            Font = new Font("Segoe UI", 10.5F),
            ForeColor = Color.FromArgb(188, 208, 244),
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(44, 148),
        };
        var avatar = new AvatarView
        {
            Location = new Point(716, 20),
            Size = new Size(150, 150),
            Image = LoadPortrait(),
            BackColor = Color.Transparent,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        hero.Controls.AddRange([badge, title, subtitle, author, avatar]);

        var pathLabel = new Label
        {
            Text = "UBICACIÓN DEL JUEGO",
            Font = new Font("Segoe UI Semibold", 9F),
            ForeColor = Palette.Muted,
            AutoSize = true,
            Location = new Point(40, 216),
        };
        var version = new Label
        {
            Text = "Versión 1.1.0",
            Font = new Font("Segoe UI Semibold", 9F),
            ForeColor = Palette.Blue,
            AutoSize = true,
            Location = new Point(778, 216),
        };
        _pathBox.Location = new Point(40, 243);
        _pathBox.Size = new Size(682, 34);
        _pathBox.Font = new Font("Segoe UI", 10.5F);
        _pathBox.BorderStyle = BorderStyle.FixedSingle;
        _pathBox.Text = InstallerCore.FindGameDirectory() ?? "";
        _pathBox.TextChanged += (_, _) => RefreshStatus();

        var browseButton = new RoundedButton
        {
            Text = "Examinar…",
            Location = new Point(738, 240),
            Size = new Size(122, 40),
            BackColor = Palette.SoftBlue,
            ForeColor = Palette.Navy,
            FlatStyle = FlatStyle.Flat,
            CornerRadius = 10,
        };
        browseButton.Click += (_, _) => Browse();

        _statusPanel.Location = new Point(40, 304);
        _statusPanel.Size = new Size(820, 68);
        _statusPanel.CornerRadius = 14;
        _statusPanel.FillColor = Palette.SoftBlue;
        _statusDot.Text = "●";
        _statusDot.Font = new Font("Segoe UI", 14F);
        _statusDot.AutoSize = true;
        _statusDot.Location = new Point(20, 20);
        _status.Location = new Point(51, 14);
        _status.Size = new Size(746, 42);
        _status.Font = new Font("Segoe UI Semibold", 10.5F);
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _statusPanel.Controls.AddRange([_statusDot, _status]);

        _installButton.Text = "Instalar traducción";
        _installButton.Location = new Point(40, 398);
        _installButton.Size = new Size(230, 52);
        _installButton.BackColor = Palette.Blue;
        _installButton.ForeColor = Color.White;
        _installButton.FlatStyle = FlatStyle.Flat;
        _installButton.Font = new Font("Segoe UI Semibold", 10.5F);
        _installButton.CornerRadius = 12;
        _installButton.Click += (_, _) => RunOperation(install: true);

        _uninstallButton.Text = "Desinstalar";
        _uninstallButton.Location = new Point(282, 398);
        _uninstallButton.Size = new Size(164, 52);
        _uninstallButton.BackColor = Color.White;
        _uninstallButton.ForeColor = Palette.Navy;
        _uninstallButton.FlatStyle = FlatStyle.Flat;
        _uninstallButton.FlatAppearance.BorderColor = Color.FromArgb(208, 216, 232);
        _uninstallButton.FlatAppearance.BorderSize = 1;
        _uninstallButton.CornerRadius = 12;
        _uninstallButton.Click += (_, _) => RunOperation(install: false);

        var closeButton = new RoundedButton
        {
            Text = "Cerrar",
            Location = new Point(714, 398),
            Size = new Size(146, 52),
            BackColor = Palette.Page,
            ForeColor = Palette.Muted,
            FlatStyle = FlatStyle.Flat,
            CornerRadius = 12,
        };
        closeButton.Click += (_, _) => Close();

        var information = new RoundedPanel
        {
            Location = new Point(40, 478),
            Size = new Size(820, 88),
            CornerRadius = 14,
            FillColor = Color.White,
        };
        var safetyTitle = new Label
        {
            Text = "Instalación segura y reversible",
            Font = new Font("Segoe UI Semibold", 10.5F),
            ForeColor = Palette.Navy,
            AutoSize = true,
            Location = new Point(20, 15),
        };
        var safetyText = new Label
        {
            Text = "✓ Crea una copia de seguridad automática     ✓ No modifica tus partidas guardadas\n" +
                   "Los textos de la traducción están integrados en el instalador.",
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = Palette.Muted,
            AutoSize = true,
            Location = new Point(20, 42),
        };
        information.Controls.AddRange([safetyTitle, safetyText]);

        var footer = new Label
        {
            Text = "MOD de traducción al español latinoamericano • Susumi",
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = Color.FromArgb(130, 139, 157),
            AutoSize = true,
            Location = new Point(40, 588),
        };

        Controls.AddRange([
            hero, pathLabel, version, _pathBox, browseButton, _statusPanel,
            _installButton, _uninstallButton, closeButton, information, footer,
        ]);
        AcceptButton = _installButton;
        Shown += (_, _) => RefreshStatus();
        ResumeLayout(false);
    }

    private static Image? LoadPortrait()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SusumiMod.Portrait");
        if (stream is null)
            return null;
        using var loaded = Image.FromStream(stream);
        return new Bitmap(loaded);
    }

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Selecciona la carpeta que contiene Chill With You.exe",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_pathBox.Text) ? _pathBox.Text : "",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _pathBox.Text = dialog.SelectedPath;
    }

    private void RefreshStatus()
    {
        var result = InstallerCore.GetStatus(_pathBox.Text);
        _status.Text = result.Message;
        (_statusPanel.FillColor, _status.ForeColor, _statusDot.ForeColor) = result.State switch
        {
            InstallState.Installed => (Palette.SoftGreen, Palette.Green, Palette.Green),
            InstallState.Original => (Palette.SoftBlue, Palette.Blue, Palette.Blue),
            InstallState.UpdateAvailable => (Palette.SoftGold, Palette.Gold, Palette.Gold),
            _ => (Palette.SoftRed, Palette.Red, Palette.Red),
        };
        _statusPanel.Invalidate();
        _installButton.Text = result.State switch
        {
            InstallState.Installed => "Reparar instalación",
            InstallState.UpdateAvailable => "Actualizar traducción",
            _ => "Instalar traducción",
        };
        _installButton.Enabled = result.State != InstallState.Unknown;
        _uninstallButton.Enabled = result.State is InstallState.Installed or InstallState.UpdateAvailable;
    }

    private void RunOperation(bool install)
    {
        Enabled = false;
        Cursor = Cursors.WaitCursor;
        try
        {
            var result = install
                ? InstallerCore.Install(_pathBox.Text)
                : InstallerCore.Uninstall(_pathBox.Text);
            MessageBox.Show(
                this,
                result.Message,
                result.Success ? "Operación completada" : "No se pudo completar",
                MessageBoxButtons.OK,
                result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
            Enabled = true;
            RefreshStatus();
        }
    }
}

internal static class Palette
{
    public static readonly Color Page = Color.FromArgb(245, 247, 252);
    public static readonly Color Navy = Color.FromArgb(28, 36, 67);
    public static readonly Color Blue = Color.FromArgb(47, 105, 214);
    public static readonly Color Rose = Color.FromArgb(221, 58, 115);
    public static readonly Color Muted = Color.FromArgb(91, 102, 126);
    public static readonly Color Green = Color.FromArgb(33, 124, 83);
    public static readonly Color Gold = Color.FromArgb(157, 103, 15);
    public static readonly Color Red = Color.FromArgb(164, 63, 67);
    public static readonly Color SoftBlue = Color.FromArgb(232, 239, 253);
    public static readonly Color SoftGreen = Color.FromArgb(230, 246, 238);
    public static readonly Color SoftGold = Color.FromArgb(255, 246, 220);
    public static readonly Color SoftRed = Color.FromArgb(253, 235, 236);
}

internal sealed class HeroPanel : Panel
{
    public HeroPanel()
    {
        DoubleBuffered = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var gradient = new LinearGradientBrush(ClientRectangle,
            Color.FromArgb(24, 40, 90), Color.FromArgb(40, 102, 194), 16F);
        e.Graphics.FillRectangle(gradient, ClientRectangle);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var glow = new SolidBrush(Color.FromArgb(22, 130, 218, 255));
        e.Graphics.FillEllipse(glow, Width - 275, -110, 340, 340);
        using var roseGlow = new SolidBrush(Color.FromArgb(20, 255, 93, 151));
        e.Graphics.FillEllipse(roseGlow, Width - 390, 95, 280, 150);
    }
}

internal sealed class RoundedPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 12;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColor { get; set; } = Color.White;

    public RoundedPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedPath(ClientRectangle, CornerRadius);
        using var brush = new SolidBrush(FillColor);
        e.Graphics.FillPath(brush, path);
        base.OnPaint(e);
    }

    internal static GraphicsPath RoundedPath(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(2, radius * 2);
        var arc = new Rectangle(rectangle.X, rectangle.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = rectangle.Right - diameter - 1;
        path.AddArc(arc, 270, 90);
        arc.Y = rectangle.Bottom - diameter - 1;
        path.AddArc(arc, 0, 90);
        arc.X = rectangle.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class RoundedButton : Button
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 10;

    public RoundedButton()
    {
        Cursor = Cursors.Hand;
        FlatAppearance.BorderSize = 0;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        using var path = RoundedPanel.RoundedPath(ClientRectangle, CornerRadius);
        var oldRegion = Region;
        Region = new Region(path);
        oldRegion?.Dispose();
    }
}

internal sealed class AvatarView : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Image? Image { get; set; }

    public AvatarView()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(5, 5, Width - 10, Height - 10);
        using var clip = new GraphicsPath();
        clip.AddEllipse(bounds);
        e.Graphics.SetClip(clip);
        if (Image is not null)
            e.Graphics.DrawImage(Image, bounds);
        else
            e.Graphics.Clear(Color.FromArgb(98, 158, 230));
        e.Graphics.ResetClip();
        using var border = new Pen(Color.FromArgb(225, 238, 255), 5F);
        e.Graphics.DrawEllipse(border, bounds);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Image?.Dispose();
        base.Dispose(disposing);
    }
}
#endif

internal enum InstallState
{
    Unknown,
    Original,
    Installed,
    UpdateAvailable,
}

internal sealed record OperationResult(bool Success, string Message, InstallState State = InstallState.Unknown);

#if false
internal static class InstallerCore
{
    private const string GameExe = "Chill With You.exe";
    private const string MarkerName = "ChillWithYou_ESLatam.mod.json";
    private const string BackupSuffix = ".eslatam.original";
    private const string CatalogRelative = @"Chill With You_Data\StreamingAssets\aa\catalog.json";
    private const string BundleRelative = @"Chill With You_Data\StreamingAssets\aa\StandaloneWindows64\scriptableobjects_assets_all_2f02d73bcdfde679d5a770af606de084.bundle";
    private const string AssemblyRelative = @"Chill With You_Data\Managed\Assembly-CSharp.dll";

    private static readonly FileSpec[] Files =
    [
        new(
            CatalogRelative,
            "SpanishMod.catalog.json",
            "aab4f94d97fda16eda59ca680d4faa08f9215d41aca86f9f82ed6779bd083f1e",
            "5e7b739f06da38fe9e997e72d9ba79e3712d9a2828f652e833d48464aa7b70c8"),
        new(
            BundleRelative,
            "SpanishMod.Localization.bundle",
            "5809e0b86af0d4ea0ae26fe9474bbfeaf0c8b01d222a3c7c9449572d9e92d5ef",
            "1a1c0c57af8e97b2876038b9cc41b0ba79360cc98debf76b64f47e869ae5ae5c"),
        new(
            AssemblyRelative,
            "SpanishMod.Assembly-CSharp.dll",
            "9cd6f635da3ee1f6916068114cdd25a8aedca7e68681dbd4090d4862fbda4bfc",
            "716cedcaaad2daed88c713627f30d341908459e5fad49ebe72259808087aa1df"),
    ];

    public static OperationResult VerifyEmbeddedPayloads()
    {
        try
        {
            foreach (var file in Files)
            {
                using var stream = OpenResource(file.ResourceName);
                var hash = Hash(stream);
                if (!hash.Equals(file.ModifiedHash, StringComparison.OrdinalIgnoreCase))
                    return new(false, $"El recurso {file.ResourceName} no pasó la verificación.");
            }
            return new(true, "Los recursos integrados son válidos.");
        }
        catch (Exception ex)
        {
            return new(false, $"No se pudieron verificar los recursos: {ex.Message}");
        }
    }

    public static string? FindGameDirectory()
    {
        var candidates = new List<string>
        {
            AppContext.BaseDirectory,
            Environment.CurrentDirectory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Steam", "steamapps", "common", "Chill with You Lo-Fi Story"),
        };

        try
        {
            var steamPath = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(steamPath))
            {
                candidates.Add(Path.Combine(steamPath, "steamapps", "common", "Chill with You Lo-Fi Story"));
                var libraries = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(libraries))
                {
                    foreach (Match match in Regex.Matches(File.ReadAllText(libraries), "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\""))
                    {
                        var library = match.Groups[1].Value.Replace("\\\\", "\\");
                        candidates.Add(Path.Combine(library, "steamapps", "common", "Chill with You Lo-Fi Story"));
                    }
                }
            }
        }
        catch
        {
            // Manual selection remains available if Steam discovery is unavailable.
        }

        return candidates
            .Select(path => Path.GetFullPath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(path => File.Exists(Path.Combine(path, GameExe)));
    }

    public static OperationResult GetStatus(string root)
    {
        try
        {
            var validation = ValidateRoot(root);
            if (!validation.Success)
                return validation;
            var hashes = Files.Select(file => HashFile(Target(root, file))).ToArray();
            if (Files.Select((file, index) => hashes[index] == file.ModifiedHash).All(value => value))
                return new(true, "Traducción instalada y lista para usar.", InstallState.Installed);
            if (Files.Select((file, index) => hashes[index] == file.OriginalHash).All(value => value))
                return new(true, "Juego compatible. Todo listo para instalar.", InstallState.Original);
            if (Files.Select((file, index) => IsKnownCurrent(file, hashes[index])).All(value => value))
                return new(true, "Versión anterior detectada. Puedes actualizarla con un clic.", InstallState.UpdateAvailable);
            return OutdatedModResult();
        }
        catch (Exception ex)
        {
            return new(false, $"No se pudo comprobar la instalación: {ex.Message}");
        }
    }

    public static OperationResult Install(string root)
    {
        try
        {
            root = Path.GetFullPath(root);
            var validation = ValidateRoot(root);
            if (!validation.Success)
                return validation;
            if (IsGameRunning())
                return new(false, "Cierra Chill With You antes de instalar la traducción.");

            var payloadCheck = VerifyEmbeddedPayloads();
            if (!payloadCheck.Success)
                return payloadCheck;

            foreach (var file in Files)
            {
                var target = Target(root, file);
                var current = HashFile(target);
                if (!IsKnownCurrent(file, current))
                    return OutdatedModResult();

                var backup = target + BackupSuffix;
                if (File.Exists(backup) && HashFile(backup) != file.OriginalHash && current != file.OriginalHash)
                    return OutdatedModResult();
                if (!File.Exists(backup) && current != file.OriginalHash)
                    return new(false, $"Falta la copia original de {Path.GetFileName(target)}. Usa Steam para verificar los archivos.");
            }

            foreach (var file in Files)
                EnsureBackup(Target(root, file), file.OriginalHash);

            var temporaryFiles = new List<string>();
            try
            {
                foreach (var file in Files)
                {
                    var target = Target(root, file);
                    var temporary = target + $".eslatam.tmp.{Guid.NewGuid():N}";
                    temporaryFiles.Add(temporary);
                    using (var source = OpenResource(file.ResourceName))
                    using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        source.CopyTo(destination);
                        destination.Flush(flushToDisk: true);
                    }
                    if (HashFile(temporary) != file.ModifiedHash)
                        throw new InvalidDataException($"Falló la verificación temporal de {file.RelativePath}.");
                }

                for (var index = 0; index < Files.Length; index++)
                    File.Replace(temporaryFiles[index], Target(root, Files[index]), null, ignoreMetadataErrors: true);
            }
            catch
            {
                RestoreFromBackups(root, onlyWhenModified: true);
                throw;
            }
            finally
            {
                foreach (var temporary in temporaryFiles.Where(File.Exists))
                    File.Delete(temporary);
            }

            foreach (var file in Files)
            {
                if (HashFile(Target(root, file)) != file.ModifiedHash)
                    throw new InvalidDataException($"La comprobación final falló para {file.RelativePath}.");
            }

            var marker = new
            {
                mod = "Chill With You — Español (Latinoamérica)",
                version = "1.2.0",
                author = "Susumi",
                installedUtc = DateTime.UtcNow,
                files = Files.Select(file => new { file.RelativePath, file.OriginalHash, file.ModifiedHash }),
            };
            File.WriteAllText(Path.Combine(root, MarkerName), JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true }));
            return new(true, "La traducción se instaló correctamente.\n\nSe crearon copias de seguridad para poder desinstalarla.", InstallState.Installed);
        }
        catch (UnauthorizedAccessException)
        {
            return new(false, "Windows negó el acceso a la carpeta. Ejecuta el instalador como administrador.");
        }
        catch (Exception ex)
        {
            return new(false, $"La instalación no pudo completarse. Se intentó restaurar el juego.\n\n{ex.Message}");
        }
    }

    public static OperationResult Uninstall(string root)
    {
        try
        {
            root = Path.GetFullPath(root);
            var validation = ValidateRoot(root);
            if (!validation.Success)
                return validation;
            if (IsGameRunning())
                return new(false, "Cierra Chill With You antes de desinstalar la traducción.");

            foreach (var file in Files)
            {
                var target = Target(root, file);
                var current = HashFile(target);
                var backup = target + BackupSuffix;
                if (!IsKnownCurrent(file, current))
                    return new(false, $"{file.RelativePath} fue modificado por otra actualización o mod. No se sobrescribirá.");
                if (!File.Exists(backup) || HashFile(backup) != file.OriginalHash)
                    return new(false, $"No existe una copia original válida de {Path.GetFileName(target)}.");
            }

            RestoreFromBackups(root, onlyWhenModified: false);
            foreach (var file in Files)
            {
                var target = Target(root, file);
                if (HashFile(target) != file.OriginalHash)
                    throw new InvalidDataException($"No se pudo restaurar {file.RelativePath}.");
            }

            foreach (var file in Files)
                File.Delete(Target(root, file) + BackupSuffix);
            var marker = Path.Combine(root, MarkerName);
            if (File.Exists(marker))
                File.Delete(marker);
            return new(true, "La traducción se desinstaló y los archivos originales fueron restaurados.", InstallState.Original);
        }
        catch (UnauthorizedAccessException)
        {
            return new(false, "Windows negó el acceso a la carpeta. Ejecuta el instalador como administrador.");
        }
        catch (Exception ex)
        {
            return new(false, $"No se pudo desinstalar la traducción.\n\n{ex.Message}");
        }
    }

    private static OperationResult ValidateRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return new(false, "Selecciona una carpeta válida del juego.");
        root = Path.GetFullPath(root);
        if (!File.Exists(Path.Combine(root, GameExe)))
            return new(false, $"No se encontró «{GameExe}» en esa carpeta.");
        foreach (var file in Files)
        {
            if (!File.Exists(Target(root, file)))
                return OutdatedModResult();
        }
        return new(true, "Carpeta válida.");
    }

    private static void EnsureBackup(string target, string expectedHash)
    {
        var backup = target + BackupSuffix;
        if (File.Exists(backup) && HashFile(backup) == expectedHash)
            return;
        if (HashFile(target) != expectedHash)
            throw new InvalidDataException($"No se puede crear la copia original de {Path.GetFileName(target)}.");
        var temporary = backup + $".tmp.{Guid.NewGuid():N}";
        try
        {
            File.Copy(target, temporary, overwrite: false);
            if (HashFile(temporary) != expectedHash)
                throw new InvalidDataException($"La copia de seguridad de {Path.GetFileName(target)} no coincide.");
            if (File.Exists(backup))
                File.Replace(temporary, backup, null, ignoreMetadataErrors: true);
            else
                File.Move(temporary, backup, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static OperationResult OutdatedModResult()
    {
        return new(false,
            "Esta versión del mod pertenece a una versión anterior del juego. " +
            "Descarga una versión actualizada del instalador.\n\nNo se modificó ningún archivo.");
    }

    private static void RestoreFromBackups(string root, bool onlyWhenModified)
    {
        foreach (var file in Files)
        {
            var target = Target(root, file);
            var backup = target + BackupSuffix;
            if (!File.Exists(backup) || HashFile(backup) != file.OriginalHash)
                continue;
            if (onlyWhenModified && !IsKnownModified(file, HashFile(target)))
                continue;
            var temporary = target + $".restore.tmp.{Guid.NewGuid():N}";
            try
            {
                File.Copy(backup, temporary, overwrite: false);
                File.Replace(temporary, target, null, ignoreMetadataErrors: true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }

    private static bool IsGameRunning()
    {
        return Process.GetProcessesByName("Chill With You").Length > 0 ||
               Process.GetProcessesByName("ChillWithYou").Length > 0;
    }

    private static string Target(string root, FileSpec file)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(fullRoot, file.RelativePath));
        if (!target.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La ruta de destino salió de la carpeta del juego.");
        return target;
    }

    private static Stream OpenResource(string name)
    {
        return Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidDataException($"No se encontró el recurso integrado {name}.");
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Hash(stream);
    }

    private static string Hash(Stream stream)
    {
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool IsKnownCurrent(FileSpec file, string hash)
    {
        return hash == file.OriginalHash || IsKnownModified(file, hash);
    }

    private static bool IsKnownModified(FileSpec file, string hash)
    {
        return hash == file.ModifiedHash || hash == file.PreviousModifiedHash;
    }

    private sealed record FileSpec(
        string RelativePath,
        string ResourceName,
        string OriginalHash,
        string ModifiedHash,
        string? PreviousModifiedHash = null);
}
#endif

internal static class InstallerCore
{
    public const string ModVersion = "1.0";
    private const string GameExe = "Chill With You.exe";
    private const string MarkerName = "ChillWithYou_ESLatam.mod.json";
    private const string BackupSuffix = ".eslatam.original";
    private const string AssemblyRelative = @"Chill With You_Data\Managed\Assembly-CSharp.dll";
    private const string TranslationResource = "SusumiMod.Translations";

    public static OperationResult VerifyEmbeddedPayloads()
    {
        try
        {
            var translations = LoadTranslations();
            if (translations.Count != 2111 || translations.Any(pair =>
                    string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null))
                return new(false, "La traducción integrada no pasó la verificación.");
            return new(true, $"Traducción integrada verificada: {translations.Count} textos.");
        }
        catch (Exception ex)
        {
            return new(false, $"No se pudo verificar la traducción integrada: {ex.Message}");
        }
    }

    public static string? FindGameDirectory()
    {
        var candidates = new List<string>
        {
            AppContext.BaseDirectory,
            Environment.CurrentDirectory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Steam", "steamapps", "common", "Chill with You Lo-Fi Story"),
        };

        try
        {
            var steamPath = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(steamPath))
            {
                candidates.Add(Path.Combine(steamPath, "steamapps", "common", "Chill with You Lo-Fi Story"));
                var libraries = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(libraries))
                {
                    foreach (Match match in Regex.Matches(File.ReadAllText(libraries), "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\""))
                    {
                        var library = match.Groups[1].Value.Replace("\\\\", "\\");
                        candidates.Add(Path.Combine(library, "steamapps", "common", "Chill with You Lo-Fi Story"));
                    }
                }
            }
        }
        catch
        {
            // La selección manual sigue disponible.
        }

        return candidates.Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(path => File.Exists(Path.Combine(path, GameExe)));
    }

    public static OperationResult GetStatus(string root)
    {
        try
        {
            var validation = ValidateRoot(root);
            if (!validation.Success)
                return validation;

            var inspection = SpanishPatchEngine.Inspect(Target(root, AssemblyRelative), ModVersion);
            return inspection.State switch
            {
                AssemblyPatchState.PatchedCurrent =>
                    new(true, "Traducción instalada y lista para usar.", InstallState.Installed),
                AssemblyPatchState.PatchedOlder =>
                    new(true, "Hay una actualización de la traducción lista para instalar.", InstallState.UpdateAvailable),
                AssemblyPatchState.LegacyPatched =>
                    new(true, "Se detectó la versión anterior del mod. Se puede migrar con un clic.", InstallState.UpdateAvailable),
                AssemblyPatchState.Compatible when File.Exists(Path.Combine(Path.GetFullPath(root), MarkerName)) =>
                    new(true, "Steam actualizó el juego. La traducción puede reaplicarse sin descargar otro instalador.", InstallState.UpdateAvailable),
                AssemblyPatchState.Compatible =>
                    new(true, "Juego compatible. Todo listo para instalar.", InstallState.Original),
                _ => new(false,
                    "La actualización del juego cambió componentes necesarios para el mod. " +
                    "Comprueba si existe una actualización del instalador."),
            };
        }
        catch (Exception ex)
        {
            return new(false, $"No se pudo comprobar la instalación: {ex.Message}");
        }
    }

    public static OperationResult Install(string root)
    {
        try
        {
            root = Path.GetFullPath(root);
            var validation = ValidateRoot(root);
            if (!validation.Success)
                return validation;
            if (IsGameRunning())
                return new(false, "Cierra Chill With You antes de instalar la traducción.");

            var payloadCheck = VerifyEmbeddedPayloads();
            if (!payloadCheck.Success)
                return payloadCheck;
            var translations = LoadTranslations();

            MigrateLegacyInstallation(root);
            var target = Target(root, AssemblyRelative);
            var backup = target + BackupSuffix;
            var inspection = SpanishPatchEngine.Inspect(target, ModVersion);
            if (inspection.State == AssemblyPatchState.PatchedCurrent)
                return new(true, "La traducción ya está instalada y lista para usar.", InstallState.Installed);

            string source;
            if (inspection.State is AssemblyPatchState.PatchedOlder or AssemblyPatchState.LegacyPatched)
            {
                if (!File.Exists(backup) ||
                    SpanishPatchEngine.Inspect(backup, ModVersion).State != AssemblyPatchState.Compatible)
                    return new(false,
                        "No se encontró una copia compatible para actualizar esta instalación. " +
                        "Verifica los archivos del juego en Steam y vuelve a intentarlo.");
                source = backup;
            }
            else if (inspection.State == AssemblyPatchState.Compatible)
            {
                source = target;
            }
            else
            {
                return new(false,
                    "Esta actualización del juego cambió componentes necesarios para el mod. " +
                    "No se modificó ningún archivo.");
            }

            var temporary = target + $".eslatam.tmp.{Guid.NewGuid():N}";
            try
            {
                string modifiedHash = SpanishPatchEngine.Patch(source, temporary, translations, ModVersion);
                if (ReferenceEquals(source, target) || source.Equals(target, StringComparison.OrdinalIgnoreCase))
                    RefreshBackup(target, backup);
                File.Replace(temporary, target, null, ignoreMetadataErrors: true);

                var finalInspection = SpanishPatchEngine.Inspect(target, ModVersion);
                if (finalInspection.State != AssemblyPatchState.PatchedCurrent || HashFile(target) != modifiedHash)
                    throw new InvalidDataException("La comprobación final de la capa de traducción falló.");

                var marker = new
                {
                    mod = "Chill With You — Español (Latinoamérica)",
                    version = ModVersion,
                    author = "Susumi",
                    mode = "dynamic-overlay",
                    installedUtc = DateTime.UtcNow,
                    translations = translations.Count,
                    files = new[]
                    {
                        new
                        {
                            RelativePath = AssemblyRelative,
                            OriginalHash = HashFile(backup),
                            ModifiedHash = modifiedHash,
                        },
                    },
                };
                File.WriteAllText(Path.Combine(root, MarkerName),
                    JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true }));
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }

            return new(true,
                "La traducción se instaló correctamente.\n\n" +
                "Las futuras líneas desconocidas se mostrarán en portugués hasta que el mod se actualice.",
                InstallState.Installed);
        }
        catch (UnauthorizedAccessException)
        {
            return new(false, "Windows negó el acceso a la carpeta. Ejecuta el instalador como administrador.");
        }
        catch (Exception ex)
        {
            return new(false, $"La instalación no pudo completarse.\n\n{ex.Message}");
        }
    }

    public static OperationResult Uninstall(string root)
    {
        try
        {
            root = Path.GetFullPath(root);
            var validation = ValidateRoot(root);
            if (!validation.Success)
                return validation;
            if (IsGameRunning())
                return new(false, "Cierra Chill With You antes de desinstalar la traducción.");

            if (IsLegacyMarker(root))
            {
                MigrateLegacyInstallation(root);
                return new(true, "La versión anterior del mod fue retirada y los archivos recuperables se restauraron.", InstallState.Original);
            }

            var target = Target(root, AssemblyRelative);
            var backup = target + BackupSuffix;
            var inspection = SpanishPatchEngine.Inspect(target, ModVersion);
            if (inspection.State == AssemblyPatchState.Compatible)
            {
                DeleteMarker(root);
                return new(true, "La traducción ya no está instalada.", InstallState.Original);
            }
            if (inspection.State is not (AssemblyPatchState.PatchedCurrent or AssemblyPatchState.PatchedOlder))
                return new(false, "La DLL fue modificada por otra actualización o mod. No se sobrescribirá.");
            if (!File.Exists(backup) ||
                SpanishPatchEngine.Inspect(backup, ModVersion).State != AssemblyPatchState.Compatible)
                return new(false, "No existe una copia original compatible para desinstalar el mod.");

            RestoreFile(backup, target);
            if (SpanishPatchEngine.Inspect(target, ModVersion).State != AssemblyPatchState.Compatible)
                throw new InvalidDataException("No se pudo restaurar la DLL original.");
            File.Delete(backup);
            DeleteMarker(root);
            return new(true, "La traducción se desinstaló y la DLL anterior fue restaurada.", InstallState.Original);
        }
        catch (UnauthorizedAccessException)
        {
            return new(false, "Windows negó el acceso a la carpeta. Ejecuta el instalador como administrador.");
        }
        catch (Exception ex)
        {
            return new(false, $"No se pudo desinstalar la traducción.\n\n{ex.Message}");
        }
    }

    private static OperationResult ValidateRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return new(false, "Selecciona una carpeta válida del juego.");
        root = Path.GetFullPath(root);
        if (!File.Exists(Path.Combine(root, GameExe)))
            return new(false, $"No se encontró «{GameExe}» en esa carpeta.");
        if (!File.Exists(Target(root, AssemblyRelative)))
            return new(false, "La instalación del juego está incompleta. Verifica sus archivos en Steam.");
        return new(true, "Carpeta válida.");
    }

    private static Dictionary<string, string> LoadTranslations()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(TranslationResource)
            ?? throw new InvalidDataException("No se encontró la traducción integrada.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidDataException("No se pudo leer la traducción integrada.");
    }

    private static void RefreshBackup(string target, string backup)
    {
        string expectedHash = HashFile(target);
        if (File.Exists(backup) && HashFile(backup) == expectedHash)
            return;
        var temporary = backup + $".tmp.{Guid.NewGuid():N}";
        try
        {
            File.Copy(target, temporary, overwrite: false);
            if (HashFile(temporary) != expectedHash)
                throw new InvalidDataException("La copia de seguridad no coincide con la DLL original.");
            if (File.Exists(backup))
                File.Replace(temporary, backup, null, ignoreMetadataErrors: true);
            else
                File.Move(temporary, backup, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void MigrateLegacyInstallation(string root)
    {
        var markerPath = Path.Combine(root, MarkerName);
        if (!File.Exists(markerPath))
            return;

        using var document = JsonDocument.Parse(File.ReadAllText(markerPath));
        if (!document.RootElement.TryGetProperty("version", out var versionElement) ||
            !Version.TryParse(versionElement.GetString(), out var version) || version.Major >= 2)
            return;
        if (!document.RootElement.TryGetProperty("files", out var files))
            throw new InvalidDataException("El registro de la instalación anterior está incompleto.");

        foreach (var entry in files.EnumerateArray())
        {
            string relative = entry.GetProperty("RelativePath").GetString()!;
            string originalHash = entry.GetProperty("OriginalHash").GetString()!;
            string modifiedHash = entry.GetProperty("ModifiedHash").GetString()!;
            string target = Target(root, relative);
            string backup = target + BackupSuffix;
            if (!File.Exists(target))
                continue;
            string currentHash = HashFile(target);
            if (currentHash == modifiedHash)
            {
                if (!File.Exists(backup) || HashFile(backup) != originalHash)
                    throw new InvalidDataException($"No se puede restaurar {Path.GetFileName(target)} de la versión anterior.");
                RestoreFile(backup, target);
                currentHash = HashFile(target);
            }
            if (currentHash == originalHash && File.Exists(backup) && HashFile(backup) == originalHash)
                File.Delete(backup);
        }
        File.Delete(markerPath);
    }

    private static bool IsLegacyMarker(string root)
    {
        var marker = Path.Combine(root, MarkerName);
        if (!File.Exists(marker))
            return false;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(marker));
            return document.RootElement.TryGetProperty("version", out var element) &&
                Version.TryParse(element.GetString(), out var version) && version.Major < 2;
        }
        catch
        {
            return false;
        }
    }

    private static void RestoreFile(string backup, string target)
    {
        var temporary = target + $".restore.tmp.{Guid.NewGuid():N}";
        try
        {
            File.Copy(backup, temporary, overwrite: false);
            File.Replace(temporary, target, null, ignoreMetadataErrors: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void DeleteMarker(string root)
    {
        var marker = Path.Combine(root, MarkerName);
        if (File.Exists(marker))
            File.Delete(marker);
    }

    private static bool IsGameRunning() =>
        Process.GetProcessesByName("Chill With You").Length > 0 ||
        Process.GetProcessesByName("ChillWithYou").Length > 0;

    private static string Target(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (!target.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La ruta de destino salió de la carpeta del juego.");
        return target;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
