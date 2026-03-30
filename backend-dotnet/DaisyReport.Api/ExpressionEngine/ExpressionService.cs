using System.Collections.Concurrent;

namespace DaisyReport.Api.ExpressionEngine;

public interface IExpressionService
{
    /// <summary>
    /// Parses and evaluates a single expression string.
    /// </summary>
    object? Evaluate(string expression, EvaluationContext? context = null);

    /// <summary>
    /// Processes a template string, replacing all ${...} expressions with their evaluated values.
    /// </summary>
    string ProcessTemplate(string template, EvaluationContext? context = null);

    /// <summary>
    /// Compiles an expression to MySQL-compatible SQL with bind parameters.
    /// </summary>
    (string Sql, List<object> Parameters) CompileToSql(string expression, string columnName, string tableName);
}

public class ExpressionService : IExpressionService
{
    private const int MaxCacheSize = 10_000;

    // LRU cache: expression string → parsed AST
    // Using ConcurrentDictionary for thread safety; eviction is approximate LRU via count check.
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly TemplateProcessor _templateProcessor = new();
    private long _accessCounter;

    public object? Evaluate(string expression, EvaluationContext? context = null)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return null;

        var ast = ParseCached(expression);
        context ??= EvaluationContext.CreateDefault();
        var evaluator = new Evaluator(context);
        return evaluator.Evaluate(ast);
    }

    public string ProcessTemplate(string template, EvaluationContext? context = null)
    {
        if (string.IsNullOrEmpty(template))
            return template;

        context ??= EvaluationContext.CreateDefault();
        return _templateProcessor.Process(template, context);
    }

    public (string Sql, List<object> Parameters) CompileToSql(string expression, string columnName, string tableName)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return ("", new List<object>());

        var ast = ParseCached(expression);
        var compiler = new SqlCompiler();
        return compiler.Compile(ast, columnName, tableName);
    }

    private AstNode ParseCached(string expression)
    {
        long currentAccess = Interlocked.Increment(ref _accessCounter);

        if (_cache.TryGetValue(expression, out var entry))
        {
            entry.LastAccess = currentAccess;
            return entry.Ast;
        }

        // Parse fresh
        var tokenizer = new Tokenizer(expression);
        var tokens = tokenizer.Tokenize();

        if (tokenizer.Errors.Count > 0)
            throw new InvalidOperationException(
                $"Tokenizer errors: {string.Join("; ", tokenizer.Errors)}");

        var parser = new Parser(tokens);
        var ast = parser.Parse();

        // Evict oldest entries if cache is full
        if (_cache.Count >= MaxCacheSize)
            EvictOldest();

        _cache.TryAdd(expression, new CacheEntry { Ast = ast, LastAccess = currentAccess });
        return ast;
    }

    private void EvictOldest()
    {
        // Remove approximately 10% of oldest entries
        int toRemove = MaxCacheSize / 10;
        var oldest = _cache
            .OrderBy(kvp => kvp.Value.LastAccess)
            .Take(toRemove)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in oldest)
            _cache.TryRemove(key, out _);
    }

    private class CacheEntry
    {
        public AstNode Ast { get; set; } = null!;
        public long LastAccess { get; set; }
    }
}
