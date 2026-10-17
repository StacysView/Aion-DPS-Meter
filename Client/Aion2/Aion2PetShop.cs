using System.Buffers.Binary;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Aion2;

/// <summary>A pet the in-game pet shop (the Black Cloud shop) offers: on which tab (<see cref="Aion2PetShop.TabSpecial"/>,
/// <see cref="Aion2PetShop.TabPets"/>) and at what price (after the discount).</summary>
public sealed record Aion2ShopPet(int PetId, int Tab, long Price);

/// <summary>
/// The in-game pet shop while the player has it open: the pets its tabs offer. The game sends a tab (<c>0x9156</c>) when the
/// shop is opened or a tab is switched, and nothing when it is closed, so the shop counts as open from its newest tab
/// until the player walks more than 3 m away from where he stood then (or five minutes have gone by); a new tab opens it
/// again. The pet panels over the game then hide the map and list only these pets (Stacy, 2026-10-17). Layout, tab bytes
/// and the "walked off = closed" rule by SkeeveTV (MIT; his releases 0.32.0, 1.1.2 and 1.3.1). Thread-safe: written on the
/// capture thread, read by the panels on the UI thread; <see cref="Changed"/> is raised outside the lock.
/// </summary>
public sealed class Aion2PetShop
{
    public const int TabSpecial = 0, TabPets = 1, TabSkins = 2;

    /// <summary>A tab older than this is forgotten (the shop renews its offers every hour; a closed shop sends nothing).</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(5);

    /// <summary>How far (game units, cm) the player may move from where he stood when the newest tab came before the
    /// shop counts as closed: 3 m.</summary>
    public const float WalkAway = 300;

    private readonly object _gate = new();
    private readonly Dictionary<int, (DateTime At, IReadOnlyList<Aion2ShopPet> Pets)> _tabs = new();
    private DateTime? _newest;
    private (float X, float Y)? _from;
    private bool _walkedOff;

    /// <summary>Raised after a shop tab was noted (the shop was opened, or a tab of it).</summary>
    public event Action? Changed;

    /// <summary>
    /// Reads a shop tab frame: <c>opcode | tab u8 | ... | offers</c>, an offer being <c>offer id u32, item id u32, quantity
    /// u64 (1), discount class u8, 0x5d, price before u64, price after u64</c>. Tab byte 0x65 = special, 0x68 = pets, 0x67 =
    /// skins. The offers are found by their shape (quantity 1 then 0x5d); only the items that stand for a pet count
    /// (<see cref="Aion2PetCatalog.PetOfShopItem"/>), each pet once. False when the frame holds no offer or an unknown tab.
    /// SkeeveTV's DecodeShop (MIT), unchanged.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> frame, out int tab, out IReadOnlyList<Aion2ShopPet> pets)
    {
        tab = -1;
        pets = [];
        if (frame.Length < 40)
        {
            return false;
        }

        tab = frame[2] switch { 0x65 => TabSpecial, 0x68 => TabPets, 0x67 => TabSkins, _ => -1 };
        var found = new List<Aion2ShopPet>();
        int offers = 0;
        for (int i = 3; i + 31 <= frame.Length; i++)
        {
            if (BinaryPrimitives.ReadUInt64LittleEndian(frame[(i + 4)..]) != 1 || frame[i + 13] != 0x5D)
            {
                continue;
            }

            offers++;
            if (Aion2PetCatalog.PetOfShopItem(unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[i..]))) is int pet && found.All(p => p.PetId != pet))
            {
                found.Add(new Aion2ShopPet(pet, tab, unchecked((long)BinaryPrimitives.ReadUInt64LittleEndian(frame[(i + 22)..]))));
            }

            i += 30;
        }

        pets = found;
        return offers >= 1 && tab >= 0;
    }

    /// <summary>A shop tab arrived at <paramref name="at"/> (UTC) while the player stood at <paramref name="where"/> (null
    /// = not known yet: the next known place counts): replaces what the same tab offered before (an empty list, the
    /// skins tab, clears it) and opens the shop.</summary>
    public void NoteTab(int tab, IReadOnlyList<Aion2ShopPet> pets, DateTime at, (float X, float Y)? where)
    {
        lock (_gate)
        {
            _tabs[tab] = (at, pets.ToList());
            _newest = _newest is { } newest && newest > at ? newest : at;
            _from = where;
            _walkedOff = false;
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// The pets the open shop offers (special tab first, then the pet tab, each in the shop's order), as of
    /// <paramref name="now"/> (UTC) with the player at <paramref name="here"/>; empty while the shop is closed: no tab
    /// within <see cref="KeepFor"/>, or the player walked more than <see cref="WalkAway"/> from where he stood when the
    /// newest tab came (it stays closed when he comes back, until the next tab).
    /// </summary>
    public IReadOnlyList<Aion2ShopPet> OpenOffers(DateTime now, (float X, float Y)? here)
    {
        lock (_gate)
        {
            if (_newest is not { } newest || now - newest > KeepFor)
            {
                return [];
            }

            if (here is { } at)
            {
                if (_from is not { } from)
                {
                    _from = at;
                }
                else if (!_walkedOff && Math.Sqrt((at.X - from.X) * (double)(at.X - from.X) + (at.Y - from.Y) * (double)(at.Y - from.Y)) > WalkAway)
                {
                    _walkedOff = true; // walked off: the shop is closed
                }
            }

            if (_walkedOff)
            {
                return [];
            }

            var cutoff = now - KeepFor;
            return _tabs.Where(t => t.Value.At >= cutoff).OrderBy(t => t.Key).SelectMany(t => t.Value.Pets).ToList();
        }
    }
}
