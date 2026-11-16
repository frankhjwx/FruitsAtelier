using FruitsAtelier.Core;
using FruitsAtelier.Mac;

static class MacAuditionTests
{
    internal static async Task Run(string wave)
    {
        using var player = new MacHitsoundPlayer(muted: true);
        var sound = new Hitsound(CatchObjectKind.Fruit, wave, .5f);
        await player.PrepareAndPlayAsync(sound);
        await Task.Delay(80);
        Check(player.SampleLoads == 1 && player.ActiveVoices == 1, "Uncached panel audition prepares and plays");
        player.Stop();
        await Task.WhenAll(player.PrepareAndPlayAsync(sound), player.PrepareAndPlayAsync(sound));
        await Task.Delay(80);
        Check(player.ActiveVoices == 2 && player.SampleLoads == 1, "Layered timing/testplay audition reuses samples without cutting off other layers");
        player.Stop();
        var pending = player.PrepareAndPlayAsync(sound);
        player.Stop(); await pending; await Task.Delay(80);
        Check(player.ActiveVoices == 0, "Stop cancels pending and active panel auditions");
        double duration = await player.PlayExclusiveAuditionAsync(sound, CancellationToken.None);
        await Task.Delay(80);
        Check(duration > 0 && player.ActiveVoices == 1, "Precise/library audition starts exclusively");
        await player.PlayExclusiveAuditionAsync(sound, CancellationToken.None);
        await Task.Delay(80);
        Check(player.ActiveVoices == 1, "A new library audition replaces its previous voice");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { await player.PlayExclusiveAuditionAsync(sound, cancellation.Token); throw new Exception("Canceled audition was accepted"); }
        catch (OperationCanceledException) { }
        await Task.Delay(80);
        Check(player.ActiveVoices == 0, "Canceled library audition leaves no voice");
        await player.PrepareAndPlayAsync(sound with { FilePath = wave + ".missing" });
        Check(player.ActiveVoices == 0, "Missing audition resources remain silent");
        static void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }
    }
}
