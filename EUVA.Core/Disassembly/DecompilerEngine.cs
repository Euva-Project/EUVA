// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Msagl.Core.Geometry;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Layout.Layered;
using Point = Microsoft.Msagl.Core.Geometry.Point;

namespace EUVA.Core.Disassembly;

public sealed class LayoutResult
{
    public LayoutNode[] Nodes = Array.Empty<LayoutNode>();
    public LayoutEdge[] Edges = Array.Empty<LayoutEdge>();
    public double TotalWidth;
    public double TotalHeight;
    public PseudocodeLine[]? FullText;
}

public struct LayoutNode
{
    public int BlockIndex;
    public double X, Y, Width, Height;
    public long StartOffset;
    public int InstructionCount;
    public int ByteLength;
    public bool IsReturn;
    public bool IsFirstBlock;
    public PseudocodeLine[]? PseudocodeLines;
}

public struct LayoutEdge
{
    public int SourceBlock, TargetBlock;
    public bool IsConditional;
    public bool IsConditionalTaken; 
    public bool IsUnconditional;    
    public Point[] Points; 
}

public sealed class DecompilerEngine
{
    private readonly CfgScanner _scanner = new();
    public Dictionary<string, string> GlobalRenames { get; } = new();
    public Dictionary<string, HashSet<ulong>> GlobalStructs { get; } = new();

    
    public double CellWidth { get; set; } = 9.0;
    public double CellHeight { get; set; } = 15.0;
    public int MaxCharsPerLine { get; set; } = 48;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public unsafe LayoutResult BuildFunctionGraph(byte* data, int length, long baseAddress, int bitness, PseudocodeGenerator? pseudoGen = null, byte* fileMap = null, long fileLength = 0)
    {
        var blocks = _scanner.ScanFunction(data, length, baseAddress, bitness);
        if (blocks.Length == 0)
            return new LayoutResult();

        var graph = new GeometryGraph();
        var msaglNodes = new Node[blocks.Length];
        var layoutNodes = new LayoutNode[blocks.Length];

        
        for (int i = 0; i < blocks.Length; i++)
        {
            ref var blk = ref blocks[i];
            
            PseudocodeLine[]? pcLines = null;
            int maxChars = 15; 
            
            if (pseudoGen != null && fileMap != null && blk.ByteLength > 0)
            {
                
                pseudoGen.SetGlobalContext(GlobalRenames, GlobalStructs);
                
                pcLines = pseudoGen.Generate(fileMap + blk.StartOffset, blk.ByteLength, blk.StartOffset, bitness, blk.IsFirstBlock, fileMap, fileLength);
                if (pcLines != null)
                {
                    foreach (var line in pcLines)
                    {
                        if (line.Text != null && line.Text.Length > maxChars)
                            maxChars = line.Text.Length;
                    }
                }
            }

            double w = Math.Min(MaxCharsPerLine, maxChars) * CellWidth + 24; 
            double rawH = Math.Max(pcLines?.Length ?? Math.Max(blk.InstructionCount, 1), 1) * CellHeight + 25;
            double h = Math.Max(rawH, 40); 
            
            var node = new Node(CurveFactory.CreateRectangle(w, h, new Point(0, 0)), i.ToString());
            msaglNodes[i] = node;
            graph.Nodes.Add(node);
            
            layoutNodes[i] = new LayoutNode
            {
                BlockIndex = i,
                StartOffset = blk.StartOffset,
                InstructionCount = blk.InstructionCount,
                ByteLength = blk.ByteLength,
                IsReturn = blk.IsReturn,
                IsFirstBlock = blk.IsFirstBlock,
                PseudocodeLines = pcLines
            };
        }

        var edgeMapping = new List<(Edge MsaglEdge, int Source, int Target, bool IsCond, bool IsTaken, bool IsUncond)>();
        for (int i = 0; i < blocks.Length; i++)
        {
            var successors = blocks[i].Successors;
            if (successors == null) continue;
            
            for (int s = 0; s < successors.Length; s++)
            {
                int target = successors[s];
                if (target < 0 || target >= blocks.Length) continue;
                
                var edge = new Edge(msaglNodes[i], msaglNodes[target]);
                
                

                graph.Edges.Add(edge);
                
                
                bool isCond = blocks[i].IsConditional;
                bool isTaken = isCond && s > 0;
                bool isUncond = !isCond;
                
                
                
                if (isCond && !isTaken)
                    edge.Weight = 50;   
                else if (isUncond)
                    edge.Weight = 15;   
                else
                    edge.Weight = 10;   
                
                edgeMapping.Add((edge, i, target, isCond, isTaken, isUncond));
            }
        }

        var settings = new SugiyamaLayoutSettings
        {
            NodeSeparation = 15,     
            LayerSeparation = 25,    
        };
        
        settings.EdgeRoutingSettings.EdgeRoutingMode = Microsoft.Msagl.Core.Routing.EdgeRoutingMode.Rectilinear;
        settings.EdgeRoutingSettings.Padding = 4;
        settings.EdgeRoutingSettings.CornerRadius = 2.0;
        
        settings.EdgeRoutingSettings.RouteMultiEdgesAsBundles = true;
        
        settings.EdgeRoutingSettings.BendPenalty = 2.0;

        var layout = new LayeredLayout(graph, settings);
        layout.Run();

        double minX = graph.BoundingBox.Left;
        double maxY = graph.BoundingBox.Top;

        var result = new LayoutResult
        {
            Nodes = layoutNodes,
            TotalWidth = graph.BoundingBox.Width,
            TotalHeight = graph.BoundingBox.Height
        };

        
        for (int i = 0; i < blocks.Length; i++)
        {
            var bbox = msaglNodes[i].BoundingBox;
            result.Nodes[i].X = bbox.Left - minX;
            result.Nodes[i].Y = maxY - bbox.Top;
            result.Nodes[i].Width = bbox.Width;
            result.Nodes[i].Height = bbox.Height;
        }

        var edgeList = new List<LayoutEdge>(edgeMapping.Count);
        foreach (var map in edgeMapping)
        {
            var pts = new List<Point>();
            var msaglEdge = map.MsaglEdge;

            if (msaglEdge.Curve != null)
            {
                
                FlattenCurve(msaglEdge.Curve, pts);
                
                
                for (int p = 0; p < pts.Count; p++)
                    pts[p] = new Point(pts[p].X - minX, maxY - pts[p].Y);
            }

            
            if (msaglEdge.EdgeGeometry?.TargetArrowhead?.TipPosition is Point tip)
                pts.Add(new Point(tip.X - minX, maxY - tip.Y));

            edgeList.Add(new LayoutEdge
            {
                SourceBlock = map.Source,
                TargetBlock = map.Target,
                IsConditional = map.IsCond,
                IsConditionalTaken = map.IsTaken,
                IsUnconditional = map.IsUncond,
                Points = pts.ToArray()
            });
        }

        result.Edges = edgeList.ToArray();

        
        if (pseudoGen != null && fileMap != null && blocks.Length > 0)
        {
            result.FullText = pseudoGen.DecompileFunction(blocks, fileMap, fileLength, baseAddress);
        }

        return result;
    }

