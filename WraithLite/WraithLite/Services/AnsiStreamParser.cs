using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System;
using System.Collections.Generic;
using System.Text;

namespace WraithLite.Services
{
    /// <summary>
    /// Stateful ANSI SGR stream parser for MAUI.
    /// - Maintains style state across calls (important for GS where sequences can span chunks).
    /// - Supports ESC[...m and optional bare "[...m" if upstream drops ESC.
    /// - Exposes:
    ///   - ParseToFormattedString(line): returns formatted spans, consuming SGR and maintaining state.
    ///   - StripSgr(line): removes SGR codes for routing decisions.
    ///   - Reset(): resets style state at start of new session.
    /// </summary>
    public sealed class AnsiStreamParser
    {
        public sealed class Options
        {
            public Color DefaultColor { get; set; } = Colors.White;
            public bool ParseBareBracketCodes { get; set; } = true;
            public bool TreatBoldAsBright { get; set; } = false; // optional behavior (not used)
        }

        private struct State
        {
            public Color Fg;
            public bool Bold;
        }

        private readonly Options _opts;
        private State _state;

        public AnsiStreamParser(Options? options = null)
        {
            _opts = options ?? new Options();
            Reset();
        }

        public void Reset()
        {
            _state = new State
            {
                Fg = _opts.DefaultColor,
                Bold = false
            };
        }

