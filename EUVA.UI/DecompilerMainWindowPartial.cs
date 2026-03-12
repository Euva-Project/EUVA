// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO.MemoryMappedFiles;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AsmResolver.PE;
using AsmResolver.PE.File;
using EUVA.Core.Disassembly;
using EUVA.UI.Controls;
using EUVA.UI.Controls.Decompilation;
using EUVA.UI.Controls.Hex;
using static EUVA.UI.Controls.Hex.DisassemblerHexView;
using System.Collections.Generic;
using EUVA.Core.Disassembly.Analysis;
using EUVA.Core.Services;
using EUVA.UI.Windows;
using EUVA.UI.Theming;
using EUVA.UI.Helpers;

namespace EUVA.UI;

public partial class MainWindow
{
    private TabItem? _decompTabItem;
    private DisassemblerHexView? _decompDisasmView;
    private DecompilerGraphView? _decompGraphView;
    private DecompilerTextView? _decompTextView;
    private readonly DecompilerEngine _decompEngine = new();
    private readonly PseudocodeGenerator _pseudocodeGen = new();
    private long _currentFunctionOffset = -1;
    private bool _textModeActive;
    private Grid? _decompRightPanel; 
    private readonly Stack<Dictionary<string, VariableSymbol>> _aiRenameHistory = new();

    

    private void MenuDecompiler_Click(object sender, RoutedEventArgs e)
    {
        if (HexView.FileLength == 0)
        {
            MessageBox.Show("Please load a PE file first.", "No File",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        EnsureCenterTabControl();

        if (_decompTabItem != null)
        {
            _centerTabControl!.SelectedItem = _decompTabItem;
            _decompDisasmView!.ScrollToOffset(HexView.CurrentScrollLine * HexView.BytesPerLine);
            LogMessage("[Decomp] Switched to Disasm+Decompiler tab.");
            return;
        }

        
        _decompDisasmView = new DisassemblerHexView();
        _decompDisasmView.DisplayMode = DisasmDisplayMode.DisasmOnly;
        var mmf = HexView.GetMemoryMappedFile();
        if (mmf != null)
        {
            var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            _decompDisasmView.SetDataSource(mmf, accessor, HexView.FileLength);
        }
        _decompDisasmView.OffsetSelected += DecompDisasmView_OffsetSelected;

        
        SetDecompPeInfo(_decompDisasmView);

        
        _decompGraphView = new DecompilerGraphView();
        _decompGraphView.SetPseudocodeGenerator(_pseudocodeGen);
        if (mmf != null)
        {
            var accessor2 = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            _decompGraphView.SetDataSource(mmf, accessor2, HexView.FileLength);
        }
        _decompGraphView.BlockSelected += DecompGraphView_BlockSelected;

        
        _decompTextView = new DecompilerTextView();
        _decompTextView.SetPseudocodeGenerator(_pseudocodeGen);
        _decompTextView.RenameApplied += (_, _) => 
        {
            if (_currentFunctionOffset >= 0)
            {
                AnalyzeFunction(_currentFunctionOffset); 
                LogMessage("[Decomp] UI refreshed based on global rename.");
            }
        };
        _decompTextView.Visibility = Visibility.Collapsed;
        _textModeActive = false;

        
        _decompRightPanel = new Grid();
        _decompRightPanel.Children.Add(_decompGraphView);
        _decompRightPanel.Children.Add(_decompTextView);

        
        var splitGrid = new Grid();
        splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(_decompDisasmView, 0);
        splitGrid.Children.Add(_decompDisasmView);

        var splitter = new GridSplitter
        {
            Width = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A)),
            Cursor = System.Windows.Input.Cursors.SizeWE
        };
        Grid.SetColumn(splitter, 1);
        splitGrid.Children.Add(splitter);

        Grid.SetColumn(_decompRightPanel, 2);
        splitGrid.Children.Add(_decompRightPanel);

        
        var dock = new DockPanel();
        dock.PreviewKeyDown += DecompDock_PreviewKeyDown;
        var toolbar = BuildDecompToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        dock.Children.Add(toolbar);
        dock.Children.Add(splitGrid);

        _decompTabItem = new TabItem
        {
            Header = "Disasm + Decompiler",
            Content = dock
        };

        _centerTabControl!.Items.Add(_decompTabItem);
        _centerTabControl.SelectedItem = _decompTabItem;

        
        TryAutoAnalyzeEntryPoint();

        LogMessage("[Decomp] Decompiler tab created.");
    }

