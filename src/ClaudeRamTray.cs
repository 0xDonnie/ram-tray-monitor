using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ClaudeRamTray {

  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
  struct MEMORYSTATUSEX {
    public uint dwLength;
    public uint dwMemoryLoad;
    public ulong ullTotalPhys;
    public ulong ullAvailPhys;
    public ulong ullTotalPageFile;
    public ulong ullAvailPageFile;
    public ulong ullTotalVirtual;
    public ulong ullAvailVirtual;
    public ulong ullAvailExtendedVirtual;
  }

  class Voce {
    public string Nome;
    public long MB;
    public int Quanti;
  }

  static class Mem {
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX b);

    public static MEMORYSTATUSEX Stato() {
      MEMORYSTATUSEX s = new MEMORYSTATUSEX();
      s.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
      GlobalMemoryStatusEx(ref s);
      return s;
    }

    public static readonly HashSet<string> Protetti = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
      "System","Idle","Registry","smss","csrss","wininit","winlogon","services","lsass",
      "fontdrvhost","dwm","Memory Compression","MemCompression","svchost","LsaIso",
      "SecurityHealthService","MsMpEng","ClaudeRamTray","WUDFHost","audiodg","conhost"
    };

    public static List<Voce> Classifica(int quanti) {
      Dictionary<string, Voce> d = new Dictionary<string, Voce>(StringComparer.OrdinalIgnoreCase);
      Process[] tutti = null;
      try { tutti = Process.GetProcesses(); } catch { return new List<Voce>(); }
      foreach (Process p in tutti) {
        try {
          string n = p.ProcessName;
          long ws = p.WorkingSet64;
          Voce v;
          if (d.TryGetValue(n, out v)) { v.MB += ws / 1048576L; v.Quanti++; }
          else d[n] = new Voce { Nome = n, MB = ws / 1048576L, Quanti = 1 };
        } catch {}
        finally { try { p.Dispose(); } catch {} }
      }
      return d.Values.OrderByDescending(x => x.MB).Take(quanti).ToList();
    }
  }

  class Pannello : Form {
    Label testa;
    ListView lista;
    Button bTermina, bTask, bRinvia, bChiudi;
    Timer refresh;
    public DateTime RinviaFino = DateTime.MinValue;
    Timer killRitardato;
    string daUccidere = null;
    bool manuale = false;   // aperto a mano dall'utente: non si chiude da solo

    protected override bool ShowWithoutActivation { get { return true; } }

    public Pannello() {
      Text = "RAM quasi esaurita";
      FormBorderStyle = FormBorderStyle.FixedToolWindow;
      StartPosition = FormStartPosition.Manual;
      ShowInTaskbar = false;
      TopMost = true;
      ClientSize = new Size(440, 380);
      BackColor = Color.FromArgb(28, 28, 30);
      ForeColor = Color.White;
      Font = new Font("Segoe UI", 9f);

      testa = new Label();
      testa.Dock = DockStyle.Top;
      testa.Height = 74;
      testa.TextAlign = ContentAlignment.MiddleCenter;
      testa.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
      testa.BackColor = Color.FromArgb(180, 30, 30);
      testa.ForeColor = Color.White;
      Controls.Add(testa);

      lista = new ListView();
      lista.View = View.Details;
      lista.FullRowSelect = true;
      lista.MultiSelect = false;
      lista.HideSelection = false;
      lista.GridLines = false;
      lista.BackColor = Color.FromArgb(38, 38, 42);
      lista.ForeColor = Color.White;
      lista.BorderStyle = BorderStyle.None;
      lista.Columns.Add("Processo", 160);
      lista.Columns.Add("MB", 62, HorizontalAlignment.Right);
      lista.Columns.Add("GB", 58, HorizontalAlignment.Right);
      lista.Columns.Add("% RAM", 52, HorizontalAlignment.Right);
      lista.Columns.Add("Processi", 68, HorizontalAlignment.Right);
      lista.Location = new Point(10, 84);
      lista.Size = new Size(420, 240);
      lista.DoubleClick += delegate { Termina(); };
      Controls.Add(lista);

      bTermina = Bottone("Chiudi questo", 10, 334, 120);
      bTermina.BackColor = Color.FromArgb(150, 35, 35);
      bTermina.Click += delegate { Termina(); };

      bTask = Bottone("Gestione attivita", 136, 334, 120);
      bTask.Click += delegate { try { Process.Start("taskmgr.exe"); } catch {} };

      bRinvia = Bottone("Zitto 30 min", 262, 334, 100);
      bRinvia.Click += delegate { RinviaFino = DateTime.Now.AddMinutes(30); Hide(); };

      bChiudi = Bottone("Chiudi", 368, 334, 62);
      bChiudi.Click += delegate { Hide(); };

      refresh = new Timer();
      refresh.Interval = 2000;
      refresh.Tick += delegate { Ridisegna(); };

      // Chiuso in qualunque modo, la prossima apertura riparte da zero.
      VisibleChanged += delegate { if (!Visible) { manuale = false; refresh.Stop(); } };

      killRitardato = new Timer();
      killRitardato.Interval = 3000;
      killRitardato.Tick += delegate {
        killRitardato.Stop();
        if (daUccidere == null) return;
        foreach (Process p in Process.GetProcessesByName(daUccidere)) {
          try { if (!p.HasExited) p.Kill(); } catch {} finally { try { p.Dispose(); } catch {} }
        }
        daUccidere = null;
        Ridisegna();
      };

      FormClosing += delegate(object o, FormClosingEventArgs e) {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
      };
    }

    Button Bottone(string t, int x, int y, int w) {
      Button b = new Button();
      b.Text = t;
      b.Location = new Point(x, y);
      b.Size = new Size(w, 32);
      b.FlatStyle = FlatStyle.Flat;
      b.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 86);
      b.BackColor = Color.FromArgb(55, 55, 60);
      b.ForeColor = Color.White;
      Controls.Add(b);
      return b;
    }

    public void Apri(int pct, long liberiMB, long crollo) { Apri(pct, liberiMB, crollo, false); }

    public void Apri(int pct, long liberiMB, long crollo, bool aMano) {
      manuale = aMano;
      Text = aMano ? "Memoria - stato attuale" : "RAM quasi esaurita";
      Rectangle wa = Screen.PrimaryScreen.WorkingArea;
      Location = new Point(wa.Right - Width - 16, wa.Bottom - Height - 16);
      Aggiorna(pct, liberiMB, crollo);
      if (!Visible) Show();
      TopMost = true;
      BringToFront();
      refresh.Start();
    }

    void Aggiorna(int pct, long liberiMB, long crollo) {
      string t = "RAM al " + pct + "%   -   " + liberiMB + " MB liberi";
      if (crollo >= 300)        t += "\n" + crollo + " MB spariti negli ultimi secondi";
      else if (liberiMB > 1500) t += "\nSituazione normale";
      else                      t += "\nChiudi qualcosa prima che si pianti";
      testa.Text = t;
      if (liberiMB < 600)       testa.BackColor = Color.FromArgb(180, 30, 30);
      else if (liberiMB <= 1500) testa.BackColor = Color.FromArgb(190, 115, 0);
      else                      testa.BackColor = Color.FromArgb(45, 90, 135);
      Riempi();
    }

    static Color Colore(Voce v) {
      if (Mem.Protetti.Contains(v.Nome)) return Color.FromArgb(130, 130, 136);
      if (v.MB >= 1500) return Color.FromArgb(255, 120, 120);
      if (v.MB >= 700)  return Color.FromArgb(255, 190, 110);
      return Color.White;
    }

    // Le tre colonne numeriche di una riga: megabyte, gigabyte, quota sul
    // totale della RAM installata.
    static string[] Numeri(Voce v, long totaleMB) {
      string gb = (v.MB / 1024.0).ToString("0.00");
      string pc = (totaleMB > 0) ? (v.MB * 100.0 / totaleMB).ToString("0.0") : "-";
      return new string[] { v.MB.ToString(), gb, pc, v.Quanti.ToString() };
    }

    void Riempi() {
      List<Voce> voci = Mem.Classifica(12);
      long totaleMB = (long)(Mem.Stato().ullTotalPhys / 1048576L);

      // Svuotare e ricostruire la lista a ogni giro riportava lo scorrimento
      // in cima dopo un secondo. Se i nomi sono gli stessi e nello stesso
      // ordine si aggiornano i valori sul posto e la barra non si muove.
      bool stessoOrdine = (lista.Items.Count == voci.Count);
      if (stessoOrdine) {
        for (int i = 0; i < voci.Count; i++) {
          if (lista.Items[i].Text != voci[i].Nome) { stessoOrdine = false; break; }
        }
      }

      lista.BeginUpdate();
      if (stessoOrdine) {
        for (int i = 0; i < voci.Count; i++) {
          ListViewItem it = lista.Items[i];
          string[] n = Numeri(voci[i], totaleMB);
          for (int c = 0; c < n.Length; c++) {
            if (it.SubItems[c + 1].Text != n[c]) it.SubItems[c + 1].Text = n[c];
          }
          it.ForeColor = Colore(voci[i]);
        }
      } else {
        string sel = (lista.SelectedItems.Count > 0) ? lista.SelectedItems[0].Text : null;
        string cima = null;
        try { if (lista.TopItem != null) cima = lista.TopItem.Text; } catch {}
        lista.Items.Clear();
        foreach (Voce v in voci) {
          ListViewItem it = new ListViewItem(v.Nome);
          foreach (string s in Numeri(v, totaleMB)) it.SubItems.Add(s);
          it.ForeColor = Colore(v);
          lista.Items.Add(it);
          if (sel != null && v.Nome == sel) it.Selected = true;
        }
        // Anche quando la classifica cambia, si prova a rimettere lo
        // scorrimento sulla riga che stava in cima.
        if (cima != null) {
          foreach (ListViewItem it in lista.Items) {
            if (it.Text == cima) { try { lista.TopItem = it; } catch {} break; }
          }
        }
      }
      lista.EndUpdate();
    }

    void Ridisegna() {
      MEMORYSTATUSEX s = Mem.Stato();
      long liberi = (long)(s.ullAvailPhys / 1048576L);
      Aggiorna((int)s.dwMemoryLoad, liberi, 0);
      // Il rientro automatico vale solo per il pannello aperto dall'allarme.
      // Se l'ha aperto l'utente resta li' finche' non lo chiude lui.
      if (liberi > 1500 && !manuale) { refresh.Stop(); Hide(); }
    }

    void Termina() {
      if (lista.SelectedItems.Count == 0) return;
      string nome = lista.SelectedItems[0].Text;
      string mb = lista.SelectedItems[0].SubItems[1].Text;
      if (Mem.Protetti.Contains(nome)) {
        MessageBox.Show(this, "'" + nome + "' e un componente di Windows, non si chiude da qui.",
                        "Non si tocca", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;
      }
      DialogResult r = MessageBox.Show(this,
          "Chiudere tutti i processi '" + nome + "' (" + mb + " MB)?\n\nEventuali dati non salvati vanno persi.",
          "Conferma", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
      if (r != DialogResult.Yes) return;

      bool qualcunoConFinestra = false;
      foreach (Process p in Process.GetProcessesByName(nome)) {
        try {
          if (p.MainWindowHandle != IntPtr.Zero) { p.CloseMainWindow(); qualcunoConFinestra = true; }
          else p.Kill();
        } catch {}
        finally { try { p.Dispose(); } catch {} }
      }
      if (qualcunoConFinestra) { daUccidere = nome; killRitardato.Start(); }
      Riempi();
    }
  }

  class TrayApp : ApplicationContext {
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool DestroyIcon(IntPtr h);

    const int SOGLIA_ARANCIONE   = 80;
    const int SOGLIA_ROSSA       = 90;
    const int SOGLIA_ALLARME     = 95;
    const long LIBERI_CRITICI_MB = 600;
    const long CROLLO_MB         = 800;

    NotifyIcon ni;
    Timer tick;
    Icon iconaVecchia;
    Pannello pannello;
    int size;
    int contatore = 0;
    string topCache = "";
    long liberiPrec = -1;
    DateTime ultimoAllarme = DateTime.MinValue;
    string csvAllarmi;
    const string RUNKEY = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public TrayApp() {
      size = SystemInformation.SmallIconSize.Width;
      if (size < 16) size = 16;
      if (size > 32) size = 32;

      string down = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
      if (!Directory.Exists(down)) down = Path.GetTempPath();
      csvAllarmi = Path.Combine(down, "claude_ram_allarmi.csv");

      pannello = new Pannello();

      ContextMenuStrip m = new ContextMenuStrip();
      m.Items.Add("Mostra pannello adesso", null, delegate { ApriPannello(true); });
      m.Items.Add("Apri Gestione attivita", null, delegate { try { Process.Start("taskmgr.exe"); } catch {} });
      m.Items.Add(new ToolStripSeparator());
      ToolStripMenuItem avvio = new ToolStripMenuItem("Avvia con Windows");
      avvio.Checked = InAvvio();
      avvio.Click += delegate {
        if (InAvvio()) { ImpostaAvvio(false); avvio.Checked = false; }
        else           { ImpostaAvvio(true);  avvio.Checked = true;  }
      };
      m.Items.Add(avvio);
      m.Items.Add(new ToolStripSeparator());
      m.Items.Add("Esci", null, delegate { Chiudi(); });

      ni = new NotifyIcon();
      ni.ContextMenuStrip = m;
      ni.Visible = true;
      ni.DoubleClick += delegate { ApriPannello(true); };

      tick = new Timer();
      tick.Interval = 2000;
      tick.Tick += delegate { Aggiorna(); };
      tick.Start();
      Aggiorna();
    }

    void ApriPannello(bool forzato) {
      MEMORYSTATUSEX s = Mem.Stato();
      pannello.Apri((int)s.dwMemoryLoad, (long)(s.ullAvailPhys / 1048576L), 0, forzato);
    }

    bool InAvvio() {
      try {
        RegistryKey k = Registry.CurrentUser.OpenSubKey(RUNKEY, false);
        if (k == null) return false;
        object v = k.GetValue("ClaudeRamTray");
        k.Close();
        return v != null;
      } catch { return false; }
    }

    void ImpostaAvvio(bool si) {
      try {
        RegistryKey k = Registry.CurrentUser.OpenSubKey(RUNKEY, true);
        if (k == null) return;
        if (si) k.SetValue("ClaudeRamTray", "\"" + Application.ExecutablePath + "\"");
        else    k.DeleteValue("ClaudeRamTray", false);
        k.Close();
      } catch {}
    }

    void Aggiorna() {
      MEMORYSTATUSEX s = Mem.Stato();
      int pct = (int)s.dwMemoryLoad;
      long liberiMB = (long)(s.ullAvailPhys / 1048576L);
      double liberiGB = s.ullAvailPhys / 1073741824.0;
      double totGB = s.ullTotalPhys / 1073741824.0;

      long crollo = (liberiPrec > 0) ? (liberiPrec - liberiMB) : 0;
      liberiPrec = liberiMB;

      contatore++;
      if (contatore % 10 == 1) {
        StringBuilder sb = new StringBuilder();
        foreach (Voce v in Mem.Classifica(3)) sb.AppendLine(v.Nome + "  " + v.MB + " MB");
        topCache = sb.ToString().TrimEnd();
      }

      Color bg;
      if (pct >= SOGLIA_ROSSA || liberiMB < LIBERI_CRITICI_MB) bg = Color.FromArgb(200, 30, 30);
      else if (pct >= SOGLIA_ARANCIONE)                        bg = Color.FromArgb(205, 120, 0);
      else                                                     bg = Color.FromArgb(35, 95, 45);
      DisegnaIcona(pct, bg);

      string tip = "RAM " + pct + "% usata\n" + liberiGB.ToString("0.00") + " GB liberi su " + totGB.ToString("0.0") + " GB";
      if (topCache.Length > 0) tip += "\n" + topCache;
      try { ni.Text = tip.Length > 120 ? tip.Substring(0, 120) : tip; }
      catch { try { ni.Text = "RAM " + pct + "%"; } catch {} }

      bool rischio = (pct >= SOGLIA_ALLARME) || (liberiMB < LIBERI_CRITICI_MB) || (crollo >= CROLLO_MB);
      bool zitto = DateTime.Now < pannello.RinviaFino;

      if (rischio && !zitto && (DateTime.Now - ultimoAllarme).TotalSeconds > 60) {
        ultimoAllarme = DateTime.Now;
        pannello.Apri(pct, liberiMB, crollo);
        try {
          List<Voce> top = Mem.Classifica(3);
          StringBuilder sb = new StringBuilder();
          foreach (Voce v in top) { if (sb.Length > 0) sb.Append(" | "); sb.Append(v.Nome + " " + v.MB + " MB"); }
          bool nuovo = !File.Exists(csvAllarmi);
          using (StreamWriter w = new StreamWriter(csvAllarmi, true, Encoding.UTF8)) {
            if (nuovo) w.WriteLine("Ora,PercentualeUsata,LiberiMB,CrolloMB,Top3");
            w.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "," + pct + "," + liberiMB + "," + crollo +
                        ",\"" + sb.ToString().Replace("\"", "'") + "\"");
          }
        } catch {}
      }
    }

    void DisegnaIcona(int pct, Color bg) {
      string t = pct >= 100 ? "99" : pct.ToString();
      Bitmap bmp = new Bitmap(size, size);
      using (Graphics g = Graphics.FromImage(bmp)) {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        g.Clear(Color.Transparent);
        using (SolidBrush b = new SolidBrush(bg)) g.FillRectangle(b, 0, 0, size, size);
        StringFormat sf = StringFormat.GenericTypographic;
        float fs = size * 0.85f;
        Font f = null;
        while (true) {
          if (f != null) f.Dispose();
          f = new Font("Segoe UI", fs, FontStyle.Bold, GraphicsUnit.Pixel);
          SizeF ms = g.MeasureString(t, f, new PointF(0, 0), sf);
          if ((ms.Width <= size - 1 && ms.Height <= size) || fs <= 6f) break;
          fs -= 0.5f;
        }
        SizeF sz = g.MeasureString(t, f, new PointF(0, 0), sf);
        g.DrawString(t, f, Brushes.White, (size - sz.Width) / 2f, (size - sz.Height) / 2f, sf);
        f.Dispose();
      }
      IntPtr h = bmp.GetHicon();
      Icon nuova;
      using (Icon tmp = Icon.FromHandle(h)) { nuova = (Icon)tmp.Clone(); }
      DestroyIcon(h);
      bmp.Dispose();
      ni.Icon = nuova;
      if (iconaVecchia != null) iconaVecchia.Dispose();
      iconaVecchia = nuova;
    }

    void Chiudi() {
      tick.Stop();
      ni.Visible = false;
      ni.Dispose();
      try { pannello.Dispose(); } catch {}
      if (iconaVecchia != null) iconaVecchia.Dispose();
      ExitThread();
    }
  }

  static class Program {
    [STAThread]
    static void Main() {
      bool nuovo;
      using (System.Threading.Mutex mx = new System.Threading.Mutex(true, "ClaudeRamTray_singola", out nuovo)) {
        if (!nuovo) return;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayApp());
      }
    }
  }
}
