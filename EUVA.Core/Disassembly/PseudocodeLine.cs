// SPDX-License-Identifier: GPL-3.0-or-later

namespace EUVA.Core.Disassembly;

public struct PseudocodeLine
{
    public string Text;
    public PseudocodeSpan[] Spans;

    public PseudocodeLine(string text, PseudocodeSpan[]? spans = null)
    {
        Text = text;
        Spans = spans ?? new[] { new PseudocodeSpan(0, text.Length, PseudocodeSyntax.Text) };
    }

    public static PseudocodeLine Comment(string text)
        => new(text, new[] { new PseudocodeSpan(0, text.Length, PseudocodeSyntax.Comment) });

    public static PseudocodeLine Empty => new("", Array.Empty<PseudocodeSpan>());
}

public struct PseudocodeSpan
{
    public int Start;
    public int Length;
    public PseudocodeSyntax Kind;
    public PseudocodeSpan(int start, int length, PseudocodeSyntax kind)
    { Start = start; Length = length; Kind = kind; }
}

public enum PseudocodeSyntax : byte
{
    Text = 0,
    Keyword = 1,        
    Type = 2,           
    Variable = 3,       
    Number = 4,         
    String = 5,         
    Function = 6,       
    Operator = 7,       
    Punctuation = 8,    
    Comment = 9,        
    Address = 10,       
}
