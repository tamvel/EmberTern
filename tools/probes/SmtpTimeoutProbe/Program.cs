using System.Diagnostics;
using System.Net.Mail;

// ── The subject ─────────────────────────────────────────────────────────────────────────────────────
//
// 10.255.255.1 is a private address with nothing behind it: a SYN sent there is DROPPED rather than
// refused, which is exactly the shape of a wrong SMTP host typed into the settings window. A refused
// connection returns instantly and would prove nothing.
const string BlackHole = "10.255.255.1";
const int Port = 587;
const int Timeout = 3_000;

Console.WriteLine($"SmtpClient.Timeout = {Timeout} ms, host = {BlackHole}:{Port}\n");

await Measure("SendMailAsync(mail)                    ", (client, mail, _) => client.SendMailAsync(mail));
await Measure("SendMailAsync(mail, token) + 3 s token ", (client, mail, token) =>
    client.SendMailAsync(mail, token));

Console.WriteLine("\nWhat to read off this:");
Console.WriteLine("  · elapsed ≈ Timeout  → SmtpClient.Timeout bounds the async path.");
Console.WriteLine("  · elapsed ≫ Timeout  → it does NOT, and the caller must bound it.");
Console.WriteLine("  · row 2 ≈ 3 s        → the CancellationToken overload can interrupt a dead connect.");

static async Task Measure(string label, Func<SmtpClient, MailMessage, CancellationToken, Task> send)
{
    using var client = new SmtpClient(BlackHole, Port)
    {
        DeliveryMethod = SmtpDeliveryMethod.Network,
        EnableSsl = true,
        Timeout = Timeout,
    };

    client.UseDefaultCredentials = false;

    using var mail = new MailMessage("probe@example.test", "probe@example.test", "probe", "probe");
    using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(Timeout));

    var watch = Stopwatch.StartNew();
    string outcome;

    try
    {
        await send(client, mail, cancel.Token);
        outcome = "returned OK (!)";
    }
    catch (Exception e)
    {
        outcome = $"{e.GetType().Name}: {e.Message}";

        if (e.InnerException is { } inner)
        {
            outcome += $" ⤷ {inner.GetType().Name}: {inner.Message}";
        }
    }

    watch.Stop();
    Console.WriteLine($"{label} → {watch.ElapsedMilliseconds,6} ms   {outcome}");
}
