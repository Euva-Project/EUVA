// SPDX-License-Identifier: GPL-3.0-or-later

using System.Buffers;
using System.Collections.Concurrent;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EUVA.Core.Disassembly;

namespace EUVA.UI.Controls.Decompilation;


public sealed class DecompilerGraphView : FrameworkElement, IDisposable
{
    
    private sealed class GlyphCache
    {
        private readonly ConcurrentDictionary<long, uint[]> _cache = new(1, 2048);
        private readonly double _fontSize;
        private readonly int _cellW, _cellH;
        private readonly double _pixelsPerDip;

        public int CellW => _cellW;
        public int CellH => _cellH;

        public GlyphCache(GlyphTypeface gtf, double fontSize, int cellW, int cellH, double ppd)
        {
            _fontSize = fontSize; _cellW = cellW; _cellH = cellH; _pixelsPerDip = ppd;
        }

        public uint[] Get(char c, uint colorArgb)
        {
            long key = ((long)(ushort)c << 32) | colorArgb;
            return _cache.GetOrAdd(key, _ => RasterizeGlyph(c, colorArgb));
        }

        public void Clear() => _cache.Clear();

        private uint[] RasterizeGlyph(char c, uint colorArgb)
        {
            double dipW = _cellW / _pixelsPerDip;
            double dipH = _cellH / _pixelsPerDip;
            double dpi = 96.0 * _pixelsPerDip;
            byte r = (byte)(colorArgb >> 16), g = (byte)(colorArgb >> 8), b = (byte)colorArgb;
            var brush = new SolidColorBrush(Color.FromArgb(255, r, g, b)); brush.Freeze();

            var dv = new DrawingVisual();
            TextOptions.SetTextRenderingMode(dv, TextRenderingMode.Aliased);
            TextOptions.SetTextFormattingMode(dv, TextFormattingMode.Display);
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, dipW, dipH));
                var tf = new Typeface(new FontFamily("Consolas"),
                    FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                dc.DrawText(new FormattedText(c.ToString(),
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, tf, _fontSize, brush, _pixelsPerDip),
                    new Point(0, 0));
            }
            var rtb = new RenderTargetBitmap(_cellW, _cellH, dpi, dpi, PixelFormats.Pbgra32);
            rtb.Render(dv);
            int stride = _cellW * 4;
            byte[] raw = new byte[_cellH * stride];
            rtb.CopyPixels(raw, stride, 0);
            var result = new uint[_cellW * _cellH];
            for (int i = 0; i < result.Length; i++)
            {
                byte pa = raw[i * 4 + 3];
                if (pa == 0) { result[i] = 0; continue; }
                result[i] = ((uint)pa << 24) | ((uint)raw[i * 4 + 2] << 16) |
                            ((uint)raw[i * 4 + 1] << 8) | raw[i * 4];
            }
            return result;
        }
    }

    private MemoryMappedViewAccessor? _accessor;
    private MemoryMappedFile? _mmf;
    private long _fileLength;
    private readonly DisassemblyEngine _engine = new(32);
    private LayoutResult? _layout;
    private PseudocodeGenerator? _pseudoGen;
    private string? _highlightReg;

    
    private double _offsetX, _offsetY; 
    private double _zoom = 1.0;
    private const double MinZoom = 0.15;
    private const double MaxZoom = 3.0;

    
    private bool _dragging;
    private Point _dragStart;
    private double _dragOffsetX, _dragOffsetY;

    
    private const double FontSize = 12;
    private const double CharWidth = 8;
    private const double LineHeight = 15;
    private const int NodePadX = 8;
    private const int NodePadY = 5;
    private const int HeaderH = 18;

    private double _pixelsPerDip = 1.0;
    private int CellW => (int)Math.Ceiling(CharWidth * _pixelsPerDip);
    private int CellH => (int)Math.Ceiling(LineHeight * _pixelsPerDip);

    private WriteableBitmap? _bitmap;
    private uint[] _backBuffer = Array.Empty<uint>();
    private int _bmpW, _bmpH;
    private bool _needsRedraw = true;
    private GlyphCache? _glyphCache;
    private readonly Image _image = new() { Stretch = Stretch.None };

    
    private uint _cBg, _cNodeBg, _cNodeBorder, _cNodeHeader, _cHeaderText;
    private uint _cText, _cMnem, _cReg, _cNum, _cKw, _cPunct;
    private uint _cEdgeFallthrough, _cEdgeTaken, _cEdgeDefault, _cEdgeUnconditional;
    private uint _cOffset;
    
    private uint _cPcKeyword, _cPcType, _cPcVariable, _cPcVariableAi, _cPcNumber, _cPcString;
    private uint _cPcFunction, _cPcOperator, _cPcPunct, _cPcComment, _cPcAddress;
    private uint _cHighlight;

    
    public event EventHandler<long>? BlockSelected;

    public DecompilerGraphView()
    {
        ClipToBounds = true; Focusable = true;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(_image, EdgeMode.Aliased);
        _image.SnapsToDevicePixels = true;
        AddVisualChild(_image); AddLogicalChild(_image);

        Loaded += (_, _) =>
        {
            _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            InitColors(); RebuildGlyphs(); Redraw();
        };
        SizeChanged += (_, _) =>
        {
            int w = (int)Math.Max(1, ActualWidth * _pixelsPerDip);
            int h = (int)Math.Max(1, ActualHeight * _pixelsPerDip);
            ResizeBmp(w, h); Redraw();
        };
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _image;
    protected override Size MeasureOverride(Size a)
    {
        double w = double.IsInfinity(a.Width) ? 800 : a.Width;
        double h = double.IsInfinity(a.Height) ? 600 : a.Height;
        _image.Measure(new Size(w, h)); return new Size(w, h);
    }
    protected override Size ArrangeOverride(Size s) { _image.Arrange(new Rect(s)); return s; }

    public void SetDataSource(MemoryMappedFile mmf, MemoryMappedViewAccessor acc, long len)
    {
        _mmf = mmf; _accessor = acc; _fileLength = len;
        Redraw();
    }

    public void SetBitness(int b) { _engine.Bitness = b; }

    public void SetPseudocodeGenerator(PseudocodeGenerator gen) { _pseudoGen = gen; }

    public void SetHighlightedRegister(string? reg)
    {
        _highlightReg = reg?.ToLowerInvariant();
        Redraw();
    }

    public void SetGraphData(LayoutResult layout)
    {
        _layout = layout;
        if (layout != null)
            AutoFit();
        Redraw();
    }

    public void RefreshView() => Redraw();

    private void AutoFit()
    {
        if (_layout == null || _layout.TotalWidth <= 0 || _layout.TotalHeight <= 0 || _bmpW <= 0 || _bmpH <= 0)
            return;

        double scaleX = (_bmpW / _pixelsPerDip) / _layout.TotalWidth;
        double scaleY = (_bmpH / _pixelsPerDip) / _layout.TotalHeight;
        _zoom = Math.Clamp(Math.Min(scaleX, scaleY) * 0.9, MinZoom, MaxZoom);

        _offsetX = ((_bmpW / _pixelsPerDip) - _layout.TotalWidth * _zoom) / 2.0 * _pixelsPerDip;
        
        double firstBlockY = 0;
        if (_layout.Nodes.Length > 0)
        {
            for (int i = 0; i < _layout.Nodes.Length; i++)
            {
                if (_layout.Nodes[i].IsFirstBlock)
                {
                    firstBlockY = _layout.Nodes[i].Y;
                    break;
                }
            }
        }
        _offsetY = (50 * _pixelsPerDip) - (firstBlockY * _zoom * _pixelsPerDip);
    }

    private void InitColors()
    {
        _cBg             = C(Color.FromRgb(0x11, 0x11, 0x1B));
        _cNodeBg         = C(Color.FromRgb(0x1E, 0x1E, 0x2E));
        _cNodeBorder     = C(Color.FromRgb(0x45, 0x47, 0x5A));
        _cNodeHeader     = C(Color.FromRgb(0x24, 0x27, 0x3A));
        _cHeaderText     = C(Color.FromRgb(0x89, 0xB4, 0xFA));
        _cText           = C(Color.FromRgb(0xCD, 0xD6, 0xF4));
        _cMnem           = C(Color.FromRgb(0x89, 0xB4, 0xFA));
        _cReg            = C(Color.FromRgb(0xF3, 0x8B, 0xA8));
        _cNum            = C(Color.FromRgb(0xFA, 0xB3, 0x87));
        _cKw             = C(Color.FromRgb(0xCB, 0xA6, 0xF7));
        _cPunct          = C(Color.FromRgb(0x6C, 0x70, 0x86));
        _cOffset         = C(Color.FromRgb(0x6C, 0x70, 0x86));
        _cEdgeFallthrough = C(Color.FromRgb(0xF3, 0x8B, 0xA8)); 
        _cEdgeTaken      = C(Color.FromRgb(0xA6, 0xE3, 0xA1));  
        _cEdgeDefault    = C(Color.FromRgb(0x58, 0x5B, 0x70));   
        _cEdgeUnconditional = C(Color.FromRgb(0x74, 0xC7, 0xEC)); 

        
        _cPcKeyword  = C(Color.FromRgb(0xCB, 0xA6, 0xF7)); 
        _cPcType     = C(Color.FromRgb(0x89, 0xB4, 0xFA)); 
        _cPcVariable = C(Color.FromRgb(0xF3, 0x8B, 0xA8)); 
        _cPcVariableAi = C(Color.FromRgb(0xCB, 0xA6, 0xF7)); 
        _cPcNumber   = C(Color.FromRgb(0xFA, 0xB3, 0x87)); 
        _cPcString   = C(Color.FromRgb(0xA6, 0xE3, 0xA1)); 
        _cPcFunction = C(Color.FromRgb(0x89, 0xDC, 0xEB)); 
        _cPcOperator = C(Color.FromRgb(0x94, 0xE2, 0xD5)); 
        _cPcPunct    = C(Color.FromRgb(0x6C, 0x70, 0x86)); 
        _cPcComment  = C(Color.FromRgb(0x58, 0x5B, 0x70)); 
        _cPcAddress  = C(Color.FromRgb(0x6C, 0x70, 0x86)); 
        _cHighlight  = C(Color.FromArgb(0x50, 0xF3, 0x8B, 0xA8)); 
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint C(Color c) => ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

    private void RebuildGlyphs()
    {
        if (_pixelsPerDip == 0) return;
        _glyphCache?.Clear();
        var tf = new Typeface(new FontFamily("Consolas"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        tf.TryGetGlyphTypeface(out var gtf);
        _glyphCache = new GlyphCache(gtf, FontSize, CellW, CellH, _pixelsPerDip);

        System.Threading.Tasks.Task.Run(() =>
        {
            var gc = _glyphCache; if (gc == null) return;
            uint[] cols = { _cText, _cMnem, _cReg, _cNum, _cKw, _cPunct, _cHeaderText, _cOffset };
            foreach (var col in cols)
                for (char c = ' '; c <= '~'; c++) gc.Get(c, col);
            Dispatcher.BeginInvoke(Redraw);
        });
    }

    private void ResizeBmp(int w, int h)
    {
        if (w <= 0 || h <= 0 || (w == _bmpW && h == _bmpH)) return;
        _bmpW = w; _bmpH = h;
        _bitmap = new WriteableBitmap(w, h, 96 * _pixelsPerDip, 96 * _pixelsPerDip, PixelFormats.Bgra32, null);
        _backBuffer = new uint[w * h];
        _image.Source = _bitmap;
        _needsRedraw = true;
    }

    private void Redraw() { _needsRedraw = true; InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        if (_bitmap == null || _bmpW == 0) return;

        if (_layout == null || _glyphCache == null)
        {
            Fill(_backBuffer, _bmpW, _bmpH, _cBg);
            Str("No graph data — select a function to decompile",
                (int)(10 * _pixelsPerDip), _bmpH / 2, _cOffset);
            FlushBitmap(); return;
        }

        if (!_needsRedraw) return;
        _needsRedraw = false;

        Fill(_backBuffer, _bmpW, _bmpH, _cBg);

        
        foreach (ref var edge in _layout.Edges.AsSpan())
        {
            uint edgeColor;
            if (edge.IsConditional)
                edgeColor = edge.IsConditionalTaken ? _cEdgeTaken : _cEdgeFallthrough;
            else if (edge.IsUnconditional)
                edgeColor = _cEdgeUnconditional;
            else
                edgeColor = _cEdgeDefault;

            if (!_layout.Nodes[edge.SourceBlock].IsReturn)
                DrawEdge(edge.Points, edgeColor);
        }

        
        unsafe
        {
            byte* mapPtr = null;
            if (_accessor != null)
                _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref mapPtr);
            try
            {
                foreach (ref var node in _layout.Nodes.AsSpan())
                    RenderNode(ref node, mapPtr);
            }
            finally
            {
                if (mapPtr != null && _accessor != null)
                    _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }

        FlushBitmap();
    }

    private unsafe void RenderNode(ref LayoutNode node, byte* mapPtr)
    {
        
        int sx = WorldToScreenX(node.X);
        int sy = WorldToScreenY(node.Y);
        int sw = (int)(node.Width * _zoom * _pixelsPerDip);
        int sh = (int)(node.Height * _zoom * _pixelsPerDip);

        
        if (sx + sw < 0 || sx > _bmpW || sy + sh < 0 || sy > _bmpH) return;

        
        FillRect(sx, sy, sw, sh, _cNodeBg);

        
        FillRect(sx, sy, sw, (int)_pixelsPerDip, _cNodeBorder);         
        FillRect(sx, sy + sh - (int)_pixelsPerDip, sw, (int)_pixelsPerDip, _cNodeBorder); 
        FillRect(sx, sy, (int)_pixelsPerDip, sh, _cNodeBorder);         
        FillRect(sx + sw - (int)_pixelsPerDip, sy, (int)_pixelsPerDip, sh, _cNodeBorder); 

        
        int headerPx = (int)(HeaderH * _zoom * _pixelsPerDip);
        FillRect(sx, sy, sw, headerPx, _cNodeHeader);
        int padX = (int)(NodePadX * _zoom * _pixelsPerDip);
        int padY = (int)(NodePadY * _zoom * _pixelsPerDip);

        
        if (_zoom >= 0.4)
        {
            string hdr = $"0x{node.StartOffset:X8}";
            if (node.IsReturn) hdr += " [RET]";
            StrScaled(hdr, sx + padX, sy + padY / 2, _cHeaderText);
        }

        
        if (mapPtr != null && _accessor != null && node.ByteLength > 0)
        {
            if (_zoom < 0.7)
            {
                
                int pcLinesCount = node.PseudocodeLines != null 
                    ? node.PseudocodeLines.Length 
                    : Math.Max(1, node.InstructionCount);
                int y = sy + headerPx + padY;
                int lineH = (int)(LineHeight * _zoom * _pixelsPerDip);
                int greekH = Math.Max(1, (int)(2 * _pixelsPerDip));
                if (lineH < greekH + 1) lineH = greekH + 1;
                
                for (int i = 0; i < pcLinesCount; i++)
                {
                    if (y > _bmpH) break;
                    int lw = (int)(sw * 0.4 + (i * 13 % 40) * 0.01 * sw);
                    if (lw > sw - padX * 2) lw = sw - padX * 2;
                    FillRect(sx + padX, y, lw, greekH, _cText); 
                    y += lineH;
                }
            }
            else
            {
                long actOffset = node.StartOffset - (long)_accessor.PointerOffset;
                if (actOffset >= 0 && actOffset + node.ByteLength <= _fileLength)
                {
                    int instrY = sy + headerPx + padY;
                    int lineH = (int)(LineHeight * _zoom * _pixelsPerDip);
                    int charW = (int)(CharWidth * _zoom * _pixelsPerDip);
                    if (charW < 1) charW = 1;
                    if (lineH < 1) lineH = 1;

                    if (_pseudoGen != null)
                    {
                        
                        if (node.PseudocodeLines == null)
                        {
                            unsafe
                            {
                                node.PseudocodeLines = _pseudoGen.Generate(
                                    mapPtr + actOffset, node.ByteLength, node.StartOffset,
                                    _engine.Bitness, node.IsFirstBlock,
                                    mapPtr, _fileLength);
                            }
                        }

                        var pcLines = node.PseudocodeLines;
                        for (int li = 0; li < pcLines.Length; li++)
                        {
                            if (instrY > _bmpH) break;
                            ref var pcLine = ref pcLines[li];
                            if (string.IsNullOrEmpty(pcLine.Text)) { instrY += lineH; continue; }

                            
                            if (_highlightReg != null && pcLine.Text.Contains(_highlightReg, StringComparison.OrdinalIgnoreCase))
                                FillRect(sx + padX, instrY, sw - padX * 2, lineH, _cHighlight);

                            
                            if (pcLine.Spans != null)
                            {
                                foreach (var span in pcLine.Spans)
                                {
                                    uint col = PseudocodeSyntaxColor(span.Kind);
                                    int end = Math.Min(span.Start + span.Length, pcLine.Text.Length);
                                    for (int ci = span.Start; ci < end; ci++)
                                    {
                                        BlitScaled(pcLine.Text[ci], sx + padX + ci * charW, instrY, charW, lineH, col);
                                    }
                                }
                            }
                            else
                            {
                                
                                for (int ci = 0; ci < pcLine.Text.Length; ci++)
                                    BlitScaled(pcLine.Text[ci], sx + padX + ci * charW, instrY, charW, lineH, _cText);
                            }
                            instrY += lineH;
                        }
                    }
                    else
                    {
                        
                        var lines = ArrayPool<DisassembledLine>.Shared.Rent(node.InstructionCount + 4);
                        try
                        {
                            int decoded = _engine.DecodeVisible(
                                mapPtr + actOffset, node.ByteLength, node.StartOffset,
                                lines, node.InstructionCount + 2);

                            for (int li = 0; li < decoded && li < node.InstructionCount; li++)
                            {
                                if (instrY > _bmpH) break;
                                ref var ln = ref lines[li];

                                fixed (char* txt = ln.TextBuffer)
                                fixed (byte* cmap = ln.TextColorMap)
                                {
                                    for (int ci = 0; ci < ln.TextLength; ci++)
                                    {
                                        uint col = SyntaxColor(cmap[ci]);
                                        BlitScaled(txt[ci], sx + padX + ci * charW, instrY, charW, lineH, col);
                                    }
                                }
                                instrY += lineH;
                            }
                        }
                        finally
                        {
                            ArrayPool<DisassembledLine>.Shared.Return(lines, true);
                        }
                    }
                }
            }
        }
    }

    private void DrawEdge(Microsoft.Msagl.Core.Geometry.Point[] points, uint color)
    {
        if (points == null || points.Length < 2) return;

        for (int i = 0; i < points.Length - 1; i++)
        {
            int x0 = WorldToScreenX(points[i].X);
            int y0 = WorldToScreenY(points[i].Y);
            int x1 = WorldToScreenX(points[i + 1].X);
            int y1 = WorldToScreenY(points[i + 1].Y);
            DrawLine(x0, y0, x1, y1, color);
        }

        
        if (points.Length >= 2)
        {
            int ax = WorldToScreenX(points[^1].X);
            int ay = WorldToScreenY(points[^1].Y);
            int sz = Math.Max(2, (int)(4 * _zoom * _pixelsPerDip));
            FillRect(ax - sz, ay - sz, sz * 2, sz * 2, color);
        }
    }

    
    private void DrawLine(int x0, int y0, int x1, int y1, uint color)
    {
        
        if ((x0 < 0 && x1 < 0) || (x0 >= _bmpW && x1 >= _bmpW)) return;
        if ((y0 < 0 && y1 < 0) || (y0 >= _bmpH && y1 >= _bmpH)) return;

        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;

        int limit = Math.Max(dx, -dy) * 2 + 2;
        int step = 0;

        while (step++ < limit)
        {
            if ((uint)x0 < (uint)_bmpW && (uint)y0 < (uint)_bmpH)
                _backBuffer[y0 * _bmpW + x0] = color | 0xFF000000u;

            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint SyntaxColor(byte cat) => cat switch
    {
        DisassembledLine.ColMnemonic    => _cMnem,
        DisassembledLine.ColRegister    => _cReg,
        DisassembledLine.ColNumber      => _cNum,
        DisassembledLine.ColKeyword     => _cKw,
        DisassembledLine.ColPunctuation => _cPunct,
        _ => _cText,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint PseudocodeSyntaxColor(PseudocodeSyntax kind) => kind switch
    {
        PseudocodeSyntax.Keyword     => _cPcKeyword,
        PseudocodeSyntax.Type        => _cPcType,
        PseudocodeSyntax.Variable    => _cPcVariable,
        PseudocodeSyntax.VariableAi  => _cPcVariableAi,
        PseudocodeSyntax.Number      => _cPcNumber,
        PseudocodeSyntax.String      => _cPcString,
        PseudocodeSyntax.Function    => _cPcFunction,
        PseudocodeSyntax.Operator    => _cPcOperator,
        PseudocodeSyntax.Punctuation => _cPcPunct,
        PseudocodeSyntax.Comment     => _cPcComment,
        PseudocodeSyntax.Address     => _cPcAddress,
        _ => _cText,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int WorldToScreenX(double wx) => (int)((wx * _zoom * _pixelsPerDip) + _offsetX);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int WorldToScreenY(double wy) => (int)((wy * _zoom * _pixelsPerDip) + _offsetY);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double ScreenToWorldX(double sx) => (sx - _offsetX) / (_zoom * _pixelsPerDip);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double ScreenToWorldY(double sy) => (sy - _offsetY) / (_zoom * _pixelsPerDip);

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e); Focus();
        if (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right)
        {
            _dragging = true;
            _dragStart = e.GetPosition(this);
            _dragOffsetX = _offsetX;
            _dragOffsetY = _offsetY;
            CaptureMouse();
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Left && _layout != null)
        {
            
            var pos = e.GetPosition(this);
            double wx = ScreenToWorldX(pos.X * _pixelsPerDip);
            double wy = ScreenToWorldY(pos.Y * _pixelsPerDip);
            foreach (ref var node in _layout.Nodes.AsSpan())
            {
                if (wx >= node.X && wx <= node.X + node.Width &&
                    wy >= node.Y && wy <= node.Y + node.Height)
                {
                    BlockSelected?.Invoke(this, node.StartOffset);
                    break;
                }
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;
        var pos = e.GetPosition(this);
        double dx = (pos.X - _dragStart.X) * _pixelsPerDip;
        double dy = (pos.Y - _dragStart.Y) * _pixelsPerDip;
        _offsetX = _dragOffsetX + dx;
        _offsetY = _dragOffsetY + dy;
        Redraw();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var pos = e.GetPosition(this);
        double mouseX = pos.X * _pixelsPerDip;
        double mouseY = pos.Y * _pixelsPerDip;

        double factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        double newZoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        double actualScale = newZoom / _zoom;

        _offsetX = mouseX - (mouseX - _offsetX) * actualScale;
        _offsetY = mouseY - (mouseY - _offsetY) * actualScale;
        _zoom = newZoom;

        Redraw();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        double step = 50 / _zoom;
        switch (e.Key)
        {
            case Key.Left:  _offsetX += step * _zoom * _pixelsPerDip; Redraw(); e.Handled = true; break;
            case Key.Right: _offsetX -= step * _zoom * _pixelsPerDip; Redraw(); e.Handled = true; break;
            case Key.Up:    _offsetY += step * _zoom * _pixelsPerDip; Redraw(); e.Handled = true; break;
            case Key.Down:  _offsetY -= step * _zoom * _pixelsPerDip; Redraw(); e.Handled = true; break;
            case Key.Home:
                AutoFit();
                Redraw(); e.Handled = true; break;
        }
    }

    private void Str(string text, int x, int y, uint col)
    {
        if (_glyphCache == null) return;
        for (int i = 0; i < text.Length; i++)
            Blit(text[i], x + i * CellW, y, col);
    }

    private void StrScaled(string text, int x, int y, uint col)
    {
        if (_glyphCache == null) return;
        int scaledCellW = (int)(CharWidth * _zoom * _pixelsPerDip);
        int scaledCellH = (int)(LineHeight * _zoom * _pixelsPerDip);
        if (scaledCellW < 1 || scaledCellH < 1) return;
        for (int i = 0; i < text.Length; i++)
            BlitScaled(text[i], x + i * scaledCellW, y, scaledCellW, scaledCellH, col);
    }

    private void BlitScaled(char c, int dx, int dy, int dw, int dh, uint color)
    {
        if (_glyphCache == null || dw <= 0 || dh <= 0) return;
        var glyph = _glyphCache.Get(c, color);
        int cw = _glyphCache.CellW, ch = _glyphCache.CellH;
        
        int startR = dy < 0 ? -dy : 0;
        int endR = dy + dh > _bmpH ? _bmpH - dy : dh;
        int startC = dx < 0 ? -dx : 0;
        int endC = dx + dw > _bmpW ? _bmpW - dx : dw;

        if (startR >= endR || startC >= endC) return;
        
        int bufLen = _backBuffer.Length;

        for (int r = startR; r < endR; r++)
        {
            int sy = (r * ch) / dh;
            int dRow = (dy + r) * _bmpW + dx;
            int sRow = sy * cw;
            
            for (int col = startC; col < endC; col++)
            {
                int sx = (col * cw) / dw;
                int dIdx = dRow + col;
                
                uint sp = glyph[sRow + sx];
                byte sa = (byte)(sp >> 24);
                if (sa == 0) continue;
                if (sa == 255) { _backBuffer[dIdx] = sp; continue; }
                
                uint dp = _backBuffer[dIdx];
                int ia = 255 - sa;
                _backBuffer[dIdx] = 0xFF000000u
                    | (uint)((((sp >> 16) & 0xFF) + (((dp >> 16) & 0xFF) * ia + 127 >> 8)) << 16)
                    | (uint)((((sp >> 8) & 0xFF) + (((dp >> 8) & 0xFF) * ia + 127 >> 8)) << 8)
                    | (uint)(((sp & 0xFF) + ((dp & 0xFF) * ia + 127 >> 8)));
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Blit(char c, int dx, int dy, uint color)
    {
        if (_glyphCache == null) return;
        var glyph = _glyphCache.Get(c, color);
        int cw = _glyphCache.CellW, ch = _glyphCache.CellH;
        int sx = 0, sy = 0, dw = cw, dh = ch;
        if (dx < 0) { sx -= dx; dw += dx; dx = 0; }
        if (dy < 0) { sy -= dy; dh += dy; dy = 0; }
        if (dx + dw > _bmpW) dw = _bmpW - dx;
        if (dy + dh > _bmpH) dh = _bmpH - dy;
        if (dw <= 0 || dh <= 0) return;

        int bufLen = _backBuffer.Length;
        for (int r = 0; r < dh; r++)
        {
            int dRow = (dy + r) * _bmpW + dx;
            int sRow = (sy + r) * cw + sx;
            for (int col = 0; col < dw; col++)
            {
                int dIdx = dRow + col;
                if ((uint)dIdx >= (uint)bufLen) continue;
                uint sp = glyph[sRow + col];
                byte sa = (byte)(sp >> 24);
                if (sa == 0) continue;
                if (sa == 255) { _backBuffer[dIdx] = sp; continue; }
                uint dp = _backBuffer[dIdx];
                int ia = 255 - sa;
                _backBuffer[dIdx] = 0xFF000000u
                    | (uint)((((sp >> 16) & 0xFF) + (((dp >> 16) & 0xFF) * ia + 127 >> 8)) << 16)
                    | (uint)((((sp >> 8) & 0xFF) + (((dp >> 8) & 0xFF) * ia + 127 >> 8)) << 8)
                    | (uint)(((sp & 0xFF) + ((dp & 0xFF) * ia + 127 >> 8)));
            }
        }
    }

    private void FillRect(int x, int y, int w, int h, uint color)
    {
        if (x < 0) { w += x; x = 0; }
        if (y < 0) { h += y; y = 0; }
        int x2 = Math.Min(x + w, _bmpW);
        int y2 = Math.Min(y + h, _backBuffer.Length / Math.Max(1, _bmpW));
        if (x >= x2 || y >= y2) return;
        byte sa = (byte)(color >> 24); if (sa == 0) return;
        byte sr = (byte)(color >> 16), sg = (byte)(color >> 8), sb = (byte)color;

        if (sa == 255)
        {
            uint solid = 0xFF000000u | ((uint)sr << 16) | ((uint)sg << 8) | sb;
            for (int r = y; r < y2; r++)
                _backBuffer.AsSpan(r * _bmpW + x, x2 - x).Fill(solid);
        }
        else
        {
            int ia = 255 - sa;
            for (int r = y; r < y2; r++)
            {
                int rs = r * _bmpW;
                for (int c = x; c < x2; c++)
                {
                    uint d = _backBuffer[rs + c];
                    _backBuffer[rs + c] = 0xFF000000u
                        | (uint)((sr * sa + (byte)(d >> 16) * ia + 127) >> 8) << 16
                        | (uint)((sg * sa + (byte)(d >> 8) * ia + 127) >> 8) << 8
                        | (uint)((sb * sa + (byte)d * ia + 127) >> 8);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Fill(uint[] buf, int w, int h, uint col)
        => buf.AsSpan(0, w * h).Fill(col | 0xFF000000u);

    private unsafe void FlushBitmap()
    {
        if (_bitmap == null) return;
        _bitmap.Lock();
        try
        {
            fixed (uint* src = _backBuffer)
                Buffer.MemoryCopy(src, (void*)_bitmap.BackBuffer,
                    (long)_bmpW * _bmpH * 4, (long)_bmpW * _bmpH * 4);
            _bitmap.AddDirtyRect(new Int32Rect(0, 0, _bmpW, _bmpH));
        }
        finally { _bitmap.Unlock(); }
    }

    public void Dispose()
    {
        _glyphCache?.Clear();
        _glyphCache = null;
        _accessor?.Dispose();
        _accessor = null;
    }
}
