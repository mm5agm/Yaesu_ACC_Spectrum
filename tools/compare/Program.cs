// Records ACC Main+Sub raw rows and YWC SDR A/B frames at once.
// usage: dotnet bin/Debug/net10.0/compare-capture.dll <outPrefix> <seconds> [accPort]
// The ACC rows come straight from the board; the SDR frames come from a running
// YWC (localhost:8080) exactly as it sends them to its pages. Receive only.
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using YaesuAccScope;

string prefix = args[0]; double secs = double.Parse(args[1]);
string? port = args.Length > 2 ? args[2] : AccBoardFinder.Find();
if (port == null) { Console.Error.WriteLine("no ACC board"); return 1; }
var sw = System.Diagnostics.Stopwatch.StartNew(); File.WriteAllText(prefix + "-t0.txt", (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()/1000.0).ToString(System.Globalization.CultureInfo.InvariantCulture));
var accW = new BinaryWriter(File.Create(prefix + "-acc.bin"));
var sdrW = new Dictionary<string, BinaryWriter> { ["A"] = new(File.Create(prefix + "-sdrA.bin")), ["B"] = new(File.Create(prefix + "-sdrB.bin")) };
var lk = new object(); long accN = 0, aN = 0, bN = 0;

using var reader = new AccSerialReader(port);
reader.FrameReceived += (_, e) => {
    var f = e.Frame; double t = sw.Elapsed.TotalSeconds;
    lock (lk) {
        accW.Write(t);
        foreach (var r in new[] { f.Main, f.Sub }) {
            accW.Write(r.VfoHz); accW.Write(r.CentreHz); accW.Write(r.SpanHz); accW.Write(r.Levels);
        }
        accN++;
    }
};
reader.Failed += (_, ex) => Console.Error.WriteLine("ACC failed: " + ex.Message);

var hub = new HubConnectionBuilder().WithUrl("http://localhost:8080/radioHub").Build();
hub.On<JsonElement>("RadioStateUpdate", m => {
    if (m.GetProperty("property").GetString() != "SpectrumUpdate") return;
    var v = m.GetProperty("value"); string id = v.GetProperty("sdrId").GetString()!;
    if (!sdrW.TryGetValue(id, out var w)) return;
    var bins = v.GetProperty("bins"); int n = bins.GetArrayLength();
    double t = sw.Elapsed.TotalSeconds;
    lock (lk) {
        w.Write(t); w.Write(v.GetProperty("centreHz").GetDouble()); w.Write(v.GetProperty("spanHz").GetDouble());
        w.Write(v.TryGetProperty("rfOrdered", out var ro) && ro.GetBoolean()); w.Write(n);
        foreach (var b in bins.EnumerateArray()) w.Write(b.GetSingle());
        if (id == "A") aN++; else bN++;
    }
});
await hub.StartAsync();
reader.Start();
await Task.Delay(TimeSpan.FromSeconds(secs));
reader.Stop(); await hub.StopAsync();
lock (lk) { accW.Dispose(); foreach (var w in sdrW.Values) w.Dispose(); }
Console.WriteLine($"port {port}: acc {accN} frames, sdrA {aN}, sdrB {bN} in {secs}s; missed {reader.PacketsMissed}");
return 0;
