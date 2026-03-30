using System.Globalization;
using System.Text;

namespace DaisyReport.Api.ExpressionEngine;

/// <summary>
/// Scans input strings for ${...} template expressions, evaluates them,
/// and substitutes the results as strings. Supports nested braces and
/// escaped \${ sequences.
/// </summary>
public class TemplateProcessor
{
    public string Process(string template, EvaluationContext context)
    {
        if (string.IsNullOrEmpty(template))
            return template;

        var sb = new StringBuilder(template.Length);
        int i = 0;

        while (i < template.Length)
        {
            // Check for escaped \${
            if (i + 2 < template.Length && template[i] == '\\' && template[i + 1] == '$' && template[i + 2] == '{')
            {
                sb.Append("${");
                i += 3;
                continue;
            }

            // Check for template start ${
            if (i + 1 < template.Length && template[i] == '$' && template[i + 1] == '{')
            {
                i += 2; // skip ${
                int exprStart = i;
                int braceDepth = 1;

                // Find matching } with brace depth tracking
                while (i < template.Length && braceDepth > 0)
                {
                    if (template[i] == '{')
                        braceDepth++;
                    else if (template[i] == '}')
                        braceDepth--;

                    if (braceDepth > 0)
                        i++;
                }

                if (braceDepth != 0)
                    throw new InvalidOperationException(
                        $"Unterminated template expression starting at position {exprStart - 2}");

                string expression = template[exprStart..i];
                i++; // skip closing }

                // Tokenize, parse, and evaluate the expression
                var tokenizer = new Tokenizer(expression);
                var tokens = tokenizer.Tokenize();

                if (tokenizer.Errors.Count > 0)
                    throw new InvalidOperationException(
                        $"Template expression error: {string.Join("; ", tokenizer.Errors)}");

                var parser = new Parser(tokens);
                var ast = parser.Parse();

                var evaluator = new Evaluator(context);
                var result = evaluator.Evaluate(ast);

                sb.Append(FormatResult(result));
                continue;
            }

            sb.Append(template[i]);
            i++;
        }

        return sb.ToString();
    }

    private static string FormatResult(object? value) => value switch
    {
        null => "",
        bool b => b ? "true" : "false",
        double d => d.ToString(CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        DaisyReport.Api.ExpressionEngine.BuiltIns.TodayObject t =>
            t.Date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };
}
