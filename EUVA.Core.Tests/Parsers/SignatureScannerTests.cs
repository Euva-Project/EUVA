// SPDX-License-Identifier: GPL-3.0-or-later

using EUVA.Core.Parsers;
using Xunit;

namespace EUVA.Core.Tests.Parsers;

public class SignatureScannerTests
{
    [Fact]
    public void FindFirst_ExactPattern_ReturnsCorrectOffset()
    {
        // Arrange
        byte[] data = [0x90, 0x55, 0x8B, 0xEC, 0xCC, 0xC3];
        string pattern = "55 8B EC";

        // Act
        long offset = SignatureScanner.FindFirst(data, pattern);

        // Assert
        Assert.Equal(1, offset);
    }

    [Fact]
    public void FindFirst_PatternWithWildcards_ReturnsCorrectOffset()
    {
        // Arrange
        byte[] data = [0x00, 0x11, 0x22, 0x33, 0x44, 0x55];
        string pattern = "11 ?? 33";

        // Act
        long offset = SignatureScanner.FindFirst(data, pattern);

        // Assert
        Assert.Equal(1, offset);
    }

    [Fact]
    public void FindFirst_PatternNotFound_ReturnsMinusOne()
    {
        // Arrange
        byte[] data = [0x01, 0x02, 0x03, 0x04];
        string pattern = "AA BB CC";

        // Act
        long offset = SignatureScanner.FindFirst(data, pattern);

        // Assert
        Assert.Equal(-1, offset);
    }

    [Fact]
    public void FindPattern_MultipleMatches_ReturnsAllOccurrences()
    {
        // Arrange
        byte[] data = [0x90, 0x55, 0x8B, 0x90, 0x55, 0x8B];
        string pattern = "55 8B";
        string signatureName = "TestFunctionProlog";

        // Act
        var matches = SignatureScanner.FindPattern(data, pattern, signatureName);

        // Assert
        Assert.Equal(2, matches.Count);
        Assert.Equal(1, matches[0].Offset);
        Assert.Equal(4, matches[1].Offset);
        Assert.All(matches, m => Assert.Equal(signatureName, m.Name));
    }

    [Fact]
    public void FindInRange_ValidRange_RestrictsSearchSpace()
    {
        // Arrange
        byte[] data = [0x00, 0xAA, 0xBB, 0x00, 0xAA, 0xBB];
        string pattern = "AA BB";
        string name = "SearchTag";

        // Act - search only in range from offset 3 for size 3
        var matches = SignatureScanner.FindInRange(data, 3, 3, pattern, name);

        // Assert
        Assert.Single(matches);
        Assert.Equal(4, matches[0].Offset);
    }
}
