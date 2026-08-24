using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
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

    [DllImport("kernel32.dll")]
    static extern bool SetProcessWorkingSetSize(IntPtr h, IntPtr min, IntPtr max);
    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();

    // Restituisce al sistema le pagine che il programma non sta piu' usando.
    // Un monitor di memoria che si tiene decine di MB di roba morta e' una
    // barzelletta: con -1,-1 Windows svuota il working set e le pagine che
    // servono davvero rientrano da sole al primo accesso.
    public static void Sgombera() {
      try { SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1)); } catch {}
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

    // Come Classifica, ma aggiunge in fondo una riga che riassume tutto il
    // resto. Serve perche' i primi dodici nomi sono l'ottanta per cento della
    // memoria occupata e gli altri centosedici sono briciole: senza quella
    // riga sembra che il conto non torni.
    public static List<Voce> ClassificaConCoda(int quanti) {
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
      List<Voce> ord = d.Values.OrderByDescending(x => x.MB).ToList();
      List<Voce> testa = ord.Take(quanti).ToList();
      if (ord.Count > quanti) {
        Voce coda = new Voce { Nome = CODA + (ord.Count - quanti) + " nomi)", MB = 0, Quanti = 0 };
        for (int i = quanti; i < ord.Count; i++) { coda.MB += ord[i].MB; coda.Quanti += ord[i].Quanti; }
        testa.Add(coda);
      }
      return testa;
    }

    // La riga di riepilogo si riconosce dal nome: non e' un processo e non si
    // puo' chiudere.
    public const string CODA = "(altri ";
    public static bool ERiepilogo(string nome) {
      return nome != null && nome.StartsWith(CODA, StringComparison.Ordinal);
    }
  }

  // Il pannello Claude dentro Excel, Word e PowerPoint gira in WebView2, cioe'
  // in msedgewebview2.exe, e resta vivo anche a pannello chiuso: sei processi
  // per circa 470 MB misurati il 24/08/2026.
  //
  // Lo stesso eseguibile lo usano anche Widgets, Copilot, Outlook, Teams e
  // Discord. Chiuderli per nome li ammazzerebbe tutti, e per questo qui si
  // legge la RIGA DI COMANDO e si tengono solo i processi che lavorano nel
  // profilo dei componenti aggiuntivi di Office, ...\Office\16.0\Wef\webview2.
  // La versione e' lasciata generica perche' Office 16 non sara' l'ultimo.
  //
  // Non si tocca il registro. In particolare non si usa il trucco IFEO
  // (Debugger=systray.exe sotto Image File Execution Options): quello impedisce
  // del tutto a WebView2 di partire e il 24/08/2026 ha rotto il componente
  // aggiuntivo con "Non e' possibile avviare questo componente aggiuntivo",
  // cioe' CO_E_SERVER_EXEC_FAILURE. Qui si chiudono dei processi, e basta.
  static class Office {
    const string ESE = "msedgewebview2";
    static readonly Regex PROFILO = new Regex(@"Office\\1[0-9]\.0\\Wef\\webview2", RegexOptions.IgnoreCase);

    public class Stato {
      public List<int> Pid = new List<int>();
      public long MB = 0;
      public int Quanti { get { return Pid.Count; } }
    }

    // La lettura della riga di comando passa da WMI e costa qualche centinaio
    // di millisecondi, quindi prima si guarda col metodo economico se esista
    // almeno un msedgewebview2: quasi sempre non ce n'e' nessuno e si esce
    // subito. Non va chiamata a ogni tick.
    public static Stato Cerca() {
      Stato st = new Stato();
      Process[] veloce;
      try { veloce = Process.GetProcessesByName(ESE); } catch { return st; }
      try {
        if (veloce.Length == 0) return st;
      } finally {
        foreach (Process p in veloce) { try { p.Dispose(); } catch {} }
      }

      try {
        using (ManagementObjectSearcher q = new ManagementObjectSearcher(
                 "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = '" + ESE + ".exe'")) {
          foreach (ManagementObject o in q.Get()) {
            using (o) {
              object riga = o["CommandLine"];
              if (riga == null || !PROFILO.IsMatch(riga.ToString())) continue;
              int pid;
              try { pid = Convert.ToInt32(o["ProcessId"]); } catch { continue; }
              st.Pid.Add(pid);
              try {
                using (Process p = Process.GetProcessById(pid)) st.MB += p.WorkingSet64 / 1048576L;
              } catch {}
            }
          }
        }
      } catch {}
      return st;
    }

    public static int Libera(Stato st) {
      int chiusi = 0;
      foreach (int pid in st.Pid) {
        try {
          using (Process p = Process.GetProcessById(pid)) {
            if (!p.HasExited) { p.Kill(); chiusi++; }
          }
        } catch {}
      }
      return chiusi;
    }

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    const int SW_RESTORE = 9;

    // Porta in primo piano la finestra di Office. Restituisce false se non c'e'
    // niente da attivare: chi chiama non deve insistere.
    public static bool InPrimoPiano() {
      string[] nomi = { "EXCEL", "WINWORD", "POWERPNT" };
      foreach (string n in nomi) {
        Process[] tutti;
        try { tutti = Process.GetProcessesByName(n); } catch { continue; }
        try {
          foreach (Process p in tutti) {
            IntPtr h = IntPtr.Zero;
            try { h = p.MainWindowHandle; } catch {}
            if (h == IntPtr.Zero) continue;
            try {
              if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
              SetForegroundWindow(h);
            } catch { continue; }
            return true;
          }
        } finally {
          foreach (Process p in tutti) { try { p.Dispose(); } catch {} }
        }
      }
      return false;
    }

    // La scorciatoia registrata dal componente aggiuntivo. Va mandata quando
    // la finestra di Office ha gia' il fuoco, non prima.
    public static void ScorciatoiaClaude() {
      try { SendKeys.SendWait("^%c"); } catch {}
    }
  }

  class Pannello : Form {
    Label testa;
    ListView lista;
    Button bTermina, bTask, bRinvia, bChiudi;
    Button bLibera, bRiapri;
    Timer riapriRitardato;
    Office.Stato office;
    Timer refresh;
    public DateTime RinviaFino = DateTime.MinValue;
    Timer killRitardato;
    string daUccidere = null;
    bool manuale = false;   // aperto a mano dall'utente: non si chiude da solo

    // Quante righe di processi mettere nella lista. Tredici stanno nel
    // pannello senza scorrere, le altre si raggiungono con la barra: lo
    // scorrimento adesso e' stabile, perche' Riempi() aggiorna le righe sul
    // posto invece di ricostruirle. In fondo resta sempre la riga che riassume
    // tutto quello che non ci sta.
    const int RIGHE = 30;

    protected override bool ShowWithoutActivation { get { return true; } }

    public Pannello() {
      Text = "RAM quasi esaurita";
      FormBorderStyle = FormBorderStyle.FixedToolWindow;
      StartPosition = FormStartPosition.Manual;
      ShowInTaskbar = false;
      TopMost = true;
      ClientSize = new Size(440, 490);
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
      lista.Size = new Size(420, 306);   // dodici righe, la riga di riepilogo e l'intestazione, senza barra
      lista.DoubleClick += delegate { Termina(); };
      Controls.Add(lista);

      bTermina = Bottone("Chiudi questo", 10, 400, 120);
      bTermina.BackColor = Color.FromArgb(150, 35, 35);
      bTermina.Click += delegate { Termina(); };

      bTask = Bottone("Gestione attivita", 136, 400, 120);
      bTask.Click += delegate { try { Process.Start("taskmgr.exe"); } catch {} };

      bRinvia = Bottone("Zitto 30 min", 262, 400, 100);
      bRinvia.Click += delegate { RinviaFino = DateTime.Now.AddMinutes(30); Hide(); };

      bChiudi = Bottone("Chiudi", 368, 400, 62);
      bChiudi.Click += delegate { Hide(); };

      // Seconda riga: i comandi del pannello Claude di Office. Sono due
      // bottoni distinti e non un unico "riavvia" perche' le due cose servono
      // in momenti diversi: la memoria si libera anche con Office chiuso, e il
      // pannello si riapre anche senza aver liberato niente. Un comando solo
      // avrebbe anche nascosto quale meta' e' fallita, e la seconda meta'
      // dipende da SetForegroundWindow, che Windows puo' sempre rifiutare.
      bLibera = Bottone("Libera Claude Office", 10, 440, 205);
      bLibera.Click += delegate { ComandoLibera(); };

      bRiapri = Bottone("Riapri Claude in Excel", 225, 440, 205);
      bRiapri.Click += delegate { ComandoRiapri(); };

      // Sui bottoni ci sta poco testo, e da solo non basta a far capire cosa
      // fanno: la spiegazione lunga sta nel suggerimento del mouse.
      ToolTip sugg = new ToolTip();
      sugg.AutoPopDelay = 15000;
      sugg.InitialDelay = 400;
      sugg.SetToolTip(bLibera,
        "Chiude i processi del pannello Claude dentro Excel, Word e PowerPoint," + Environment.NewLine +
        "che restano in memoria anche quando il pannello e' chiuso." + Environment.NewLine +
        "Non tocca Widgets, Copilot, Outlook, Teams e Discord: usano lo stesso" + Environment.NewLine +
        "motore ma un profilo diverso.");
      sugg.SetToolTip(bRiapri,
        "Porta Excel (o Word, o PowerPoint) in primo piano e manda Ctrl+Alt+C," + Environment.NewLine +
        "la scorciatoia che riapre il pannello Claude. Da usare dopo aver" + Environment.NewLine +
        "liberato la memoria, per farlo ripartire pulito.");

      refresh = new Timer();
      refresh.Interval = 2000;
      refresh.Tick += delegate { Ridisegna(); };

      // Chiuso in qualunque modo, la prossima apertura riparte da zero.
      VisibleChanged += delegate {
        if (!Visible) { refresh.Stop(); Mem.Sgombera(); }
      };

      // Fra l'attivazione della finestra di Office e la scorciatoia serve un
      // attimo, altrimenti i tasti arrivano a una finestra che non ha ancora
      // il fuoco. Si aspetta con un timer, mai con uno Sleep: sarebbe il
      // thread dell'interfaccia a fermarsi.
      riapriRitardato = new Timer();
      riapriRitardato.Interval = 400;
      riapriRitardato.Tick += delegate {
        riapriRitardato.Stop();
        Office.ScorciatoiaClaude();
      };

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
      Text = aMano ? "Memoria - stato attuale" : "RAM quasi esaurita";
      Rectangle wa = Screen.PrimaryScreen.WorkingArea;
      Location = new Point(wa.Right - Width - 16, wa.Bottom - Height - 16);
      Aggiorna(pct, liberiMB, crollo);
      AggiornaOffice();
      if (!Visible) Show();
      TopMost = true;
      BringToFront();
      // Il modo si assegna DOPO Show(): creando l'handle della finestra
      // WinForms fa scattare VisibleChanged, e assegnandolo prima il flag
      // veniva azzerato li' dentro. Risultato: il pannello aperto a mano si
      // richiudeva lo stesso al primo tick, che e' il difetto che si voleva
      // togliere. Non spostare questa riga piu' in alto.
      manuale = aMano;
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
      if (Mem.ERiepilogo(v.Nome)) return Color.FromArgb(120, 120, 126);
      if (Mem.Protetti.Contains(v.Nome)) return Color.FromArgb(130, 130, 136);
      if (v.MB >= 1500) return Color.FromArgb(255, 120, 120);
      if (v.MB >= 700)  return Color.FromArgb(255, 190, 110);
      return Color.White;
    }

    // Costa qualche centinaio di millisecondi perche' passa da WMI, quindi si
    // chiama quando il pannello si apre e dopo un comando, non a ogni tick.
    void AggiornaOffice() {
      office = Office.Cerca();
      if (office.Quanti == 0) {
        bLibera.Enabled = false;
        bLibera.Text = "Claude Office: 0 MB";
      } else {
        bLibera.Enabled = true;
        bLibera.Text = "Libera Claude Office: " + office.MB + " MB";
      }
      bRiapri.Text = "Riapri Claude in Excel";
    }

    public void ComandoLibera() {
      AggiornaOffice();
      if (office.Quanti == 0) return;
      long prima = office.MB;
      int quanti = office.Quanti;
      Office.Libera(office);
      Mem.Sgombera();
      AggiornaOffice();
      // Il risultato si legge sul bottone: niente finestrelle di conferma,
      // il pannello compare quando la macchina sta gia' soffrendo.
      bLibera.Text = "Liberati " + prima + " MB";
      if (Visible) Riempi();
    }

    public void ComandoRiapri() {
      if (!Office.InPrimoPiano()) { bRiapri.Text = "Excel non e' aperto"; return; }
      bRiapri.Text = "Riapri Claude in Excel";
      riapriRitardato.Start();
    }

    // Le tre colonne numeriche di una riga: megabyte, gigabyte, quota sul
    // totale della RAM installata.
    static string[] Numeri(Voce v, long totaleMB) {
      string gb = (v.MB / 1024.0).ToString("0.00");
      string pc = (totaleMB > 0) ? (v.MB * 100.0 / totaleMB).ToString("0.0") : "-";
      return new string[] { v.MB.ToString(), gb, pc, v.Quanti.ToString() };
    }

    void Riempi() {
      List<Voce> voci = Mem.ClassificaConCoda(RIGHE);
      long totaleMB = (long)(Mem.Stato().ullTotalPhys / 1048576L);

      string sel = (lista.SelectedItems.Count > 0) ? lista.SelectedItems[0].Text : null;

      // La lista si SVUOTA il meno possibile: ricostruirla riporta la barra di
      // scorrimento in cima, ed e' il difetto che si e' gia' pagato due volte.
      // Il numero di righe e' fisso (RIGHE piu' il riepilogo), quindi quasi
      // sempre basta riscrivere il contenuto delle righe che ci sono gia',
      // nomi compresi: cosi' la barra non si muove nemmeno quando la
      // classifica si riordina, cosa che con trenta processi succede in
      // continuazione perche' gli ultimi si scambiano di posto per un MB.
      lista.BeginUpdate();
      if (lista.Items.Count != voci.Count) {
        lista.Items.Clear();
        foreach (Voce v in voci) {
          ListViewItem it = new ListViewItem(v.Nome);
          foreach (string n in Numeri(v, totaleMB)) it.SubItems.Add(n);
          it.ForeColor = Colore(v);
          lista.Items.Add(it);
        }
      } else {
        for (int i = 0; i < voci.Count; i++) {
          ListViewItem it = lista.Items[i];
          if (it.Text != voci[i].Nome) it.Text = voci[i].Nome;
          string[] n = Numeri(voci[i], totaleMB);
          for (int c = 0; c < n.Length; c++) {
            if (it.SubItems[c + 1].Text != n[c]) it.SubItems[c + 1].Text = n[c];
          }
          it.ForeColor = Colore(voci[i]);
        }
      }
      lista.EndUpdate();

      // La selezione segue il NOME, non la posizione: se il processo scelto e'
      // sceso di due righe la riga evidenziata deve scendere con lui, altrimenti
      // "Chiudi questo" chiuderebbe quello che nel frattempo gli e' finito
      // sotto il cursore. Se e' sparito, non resta selezionato niente.
      bool giusta = (lista.SelectedItems.Count > 0) && (sel != null) && (lista.SelectedItems[0].Text == sel);
      if (!giusta) {
        try { lista.SelectedIndices.Clear(); } catch {}
        if (sel != null) {
          foreach (ListViewItem it in lista.Items) {
            if (it.Text == sel) { it.Selected = true; break; }
          }
        }
      }
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
      if (Mem.ERiepilogo(nome)) return;   // e' il riassunto della coda, non un processo
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
      AllineaAvvio();

      ContextMenuStrip m = new ContextMenuStrip();
      m.Items.Add("Mostra pannello adesso", null, delegate { ApriPannello(true); });
      m.Items.Add("Apri Gestione attivita", null, delegate { try { Process.Start("taskmgr.exe"); } catch {} });
      m.Items.Add(new ToolStripSeparator());
      m.Items.Add("Libera Claude Office (WebView2)", null, delegate { pannello.ComandoLibera(); });
      m.Items.Add("Riapri pannello Claude in Office", null, delegate { pannello.ComandoRiapri(); });
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

    // A ogni avvio il programma controlla di essere ancora nell'avvio
    // automatico di Windows, e ci si rimette se la voce manca o se punta a un
    // eseguibile che non esiste piu'. Serve perche' spostando la cartella il
    // registro restava a puntare al percorso vecchio e al riavvio del PC il
    // monitor non ripartiva, senza che niente lo segnalasse.
    //
    // Quello che NON deve fare e' riscrivere una voce che funziona: una copia
    // di prova lanciata da un'altra cartella si prenderebbe l'avvio automatico
    // al posto dell'installazione vera. E' successo davvero.
    void AllineaAvvio() {
      try {
        string mio = "\"" + Application.ExecutablePath + "\"";
        RegistryKey k = Registry.CurrentUser.OpenSubKey(RUNKEY, true);
        if (k == null) return;
        object v = k.GetValue("ClaudeRamTray");
        string attuale = (v == null) ? null : v.ToString().Trim();
        bool daRifare = string.IsNullOrEmpty(attuale);
        if (!daRifare && attuale != mio) {
          string percorso = attuale.Trim('"');
          daRifare = !File.Exists(percorso);   // punta a qualcosa che non c'e' piu'
        }
        if (daRifare) k.SetValue("ClaudeRamTray", mio);
        k.Close();
      } catch {}
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
      // Ogni cinque minuti, e sempre subito dopo l'avvio, il programma
      // restituisce a Windows la memoria che ha smesso di usare.
      if (contatore == 3 || contatore % 150 == 0) Mem.Sgombera();
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
