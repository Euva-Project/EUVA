// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading.Tasks;

namespace EUVA.Core.Robots;

public enum AdminDecision
{
    InheritData,
    Ignore
}

public readonly struct AdminResponse
{
    public AdminDecision Decision { get; }
    public byte[]? Payload { get; }

    public AdminResponse(AdminDecision decision, byte[]? payload = null)
    {
        Decision = decision;
        Payload = payload;
    }
}

public interface IProcessAdmin
{
    Task<AdminResponse> OnRobotErrorAsync(Guid robotId, RobotRole role, string missingKey);
    RobotVerifier Verifier { get; }
}
