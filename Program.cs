using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DaleMasBajoReelMaker
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    public class MainForm : Form
    {
        private readonly Panel _dropZone;
        private readonly Label _lblDropTitle;
        private readonly Label _lblFileInfo;
        private readonly TextBox _txtTitle;
        private readonly TextBox _txtArtist;
        private readonly TextBox _txtBpm;
        private readonly NumericUpDown _numStart;
        private readonly NumericUpDown _numEnd;
        private readonly TextBox _txtOutDir;
        private readonly Button _btnBrowseOut;
        private readonly Button _btnRender;
        private readonly Button _btnOpenOut;
        private readonly ProgressBar _progress;
        private readonly Label _lblStatus;

        private string _selectedVideoPath;
        private double _videoDurationSec = 0;
        private string _lastOutputPath;
        private CancellationTokenSource _cts;

        private static readonly Color BgMain = ColorTranslator.FromHtml("#F8F9FA");
        private static readonly Color CardWhite = Color.White;
        private static readonly Color AccentBlue = ColorTranslator.FromHtml("#0066FF");
        private static readonly Color TextPrimary = ColorTranslator.FromHtml("#1C1E21");
        private static readonly Color TextSecondary = ColorTranslator.FromHtml("#65676B");
        private static readonly Color BorderGray = ColorTranslator.FromHtml("#E4E6EB");
        private static readonly Color SuccessGreen = ColorTranslator.FromHtml("#16A34A");

        private static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DaleMasBajoReelMaker");

        private static readonly string FfmpegExePath = Path.Combine(AppDataDir, "ffmpeg.exe");

        public MainForm()
        {
            Text = "DaleMásBajo — Reel Maker Portable (9:16)";
            Size = new Size(520, 650);
            MinimumSize = new Size(520, 650);
            MaximumSize = new Size(520, 650);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgMain;
            Font = new Font("Segoe UI", 9.5f);
            AllowDrop = true;

            var header = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = CardWhite };
            header.Paint += (s, e) => e.Graphics.DrawLine(new Pen(BorderGray), 0, header.Height - 1, header.Width, header.Height - 1);
            var lblHeader = new Label
            {
                Text = "DaleMásBajo  •  Reel Maker 9:16",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = TextPrimary,
                AutoSize = true,
                Location = new Point(20, 16)
            };
            header.Controls.Add(lblHeader);
            Controls.Add(header);

            _dropZone = new Panel
            {
                Location = new Point(20, 74),
                Size = new Size(464, 118),
                BackColor = CardWhite,
                AllowDrop = true,
                Cursor = Cursors.Hand
            };
            _dropZone.Paint += (s, e) =>
            {
                using (var pen = new Pen(AccentBlue, 2) { DashStyle = DashStyle.Dash })
                {
                    e.Graphics.DrawRectangle(pen, 1, 1, _dropZone.Width - 3, _dropZone.Height - 3);
                }
            };
            _lblDropTitle = new Label
            {
                Text = "Arrastra tu video MP4 aquí\no haz clic para seleccionar",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = AccentBlue,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            _dropZone.Controls.Add(_lblDropTitle);
            _dropZone.Click += (s, e) => BrowseVideo();
            _lblDropTitle.Click += (s, e) => BrowseVideo();

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            _dropZone.DragEnter += OnDragEnter;
            _dropZone.DragDrop += OnDragDrop;
            Controls.Add(_dropZone);

            _lblFileInfo = new Label
            {
                Text = "Ningún video cargado",
                ForeColor = TextSecondary,
                Location = new Point(20, 200),
                Size = new Size(464, 22),
                AutoEllipsis = true
            };
            Controls.Add(_lblFileInfo);

            var metaCard = new Panel
            {
                Location = new Point(20, 228),
                Size = new Size(464, 240),
                BackColor = CardWhite
            };
            metaCard.Paint += (s, e) => e.Graphics.DrawRectangle(new Pen(BorderGray), 0, 0, metaCard.Width - 1, metaCard.Height - 1);

            AddLabel(metaCard, "Título de la canción", 16, 14);
            _txtTitle = AddTextBox(metaCard, 16, 36, 430);

            AddLabel(metaCard, "Artista(s)", 16, 74);
            _txtArtist = AddTextBox(metaCard, 16, 96, 280);

            AddLabel(metaCard, "BPM", 312, 74);
            _txtBpm = AddTextBox(metaCard, 312, 96, 134);

            AddLabel(metaCard, "Desde (segundo)", 16, 136);
            _numStart = new NumericUpDown
            {
                Location = new Point(16, 158),
                Size = new Size(130, 28),
                Minimum = 0,
                Maximum = 36000,
                DecimalPlaces = 1,
                Increment = 1,
                Value = 10
            };
            metaCard.Controls.Add(_numStart);

            AddLabel(metaCard, "Hasta (segundo)", 164, 136);
            _numEnd = new NumericUpDown
            {
                Location = new Point(164, 158),
                Size = new Size(130, 28),
                Minimum = 1,
                Maximum = 36000,
                DecimalPlaces = 1,
                Increment = 1,
                Value = 45
            };
            metaCard.Controls.Add(_numEnd);

            AddLabel(metaCard, "Carpeta de salida", 16, 180);
            _txtOutDir = AddTextBox(metaCard, 16, 200, 340);
            _txtOutDir.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Reels");

            _btnBrowseOut = new Button
            {
                Text = "Cambiar...",
                Location = new Point(364, 199),
                Size = new Size(82, 27),
                FlatStyle = FlatStyle.Flat,
                BackColor = BgMain,
                ForeColor = TextPrimary
            };
            _btnBrowseOut.FlatAppearance.BorderColor = BorderGray;
            _btnBrowseOut.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    if (dlg.ShowDialog() == DialogResult.OK) _txtOutDir.Text = dlg.SelectedPath;
                }
            };
            metaCard.Controls.Add(_btnBrowseOut);
            Controls.Add(metaCard);

            _btnRender = new Button
            {
                Text = "Generar Reel 9:16",
                Location = new Point(20, 484),
                Size = new Size(340, 44),
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentBlue,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnRender.FlatAppearance.BorderSize = 0;
            _btnRender.Click += async (s, e) => await StartRenderAsync();
            Controls.Add(_btnRender);

            _btnOpenOut = new Button
            {
                Text = "Abrir carpeta",
                Location = new Point(370, 484),
                Size = new Size(114, 44),
                FlatStyle = FlatStyle.Flat,
                BackColor = CardWhite,
                ForeColor = TextPrimary,
                Enabled = false
            };
            _btnOpenOut.FlatAppearance.BorderColor = BorderGray;
            _btnOpenOut.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(_lastOutputPath) && File.Exists(_lastOutputPath))
                    Process.Start("explorer.exe", $"/select,\"{_lastOutputPath}\"");
                else if (Directory.Exists(_txtOutDir.Text))
                    Process.Start("explorer.exe", _txtOutDir.Text);
            };
            Controls.Add(_btnOpenOut);

            _progress = new ProgressBar
            {
                Location = new Point(20, 542),
                Size = new Size(464, 18),
                Style = ProgressBarStyle.Continuous
            };
            Controls.Add(_progress);

            _lblStatus = new Label
            {
                Text = File.Exists(FfmpegExePath) ? "Listo para recibir un video." : "Listo. En el primer uso descargará FFmpeg automáticamente.",
                ForeColor = TextSecondary,
                Location = new Point(20, 568),
                Size = new Size(464, 24)
            };
            Controls.Add(_lblStatus);
        }

        private static void AddLabel(Control parent, string text, int x, int y)
        {
            parent.Controls.Add(new Label
            {
                Text = text,
                ForeColor = TextSecondary,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(x, y),
                AutoSize = true
            });
        }

        private static TextBox AddTextBox(Control parent, int x, int y, int w)
        {
            var tb = new TextBox
            {
                Location = new Point(x, y),
                Size = new Size(w, 26),
                BorderStyle = BorderStyle.FixedSingle
            };
            parent.Controls.Add(tb);
            return tb;
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                LoadVideo(files[0]);
        }

        private void BrowseVideo()
        {
            using (var ofd = new OpenFileDialog { Filter = "Videos|*.mp4;*.mov;*.mkv;*.avi;*.webm|Todos|*.*" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                    LoadVideo(ofd.FileName);
            }
        }

        private void LoadVideo(string path)
        {
            _selectedVideoPath = path;
            string name = Path.GetFileNameWithoutExtension(path);
            _lblDropTitle.Text = Path.GetFileName(path);

            var parts = name.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
            _txtTitle.Text = parts.Length >= 1 ? parts[0].Trim() : name;
            _txtArtist.Text = parts.Length >= 2 ? parts[1].Trim() : "";

            var bpmMatch = Regex.Match(name, @"(\d{2,3})\s*bpm", RegexOptions.IgnoreCase);
            _txtBpm.Text = bpmMatch.Success ? $"{bpmMatch.Groups[1].Value} BPM" : "120 BPM";

            _videoDurationSec = ProbeDurationWithFfmpeg(path);
            if (_videoDurationSec > 0)
            {
                _lblFileInfo.Text = $"Duración detectada: {_videoDurationSec:F1}s";
                _numStart.Maximum = (decimal)Math.Max(0, _videoDurationSec - 1);
                _numEnd.Maximum = (decimal)_videoDurationSec;
                if (_videoDurationSec >= 45)
                {
                    _numStart.Value = 10;
                    _numEnd.Value = 45;
                }
                else
                {
                    _numStart.Value = 0;
                    _numEnd.Value = (decimal)Math.Floor(_videoDurationSec);
                }
            }
            else
            {
                _lblFileInfo.Text = "Video cargado (ajusta Desde / Hasta)";
            }

            _lblStatus.ForeColor = TextSecondary;
            _lblStatus.Text = "Video listo. Ajusta los segundos si lo necesitas y presiona Generar.";
        }

        private static string GetFfmpegPath()
        {
            string besideExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            if (File.Exists(besideExe)) return besideExe;
            if (File.Exists(FfmpegExePath)) return FfmpegExePath;
            return null;
        }

        private async Task EnsureFfmpegAsync()
        {
            if (GetFfmpegPath() != null) return;

            Directory.CreateDirectory(AppDataDir);
            string zipPath = Path.Combine(AppDataDir, "ffmpeg.zip");
            const string url = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

            Invoke((Action)(() =>
            {
                _lblStatus.ForeColor = AccentBlue;
                _lblStatus.Text = "Descargando motor de video (solo esta primera vez)...";
                _progress.Style = ProgressBarStyle.Marquee;
            }));

            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
            using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await resp.Content.CopyToAsync(fs);
                }
            }

            Invoke((Action)(() => _lblStatus.Text = "Extrayendo ffmpeg.exe..."));

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.ExtractToFile(FfmpegExePath, true);
                        break;
                    }
                }
            }

            if (File.Exists(zipPath)) File.Delete(zipPath);

            Invoke((Action)(() =>
            {
                _progress.Style = ProgressBarStyle.Continuous;
                _progress.Value = 0;
            }));
        }

        private static double ProbeDurationWithFfmpeg(string path)
        {
            string ff = GetFfmpegPath();
            if (ff == null) return 0;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ff,
                    Arguments = $"-i \"{path}\"",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    string err = p?.StandardError.ReadToEnd() ?? "";
                    p?.WaitForExit();
                    var m = Regex.Match(err, @"Duration:\s*(\d{2}):(\d{2}):(\d{2}\.\d+)");
                    if (m.Success)
                    {
                        int h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                        int min = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                        double sec = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                        return h * 3600 + min * 60 + sec;
                    }
                }
            }
            catch { }
            return 0;
        }

        private async Task StartRenderAsync()
        {
            if (string.IsNullOrEmpty(_selectedVideoPath) || !File.Exists(_selectedVideoPath))
            {
                MessageBox.Show("Primero arrastra o selecciona un video.", "Falta video", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            double startSec = (double)_numStart.Value;
            double endSec = (double)_numEnd.Value;
            if (endSec <= startSec)
            {
                MessageBox.Show("El segundo final ('Hasta') debe ser mayor que el inicial ('Desde').", "Rango inválido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _btnRender.Enabled = false;
            _btnOpenOut.Enabled = false;

            try
            {
                await EnsureFfmpegAsync();

                Directory.CreateDirectory(_txtOutDir.Text);
                string safeTitle = Regex.Replace(_txtTitle.Text.Trim(), @"[^\w\s-]", "").Replace(" ", "_");
                if (string.IsNullOrWhiteSpace(safeTitle)) safeTitle = "Reel";
                string outPath = Path.Combine(_txtOutDir.Text, $"{safeTitle}_Reel_9x16.mp4");

                _progress.Value = 0;
                _lblStatus.ForeColor = AccentBlue;
                _lblStatus.Text = "Renderizando Reel 9:16...";

                _cts = new CancellationTokenSource();
                string title = _txtTitle.Text.Trim();
                string artist = _txtArtist.Text.Trim();
                string bpm = _txtBpm.Text.Trim();
                string input = _selectedVideoPath;
                string ff = GetFfmpegPath();

                await Task.Run(() => RenderReel(ff, input, outPath, title, artist, bpm, startSec, endSec, pct =>
                {
                    Invoke((Action)(() =>
                    {
                        _progress.Value = ClampInt(pct, 0, 100);
                        _lblStatus.Text = $"Renderizando... {pct}%";
                    }));
                }, _cts.Token));

                _lastOutputPath = outPath;
                _progress.Value = 100;
                _lblStatus.ForeColor = SuccessGreen;
                _lblStatus.Text = $"¡Listo! Guardado en {Path.GetFileName(outPath)}";
                _btnOpenOut.Enabled = true;
            }
            catch (Exception ex)
            {
                _progress.Style = ProgressBarStyle.Continuous;
                _lblStatus.ForeColor = Color.Red;
                _lblStatus.Text = "Error: " + ex.Message;
            }
            finally
            {
                _btnRender.Enabled = true;
            }
        }

        private static int ClampInt(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        private static double ClampDouble(double v, double min, double max) => v < min ? min : (v > max ? max : v);

        private static double EaseOutCubic(double t)
        {
            t = ClampDouble(t, 0.0, 1.0);
            return 1.0 - Math.Pow(1.0 - t, 3.0);
        }

        private static double SpringPop(double t)
        {
            t = ClampDouble(t, 0.0, 1.0);
            if (t <= 0.0) return 0.0;
            if (t < 0.7) return 1.10 * EaseOutCubic(t / 0.7);
            return 1.10 - 0.10 * EaseOutCubic((t - 0.7) / 0.3);
        }

        private static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2f;
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static string FormatTime(double sec)
        {
            int s = Math.Max(0, (int)Math.Round(sec));
            return $"{s / 60}:{s % 60:D2}";
        }

        private static void RenderReel(
            string ffmpeg, string inputPath, string outputPath,
            string title, string artist, string bpm,
            double startSec, double endSec,
            Action<int> onProgress, CancellationToken ct)
        {
            const int W = 1080, H = 1920, FPS = 30;
            const int CARD_W = 720, CARD_H = 1010, CARD_X = (W - CARD_W) / 2, CARD_Y_FINAL = 440, CARD_R = 40;
            const int VID_SIZE = 616, VID_PAD = 52, VID_R = 26;

            double duration = endSec - startSec;
            int totalFrames = (int)Math.Round(duration * FPS);
            string tmpAudio = Path.Combine(Path.GetTempPath(), $"dmb_audio_{Guid.NewGuid():N}.aac");

            try
            {
                var audPsi = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = $"-y -ss {startSec.ToString(CultureInfo.InvariantCulture)} -t {duration.ToString(CultureInfo.InvariantCulture)} -i \"{inputPath}\" -vn -c:a aac -b:a 192k \"{tmpAudio}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var ap = Process.Start(audPsi)) ap?.WaitForExit();

                var decPsi = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = $"-ss {startSec.ToString(CultureInfo.InvariantCulture)} -t {duration.ToString(CultureInfo.InvariantCulture)} -i \"{inputPath}\" -vf \"crop='min(iw,ih)':'min(iw,ih)':'(iw-min(iw,ih))/2':'(ih-min(iw,ih))/2',scale={VID_SIZE}:{VID_SIZE}:flags=lanczos,fps={FPS}\" -an -f rawvideo -pix_fmt bgra -",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var encPsi = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = $"-y -f rawvideo -vcodec rawvideo -s {W}x{H} -pix_fmt bgra -r {FPS} -i - -i \"{tmpAudio}\" -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p -colorspace bt709 -color_primaries bt709 -color_trc bt709 -c:a copy -shortest -movflags +faststart \"{outputPath}\"",
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var decProc = Process.Start(decPsi))
                using (var encProc = Process.Start(encPsi))
                {
                    if (decProc == null || encProc == null)
                        throw new Exception("No se pudo iniciar FFmpeg.");

                    int vidByteCount = VID_SIZE * VID_SIZE * 4;
                    byte[] vidBuf = new byte[vidByteCount];
                    byte[] outBuf = new byte[W * H * 4];

                    using (var canvasBmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
                    using (var vidBmp = new Bitmap(VID_SIZE, VID_SIZE, PixelFormat.Format32bppArgb))
                    using (var fontAvail = new Font("Segoe UI", 22f, FontStyle.Regular))
                    using (var fontTitle = new Font("Segoe UI", 38f, FontStyle.Bold))
                    using (var fontArtist = new Font("Segoe UI", 26f, FontStyle.Regular))
                    using (var fontTime = new Font("Segoe UI", 17f, FontStyle.Regular))
                    using (var brushBlue = new SolidBrush(ColorTranslator.FromHtml("#0066FF")))
                    using (var brushWhite = new SolidBrush(Color.White))
                    using (var brushNavy = new SolidBrush(ColorTranslator.FromHtml("#081229")))
                    using (var brushSlate = new SolidBrush(ColorTranslator.FromHtml("#65676B")))
                    using (var brushTrack = new SolidBrush(ColorTranslator.FromHtml("#E4E6EB")))
                    using (var brushShadow = new SolidBrush(Color.FromArgb(40, 0, 20, 70)))
                    {
                        const string availText = "A V A I L A B L E   N O W";

                        for (int idx = 0; idx < totalFrames; idx++)
                        {
                            ct.ThrowIfCancellationRequested();

                            int readTotal = 0;
                            while (readTotal < vidByteCount)
                            {
                                int n = decProc.StandardOutput.BaseStream.Read(vidBuf, readTotal, vidByteCount - readTotal);
                                if (n <= 0) break;
                                readTotal += n;
                            }

                            if (readTotal == vidByteCount)
                            {
                                var vLock = vidBmp.LockBits(new Rectangle(0, 0, VID_SIZE, VID_SIZE), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                                Marshal.Copy(vidBuf, 0, vLock.Scan0, vidByteCount);
                                vidBmp.UnlockBits(vLock);
                            }

                            double t = (double)idx / FPS;

                            using (var g = Graphics.FromImage(canvasBmp))
                            {
                                g.SmoothingMode = SmoothingMode.AntiAlias;
                                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                                g.Clear(ColorTranslator.FromHtml("#0066FF"));

                                if (t >= 0.08)
                                {
                                    double p = Math.Min(1.0, (t - 0.08) / 0.37);
                                    int nChars = (int)Math.Round(p * availText.Length);
                                    string shown = availText.Substring(0, nChars) + (t < 0.50 && nChars < availText.Length ? "|" : "");
                                    SizeF fullSz = g.MeasureString(availText, fontAvail);
                                    g.DrawString(shown, fontAvail, brushWhite, (W - fullSz.Width) / 2f, 288f);
                                }

                                double pCard = EaseOutCubic(t / 0.42);
                                int cardY = (int)Math.Round(H - (H - CARD_Y_FINAL) * pCard);

                                using (var shPath = RoundedRect(new RectangleF(CARD_X + 4, cardY + 14, CARD_W - 8, CARD_H), CARD_R))
                                    g.FillPath(brushShadow, shPath);

                                using (var cardPath = RoundedRect(new RectangleF(CARD_X, cardY, CARD_W, CARD_H), CARD_R))
                                    g.FillPath(brushWhite, cardPath);

                                int vx = CARD_X + VID_PAD;
                                int vy = cardY + VID_PAD;

                                if (t >= 0.18)
                                {
                                    double pWipe = EaseOutCubic((t - 0.18) / 0.34);
                                    int curW = Math.Max(4, (int)Math.Round(VID_SIZE * pWipe));
                                    int x0 = (VID_SIZE - curW) / 2;
                                    float rDyn = Math.Min(VID_R, curW / 2f);
                                    using (var clipPath = RoundedRect(new RectangleF(vx + x0, vy, curW, VID_SIZE), rDyn))
                                    {
                                        var state = g.Save();
                                        g.SetClip(clipPath);
                                        g.DrawImage(vidBmp, vx, vy, VID_SIZE, VID_SIZE);
                                        g.Restore(state);
                                    }
                                }

                                float titleX = CARD_X + VID_PAD;
                                float titleY = cardY + 716;
                                float artistY = cardY + 778;

                                if (t >= 0.38 && title.Length > 0)
                                {
                                    double pt = Math.Min(1.0, (t - 0.38) / 0.24);
                                    int nt = (int)Math.Round(pt * title.Length);
                                    string ts = title.Substring(0, nt) + (t < 0.62 ? "|" : "");
                                    g.DrawString(ts, fontTitle, brushNavy, titleX, titleY);
                                }

                                if (t >= 0.62 && artist.Length > 0)
                                {
                                    double pa = Math.Min(1.0, (t - 0.62) / 0.30);
                                    int na = (int)Math.Round(pa * artist.Length);
                                    string ars = artist.Substring(0, na) + (t < 0.98 ? "|" : "");
                                    g.DrawString(ars, fontArtist, brushSlate, titleX, artistY);
                                }

                                if (t >= 0.65 && bpm.Length > 0)
                                {
                                    double sb = SpringPop((t - 0.65) / 0.30);
                                    if (sb > 0.02)
                                    {
                                        float pw = 148f * (float)sb;
                                        float ph = 52f * (float)sb;
                                        float cx = CARD_X + CARD_W - VID_PAD - 74f;
                                        float cy = cardY + 768f;
                                        using (var pPath = RoundedRect(new RectangleF(cx - pw / 2f, cy - ph / 2f, pw, ph), ph / 2f))
                                            g.FillPath(brushBlue, pPath);
                                        if (sb > 0.45)
                                        {
                                            using (var scaledFont = new Font("Segoe UI", 19f * (float)sb, FontStyle.Bold))
                                            {
                                                SizeF bsz = g.MeasureString(bpm, scaledFont);
                                                g.DrawString(bpm, scaledFont, brushWhite, cx - bsz.Width / 2f, cy - bsz.Height / 2f);
                                            }
                                        }
                                    }
                                }

                                float barX = CARD_X + VID_PAD;
                                float barY = cardY + 885;
                                float barW = VID_SIZE;
                                float barH = 6f;

                                if (t >= 0.52)
                                {
                                    double pBar = EaseOutCubic((t - 0.52) / 0.28);
                                    float curBarW = (float)(barW * pBar);
                                    if (curBarW > 2f)
                                    {
                                        using (var trPath = RoundedRect(new RectangleF(barX, barY, curBarW, barH), 3f))
                                            g.FillPath(brushTrack, trPath);
                                    }

                                    if (t >= 0.80)
                                    {
                                        double prog = Math.Min(1.0, t / duration);
                                        float fillW = Math.Max(8f, (float)(barW * prog));
                                        using (var flPath = RoundedRect(new RectangleF(barX, barY, fillW, barH), 3f))
                                            g.FillPath(brushBlue, flPath);
                                        g.FillEllipse(brushBlue, barX + fillW - 9f, barY + barH / 2f - 9f, 18f, 18f);

                                        string tCur = FormatTime(t);
                                        string tRem = "-" + FormatTime(duration - t);
                                        g.DrawString(tCur, fontTime, brushSlate, barX, barY + 22f);
                                        SizeF rsz = g.MeasureString(tRem, fontTime);
                                        g.DrawString(tRem, fontTime, brushSlate, barX + barW - rsz.Width, barY + 22f);
                                    }
                                }
                            }

                            var cLock = canvasBmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                            Marshal.Copy(cLock.Scan0, outBuf, 0, outBuf.Length);
                            canvasBmp.UnlockBits(cLock);
                            encProc.StandardInput.BaseStream.Write(outBuf, 0, outBuf.Length);

                            if (idx % 10 == 0)
                                onProgress((int)Math.Round(100.0 * idx / totalFrames));
                        }
                    }

                    encProc.StandardInput.BaseStream.Close();
                    encProc.WaitForExit();
                    decProc.WaitForExit();
                }
            }
            finally
            {
                if (File.Exists(tmpAudio)) File.Delete(tmpAudio);
            }
        }
    }
}
