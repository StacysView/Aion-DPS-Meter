using System.Buffers.Binary;
using AionDPS.Aion2;

namespace AionDPS.Combat;

/// <summary>
/// The Hidden Cube detector's key gate: the keys counted in the bag from the login frame (0x1156) and the changes (0x1B56), the storages left out, an unknown bag
/// (no login frame seen) not taken for an empty one, and the rule that switches the detector. The frame layout is the one read on the recordings of
/// 2026-10-13 to 17 (36, 37, 34, 33 and 32 keys "Key: Hidden Cube (Bound)" at the five logins).
/// </summary>
public static class SelfCheckCubeKeys
{
    private const int Bound = 630900003, Plain = 610900001, Kinah = 930100031;

    /// <summary>A frame as the game sends it: opcode, then a record per item (kind byte, 9 bytes, item u32, quantity u64, container, slot, 12 bytes).</summary>
    private static byte[] Frame(byte opcodeHigh, int minimumLength, params (byte Kind, byte Container, byte Slot, int Item, long Quantity)[] records)
    {
        var body = new List<byte> { (byte)records.Length };
        foreach (var r in records)
        {
            body.Add(r.Kind);
            body.AddRange(new byte[9]);
            var item = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(item, (uint)r.Item);
            body.AddRange(item);
            var quantity = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(quantity, (ulong)r.Quantity);
            body.AddRange(quantity);
            body.Add(r.Container);
            body.Add(r.Slot);
            body.AddRange(new byte[12]);
        }

        while (body.Count + 2 < minimumLength)
        {
            body.Add(0);
        }

        var frame = new List<byte> { opcodeHigh, 0x56 };
        frame.AddRange(body);
        return frame.ToArray();
    }

    public static bool Run()
    {
        Console.WriteLine("[selftest] Hidden Cube keys (the detector's gate):");
        DateTime t = new(2026, 10, 27, 21, 0, 0, DateTimeKind.Local);
        Aion2Stock.Reset();

        bool unknown = Aion2Stock.HiddenCubeKeys() is null && Aion2HiddenCubes.DetectorActive(null, false);
        Console.WriteLine($"  -> before the login frame the bag is unknown, not empty, and the detector runs: {unknown}");

        // login: 3 bound keys and 2 plain ones in the bag, 5 more in the character storage (those open nothing)
        Aion2Stock.NoteSnapshot(Frame(0x11, 6000, (1, 1, 0, Bound, 3), (1, 1, 1, Plain, 2), (1, 1, 2, Kinah, 1000), (1, 15, 0, Bound, 5)), t);
        bool login = Aion2Stock.HiddenCubeKeys() == 5;
        Console.WriteLine($"  -> the login frame counts the keys of the bag only (3 + 2, not the 5 of the storage): {login}");

        Aion2Stock.NoteChange(Frame(0x1b, 0, (1, 1, 0, Bound, 2)), t.AddSeconds(30));
        bool used = Aion2Stock.HiddenCubeKeys() == 4;
        Aion2Stock.NoteChange(Frame(0x1b, 0, (2, 1, 0, Bound, 0), (2, 1, 1, Plain, 0)), t.AddSeconds(60));
        bool none = Aion2Stock.HiddenCubeKeys() == 0;
        Console.WriteLine($"  -> opening a cube lowers the count, the last key gone makes it 0 (4 then {Aion2Stock.HiddenCubeKeys()}): {used && none}");

        Aion2Stock.NoteChange(Frame(0x1b, 0, (0, 1, 3, Plain, 1)), t.AddSeconds(90));
        bool picked = Aion2Stock.HiddenCubeKeys() == 1;
        Console.WriteLine($"  -> a key picked up counts again: {picked}");

        bool gate = Aion2HiddenCubes.DetectorActive(3, false) && !Aion2HiddenCubes.DetectorActive(0, false) && Aion2HiddenCubes.DetectorActive(0, true)
            && Aion2HiddenCubes.DetectorActive(null, false) && Aion2HiddenCubes.DetectorActive(null, true);
        Console.WriteLine($"  -> the detector runs with a key, is silent with none, runs when forced on or when the bag is unknown: {gate}");

        Aion2Stock.Reset();
        return unknown && login && used && none && picked && gate;
    }
}
