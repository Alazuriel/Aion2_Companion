using AION_2_Companion.Core.Localization;
using AION_2_Companion.Core;

if (args is ["--export-template", string outputPath])
{
    var template = new NflDocument();
    template.Metadata["language"] = "en-US";
    template.Metadata["name"] = "English";
    template.Metadata["author"] = "Nexforge Studios";
    template.Metadata["fallback"] = "en-US";
    template.Metadata["product"] = "aion2-companion";
    template.Sections["Companion"] = LocalizationService.GetEnglishTemplate()
        .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
    File.WriteAllBytes(outputPath, NflParser.SerializeFile(template));
    Console.WriteLine($"Exported {template.Sections["Companion"].Count} translations to {outputPath}");
    return 0;
}

if (args is ["--export-all", string outputDirectory])
{
    var languages = new (string Code, string Name)[]
    {
        ("en", "English"), ("hu", "Magyar"), ("de", "Deutsch"), ("pl", "Polski"),
    };
    Directory.CreateDirectory(outputDirectory);
    foreach ((string code, string name) in languages)
    {
        var document = new NflDocument();
        document.Metadata["language"] = code;
        document.Metadata["name"] = name;
        document.Metadata["author"] = "Nexforge Studios";
        document.Metadata["fallback"] = "en";
        document.Metadata["product"] = "aion2-companion";
        document.Sections["Companion"] = LocalizationService.GetLanguageTemplate(code)
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        string path = Path.Combine(outputDirectory, code + ".nfl");
        File.WriteAllBytes(path, NflParser.SerializeFile(document));
        Console.WriteLine($"Exported {document.Sections["Companion"].Count} translations to {path}");
    }
    return 0;
}

var tests = new (string Name, Action Run)[]
{
    ("shared valid fixture", SharedValidFixture),
    ("full Companion template", FullCompanionTemplate),
    ("automatic packaged languages", AutomaticPackagedLanguages),
    ("deterministic round trip", DeterministicRoundTrip),
    ("shared duplicate fixture", SharedDuplicateFixture),
    ("invalid escape", InvalidEscape),
    ("placeholder validation", PlaceholderValidation),
};

int passed = 0;
foreach ((string name, Action run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS  {name}");
        passed++;
    }
    catch (Exception exception)
    {
        Console.WriteLine($"FAIL  {name}: {exception.Message}");
    }
}
Console.WriteLine($"{passed}/{tests.Length} tests passed");
return passed == tests.Length ? 0 : 1;

static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

static void SharedValidFixture()
{
    NflDocument document = NflParser.ParseFile(Fixture("valid.hu-HU.nfl"));
    Equal("hu-HU", document.Language);
    Equal("Főoldal", document.Sections["Navigation"]["Home"]);
    True(NflValidator.Placeholders(document.Sections["Messages"]["FightSaved"]).SetEquals(["name"]));
}

static void FullCompanionTemplate()
{
    NflDocument document = NflParser.ParseFile(Fixture("en.nfl"));
    Equal("en", document.Language);
    True(document.Flatten().Count >= 500);
    Equal(0, NflValidator.Validate(document, document).Count);
}

static void AutomaticPackagedLanguages()
{
    NflLanguagePack[] packs = NflLanguagePackService.Discover().ToArray();
    True(new[] { "en", "hu", "de", "pl" }.All(code =>
        packs.Any(pack => string.Equals(pack.Code, code, StringComparison.OrdinalIgnoreCase))));
    LocalizationService.Apply("hu");
    Equal("Főoldal", LocalizationService.Get("NavHome"));
    LocalizationService.Apply("en");
}

static void DeterministicRoundTrip()
{
    NflDocument document = NflParser.ParseFile(Fixture("valid.hu-HU.nfl"));
    string first = NflParser.Serialize(document);
    string second = NflParser.Serialize(NflParser.Parse(first));
    Equal(first, second);
}

static void SharedDuplicateFixture()
{
    try
    {
        NflParser.ParseFile(Fixture("invalid.duplicate.nfl"));
        throw new InvalidOperationException("Expected duplicate key rejection.");
    }
    catch (NflParseException exception)
    {
        Equal(6, exception.Line);
    }
}

static void InvalidEscape()
{
    const string source = "!NFL 1.0\n@language \"en-US\"\n@name \"English\"\n[X]\nA = \"bad\\q\"\n";
    try
    {
        NflParser.Parse(source);
        throw new InvalidOperationException("Expected invalid escape rejection.");
    }
    catch (NflParseException) { }
}

static void PlaceholderValidation()
{
    NflDocument basis = NflParser.Parse("!NFL 1.0\n@language \"en-US\"\n@name \"English\"\n[X]\nA = \"Hi {name}\"\nB = \"Bye\"\n");
    NflDocument target = NflParser.Parse("!NFL 1.0\n@language \"hu-HU\"\n@name \"Magyar\"\n[X]\nA = \"Szia {user}\"\n");
    NflValidationIssue[] issues = NflValidator.Validate(target, basis).ToArray();
    Equal(2, issues.Length);
    True(issues.Select(issue => issue.Key).ToHashSet().SetEquals(["X.A", "X.B"]));
}

static void True(bool value)
{
    if (!value) throw new InvalidOperationException("Expected true.");
}

static void Equal<T>(T expected, T actual) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
}
