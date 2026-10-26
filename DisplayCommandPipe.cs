using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SoundSwitchQuick;

internal sealed class DisplayCommandPipe : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private static string Name => "SoundSwitchQuick.Display." + Environment.UserName + "." + System.Diagnostics.Process.GetCurrentProcess().SessionId;
    public DisplayCommandPipe(Dispatcher dispatcher, Action<string[]> handler)
    {
        _ = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(_stop.Token);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    using var reader = new StreamReader(pipe, leaveOpen: true);
                    var line = await reader.ReadLineAsync(timeout.Token);
                    if (line == null || line.Length > 32768) continue;
                    var args = JsonSerializer.Deserialize<string[]>(line) ?? Array.Empty<string>();
                    // Acknowledge only after queuing the command on the UI dispatcher.
                    _ = dispatcher.BeginInvoke(new Action(() => handler(args)));
                    using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                    await writer.WriteLineAsync("OK");
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { DisplayRouter.Log("IPC: " + ex.Message); }
            }
        });
    }
    public static void Send(string[] args)
    {
        using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
        pipe.Connect(5000);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        writer.WriteLine(JsonSerializer.Serialize(args));
        using var reader = new StreamReader(pipe);
        var acknowledgement = reader.ReadLineAsync();
        if (!acknowledgement.Wait(5000) || acknowledgement.Result != "OK") throw new TimeoutException("Виджет не подтвердил команду запуска.");
    }
    public void Dispose() { _stop.Cancel(); }
}
