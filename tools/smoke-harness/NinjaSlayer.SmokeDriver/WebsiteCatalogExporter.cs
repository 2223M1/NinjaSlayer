using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using NinjaSlayer.Cards.Standard;
using NinjaSlayer.Cards;
using NinjaSlayer.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace NinjaSlayer.SmokeDriver;

// Runs in the installed candidate's main menu or the offline exact-host contract.
// The website never reads test card specifications.
internal static class WebsiteCatalogExporter
{
    private static readonly Dictionary<(ulong TextureId, int Width), string> ImageNames = new();

    internal static void Export(string destination, bool exportImages = true, string? assemblyPath = null)
    {
        ImageNames.Clear();
        if (DirAccess.DirExistsAbsolute("res://Website"))
            throw new InvalidOperationException("Website resources must not be shipped in the game pack.");
        Directory.CreateDirectory(destination);
        string assets = Path.Combine(destination, "images");
        Directory.CreateDirectory(assets);
        Assembly product = typeof(NinjaSlayerCharacter).Assembly;
        string version = System.Environment.GetEnvironmentVariable("NINJASLAYER_CATALOG_VERSION")
            ?? throw new InvalidOperationException("Catalog export requires the candidate package version.");
        var cards = ModelDb.AllCards.Concat(new CardModel[] {
            ModelDb.Card<Chado>(), ModelDb.Card<StraightKi>(),
            ModelDb.Card<BlackFlame>(), ModelDb.Card<StrongShuriken>(), ModelDb.Card<BusyLine>(),
            ModelDb.Card<Machete>()
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
            foreach (string language in LocManager.Languages)
            {
                SetExportLanguage(language, models);
                VerifyLocalizedTables(language);
                translations[language] = models.Select(model => Describe(model, assets, exportImages)).ToArray();
                GD.Print($"Catalog language verified: {language}; {cards.Count(card => card is ModCardTemplate)} mod cards; " +
                    $"{cards.Where(card => card is ModCardTemplate).Sum(card => card.MaxUpgradeLevel > 0 ? 2 : 1)} base/upgraded mod variants.");
                var events = LocManager.Instance.GetTable("events");
                labels[language] = events.Keys.Where(key => key.EndsWith(".title", StringComparison.Ordinal))
                    .ToDictionary(key => key, key => events.GetRawText(key));
            }
        }
        finally
        {
            SetExportLanguage(originalLanguage, models);
            ImageNames.Clear();
        }
        var catalog = new
        {
            schemaVersion = 1, version, imagesExported = exportImages,
            sourceRevision = product.GetCustomAttributes<AssemblyMetadataAttribute>().Single(item => item.Key == "NinjaSlayerSourceRevision").Value,
            // Godot may load the offline candidate assembly from bytes (Location="").
            // The contract passes the exact file whose MVID it already verified.
            dllSha256 = Convert.ToHexStringLower(SHA256.HashData(System.IO.File.ReadAllBytes(assemblyPath ?? product.Location))),
            hostMvid = typeof(CardModel).Assembly.ManifestModule.ModuleVersionId,
            languages = translations, labels
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(catalog, new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllBytes(Path.Combine(destination, "catalog.json"), bytes);
        System.IO.File.WriteAllText(Path.Combine(destination, "fingerprint.txt"), Convert.ToHexStringLower(SHA256.HashData(bytes)) + "\n");
        GD.Print($"Website catalog exported: {cards.Length} cards, {models.Length} models, {translations.Count} languages, candidate {version}.");
    }

    private static void SetExportLanguage(string language, IEnumerable<AbstractModel> models)
    {
        LocManager.Instance.SetLanguage(language);
        // Normal play doesn't render every locale in one process. Native tips and
        // CanonicalVars cache translated strings (e.g. Pael's enchantment names).
        // Invalidate those presentation caches, including when restoring the menu.
        foreach (string field in new[] { "_keywordHoverTips", "_potionHoverTips" })
            ((System.Collections.IDictionary)typeof(HoverTipFactory)
                .GetField(field, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!).Clear();
        foreach (Type type in new[] { typeof(CardModel), typeof(RelicModel), typeof(PotionModel), typeof(PowerModel) })
        {
            FieldInfo cache = type.GetField("_dynamicVars", BindingFlags.NonPublic | BindingFlags.Instance)!;
            foreach (AbstractModel model in models.Where(type.IsInstanceOfType)) cache.SetValue(model, null);
        }
    }

    private static object Describe(AbstractModel model, string assets, bool exportImages)
    {
        return model switch
        {
            CardModel card => new
            {
                id = card.Id.ToString(), kind = "card", mod = card is ModCardTemplate,
                rarity = card.Rarity.ToString(), type = card.Type.ToString(),
                image = exportImages ? SaveImage(card.Portrait, assets, 512) : null,
                thumbnail = exportImages ? SaveImage(card.Portrait, assets, 96) : null,
                imagePath = card.PortraitPath,
                variants = card.MaxUpgradeLevel > 0 ? new[] { CardVariant(card, false), CardVariant(card, true) } : new[] { CardVariant(card, false) }
            },
            RelicModel relic => new { id = relic.Id.ToString(), kind = "relic", name = relic.Title.GetFormattedText(),
                description = relic.DynamicDescription.GetFormattedText(), image = exportImages ? SaveImage(relic.Icon, assets, 256) : null,
                tips = Tips(relic.HoverTipsExcludingRelic) },
            PotionModel potion => new { id = potion.Id.ToString(), kind = "potion", name = potion.Title.GetFormattedText(),
                description = potion.DynamicDescription.GetFormattedText(), image = exportImages ? SaveImage(potion.Image, assets, 256) : null, tips = Tips(potion.ExtraHoverTips) },
            MonsterModel monster => new { id = monster.Id.ToString(), kind = "enemy", name = monster.Title.GetFormattedText() },
            PowerModel power => new { id = power.Id.ToString(), kind = "power", name = power.Title.GetFormattedText() },
            CharacterModel character => new { id = character.Id.ToString(), kind = "character", name = character.Title.GetFormattedText() },
            OrbModel orb => new { id = orb.Id.ToString(), kind = "orb", name = orb.Title.GetFormattedText(), description = orb.Description.GetFormattedText() },
            EncounterModel encounter => new { id = encounter.Id.ToString(), kind = "encounter", name = encounter.Title.GetFormattedText(),
                monsters = encounter.AllPossibleMonsters.Select(monster => monster.Id.ToString()).ToArray() },
            EventModel evt => new { id = evt.Id.ToString(), kind = "event", name = evt.Title.GetFormattedText(),
                image = exportImages && evt.LayoutType == MegaCrit.Sts2.Core.Events.EventLayoutType.Default ? SaveImage(evt.CreateInitialPortrait(), assets, 512) : null },
            _ => throw new InvalidOperationException($"Unsupported catalog model: {model.Id}")
        };
    }

    private static void VerifyLocalizedTables(string language)
    {
        int localizedEntries = 0;
        foreach (string table in new[] { "cards", "powers", "relics", "events", "ancients",
            "characters", "monsters", "intents", "card_keywords", "potions", "settings_ui",
            "afflictions", "enchantments", "orbs", "static_hover_tips", "encounters", "card_selection" })
        {
            string path = $"res://NinjaSlayer/localization/{language}/{table}.json";
            if (!Godot.FileAccess.FileExists(path)) throw new InvalidOperationException($"Missing packed localization: {path}");
            using JsonDocument expected = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(path));
            foreach (JsonProperty entry in expected.RootElement.EnumerateObject())
            {
                localizedEntries++;
                if (!LocString.Exists(table, entry.Name)
                    || LocManager.Instance.GetTable(table).GetRawText(entry.Name) != entry.Value.GetString())
                    throw new InvalidOperationException($"Localization fallback or mismatch: {language}/{table}/{entry.Name}");
            }
        }
        var greeting = new LocString("characters", "NINJA_SLAYER_GREETING_PLAYER");
        greeting.Add("BossTitle", "TEST");
        string formatted = greeting.GetFormattedText();
        if (!formatted.Contains("TEST") || formatted.Contains("{BossTitle}"))
            throw new InvalidOperationException($"Greeting formatting failed: {language}");
        var exhaust = (HoverTip)HoverTipFactory.FromKeyword(CardKeyword.Exhaust);
        if (exhaust.Title != new LocString("card_keywords", "EXHAUST.title").GetFormattedText()
            || exhaust.Description != new LocString("card_keywords", "EXHAUST.description").GetFormattedText())
            throw new InvalidOperationException($"Cached keyword text has the wrong language: {language}");
        // Powers use Amount/Damage rather than card dynamic vars. Exercise singular,
        // few, and many forms, including Russian and Polish three-branch plurals.
        using JsonDocument powers = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(
            $"res://NinjaSlayer/localization/{language}/powers.json"));
        foreach (JsonProperty entry in powers.RootElement.EnumerateObject())
        {
            foreach (int amount in new[] { 1, 2, 5 })
            {
                var text = new LocString("powers", entry.Name);
                text.Add("Amount", amount);
                text.Add("Damage", 7);
                string result = text.GetFormattedText();
                if (System.Text.RegularExpressions.Regex.IsMatch(result, @"\{[A-Za-z_]"))
                    throw new InvalidOperationException($"Unresolved power text: {language}/{entry.Name}/{amount}");
            }
        }
        GD.Print($"Localization tables verified: {language}, 17 tables, {localizedEntries} entries; " +
            $"all {powers.RootElement.EnumerateObject().Count()} power entries rendered at Amount 1/2/5.");
    }

    private static object CardVariant(CardModel canonical, bool upgraded)
    {
        CardModel card = canonical.ToMutable();
        if (upgraded && card.MaxUpgradeLevel > 0) card.UpgradeInternal();
        string description = card.GetDescriptionForPile(PileType.None);
        if (card is ModCardTemplate && System.Text.RegularExpressions.Regex.IsMatch(description, @"\{[A-Za-z_]"))
            throw new InvalidOperationException($"Unresolved card text: {LocManager.Instance.Language}/{card.Id}/{upgraded}");
        return new { name = card.Title, upgraded, cost = card.EnergyCost.GetWithModifiers(CostModifiers.Local),
            costsX = card.EnergyCost.CostsX, description,
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
        var key = (texture.GetInstanceId(), width);
        if (ImageNames.TryGetValue(key, out string? cached)) return cached;
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
        string relative = "images/" + name;
        ImageNames.Add(key, relative);
        return relative;
    }
}
