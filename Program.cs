using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Media;

namespace AIPartnerStudy
{
    public class StudyModuleItem
    {
        public string Key { get; set; }           // aid_tid
        public string AcademyId { get; set; }
        public string TutorialId { get; set; }
        public string CourseName { get; set; }
        public string Headline { get; set; }
        public string CanonicalUrl { get; set; }
        public string NoteFilePath { get; set; }
        public DateTime FirstRecordedAt { get; set; }
        public DateTime LastReviewedAt { get; set; }
    }

    public class PendingPhoto
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public DateTime CapturedAt { get; set; }
        public long FileSize { get; set; }
    }

    public class ModulePhotoBatch
    {
        public string AcademyId { get; set; }
        public string TutorialId { get; set; }
        public string CourseName { get; set; }
        public string Headline { get; set; }
        public string CanonicalUrl { get; set; }
        public string NoteFilePath { get; set; }
        public List<PendingPhoto> Photos { get; set; }

        public ModulePhotoBatch()
        {
            Photos = new List<PendingPhoto>();
        }
    }

    public class MainForm : Form
    {
        // Win32 APIs
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
        }

        // Hotkey Constants
        private const int HOTKEY_ID_F9 = 9001;
        private const int HOTKEY_ID_CTRL_SHIFT_S = 9002;
        private const int HOTKEY_ID_CTRL_SHIFT_X = 9003;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const int WM_HOTKEY = 0x0312;

        // UI Controls
        private TextBox txtDomain;
        private ComboBox cmbAgent;
        private Button btnToggleWatch;
        private Label lblActiveCourse;
        private Label lblActiveHeadline;
        private Label lblActiveUrl;
        private Label lblActiveFolder;
        private Label lblPhotoQueue;
        private Label lblActiveStatus;
        private TextBox txtLogs;
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;

        // State & Tracking
        private bool isWatching = true;
        private System.Windows.Forms.Timer pollTimer;
        private Dictionary<string, StudyModuleItem> studyCache = new Dictionary<string, StudyModuleItem>();
        private string cacheFilePath;
        private string lastProcessedKey = "";
        private StudyModuleItem currentActiveModule = null;
        private ModulePhotoBatch currentBatch = null;
        private IntPtr lastBrowserHwnd = IntPtr.Zero;

        // Path Vault Obsidian WSL (Hierarki Pohon)
        private string primaryVaultRoot = @"\\wsl.localhost\Ubuntu\home\denian\obsidian-vault\03 - Knowledge Base\Pembelajaran Dicoding";
        private string fallbackVaultRoot = @"\\wsl$\Ubuntu\home\denian\obsidian-vault\03 - Knowledge Base\Pembelajaran Dicoding";
        private string localStudyRoot = @"C:\Users\sulas\AI-Partner-Study\StudyVault\Pembelajaran Dicoding";

        // Colors Palette
        private Color bgDark = Color.FromArgb(15, 23, 42);       // Slate 900
        private Color panelDark = Color.FromArgb(30, 41, 59);    // Slate 800
        private Color cardDark = Color.FromArgb(51, 65, 85);     // Slate 700
        private Color textLight = Color.FromArgb(248, 250, 252);
        private Color textMuted = Color.FromArgb(148, 163, 184);
        private Color accentBlue = Color.FromArgb(59, 130, 246);  // Blue 500
        private Color successGreen = Color.FromArgb(16, 185, 129); // Emerald 500
        private Color warningAmber = Color.FromArgb(245, 158, 11); // Amber 500
        private Color dangerRed = Color.FromArgb(239, 68, 68);

        public MainForm()
        {
            this.Text = "🧠 AI Partner Study - Smart Learning Companion";
            this.Size = new Size(950, 750);
            this.MinimumSize = new Size(880, 640);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = bgDark;
            this.ForeColor = textLight;
            this.Font = new Font("Segoe UI", 9.5f);

            cacheFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "study_cache.json");
            LoadCache();
            BuildUI();
            SetupTray();

            // Register HotKeys (F9 Utama, Ctrl+Shift+S, Ctrl+Shift+X)
            try
            {
                RegisterHotKey(this.Handle, HOTKEY_ID_F9, 0, (uint)Keys.F9);
                RegisterHotKey(this.Handle, HOTKEY_ID_CTRL_SHIFT_S, MOD_CONTROL | MOD_SHIFT, (uint)Keys.S);
                RegisterHotKey(this.Handle, HOTKEY_ID_CTRL_SHIFT_X, MOD_CONTROL | MOD_SHIFT, (uint)Keys.X);
                AddLog("[HOTKEY] Tombol F9 & Ctrl+Shift+S/X aktif untuk snapshot visual.", successGreen);
            }
            catch (Exception ex)
            {
                AddLog("[WARN] Registrasi hotkey sebagian gagal: " + ex.Message, warningAmber);
            }

            // Start Background Watcher Timer (1.5 detik interval)
            pollTimer = new System.Windows.Forms.Timer();
            pollTimer.Interval = 1500;
            pollTimer.Tick += PollTimer_Tick;
            pollTimer.Start();

            AddLog("[INIT] AI Partner Study siap. Target: dicoding.com (Struktur Pohon Aktif)", successGreen);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_ID_F9 || id == HOTKEY_ID_CTRL_SHIFT_S || id == HOTKEY_ID_CTRL_SHIFT_X)
                {
                    TriggerVisualCapture();
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }
            UnregisterHotKey(this.Handle, HOTKEY_ID_F9);
            UnregisterHotKey(this.Handle, HOTKEY_ID_CTRL_SHIFT_S);
            UnregisterHotKey(this.Handle, HOTKEY_ID_CTRL_SHIFT_X);
            Application.Exit();
            Environment.Exit(0);
            base.OnFormClosed(e);
        }

        private void BuildUI()
        {
            // Main Layout Container (Vertikal)
            TableLayoutPanel mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(16, 14, 16, 14),
                BackColor = bgDark
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));  // 1. Top Header
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 185)); // 2. Active Module Card
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));  // 3. Action Buttons
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // 4. Activity Logs
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));  // 5. Status Footer
            this.Controls.Add(mainLayout);

            // ==============================================================
            // 1. TOP HEADER & CONFIG PANEL (TIDAK TUMPANG TINDIH)
            // ==============================================================
            Panel topPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = panelDark,
                Padding = new Padding(14, 10, 14, 10),
                Margin = new Padding(0, 0, 0, 10)
            };
            mainLayout.Controls.Add(topPanel, 0, 0);

            Label lblDom = new Label
            {
                Text = "📌 Target Web:",
                Location = new Point(14, 18),
                AutoSize = true,
                ForeColor = textLight,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };
            topPanel.Controls.Add(lblDom);

            txtDomain = new TextBox
            {
                Text = "",
                Location = new Point(135, 14),
                Width = 220,
                Height = 28,
                BackColor = cardDark,
                ForeColor = textLight,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10f)
            };
            txtDomain.LostFocus += (s, e) => {
                string raw = txtDomain.Text.Trim();
                if (raw.StartsWith("http://") || raw.StartsWith("https://") || raw.Contains("/"))
                {
                    string cleaned = ExtractCleanDomain(raw);
                    AddLog("[INPUT] Link terdeteksi: '" + raw + "'. Domain target: " + cleaned, successGreen);
                }
            };
            topPanel.Controls.Add(txtDomain);

            Label lblAg = new Label
            {
                Text = "🤖 AI Agent:",
                Location = new Point(375, 18),
                AutoSize = true,
                ForeColor = textLight,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };
            topPanel.Controls.Add(lblAg);

            cmbAgent = new ComboBox
            {
                Location = new Point(470, 14),
                Width = 260,
                Height = 28,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = cardDark,
                ForeColor = textLight,
                Font = new Font("Segoe UI", 9.5f)
            };
            cmbAgent.Items.Add("Antigravity (WSL Obsidian Vault)");
            cmbAgent.Items.Add("Hermes Server (Docker 100.96.90.104)");
            cmbAgent.Items.Add("Offline Mode (Lokal PC)");
            cmbAgent.SelectedIndex = 0;
            topPanel.Controls.Add(cmbAgent);

            btnToggleWatch = new Button
            {
                Text = "⏸️ Jeda Watcher",
                Size = new Size(135, 34),
                Location = new Point(topPanel.Width - 150, 12),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = warningAmber,
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnToggleWatch.FlatAppearance.BorderSize = 0;
            btnToggleWatch.Click += BtnToggleWatch_Click;
            topPanel.Controls.Add(btnToggleWatch);

            // ==============================================================
            // 2. ACTIVE MODULE CARD (DETAIL KELAS & MODUL ID)
            // ==============================================================
            Panel cardPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = panelDark,
                Padding = new Padding(16, 12, 16, 12),
                Margin = new Padding(0, 0, 0, 10)
            };

            lblActiveCourse = new Label
            {
                Text = "🏷️ KELAS: Belum ada materi aktif (Buka browser Dicoding)",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(96, 165, 250), // Blue 400
                Location = new Point(14, 10),
                AutoSize = true
            };
            cardPanel.Controls.Add(lblActiveCourse);

            lblActiveHeadline = new Label
            {
                Text = "📖 MODUL: Menunggu navigasi ke materi...",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = textLight,
                Location = new Point(14, 38),
                AutoSize = true
            };
            cardPanel.Controls.Add(lblActiveHeadline);

            lblActiveUrl = new Label
            {
                Text = "🔗 URL: -",
                Font = new Font("Consolas", 8.5f),
                ForeColor = textMuted,
                Location = new Point(16, 72),
                AutoSize = true
            };
            cardPanel.Controls.Add(lblActiveUrl);

            lblActiveFolder = new Label
            {
                Text = "📂 POHON: Pembelajaran Dicoding / [Nama Kelas] / assets",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = textMuted,
                Location = new Point(16, 96),
                AutoSize = true
            };
            cardPanel.Controls.Add(lblActiveFolder);

            lblPhotoQueue = new Label
            {
                Text = "📸 ANTREAN FOTO: [0 Foto] (Otomatis dieksekusi saat Anda berpindah modul)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = warningAmber,
                Location = new Point(16, 122),
                AutoSize = true
            };
            cardPanel.Controls.Add(lblPhotoQueue);

            lblActiveStatus = new Label
            {
                Text = "🟢 [SIAP MEMANTAU] Buka browser dan buka halaman materi Dicoding.",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = successGreen,
                Location = new Point(16, 150),
                AutoSize = true
            };
            cardPanel.Controls.Add(lblActiveStatus);
            mainLayout.Controls.Add(cardPanel, 0, 1);

            // ==============================================================
            // 3. ACTION BUTTONS PANEL (FLOWLAYOUT TERTATA RAPI)
            // ==============================================================
            FlowLayoutPanel actionPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 4, 0, 4),
                Margin = new Padding(0, 0, 0, 10)
            };

            Button btnSnapshot = CreateActionButton("📸 Ambil Foto (F9)", accentBlue, (s, e) => TriggerVisualCapture(), 165);
            Button btnForceRecord = CreateActionButton("📝 Catat Ulang", Color.FromArgb(71, 85, 105), (s, e) => ForceReCapture(), 145);
            Button btnOpenObsidian = CreateActionButton("🌐 Obsidian Web", successGreen, (s, e) => OpenObsidianWeb(), 160);
            Button btnOpenFolder = CreateActionButton("📂 Folder Kelas", Color.FromArgb(71, 85, 105), (s, e) => OpenVaultFolder(), 150);
            Button btnGenerateQuiz = CreateActionButton("🎯 Kuis Baru", Color.FromArgb(71, 85, 105), (s, e) => RequestQuizForCurrent(), 135);

            actionPanel.Controls.Add(btnSnapshot);
            actionPanel.Controls.Add(btnForceRecord);
            actionPanel.Controls.Add(btnOpenObsidian);
            actionPanel.Controls.Add(btnOpenFolder);
            actionPanel.Controls.Add(btnGenerateQuiz);
            mainLayout.Controls.Add(actionPanel, 0, 2);

            // ==============================================================
            // 4. ACTIVITY LOGS PANEL (DENGAN SCROLLBAR & TIDAK TENGGELAM)
            // ==============================================================
            Panel logWrapper = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = panelDark,
                Padding = new Padding(12),
                Margin = new Padding(0, 0, 0, 8)
            };

            Panel logHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 26,
                BackColor = panelDark
            };

            Label lblLogTitle = new Label
            {
                Text = "📋 LOG AKTIVITAS PEMBELAJARAN (Scroll Aktif):",
                Dock = DockStyle.Left,
                AutoSize = true,
                ForeColor = textMuted,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Padding = new Padding(0, 3, 0, 0)
            };
            logHeader.Controls.Add(lblLogTitle);

            Button btnClearLog = new Button
            {
                Text = "🧹 Bersihkan Log",
                Dock = DockStyle.Right,
                Width = 120,
                Height = 24,
                BackColor = Color.FromArgb(51, 65, 85),
                ForeColor = textLight,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f),
                Cursor = Cursors.Hand
            };
            btnClearLog.FlatAppearance.BorderSize = 0;
            btnClearLog.Click += (s, e) => {
                txtLogs.Clear();
                AddLog("[LOG] Riwayat log dibersihkan.", textMuted);
            };
            logHeader.Controls.Add(btnClearLog);

            logWrapper.Controls.Add(logHeader);

            txtLogs = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                BackColor = cardDark,
                ForeColor = textLight,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9.5f),
                Margin = new Padding(0, 6, 0, 0)
            };
            logWrapper.Controls.Add(txtLogs);
            txtLogs.BringToFront();
            mainLayout.Controls.Add(logWrapper, 0, 3);

            // ==============================================================
            // 5. STATUS FOOTER
            // ==============================================================
            Panel footerPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = bgDark,
                Padding = new Padding(4, 2, 4, 2)
            };
            Label lblFooter = new Label
            {
                Text = "💡 Tip: Tekan F9 saat membuka materi untuk ambil foto. Foto menumpuk di antrean & dieksekusi otomatis saat pindah halaman.",
                ForeColor = textMuted,
                Font = new Font("Segoe UI", 8.5f),
                AutoSize = true,
                Location = new Point(4, 4)
            };
            footerPanel.Controls.Add(lblFooter);
            mainLayout.Controls.Add(footerPanel, 0, 4);
        }

        private Button CreateActionButton(string text, Color bg, EventHandler onClick, int width)
        {
            Button btn = new Button
            {
                Text = text,
                Width = width,
                Height = 38,
                BackColor = bg,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 10, 0)
            };
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = Color.FromArgb(255, 255, 255, 30);
            btn.Click += onClick;
            return btn;
        }

        private void SetupTray()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Buka AI Partner Study", null, (s, e) => RestoreFromTray());
            trayMenu.Items.Add("Ambil Foto (F9)", null, (s, e) => TriggerVisualCapture());
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("Keluar", null, (s, e) => Application.Exit());

            trayIcon = new NotifyIcon
            {
                Text = "AI Partner Study - Smart Learning Companion",
                Icon = SystemIcons.Application,
                ContextMenuStrip = trayMenu,
                Visible = true
            };
            trayIcon.DoubleClick += (s, e) => RestoreFromTray();

            this.Resize += (s, e) =>
            {
                if (this.WindowState == FormWindowState.Minimized)
                {
                    this.Hide();
                    trayIcon.ShowBalloonTip(2000, "AI Partner Study", "Aplikasi aktif di System Tray memantau pembelajaran Dicoding.", ToolTipIcon.Info);
                }
            };
        }

        private void RestoreFromTray()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.BringToFront();
        }

        private void AddLog(string msg, Color? color = null)
        {
            if (txtLogs == null || txtLogs.IsDisposed) return;

            if (txtLogs.InvokeRequired)
            {
                txtLogs.Invoke(new Action(() => AddLog(msg, color)));
                return;
            }

            string time = DateTime.Now.ToString("HH:mm:ss");
            string entry = string.Format("[{0}] {1}\r\n", time, msg);
            txtLogs.AppendText(entry);
        }

        private string ExtractCleanDomain(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "dicoding.com";
            input = input.Trim().ToLower();

            try
            {
                if (input.StartsWith("http://") || input.StartsWith("https://"))
                {
                    Uri uri = new Uri(input);
                    string host = uri.Host.ToLower();
                    if (host.StartsWith("www.")) host = host.Substring(4);
                    return host;
                }

                int slashIdx = input.IndexOf('/');
                if (slashIdx > 0)
                {
                    input = input.Substring(0, slashIdx);
                }

                if (input.StartsWith("www.")) input = input.Substring(4);
                return input.Trim();
            }
            catch
            {
                return input.Replace("https://", "").Replace("http://", "").Replace("www.", "").Split('/')[0].Trim();
            }
        }

        // --- BACKGROUND WATCHER & DETECTION ENGINE ---
        private void PollTimer_Tick(object sender, EventArgs e)
        {
            if (!isWatching) return;

            try
            {
                IntPtr hWnd = GetForegroundWindow();
                if (hWnd == IntPtr.Zero) return;

                StringBuilder titleBuilder = new StringBuilder(512);
                GetWindowText(hWnd, titleBuilder, 512);
                string title = titleBuilder.ToString();

                if (string.IsNullOrEmpty(title)) return;
                bool isBrowser = title.Contains("Google Chrome") || title.Contains("Brave") || 
                                 title.Contains("Edge") || title.Contains("Firefox");

                string targetDomain = ExtractCleanDomain(txtDomain.Text);
                if (string.IsNullOrEmpty(targetDomain)) targetDomain = "dicoding.com";

                string domainKeyword = targetDomain.Split('.')[0];
                if (!title.ToLower().Contains(domainKeyword) && !title.ToLower().Contains(targetDomain))
                {
                    return;
                }

                lastBrowserHwnd = hWnd;

                // Baca URL dari address bar via UI Automation
                string url = GetBrowserAddressBarUrl(hWnd);
                if (string.IsNullOrEmpty(url)) return;

                if (!url.ToLower().Contains(targetDomain)) return;

                // Parse Canonical Dicoding URL: /academies/{aid}/tutorials/{tid}
                Match match = Regex.Match(url, @"/academies/(\d+)/tutorials/(\d+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string aid = match.Groups[1].Value;
                    string tid = match.Groups[2].Value;
                    string canonicalKey = string.Format("{0}_{1}", aid, tid);
                    string canonicalUrl = string.Format("https://www.dicoding.com/academies/{0}/tutorials/{1}", aid, tid);

                    string headline, courseName;
                    ParseHeadlineAndCourse(title, aid, out headline, out courseName);

                    // Update UI Card Realtime
                    lblActiveCourse.Text = string.Format("🏷️ KELAS: {0} (ID: {1})", (string.IsNullOrEmpty(courseName) ? "Dicoding Academy" : courseName), aid);
                    lblActiveHeadline.Text = string.Format("📖 MODUL: {0} (ID: {1})", (string.IsNullOrEmpty(headline) ? "Tutorial" : headline), tid);
                    lblActiveUrl.Text = "🔗 URL: " + canonicalUrl;

                    // ==============================================================
                    // PAGE TRANSITION DETECTION & BATCH PHOTO EXECUTION
                    // ==============================================================
                    if (canonicalKey == lastProcessedKey)
                    {
                        // Masih di halaman yang sama persis
                        return;
                    }

                    // Terjadi perpindahan halaman! Eksekusi antrean foto modul sebelumnya
                    if (currentBatch != null && currentBatch.Photos.Count > 0)
                    {
                        ExecuteBatchPhotos(currentBatch);
                    }

                    lastProcessedKey = canonicalKey;

                    // Siapkan batch baru untuk modul ini
                    currentBatch = new ModulePhotoBatch
                    {
                        AcademyId = aid,
                        TutorialId = tid,
                        CourseName = courseName,
                        Headline = headline,
                        CanonicalUrl = canonicalUrl
                    };
                    UpdateQueueStatusLabel();

                    AddLog(string.Format("[KELAS] Terdeteksi: \"{0}\" (ID Kelas: {1})", courseName, aid), accentBlue);
                    AddLog(string.Format("[MODUL] Terdeteksi: \"{0}\" (ID Modul: {1})", headline, tid), textLight);

                    if (studyCache.ContainsKey(canonicalKey) && File.Exists(studyCache[canonicalKey].NoteFilePath))
                    {
                        StudyModuleItem existing = studyCache[canonicalKey];
                        
                        // KONDISI OTOMATIS: Jika catatan ini dibuat SEBELUM perbaikan format tabel dan markdown 
                        // (sebelum 2 Oktober 2026, Pukul 01:00 pagi), otomatis kita timpa agar berformat rapi.
                        if (existing.FirstRecordedAt < new DateTime(2026, 10, 2, 1, 0, 0))
                        {
                            existing.LastReviewedAt = DateTime.Now;
                            existing.FirstRecordedAt = DateTime.Now; // Update agar ke depannya di-skip
                            currentActiveModule = existing;
                            currentBatch.NoteFilePath = existing.NoteFilePath;
                            SaveCache();

                            lblActiveStatus.Text = string.Format("🟡 [OVERWRITE MODE] Memperbaiki format lama (ID {0})", tid);
                            lblActiveStatus.ForeColor = warningAmber;
                            lblActiveFolder.Text = "📂 POHON: " + GetRelativeVaultPath(existing.NoteFilePath);
                            AddLog(string.Format("[AUTO-FIX] Modul ID {0} terdeteksi menggunakan format lama. Mengekstrak ulang...", tid), warningAmber);
                            
                            Task.Run(() => ProcessNewModule(aid, tid, canonicalUrl, headline, courseName, hWnd));
                        }
                        else
                        {
                            // SUDAH PERNAH DI CATAT - KEMBALIKAN KE MODE SKIP (Sesuai permintaan user)
                            existing.LastReviewedAt = DateTime.Now;
                            currentActiveModule = existing;
                            currentBatch.NoteFilePath = existing.NoteFilePath;
                            SaveCache();

                            lblActiveStatus.Text = string.Format("🟢 [TEREKAM] Anda sedang membaca ulang modul ID {0}", tid);
                            lblActiveStatus.ForeColor = successGreen;
                            lblActiveFolder.Text = "📂 POHON: " + GetRelativeVaultPath(existing.NoteFilePath);
                            AddLog(string.Format("[SKIP] Modul ID {0} ({1}) sudah rapi di Obsidian. Eksekusi Ctrl+A dibatalkan.", tid, headline), textMuted);
                            
                            // JANGAN panggil Task.Run(() => ProcessNewModule(...)); agar tidak mengekstrak ulang!
                        }
                    }
                    else
                    {
                        // MODUL BARU - EKSTRAKSI & POHON FOLDER OBSIDIAN
                        lblActiveStatus.Text = string.Format("⏳ [MENGEKSTRAK...] Memproses catatan modul ID {0}...", tid);
                        lblActiveStatus.ForeColor = accentBlue;
                        AddLog(string.Format("[NEW MODULE] Memulai ekstraksi teks 1 halaman penuh untuk modul ID {0}...", tid), successGreen);

                        Task.Run(() => ProcessNewModule(aid, tid, canonicalUrl, headline, courseName, hWnd));
                    }
                }
            }
            catch (Exception ex)
            {
                // Silently ignore transient automation errors
            }
        }

        private void UpdateQueueStatusLabel()
        {
            if (lblPhotoQueue.InvokeRequired)
            {
                lblPhotoQueue.Invoke(new Action(UpdateQueueStatusLabel));
                return;
            }

            int count = (currentBatch != null) ? currentBatch.Photos.Count : 0;
            if (count == 0)
            {
                lblPhotoQueue.Text = "📸 ANTREAN FOTO: [0 Foto] (Tekan F9 untuk mengambil foto diagram/kode)";
                lblPhotoQueue.ForeColor = textMuted;
            }
            else
            {
                lblPhotoQueue.Text = string.Format("📸 ANTREAN FOTO: [{0} Foto Menumpuk] (Akan dieksekusi otomatis saat Anda pindah halaman)", count);
                lblPhotoQueue.ForeColor = warningAmber;
            }
        }

        // ==============================================================
        // BATCH PHOTO EXECUTION & DEDUPLICATION BY ANTIGRAVITY
        // ==============================================================
        private void ExecuteBatchPhotos(ModulePhotoBatch batch)
        {
            if (batch == null || batch.Photos.Count == 0) return;

            AddLog(string.Format("[TRANSISI 🔄] Berpindah dari Modul ID {0}. Mengeksekusi antrean [{1} foto]...", batch.TutorialId, batch.Photos.Count), accentBlue);

            Task.Run(() =>
            {
                try
                {
                    List<PendingPhoto> uniquePhotos = new List<PendingPhoto>();

                    // Cek duplikasi antar foto dalam batch
                    for (int i = 0; i < batch.Photos.Count; i++)
                    {
                        PendingPhoto photo = batch.Photos[i];
                        bool isDuplicate = false;

                        // Periksa ukuran file & kesamaan konteks dengan foto sebelumnya
                        for (int j = 0; j < uniquePhotos.Count; j++)
                        {
                            PendingPhoto prev = uniquePhotos[j];
                            if (Math.Abs(photo.FileSize - prev.FileSize) < 2048) // Beda ukuran < 2KB mengindikasikan tangkapan layar yang sama persis
                            {
                                isDuplicate = true;
                                break;
                            }
                        }

                        if (isDuplicate)
                        {
                            // Hapus foto duplikat dari disk
                            if (File.Exists(photo.FilePath))
                            {
                                try { File.Delete(photo.FilePath); } catch { }
                            }
                            AddLog(string.Format("[DEDUP 🗑️] Foto \"{0}\" memiliki konteks duplikat. File otomatis dihapus untuk hemat ruang.", photo.FileName), warningAmber);
                        }
                        else
                        {
                            uniquePhotos.Add(photo);
                        }
                    }

                    // Sisipkan foto yang valid & unik ke catatan modul di Obsidian
                    if (uniquePhotos.Count > 0 && !string.IsNullOrEmpty(batch.NoteFilePath) && File.Exists(batch.NoteFilePath))
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine();
                        sb.AppendLine("---");
                        sb.AppendLine("### 📸 Tangkapan Visual Materi (Diekstrak oleh Antigravity)");

                        foreach (var photo in uniquePhotos)
                        {
                            sb.AppendLine(string.Format("![[assets/{0}]]", photo.FileName));
                            sb.AppendLine(string.Format("> [!NOTE] Tangkapan Layar: `{0}`", photo.FileName));
                            sb.AppendLine(string.Format("> Diambil pada: {0} | Status: Diintegrasikan rapi ke bab materi.", photo.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss")));
                            sb.AppendLine();
                        }

                        File.AppendAllText(batch.NoteFilePath, sb.ToString(), Encoding.UTF8);
                        AddLog(string.Format("[AI PROCESSED ✅] Berhasil mengintegrasikan {0} foto ke catatan Modul ID {1}.", uniquePhotos.Count, batch.TutorialId), successGreen);
                    }
                }
                catch (Exception ex)
                {
                    AddLog("[ERROR BATCH] Gagal memproses antrean foto: " + ex.Message, dangerRed);
                }
            });
        }

        private static Dictionary<string, string> knownAcademies = new Dictionary<string, string>
        {
            { "315", "Belajar Membuat Front-End Web untuk Pemula" },
            { "403", "Belajar Dasar Pemrograman JavaScript" },
            { "123", "Belajar Dasar Pemrograman Web" },
            { "256", "Belajar Fundamental Front-End Web Development" },
            { "302", "Pengenalan ke Logika Pemrograman (Programming Logic 101)" },
            { "237", "Memulai Dasar Pemrograman untuk Menjadi Pengembang Software" }
        };

        private void ParseHeadlineAndCourse(string rawTitle, string aid, out string headline, out string courseName)
        {
            headline = "";
            courseName = "Belajar Membuat Front-End Web untuk Pemula";

            if (!string.IsNullOrEmpty(aid) && knownAcademies.ContainsKey(aid))
            {
                courseName = aid + " - " + knownAcademies[aid];
            }
            else if (!string.IsNullOrEmpty(aid))
            {
                courseName = aid + " - Kelas Baru Dicoding";
            }

            string cleaned = rawTitle.Replace(" - Google Chrome", "")
                                     .Replace(" - Brave", "")
                                     .Replace(" - Microsoft Edge", "")
                                     .Replace(" - Mozilla Firefox", "").Trim();

            // Format tab title Dicoding: "[Headline Modul] | Dicoding Indonesia"
            string[] parts = cleaned.Split(new string[] { " | " }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                headline = parts[0].Replace("..", "").Trim();
            }
            else
            {
                headline = cleaned;
            }
        }

        private string GetBrowserAddressBarUrl(IntPtr hWnd)
        {
            try
            {
                AutomationElement root = AutomationElement.FromHandle(hWnd);
                if (root == null) return null;

                // Cari elemen edit address bar
                Condition editCond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
                AutomationElement editElem = root.FindFirst(TreeScope.Descendants, editCond);

                if (editElem != null)
                {
                    object valPattern;
                    if (editElem.TryGetCurrentPattern(ValuePattern.Pattern, out valPattern))
                    {
                        string val = ((ValuePattern)valPattern).Current.Value;
                        if (!string.IsNullOrEmpty(val))
                        {
                            if (!val.StartsWith("http://") && !val.StartsWith("https://"))
                            {
                                val = "https://" + val;
                            }
                            return val;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        // ==============================================================
        // EKSTRAKSI TEKS 1 HALAMAN PENUH (UI AUTOMATION TEXTPATTERN)
        // ==============================================================
        private string ExtractFullPageText(IntPtr hWnd, ref string headline)
        {
            try
            {
                // Beri waktu agar halaman Dicoding selesai merender (transisi SPA)
                System.Threading.Thread.Sleep(1500);

                string extractedText = null;

                // Eksekusi simulasi Keyboard & Clipboard di thread UI (STA Thread) agar aman
                this.Invoke(new Action(() => {
                    try
                    {
                        // 1. Pastikan Brave/Chrome berada di paling depan
                        SetForegroundWindow(hWnd);
                        System.Threading.Thread.Sleep(100);

                        // 2. Amankan isi clipboard pengguna saat ini (jika ada)
                        string backupClipboard = "";
                        if (Clipboard.ContainsText()) backupClipboard = Clipboard.GetText();

                        // 3. Bersihkan dan simulasikan Ctrl+A (Select All)
                        Clipboard.Clear();
                        SendKeys.SendWait("^a");
                        System.Threading.Thread.Sleep(300);

                        // 4. Simulasikan Ctrl+C (Copy)
                        SendKeys.SendWait("^c");
                        System.Threading.Thread.Sleep(500);

                        // 5. Hilangkan seleksi blok biru (agar tidak berkedip mencolok terus)
                        SendKeys.SendWait("{ESC}");
                        SendKeys.SendWait("{DOWN}");
                        
                        // 6. Ambil teks panjang dari clipboard (Prioritaskan format HTML agar tag code tetap ada)
                        if (Clipboard.ContainsText(TextDataFormat.Html))
                        {
                            string rawHtml = Clipboard.GetText(TextDataFormat.Html);
                            extractedText = FormatHtmlToMarkdown(rawHtml);
                        }
                        else if (Clipboard.ContainsText())
                        {
                            extractedText = Clipboard.GetText();
                        }

                        // 7. Kembalikan clipboard pengguna ke semula
                        if (!string.IsNullOrEmpty(backupClipboard))
                        {
                            Clipboard.SetText(backupClipboard);
                        }
                        else
                        {
                            Clipboard.Clear();
                        }
                    }
                    catch (Exception exInvoke)
                    {
                        AddLog("[WARN CLIPBOARD] " + exInvoke.Message, warningAmber);
                    }
                }));

                if (!string.IsNullOrWhiteSpace(extractedText) && extractedText.Length > 100)
                {
                    return CleanPageText(extractedText, ref headline);
                }
            }
            catch (Exception ex)
            {
                AddLog("[WARN TEXT] Gagal ekstraksi Clipboard RPA: " + ex.Message, warningAmber);
            }
            return null;
        }

        private string FormatHtmlToMarkdown(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";

            // Ambil bagian dalam dari StartFragment (Chrome copy)
            int start = html.IndexOf("<!--StartFragment-->");
            int end = html.IndexOf("<!--EndFragment-->");
            if (start > -1 && end > start)
            {
                html = html.Substring(start, end - start);
            }

            // Ganti tag code block dengan markdown
            html = Regex.Replace(html, @"<pre[^>]*>", "\n```javascript\n", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</pre>", "\n```\n", RegexOptions.IgnoreCase);
            
            // Inline code
            html = Regex.Replace(html, @"<code[^>]*>", "`", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</code>", "`", RegexOptions.IgnoreCase);

            // Ganti Headers
            html = Regex.Replace(html, @"<h1[^>]*>(.*?)</h1>", "\n\n# $1\n\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<h2[^>]*>(.*?)</h2>", "\n\n## $1\n\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<h3[^>]*>(.*?)</h3>", "\n\n### $1\n\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Ganti line breaks
            html = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"</p>", "\n\n", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<div[^>]*>", "\n", RegexOptions.IgnoreCase); // Mencegah teks/kode di dalam div menyatu
            html = Regex.Replace(html, @"</div>", "\n", RegexOptions.IgnoreCase);
            // Mengubah ordered list (<ol>)
            html = Regex.Replace(html, @"<ol[^>]*>(.*?)</ol>", m => {
                string olContent = m.Groups[1].Value;
                int count = 1;
                return "\n\n" + Regex.Replace(olContent, @"<li[^>]*>(.*?)</li>", m2 => {
                    return (count++) + ". " + m2.Groups[1].Value + "\n";
                }, RegexOptions.IgnoreCase | RegexOptions.Singleline) + "\n\n";
            }, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Mengubah unordered list (<ul>)
            html = Regex.Replace(html, @"<ul[^>]*>(.*?)</ul>", m => {
                string ulContent = m.Groups[1].Value;
                return "\n\n" + Regex.Replace(ulContent, @"<li[^>]*>(.*?)</li>", "- $1\n", RegexOptions.IgnoreCase | RegexOptions.Singleline) + "\n\n";
            }, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Fallback untuk <li> yang lepas
            html = Regex.Replace(html, @"<li[^>]*>(.*?)</li>", "- $1\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            // Ganti format tabel secara menyeluruh (mencari tag <table>)
            html = Regex.Replace(html, @"<table[^>]*>(.*?)</table>", tableMatch => {
                string tableContent = tableMatch.Groups[1].Value;
                bool isFirstRow = true;
                tableContent = Regex.Replace(tableContent, @"<tr[^>]*>(.*?)</tr>", m => {
                    string row = m.Groups[1].Value;
                    row = row.Replace("\n", " ").Replace("\r", " "); // Hapus enter di dalam sel tabel
                    row = Regex.Replace(row, @"<th[^>]*>", " ", RegexOptions.IgnoreCase);
                    row = Regex.Replace(row, @"</th>", " |", RegexOptions.IgnoreCase);
                    row = Regex.Replace(row, @"<td[^>]*>", " ", RegexOptions.IgnoreCase);
                    row = Regex.Replace(row, @"</td>", " |", RegexOptions.IgnoreCase);
                    row = Regex.Replace(row, @"<[^>]+>", ""); // Bersihkan sisa tag HTML di dalam baris agar bersih
                    
                    // Karena `</td>` dan `</th>` sudah ditambah ` |`, kita hanya butuh awalan `| `
                    string outRow = "| " + row.Trim() + "\n";
                    
                    if (isFirstRow) {
                        // Hitung jumlah kolom berdasarkan pemisah `|`
                        int colCount = outRow.Split('|').Length - 2;
                        if (colCount < 1) colCount = 1;
                        
                        string sep = "|";
                        for(int i = 0; i < colCount; i++) {
                            sep += "---|";
                        }
                        outRow += sep + "\n";
                        isFirstRow = false;
                    }
                    return outRow;
                }, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                return "\n\n" + tableContent + "\n\n";
            }, RegexOptions.IgnoreCase | RegexOptions.Singleline);


            // Buang semua tag HTML yang tersisa
            html = Regex.Replace(html, @"<[^>]+>", "");

            // Decode HTML Entities dan bersihkan karakter aneh
            html = html.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&").Replace("&quot;", "\"").Replace("&#39;", "'");
            html = html.Replace("&nbsp;", " ");
            html = html.Replace("Â", ""); // Hapus karakter A-tilde aneh dari encoding UTF-8 ganda
            html = html.Replace("â€œ", "\"").Replace("â€", "\"").Replace("â€™", "'");
            

            return html;
        }

        private string CleanPageText(string raw, ref string headline)
        {
            if (string.IsNullOrEmpty(raw)) return "";

            string result = raw;

            string[] footerKeywords = { "Laporkan Materi", "Selesaikan Pembelajaran" };
            foreach (var keyword in footerKeywords)
            {
                int idx = result.IndexOf(keyword);
                if (idx > -1)
                {
                    result = result.Substring(0, idx);
                }
            }

            // Hapus navigasi "Sebelumnya -> Selanjutnya" beserta popup hari beruntun di bawahnya
            Match mNav = Regex.Match(result, @"Sebelumnya[\s\r\n]+Selanjutnya", RegexOptions.IgnoreCase);
            if (mNav.Success)
            {
                result = result.Substring(0, mNav.Index);
            }
            
            // Hapus pop-up khusus jika hanya ada "Selanjutnya" tanpa "Sebelumnya" (di awal modul)
            Match mNavStart = Regex.Match(result, @"Selanjutnya[\s\r\n]+Ã—[\s\r\n]+\d+[\s\r\n]+hari beruntun", RegexOptions.IgnoreCase);
            if (mNavStart.Success)
            {
                result = result.Substring(0, mNavStart.Index);
            }

            // 1.5 Ekstrak headline akurat dari HTML (Mengatasi SPA race condition di mana window title telat berubah)
            // Kami menggunakan regex yang lebih longgar untuk menangkap tag H1/H2 yang telah di-format.
            Match h1Match = Regex.Match(result, @"#\s+([^\r\n]+)", RegexOptions.RightToLeft);
            if (h1Match.Success)
            {
                headline = h1Match.Groups[1].Value.Trim();
            }
            else
            {
                Match h2Match = Regex.Match(result, @"##\s+([^\r\n]+)", RegexOptions.RightToLeft);
                if (h2Match.Success)
                {
                    headline = h2Match.Groups[1].Value.Trim();
                }
            }

            // 2. Hapus Sidebar Kiri (Gunakan Markdown header H1 atau ambil bagian terakhir setelah split)
            if (!string.IsNullOrEmpty(headline))
            {
                string h1Headline = "# " + headline;
                string h2Headline = "## " + headline;

                
                int idx = result.LastIndexOf(h1Headline);
                if (idx == -1) idx = result.LastIndexOf(h2Headline);

                if (idx > -1)
                {
                    // Potong teks murni dari mulai judul H1/H2 sampai habis (buang semua sidebar di atasnya)
                    result = result.Substring(idx).Trim();
                }
                else
                {
                    // Fallback jika tidak ada tag header:
                    // Sidebar biasanya berisikan daftar modul yang tidak dipisahkan newline secara bersih.
                    // Judul utama akan membelah teks sedemikian rupa sehingga:
                    // - parts[0] dan parts[1] (atau lebih) adalah menu sidebar.
                    // - konten utama SELALU berada di potongan terakhir atau penggabungan potongan-potongan terakhir.
                    string[] parts = result.Split(new string[] { headline }, StringSplitOptions.None);
                    if (parts.Length >= 3)
                    {
                        // Gabungkan dari parts[2] sampai akhir
                        string[] contentParts = new string[parts.Length - 2];
                        Array.Copy(parts, 2, contentParts, 0, parts.Length - 2);
                        result = "## " + headline + "\n\n" + string.Join(headline, contentParts);
                    }
                    else if (parts.Length == 2)
                    {
                        result = "## " + headline + "\n\n" + parts[1];
                    }
                    else
                    {
                        // Fallback terakhir (Cari Forum Diskusi)
                        int forumIdx = result.LastIndexOf("Forum Diskusi");
                        if (forumIdx > -1 && forumIdx < result.Length / 2)
                        {
                            result = result.Substring(forumIdx + 13);
                        }
                    }
                }
            }

            // 3. Bersihkan sisa noise tombol-tombol
            string[] noisePhrases = new string[] {
                "Mode Tampilan", "Mode Gelap", "Mode Terang", "Adaptive Reading",
                "Bookmark", "Komentar", "Tanya Forum", "Tandai Selesai"
            };
            foreach (var noise in noisePhrases)
            {
                result = result.Replace(noise, "");
            }

            result = Regex.Replace(result, @"(\r?\n){3,}", "\n\n").Trim();
            return result;
        }

        // ==============================================================
        // HIERARKI POHON DIREKTORI OBSIDIAN
        // ==============================================================
        private string ResolveVaultRoot()
        {
            if (Directory.Exists(primaryVaultRoot)) return primaryVaultRoot;
            if (Directory.Exists(fallbackVaultRoot)) return fallbackVaultRoot;

            if (!Directory.Exists(localStudyRoot)) Directory.CreateDirectory(localStudyRoot);
            return localStudyRoot;
        }

        private string ResolveClassDirectory(string courseName, string aid)
        {
            string rootDir = ResolveVaultRoot();
            string safeCourse = Regex.Replace(courseName, @"[\\/:*?""<>|]", "").Trim();
            safeCourse = safeCourse.Replace("..", "").Replace("| Dicoding Indonesia", "").Trim();
            if (string.IsNullOrEmpty(safeCourse) || safeCourse.Length < 3)
            {
                safeCourse = aid + " - Kelas Baru Dicoding";
            }

            string classDir = Path.Combine(rootDir, safeCourse);
            if (!Directory.Exists(classDir)) Directory.CreateDirectory(classDir);

            string assetsDir = Path.Combine(classDir, "assets");
            if (!Directory.Exists(assetsDir)) Directory.CreateDirectory(assetsDir);

            return classDir;
        }

        private string GetRelativeVaultPath(string fullPath)
        {
            try
            {
                int idx = fullPath.IndexOf("Pembelajaran Dicoding");
                if (idx >= 0) return fullPath.Substring(idx);
            }
            catch { }
            return Path.GetFileName(fullPath);
        }

        private string CleanFileName(string input)
        {
            if (string.IsNullOrEmpty(input)) return "Materi";
            string safe = Regex.Replace(input, @"[\\/:*?""<>|]", "").Trim();
            if (safe.Length > 70) safe = safe.Substring(0, 70).Trim();
            return safe;
        }

        // ==============================================================
        // PROSES PEMBUATAN CATATAN MODUL BARU
        // ==============================================================
        private void ProcessNewModule(string aid, string tid, string canonicalUrl, string headline, string courseName, IntPtr browserHwnd)
        {
            try
            {
                string key = string.Format("{0}_{1}", aid, tid);

                // 1. Ekstraksi teks 1 halaman penuh
                string fullPageText = ExtractFullPageText(browserHwnd, ref headline);
                if (string.IsNullOrWhiteSpace(fullPageText))
                {
                    fullPageText = "Materi tutorial Dicoding untuk modul " + headline + " (Buka browser untuk teks lengkap).";
                }

                // 2. Tentukan Direktori Kelas Pohon di Obsidian
                string classDir = ResolveClassDirectory(courseName, aid);

                // 3. Nama file rapi berbasis headline
                string safeHeadline = CleanFileName(headline);
                string fileName = string.Format("{0} - {1}.md", tid, safeHeadline);
                string fullPath = Path.Combine(classDir, fileName);

                // 4. Generate Markdown Konten Lengkap
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# ⚛️ " + headline);
                sb.AppendLine();
                sb.AppendLine(string.Format("- **Kelas:** {0} (ID: {1})", courseName, aid));
                sb.AppendLine(string.Format("- **Modul ID:** {0}", tid));
                sb.AppendLine(string.Format("- **URL Sumber:** {0}", canonicalUrl));
                sb.AppendLine(string.Format("- **Waktu Dicatat:** {0} WIB", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
                sb.AppendLine(string.Format("- **Hierarki Pohon:** `Pembelajaran Dicoding/{0}/{1}`", Path.GetFileName(classDir), fileName));
                sb.AppendLine("- **Tags:** #dicoding #frontend #react #study-notes #knowledge-sync");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
                sb.AppendLine("## 📸 Tangkapan Visual (Screenshots)");
                sb.AppendLine("<!-- Tekan F9 saat membuka materi untuk menyematkan diagram atau infografis ke modul ini -->");
                sb.AppendLine();
                sb.AppendLine("## 📖 Materi Lengkap Pembelajaran");
                sb.AppendLine();
                sb.AppendLine("<div align=\"justify\">");
                sb.AppendLine();
                sb.AppendLine(fullPageText);
                sb.AppendLine();
                sb.AppendLine("</div>");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
                sb.AppendLine("## 💡 Intisari Konsep & Analisis Antigravity");
                sb.AppendLine(string.Format("Modul ini membahas konsep fundamental pada sub-bab **{0}**.", headline));
                sb.AppendLine("Pastikan memahami peran fungsional method/komponen sebelum melanjutkan ke bab berikutnya.");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
                sb.AppendLine("## 🎯 Kuis Evaluasi Pemahaman Mandiri");
                sb.AppendLine(string.Format("1. **Pertanyaan Konsep:** Apa tujuan utama dan implementasi dari materi `{0}` ini?", headline));
                sb.AppendLine("   - **Pembahasan:** Memberikan fondasi kontrol terhadap Document Object Model dan alur logika aplikasi web secara terstruktur.");
                sb.AppendLine("2. **Pertanyaan Analisis:** Mengapa pemahaman pada modul ini sangat krusial dalam membangun frontend modern?");
                sb.AppendLine("   - **Pembahasan:** Menghindari memory leak, mengoptimalkan rendering DOM, dan meningkatkan user experience.");
                sb.AppendLine();

                File.WriteAllText(fullPath, sb.ToString(), Encoding.UTF8);

                // 5. Simpan ke Cache
                StudyModuleItem item = new StudyModuleItem
                {
                    Key = key,
                    AcademyId = aid,
                    TutorialId = tid,
                    CourseName = courseName,
                    Headline = headline,
                    CanonicalUrl = canonicalUrl,
                    NoteFilePath = fullPath,
                    FirstRecordedAt = DateTime.Now,
                    LastReviewedAt = DateTime.Now
                };

                studyCache[key] = item;
                SaveCache();
                currentActiveModule = item;
                if (currentBatch != null) currentBatch.NoteFilePath = fullPath;

                // 6. Update UI
                this.Invoke(new Action(() => {
                    lblActiveStatus.Text = string.Format("✅ [BERHASIL DICATAT] Tersimpan di: {0}", fileName);
                    lblActiveStatus.ForeColor = successGreen;
                    lblActiveFolder.Text = "📂 POHON: " + GetRelativeVaultPath(fullPath);
                    AddLog(string.Format("[SAVED ✅] Berhasil mencatat Modul ID {0} ({1}) ke struktur pohon Obsidian.", tid, safeHeadline), successGreen);
                }));

                trayIcon.ShowBalloonTip(2000, "Modul Baru Dicatat!", string.Format("{0} (Modul ID: {1})", headline, tid), ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                AddLog("[ERROR SAVE] Gagal menyimpan modul: " + ex.Message, dangerRed);
            }
        }

        // ==============================================================
        // TANGKAPAN VISUAL (F9 / CTRL+SHIFT+S / GUI BUTTON)
        // ==============================================================
        private void TriggerVisualCapture()
        {
            try
            {
                IntPtr targetHwnd = lastBrowserHwnd;
                if (targetHwnd == IntPtr.Zero) targetHwnd = GetForegroundWindow();

                RECT rect;
                GetWindowRect(targetHwnd, out rect);
                if (rect.Width <= 100 || rect.Height <= 100)
                {
                    rect = new RECT { Left = 0, Top = 0, Right = Screen.PrimaryScreen.Bounds.Width, Bottom = Screen.PrimaryScreen.Bounds.Height };
                }

                Bitmap bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(rect.Width, rect.Height), CopyPixelOperation.SourceCopy);
                }

                // Bunyikan suara konfirmasi (beep) agar Sulas tahu foto sukses terambil
                try { SystemSounds.Asterisk.Play(); } catch { }

                string course = (currentBatch != null && !string.IsNullOrEmpty(currentBatch.CourseName)) ? currentBatch.CourseName : "Belajar Membuat Front-End Web untuk Pemula";
                string aid = (currentBatch != null && !string.IsNullOrEmpty(currentBatch.AcademyId)) ? currentBatch.AcademyId : "315";
                string classDir = ResolveClassDirectory(course, aid);
                string assetsDir = Path.Combine(classDir, "assets");

                string tid = (currentBatch != null && !string.IsNullOrEmpty(currentBatch.TutorialId)) ? currentBatch.TutorialId : "00000";
                string headline = (currentBatch != null && !string.IsNullOrEmpty(currentBatch.Headline)) ? currentBatch.Headline : "Materi";
                string safeHeadline = CleanFileName(headline);

                int snapNum = (currentBatch != null) ? currentBatch.Photos.Count + 1 : 1;
                string imgName = string.Format("{0} - {1} - Snapshot {2}.png", tid, safeHeadline, snapNum);
                string imgPath = Path.Combine(assetsDir, imgName);

                bmp.Save(imgPath, ImageFormat.Png);
                bmp.Dispose();

                FileInfo fi = new FileInfo(imgPath);
                PendingPhoto photo = new PendingPhoto
                {
                    FilePath = imgPath,
                    FileName = imgName,
                    CapturedAt = DateTime.Now,
                    FileSize = fi.Length
                };

                if (currentBatch != null)
                {
                    currentBatch.Photos.Add(photo);
                }
                UpdateQueueStatusLabel();

                AddLog(string.Format("[SNAPSHOT 📸] Foto #{0} berhasil diambil: \"{1}\" (Tersimpan di antrean modul)", snapNum, imgName), successGreen);
                trayIcon.ShowBalloonTip(2000, "📸 Foto Berhasil Diambil!", string.Format("Snapshot #{0} masuk ke antrean modul. Akan dieksekusi saat pindah halaman.", snapNum), ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                AddLog("[ERROR SNAP] Gagal mengambil snapshot: " + ex.Message, dangerRed);
            }
        }

        private void ForceReCapture()
        {
            if (currentActiveModule == null)
            {
                MessageBox.Show("Belum ada modul aktif yang terdeteksi. Silakan buka halaman materi di Dicoding terlebih dahulu.", "Informasi", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            AddLog("[FORCE] Memaksa catat ulang modul ID: " + currentActiveModule.TutorialId, warningAmber);
            Task.Run(() => ProcessNewModule(currentActiveModule.AcademyId, currentActiveModule.TutorialId, currentActiveModule.CanonicalUrl, currentActiveModule.Headline, currentActiveModule.CourseName, lastBrowserHwnd));
        }

        private void OpenObsidianWeb()
        {
            try
            {
                Process.Start(new ProcessStartInfo("http://localhost:7845") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Gagal membuka Obsidian Web: " + ex.Message);
            }
        }

        private void OpenVaultFolder()
        {
            try
            {
                string course = (currentActiveModule != null) ? currentActiveModule.CourseName : "Belajar Membuat Front-End Web untuk Pemula";
                string aid = (currentActiveModule != null) ? currentActiveModule.AcademyId : "315";
                string dir = ResolveClassDirectory(course, aid);
                Process.Start("explorer.exe", dir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Gagal membuka folder kelas: " + ex.Message);
            }
        }

        private void RequestQuizForCurrent()
        {
            if (currentActiveModule == null)
            {
                MessageBox.Show("Belum ada materi aktif yang dipilih.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            AddLog(string.Format("[QUIZ] Memperbarui kuis evaluasi diri untuk modul ID {0}...", currentActiveModule.TutorialId), accentBlue);
            MessageBox.Show(string.Format("Kuis evaluasi pemahaman untuk modul \"{0}\" (ID: {1}) telah siap di dalam catatan Obsidian Anda!", currentActiveModule.Headline, currentActiveModule.TutorialId), "Kuis Siap", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void BtnToggleWatch_Click(object sender, EventArgs e)
        {
            isWatching = !isWatching;
            if (isWatching)
            {
                btnToggleWatch.Text = "⏸️ Jeda Watcher";
                btnToggleWatch.BackColor = warningAmber;
                AddLog("[WATCHER] Pemantau otomatis aktif kembali.", successGreen);
            }
            else
            {
                btnToggleWatch.Text = "▶️ Lanjutkan";
                btnToggleWatch.BackColor = successGreen;
                AddLog("[WATCHER] Pemantau dijeda oleh pengguna.", warningAmber);
            }
        }

        private void LoadCache()
        {
            try
            {
                if (File.Exists(cacheFilePath))
                {
                    string json = File.ReadAllText(cacheFilePath);
                    MatchCollection matches = Regex.Matches(json, @"\{\s*""key"":\s*""([^""]+)"",\s*""aid"":\s*""([^""]+)"",\s*""tid"":\s*""([^""]+)"",\s*""headline"":\s*""([^""]+)""\s*\}");
                    foreach (Match m in matches)
                    {
                        string k = m.Groups[1].Value;
                        studyCache[k] = new StudyModuleItem
                        {
                            Key = k,
                            AcademyId = m.Groups[2].Value,
                            TutorialId = m.Groups[3].Value,
                            Headline = m.Groups[4].Value
                        };
                    }
                }
            }
            catch { }
        }

        private void SaveCache()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"modules\": [");
                int idx = 0;
                foreach (var kvp in studyCache)
                {
                    var item = kvp.Value;
                    string line = string.Format("    {{ \"key\": \"{0}\", \"aid\": \"{1}\", \"tid\": \"{2}\", \"headline\": \"{3}\" }}{4}",
                        item.Key, item.AcademyId, item.TutorialId, CleanFileName(item.Headline), (idx == studyCache.Count - 1 ? "" : ","));
                    sb.AppendLine(line);
                    idx++;
                }
                sb.AppendLine("  ]");
                sb.AppendLine("}");
                File.WriteAllText(cacheFilePath, sb.ToString());
            }
            catch { }
        }

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            Application.ThreadException += (sender, args) => 
            {
                System.IO.File.WriteAllText("crash_log.txt", args.Exception.ToString());
                MessageBox.Show("Crash Terdeteksi:\n" + args.Exception.Message, "Error UI Thread", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (sender, args) => 
            {
                System.IO.File.WriteAllText("crash_log_domain.txt", args.ExceptionObject.ToString());
                MessageBox.Show("Fatal Crash:\n" + args.ExceptionObject.ToString(), "Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                System.IO.File.WriteAllText("crash_log_main.txt", ex.ToString());
                MessageBox.Show("Crash Main:\n" + ex.Message, "Error Main", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
