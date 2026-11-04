using System;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace MadiSetup;
internal static class Program
{
 [STAThread] static void Main(string[] args)
 {
  var smoke = args.Contains("--smoke-ui", StringComparer.OrdinalIgnoreCase);
  var launchedAsUninstaller = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "").StartsWith("Uninstall Madi AI", StringComparison.OrdinalIgnoreCase);
  var uninstallRequested = launchedAsUninstaller || args.Contains("/uninstall", StringComparer.OrdinalIgnoreCase) || args.Contains("/uninstall-temp", StringComparer.OrdinalIgnoreCase);
  try
  {
   if (uninstallRequested && !smoke && !args.Contains("/uninstall-temp", StringComparer.OrdinalIgnoreCase))
   {
    var temp = Path.Combine(Path.GetTempPath(), "MadiAI-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
    File.Copy(Environment.ProcessPath!, temp, true);
    Process.Start(new ProcessStartInfo(temp, "/uninstall-temp") { UseShellExecute = true });
    return;
   }
   var uninstall = uninstallRequested || args.Contains("--smoke-uninstall-ui", StringComparer.OrdinalIgnoreCase);
   if (args.Contains("--smoke-install-uninstall", StringComparer.OrdinalIgnoreCase))
   {
    try
    {
     var window = new SetupWindow(false);
     System.Threading.Tasks.Task.Run(window.IntegrationTest).GetAwaiter().GetResult();
     File.WriteAllText(Path.Combine(Path.GetTempPath(),"madi-install-integration.log"),"Install, Control Panel registration, and uninstall succeeded");
    }
    catch (Exception ex) { LogError(ex); Environment.ExitCode = 5; }
    return;
   }
   var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
   app.DispatcherUnhandledException += (_, e) =>
   {
    if (smoke) LogError(e.Exception); else ShowError(e.Exception);
    e.Handled = true;
    app.Shutdown(1);
   };
   AppDomain.CurrentDomain.UnhandledException += (_, e) =>
   {
    if (e.ExceptionObject is Exception ex) LogError(ex);
   };
   if (smoke)
   {
    var log = Path.Combine(Path.GetTempPath(), "madi-setup-smoke.log");
    File.WriteAllText(log, "Process entered Main at " + DateTime.UtcNow.ToString("O") + Environment.NewLine);
    var win = new SetupWindow(uninstall);
    win.Loaded += (_, _) => File.AppendAllText(log, "WPF setup window Loaded; mode: " + (uninstall ? "uninstall" : "install") + "; first-install-code: " + win.RequiresCode + Environment.NewLine);
    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
    timer.Tick += (_, _) => { timer.Stop(); File.AppendAllText(log, "Window visible: " + win.IsVisible + "; loaded: " + win.IsLoaded + Environment.NewLine); app.Shutdown(win.IsLoaded ? 0 : 3); };
    timer.Start();
    app.Run(win);
   }
   else app.Run(new SetupWindow(uninstall));
  }
  catch (Exception ex) { if (smoke) { LogError(ex); Environment.ExitCode = 4; } else ShowError(ex); }
 }
 static void ShowError(Exception ex)
 {
  var log = LogError(ex);
  try { MessageBox.Show("Madi AI Setup could not start. Details were saved to:\n" + log + "\n\n" + ex.Message,
    "Madi AI Setup - Startup Error", MessageBoxButton.OK, MessageBoxImage.Error); }
  catch { }
 }
 static string LogError(Exception ex)
 {
  var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MadiAI");
  var file = Path.Combine(folder, "setup-crash.log");
  try
  {
   Directory.CreateDirectory(folder);
   File.AppendAllText(file, DateTime.Now.ToString("O") + Environment.NewLine + ex + Environment.NewLine + Environment.NewLine);
  }
  catch { }
  return file;
 }
}
public sealed class SetupWindow : Window
{
 static readonly Color Cyan=Color.FromRgb(32,209,255);
 static readonly Brush Ink=new SolidColorBrush(Color.FromRgb(226,246,255));
 static readonly Brush Panel=new SolidColorBrush(Color.FromRgb(7,25,49));
 readonly string target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","MadiAI");
 readonly bool uninstall;
 public bool RequiresCode => !uninstall && !File.Exists(authPath);
 readonly string authPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MadiAI","unlock.json");
 readonly PasswordBox secretInput=new(){Height=35,FontSize=17,Background=new SolidColorBrush(Color.FromRgb(7,32,61)),Foreground=Brushes.White,BorderBrush=new SolidColorBrush(Cyan),BorderThickness=new Thickness(1),Margin=new Thickness(0,4,0,7)};
 readonly PasswordBox confirmInput=new(){Height=35,FontSize=17,Background=new SolidColorBrush(Color.FromRgb(7,32,61)),Foreground=Brushes.White,BorderBrush=new SolidColorBrush(Cyan),BorderThickness=new Thickness(1),Margin=new Thickness(0,4,0,7)};
 bool finished;
 readonly TextBlock status=new(){FontSize=14,Foreground=Ink,TextWrapping=TextWrapping.Wrap,HorizontalAlignment=HorizontalAlignment.Center};
 readonly ProgressBar progress=new(){Height=13,Minimum=0,Maximum=100,Value=0,Foreground=new SolidColorBrush(Cyan),Background=new SolidColorBrush(Color.FromRgb(12,43,77)),Margin=new Thickness(0,17,0,17)};
 readonly Button primary=new(){Height=48,FontSize=17,FontWeight=FontWeights.Bold,Foreground=Brushes.White,Background=new SolidColorBrush(Color.FromRgb(16,102,163)),BorderBrush=new SolidColorBrush(Cyan),BorderThickness=new Thickness(2),Cursor=System.Windows.Input.Cursors.Hand};
 readonly CheckBox launch=new(){Content="Launch Madi AI after installation",Foreground=Ink,IsChecked=true,Margin=new Thickness(0,12,0,12),HorizontalAlignment=HorizontalAlignment.Center};
 readonly TextBlock title=new(){FontSize=31,FontWeight=FontWeights.Bold,Foreground=Ink,HorizontalAlignment=HorizontalAlignment.Center};
 readonly TextBlock subtitle=new(){FontSize=14,Foreground=new SolidColorBrush(Color.FromRgb(137,189,224)),HorizontalAlignment=HorizontalAlignment.Center};
 public SetupWindow(bool remove)
 {
  uninstall=remove; Title=remove?"Madi AI Uninstaller":"Madi AI Setup";Width=820;Height=640;MinWidth=820;MinHeight=640;MaxWidth=820;MaxHeight=640;
  WindowStartupLocation=WindowStartupLocation.CenterScreen;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;Background=Brushes.Transparent;AllowsTransparency=true;
  var border=new Border{CornerRadius=new CornerRadius(18),BorderBrush=new SolidColorBrush(Cyan),BorderThickness=new Thickness(2),Background=Panel,Effect=new DropShadowEffect{Color=Colors.DeepSkyBlue,BlurRadius=28,Opacity=.32,ShadowDepth=0}};
  Content=border;
  var grid=new Grid();border.Child=grid;
  grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(52)});
  grid.RowDefinitions.Add(new RowDefinition());
  var header=new Grid{Background=new SolidColorBrush(Color.FromRgb(5,18,36))};Grid.SetRow(header,0);grid.Children.Add(header);
  header.MouseLeftButtonDown+=(_,e)=>{if(e.ButtonState==System.Windows.Input.MouseButtonState.Pressed)DragMove();};
  header.Children.Add(new TextBlock{Text="◈  MADI AI  /  "+(remove?"UNINSTALL":"SETUP"),FontWeight=FontWeights.Bold,FontSize=15,Foreground=new SolidColorBrush(Cyan),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(22,0,22,0)});
  var close=new Button{Content="×",Width=50,FontSize=27,Foreground=Ink,Background=Brushes.Transparent,BorderThickness=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Right,Cursor=System.Windows.Input.Cursors.Hand};close.Click+=(_,_)=>Close();header.Children.Add(close);
  var body=new Grid{Margin=new Thickness(22,20,22,18)};
  body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(260)});
  body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
  Grid.SetRow(body,1);grid.Children.Add(body);
  var portrait=new Border{
   CornerRadius=new CornerRadius(16),
   Background=new SolidColorBrush(Color.FromRgb(3,18,40)),
   BorderBrush=new SolidColorBrush(Color.FromRgb(19,93,153)),
   BorderThickness=new Thickness(1),
   Margin=new Thickness(0,0,16,0)
  };
  Grid.SetColumn(portrait,0);body.Children.Add(portrait);
  var portraitGrid=new Grid();portrait.Child=portraitGrid;
  portraitGrid.Children.Add(new TextBlock{
   Text="MADI AI",FontSize=18,FontWeight=FontWeights.Bold,Foreground=new SolidColorBrush(Cyan),
   HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,0,20)
  });
  try
  {
   portraitGrid.Children.Add(new Image{
    Source=new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/assistant.png")),
    Width=242,Height=440,Stretch=Stretch.Uniform,VerticalAlignment=VerticalAlignment.Top,
    Margin=new Thickness(0,6,0,0)
   });
  }
  catch
  {
   portraitGrid.Children.Add(new TextBlock{
    Text="◈",FontSize=100,Foreground=new SolidColorBrush(Cyan),
    HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center
   });
  }
  var right=new StackPanel{Margin=new Thickness(12,12,8,0)};
  Grid.SetColumn(right,1);body.Children.Add(right);
  title.Text=remove?"Uninstall Madi AI":"Welcome to Madi AI Setup";
  title.FontSize=27;title.TextAlignment=TextAlignment.Center;title.TextWrapping=TextWrapping.Wrap;
  right.Children.Add(title);
  subtitle.Text=remove
    ?"Remove Madi AI from this PC. Your personal unlock profile will be preserved."
    :"Your Personal AI Assistant  •  Native Windows Edition";
  subtitle.TextWrapping=TextWrapping.Wrap;subtitle.TextAlignment=TextAlignment.Center;
  subtitle.Margin=new Thickness(0,9,0,10);right.Children.Add(subtitle);
  status.Text=remove
    ?"Press UNINSTALL to remove the application, shortcuts and Control Panel entry."
    :"Install the native Madi AI desktop application. No command prompt required.";
  status.Margin=new Thickness(4,6,4,8);right.Children.Add(status);
  if(!remove)
  {
   bool firstInstall=!File.Exists(authPath);
   right.Children.Add(new TextBlock{
    Text=firstInstall?"CREATE YOUR PRIVATE ACCESS CODE":"PRIVATE ACCESS CODE",
    Foreground=new SolidColorBrush(Cyan),FontWeight=FontWeights.SemiBold,
    FontSize=13,Margin=new Thickness(0,14,0,4)
   });
   right.Children.Add(secretInput);
   right.Children.Add(new TextBlock{Text="CONFIRM ACCESS CODE",Foreground=new SolidColorBrush(Cyan),FontWeight=FontWeights.SemiBold,FontSize=13,Margin=new Thickness(0,5,0,4)});
   right.Children.Add(confirmInput);
   right.Children.Add(new TextBlock{
    Text=firstInstall?"Choose at least 6 characters. Your code stays private on this computer.":"Enter a new code to change it, or leave both fields blank to keep your current code.",
    TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.FromRgb(146,190,222)),
    FontSize=12,Margin=new Thickness(0,7,0,5)
   });
  }
  right.Children.Add(progress);
  if(!remove)right.Children.Add(launch);
  primary.Content=remove?"UNINSTALL MADI AI":"INSTALL MADI AI";
  primary.Margin=new Thickness(45,12,45,0);
  primary.Background=new SolidColorBrush(Color.FromRgb(93,42,174));
  primary.BorderBrush=new SolidColorBrush(Color.FromRgb(181,102,255));
  primary.Click+=Primary_Click;right.Children.Add(primary);
  right.Children.Add(new TextBlock{
   Text=remove?"REMOVE APPLICATION  •  KEEP PERSONAL CODE":"MADI AI  •  WINDOWS DESKTOP  •  LOCAL CORE",
   Foreground=new SolidColorBrush(Color.FromRgb(94,172,223)),FontSize=11,
   HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,15,0,0)
  });
 }
 async void Primary_Click(object sender,RoutedEventArgs e)
 {
  if(finished){if(!uninstall && launch.IsChecked==true)TryLaunch();Close();return;}
  string? code=null;
  if(!uninstall)
  {
   // Read WPF PasswordBox and checkbox state only on the owning UI thread.
   // Never access these controls from the background installation worker.
   string entered=secretInput.Password;
   string confirmed=confirmInput.Password;
   bool mustSetCode=!File.Exists(authPath);
   if(mustSetCode || entered.Length>0 || confirmed.Length>0)
   {
    if(entered.Length<6){status.Text="Please enter an access code with at least 6 characters.";secretInput.Focus();return;}
    if(!string.Equals(entered,confirmed,StringComparison.Ordinal)){status.Text="The two access codes do not match.";confirmInput.Focus();return;}
    code=entered;
   }
  }
  await ExecuteAsync(code);
 }
 async System.Threading.Tasks.Task ExecuteAsync(string? newCode)
 {
  primary.IsEnabled=false;
  try
  {
   status.Text=uninstall?"Removing application...":"Installing application...";
   progress.IsIndeterminate=true;
   // Pass immutable code data into the worker, not WPF controls or Window properties.
   await System.Threading.Tasks.Task.Run(()=>{if(uninstall)Remove();else Install(newCode);});
   progress.IsIndeterminate=false;progress.Value=100;
   status.Text=uninstall?"Madi AI was uninstalled.":"Madi AI was installed successfully.";
   primary.Content="FINISH";finished=true;primary.IsEnabled=true;
   secretInput.Clear();confirmInput.Clear();
  }
  catch(Exception ex)
  {
   LogInstallationFailure(ex);
   progress.IsIndeterminate=false;
   progress.Value=0;
   status.Text=(uninstall?"Uninstall could not finish: ":"Installation failed: ")+ex.Message+
     (uninstall?"  Close Madi AI and retry.":"  You can retry without re-entering your code.");
   primary.Content=uninstall?"RETRY UNINSTALL":"RETRY INSTALL";
   finished=false;
   primary.IsEnabled=true;
  }
 }
 static void LogInstallationFailure(Exception ex)
 {
  try
  {
   var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MadiAI");
   Directory.CreateDirectory(folder);
   File.AppendAllText(Path.Combine(folder,"setup-crash.log"),DateTime.Now.ToString("O")+Environment.NewLine+ex+Environment.NewLine);
  }
  catch { }
 }
 void TryLaunch(){try{Process.Start(new ProcessStartInfo(Path.Combine(target,"MadiAI.exe")){UseShellExecute=true});}catch{}}
 public void IntegrationTest()
 {
  var createdProfile=!File.Exists(authPath);
  var testCode=Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
  // Exercise the real first-install / update code path from a background thread.
  Install(createdProfile?testCode:null);
  if(!File.Exists(authPath)) throw new IOException("Installer did not create secret code profile.");
  if(createdProfile)
  {
   VerifyCode(testCode);
   var replacementCode=Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
   Install(replacementCode);
   VerifyCode(replacementCode);
  }
  if (!File.Exists(Path.Combine(target,"MadiAI.exe"))) throw new IOException("Installed app missing.");
  using (var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\MadiAI"))
  {
   if (key == null || !(key.GetValue("UninstallString")?.ToString()?.Contains("/uninstall") ?? false))
    throw new IOException("Control Panel uninstall registration invalid.");
  }
  Remove();
  if (Directory.Exists(target)) throw new IOException("Uninstall did not remove application folder.");
  using var leftover=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\MadiAI");
  if (leftover != null) throw new IOException("Uninstall registry entry was not removed.");
  if(createdProfile) File.Delete(authPath);
 }
 void VerifyCode(string code)
 {
  using var document=JsonDocument.Parse(File.ReadAllText(authPath));
  var salt=Convert.FromBase64String(document.RootElement.GetProperty("salt").GetString()!);
  var saved=Convert.FromBase64String(document.RootElement.GetProperty("hash").GetString()!);
  if(salt.Length!=16||saved.Length!=32)throw new IOException("Invalid saved code format.");
  var calculated=Rfc2898DeriveBytes.Pbkdf2(code,salt,200000,HashAlgorithmName.SHA256,32);
  if(!CryptographicOperations.FixedTimeEquals(saved,calculated))throw new IOException("Saved access code verification failed.");
 }
 void Install(string? newCode)
 {
  if(!File.Exists(authPath) && string.IsNullOrWhiteSpace(newCode))
   throw new InvalidOperationException("Please set your private access code before installing.");
  Directory.CreateDirectory(target);
  using var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("MadiAI.Payload") ?? throw new FileNotFoundException("Embedded app package not found.");
  using var archive=new ZipArchive(resource,ZipArchiveMode.Read);
  archive.ExtractToDirectory(target,true);
  File.Copy(Environment.ProcessPath!,Path.Combine(target,"Uninstall Madi AI.exe"),true);
  var exe=Path.Combine(target,"MadiAI.exe");
  Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"Madi AI.lnk"),exe);
  var menu=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Madi AI");
  Directory.CreateDirectory(menu);
  Shortcut(Path.Combine(menu,"Madi AI.lnk"),exe);
  Shortcut(Path.Combine(menu,"Uninstall Madi AI.lnk"),Path.Combine(target,"Uninstall Madi AI.exe"),"/uninstall");
  using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\MadiAI");
  key.SetValue("DisplayName","Madi AI");
  key.SetValue("DisplayIcon",exe);
  key.SetValue("Publisher","Madi AI");
  key.SetValue("UninstallString",'"'+Path.Combine(target,"Uninstall Madi AI.exe")+'"'+" /uninstall");
  key.SetValue("DisplayVersion","0.2.5");
  key.SetValue("NoModify",1,RegistryValueKind.DWord);
  key.SetValue("NoRepair",1,RegistryValueKind.DWord);
  key.SetValue("InstallLocation",target);
  if(!string.IsNullOrEmpty(newCode))
  {
   var salt=RandomNumberGenerator.GetBytes(16);
   var hash=Rfc2898DeriveBytes.Pbkdf2(newCode,salt,200000,HashAlgorithmName.SHA256,32);
   Directory.CreateDirectory(Path.GetDirectoryName(authPath)!);
   var temp=authPath+".new";
   File.WriteAllText(temp,JsonSerializer.Serialize(new {salt=Convert.ToBase64String(salt),hash=Convert.ToBase64String(hash)}));
   if(File.Exists(authPath))File.Replace(temp,authPath,null);
   else File.Move(temp,authPath,false);
  }
 }
 static void Shortcut(string path,string exe,string arguments="")
 {
  var t=Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows shortcut support unavailable.");
  dynamic shell=Activator.CreateInstance(t)!;
  dynamic link=shell.CreateShortcut(path);
  link.TargetPath=exe;link.Arguments=arguments;link.WorkingDirectory=Path.GetDirectoryName(exe);link.IconLocation=exe+",0";link.Save();
  MarshalRelease(link);MarshalRelease(shell);
 }
 static void MarshalRelease(object obj){try{if(System.Runtime.InteropServices.Marshal.IsComObject(obj))System.Runtime.InteropServices.Marshal.FinalReleaseComObject(obj);}catch{}}
 void Remove()
 {
  // The uninstaller runs from a temporary copy so it can remove its installed executable.
  // Do not remove shortcuts/registry until the application directory is actually gone.
  // Retry transient Windows file locks rather than reporting a partially completed uninstall.
  var appExe=Path.GetFullPath(Path.Combine(target,"MadiAI.exe"));
  foreach (var proc in Process.GetProcessesByName("MadiAI"))
  {
   try
   {
    string? processFile=null;
    try { processFile=proc.MainModule?.FileName; }
    catch (System.ComponentModel.Win32Exception) { }
    catch (InvalidOperationException) { }
    if (processFile!=null && string.Equals(Path.GetFullPath(processFile),appExe,StringComparison.OrdinalIgnoreCase))
    {
     if (!proc.HasExited)
     {
      proc.CloseMainWindow();
      if (!proc.WaitForExit(8000))
       throw new IOException("Madi AI is still running. Save your work, close its window, and select RETRY UNINSTALL.");
     }
    }
   }
   finally { proc.Dispose(); }
  }
  if (Directory.Exists(target))
  {
   Exception? last=null;
   for (int attempt=0;attempt<8;attempt++)
   {
    try { Directory.Delete(target,true); last=null; break; }
    catch (IOException ex) { last=ex; }
    catch (UnauthorizedAccessException ex) { last=ex; }
    if (attempt<7) Thread.Sleep(750);
   }
   if (Directory.Exists(target))
    throw new IOException("Some application files are still locked by Windows or another process. Close Madi AI and any open files in its installation folder, then retry. Details: "+last?.Message,last);
  }
  var desktop=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"Madi AI.lnk");
  if(File.Exists(desktop))File.Delete(desktop);
  var menu=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Madi AI");
  if(Directory.Exists(menu))Directory.Delete(menu,true);
  Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\MadiAI",false);
  // The LocalAppData/MadiAI profile intentionally remains for secret-code persistence.
 }
}
