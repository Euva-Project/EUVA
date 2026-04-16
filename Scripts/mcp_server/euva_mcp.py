import sys
import os
import glob
import json
import re
import argparse
from typing import List, Dict, Any, Optional
from mcp.server.fastmcp import FastMCP

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
DEFAULT_WORKSPACE = os.path.join(SCRIPT_DIR, "..", "..", "EUVA.UI", "bin", "Debug", "net8.0-windows")
parser = argparse.ArgumentParser(description="EUVA Python MCP Server")
parser.add_argument("--workspace", type=str, default=DEFAULT_WORKSPACE,
                    help="Path to the EUVA workspace (directory containing 'Dumps' and 'Robots')")
args, _ = parser.parse_known_args()
WORKSPACE_DIR = os.path.abspath(args.workspace)
DUMPS_DIR = os.path.join(WORKSPACE_DIR, "Dumps")

mcp = FastMCP("EUVA", dependencies=["pydantic"])

def _read_dump_lines(dump_filename: str):
    path = os.path.join(DUMPS_DIR, dump_filename)
    if not os.path.exists(path):
        return None, path
    with open(path, "r", encoding="utf-8") as f:
        return f.readlines(), path

def _write_dump(path: str, lines: list):
    with open(path, "w", encoding="utf-8") as f:
        f.writelines(lines)

def _rename_in_text(content: str, old: str, new: str):
    pattern = r'\b' + re.escape(old) + r'\b'
    count = len(re.findall(pattern, content))
    if count > 0:
        content = re.sub(pattern, new, content)
        content = _tag_ai_lines(content, old, new)
        return content, count
    count = content.count(old)
    if count > 0:
        content = content.replace(old, new)
        content = _tag_ai_lines(content, old, new)
        return content, count
    return content, 0

def _tag_ai_line(line: str) -> str:
    stripped = line.rstrip('\n').rstrip()
    if '// AI' in stripped:
        return line
    return stripped + ' // AI\n'

def _tag_ai_lines(content: str, old: str, new: str) -> str:
    out = []
    for line in content.splitlines(True):
        if new in line and '// AI' not in line:
            out.append(_tag_ai_line(line))
        else:
            out.append(line)
    return "".join(out)

def _rename_in_lines(lines: list, old: str, new: str, start: int, end: int):
    pattern = re.compile(r'\b' + re.escape(old) + r'\b')
    count = 0
    for i in range(start, end):
        matches = len(pattern.findall(lines[i]))
        if matches > 0:
            lines[i] = pattern.sub(new, lines[i])
            lines[i] = _tag_ai_line(lines[i])
            count += matches
    if count == 0:
        for i in range(start, end):
            c = lines[i].count(old)
            if c > 0:
                lines[i] = lines[i].replace(old, new)
                lines[i] = _tag_ai_line(lines[i])
                count += c
    return lines, count

