using System.Numerics;

namespace Echo.Visuals.Cassette;

// Fixed pools (100 particles total). No per-frame particle objects or instance serialization.
internal sealed class CassetteParticles
{
    private sealed class Particle
    {
        public double X, Y, Vx, Vy, Age, Life = 1, Size = 1, Seed;
        public bool Alive;
        public int FrameOffset;
    }
    private readonly Particle[][] groups = [Pool(18), Pool(6), Pool(46), Pool(6), Pool(10), Pool(10), Pool(4)];
    private readonly Random random = new();
    private double meteorTimer = 1.2, flockTimer = 6, time;
    private int flockPhase;
    private static Particle[] Pool(int count) => Enumerable.Range(0, count).Select(_ => new Particle()).ToArray();
    private double Random(double min, double max) => min + random.NextDouble() * (max - min);
    public void Reset()
    {
        foreach (var list in groups) foreach (var p in list) p.Alive = false;
        meteorTimer = 1.2; flockTimer = 6;
    }
    public void Step(double dt, ReadOnlySpan<double> weights)
    {
        var step = Math.Clamp(dt, 0, .1); time += step;
        if (weights[2] > .01)
        {
            Spawn(0); Spawn(1); Spawn(2, 2); meteorTimer -= step;
            if (meteorTimer <= 0) { Spawn(3); meteorTimer = Random(.9,2.6); }
        }
        if (weights[1] > .01) { Spawn(4); Spawn(5); }
        flockTimer -= step;
        if (flockTimer <= 0 && (weights[0] > .01 || weights[1] > .01))
        { Flock(weights[0] >= weights[1] ? 0 : 1); flockTimer = Random(48,72); }
        foreach (var list in groups)
        foreach (var p in list)
        {
            if (!p.Alive) continue;
            p.Age += step; if (p.Age >= p.Life) { p.Alive = false; continue; }
            p.X += p.Vx * step; p.Y += p.Vy * step;
        }
        foreach (var p in groups[2])
        {
            if (!p.Alive) continue;
            var heading = p.Seed * 6.283 + time * .35 + Math.Sin(time * .7 + p.Seed * 9) * 1.4;
            var speed = 100 + p.Seed * 70;
            p.Vx += (Math.Cos(heading) * speed - p.Vx) * step; p.Vy += (Math.Sin(heading) * speed - p.Vy) * step;
        }
        foreach (var p in groups[3]) if (p.Alive) { p.Vx -= 50 * 9 * step * .2; p.Vy += 50 * 9 * step * .2; }
    }
    private void Spawn(int group, int maximum = 1)
    {
        var made = 0;
        foreach (var p in groups[group])
        {
            if (made >= maximum) return;
            if (p.Alive) continue;
            p.Alive = true; p.Age = p.Vx = p.Vy = 0; p.Seed = random.NextDouble(); made++;
            switch (group)
            {
                case 0: p.X = Random(0,3840); p.Y = Random(0,1500); p.Life = Random(4,8); p.Size = Random(150,180) * 1.5; break;
                case 1: p.X = Random(0,3840); p.Y = Random(0,1400); p.Life = Random(4,8); p.Size = Random(80,150) * 1.5; break;
                case 2: p.X = Random(0,3840); p.Y = Random(0,1700); p.Life = Random(5,7); p.Size = Random(14,18) * 1.5; break;
                case 3:
                    p.X = Random(1100,4600); p.Y = Random(-300,700); var speed = Random(100,250) * 9;
                    p.Vx = -speed * .7071; p.Vy = speed * .7071; p.Life = Random(.9,1.6); p.Size = Random(5,10) * 3; break;
                case 4: p.X = 3249 + Random(-205,205); p.Y = 1092 + Random(-110,110); p.Life = Random(3,20); p.Size = Random(70,90) * .4; break;
                case 5: p.X = 540 + Random(-102,102); p.Y = 1594 + Random(-60,60); p.Life = Random(3,20); p.Size = Random(70,90) * .2; break;
            }
        }
    }
    private void Flock(int phase)
    {
        flockPhase = phase; var direction = phase == 0 ? -1 : 1;
        var count = 1; for (var i = 0; i < 3; i++) if (random.NextDouble() < .5) count++;
        for (var i = 0; i < 4; i++)
        {
            var bird = groups[6][i]; bird.Alive = i < count; if (!bird.Alive) continue;
            bird.Age = 0; bird.Life = 60; bird.Seed = random.NextDouble(); bird.FrameOffset = (int)(random.NextDouble() * 16);
            bird.X = (phase == 0 ? 4100 : -260) - direction * i * Random(90,180);
            bird.Y = (phase == 0 ? 408 : 686) + Random(-120,120);
            bird.Vx = direction * Random(100,120); bird.Vy = Random(-10,10); bird.Size = Random(100,150);
        }
    }
    private static double Fade(Particle p)
    { var t = p.Age / p.Life; return Math.Min(1,t / .1) * Math.Min(1,(1 - t) / .1); }
    private static void Emit(CassetteScenePainter painter, int texture, CassetteLayout layout, double ratio,
        double x, double y, double w, double h, double rotation, Vector4 uv, Vector4 color, bool additive)
    {
        if (texture == 0) return;
        var scale = layout.Scale * ratio;
        painter.Particle(texture, (layout.SceneX + x * layout.Scale) * ratio, (layout.SceneY + y * layout.Scale) * ratio,
            w * scale, h * scale, rotation, uv, color, additive);
    }
    public void EmitStars(CassetteScenePainter painter, int texture, double weight, CassetteLayout layout, double ratio, int group)
    {
        foreach (var p in groups[group])
        {
            if (!p.Alive) continue;
            var twinkle = .2 + .8 * (.5 + .5 * Math.Sin(time * (1 + p.Seed * 2) * 6.283 + p.Seed * 40));
            var alpha = Fade(p) * twinkle * weight * (group == 2 ? .9 : .8);
            if (alpha <= .003) continue;
            Emit(painter, texture, layout, ratio, p.X, p.Y, p.Size, p.Size, 0, new(0,0,1,1), new(1,1,1,(float)alpha), true);
        }
    }
    public void EmitMeteors(CassetteScenePainter painter, int texture, double weight, CassetteLayout layout, double ratio, bool heads)
    {
        foreach (var p in groups[3])
        {
            if (!p.Alive) continue;
            var t = p.Age / p.Life; var alpha = Math.Min(1,t / .08) * (1 - t) * weight;
            var speed = Math.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy); var angle = Math.Atan2(p.Vy,p.Vx);
            if (heads) { Emit(painter,texture,layout,ratio,p.X,p.Y,p.Size*6,p.Size*6,0,new(0,0,1,1),new(.92f,.96f,1,(float)alpha),true); continue; }
            var length = Math.Max(40,speed * .4);
            Emit(painter,texture,layout,ratio,p.X-Math.Cos(angle)*length/2,p.Y-Math.Sin(angle)*length/2,length,p.Size*1.6,angle,new(0,0,1,1),new(.9f,.95f,1,(float)alpha),true);
        }
    }
    public void EmitFireflies(CassetteScenePainter painter, int texture, double weight, CassetteLayout layout, double ratio)
    {
        for (var group = 4; group <= 5; group++) foreach (var p in groups[group])
        {
            if (!p.Alive) continue;
            var wander = 15 * Math.Sin(time * (.3 + p.Seed * .7) + p.Seed * 20);
            var x = p.X + wander + Math.Sin(time * .5 + p.Seed * 7) * 30; var y = p.Y + Math.Cos(time * .43 + p.Seed * 5) * 22;
            var flicker = .7 + .3 * Math.Sin(time * (10 + p.Seed * 10) * 6.283 * .1 + p.Seed * 9);
            Emit(painter,texture,layout,ratio,x,y,p.Size*2.2,p.Size*2.2,0,new(0,0,1,1),new((float)((136+p.Seed*38)/255),1,(float)((80+p.Seed*26)/255),(float)(Fade(p)*flicker*weight)),true);
        }
    }
    public void EmitBirds(CassetteScenePainter painter, int texture, ReadOnlySpan<double> weights, CassetteLayout layout, double ratio)
    {
        foreach (var bird in groups[6])
        {
            if (!bird.Alive) continue;
            var frame = ((int)Math.Floor(time * 12) + bird.FrameOffset) % 16; var u0 = frame % 4 / 4f; var v0 = (float)Math.Floor(frame / 4d) / 4;
            var flip = flockPhase == 1; var bob = Math.Sin(time * (2 + bird.Seed * 2) + bird.Seed * 10) * 3 * (2 + bird.Seed * 2);
            Emit(painter,texture,layout,ratio,bird.X,bird.Y+bob,bird.Size,bird.Size,0,new(flip?u0+.25f:u0,v0,flip?u0:u0+.25f,v0+.25f),new(1,1,1,(float)(Math.Min(1,bird.Age/1.5)*weights[flockPhase])),false);
        }
    }
}
