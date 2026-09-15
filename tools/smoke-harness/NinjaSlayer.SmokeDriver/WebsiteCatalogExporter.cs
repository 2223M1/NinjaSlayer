using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Cards.RedesignV1;
using NinjaSlayer.Cards;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.SmokeDriver;

// Runs in the installed candidate's main menu. The website never reads test card specifications.
internal static class WebsiteCatalogExporter
{
    internal static void Export(string destination)
    {
        if (DirAccess.DirExistsAbsolute("res://Website"))
            throw new InvalidOperationException("Website resources must not be shipped in the game pack.");
        Directory.CreateDirectory(destination);
        string assets = Path.Combine(destination, "images");
        Directory.CreateDirectory(assets);
        Assembly product = typeof(NinjaSlayerCharacter).Assembly;
        string version = System.Environment.GetEnvironmentVariable("NINJASLAYER_CATALOG_VERSION")
            ?? throw new InvalidOperationException("Catalog export requires the candidate package version.");
        var cards = ModelDb.AllCards.Concat(new CardModel[] {
            ModelDb.Card<ChadoEnergyRedesignV1>(), ModelDb.Card<StraightKiRedesignV1>(),
            ModelDb.Card<BlackFlameRedesignV1>(), ModelDb.Card<StrongShurikenTokenRedesignV1>(), ModelDb.Card<BusyLine>(),
            ModelDb.Card<SawatariMachete>()
        }).DistinctBy(card => card.Id).ToArray();
        var models = cards.Cast<AbstractModel>().Concat(ModelDb.AllRelics).Concat(ModelDb.AllPotions)
            .Concat(ModelDb.Monsters).Concat(ModelDb.AllEvents).Concat(ModelDb.AllAncients).Concat(ModelDb.AllEncounters).Concat(ModelDb.AllPowers)
            .Concat(ModelDb.AllCharacters).Concat(ModelDb.Orbs).Append(ModelDb.Orb<NinjaSlayer.Orbs.ShurikenOrb>())
            .Concat(ModelDb.AllAbstractModelSubtypes.Where(type => type.Assembly == product && type.IsSubclassOf(typeof(EncounterModel)))
                .Select(type => ModelDb.GetById<EncounterModel>(ModelDb.GetId(type))))
            .DistinctBy(model => model.Id).ToArray();
        var translations = new Dictionary<string, object>();
        var labels = new Dictionary<string, object>();
        string originalLanguage = LocManager.Instance.Language;
        try
        {
            foreach (string language in new[] { "zhs", "eng" })
            {
                LocManager.Instance.SetLanguage(language);
                translations[language] = models.Select(model => Describe(model, assets)).ToArray();
                var events = LocManager.Instance.GetTable("events");
                labels[language] = events.Keys.Where(key => key.EndsWith(".title", StringComparison.Ordinal))
                    .ToDictionary(key => key, key => events.GetRawText(key));
            }
        }
        finally { LocManager.Instance.SetLanguage(originalLanguage); }
        var catalog = new
        {
            schemaVersion = 1, version,
            sourceRevision = product.GetCustomAttributes<AssemblyMetadataAttribute>().Single(item => item.Key == "NinjaSlayerSourceRevision").Value,
            dllSha256 = Convert.ToHexStringLower(SHA256.HashData(System.IO.File.ReadAllBytes(product.Location))),
            hostMvid = typeof(CardModel).Assembly.ManifestModule.ModuleVersionId,
            languages = translations, labels
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(catalog, new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllBytes(Path.Combine(destination, "catalog.json"), bytes);
        System.IO.File.WriteAllText(Path.Combine(destination, "fingerprint.txt"), Convert.ToHexStringLower(SHA256.HashData(bytes)) + "\n");
        GD.Print($"Website catalog exported: {cards.Length} cards, {models.Length} models, candidate {version}.");
    }

    private static object Describe(AbstractModel model, string assets)
    {
        return model switch
        {
            CardModel card => new
            {
                id = card.Id.ToString(), kind = "card", mod = card is ModCardTemplate,
                rarity = card.Rarity.ToString(), type = card.Type.ToString(),
                image = SaveImage(card.Portrait, assets, 512), thumbnail = SaveImage(card.Portrait, assets, 96),
                imagePath = card.PortraitPath,
                variants = card.MaxUpgradeLevel > 0 ? new[] { CardVariant(card, false), CardVariant(card, true) } : new[] { CardVariant(card, false) }
            },
            RelicModel relic => new { id = relic.Id.ToString(), kind = "relic", name = relic.Title.GetFormattedText(),
                description = relic.DynamicDescription.GetFormattedText(), image = SaveImage(relic.Icon, assets, 256),
                tips = Tips(relic.HoverTipsExcludingRelic) },
            PotionModel potion => new { id = potion.Id.ToString(), kind = "potion", name = potion.Title.GetFormattedText(),
                description = potion.DynamicDescription.GetFormattedText(), image = SaveImage(potion.Image, assets, 256), tips = Tips(potion.ExtraHoverTips) },
            MonsterModel monster => new { id = monster.Id.ToString(), kind = "enemy", name = monster.Title.GetFormattedText() },
            PowerModel power => new { id = power.Id.ToString(), kind = "power", name = power.Title.GetFormattedText() },
            CharacterModel character => new { id = character.Id.ToString(), kind = "character", name = character.Title.GetFormattedText() },
            OrbModel orb => new { id = orb.Id.ToString(), kind = "orb", name = orb.Title.GetFormattedText(), description = orb.Description.GetFormattedText() },
            EncounterModel encounter => new { id = encounter.Id.ToString(), kind = "encounter", name = encounter.Title.GetFormattedText(),
                monsters = encounter.AllPossibleMonsters.Select(monster => monster.Id.ToString()).ToArray() },
            EventModel evt => new { id = evt.Id.ToString(), kind = "event", name = evt.Title.GetFormattedText(),
                image = evt.LayoutType == MegaCrit.Sts2.Core.Events.EventLayoutType.Default ? SaveImage(evt.CreateInitialPortrait(), assets, 512) : null },
            _ => throw new InvalidOperationException($"Unsupported catalog model: {model.Id}")
        };
    }

    private static object CardVariant(CardModel canonical, bool upgraded)
    {
        CardModel card = canonical.ToMutable();
        if (upgraded && card.MaxUpgradeLevel > 0) card.UpgradeInternal();
        return new { name = card.Title, upgraded, cost = card.EnergyCost.GetWithModifiers(CostModifiers.Local),
            costsX = card.EnergyCost.CostsX, description = card.GetDescriptionForPile(PileType.None),
            keywords = card.Keywords.Select(keyword => keyword.ToString()).ToArray(), tips = Tips(card.HoverTips) };
    }

    private static object[] Tips(IEnumerable<IHoverTip> tips) => tips.Select(tip => tip switch
    {
        CardHoverTip card => (object)new { card = card.Card.Id.ToString(), upgraded = card.Card.IsUpgraded },
        HoverTip text => new { title = text.Title, description = text.Description },
        _ => new { id = tip.Id }
    }).ToArray();

    private static string SaveImage(Texture2D texture, string assets, int width)
    {
        // AtlasTexture.GetImage blits before decompressing. Imported BC7 atlases must be decoded first.
        using Image source = (texture is AtlasTexture atlas ? atlas.Atlas : texture).GetImage();
        if (source.IsCompressed()) source.Decompress();
        using Image image = texture is AtlasTexture region ? source.GetRegion((Rect2I)region.Region) : (Image)source.Duplicate();
        if (image.IsCompressed()) image.Decompress();
        if (image.GetWidth() > width) image.Resize(width, Math.Max(1, image.GetHeight() * width / image.GetWidth()), Image.Interpolation.Lanczos);
        byte[] bytes = image.SaveWebpToBuffer();
        string name = Convert.ToHexStringLower(SHA256.HashData(bytes)) + ".webp";
        string path = Path.Combine(assets, name);
        if (!System.IO.File.Exists(path)) System.IO.File.WriteAllBytes(path, bytes);
        return "images/" + name;
    }
}
