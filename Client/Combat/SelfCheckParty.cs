using System.Text;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Combat;

/// <summary>
/// The party list 0x0092 the game sends when the player joins a group (the 2026-10-29 16:07 recording: Kayzia, bobow and the player, 629 bytes): each member's record
/// has a 36-character id string, then <c>17 05 | name length | name | class code u32</c>. The frame here is built on that layout with other names.
/// </summary>
public static class SelfCheckParty
{
    private static void Member(List<byte> body, string id, string name, uint classCode)
    {
        body.AddRange(new byte[] { 0x17, 0x05, 0xE7, 0x0C, 0x24 });
        body.AddRange(Encoding.ASCII.GetBytes(id));
        body.AddRange(new byte[] { 0xE1, 0x4E, 0x03, 0x00, 0x00, 0x00, 0x17, 0x05 });
        byte[] bytes = Encoding.UTF8.GetBytes(name);
        body.Add((byte)bytes.Length);
        body.AddRange(bytes);
        body.AddRange(BitConverter.GetBytes(classCode));
        body.AddRange(new byte[] { 0x98, 0xB8, 0x01, 0x98, 0xB8, 0x01, 0x12, 0x0C, 0x43, 0x72, 0x69, 0x6D, 0x73, 0x6F, 0x6E, 0x4F, 0x74, 0x74, 0x65, 0x72 });
    }

    public static bool Run()
    {
        Console.WriteLine("[selftest] Party list 0x0092 (the Group and PvP tabs know the group):");
        var frame = new List<byte> { 0x00, 0x92, 0x08, 0xFD, 0x1D, 0x0A, 0x00, 0xB2, 0xDF, 0xF7, 0x23, 0x04, 0x76, 0x03, 0x00, 0x00, 0x00, 0x17, 0x05, 0x05, 0x00, 0x00, 0x00, 0x02, 0x01 };
        Member(frame, "0D65598D-BDFB-49A2-BBBF-CBD0AD04C64A", "Aldric", 30);
        Member(frame, "078DEEAC-D570-4364-B11B-A18488FCBD01", "Maëlle", 14);
        Member(frame, "E76AF586-332C-4BB4-98C3-C9E41F623BAF", "Thorgrimm", 21);
        var entities = new Aion2EntityDirectory();
        var decoder = new Aion2FrameDecoder(Aion2Protocol.Load(), entities);
        DateTime t = new(2026, 10, 29, 16, 7, 0, DateTimeKind.Local);
        bool silent = !decoder.Decode(frame.ToArray(), t).Any();
        bool party = entities.PartyNames.SetEquals(new[] { "Aldric", "Maëlle", "Thorgrimm" });
        Console.WriteLine($"  -> the three members are the party (an accented name too), no damage event: {party && silent}");

        // a frame of that opcode with no member record changes nothing
        decoder.Decode(new byte[] { 0x00, 0x92, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 }, t.AddSeconds(5));
        bool kept = entities.PartyNames.Count == 3;
        Console.WriteLine($"  -> a frame without member records leaves the party as it was: {kept}");
        return party && silent && kept;
    }
}
