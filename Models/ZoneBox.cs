using SwiftlyS2.Shared.Natives;

namespace Bolgeler.Models;

/// <summary>Eksen hizali kutu matematigi.</summary>
public readonly struct ZoneBox
{
    public const float MinSize = 16f;

    public Vector Mins { get; }
    public Vector Maxs { get; }

    public ZoneBox(Vector mins, Vector maxs)
    {
        Mins = new Vector(MathF.Min(mins.X, maxs.X), MathF.Min(mins.Y, maxs.Y), MathF.Min(mins.Z, maxs.Z));
        Maxs = new Vector(MathF.Max(mins.X, maxs.X), MathF.Max(mins.Y, maxs.Y), MathF.Max(mins.Z, maxs.Z));
    }

    /// <summary>
    /// Mouse ile secilen iki koseden kutu uretir. Kutunun yuksekligi en az <paramref name="defaultHeight"/>
    /// olur; eger 2. kose daha yuksek bir duvara/noktaya nisanlanmissa o yuksekligi de kapsar.
    /// </summary>
    public static ZoneBox FromCorners(Vector a, Vector b, float defaultHeight)
    {
        float minX = MathF.Min(a.X, b.X), maxX = MathF.Max(a.X, b.X);
        float minY = MathF.Min(a.Y, b.Y), maxY = MathF.Max(a.Y, b.Y);
        float minZ = MathF.Min(a.Z, b.Z), maxZ = MathF.Max(a.Z, b.Z);

        if (maxX - minX < MinSize) { var p = (MinSize - (maxX - minX)) / 2f; minX -= p; maxX += p; }
        if (maxY - minY < MinSize) { var p = (MinSize - (maxY - minY)) / 2f; minY -= p; maxY += p; }

        float targetHeight = MathF.Max(defaultHeight, MinSize);
        if (maxZ - minZ < targetHeight)
        {
            maxZ = minZ + targetHeight;
        }

        return new ZoneBox(new Vector(minX, minY, minZ), new Vector(maxX, maxY, maxZ));
    }

    public static ZoneBox FromArrays(float[] mins, float[] maxs)
        => new(new Vector(mins[0], mins[1], mins[2]), new Vector(maxs[0], maxs[1], maxs[2]));

    public float[] MinsArray() => new[] { Mins.X, Mins.Y, Mins.Z };
    public float[] MaxsArray() => new[] { Maxs.X, Maxs.Y, Maxs.Z };

    public Vector Center => new((Mins.X + Maxs.X) / 2f, (Mins.Y + Maxs.Y) / 2f, (Mins.Z + Maxs.Z) / 2f);
    public Vector Size => new(Maxs.X - Mins.X, Maxs.Y - Mins.Y, Maxs.Z - Mins.Z);

    /// <summary>
    /// Oyuncu ayak pozisyonu (AbsOrigin) icin govde (hull) ve zemin toleransli kontrol.
    /// CS2 oyuncu govdesi ~72 birim yuksekliginde ve ~32 birim genisligindedir.
    /// </summary>
    public bool ContainsPlayer(Vector feet, float horizontalPad = 12f)
    {
        return feet.X + horizontalPad >= Mins.X && feet.X - horizontalPad <= Maxs.X
            && feet.Y + horizontalPad >= Mins.Y && feet.Y - horizontalPad <= Maxs.Y
            && feet.Z + 64f >= Mins.Z && feet.Z - 8f <= Maxs.Z;
    }

    public ZoneBox WithSize(float x, float y, float z)
    {
        var c = Center;
        x = MathF.Max(x, MinSize); y = MathF.Max(y, MinSize); z = MathF.Max(z, MinSize);
        // Taban sabit kalsin (yukseklik degisince bolge yere gomulmesin).
        return new ZoneBox(
            new Vector(c.X - x / 2f, c.Y - y / 2f, Mins.Z),
            new Vector(c.X + x / 2f, c.Y + y / 2f, Mins.Z + z));
    }

    public ZoneBox Offset(float dx, float dy, float dz)
        => new(new Vector(Mins.X + dx, Mins.Y + dy, Mins.Z + dz), new Vector(Maxs.X + dx, Maxs.Y + dy, Maxs.Z + dz));

    /// <summary>Bir noktanin kutudan en kisa yoldan cikacagi yatay yon.</summary>
    public Vector OutwardDirection(Vector pos)
    {
        float best = MathF.Abs(pos.X - Mins.X);
        var dir = new Vector(-1f, 0f, 0f);

        float distMaxX = MathF.Abs(Maxs.X - pos.X);
        if (distMaxX < best) { best = distMaxX; dir = new Vector(1f, 0f, 0f); }

        float distMinY = MathF.Abs(pos.Y - Mins.Y);
        if (distMinY < best) { best = distMinY; dir = new Vector(0f, -1f, 0f); }

        float distMaxY = MathF.Abs(Maxs.Y - pos.Y);
        if (distMaxY < best) { dir = new Vector(0f, 1f, 0f); }

        return dir;
    }

    /// <summary>
    /// Giris engelli bolgeye giren oyuncuyu en yakin kenarin disina cikaracak guvenli hedef noktayi hesaplar.
    /// </summary>
    public Vector GetPushOutTarget(Vector pos, float margin = 20f)
    {
        var dir = OutwardDirection(pos);
        if (dir.X < -0.5f) return new Vector(Mins.X - margin, pos.Y, pos.Z);
        if (dir.X > 0.5f) return new Vector(Maxs.X + margin, pos.Y, pos.Z);
        if (dir.Y < -0.5f) return new Vector(pos.X, Mins.Y - margin, pos.Z);
        return new Vector(pos.X, Maxs.Y + margin, pos.Z);
    }

    public Vector[] Corners()
    {
        var r = new Vector[8];
        for (int i = 0; i < 8; i++)
        {
            r[i] = new Vector(
                (i & 1) == 0 ? Mins.X : Maxs.X,
                (i & 2) == 0 ? Mins.Y : Maxs.Y,
                (i & 4) == 0 ? Mins.Z : Maxs.Z);
        }
        return r;
    }

    /// <summary>12 kenarin kose indeksleri.</summary>
    public static readonly (int A, int B)[] Edges =
    {
        (0, 1), (0, 2), (3, 1), (3, 2),
        (4, 5), (4, 6), (7, 5), (7, 6),
        (0, 4), (1, 5), (2, 6), (3, 7),
    };
}