        /// <summary>
        /// Removes SGR sequences (ESC[...m and optionally bare "[...m") but keeps visible text.
        /// Use this ONLY for routing regex/tests. Do not use for rendering.
        /// </summary>
        public string StripSgr(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            var sb = new StringBuilder(input.Length);

            int i = 0;
            while (i < input.Length)
            {
                char ch = input[i];

                // ESC[
                if (ch == '\u001b' && i + 1 < input.Length && input[i + 1] == '[')
                {
                    i += 2; // skip ESC[
                    SkipSgr(input, ref i);
                    continue;
                }

                // bare [
                if (_opts.ParseBareBracketCodes && ch == '[')
                {
                    int j = i + 1;
                    if (LooksLikeSgr(input, ref j))
                    {
                        i = j; // j positioned after 'm'
                        continue;
                    }
                }

                sb.Append(ch);
                i++;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Converts text to a FormattedString by consuming SGR codes and applying styles to spans.
        /// This is STATEFUL across calls.
        /// </summary>
        public FormattedString ParseToFormattedString(string input)
        {
            input ??= string.Empty;

            var fs = new FormattedString();
            var textBuf = new StringBuilder();

            void FlushSpan()
            {
                if (textBuf.Length == 0)
                    return;

                fs.Spans.Add(new Span
                {
                    Text = textBuf.ToString(),
                    TextColor = _state.Fg,
                    FontAttributes = _state.Bold ? FontAttributes.Bold : FontAttributes.None
                });

                textBuf.Clear();
            }

            int i = 0;
            while (i < input.Length)
            {
                char ch = input[i];

                // ESC[
                if (ch == '\u001b' && i + 1 < input.Length && input[i + 1] == '[')
                {
                    i += 2; // skip ESC[
                    if (TryParseSgrCodes(input, ref i, out var codes))
                    {
                        FlushSpan();
                        ApplyCodes(codes);
                        continue;
                    }

                    // not valid sgr, treat ESC as ignorable
                    continue;
                }

                // bare [
                if (_opts.ParseBareBracketCodes && ch == '[')
                {
                    int j = i + 1;
                    if (TryParseBareSgrCodes(input, ref j, out var codes))
                    {
                        FlushSpan();
                        ApplyCodes(codes);
                        i = j;
                        continue;
                    }
                }

                textBuf.Append(ch);
                i++;
            }

            FlushSpan();
            return fs;
        }

        // ----------------------------
        // Parsing helpers
        // ----------------------------

        private static void SkipSgr(string s, ref int i)
        {
            // Skip digits/; until 'm'
            while (i < s.Length)
            {
                char c = s[i];
                i++;
                if (c == 'm')
                    return;
            }
        }

        private static bool LooksLikeSgr(string s, ref int i)
        {
            // i positioned after '['
            int start = i;
            bool sawAny = false;

            while (i < s.Length)
            {
                char c = s[i];

                if ((c >= '0' && c <= '9') || c == ';')
                {
                    sawAny = true;
                    i++;
                    continue;
                }

                if (c == 'm' && sawAny)
                {
                    i++; // consume m
                    return true;
                }

                // not SGR
                break;
            }

            i = start;
            return false;
        }

        private static bool TryParseSgrCodes(string s, ref int i, out List<int> codes)
        {
            // positioned after ESC[
            codes = new List<int>();
            int current = -1;
            bool sawAny = false;

            while (i < s.Length)
            {
                char c = s[i];

                if (c >= '0' && c <= '9')
                {
                    sawAny = true;
                    int digit = c - '0';
                    current = current < 0 ? digit : (current * 10 + digit);
                    i++;
                    continue;
                }

                if (c == ';')
                {
                    codes.Add(current < 0 ? 0 : current);
                    current = -1;
                    i++;
                    continue;
                }

                if (c == 'm')
                {
                    if (sawAny)
                        codes.Add(current < 0 ? 0 : current);
                    else
                        codes.Add(0); // ESC[m means reset
                    i++; // consume m
                    return true;
                }

                return false;
            }

            return false;
        }

        private static bool TryParseBareSgrCodes(string s, ref int i, out List<int> codes)
        {
            // positioned after '['
            codes = new List<int>();
            int current = -1;
            bool sawAny = false;

            while (i < s.Length)
            {
                char c = s[i];

                if (c >= '0' && c <= '9')
                {
                    sawAny = true;
                    int digit = c - '0';
                    current = current < 0 ? digit : (current * 10 + digit);
                    i++;
                    continue;
                }

                if (c == ';')
                {
                    codes.Add(current < 0 ? 0 : current);
                    current = -1;
                    i++;
                    continue;
                }

                if (c == 'm')
                {
                    if (sawAny)
                        codes.Add(current < 0 ? 0 : current);
                    else
                        codes.Add(0);
                    i++; // consume m
                    return true;
                }

                return false;
            }

            return false;
        }

        private void ApplyCodes(List<int> codes)
        {
            foreach (var code in codes)
            {
                switch (code)
                {
                    case 0: // reset
                        _state.Fg = _opts.DefaultColor;
                        _state.Bold = false;
                        break;

                    case 1: // bold
                        _state.Bold = true;
                        break;

                    case 22: // normal intensity
                        _state.Bold = false;
                        break;

                    case 39: // default fg
                        _state.Fg = _opts.DefaultColor;
                        break;

                    case int c when (c >= 30 && c <= 37):
                        _state.Fg = MapAnsi8ToColor(c - 30, bright: false);
                        break;

                    case int c when (c >= 90 && c <= 97):
                        _state.Fg = MapAnsi8ToColor(c - 90, bright: true);
                        break;

                    default:
                        // backgrounds and other SGR ignored for now
                        break;
                }
            }
        }

        private static Color MapAnsi8ToColor(int idx, bool bright)
        {
            if (!bright)
            {
                return idx switch
                {
                    0 => Colors.Black,
                    1 => Color.FromArgb("#C50F1F"),
                    2 => Color.FromArgb("#13A10E"),
                    3 => Color.FromArgb("#C19C00"),
                    4 => Color.FromArgb("#0037DA"),
                    5 => Color.FromArgb("#881798"),
                    6 => Color.FromArgb("#3A96DD"),
                    7 => Color.FromArgb("#CCCCCC"),
                    _ => Colors.White
                };
            }

            return idx switch
            {
                0 => Color.FromArgb("#767676"),
                1 => Color.FromArgb("#E74856"),
                2 => Color.FromArgb("#16C60C"),
                3 => Color.FromArgb("#F9F1A5"),
                4 => Color.FromArgb("#3B78FF"),
                5 => Color.FromArgb("#B4009E"),
                6 => Color.FromArgb("#61D6D6"),
                7 => Colors.White,
                _ => Colors.White
            };
        }
    }
}
