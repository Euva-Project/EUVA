# Nonlinear Decompiler

The Nonlinear Decompiler is not a traditional, rigid translation pipeline. Instead, it is a decentralized system of micro-algorithms. We removed the hardcoded logic of how code *should* look and replaced it with a flexible ecosystem where you dictate the rules. 

You pour in your own principles, change the signatures, and get the exact output you want. Create, learn, sculpt that's all.

## Open Signatures & Rules

Every transformation the decompiler makes is based on open rules. These signatures and regex patterns live plainly in the `EUVA.Core/Robots/Patterns/` or related `Rules/` directories. 

You are not locked into our vision of C++. If you don't like how a macro is reconstructed or how a pointer is cast, just open the rule file, change the Regex, and the decompiler will permanently adapt to your style.

## The Fleet Hierarchy

To keep this decentralized system fast and stable, it operates under a strict three-tier hierarchy:

1. **Admin (`ProcessAdmin.cs`)**  
   The orchestrator of the fleet. The Admin takes the initial linear assembly dump, creates the working environment, and signals all the robots to start investigating simultaneously.

2. **Robots (`DecompilerRobot.cs`)**  
   Small, hyper-focused workers. Rather than one massive algorithm trying to understand the whole function, we use specialized robots. One robot only looks for `if/else` structures. Another only renames WinAPI calls. You can create your own robot, configure it based on existing ones, and plug it into the Admin to extract any custom logic you need.

3. **Verifier (`RobotVerifier.cs`)**  
   The bouncer. Because robots work in parallel and can be written by anyone (including you), the system must defend itself against bad logic. Every robot must sign its changes with a cryptographic-like Verification Key. The Verifier checks all incoming changes. If a robot violates the rules or returns a bad key, the Admin rejects its work. The algorithm strictly follows the rules.

## Building Your Own

Want to extract custom cryptographic structures or simplify an decompiled code?

1. Write a simple regex rule.
2. Clone a basic Robot to apply that rule.
3. Register it in `ProcessAdmin.cs`.
4. Run the decompiler.

No massive APIs to learn. Just micro-algorithms collaborating on a single text file.

P.S 
In fact, this is just a wrapper for the main decompiler that creates. It is important to understand that this non-linear decompiler is just cosmetics for the main linear decompiler. The non-linear decompiler was created to simplify and humanize the decompiled code as much as possible and make it easier to understand due to regex rules. If you need more extensive changes, you may need to write C# scripts in Roslyn, taking into account our decompiler SDK.