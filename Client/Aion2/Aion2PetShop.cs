using System.Buffers.Binary;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Aion2;

/// <summary>A pet the in-game pet shop (the Black Cloud shop) offers: on which tab (<see cref="Aion2PetShop.TabSpecial"/>,
/// <see cref="Aion2PetShop.TabPets"/>) and at what price (after the discount).</summary>
public sealed record Aion2ShopPet(int PetId, int Tab, long Price);

/// <summary>A weapon skin the shop offers (the appearance tab, sometimes the special one): its item, tab, price, and the weapon it dresses
/// (English, as the item name says it: "Orb", "Greatsword"...). A skin of the player's own weapon gives power when he does not have it yet
/// (Stacy, 2026-11-19); the game shows "En possession" itself, the meter cannot tell.</summary>
public sealed record Aion2ShopSkin(int ItemId, int Tab, long Price, string Weapon);

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
    private readonly Dictionary<int, (DateTime At, IReadOnlyList<Aion2ShopPet> Pets, IReadOnlyList<Aion2ShopSkin> Skins)> _tabs = new();
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
    public static bool TryParse(ReadOnlySpan<byte> frame, out int tab, out IReadOnlyList<Aion2ShopPet> pets) => TryParse(frame, out tab, out pets, out _);

    /// <summary>The weapons a skin can dress, as the item names say them ("... (Skin: Orb)").</summary>
    public static readonly string[] Weapons = ["Greatsword", "Longsword", "Dagger", "Bow", "Spellbook", "Orb", "Mace", "Staff"];

    /// <summary>The weapon a skin item dresses ("Orb"...), null for any other item (armour skins, pets...).</summary>
    public static string? WeaponOfSkin(int itemId)
    {
        string? name = Aion2ItemTable.NameOf(itemId, "en");
        if (name is null)
        {
            return null;
        }

        foreach (string weapon in Weapons)
        {
            if (name.Contains("(Skin: " + weapon + ")", StringComparison.Ordinal))
            {
                return weapon;
            }
        }

        return null;
    }

    /// <summary>The weapon of each class (the class names of <see cref="Aion2SkillNames.ClassFromCode"/>); null for one not known.</summary>
    public static string? WeaponOfClass(string? className) => className switch
    {
        "Gladiator" => "Greatsword",
        "Templar" => "Longsword",
        "Assassin" => "Dagger",
        "Ranger" => "Bow",
        "Sorcerer" => "Spellbook",
        "Elementalist" => "Orb",
        "Cleric" => "Mace",
        "Chanter" => "Staff",
        _ => null,
    };

    /// <summary>As the other TryParse, with the weapon skins of the tab too.</summary>
    public static bool TryParse(ReadOnlySpan<byte> frame, out int tab, out IReadOnlyList<Aion2ShopPet> pets, out IReadOnlyList<Aion2ShopSkin> skins)
    {
        tab = -1;
        pets = [];
        skins = [];
        if (frame.Length < 40)
        {
            return false;
        }

        tab = frame[2] switch { 0x65 => TabSpecial, 0x68 => TabPets, 0x67 => TabSkins, _ => -1 };
        var found = new List<Aion2ShopPet>();
        var foundSkins = new List<Aion2ShopSkin>();
        int offers = 0;
        for (int i = 3; i + 31 <= frame.Length; i++)
        {
            if (BinaryPrimitives.ReadUInt64LittleEndian(frame[(i + 4)..]) != 1 || frame[i + 13] != 0x5D)
            {
                continue;
            }

            offers++;
            int item = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(frame[i..]));
            long price = unchecked((long)BinaryPrimitives.ReadUInt64LittleEndian(frame[(i + 22)..]));
            if (Aion2PetCatalog.PetOfShopItem(item) is int pet && found.All(p => p.PetId != pet))
            {
                found.Add(new Aion2ShopPet(pet, tab, price));
            }
            else if (WeaponOfSkin(item) is string weapon && foundSkins.All(k => k.ItemId != item))
            {
                foundSkins.Add(new Aion2ShopSkin(item, tab, price, weapon));
            }

            i += 30;
        }

        pets = found;
        skins = foundSkins;
        return offers >= 1 && tab >= 0;
    }

    /// <summary>A shop tab arrived at <paramref name="at"/> (UTC) while the player stood at <paramref name="where"/> (null
    /// = not known yet: the next known place counts): replaces what the same tab offered before (an empty list, the
    /// skins tab, clears it) and opens the shop.</summary>
    public void NoteTab(int tab, IReadOnlyList<Aion2ShopPet> pets, DateTime at, (float X, float Y)? where, IReadOnlyList<Aion2ShopSkin>? skins = null)
    {
        lock (_gate)
        {
            _tabs[tab] = (at, pets.ToList(), (skins ?? []).ToList());
            _newest = _newest is { } newest && newest > at ? newest : at;
            _from = where;
            _walkedOff = false;
        }

        Changed?.Invoke();
    }

    /// <summary>The shop was seen closed (the game menu it lives in closed: Stacy, 2026-11-17, the list stayed while he stood still):
    /// closed until the next tab, as after a walk away.</summary>
    public void Close()
    {
        lock (_gate)
        {
            _walkedOff = true;
        }
    }

    /// <summary>
    /// The pets the open shop offers (special tab first, then the pet tab, each in the shop's order), as of
    /// <paramref name="now"/> (UTC) with the player at <paramref name="here"/>; empty while the shop is closed: no tab
    /// within <see cref="KeepFor"/>, or the player walked more than <see cref="WalkAway"/> from where he stood when the
    /// newest tab came (it stays closed when he comes back, until the next tab).
    /// </summary>
    public IReadOnlyList<Aion2ShopPet> OpenOffers(DateTime now, (float X, float Y)? here) =>
        OpenTabs(now, here).SelectMany(t => t.Pets).ToList();

    /// <summary>The weapon skins the open shop offers (as <see cref="OpenOffers"/>), empty while it is closed.</summary>
    public IReadOnlyList<Aion2ShopSkin> OpenSkins(DateTime now, (float X, float Y)? here) =>
        OpenTabs(now, here).SelectMany(t => t.Skins).ToList();

    private List<(DateTime At, IReadOnlyList<Aion2ShopPet> Pets, IReadOnlyList<Aion2ShopSkin> Skins)> OpenTabs(DateTime now, (float X, float Y)? here)
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
            return _tabs.Where(t => t.Value.At >= cutoff).OrderBy(t => t.Key).Select(t => t.Value).ToList();
        }
    }
}
