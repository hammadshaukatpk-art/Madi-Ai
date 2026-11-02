using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Speech.Synthesis;
using System.Text.RegularExpressions;
using System.Data;
namespace MadiAI;
internal class Program {
 [STAThread] static void Main(){var a=new Application();a.Run(new Home());}
}
public sealed class Home:Window {
 readonly SpeechSynthesizer speech=new();
 readonly Grid root=new(); readonly TextBlock status=new();
 readonly PasswordBox password=new(); readonly TextBox command=new();
 readonly string auth=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MadiAI","unlock.json");
 bool unlocked;
 static readonly Brush ink=new SolidColorBrush(Color.FromRgb(223,246,255));
 static readonly Brush panel=new SolidColorBrush(Color.FromRgb(8,24,42));
 static readonly Brush cyan=new SolidColorBrush(Color.FromRgb(41,211,250));
 public Home(){
  Title="Madi AI — Personal Assistant";Width=1500;Height=900;MinWidth=1100;MinHeight=650;
  Background=new SolidColorBrush(Color.FromRgb(3,11,23));Foreground=ink;FontFamily=new FontFamily("Segoe UI");WindowStartupLocation=WindowStartupLocation.CenterScreen;
  Content=root;root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(230)});root.ColumnDefinitions.Add(new ColumnDefinition());
  BuildShell();ShowGate();
  Loaded+=(_,_)=>Say("Tell me your secret code to open me");
 }
 void Say(string s){try{speech.SpeakAsyncCancelAll();speech.SpeakAsync(s);}catch{}}
 Border Card(){return new Border{Background=panel,BorderBrush=new SolidColorBrush(Color.FromRgb(24,94,135)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(13),Margin=new Thickness(7),Padding=new Thickness(13)};}
 TextBlock Text(string value,int size=15,Brush? color=null){return new TextBlock{Text=value,FontSize=size,Foreground=color??ink,Margin=new Thickness(5),TextWrapping=TextWrapping.Wrap};}
 Button Button(string title,Action click){var b=new Button{Content=title,Foreground=ink,Background=new SolidColorBrush(Color.FromRgb(10,41,69)),BorderBrush=cyan,BorderThickness=new Thickness(1),Margin=new Thickness(4),Padding=new Thickness(9),Cursor=System.Windows.Input.Cursors.Hand};b.Click+=(_,_)=>click();return b;}
 void BuildShell(){
  var side=new StackPanel{Background=panel,Margin=new Thickness(6)};
  side.Children.Add(Text("◈  MADI AI",27,cyan));side.Children.Add(Text("PERSONAL INTELLIGENCE",10,cyan));
  foreach(var label in new[]{"Home","Voice Control","AI Chat","System Control","Apps & Automation","Web & Research","Trading (MT5)","eBay Automation","AliExpress Hunting","YouTube Control","Canva Design","WhatsApp","Files & Folders","Tasks & Routines","Settings"})
   side.Children.Add(Button("◈  "+label,()=>{status.Text=label+" — available commands or planned integrations";}));
  root.Children.Add(side);
  var main=new Grid{Margin=new Thickness(12)};Grid.SetColumn(main,1);root.Children.Add(main);
  main.RowDefinitions.Add(new RowDefinition{Height=new GridLength(100)});
  main.RowDefinitions.Add(new RowDefinition());main.RowDefinitions.Add(new RowDefinition{Height=new GridLength(98)});main.RowDefinitions.Add(new RowDefinition{Height=new GridLength(74)});
  var header=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center};
  header.Children.Add(Text("MADI AI",38,cyan));header.Children.Add(Text("Your Personal AI Assistant",16));
  main.Children.Add(header);
  var mid=new Grid();mid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(26,GridUnitType.Star)});mid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(47,GridUnitType.Star)});mid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(27,GridUnitType.Star)});Grid.SetRow(mid,1);main.Children.Add(mid);
  var left=new StackPanel();Grid.SetColumn(left,0);mid.Children.Add(left);
  var stats=Card();var statContent=new StackPanel();stats.Child=statContent;left.Children.Add(stats);
  statContent.Children.Add(Text("● SYSTEM STATUS",18,cyan));
  var memory=Text("RAM  checking…");statContent.Children.Add(memory);
  var disk=Text("DISK  checking…");statContent.Children.Add(disk);
  statContent.Children.Add(Text("CPU monitoring: planned",12));
  var modules=Card();left.Children.Add(modules);var mod=new StackPanel();modules.Child=mod;
  mod.Children.Add(Text("✦ ACTIVE MODULES",18,cyan));
  foreach(var entry in new[]{"● App control","● Web search","● Spoken answers","◌ Offline AI chat: planned","◌ MT5 trading: planned","◌ WhatsApp bridge: planned"})mod.Children.Add(Text(entry,13));
  var center=Card();Grid.SetColumn(center,1);mid.Children.Add(center);
  var centerStack=new StackPanel{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};center.Child=centerStack;
  var orbit=new Grid{Width=375,Height=375};centerStack.Children.Add(orbit);
  foreach(var spec in new[]{(360,2.0,26.0),(305,3.0,-17.0),(240,3.0,10.0),(184,4.0,-8.0)}){
   var ring=new System.Windows.Shapes.Ellipse{Width=spec.Item1,Height=spec.Item1,Stroke=spec.Item3>0?cyan:Brushes.MediumPurple,StrokeThickness=spec.Item2,StrokeDashArray=new DoubleCollection{10,7},Opacity=.82};
   var rotate=new RotateTransform();ring.RenderTransform=rotate;ring.RenderTransformOrigin=new Point(.5,.5);
   rotate.BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(0,360,TimeSpan.FromSeconds(Math.Abs(spec.Item3))){RepeatBehavior=RepeatBehavior.Forever,To=spec.Item3<0?-360:360});
   orbit.Children.Add(ring);
  }
  var core=new Border{Width=140,Height=140,Background=new RadialGradientBrush(Color.FromRgb(25,92,173),Color.FromRgb(3,16,43)),BorderBrush=cyan,BorderThickness=new Thickness(4),CornerRadius=new CornerRadius(100)};
  core.Child=new TextBlock{Text="M",Foreground=cyan,FontWeight=FontWeights.Bold,FontSize=100,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
  orbit.Children.Add(core);
  centerStack.Children.Add(Text("◉  MADI AI • ONLINE",21,cyan));
  status.Text="Ready for your command";status.FontSize=13;status.Foreground=ink;status.TextAlignment=TextAlignment.Center;centerStack.Children.Add(status);
  var right=new StackPanel();Grid.SetColumn(right,2);mid.Children.Add(right);
  var quick=Card();right.Children.Add(quick);var qs=new StackPanel();quick.Child=qs;qs.Children.Add(Text("⚡ QUICK ACTIONS",17,cyan));
  foreach(var (title,cmd) in new[]{("Browser","open browser"),("YouTube","open youtube"),("Calculator","open calculator"),("Open file","open file"),("WhatsApp","open whatsapp"),("MT5","open mt5")})qs.Children.Add(Button(title,()=>Run(cmd)));
  var cap=Card();right.Children.Add(cap);var caps=new StackPanel();cap.Child=caps;
  caps.Children.Add(Text("✦ MY CAPABILITIES",17,cyan));foreach(var x in new[]{"✓ Open Windows apps","✓ Web and search","✓ Calculator and files","◌ Listing automation: planned","◌ Canva editing: planned","◌ WhatsApp calling: unsupported"})caps.Children.Add(Text(x,12));
  var cmdGrid=new Grid();cmdGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(90)});cmdGrid.ColumnDefinitions.Add(new ColumnDefinition());cmdGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(115)});
  Grid.SetRow(cmdGrid,2);main.Children.Add(cmdGrid);
  var mic=Button("🎙 Voice",()=>{status.Text="Offline voice recognition requires a separate speech-recognition module. Text commands work.";Say(status.Text);});cmdGrid.Children.Add(mic);
  command.Background=panel;command.Foreground=ink;command.BorderBrush=cyan;command.FontSize=16;command.Margin=new Thickness(8,19);command.VerticalContentAlignment=VerticalAlignment.Center;
  command.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter)Run(command.Text);};Grid.SetColumn(command,1);cmdGrid.Children.Add(command);
  var run=Button("EXECUTE",()=>Run(command.Text));Grid.SetColumn(run,2);cmdGrid.Children.Add(run);
  var dock=new UniformGrid{Columns=8};Grid.SetRow(dock,3);main.Children.Add(dock);
  foreach(var (name,cmd) in new[]{("Browser","open browser"),("YouTube","open youtube"),("WhatsApp","open whatsapp"),("eBay","open ebay"),("AliExpress","open aliexpress"),("Canva","open canva"),("Office","open word"),("ChatGPT","open chatgpt")})dock.Children.Add(Button(name,()=>Run(cmd)));
  var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(6)};
  timer.Tick+=(_,_)=>{try{var mem=GC.GetGCMemoryInfo();var drv=new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);disk.Text="Disk: "+Math.Round(100.0*(1-drv.TotalFreeSpace/(double)drv.TotalSize))+"% used";memory.Text="RAM available in system settings";}catch{}};
  timer.Start();
 }
 void ShowGate(){
  var layer=new Grid{Background=new SolidColorBrush(Color.FromArgb(245,3,11,23))};Grid.SetColumnSpan(layer,2);Panel.SetZIndex(layer,50);root.Children.Add(layer);
  var card=Card();card.Width=490;card.Height=310;card.HorizontalAlignment=HorizontalAlignment.Center;card.VerticalAlignment=VerticalAlignment.Center;layer.Children.Add(card);
  var stack=new StackPanel{VerticalAlignment=VerticalAlignment.Center};card.Child=stack;stack.Children.Add(Text("◈  MADI AI",36,cyan));
  stack.Children.Add(Text("Tell me your secret code to open me",17));password.Height=42;password.Background=panel;password.Foreground=ink;password.BorderBrush=cyan;password.Margin=new Thickness(4,12);stack.Children.Add(password);
  var label=Text(File.Exists(auth)?"Enter your saved secret code":"First launch: set your secret code (6+ characters)",12);stack.Children.Add(label);
  void Unlock(){
   var code=password.Password;if(code.Length<6){label.Text="At least 6 characters required";return;}
   try{
    if(File.Exists(auth)){
      using var doc=JsonDocument.Parse(File.ReadAllText(auth));
      var salt=Convert.FromBase64String(doc.RootElement.GetProperty("salt").GetString()!);
      var existing=Convert.FromBase64String(doc.RootElement.GetProperty("hash").GetString()!);
      if(!CryptographicOperations.FixedTimeEquals(existing,Rfc2898DeriveBytes.Pbkdf2(code,salt,200000,HashAlgorithmName.SHA256,32))){label.Text="Incorrect code";return;}
    }else{
      var salt=RandomNumberGenerator.GetBytes(16);Directory.CreateDirectory(Path.GetDirectoryName(auth)!);
      var hash=Rfc2898DeriveBytes.Pbkdf2(code,salt,200000,HashAlgorithmName.SHA256,32);
      File.WriteAllText(auth,JsonSerializer.Serialize(new{salt=Convert.ToBase64String(salt),hash=Convert.ToBase64String(hash)}));
    }
    unlocked=true;root.Children.Remove(layer);password.Clear();
    status.Text="Welcome, Boss Hammad. Madi AI is online.";Say(status.Text);
   }catch(Exception ex){label.Text="Unlock error: "+ex.Message;}
  }
  stack.Children.Add(Button("UNLOCK MADI AI",Unlock));
  password.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter)Unlock();};
 }
 static void Open(string target)=>Process.Start(new ProcessStartInfo(target){UseShellExecute=true});
 void Run(string text){
  if(!unlocked)return;
  var cmd=text.Trim().ToLowerInvariant();if(cmd.Length==0)return;
  string answer;
  try{
   if(cmd.StartsWith("calculate ")){
    var exp=cmd[10..];if(!Regex.IsMatch(exp,@"^[0-9.+*/%()\s-]{1,100}$"))throw new Exception("Only arithmetic is supported");
    answer="Result: "+new DataTable().Compute(exp.Replace("%","/100"),null);
   }else if(cmd.StartsWith("search ")){Open("https://www.google.com/search?q="+Uri.EscapeDataString(text[7..]));answer="Searching Google";}
   else if(Regex.IsMatch(cmd,@"\b(shutdown|restart|delete|trade|buy|sell|publish)\b"))answer="This sensitive action is not available yet.";
   else if(cmd.StartsWith("open ")||cmd.StartsWith("launch ")){
    var item=cmd[(cmd.IndexOf(' ')+1)..].Trim();
    var urls=new System.Collections.Generic.Dictionary<string,string>{{"browser","https://www.google.com"},{"google","https://www.google.com"},{"youtube","https://www.youtube.com"},{"whatsapp","https://web.whatsapp.com"},{"chatgpt","https://chatgpt.com"},{"ebay","https://www.ebay.com"},{"aliexpress","https://www.aliexpress.com"},{"canva","https://www.canva.com"}};
    var apps=new System.Collections.Generic.Dictionary<string,string>{{"calculator","calc.exe"},{"notepad","notepad.exe"},{"paint","mspaint.exe"},{"settings","ms-settings:"},{"explorer","explorer.exe"},{"task manager","taskmgr.exe"},{"word","winword.exe"},{"excel","excel.exe"}};
    if(urls.TryGetValue(item,out var u)){Open(u);answer="Opening "+item;}
    else if(apps.TryGetValue(item,out var exe)){Open(exe);answer="Opening "+item;}
    else if(item=="file"){var dialog=new Microsoft.Win32.OpenFileDialog();if(dialog.ShowDialog()==true){Open(dialog.FileName);answer="Opening selected file";}else answer="File selection cancelled";}
    else if(item=="mt5")answer="MT5 launch and trading integration are not available in this starter.";
    else answer="No action is configured for "+item;
   }else if(cmd is "hello" or "hi" or "how are you")answer="Hello, Boss Hammad. How can I help you today?";
   else answer="This command is not supported in the current starter.";
  }catch(Exception ex){answer="Unable to complete: "+ex.Message;}
  status.Text=answer;Say(answer);
 }
}
