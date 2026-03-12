// SPDX-License-Identifier: GPL-3.0-or-later

namespace EUVA.Core.Disassembly.Analysis;

public record struct VariableSymbol(string OriginalName, string Name, bool IsAiGenerated)
{
    public VariableSymbol(string originalName, string name) : this(originalName, name, false) { }
}
