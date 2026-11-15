namespace WeaponLab.Cli;

/// <summary>
/// Splits arguments into positional words and <c>--name value</c> options.
/// </summary>
internal sealed class CommandLine
{
    private readonly Dictionary<string, string> _options;

    private CommandLine(string[] positional, Dictionary<string, string> options)
    {
        Positional = positional;
        _options = options;
    }

    public string[] Positional { get; }

    public string? Game => Value("--game");

    public string? Workspace => Value("--workspace");

    public string? Value(string name) => _options.GetValueOrDefault(name);

    public static CommandLine Parse(string[] args)
    {
        var positional = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].StartsWith("--", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException($"Option {args[index]} needs a value.");
                }

                options[args[index]] = args[++index];
            }
            else
            {
                positional.Add(args[index]);
            }
        }

        return new CommandLine(positional.ToArray(), options);
    }
}
