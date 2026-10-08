namespace EvpLab;

/// <summary>Backgrounds a recorder captures and sounds that are not speech but get flagged like it.</summary>
internal static class Synth
{
    private const double BackgroundDbfs = -45;

    public static float[] Background(string kind, Random rng, double seconds)
    {
        var n = (int)(seconds * Audio.Rate);
        var s = new float[n];
        switch (kind)
        {
            case "room":   // pink noise, a faint mains hum, and a level that wanders as the room does
                Pink(rng, s);
                AddHum(s, 60, 0.08, rng);
                Wander(s, rng, 3);
                break;
            case "hiss":   // tape hiss: white noise tilted towards the top
                White(rng, s);
                var hp = Audio.Biquad.HighPass(2500);
                var tilted = (float[])s.Clone(); hp.Apply(tilted);
                for (var i = 0; i < n; i++) s[i] = 0.35f * s[i] + tilted[i];
                break;
            case "static": // white noise with crackle
                White(rng, s);
                Normalise(s, BackgroundDbfs);
                for (var k = 0; k < seconds * 6; k++)
                {
                    var at = rng.Next(n - 40);
                    var amp = (float)(0.02 + rng.NextDouble() * 0.06) * (rng.Next(2) == 0 ? 1 : -1);
                    for (var j = 0; j < 40; j++) s[at + j] += amp * MathF.Exp(-j / 6f);
                }
                return s;
            case "hvac":   // low rumble with hum harmonics
                Brown(rng, s);
                AddHum(s, 120, 0.15, rng);
                AddHum(s, 240, 0.05, rng);
                break;
            default: throw new ArgumentException(kind);
        }
        Normalise(s, BackgroundDbfs);
        return s;
    }

    public static (string Subtype, float[] Clip) Impostor(Random rng)
    {
        switch (rng.Next(4))
        {
            case 0: // three knocks: damped wood resonances
            {
                var s = new float[(int)(0.9 * Audio.Rate)];
                var f1 = 180 + rng.NextDouble() * 200; var f2 = f1 * 2.3;
                for (var k = 0; k < 3; k++)
                {
                    var at = (int)(k * 0.28 * Audio.Rate);
                    for (var j = 0; at + j < s.Length && j < Audio.Rate / 8; j++)
                    {
                        var t = (double)j / Audio.Rate;
                        s[at + j] += (float)(Math.Exp(-t * 40) * (Math.Sin(2 * Math.PI * f1 * t) + 0.5 * Math.Sin(2 * Math.PI * f2 * t)));
                    }
                }
                return ("knocks", s);
            }
            case 1: // footsteps: low thumps
            {
                var s = new float[(int)(2.2 * Audio.Rate)];
                for (var k = 0; k < 4; k++)
                {
                    var at = (int)(k * 0.55 * Audio.Rate);
                    var lp = Audio.Biquad.LowPass(250);
                    for (var j = 0; j < Audio.Rate / 10; j++)
                        s[at + j] += lp.Next((float)((rng.NextDouble() * 2 - 1) * Math.Exp(-j / 400.0)));
                }
                return ("footsteps", s);
            }
            case 2: // a creak: a wavering tone, the impostor most like a voice
            {
                var len = (int)((0.5 + rng.NextDouble() * 0.6) * Audio.Rate);
                var s = new float[len];
                double phase = 0; var f0 = 350 + rng.NextDouble() * 250;
                for (var j = 0; j < len; j++)
                {
                    var t = (double)j / len;
                    var f = f0 * (1 + 0.6 * t) + 20 * Math.Sin(2 * Math.PI * 9 * t);
                    phase += 2 * Math.PI * f / Audio.Rate;
                    var env = Math.Sin(Math.PI * t);
                    s[j] = (float)(env * (Math.Sin(phase) + 0.4 * Math.Sin(2 * phase) + 0.2 * Math.Sin(3 * phase)));
                }
                return ("creak", s);
            }
            default: // a door or bump: a broadband thump with a boom
            {
                var len = (int)(0.6 * Audio.Rate);
                var s = new float[len];
                var lp = Audio.Biquad.LowPass(900);
                for (var j = 0; j < len; j++)
                {
                    var t = (double)j / Audio.Rate;
                    s[j] = lp.Next((float)((rng.NextDouble() * 2 - 1) * Math.Exp(-t * 18))) + (float)(0.6 * Math.Exp(-t * 10) * Math.Sin(2 * Math.PI * 70 * t));
                }
                return ("thump", s);
            }
        }
    }

    private static void White(Random rng, float[] s) { for (var i = 0; i < s.Length; i++) s[i] = (float)(rng.NextDouble() * 2 - 1); }

    private static void Pink(Random rng, float[] s)
    {
        double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var w = rng.NextDouble() * 2 - 1;
            b0 = 0.99886 * b0 + w * 0.0555179; b1 = 0.99332 * b1 + w * 0.0750759; b2 = 0.96900 * b2 + w * 0.1538520;
            b3 = 0.86650 * b3 + w * 0.3104856; b4 = 0.55000 * b4 + w * 0.5329522; b5 = -0.7616 * b5 - w * 0.0168980;
            s[i] = (float)(b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362); b6 = w * 0.115926;
        }
    }

    private static void Brown(Random rng, float[] s)
    {
        double last = 0;
        for (var i = 0; i < s.Length; i++) { last = (last + 0.02 * (rng.NextDouble() * 2 - 1)) / 1.02; s[i] = (float)last; }
    }

    private static void AddHum(float[] s, double hz, double relative, Random rng)
    {
        var rms = Audio.Rms(s);
        var phase = rng.NextDouble() * Math.PI * 2;
        for (var i = 0; i < s.Length; i++) s[i] += (float)(rms * relative * Math.Sqrt(2) * Math.Sin(phase + 2 * Math.PI * hz * i / Audio.Rate));
    }

    private static void Wander(float[] s, Random rng, double depthDb)
    {
        var rate = 0.05 + rng.NextDouble() * 0.1; var phase = rng.NextDouble() * Math.PI * 2;
        for (var i = 0; i < s.Length; i++) s[i] *= (float)Math.Pow(10, depthDb * Math.Sin(phase + 2 * Math.PI * rate * i / Audio.Rate) / 20);
    }

    private static void Normalise(float[] s, double dbfs)
    {
        var gain = (float)(Math.Pow(10, dbfs / 20) / Math.Max(Audio.Rms(s), 1e-9));
        for (var i = 0; i < s.Length; i++) s[i] *= gain;
    }
}