    private Border BuildDecompToolbar()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(4, 2, 4, 2)
        };

        
        var epBtn = new Button
        {
            Content = "⏎ Entry Point",
            FontSize = 12,
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A)),
        };
        epBtn.Click += (_, _) =>
        {
            if (_decompDisasmView == null) return;
            long ep = _decompDisasmView.EntryPointFileOffset;
            if (ep >= 0 && ep < _decompDisasmView.FileLength)
            {
                _decompDisasmView.ScrollToOffset(ep);
                AnalyzeFunction(ep);
                LogMessage($"[Decomp] Analyzed entry point at 0x{ep:X8}.");
            }
            else
                LogMessage("[Decomp] Entry Point not available.");
        };
        panel.Children.Add(epBtn);

        
        var analyzeBtn = new Button
        {
            Content = "▶ Analyze Here",
            FontSize = 12,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A)),
        };
        analyzeBtn.Click += (_, _) =>
        {
            long offset = HexView.SelectedOffset;
            if (offset >= 0 && offset < HexView.FileLength)
            {
                _decompDisasmView?.ScrollToOffset(offset);
                AnalyzeFunction(offset);
                LogMessage($"[Decomp] Analyzing function at 0x{offset:X8}.");
            }
        };
        panel.Children.Add(analyzeBtn);

        var rejectAiBtn = new Button
        {
            Content = "↩ Reject AI",
            FontSize = 12,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A)),
            Visibility = Visibility.Collapsed
        };
        
        var jumpAiBtn = new Button
        {
            Content = "🔍 Jump AI",
            FontSize = 12,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x89, 0xDC, 0xEB)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A)),
            Visibility = Visibility.Collapsed
        };

        rejectAiBtn.Click += (_, _) =>
        {
            _pseudocodeGen.ClearAiRenames();
            _aiRenameHistory.Clear();
            
            if (_currentFunctionOffset >= 0)
            {
                AnalyzeFunction(_currentFunctionOffset);
                LogMessage("[Decomp] All AI renames rejected and purged.");
            }
            
            rejectAiBtn.Visibility = Visibility.Collapsed;
            jumpAiBtn.Visibility = Visibility.Collapsed;
        };
        panel.Children.Add(rejectAiBtn);

        jumpAiBtn.Click += (_, _) =>
        {
            _decompTextView?.JumpNextAiChange();
        };
        panel.Children.Add(jumpAiBtn);

        var aiRefactorBtn = new Button
        {
            Content = "✨ AI Refactor",
            FontSize = 12,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xC2, 0xE7)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A)),
        };
        aiRefactorBtn.Click += async (_, _) =>
        {
            if (_currentFunctionOffset < 0 || _decompTextView == null) return;
            
            string apiKey = AiSecurityHelper.Decrypt(EuvaSettings.Default.AiApiKeyEncrypted);
            

            aiRefactorBtn.IsEnabled = false;
            aiRefactorBtn.Content = "⏳ AI Working...";
            try
            {
               
                var blocks = _pseudocodeGen.LastBlocks;
                if (blocks == null) return;

               
                string miniIr = AiContextGenerator.Generate(
                    blocks, 
                    _pseudocodeGen.ResolvedImports, 
                    _pseudocodeGen.ResolvedStrings,
                    _pseudocodeGen.Pipeline?.LastSignature);

               
                using var client = new AiClient();
                string response = await client.RequestRenamesAsync(
                    apiKey,
                    EuvaSettings.Default.AiCustomPrompt,
                    miniIr,
                    EuvaSettings.Default.AiBaseUrl,
                    EuvaSettings.Default.AiModelName);

              
                var currentState = _pseudocodeGen.UserRenames?.ToDictionary(k => k.Key, v => v.Value) ?? new();
                _aiRenameHistory.Push(currentState);

              
                var newRenames = new Dictionary<string, string>();
                AiResponseParser.Parse(response, newRenames);

                foreach (var kv in newRenames)
                {
                    _pseudocodeGen.ApplyRename(kv.Key, kv.Value, isAiGenerated: true);
                }

              
                AnalyzeFunction(_currentFunctionOffset);
                LogMessage($"[Decomp] AI Refactoring complete: {newRenames.Count} variables renamed.");
                rejectAiBtn.Visibility = Visibility.Visible;
                jumpAiBtn.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                LogMessage($"[Decomp] AI Refactoring failed: {ex.Message}");
                MessageBox.Show($"AI Request failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                aiRefactorBtn.IsEnabled = true;
                aiRefactorBtn.Content = "✨ AI Refactor";
            }
        };
        panel.Children.Add(aiRefactorBtn);

        var aiSettingsBtn = new Button
        {
            Content = "⚙ AI Settings",
            FontSize = 12,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xE2, 0xD5)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A)),
        };
        aiSettingsBtn.Click += (_, _) =>
        {
            new AiSettingsWindow { Owner = this }.ShowDialog();
        };
        panel.Children.Add(aiSettingsBtn);

        
        _pseudocodeGen.RenameApplied += (s, e) => 
        {
            if (e is VariableSymbol sym && sym.IsAiGenerated)
            {
                rejectAiBtn.Visibility = Visibility.Visible;
                jumpAiBtn.Visibility = Visibility.Visible;
            }
        };

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(2),
            Child = panel
        };
    }

    

    private void AnalyzeFunction(long fileOffset)
    {
        _currentFunctionOffset = fileOffset;
        if (_decompGraphView == null || HexView.FileLength == 0) return;

        try
        {
            unsafe
            {
                var mmf = HexView.GetMemoryMappedFile();
                if (mmf == null) return;
                using var acc = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

                byte* ptr = null;
                acc.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
                try
                {
                    
                    var length = HexView.FileLength - fileOffset;
                    if (length <= 0) return;

                    var maxLen = (int)Math.Min(16384, length);
                    if (maxLen <= 0) return;

                    var result = _decompEngine.BuildFunctionGraph(
                        ptr + fileOffset, maxLen, fileOffset, 64, _pseudocodeGen, ptr, HexView.FileLength);

                    Dispatcher.Invoke(() =>
                    {
                        _decompGraphView.SetGraphData(result);
                        _decompTextView?.SetGraphData(result);
                        LogMessage($"[Decomp] Graph: {result.Nodes.Length} blocks, {result.Edges.Length} edges.");
                    });
                }
                finally
                {
                    acc.SafeMemoryMappedViewHandle.ReleasePointer();
                }
            }
        }
        catch (Exception ex)
        {
            LogMessage($"[Decomp] Analysis failed: {ex.ToString()}");
        }
    }

    private void TryAutoAnalyzeEntryPoint()
    {
        if (_decompDisasmView == null) return;

        
        SetDecompPeInfo(_decompDisasmView);

        long ep = _decompDisasmView.EntryPointFileOffset;
        if (ep >= 0 && ep < HexView.FileLength)
        {
            _decompDisasmView.ScrollToOffset(ep);
            AnalyzeFunction(ep);
        }
        else
        {
            
            _decompDisasmView.ScrollToOffset(0);
        }
    }

    private void SetDecompPeInfo(DisassemblerHexView view)
    {
        if (_mapper?.RootStructure == null) return;
        try
        {
            var root = _mapper.RootStructure;
            var sectionsNode = root.FindByPath("Sections");
            long entryPointFileOffset = -1;
            var secList = new PeSectionInfo[sectionsNode?.Children.Count ?? 0];
            int secCount = 0;

            if (sectionsNode != null)
            {
                foreach (var secNode in sectionsNode.Children)
                {
                    long ptrRawData = secNode.Offset ?? 0;
                    long secSize = secNode.Size ?? 0;
                    uint virtualAddr = 0;
                    foreach (var field in secNode.Children)
                    {
                        if (field.Name == "VirtualAddress" && field.Value != null)
                        {
                            try { virtualAddr = Convert.ToUInt32(field.Value); } catch { }
                            break;
                        }
                    }
                    if (ptrRawData < 0 || ptrRawData >= HexView.FileLength) ptrRawData = 0;
                    if (secCount < secList.Length)
                    {
                        secList[secCount] = new PeSectionInfo(
                            secNode.Name, ptrRawData, secSize, virtualAddr);
                        secCount++;
                    }
                }
            }

            var optHeader = root.FindByPath("NT Headers", "Optional Header");
            if (optHeader != null)
            {
                foreach (var field in optHeader.Children)
                {
                    if (field.Name == "AddressOfEntryPoint" && field.Value != null)
                    {
                        uint epRva = Convert.ToUInt32(field.Value);
                        entryPointFileOffset = RvaToFileOffset(epRva, secList, secCount);
                        break;
                    }
                }
            }

            view.SetPeInfo(entryPointFileOffset, secList, secCount);
        }
        catch (Exception ex)
        {
            LogMessage($"[Decomp] PE info extraction failed: {ex.Message}");
        }
    }
    
    private void DecompDock_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.F5)
        {
            ToggleDecompTextMode();
            e.Handled = true;
        }
    }

    private void ToggleDecompTextMode()
    {
        if (_decompGraphView == null || _decompTextView == null) return;

        _textModeActive = !_textModeActive;

        if (_textModeActive)
        {
            _decompGraphView.Visibility = Visibility.Collapsed;
            _decompTextView.Visibility = Visibility.Visible;
            _decompTextView.RefreshView();
            if (_decompTabItem != null)
                _decompTabItem.Header = "Disasm + Pseudocode";
            LogMessage("[Decomp] Switched to TEXT mode (F5 to toggle back).");
        }
        else
        {
            _decompTextView.Visibility = Visibility.Collapsed;
            _decompGraphView.Visibility = Visibility.Visible;
            _decompGraphView.RefreshView();
            if (_decompTabItem != null)
                _decompTabItem.Header = "Disasm + Decompiler";
            LogMessage("[Decomp] Switched to GRAPH mode (F5 to toggle back).");
        }
    }


    private void DecompDisasmView_OffsetSelected(object? sender, long offset)
    {
        HexView.SelectedOffset = offset;
        HexView_OffsetSelected(HexView, offset);
    }

    private void DecompGraphView_BlockSelected(object? sender, long offset)
    {
        _decompDisasmView?.ScrollToOffset(offset);
        LogMessage($"[Decomp] Graph block selected at 0x{offset:X8}.");
    }

    private void RefreshDecompOnFileLoad()
    {
        if (_decompDisasmView == null) return;

        var mmf = HexView.GetMemoryMappedFile();
        if (mmf != null)
        {
            var acc = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            _decompDisasmView.SetDataSource(mmf, acc, HexView.FileLength);
        }
        SetDecompPeInfo(_decompDisasmView);
        _decompDisasmView.RefreshView();

        if (_decompGraphView != null && mmf != null)
        {
            var acc2 = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            _decompGraphView.SetDataSource(mmf, acc2, HexView.FileLength);
        }

        TryAutoAnalyzeEntryPoint();
    }
}
