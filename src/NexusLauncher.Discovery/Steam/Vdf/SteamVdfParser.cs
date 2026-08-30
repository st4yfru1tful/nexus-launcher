using System.Text;

namespace NexusLauncher.Discovery.Steam.Vdf;

/// <summary>One value in Valve's text VDF format.</summary>
public sealed class VdfValue
{
    private VdfValue(string? scalar, VdfObject? @object)
    {
        Scalar = scalar;
        Children = @object;
    }

    public string? Scalar { get; }

    public VdfObject? Children { get; }

    public bool IsContainer => Children is not null;

    public static VdfValue FromScalar(string value) => new(value, @object: null);

    public static VdfValue FromContainer(VdfObject value) => new(scalar: null, @object: value);
}

/// <summary>One key/value entry in a VDF object.  Entries remain ordered and may repeat.</summary>
public sealed record VdfEntry(string Key, VdfValue Value);

/// <summary>A VDF object supporting case-insensitive lookup and repeated keys.</summary>
public sealed class VdfObject
{
    public VdfObject(IReadOnlyList<VdfEntry> entries)
    {
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
    }

    public IReadOnlyList<VdfEntry> Entries { get; }

    public IEnumerable<VdfEntry> GetEntries(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Entries.Where(entry => entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    public string? GetString(string key)
    {
        return GetEntries(key).Select(entry => entry.Value.Scalar).FirstOrDefault(value => value is not null);
    }

    public VdfObject? GetObject(string key)
    {
        return GetEntries(key).Select(entry => entry.Value.Children).FirstOrDefault(value => value is not null);
    }
}

/// <summary>A parsed text VDF document.</summary>
public sealed record VdfDocument(VdfObject Root);

/// <summary>Describes a malformed VDF document and its source location.</summary>
public sealed class VdfParseException : FormatException
{
    public VdfParseException(string message, int line, int column)
        : base($"{message} (line {line}, column {column}).")
    {
        Line = line;
        Column = column;
    }

    public int Line { get; }

    public int Column { get; }
}

/// <summary>Parser for Steam's quoted, brace-delimited text VDF files.</summary>
public sealed class SteamVdfParser
{
    public static VdfDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokenizer = new Tokenizer(text);
        var root = ParseObject(tokenizer, isRoot: true);
        return new VdfDocument(root);
    }

    public static bool TryParse(string text, out VdfDocument? document)
    {
        try
        {
            document = Parse(text);
            return true;
        }
        catch (VdfParseException)
        {
            document = null;
            return false;
        }
    }

    private static VdfObject ParseObject(Tokenizer tokenizer, bool isRoot)
    {
        var entries = new List<VdfEntry>();
        while (true)
        {
            var key = tokenizer.Read();
            if (key.Kind == TokenKind.End)
            {
                if (isRoot)
                {
                    return new VdfObject(entries);
                }

                throw tokenizer.Error("Unexpected end of VDF while reading an object");
            }

            if (key.Kind == TokenKind.CloseBrace)
            {
                if (isRoot)
                {
                    throw tokenizer.Error("Unexpected closing brace");
                }

                return new VdfObject(entries);
            }

            if (key.Kind != TokenKind.Text)
            {
                throw tokenizer.Error("Expected a VDF key");
            }

            var value = tokenizer.Read();
            if (value.Kind == TokenKind.Text)
            {
                entries.Add(new VdfEntry(key.Value!, VdfValue.FromScalar(value.Value!)));
                continue;
            }

            if (value.Kind == TokenKind.OpenBrace)
            {
                entries.Add(new VdfEntry(key.Value!, VdfValue.FromContainer(ParseObject(tokenizer, isRoot: false))));
                continue;
            }

            throw tokenizer.Error("Expected a VDF value or opening brace");
        }
    }

    private enum TokenKind
    {
        Text,
        OpenBrace,
        CloseBrace,
        End,
    }

    private sealed record Token(TokenKind Kind, string? Value = null);

    private sealed class Tokenizer
    {
        private readonly string _text;
        private int _position;
        private int _line = 1;
        private int _column = 1;

        public Tokenizer(string text)
        {
            _text = text;
        }

        public Token Read()
        {
            SkipWhitespaceAndComments();
            if (_position >= _text.Length)
            {
                return new Token(TokenKind.End);
            }

            return _text[_position] switch
            {
                '{' => ReadSingleCharacter(TokenKind.OpenBrace),
                '}' => ReadSingleCharacter(TokenKind.CloseBrace),
                '"' => new Token(TokenKind.Text, ReadQuotedString()),
                _ => new Token(TokenKind.Text, ReadBareToken()),
            };
        }

        public VdfParseException Error(string message) => new(message, _line, _column);

        private Token ReadSingleCharacter(TokenKind kind)
        {
            Advance();
            return new Token(kind);
        }

        private string ReadQuotedString()
        {
            Advance(); // opening quote
            var builder = new StringBuilder();
            while (_position < _text.Length)
            {
                var current = _text[_position];
                if (current == '"')
                {
                    Advance();
                    return builder.ToString();
                }

                if (current == '\\')
                {
                    Advance();
                    if (_position >= _text.Length)
                    {
                        throw Error("Unterminated escape sequence");
                    }

                    var escaped = _text[_position];
                    builder.Append(escaped switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        _ => escaped,
                    });
                    Advance();
                    continue;
                }

                builder.Append(current);
                Advance();
            }

            throw Error("Unterminated quoted string");
        }

        private string ReadBareToken()
        {
            var start = _position;
            while (_position < _text.Length &&
                   !char.IsWhiteSpace(_text[_position]) &&
                   _text[_position] is not '{' and not '}' and not '"')
            {
                Advance();
            }

            if (start == _position)
            {
                throw Error("Unexpected character in VDF");
            }

            return _text[start.._position];
        }

        private void SkipWhitespaceAndComments()
        {
            while (_position < _text.Length)
            {
                if (char.IsWhiteSpace(_text[_position]))
                {
                    Advance();
                    continue;
                }

                if (_text[_position] == '/' &&
                    _position + 1 < _text.Length &&
                    _text[_position + 1] == '/')
                {
                    while (_position < _text.Length && _text[_position] is not '\r' and not '\n')
                    {
                        Advance();
                    }

                    continue;
                }

                break;
            }
        }

        private void Advance()
        {
            if (_position >= _text.Length)
            {
                return;
            }

            if (_text[_position] == '\n')
            {
                _line++;
                _column = 1;
            }
            else
            {
                _column++;
            }

            _position++;
        }
    }
}
