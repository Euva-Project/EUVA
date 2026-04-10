// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;

namespace EUVA.Core.Robots;

public sealed class RobotResult
{
    public Guid RobotId { get; init; }

    public RobotRole Role { get; init; }

    public bool HasFindings { get; init; }

    public string Summary { get; init; } = string.Empty;

    public int AnnotationCount { get; init; }

    public DateTime CompletedAt { get; init; } = DateTime.UtcNow;

    public double Confidence { get; init; } = 1.0;

    public byte[]? VerificationKey { get; init; }

    public override string ToString() =>
        $"[Result robot={RobotId:N} role={Role} findings={HasFindings} annotations={AnnotationCount} confidence={Confidence:P0}]";
}
