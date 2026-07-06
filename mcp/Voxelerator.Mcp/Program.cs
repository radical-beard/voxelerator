using Voxelerator.Core;

namespace Voxelerator.Mcp;

// voxelerator-mcp: stdio MCP server over the Voxelerator core library.
// Newline-delimited JSON-RPC 2.0; all logging goes to stderr so stdout stays
// a clean protocol stream. Register in Claude Code with:
//   claude mcp add voxelerator -- voxelerator-mcp
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Contains("--version"))
        {
            Console.WriteLine($"{McpServer.ServerName} {McpServer.ServerVersion}");
            return 0;
        }

        Registry.SeedBuiltinPalettes();
        Console.Error.WriteLine($"[voxelerator-mcp] ready (data: {Registry.DataDir})");

        var server = new McpServer();
        string? line;
        while ((line = Console.In.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            string? resp;
            try { resp = server.HandleLine(line); }
            catch (Exception e)
            {
                Console.Error.WriteLine($"[voxelerator-mcp] unhandled: {e}");
                continue;
            }
            if (resp is not null)
            {
                Console.Out.WriteLine(resp);
                Console.Out.Flush();
            }
        }
        return 0;
    }
}
