"""Call the installed official Blender MCP over stdio (no direct bpy/socket bypass).

Usage: <Blender MCP venv python> BlenderMcp.py <tool> <arguments.json>
The interactive tool uses the already running Blender addon. CLI tools render
saved copies in a background process, leaving the interactive session available.
"""
import asyncio
import json
import os
import sys
from pathlib import Path

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


async def main():
    if len(sys.argv) > 2 and sys.argv[2].endswith(".py"):
        code_path = Path(sys.argv[2]).resolve()
        arguments = {"code": "__file__ = " + repr(str(code_path)) + "\n" + code_path.read_text(encoding="utf-8-sig")}
        if len(sys.argv) > 3:
            arguments["blend_file"] = sys.argv[3]
    else:
        arguments = json.loads(Path(sys.argv[2]).read_text(encoding="utf-8-sig")) if len(sys.argv) > 2 else {}
    server = StdioServerParameters(command=sys.executable, args=["-m", "blmcp"], env=dict(os.environ))
    async with stdio_client(server) as (reader, writer):
        async with ClientSession(reader, writer) as session:
            await session.initialize()
            if sys.argv[1] == "list":
                value = await session.list_tools()
            else:
                value = await session.call_tool(sys.argv[1], arguments)
            print(json.dumps(value.structuredContent, indent=2) if getattr(value, "structuredContent", None) else value.model_dump_json(indent=2))
            if getattr(value, "isError", False) or (getattr(value, "structuredContent", None) or {}).get("status") == "error":
                raise RuntimeError("Blender MCP tool failed")


if __name__ == "__main__":
    asyncio.run(main())