    private static void FlattenCurve(ICurve curve, List<Point> points)
    {
        if (curve is LineSegment ls)
        {
            
            if (points.Count == 0 || DistSq(points[^1], ls.Start) > 0.01)
                points.Add(ls.Start);
            points.Add(ls.End);
        }
        else if (curve is Curve composite)
        {
            
            foreach (var seg in composite.Segments)
            {
                if (seg is LineSegment lseg)
                {
                    
                    if (points.Count == 0 || DistSq(points[^1], lseg.Start) > 0.01)
                        points.Add(lseg.Start);
                    points.Add(lseg.End);
                }
                else
                {
                    
                    SampleSegment(seg, points, 12);
                }
            }
        }
        else
        {
            
            SampleSegment(curve, points, 16);
        }
    }

    private static void SampleSegment(ICurve seg, List<Point> points, int minSamples)
    {
        double parLen = seg.ParEnd - seg.ParStart;
        int samples = Math.Max(minSamples, (int)(parLen / 1.5));
        
        for (int i = 0; i <= samples; i++)
        {
            double t = seg.ParStart + parLen * i / samples;
            var pt = seg[t];
            
            if (points.Count > 0 && DistSq(points[^1], pt) < 0.01)
                continue;
            points.Add(pt);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double DistSq(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }
}