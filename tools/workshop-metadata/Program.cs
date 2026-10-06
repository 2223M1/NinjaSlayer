using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Steamworks;

// Metadata only. Content, images, dependencies, visibility and change notes are never written.
if (args.Length != 3 || args[0] is not ("inspect" or "apply" or "apply-tags" or "verify"))
    throw new ArgumentException("Usage: inspect|apply|apply-tags|verify <Workshop/workshop.json> <eng/compatibility.json>, from the repository root");

using var metadata = JsonDocument.Parse(File.ReadAllText(args[1]));
using var compatibility = JsonDocument.Parse(File.ReadAllText(args[2]));
using var languages = JsonDocument.Parse(File.ReadAllText("Workshop/languages.json"));
var mappedLanguages = languages.RootElement.EnumerateObject()
    .Select(entry => entry.Value.GetProperty("steam").GetString()!).ToArray();
var languageTags = languages.RootElement.EnumerateObject()
    .Select(entry => entry.Value.GetProperty("tag").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
var configuredTags = metadata.RootElement.GetProperty("tags").EnumerateArray()
    .Select(tag => tag.GetString()!).ToArray();
if (configuredTags.Any(tag => string.IsNullOrWhiteSpace(tag) || Encoding.UTF8.GetByteCount(tag) > Constants.k_cubUFSTagValueMax)
    || configuredTags.Distinct(StringComparer.OrdinalIgnoreCase).Count() != configuredTags.Length
    || !languageTags.IsSubsetOf(configuredTags))
    throw new InvalidDataException("Workshop tags must cover every supported language without duplicates.");
var title = metadata.RootElement.GetProperty("title").GetString()!;
if (string.IsNullOrWhiteSpace(title) || Encoding.UTF8.GetByteCount(title) >= Constants.k_cchPublishedDocumentTitleMax)
    throw new InvalidDataException("Invalid Workshop title.");
var id = new PublishedFileId_t(ulong.Parse(compatibility.RootElement.GetProperty("workshop").GetProperty("itemId").GetString()!));
var descriptions = new Dictionary<string, string>
{
    ["english"] = metadata.RootElement.GetProperty("description").GetString()!
};
foreach (var entry in metadata.RootElement.GetProperty("localizedDescriptions").EnumerateObject())
    descriptions.Add(entry.Name, entry.Value.GetString()!);
if (mappedLanguages.Distinct(StringComparer.Ordinal).Count() != mappedLanguages.Length
    || !descriptions.Keys.Order().SequenceEqual(mappedLanguages.Order()))
    throw new InvalidDataException("Descriptions must cover exactly the mapped Steam languages.");
foreach (var (language, description) in descriptions)
    if (string.IsNullOrWhiteSpace(description) || Encoding.UTF8.GetByteCount(description) >= Constants.k_cchPublishedDocumentDescriptionMax)
        throw new InvalidDataException($"Invalid {language} description.");

Environment.SetEnvironmentVariable("SteamAppId", "2868840");
Environment.SetEnvironmentVariable("SteamGameId", "2868840");
if (!SteamAPI.Init()) throw new InvalidOperationException("Start Steam and sign in to the Workshop owner account first.");
try
{
    // Read every language before the first write; abort rather than overwrite on query errors.
    var before = descriptions.Keys.ToDictionary(language => language, Query);
    Console.WriteLine(JsonSerializer.Serialize(new { Phase = "before", Item = id.m_PublishedFileId, Languages = before }));
    if (args[0] == "inspect") return;
    bool applyTags = args[0] == "apply-tags";
    // Preserve unrelated owner tags; replace the old display-name Chinese aliases with filter ids.
    var oldLanguageTags = new HashSet<string>(languageTags, StringComparer.OrdinalIgnoreCase)
    {
        "Simplified Chinese", "Traditional Chinese", "Portuguese (Brazil)", "Spanish-Latin America"
    };
    var expectedTags = applyTags
        ? configuredTags.Concat(before["english"].Tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Where(tag => !oldLanguageTags.Contains(tag.Trim()))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        : before["english"].Tags.Split(',', StringSplitOptions.RemoveEmptyEntries);
    bool tagsChanged = applyTags && !before["english"].Tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
        .ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(expectedTags);
    if (args[0] is "apply" or "apply-tags")
    {
        if (before.Values.Any(item => item.Owner != SteamUser.GetSteamID().m_SteamID))
            throw new InvalidOperationException("The current Steam account does not own this Workshop item.");
        foreach (var (language, description) in descriptions)
        {
            if (before[language].Title == title && Normalize(before[language].Description) == Normalize(description)
                && !(language == "english" && tagsChanged)) continue;
            var update = SteamUGC.StartItemUpdate(new AppId_t(2868840), id);
            // Creating a localized entry does not inherit the default title. Set both fields.
            if (!SteamUGC.SetItemUpdateLanguage(update, language) || !SteamUGC.SetItemTitle(update, title) ||
                !SteamUGC.SetItemDescription(update, description))
                throw new InvalidOperationException($"Steam refused the {language} description.");
            if (language == "english" && tagsChanged && !SteamUGC.SetItemTags(update, expectedTags, false))
                throw new InvalidOperationException("Steam refused the supported-language tags.");
            SubmitItemUpdateResult_t result = default;
            bool done = false;
            bool ioFailure = false;
            using var callback = CallResult<SubmitItemUpdateResult_t>.Create((value, error) =>
            {
                result = value;
                ioFailure = error;
                done = true;
            });
            // SetItemUpdateLanguage applies to title/description, NOT changelogs.
            // Metadata maintenance does not create 16 release notes or overwrite owner-written notes.
            callback.Set(SteamUGC.SubmitItemUpdate(update, null));
            PumpUntil(() => done);
            if (ioFailure || result.m_eResult != EResult.k_EResultOK || result.m_bUserNeedsToAcceptWorkshopLegalAgreement)
                throw new InvalidOperationException($"Updating {language} failed or requires agreement: {result.m_eResult}");
            Console.WriteLine($"Updated metadata: {language}");
        }
    }
    var after = descriptions.Keys.ToDictionary(language => language, Query);
    Console.WriteLine(JsonSerializer.Serialize(new { Phase = "after", Item = id.m_PublishedFileId, Languages = after }));
    foreach (var (language, expected) in descriptions)
    {
        if (after[language].Title != title || Normalize(after[language].Description) != Normalize(expected))
            throw new InvalidOperationException($"Remote {language} title/description does not match the local text.");
        if (!after[language].Tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(expectedTags))
            throw new InvalidOperationException($"Remote {language} tags do not match the requested tag set.");
        if (JsonSerializer.Serialize(before[language] with { Title = "", Description = "", Updated = 0, Tags = "" }) !=
            JsonSerializer.Serialize(after[language] with { Title = "", Description = "", Updated = 0, Tags = "" }))
            throw new InvalidOperationException($"Unrelated Workshop fields changed during the {language} update; inspect the snapshots.");
    }
    Console.WriteLine($"Verified all {descriptions.Count} titles/descriptions and the tag set; content, visibility, dependencies and previews unchanged.");
}
finally
{
    SteamAPI.Shutdown();
}

Snapshot Query(string language)
{
    var handle = SteamUGC.CreateQueryUGCDetailsRequest([id], 1);
    try
    {
        if (!SteamUGC.SetLanguage(handle, language) || !SteamUGC.SetReturnLongDescription(handle, true) ||
            !SteamUGC.SetReturnChildren(handle, true) || !SteamUGC.SetReturnAdditionalPreviews(handle, true) ||
            !SteamUGC.SetAllowCachedResponse(handle, 0))
            throw new InvalidOperationException($"Steam refused the {language} query options.");
        SteamUGCQueryCompleted_t result = default;
        bool done = false;
        bool ioFailure = false;
        using var callback = CallResult<SteamUGCQueryCompleted_t>.Create((value, error) =>
        {
            result = value;
            ioFailure = error;
            done = true;
        });
        callback.Set(SteamUGC.SendQueryUGCRequest(handle));
        PumpUntil(() => done);
        if (ioFailure || result.m_eResult != EResult.k_EResultOK ||
            !SteamUGC.GetQueryUGCResult(handle, 0, out var details) || details.m_eResult != EResult.k_EResultOK)
            throw new InvalidOperationException($"Reading {language} failed: {result.m_eResult}");
        var children = new PublishedFileId_t[details.m_unNumChildren];
        if (children.Length > 0 && !SteamUGC.GetQueryUGCChildren(handle, 0, children, (uint)children.Length))
            throw new InvalidOperationException("Cannot read Workshop dependencies.");
        var previews = new List<Preview>();
        for (uint i = 0; i < SteamUGC.GetQueryUGCNumAdditionalPreviews(handle, 0); i++)
        {
            if (!SteamUGC.GetQueryUGCAdditionalPreview(handle, 0, i, out var url, 8192, out var file, 1024, out var kind))
                throw new InvalidOperationException("Cannot read Workshop gallery previews.");
            previews.Add(new Preview(url, file, kind.ToString()));
        }
        if (!SteamUGC.GetQueryUGCPreviewURL(handle, 0, out var preview, 8192))
            throw new InvalidOperationException("Cannot read Workshop list preview.");
        return new Snapshot(details.m_rgchTitle, details.m_rgchDescription, details.m_ulSteamIDOwner,
            details.m_rtimeUpdated, details.m_hFile.m_UGCHandle, details.m_nFileSize, details.m_eVisibility.ToString(),
            details.m_rgchTags, children.Select(child => child.m_PublishedFileId).ToArray(), preview, previews.ToArray());
    }
    finally
    {
        SteamUGC.ReleaseQueryUGCRequest(handle);
    }
}

static void PumpUntil(Func<bool> completed)
{
    var timer = Stopwatch.StartNew();
    while (!completed())
    {
        if (timer.Elapsed > TimeSpan.FromMinutes(2))
            throw new TimeoutException("Steam request timed out. Query remote state before retrying any update.");
        SteamAPI.RunCallbacks();
        Thread.Sleep(50);
    }
}

static string Normalize(string text) => text.Replace("\r\n", "\n");

record Preview(string Url, string File, string Type);
record Snapshot(string Title, string Description, ulong Owner, uint Updated, ulong ContentHandle, int ContentSize,
    string Visibility, string Tags, ulong[] Dependencies, string MainPreview, Preview[] Previews);