def _apply_action(lines: list, action: str, target: str, context: str):
    act = action.upper()

    try:
        if act == "COMMENT":
            idx = int(target) - 1
            if not (0 <= idx < len(lines)):
                return lines, f"Error: Line {target} out of range (1..{len(lines)})."
            lines[idx] = lines[idx].rstrip() + f" // [MCP] {context}\n"
            return lines, f"OK: Added comment at line {target}."

        elif act == "REMOVE_COMMENT":
            idx = int(target) - 1
            if not (0 <= idx < len(lines)):
                return lines, f"Error: Line {target} out of range."
            lines[idx] = re.sub(r'\s*//\s*\[MCP\].*$', '', lines[idx]).rstrip() + "\n"
            return lines, f"OK: Removed MCP comment from line {target}."

        elif act == "EDIT_COMMENT":
            idx = int(target) - 1
            if not (0 <= idx < len(lines)):
                return lines, f"Error: Line {target} out of range."
            if "// [MCP]" in lines[idx]:
                lines[idx] = re.sub(r'//\s*\[MCP\].*$', f'// [MCP] {context}', lines[idx])
            else:
                lines[idx] = lines[idx].rstrip() + f" // [MCP] {context}\n"
            return lines, f"OK: Updated MCP comment at line {target}."

        elif act == "RENAME":
            content = "".join(lines)
            content, count = _rename_in_text(content, target, context)
            if count == 0:
                return lines, f"WARN: '{target}' not found in file. Nothing renamed."
            new_lines = content.splitlines(True)
            return new_lines, f"OK: Renamed '{target}' → '{context}' ({count} occurrences)."

        elif act == "RENAME_SCOPED":
            parts = target.rsplit(":", 1)
            if len(parts) != 2 or "-" not in parts[1]:
                return lines, "Error: RENAME_SCOPED target format: 'old_name:start_line-end_line'"
            old_name = parts[0]
            try:
                start, end = parts[1].split("-", 1)
                start_idx = int(start) - 1
                end_idx = int(end)
            except ValueError:
                return lines, "Error: Invalid line range in RENAME_SCOPED target."
            if start_idx < 0 or end_idx > len(lines) or start_idx >= end_idx:
                return lines, f"Error: Line range {start}-{end} out of bounds (1..{len(lines)})."
            lines, count = _rename_in_lines(lines, old_name, context, start_idx, end_idx)
            if count == 0:
                return lines, f"WARN: '{old_name}' not found in lines {start}-{end}. Nothing renamed."
            return lines, f"OK: Renamed '{old_name}' → '{context}' in lines {start}-{end} ({count} occurrences)."

        elif act == "REPLACE_LINE":
            idx = int(target) - 1
            if not (0 <= idx < len(lines)):
                return lines, f"Error: Line {target} out of range (1..{len(lines)})."
            old_content = lines[idx].strip()
            lines[idx] = context.rstrip() + "\n"
            return lines, f"OK: Replaced line {target}: '{old_content[:60]}' → '{context[:60]}'"

        elif act == "RENAME_LABEL":
            content = "".join(lines)
            content, count = _rename_in_text(content, target, context)
            if count == 0:
                return lines, f"WARN: Label '{target}' not found. Nothing renamed."
            new_lines = content.splitlines(True)
            return new_lines, f"OK: Renamed label '{target}' → '{context}' ({count} occurrences)."

        elif act == "INSERT_LINE":
            idx = int(target) - 1
            if not (0 <= idx <= len(lines)):
                return lines, f"Error: Line {target} out of range."
            lines.insert(idx, context.rstrip() + "\n")
            return lines, f"OK: Inserted line before position {target}."

        elif act == "DELETE_LINE":
            idx = int(target) - 1
            if not (0 <= idx < len(lines)):
                return lines, f"Error: Line {target} out of range."
            removed = lines.pop(idx).strip()
            return lines, f"OK: Deleted line {target}: '{removed[:60]}'"

        else:
            return lines, f"Error: Unknown action '{action}'. Use: COMMENT, REMOVE_COMMENT, EDIT_COMMENT, RENAME, RENAME_SCOPED, REPLACE_LINE, RENAME_LABEL, INSERT_LINE, DELETE_LINE."

    except ValueError:
        return lines, f"Error: 'target' must be a line number for action '{action}'."
    except Exception as e:
        return lines, f"Error: {e}"

@mcp.tool()
def list_dumps() -> List[str]:
    if not os.path.exists(DUMPS_DIR):
        return []
    dumps = glob.glob(os.path.join(DUMPS_DIR, "*.dump"))
    return [os.path.basename(d) for d in dumps]

@mcp.tool()
def read_dump(filename: str) -> Dict[str, Any]:
    lines, path = _read_dump_lines(filename)
    if lines is None:
        return {"Error": f"Dump file {filename} not found."}
    address = filename.replace("func_", "").replace(".dump", "")
    return {
        "Address": address,
        "File": filename,
        "LineCount": len(lines),
        "Content": "".join(lines)
    }

@mcp.tool()
def modify_dump(dump_filename: str, action: str, target: str, context: str) -> str:
    lines, path = _read_dump_lines(dump_filename)
    if lines is None:
        return f"Error: Dump file {dump_filename} not found."
    lines, msg = _apply_action(lines, action, target, context)
    if not msg.startswith("Error") and not msg.startswith("WARN"):
        _write_dump(path, lines)
    elif msg.startswith("WARN"):
        pass
    return msg

@mcp.tool()
def batch_modify(dump_filename: str, operations: str) -> List[str]:
    lines, path = _read_dump_lines(dump_filename)
    if lines is None:
        return [f"Error: File {dump_filename} not found."]

    try:
        ops = json.loads(operations)
    except json.JSONDecodeError as e:
        return [f"Error: Invalid JSON: {e}"]

    if not isinstance(ops, list):
        return ["Error: 'operations' must be a JSON array of {action, target, context} objects."]

    results = []
    for i, op in enumerate(ops):
        action = op.get("action", "")
        target = op.get("target", "")
        context = op.get("context", "")
        lines, msg = _apply_action(lines, action, target, context)
        results.append(f"[{i+1}] {msg}")

    _write_dump(path, lines)
    return results

