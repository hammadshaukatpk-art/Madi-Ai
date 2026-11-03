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

namespace MadiSetup;
internal static class Program
{
 [STAThread] static void Main(string[] args)
 {
  if (args.Contains("/uninstall", StringComparer.OrdinalIgnoreCase))
  {
   // Run the uninstaller outside the install directory so all installed files can be removed.
   var temp = Path.Combine(Path.GetTempPath(), "MadiAI-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
   File.Copy(Environment.ProcessPath!, temp, true);
   Process.Start(new ProcessStartInfo(temp, "/uninstall-temp") { UseShellExecute = true });
   return;
  }
  var uninstall = args.Contains("/uninstall-temp", StringComparer.OrdinalIgnoreCase);
  var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
  app.Run(new SetupWindow(uninstall));
 }
}
public sealed class SetupWindow : Window
{
 static readonly Color Cyan=Color.FromRgb(32,209,255);
 static readonly Brush Ink=new SolidColorBrush(Color.FromRgb(226,246,255));
 static readonly Brush Panel=new SolidColorBrush(Color.FromRgb(7,25,49));
 readonly string target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","MadiAI");
 readonly bool uninstall;
 readonly TextBlock status=new(){FontSize=14,Foreground=Ink,TextWrapping=TextWrapping.Wrap,HorizontalAlignment=HorizontalAlignment.Center};
 readonly ProgressBar progress=new(){Height=13,Minimum=0,Maximum=100,Value=0,Foreground=new SolidColorBrush(Cyan),Background=new SolidColorBrush(Color.FromRgb(12,43,77)),Margin=new Thickness(0,17,0,17)};
 readonly Button primary=new(){Height=48,FontSize=17,FontWeight=FontWeights.Bold,Foreground=Brushes.White,Background=new SolidColorBrush(Color.FromRgb(16,102,163)),BorderBrush=new SolidColorBrush(Cyan),BorderThickness=new Thickness(2),Cursor=System.Windows.Input.Cursors.Hand};
 readonly CheckBox launch=new(){Content="Launch Madi AI after installation",Foreground=Ink,IsChecked=true,Margin=new Thickness(0,12),HorizontalAlignment=HorizontalAlignment.Center};
 readonly TextBlock title=new(){FontSize=31,FontWeight=FontWeights.Bold,Foreground=Ink,HorizontalAlignment=HorizontalAlignment.Center};
 readonly TextBlock subtitle=new(){FontSize=14,Foreground=new SolidColorBrush(Color.FromRgb(137,189,224)),HorizontalAlignment=HorizontalAlignment.Center};
 public SetupWindow(bool remove)
 {
  uninstall=remove; Title=remove?"Madi AI Uninstaller":"Madi AI Setup";Width=740;Height=550;MinWidth=740;MinHeight=550;MaxWidth=740;MaxHeight=550;
  WindowStartupLocation=WindowStartupLocation.CenterScreen;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;Background=Brushes.Transparent;AllowsTransparency=true;
  var border=new Border{CornerRadius=new CornerRadius(18),BorderBrush=new SolidColorBrush(Cyan),BorderThickness=new Thickness(2),Background=Panel,Effect=new DropShadowEffect{Color=Colors.DeepSkyBlue,BlurRadius=28,Opacity=.32,ShadowDepth=0}};
  Content=border;
  var grid=new Grid();border.Child=grid;
  grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(52)});
  grid.RowDefinitions.Add(new RowDefinition());
  var header=new Grid{Background=new SolidColorBrush(Color.FromRgb(5,18,36))};Grid.SetRow(header,0);grid.Children.Add(header);
  header.MouseLeftButtonDown+=(_,e)=>{if(e.ButtonState==System.Windows.Input.MouseButtonState.Pressed)DragMove();};
  header.Children.Add(new TextBlock{Text="◈  MADI AI  /  "+(remove?"UNINSTALL":"SETUP"),FontWeight=FontWeights.Bold,FontSize=15,Foreground=new SolidColorBrush(Cyan),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(22,0)});
  var close=new Button{Content="×",Width=50,FontSize=27,Foreground=Ink,Background=Brushes.Transparent,BorderThickness=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Right,Cursor=System.Windows.Input.Cursors.Hand};close.Click+=(_,_)=>Close();header.Children.Add(close);
  var body=new StackPanel{Margin=new Thickness(82,12),HorizontalAlignment=HorizontalAlignment.Stretch};Grid.SetRow(body,1);grid.Children.Add(body);
  body.Children.Add(new Image{Source=new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/madi.png")),Width=118,Height=118,HorizontalAlignment=HorizontalAlignment.Center});
  title.Text=remove?"Uninstall Madi AI":"Welcome to Madi AI Setup";body.Children.Add(title);
  subtitle.Text=remove?"Remove the application while keeping your personal secret-code profile.":"Your personal Windows AI assistant • Native desktop edition";subtitle.Margin=new Thickness(0,10,0,8);body.Children.Add(subtitle);
  status.Text=remove?"Click Uninstall to remove the Madi AI application.":"The application will be installed automatically. No CMD setup or paid API key is required.";
  status.Margin=new Thickness(0,12);body.Children.Add(status);
  body.Children.Add(progress);
  if(!remove)body.Children.Add(launch);
  primary.Content=remove?"UNINSTALL MADI AI":"INSTALL MADI AI";
  primary.Margin=new Thickness(90,13,90,0);primary.Click+=async (_,_)=>await ExecuteAsync();body.Children.Add(primary);
  body.Children.Add(new TextBlock{Text="MADI AI  •  DARK CYBER INTERFACE  •  FREE LOCAL CORE",Foreground=new SolidColorBrush(Color.FromRgb(73,153,194)),FontSize=11,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,22,0,0)});
 }
 async System.Threading.Tasks.Task ExecuteAsync()
 {
  primary.IsEnabled=false;
  try
  {
   status.Text=uninstall?"Removing application...":"Installing application...";
   progress.IsIndeterminate=true;
   await System.Threading.Tasks.Task.Run(uninstall?Remove:Install);
   progress.IsIndeterminate=false;progress.Value=100;
   status.Text=uninstall?"Madi AI was uninstalled. Your saved secret code was preserved.":"Madi AI was installed successfully.";
   primary.Content="FINISH";primary.IsEnabled=true;
   primary.Click-=NoOp; // no operation; finish handler below
   primary.Click+=(_,_)=>{if(!uninstall && launch.IsChecked==true)TryLaunch();Close();};
  }
  catch(Exception ex)
  {
   progress.IsIndeterminate=false;status.Text="Unable to finish: "+ex.Message;primary.Content="CLOSE";primary.IsEnabled=true;primary.Click+=(_,_)=>Close();
  }
 }
 void NoOp(object? sender,RoutedEventArgs e){}
 void TryLaunch(){try{Process.Start(new ProcessStartInfo(Path.Combine(target,"MadiAI.exe")){UseShellExecute=true});}catch{}}
 void Install()
 {
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
  Shortcut(Path.Combine(menu,"Uninstall Madi AI.lnk"),Path.Combine(target,"Uninstall Madi AI.exe"));
  using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\MadiAI");
  key.SetValue("DisplayName","Madi AI");
  key.SetValue("DisplayIcon",exe);
  key.SetValue("Publisher","Madi AI");
  key.SetValue("UninstallString",'"'+Path.Combine(target,"Uninstall Madi AI.exe")+'"');
  key.SetValue("InstallLocation",target);
 }
 static void Shortcut(string path,string exe)
 {
  var t=Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows shortcut support unavailable.");
  dynamic shell=Activator.CreateInstance(t)!;
  dynamic link=shell.CreateShortcut(path);
  link.TargetPath=exe;link.WorkingDirectory=Path.GetDirectoryName(exe);link.IconLocation=exe+",0";link.Save();
  MarshalRelease(link);MarshalRelease(shell);
 }
 static void MarshalRelease(object obj){try{if(System.Runtime.InteropServices.Marshal.IsComObject(obj))System.Runtime.InteropServices.Marshal.FinalReleaseComObject(obj);}catch{}}
 void Remove()
 {
  var desktop=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"Madi AI.lnk");
  if(File.Exists(desktop))File.Delete(desktop);
  var menu=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Madi AI");
  if(Directory.Exists(menu))Directory.Delete(menu,true);
  if(Directory.Exists(target))Directory.Delete(target,true);
  Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\MadiAI",false);
  // The LocalAppData/MadiAI profile intentionally remains for secret-code persistence.
 }
}
