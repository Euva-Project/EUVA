// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO.MemoryMappedFiles;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EUVA.UI.Controls;
using EUVA.UI.Controls.Hex;
using static EUVA.UI.Controls.Hex.DisassemblerHexView;

namespace EUVA.UI;

public partial class MainWindow
{
    private TabItem? _disasmTabItem;
    private DisassemblerHexView? _disasmView;
    private TabControl? _centerTabControl;
    private bool _suppressDisasmSync;
    private ComboBox? _disasmSectionCombo;


    private void MenuDisassembler_Click(object sender, RoutedEventArgs e)
    {
        if (HexView.FileLength == 0)
        {
            MessageBox.Show("Please load a PE file first.", "No File",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        EnsureCenterTabControl();

        if (_disasmTabItem != null)
        {
            
            _centerTabControl!.SelectedItem = _disasmTabItem;
            _disasmView!.ScrollToOffset(HexView.CurrentScrollLine * HexView.BytesPerLine);
            LogMessage("[Disasm] Switched to Hex+Disasm tab.");
            return;
        }

        
        _disasmView = new DisassemblerHexView();

        
        var mmf = HexView.GetMemoryMappedFile();
        if (mmf != null)
        {
            var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            _disasmView.SetDataSource(mmf, accessor, HexView.FileLength);
        }

        
        _disasmView.OffsetSelected += DisasmView_OffsetSelected;

        
        ExtractAndSetPeInfo();

        
        var dock = new DockPanel();

        var toolbar = BuildDisasmToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        dock.Children.Add(toolbar);
        dock.Children.Add(_disasmView); 

        
        _disasmTabItem = new TabItem
        {
            Header = "Hex + Disasm",
            Content = dock
        };

        
        _disasmView.ScrollToOffset(HexView.CurrentScrollLine * HexView.BytesPerLine);

        _centerTabControl!.Items.Add(_disasmTabItem);
        _centerTabControl.SelectedItem = _disasmTabItem;
        LogMessage("[Disasm] Disassembler tab created.");
    }

    private Border BuildDisasmToolbar()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(4, 2, 4, 2)
        };

        
        var secLabel = new TextBlock
        {
            Text = "Section:",
            Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0x9C, 0xB0)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 12
        };
        panel.Children.Add(secLabel);

        
        _disasmSectionCombo = new ComboBox
        {
            Width = 200,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Consolas"),
        };
        _disasmSectionCombo.SelectionChanged += DisasmSectionCombo_Changed;
        panel.Children.Add(_disasmSectionCombo);

        
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
            if (_disasmView == null) return;
            long ep = _disasmView.EntryPointFileOffset;
            if (ep >= 0 && ep < _disasmView.FileLength)
            {
                _disasmView.ScrollToOffset(ep);
                LogMessage($"[Disasm] Jumped to Entry Point at file offset 0x{ep:X8}.");
            }
            else
                LogMessage("[Disasm] Entry Point not available or out of range.");
        };
        panel.Children.Add(epBtn);

        
        var homeBtn = new Button
        {
            Content = "⌂ File Start",
            FontSize = 12,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A)),
        };
        homeBtn.Click += (_, _) => _disasmView?.ScrollToOffset(0);
        panel.Children.Add(homeBtn);

        PopulateDisasmSections();

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(2),
            Child = panel
        };
    }


    private void PopulateDisasmSections()
    {
        if (_disasmSectionCombo == null || _disasmView == null) return;
        _disasmSectionCombo.Items.Clear();

        var sections = _disasmView.PeSections;
        for (int i = 0; i < sections.Length; i++)
        {
            ref readonly var sec = ref _disasmView.PeSections[i];
            _disasmSectionCombo.Items.Add(new ComboBoxItem
            {
                Content = $"{sec.Name}  (0x{sec.FileOffset:X}  |  {sec.Size:N0} bytes)",
                Tag = sec.FileOffset
            });
        }

        if (_disasmSectionCombo.Items.Count > 0)
            _disasmSectionCombo.SelectedIndex = 0;
    }

    private void DisasmSectionCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_disasmView == null || _disasmSectionCombo == null) return;
        if (_disasmSectionCombo.SelectedItem is ComboBoxItem item && item.Tag is long offset)
        {
            if (offset >= 0 && offset < _disasmView.FileLength)
            {
                _disasmView.ScrollToOffset(offset);
                LogMessage($"[Disasm] Jumped to section at file offset 0x{offset:X8}.");
            }
        }
    }


    private void ExtractAndSetPeInfo()
    {
        if (_disasmView == null || _mapper?.RootStructure == null) return;

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

                    
                    if (ptrRawData < 0 || ptrRawData >= HexView.FileLength)
                        ptrRawData = 0;

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

            _disasmView.SetPeInfo(entryPointFileOffset, secList, secCount);

            if (entryPointFileOffset >= 0)
                LogMessage($"[Disasm] Entry Point: RVA → file offset 0x{entryPointFileOffset:X8}");
            LogMessage($"[Disasm] {secCount} sections loaded.");
        }
        catch (Exception ex)
        {
            LogMessage($"[Disasm] PE info extraction failed (non-fatal): {ex.Message}");
        }
    }


    private static long RvaToFileOffset(uint rva, PeSectionInfo[] sections, int count)
    {
        for (int i = 0; i < count; i++)
        {
            ref readonly var sec = ref sections[i];
            
            if (rva >= sec.VirtualAddress && rva < sec.VirtualAddress + sec.Size)
                return rva - sec.VirtualAddress + sec.FileOffset;
        }
        
        return rva < 0x1000 ? rva : -1;
    }

    private void EnsureCenterTabControl()
    {
        if (_centerTabControl != null) return;

        var rootGrid = (Grid)Content;
        Grid? mainGrid = null;
        foreach (UIElement child in rootGrid.Children)
        {
            if (Grid.GetRow(child) == 2 && child is Grid g)
            {
                mainGrid = g;
                break;
            }
        }
        if (mainGrid == null) return;

        Border hexBorder = null!;
        foreach (UIElement child in mainGrid.Children)
        {
            if (Grid.GetColumn(child) == 2 && child is Border b)
            {
                hexBorder = b;
                break;
            }
        }
        if (hexBorder == null) return;

        var hexContent = hexBorder.Child;
        hexBorder.Child = null;

        _centerTabControl = new TabControl
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0)
        };

        _centerTabControl.SelectionChanged += (s, e) =>
        {
            if (e.Source != _centerTabControl) return;
            if (_centerTabControl.SelectedItem is TabItem ti)
            {
                string header = ti.Header?.ToString() ?? "";
                bool isSpecial = header.Contains("Decompiler") || header.Contains("Disasm");
                ToggleRightPanel(!isSpecial);
            }
        };

        var hexTab = new TabItem
        {
            Header = "Hex Editor",
            Content = hexContent
        };
        _centerTabControl.Items.Add(hexTab);
        _centerTabControl.SelectedIndex = 0;

        hexBorder.Child = _centerTabControl;
    }


    private void DisasmView_OffsetSelected(object? sender, long offset)
    {
        if (_suppressDisasmSync) return;
        _suppressDisasmSync = true;
        try
        {
            HexView.SelectedOffset = offset;
            HexView_OffsetSelected(HexView, offset);
        }
        finally { _suppressDisasmSync = false; }
    }

    private void RefreshDisasmOnFileLoad()
    {
        if (_disasmView == null) return;

        var mmf = HexView.GetMemoryMappedFile();
        if (mmf != null)
        {
            var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            _disasmView.SetDataSource(mmf, accessor, HexView.FileLength);
        }
        ExtractAndSetPeInfo();
        PopulateDisasmSections();
        _disasmView.RefreshView();
    }
}
