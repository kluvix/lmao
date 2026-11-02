using Pigless.CameraMod;
using Pigless.Interaction;
using Pigless.Sheesh;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

int assertions = 0;
void Check(bool value, string name) { assertions++; if (!value) throw new Exception("FAILED: " + name); }
var photo = new PhotoCountdown();
Check(photo.Start(10), "start photo");
Check(!photo.Start(11), "second press cannot reset countdown");
Check(photo.Remaining(10) == 2 && photo.Remaining(11.01) == 1, "two countdown ticks");
Check(!photo.Poll(11.999), "never capture early");
Check(photo.Poll(12), "capture at two seconds");
Check(!photo.Poll(12.01), "capture once");
photo.Start(20); photo.Cancel(); Check(!photo.Poll(22), "disable cancels pending capture");
Check(!photo.Start(double.NaN) && !photo.Start(double.PositiveInfinity), "reject invalid clock");
var touch = new TouchLatch();
Check(touch.Poll(1), "first touch fires");
Check(!touch.Poll(1) && !touch.Poll(2), "holding or sliding does not repeat");
Check(!touch.Poll(0) && touch.Poll(2), "withdraw then retouch fires");
touch.Reset(); Check(touch.Poll(2), "reopen resets contact");
var toggle = new DoublePressToggle();
Check(!toggle.Poll(true, false, 0), "arm toggle");
Check(!toggle.Poll(true, true, .1f), "single press cannot open");
Check(!toggle.Poll(true, true, .11f), "held press is not double press");
toggle.Poll(true, false, .2f);
Check(toggle.Poll(true, true, .3f), "double press opens");
toggle.Reset(); toggle.Poll(true, false, 1); toggle.Poll(true, true, 1.1f); toggle.Poll(true, false, 1.2f);
Check(!toggle.Poll(true, true, 1.7f), "late second press cannot open");
Check(CameraGrab.Select(GrabHand.None,true,true,.3f,.1f,.2f)==GrabHand.Right,"grab nearest in reach");
Check(CameraGrab.Select(GrabHand.Left,true,true,2,0,.2f)==GrabHand.Left,"held camera does not switch hands");
Check(CameraGrab.Select(GrabHand.None,true,false,float.NaN,0)==GrabHand.None,"invalid grab rejected");
foreach (var action in TabletControls.MainActions)
{
    var rect = TabletControls.GetRect(action);
    Check(TabletControls.HitTest(rect.X,rect.Y,false)==action, "main button " + action);
}
for(int page = 0; page < 2; page++)
foreach(var action in TabletControls.PageActions(page).Concat(TabletControls.SettingsCommonActions))
{
    var rect = TabletControls.GetRect(action);
    Check(TabletControls.HitTest(rect.X,rect.Y,true,page)==action,"settings button " + action);
}
Check(!Enum.GetNames<TabletAction>().Contains("Clip"),"no clip action");
Check(TabletControls.HitTest(0,0.026f,true)==TabletAction.None,"settings panel blocks underlying view");
CameraMath.GetResolution(1,0,4096,out int width,out int height);
Check(width==1920 && height==1080,"photo output retains 1080p");

// Inspect metadata, never execute game code or require Unity native libraries.
string gamePath = Path.Combine(Environment.GetEnvironmentVariable("PIGLESS_GAME_MANAGED_DIR") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../lib/game")), "Assembly-CSharp.dll");
using var pe = new PEReader(File.OpenRead(gamePath));
var metadata = pe.GetMetadataReader();
TypeDefinition FindType(string name) => metadata.GetTypeDefinition(metadata.TypeDefinitions.Single(h => metadata.GetString(metadata.GetTypeDefinition(h).Name)==name));
MethodDefinition Method(string type,string name, int parameters) => metadata.GetMethodDefinition(FindType(type).GetMethods().Single(h => {
    var m = metadata.GetMethodDefinition(h);
    return metadata.GetString(m.Name)==name && m.GetParameters().Count(p=>metadata.GetParameter(p).SequenceNumber>0)==parameters;
}));
Check((Method("BetterDayNightManager","SetTimeOfDay",2).Attributes & MethodAttributes.Public)!=0,"time setter signature present");
Check((Method("BetterDayNightManager","UpdateTimeOfDay",1).Attributes & MethodAttributes.Public)!=0,"time refresh signature present");
Check(FindType("BetterDayNightManager").GetProperties().Any(h=>metadata.GetString(metadata.GetPropertyDefinition(h).Name)=="timeOfDayRange"),"time preset count is a property");
Check((Method("GorillaTagger","TryToTag",6).Attributes & MethodAttributes.Private)!=0,"validated tag signature present");
Check((Method("GorillaTagManager","AddInfectedPlayer",2).Attributes & MethodAttributes.Public)!=0,"host infection signature present");
foreach(string field in new[]{"currentInfected","currentInfectedArray","isCurrentlyTag","waitingToStartNextInfectionGame"})
    Check(FindType("GorillaTagManager").GetFields().Any(h=>metadata.GetString(metadata.GetFieldDefinition(h).Name)==field), "tag field " + field);
Console.WriteLine($"PASS: {assertions} assertions (input, touch, photos, controls, game API metadata). No in-game behavior claimed.");
