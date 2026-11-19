using System.Globalization;

namespace Facet.Core.Palette;

/// <summary>
/// Small recursive-descent calculator: + - * / % ^ (right-assoc), parentheses, unary signs,
/// constants (pi, e, tau) and common functions. Throws FormatException on bad input.
/// </summary>
public static class MathEvaluator
{
    public static double Evaluate(string expression)
    {
        var p = new Parser(expression);
        var value = p.ParseExpression();
        p.SkipWhitespace();
        if (!p.AtEnd) throw new FormatException($"Unexpected '{p.Current}'");
        if (double.IsNaN(value) || double.IsInfinity(value)) throw new FormatException("Not a finite number");
        return value;
    }

    public static bool TryEvaluate(string expression, out double value)
    {
        try
        {
            value = Evaluate(expression);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            value = 0;
            return false;
        }
    }

    /// <summary>True when the text contains an operator or call and evaluates cleanly.</summary>
    public static bool LooksLikeMath(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return false;
        if (text.IndexOfAny(['+', '-', '*', '/', '^', '%', '(']) < 0) return false;
        return TryEvaluate(text, out _);
    }

    private sealed class Parser(string text)
    {
        private int _pos;

        public bool AtEnd => _pos >= text.Length;
        public char Current => AtEnd ? '\0' : text[_pos];

        public void SkipWhitespace()
        {
            while (!AtEnd && char.IsWhiteSpace(text[_pos])) _pos++;
        }

        private bool Accept(char c)
        {
            SkipWhitespace();
            if (Current != c) return false;
            _pos++;
            return true;
        }

        public double ParseExpression()
        {
            var v = ParseTerm();
            while (true)
            {
                if (Accept('+')) v += ParseTerm();
                else if (Accept('-')) v -= ParseTerm();
                else return v;
            }
        }

        private double ParseTerm()
        {
            var v = ParseUnary();
            while (true)
            {
                if (Accept('*')) v *= ParseUnary();
                else if (Accept('/')) v /= ParseUnary();
                else if (Accept('%')) v %= ParseUnary();
                else return v;
            }
        }

        private double ParseUnary()
        {
            if (Accept('-')) return -ParseUnary();
            if (Accept('+')) return ParseUnary();
            return ParsePower();
        }

        private double ParsePower()
        {
            var b = ParsePrimary();
            return Accept('^') ? Math.Pow(b, ParseUnary()) : b;
        }

        private double ParsePrimary()
        {
            SkipWhitespace();
            if (Accept('('))
            {
                var v = ParseExpression();
                if (!Accept(')')) throw new FormatException("Missing ')'");
                return v;
            }

            if (char.IsDigit(Current) || Current == '.') return ParseNumber();

            if (char.IsLetter(Current))
            {
                var start = _pos;
                while (!AtEnd && char.IsLetter(text[_pos])) _pos++;
                var name = text[start.._pos].ToLowerInvariant();

                switch (name)
                {
                    case "pi": return Math.PI;
                    case "e": return Math.E;
                    case "tau": return Math.Tau;
                }

                if (!Accept('(')) throw new FormatException($"Unknown name '{name}'");
                var arg = ParseExpression();
                if (!Accept(')')) throw new FormatException("Missing ')'");
                return name switch
                {
                    "sqrt" => Math.Sqrt(arg),
                    "abs" => Math.Abs(arg),
                    "sin" => Math.Sin(arg),
                    "cos" => Math.Cos(arg),
                    "tan" => Math.Tan(arg),
                    "asin" => Math.Asin(arg),
                    "acos" => Math.Acos(arg),
                    "atan" => Math.Atan(arg),
                    "log" => Math.Log10(arg),
                    "ln" => Math.Log(arg),
                    "exp" => Math.Exp(arg),
                    "round" => Math.Round(arg),
                    "floor" => Math.Floor(arg),
                    "ceil" => Math.Ceiling(arg),
                    _ => throw new FormatException($"Unknown function '{name}'"),
                };
            }

            throw new FormatException(AtEnd ? "Unexpected end" : $"Unexpected '{Current}'");
        }

        private double ParseNumber()
        {
            var start = _pos;
            while (!AtEnd && (char.IsDigit(text[_pos]) || text[_pos] == '.')) _pos++;
            if (!AtEnd && (text[_pos] == 'e' || text[_pos] == 'E'))
            {
                var save = _pos;
                _pos++;
                if (!AtEnd && (text[_pos] == '+' || text[_pos] == '-')) _pos++;
                if (!AtEnd && char.IsDigit(text[_pos]))
                {
                    while (!AtEnd && char.IsDigit(text[_pos])) _pos++;
                }
                else
                {
                    _pos = save; // the 'e' was not an exponent
                }
            }

            var token = text[start.._pos];
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                throw new FormatException($"Bad number '{token}'");
            return v;
        }
    }
}