@mcp.tool()
def search_pattern(dump_filename: str, pattern: str, is_regex: bool = False) -> List[Dict[str, Any]]:
    lines, path = _read_dump_lines(dump_filename)
    if lines is None:
        return [{"Error": f"File {dump_filename} not found."}]

    results = []
    for i, line in enumerate(lines):
        try:
            match = re.search(pattern, line) if is_regex else (pattern in line)
        except re.error as e:
            return [{"Error": f"Invalid regex: {e}"}]
        if match:
            results.append({"Line": i + 1, "Text": line.strip()})
    return results

@mcp.tool()
def batch_search(dump_filename: str, patterns: str, is_regex: bool = False) -> Dict[str, List[Dict[str, Any]]]:
    lines, path = _read_dump_lines(dump_filename)
    if lines is None:
        return {"Error": f"File {dump_filename} not found."}

    try:
        pattern_list = json.loads(patterns)
    except json.JSONDecodeError as e:
        return {"Error": f"Invalid JSON: {e}"}

    if not isinstance(pattern_list, list):
        return {"Error": "'patterns' must be a JSON array of strings."}

    all_results = {}
    for pat in pattern_list:
        matches = []
        for i, line in enumerate(lines):
            try:
                found = re.search(pat, line) if is_regex else (pat in line)
            except re.error as e:
                matches.append({"Error": f"Invalid regex: {e}"})
                break
            if found:
                matches.append({"Line": i + 1, "Text": line.strip()})
        all_results[pat] = matches
    return all_results

@mcp.tool()
def get_function_summary(dump_filename: str) -> Dict[str, Any]:
    lines, path = _read_dump_lines(dump_filename)
    if lines is None:
        return {"Error": f"File {dump_filename} not found."}

    content = "".join(lines)
    api_calls = re.findall(r'\b([A-Z][a-zA-Z]+(?:Ex)?[AW]?)\s*\(', content)
    strings = re.findall(r'"([^"]*)"', content)
    locals_vars = set(re.findall(r'\b(spill_\w+|var_\w+|arg_\w+)\b', content))
    ifs = len(re.findall(r'\bif\s*\(', content))
    whiles = len(re.findall(r'\bwhile\s*\(', content))
    fors = len(re.findall(r'\bfor\s*\(', content))
    mcp_comments = [l.strip() for l in lines if "// [MCP]" in l]

    return {
        "File": dump_filename,
        "TotalLines": len(lines),
        "ApiCalls": list(set(api_calls)),
        "StringLiterals": strings[:20],
        "LocalVariables": sorted(locals_vars),
        "ControlFlow": {"if": ifs, "while": whiles, "for": fors},
        "McpAnnotations": mcp_comments
    }

@mcp.tool()
def xref_symbol(symbol: str) -> List[Dict[str, Any]]:
    if not os.path.exists(DUMPS_DIR):
        return [{"Error": "Dumps directory not found."}]

    results = []
    for dump_file in glob.glob(os.path.join(DUMPS_DIR, "*.dump")):
        fname = os.path.basename(dump_file)
        with open(dump_file, "r", encoding="utf-8") as f:
            for i, line in enumerate(f, 1):
                if re.search(r'\b' + re.escape(symbol) + r'\b', line):
                    results.append({"File": fname, "Line": i, "Text": line.strip()})
    return results

@mcp.tool()
def batch_rename(dump_filename: str, renames: str, start_line: int = 0, end_line: int = 0) -> str:
    lines, path = _read_dump_lines(dump_filename)
    if lines is None:
        return f"Error: File {dump_filename} not found."

    try:
        rename_map = json.loads(renames)
    except json.JSONDecodeError as e:
        return f"Error: Invalid JSON in renames: {e}"

    scoped = start_line > 0 and end_line > 0
    total = 0

    if scoped:
        s = max(0, start_line - 1)
        e = min(len(lines), end_line)
        for old, new in rename_map.items():
            lines, count = _rename_in_lines(lines, old, new, s, e)
            total += count
        _write_dump(path, lines)
        if total == 0:
            return f"WARN: No matches found for any of {len(rename_map)} patterns in lines {start_line}-{end_line}."
        return f"OK: Applied {len(rename_map)} scoped renames in lines {start_line}-{end_line} ({total} total replacements)."
    else:
        content = "".join(lines)
        for old, new in rename_map.items():
            content, count = _rename_in_text(content, old, new)
            total += count
        with open(path, "w", encoding="utf-8") as f:
            f.write(content)
        if total == 0:
            return f"WARN: No matches found for any of {len(rename_map)} patterns."
        return f"OK: Applied {len(rename_map)} renames ({total} total replacements)."

if __name__ == "__main__":
    mcp.run()
