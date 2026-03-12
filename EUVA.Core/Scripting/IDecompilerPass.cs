// SPDX-License-Identifier: GPL-3.0-or-later

namespace EUVA.Core.Scripting;

/// <summary>
/// Implement this interface in your scripts to hook into the EUVA recompilation pipeline.
/// </summary>
public interface IDecompilerPass
{
    /// <summary>
    /// Specifies when this pass should be executed in the decompilation pipeline.
    /// </summary>
    PassStage Stage { get; }

    /// <summary>
    /// Called when the engine hits the specified PassStage for the current function.
    /// </summary>
    /// <param name="context">The state of the decompilation for the current function.</param>
    void Execute(DecompilerContext context);
}
