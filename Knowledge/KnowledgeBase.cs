using System.Text.Json;

namespace OComplexAssistant.Knowledge;

public sealed record KbArticle(string Id, string Category, string Title, string[] Keywords, string Content);

public sealed record KbHit(KbArticle Article, int Score);

/// <summary>
/// Короткая база знаний в памяти + поиск по ключевым словам.
/// Для десятка статей этого достаточно; при росте базы Search заменяется на векторный поиск,
/// остальной код не меняется.
/// </summary>
public sealed class KnowledgeBase
{
    private const string UpsellCategory = "upsell";
    private const int KeywordWeight = 3;
    private const int TitleWeight = 2;
    private const int ContentWeight = 1;
    private const int StemLength = 5;

    private static readonly HashSet<string> StopWords =
    [
        "как", "что", "это", "для", "или", "при", "так", "уже", "если", "мне", "меня", "мой", "вас", "вам",
        "есть", "можно", "нужно", "здравствуйте", "добрый", "день", "привет", "спасибо", "пожалуйста",
        "подскажите", "скажите", "хочу", "хотела", "хотел", "очень", "тоже", "когда", "какой", "какая", "какие",
    ];

    private readonly List<IndexedArticle> _index;

    public KnowledgeBase(IWebHostEnvironment env)
    {
        var path = Path.Combine(env.ContentRootPath, "Data", "knowledge-base.json");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var articles = JsonSerializer.Deserialize<List<KbArticle>>(File.ReadAllText(path), options)
            ?? throw new InvalidOperationException($"Knowledge base at '{path}' is empty.");

        _index = articles.Select(a => new IndexedArticle(
            a,
            a.Keywords.SelectMany(Stems).ToHashSet(),
            Stems(a.Title).ToHashSet(),
            Stems(a.Content).ToHashSet())).ToList();
    }

    public IReadOnlyList<KbArticle> Articles => _index.Select(i => i.Article).ToList();

    /// <summary>Правила допродаж передаются модели всегда, независимо от запроса.</summary>
    public IReadOnlyList<KbArticle> UpsellRules =>
        _index.Where(i => i.Article.Category == UpsellCategory).Select(i => i.Article).ToList();

    public IReadOnlyList<KbHit> Search(string query, int top = 4)
    {
        var queryStems = Stems(query).ToHashSet();
        if (queryStems.Count == 0)
        {
            return [];
        }

        return _index
            .Where(i => i.Article.Category != UpsellCategory)
            .Select(i => new KbHit(i.Article, queryStems.Sum(i.Weight)))
            .Where(h => h.Score > 0)
            .OrderByDescending(h => h.Score)
            .Take(top)
            .ToList();
    }

    // Грубый стемминг: первые 5 букв слова. «доставка», «доставить», «доставят» дают один корень «доста».
    private static IEnumerable<string> Stems(string text) =>
        text.ToLowerInvariant()
            .Replace('ё', 'е')
            .Split(text.Where(c => !char.IsLetterOrDigit(c)).Distinct().ToArray(), StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 && !StopWords.Contains(w))
            .Select(w => w.Length > StemLength ? w[..StemLength] : w);

    private sealed record IndexedArticle(
        KbArticle Article, HashSet<string> Keywords, HashSet<string> Title, HashSet<string> Content)
    {
        public int Weight(string stem) =>
            Keywords.Contains(stem) ? KeywordWeight
            : Title.Contains(stem) ? TitleWeight
            : Content.Contains(stem) ? ContentWeight
            : 0;
    }
}
