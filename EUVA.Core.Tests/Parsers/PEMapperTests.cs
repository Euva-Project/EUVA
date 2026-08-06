// SPDX-License-Identifier: GPL-3.0-or-later

using EUVA.Core.Models;
using EUVA.Core.Parsers;
using Xunit;

namespace EUVA.Core.Tests.Parsers;

public class PEMapperTests
{
    [Fact]
    public void Parse_InvalidData_CreatesParseErrorNode()
    {
        // Arrange
        byte[] invalidData = [0x00, 0x01, 0x02, 0x03];
        var mapper = new PEMapper();

        // Act
        var root = mapper.Parse(invalidData);

        // Assert
        Assert.NotNull(root);
        Assert.Equal("PE File", root.Name);
        Assert.Contains(root.Children, c => c.Type == "Error");
    }

    [Fact]
    public void Parse_ValidMinimalPE_ParsesDosAndNtHeaders()
    {
        // Arrange: minimal valid PE32 byte array
        byte[] peBytes = CreateMinimalPEBytes();
        var mapper = new PEMapper();

        // Act
        var root = mapper.Parse(peBytes);

        // Assert
        Assert.NotNull(root);
        Assert.Equal("PE File", root.Name);

        var dosHeader = root.Children.FirstOrDefault(c => c.Name == "DOS Header");
        Assert.NotNull(dosHeader);
        Assert.Equal("IMAGE_DOS_HEADER", dosHeader.Type);

        var ntHeaders = root.Children.FirstOrDefault(c => c.Name == "NT Headers");
        Assert.NotNull(ntHeaders);
        Assert.Equal("IMAGE_NT_HEADERS", ntHeaders.Type);

        // Verify regions
        var regions = mapper.GetRegions();
        Assert.NotEmpty(regions);
        Assert.Contains(regions, r => r.Name == "DOS Header");
    }

    [Fact]
    public void FindRegionAt_ValidOffset_ReturnsCorrectRegion()
    {
        // Arrange
        byte[] peBytes = CreateMinimalPEBytes();
        var mapper = new PEMapper();
        mapper.Parse(peBytes);

        // Act - DOS header is at offset 0, size 64
        var region = mapper.FindRegionAt(10);

        // Assert
        Assert.NotNull(region);
        Assert.Equal("DOS Header", region.Name);
    }

    [Fact]
    public void FindRegionAt_OutOfBoundsOffset_ReturnsNull()
    {
        // Arrange
        byte[] peBytes = CreateMinimalPEBytes();
        var mapper = new PEMapper();
        mapper.Parse(peBytes);

        // Act
        var region = mapper.FindRegionAt(999999);

        // Assert
        Assert.Null(region);
    }

    private static byte[] CreateMinimalPEBytes()
    {
        byte[] bytes = new byte[512];

        // DOS Header
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        // e_lfanew at 0x3C - 0x80
        bytes[0x3C] = 0x80;
        bytes[0x3D] = 0x00;
        bytes[0x3E] = 0x00;
        bytes[0x3F] = 0x00;

        int ntOffset = 0x80;
        // NT Signature: "PE\0\0"
        bytes[ntOffset] = (byte)'P';
        bytes[ntOffset + 1] = (byte)'E';
        bytes[ntOffset + 2] = 0;
        bytes[ntOffset + 3] = 0;

        // FileHeader at ntOffset + 4 - 0x84
        int fileHeaderOffset = ntOffset + 4;
        bytes[fileHeaderOffset] = 0x4C;      // Machine - IMAGE_FILE_MACHINE_I386
        bytes[fileHeaderOffset + 1] = 0x01;
        bytes[fileHeaderOffset + 2] = 0x01;  // NumberOfSections - 1
        bytes[fileHeaderOffset + 3] = 0x00;
        bytes[fileHeaderOffset + 16] = 0xE0; // SizeOfOptionalHeader - 224
        bytes[fileHeaderOffset + 17] = 0x00;
        bytes[fileHeaderOffset + 18] = 0x02; // Characteristics - 0x0102
        bytes[fileHeaderOffset + 19] = 0x01;

        // OptionalHeader at fileHeaderOffset + 20 - 0x98
        int optHeaderOffset = fileHeaderOffset + 20;
        bytes[optHeaderOffset] = 0x0B;       // Magic - PE32
        bytes[optHeaderOffset + 1] = 0x01;

        return bytes;
    }
}
