using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MazdaLink.Core;
using MazdaLink.J2534;

namespace MazdaLink;

internal sealed class MainWindow:Form
{
    private readonly string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"MazdaLink");
    private readonly DesktopFiles files;
    private readonly ComboBox drivers=new(){DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill,Name="Adapter"};
    private readonly Button refresh=Button("Refresh drivers","RefreshDrivers"),read=Button("Read ECU & save backup","ReadEcu",true),
        export=Button("Save a .bin copy…","SaveBin"),choose=Button("Choose original backup…","ChooseBackup"),
        load=Button("Load edited .bin & check…","LoadEdited",true),write=Button("Write prepared BIN to spare","WritePrepared",true),
        recover=Button("Restore original — bench spare","RecoverOriginal"),stop=Button("Stop operation","Stop");
    private readonly TextBox original=PathBox("OriginalBackup"),edited=PathBox("EditedBin");
    private readonly Label readInfo=Label("No backup yet. Reading saves two matching captures and keeps the complete original backup locally.","ReadStatus"),
        preparedInfo=Label("Choose your original backup, then load the file you edited externally.","PreparedStatus"),
        status=Label("Ready","Status");
    private readonly RichTextBox log=new(){ReadOnly=true,Dock=DockStyle.Fill,BackColor=Color.FromArgb(20,29,42),ForeColor=Color.FromArgb(219,230,242),Font=new Font("Consolas",9),BorderStyle=BorderStyle.None,Name="Activity"};
    private readonly ProgressBar progress=new(){Dock=DockStyle.Fill,Name="Progress"};
    private readonly TabControl tabs=new(){Dock=DockStyle.Fill,Padding=new Point(20,10),Name="Sections"};
    private readonly System.Windows.Forms.Timer timer=new(){Interval=1000};
    private CancellationTokenSource? cancellation;
    private J2534Driver? activeDriver;
    private bool busy,destructive;
    private Stopwatch elapsed=new();
    private readonly bool smoke;
    public MainWindow(bool smoke=false)
    {
        SuspendLayout();AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
        this.smoke=smoke;files=new(root);
        Text="MazdaLink — Mazda 2 DE";Name="MazdaLink";ClientSize=new Size(1120,850);MinimumSize=new Size(900,680);
        Font=new Font("Segoe UI",10);BackColor=Color.FromArgb(242,245,249);AutoScaleMode=AutoScaleMode.Dpi;
        StartPosition=FormStartPosition.CenterScreen;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(20),BackColor=BackColor};
        foreach(float h in new[]{80f,58f,0f,26f,130f,46f})layout.RowStyles.Add(new(h==0?SizeType.Percent:SizeType.Absolute,h==0?100:h));
        var header=new Panel{Dock=DockStyle.Fill,BackColor=Color.FromArgb(21,43,68),Padding=new Padding(16,5,16,5)};
        var title=Label("MazdaLink","Title");title.Font=new Font("Segoe UI",23,FontStyle.Bold);title.ForeColor=Color.White;title.Dock=DockStyle.Top;title.Height=40;
        var sub=Label("Mazda 2 DE  •  Read your ECU, edit externally, write a prepared file","Subtitle");sub.ForeColor=Color.FromArgb(192,213,235);sub.Dock=DockStyle.Bottom;sub.Height=23;
        header.Controls.Add(title);header.Controls.Add(sub);layout.Controls.Add(header,0,0);
        var adapter=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,Padding=new Padding(0,8,0,4)};
        adapter.ColumnStyles.Add(new(SizeType.Absolute,100));adapter.ColumnStyles.Add(new(SizeType.Percent,100));adapter.ColumnStyles.Add(new(SizeType.Absolute,150));
        adapter.Controls.Add(Label("Adapter","AdapterLabel"),0,0);adapter.Controls.Add(drivers,1,0);adapter.Controls.Add(refresh,2,0);layout.Controls.Add(adapter,0,1);
        tabs.TabPages.Add(Page("Read & save",ReadPage()));tabs.TabPages.Add(Page("Write edited BIN",WritePage()));layout.Controls.Add(tabs,0,2);
        layout.Controls.Add(progress,0,3);
        var activity=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1,Padding=new Padding(0,8,0,0)};
        activity.RowStyles.Add(new(SizeType.Absolute,25));activity.RowStyles.Add(new(SizeType.Percent,100));activity.Controls.Add(Label("Activity","ActivityTitle"),0,0);activity.Controls.Add(log,0,1);layout.Controls.Add(activity,0,4);
        var footer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};footer.ColumnStyles.Add(new(SizeType.Percent,100));footer.ColumnStyles.Add(new(SizeType.Absolute,150));footer.Controls.Add(status,0,0);footer.Controls.Add(stop,1,0);layout.Controls.Add(footer,0,5);
        Controls.Add(layout);
        refresh.Click+=(_,_)=>RefreshDrivers();read.Click+=async(_,_)=>await ReadAsync();export.Click+=(_,_)=>Export();choose.Click+=(_,_)=>ChooseBackup();
        load.Click+=async(_,_)=>await LoadEditedAsync();write.Click+=async(_,_)=>await FlashAsync(false);recover.Click+=async(_,_)=>await FlashAsync(true);
        drivers.SelectedIndexChanged+=(_,_)=>UpdateActions();stop.Click+=(_,_)=>Stop();
        timer.Tick+=(_,_)=>{if(busy)status.Text=$"{(destructive?"Bench write / recovery":"Working")}  •  {elapsed.Elapsed:mm\\:ss}";};
        Shown+=(_,_)=>{if(!smoke)RefreshDrivers();};
        FormClosing+=(_,e)=>{if(busy){e.Cancel=true;Stop();}};
        FormClosed+=(_,_)=>{timer.Dispose();cancellation?.Dispose();};
        UpdateActions();ResumeLayout(true);
    }
    private Control ReadPage()
    {
        var panel=Grid(5,36,48,48,64,56);
        var title=Label("Read and keep an original backup","ReadHeading");title.Font=new Font(Font.FontFamily,16,FontStyle.Bold);panel.Controls.Add(title);
        panel.Controls.Add(Label("Connect your Mongoose, switch ignition on and keep the engine off. Save the BIN copy for editing in your preferred tuning tool.","ReadDescription"));
        var actions=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};actions.ColumnStyles.Add(new(SizeType.Percent,55));actions.ColumnStyles.Add(new(SizeType.Percent,45));actions.Controls.Add(read,0,0);actions.Controls.Add(export,1,0);
        panel.Controls.Add(actions);panel.Controls.Add(readInfo);
        panel.Controls.Add(Label("The complete backup stays in Documents\\MazdaLink\\backups. Keep this folder intact; the BIN copy is your external working file.","BackupHint"));
        return panel;
    }
    private Control WritePage()
    {
        var host=new Panel{Dock=DockStyle.Fill};
        var body=new Panel{Dock=DockStyle.Fill,AutoScroll=true};
        var panel=Grid(5,32,44,60,60,70);panel.Padding=new Padding(12);body.Controls.Add(panel);
        var title=Label("Load your edited file and write it back","WriteHeading");title.Font=new Font(Font.FontFamily,16,FontStyle.Bold);panel.Controls.Add(title);
        var scope=Label("Experimental: detached ZYJ2 / ZYC3 bench spare only. Keep the good ECU disconnected. In-car and ZY87 writing are unavailable.","WriteScope");scope.ForeColor=Color.FromArgb(148,72,13);panel.Controls.Add(scope);
        panel.Controls.Add(FileRow("Original backup",original,choose));panel.Controls.Add(FileRow("Externally edited BIN",edited,load));
        panel.Controls.Add(preparedInfo);
        var actions=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};actions.ColumnStyles.Add(new(SizeType.Percent,55));actions.ColumnStyles.Add(new(SizeType.Percent,45));actions.Controls.Add(write,0,0);actions.Controls.Add(recover,1,0);actions.Dock=DockStyle.Bottom;actions.Height=52;actions.Padding=new Padding(12,0,12,6);
        host.Controls.Add(body);host.Controls.Add(actions);return host;
    }
    private static TableLayoutPanel Grid(int rows,params float[] heights)
    {
        var p=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,RowCount=rows,Padding=new Padding(18)};
        foreach(float h in heights)p.RowStyles.Add(new(SizeType.Absolute,h));return p;
    }
    private static TabPage Page(string text,Control child){var page=new TabPage(text){BackColor=Color.White,Padding=new Padding(4),AutoScroll=true};page.Controls.Add(child);return page;}
    private static Control FileRow(string text,TextBox path,Button button)
    {
        var row=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=2};row.ColumnStyles.Add(new(SizeType.Percent,100));row.ColumnStyles.Add(new(SizeType.Absolute,265));row.RowStyles.Add(new(SizeType.Absolute,22));row.RowStyles.Add(new(SizeType.Percent,100));
        row.Controls.Add(Label(text,text.Replace(" ","",StringComparison.Ordinal)+"Label"),0,0);row.SetColumnSpan(row.GetControlFromPosition(0,0)!,2);row.Controls.Add(path,0,1);row.Controls.Add(button,1,1);return row;
    }
    private static TextBox PathBox(string name)=>new(){Name=name,ReadOnly=true,Dock=DockStyle.Fill,PlaceholderText="No file selected"};
    private static Label Label(string text,string name)=>new(){Text=text,Name=name,Dock=DockStyle.Fill,AutoEllipsis=true,TextAlign=ContentAlignment.MiddleLeft};
    private static Button Button(string text,string name,bool primary=false)=>new(){Text=text,Name=name,Dock=DockStyle.Fill,FlatStyle=FlatStyle.Flat,BackColor=primary?Color.FromArgb(27,100,187):Color.FromArgb(232,238,246),ForeColor=primary?Color.White:Color.FromArgb(25,46,69),Margin=new Padding(3),Cursor=Cursors.Hand,UseVisualStyleBackColor=false};
    private void RefreshDrivers()
    {
        try{drivers.DataSource=J2534Drivers.Discover().ToArray();drivers.DisplayMember=nameof(J2534Driver.Name);status.Text=drivers.Items.Count==0?"Install the manufacturer's 32-bit Mongoose J2534 driver.":"Ready — choose an action";}
        catch(Exception error){ShowError(error);}UpdateActions();
    }
    private IReadTransport Connect()
    {
        if(activeDriver is null)throw new TransportException("Select an installed Mongoose adapter driver first.");
        var link=new J2534ReadTransport(new NativeJ2534Channel(activeDriver));try{link.Initialize();return link;}catch{link.Dispose();throw;}
    }
    private async Task ReadAsync()
    {
        string? saved=null;
        await Run(token=>
        {
            var link=Connect();try{saved=files.Read(link,(pass,bytes)=>{if(bytes%32768==0||bytes==TargetPolicy.RomSize)Ui(()=>{progress.Style=ProgressBarStyle.Continuous;progress.Maximum=TargetPolicy.RomSize*2;progress.Value=(pass-1)*TargetPolicy.RomSize+bytes;readInfo.Text=$"Read {pass}/2 — {bytes/1024:N0} / 1,024 KiB";});},token);}
            finally{link.Dispose();if(link.CleanupWarning is not null)throw new BenchCleanupException(link.CleanupWarning);}
            Append("Verified original backup saved: "+saved);
        });
        if(saved is not null){original.Text=saved;readInfo.Text="Backup verified. Save a BIN copy for editing externally.\n"+saved;UpdateActions();}
    }
    private void ChooseBackup()
    {
        using var dialog=new OpenFileDialog{Title="Choose original.rom inside the complete backup folder",Filter="Original MazdaLink backup|original.rom",CheckFileExists=true};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        try{files.SelectOriginal(dialog.FileName);original.Text=files.OriginalPath;edited.Clear();preparedInfo.Text="Original backup verified. Load your externally edited BIN next.";readInfo.Text="Existing verified backup selected.";}
        catch(Exception error){original.Clear();edited.Clear();ShowError(error);}UpdateActions();
    }
    private void Export()
    {
        using var dialog=new SaveFileDialog{Title="Save raw BIN copy for external editing",Filter="Raw BIN file|*.bin",FileName="Mazda2-original.bin",OverwritePrompt=false};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        try{files.SaveBin(dialog.FileName);Append("Exact BIN copy saved: "+dialog.FileName);status.Text="BIN saved — edit a working copy externally";}
        catch(Exception error){ShowError(error);}
    }
    private async Task LoadEditedAsync()
    {
        using var dialog=new OpenFileDialog{Title="Load externally edited BIN or ROM",Filter="Raw ECU files|*.bin;*.rom|All files|*.*",CheckFileExists=true};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        string selected=dialog.FileName;
        await Run(_=>{string prepared=files.Prepare(selected);Append("All 19 checksums corrected and verified. Prepared copy: "+prepared);});
        edited.Text=files.EditedPath??"";
        preparedInfo.Text=files.PreparedPath is null?"File preparation did not complete. Check the activity log.":$"Ready: {files.CalibrationChanges:N0} calibration bytes changed; all 19 checksums pass.\nOriginal and edited inputs are unchanged. Prepared SHA256:\n{files.PreparedSha256}";
        UpdateActions();
    }
    private async Task FlashAsync(bool recovery)
    {
        await Run(token=>
        {
            string originalPath=files.OriginalPath??throw new InvalidDataException("Choose a verified original backup.");
            string? prepared=recovery?null:files.RequirePrepared();
            var writer=new UiWriter(Append);
            using var reader=new PromptReader(()=>Prompt(writer.TakePrompt()));
            new ModifiedFlashTerminal(Connect,(sequence,plan,journal)=>new NativeModifiedFlashChannel(activeDriver!,sequence,plan,journal),reader,writer,root)
                .Run(originalPath,prepared,recovery,token);
        },true);
    }
    private async Task Run(Action<CancellationToken> action,bool isDestructive=false)
    {
        if(busy)return;
        activeDriver=drivers.SelectedItem as J2534Driver;
        busy=true;destructive=isDestructive;cancellation=new();elapsed=Stopwatch.StartNew();timer.Start();progress.Style=ProgressBarStyle.Marquee;UpdateActions();
        try{await Task.Run(()=>action(cancellation.Token));status.Text="Completed — see activity and saved evidence";}
        catch(OperationCanceledException){Append("STOPPED. No request was resent. Keep any saved evidence.");status.Text="Stopped — review activity before continuing";}
        catch(Exception error) when(error is TransportException or InvalidDataException or IOException or UnauthorizedAccessException){ShowError(error);}
        catch(Exception error)
        {
            ShowError(error);busy=false;
            MessageBox.Show(this,"MazdaLink must close after an unexpected or cleanup failure. Retain the saved evidence and reconnect before continuing.\n"+error.Message,"MazdaLink stopped",MessageBoxButtons.OK,MessageBoxIcon.Error);
            Close();
        }
        finally{timer.Stop();busy=false;destructive=false;cancellation?.Dispose();cancellation=null;if(!IsDisposed){progress.Style=ProgressBarStyle.Continuous;progress.Value=0;UpdateActions();}}
    }
    private void Stop()
    {
        if(!busy)return;
        if(destructive&&MessageBox.Show(this,"Stopping a write can leave incomplete ECU data. Stop the host operation and retain evidence? No request will be retried.","Stop bench write?",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
        cancellation?.Cancel();stop.Enabled=false;
    }
    private void UpdateActions()
    {
        bool adapter=drivers.SelectedItem is J2534Driver;
        read.Enabled=!busy&&adapter;refresh.Enabled=!busy;drivers.Enabled=!busy;choose.Enabled=!busy;
        export.Enabled=!busy&&files.OriginalPath is not null;load.Enabled=!busy&&files.OriginalPath is not null;
        write.Enabled=!busy&&adapter&&files.PreparedPath is not null&&files.Calibration==Zyj2RecoveryPolicy.SoftwareId;recover.Enabled=!busy&&adapter&&files.OriginalPath is not null&&files.Calibration==Zyj2RecoveryPolicy.SoftwareId;
        stop.Enabled=busy;
        foreach(var button in new[]{read,load,write})
        {button.BackColor=button.Enabled?Color.FromArgb(27,100,187):Color.FromArgb(232,238,246);button.ForeColor=button.Enabled?Color.White:Color.FromArgb(110,120,135);}
    }
    private void ShowError(Exception error){Append("STOPPED: "+error.Message);status.Text="Stopped — see activity";}
    private void Append(string text)=>Ui(()=>{if(log.TextLength>500000)log.Text=log.Text[^250000..];log.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\n");log.SelectionStart=log.TextLength;log.ScrollToCaret();});
    private void Ui(Action action){if(IsDisposed)return;if(InvokeRequired)BeginInvoke(action);else action();}
    private string Prompt(string message)
    {
        if(InvokeRequired)
        {
            try{return (string)Invoke(()=>Prompt(message));}
            catch(System.Reflection.TargetInvocationException error) when(error.InnerException is OperationCanceledException){throw new OperationCanceledException("Operator cancelled the bench sequence.");}
        }
        using var dialog=new Form{Text="MazdaLink — operator confirmation",StartPosition=FormStartPosition.CenterParent,ClientSize=new Size(800,560),MinimumSize=new Size(700,500),Font=Font};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(18),RowCount=4,ColumnCount=1};
        layout.RowStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.Absolute,32));layout.RowStyles.Add(new(SizeType.Absolute,38));layout.RowStyles.Add(new(SizeType.Absolute,46));
        var body=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,Text=message,BackColor=Color.White};
        var response=new TextBox{Dock=DockStyle.Fill,PlaceholderText="Type the words requested above exactly"};
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
        var confirm=new Button{Text="Continue",DialogResult=DialogResult.OK,AutoSize=true};var cancel=new Button{Text="Cancel / stop",DialogResult=DialogResult.Cancel,AutoSize=true};buttons.Controls.Add(confirm);buttons.Controls.Add(cancel);
        layout.Controls.Add(body);layout.Controls.Add(Label("Follow the power instructions, then enter the requested confirmation:","PromptHint"));layout.Controls.Add(response);layout.Controls.Add(buttons);dialog.Controls.Add(layout);dialog.AcceptButton=confirm;dialog.CancelButton=cancel;
        dialog.Shown+=(_,_)=>{body.SelectionStart=body.TextLength;body.ScrollToCaret();response.Focus();};
        if(dialog.ShowDialog(this)!=DialogResult.OK)throw new OperationCanceledException("Operator cancelled the bench sequence.");
        return response.Text;
    }
    internal static int Smoke(string folder)
    {
        Directory.CreateDirectory(folder);using var window=new MainWindow(true);window.Show();Application.DoEvents();
        if(window.read.Enabled||window.write.Enabled||window.recover.Enabled||window.export.Enabled||window.stop.Enabled||window.tabs.TabPages.Count!=2)throw new InvalidDataException("Unsafe initial UI action state.");
        for(int i=0;i<2;i++){window.tabs.SelectedIndex=i;Application.DoEvents();using var bitmap=new Bitmap(window.Width,window.Height);window.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(folder,i==0?"ui-read.png":"ui-write.png"));}
        File.WriteAllText(Path.Combine(folder,"ui-report.json"),JsonSerializer.Serialize(new{passed=true,frontend="WindowsForms",sections=new[]{"Read & save","Write edited BIN"},hardwareAccessed=false,unpreparedWriteDisabled=true,noAdapterActionsDisabled=true,imagesRendered=2,dpi=window.DeviceDpi,width=window.Width,height=window.Height,clientWidth=window.ClientSize.Width,clientHeight=window.ClientSize.Height,writeActionVisible=window.write.Visible}));
        window.Close();Console.WriteLine("UI SMOKE PASSED — two sections rendered; hardware unopened and unprepared writing disabled.");return 0;
    }
    private sealed class PromptReader(Func<string> read):TextReader{public override string ReadLine()=>read();}
    private sealed class UiWriter(Action<string> line):TextWriter
    {
        private readonly StringBuilder prompt=new(),pending=new();public override Encoding Encoding=>Encoding.UTF8;
        public override void Write(char value){if(value=='\n'){WriteLine(pending.ToString());pending.Clear();}else if(value!='\r')pending.Append(value);}
        public override void WriteLine(string? value){string text=value??"";line(text);prompt.AppendLine(text);if(prompt.Length>16000)prompt.Remove(0,prompt.Length-12000);}
        public string TakePrompt(){string text=prompt.ToString();prompt.Clear();return text;}
    }
}
